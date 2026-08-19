using System;
using System.Collections.Generic;
using UnityEngine;
using Votanic.vNet.Networking;
using Votanic.vXR.vGear.Networking;

/// <summary>
/// Point-capture match flow: 3-minute score race, or wipeout when a side holds no villages.
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public class PointCaptureMatch : MonoBehaviour
{
    public enum MatchState
    {
        Waiting,
        Countdown,
        Playing,
        Ended
    }

    public static PointCaptureMatch Instance { get; private set; }

    [Header("References")]
    [SerializeField] private PointCaptureBoard board;
    [SerializeField] private PointCaptureSpawner spawner;
    [SerializeField] private RtsCampManager campManager;

    [Header("Timing")]
    [SerializeField, Min(10f)] private float matchDurationSeconds = 180f;
    [Tooltip("Unused. Both sides must ready before the match starts.")]
    [SerializeField] private bool autoStartOnPlay = false;
    [SerializeField, Min(0f)] private float countdownSeconds = 3f;

    [Header("Scoring")]
    [SerializeField, Min(0f)] private float scorePerVillagePerSecond = 1f;

    [Header("Manpower")]
    [SerializeField, Min(0f)] private float startingManpower = 40f;
    [SerializeField, Min(0f)] private float baseManpowerPerTenSeconds = 5f;
    [SerializeField, Min(0f)] private float manpowerPerVillagePerTenSeconds = 2f;
    [SerializeField, Min(0f)] private float infantryManpowerCost = 25f;
    [SerializeField, Min(0f)] private float archerManpowerCost = 35f;
    [SerializeField, Min(1f)] private float manpowerIncomeIntervalSeconds = 10f;

    [Header("Army Economy")]
    [SerializeField] private PointCaptureArmyEconomy armyEconomy;

    [Header("Opening Forces")]
    [SerializeField] private bool spawnStartingRegiments = true;

    [Header("Debug")]
    [SerializeField] private bool logMatchEvents = true;

    private const string ReadyMessagePrefix = "PC|";
    private const float ReadyAnnounceIntervalSeconds = 0.75f;

    private MatchState currentState = MatchState.Waiting;
    private float stateElapsed;
    private float redScore;
    private float yellowScore;
    private float redManpower;
    private float yellowManpower;
    private CaptureOwner winner = CaptureOwner.Neutral;
    private string resultMessage = string.Empty;
    private float manpowerIntervalElapsed;
    private bool yellowReady;
    private bool redReady;
    private bool yellowResetRequested;
    private bool redResetRequested;
    private vGear_Networking networking;
    private NetworkManager.OnReceived previousReceivedHandler;
    private string localInstanceToken = string.Empty;
    private float nextReadyAnnounceTime;
    private bool networkingBound;

    public MatchState CurrentState => currentState;
    public bool IsPlaying => currentState == MatchState.Playing;
    public bool IsYellowReady => yellowReady;
    public bool IsRedReady => redReady;
    public bool AreBothSidesReady => yellowReady && redReady;
    public bool IsYellowResetRequested => yellowResetRequested;
    public bool IsRedResetRequested => redResetRequested;
    public bool AreBothSidesResetRequested => yellowResetRequested && redResetRequested;
    public float MatchDurationSeconds => matchDurationSeconds;
    public float RemainingSeconds =>
        currentState == MatchState.Playing
            ? Mathf.Max(0f, matchDurationSeconds - stateElapsed)
            : currentState == MatchState.Countdown
                ? Mathf.Max(0f, countdownSeconds - stateElapsed)
                : matchDurationSeconds;
    public float RedScore => redScore;
    public float YellowScore => yellowScore;
    public float RedManpower => redManpower;
    public float YellowManpower => yellowManpower;
    public float InfantryManpowerCost => infantryManpowerCost;
    public float ArcherManpowerCost => archerManpowerCost;
    public CaptureOwner Winner => winner;
    public string ResultMessage => resultMessage;
    public PointCaptureBoard Board => board;
    public PointCaptureSpawner Spawner => spawner;
    public string LocalNetworkToken => localInstanceToken;
    public bool IsNetworkHost
    {
        get
        {
            if (networking == null)
            {
                return true;
            }

            try
            {
                return networking.type == UserType.Host;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    public event Action<MatchState> StateChanged;

    public void SendNetworkMessage(string message)
    {
        SendNetwork(message);
    }

#if UNITY_EDITOR
    [ContextMenu("Point Capture/Generate Placeholder Scene Setup")]
    private void EditorGeneratePlaceholders()
    {
        UnityEditor.EditorApplication.ExecuteMenuItem("Tools/Point Capture/Generate Placeholder Scene Setup");
    }
#endif

    private void Awake()
    {
        Instance = this;
        localInstanceToken = Guid.NewGuid().ToString("N").Substring(0, 8);
        if (board == null)
        {
            board = GetComponent<PointCaptureBoard>();
        }

        if (spawner == null)
        {
            spawner = GetComponent<PointCaptureSpawner>();
        }

        if (campManager == null)
        {
            campManager = GetComponent<RtsCampManager>();
        }

        if (armyEconomy == null)
        {
            armyEconomy = GetComponent<PointCaptureArmyEconomy>();
        }

        if (armyEconomy == null)
        {
            armyEconomy = gameObject.AddComponent<PointCaptureArmyEconomy>();
        }

        networking = FindObjectOfType<vGear_Networking>();
        BindNetworking();
        if (GetComponent<PointCaptureNetworkSession>() == null)
        {
            gameObject.AddComponent<PointCaptureNetworkSession>();
        }

        if (SiegePlayEnvironment.Instance == null)
        {
            gameObject.AddComponent<SiegePlayEnvironment>();
        }
    }

    private void Start()
    {
        if (board != null)
        {
            board.BindVillages();
            BindHomeCamps();
        }

        ResetMatch(startImmediately: false);
    }

    private void OnDestroy()
    {
        UnbindNetworking();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (currentState == MatchState.Waiting || currentState == MatchState.Ended)
        {
            if (networking == null)
            {
                networking = FindObjectOfType<vGear_Networking>();
            }

            if (!networkingBound)
            {
                BindNetworking();
            }

            if (currentState == MatchState.Waiting)
            {
                TryAnnounceReady();
            }
            else
            {
                TryAnnounceReset();
            }
        }

        switch (currentState)
        {
            case MatchState.Countdown:
                stateElapsed += Time.deltaTime;
                if (stateElapsed >= countdownSeconds)
                {
                    BeginPlay();
                }

                break;
            case MatchState.Playing:
                TickPlay(Time.deltaTime);
                break;
        }
    }

    public void Configure(
        PointCaptureBoard captureBoard,
        PointCaptureSpawner captureSpawner,
        RtsCampManager camps)
    {
        board = captureBoard;
        spawner = captureSpawner;
        campManager = camps;
    }

    public void BeginCountdown()
    {
        ResetMatch(startImmediately: false);
        SetState(MatchState.Countdown);
        Log("Countdown started.");
    }

    public void BeginPlay()
    {
        if (board != null)
        {
            board.BindVillages();
            BindHomeCamps();
        }

        if (spawnStartingRegiments && spawner != null)
        {
            spawner.ClearSpawnedUnits();
            spawner.SpawnStartingForces();
        }

        SetState(MatchState.Playing);
        Log("Point Capture match started.");
    }

    /// <summary>
    /// External lobby/session code can call this right after loading the scene
    /// to stop any auto countdown and keep the match in a ready/waiting state.
    /// </summary>
    public void PrepareForManualStart()
    {
        // ResetMatch(startImmediately:false) ensures the match state becomes Waiting
        // without entering Countdown.
        ResetMatch(startImmediately: false);
        SetState(MatchState.Waiting);
    }

    /// <summary>
    /// Starts the match immediately (no countdown).
    /// </summary>
    public void StartPlayingNow()
    {
        BeginPlay();
    }

    /// <summary>
    /// Multiplayer/session code can override duration so both peers use the same timer.
    /// </summary>
    public void SetMatchDurationSeconds(float seconds)
    {
        matchDurationSeconds = Mathf.Max(10f, seconds);
    }

    private void BindHomeCamps()
    {
        if (campManager == null || board == null)
        {
            return;
        }

        PointCaptureVillage yellowHome = board.GetHomeVillage(CaptureOwner.Yellow);
        PointCaptureVillage redHome = board.GetHomeVillage(CaptureOwner.Red);
        if (yellowHome == null || redHome == null)
        {
            return;
        }

        campManager.AssignCamps(yellowHome.transform, redHome.transform);
    }

    public void Restart()
    {
        if (spawner != null)
        {
            spawner.ClearSpawnedUnits();
        }

        ResetMatch(startImmediately: false);
    }

    public bool HasConnectedPeer
    {
        get
        {
            if (networking == null)
            {
                return false;
            }

            try
            {
                foreach (vGear_NetworkUser user in networking.GetAllNetworkUsers())
                {
                    if (user != null && user.userID != networking.networkID)
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
            }

            return false;
        }
    }

    public CaptureOwner NetworkLocalFaction
    {
        get
        {
            if (networking == null)
            {
                return CaptureOwner.Yellow;
            }

            try
            {
                return networking.type == UserType.Host ? CaptureOwner.Yellow : CaptureOwner.Red;
            }
            catch (Exception)
            {
                return CaptureOwner.Yellow;
            }
        }
    }

    public void NotifySideReady(CaptureOwner owner)
    {
        if (currentState != MatchState.Waiting || !CaptureTeams.IsPlayerSide(owner))
        {
            return;
        }

        if (owner == CaptureOwner.Yellow)
        {
            yellowReady = true;
        }
        else
        {
            redReady = true;
        }

        Log(CaptureTeams.GetDisplayName(owner) + " is ready.");
        BroadcastReadyState();
        TryBeginWhenBothReady();
    }

    public void NotifySideReset(CaptureOwner owner)
    {
        if (currentState != MatchState.Ended || !CaptureTeams.IsPlayerSide(owner))
        {
            return;
        }

        if (owner == CaptureOwner.Yellow)
        {
            yellowResetRequested = true;
        }
        else
        {
            redResetRequested = true;
        }

        Log(CaptureTeams.GetDisplayName(owner) + " requested reset.");
        BroadcastResetState();
        TryRestartWhenBothReset();
    }

    public float GetScore(CaptureOwner owner)
    {
        return owner == CaptureOwner.Yellow ? yellowScore : redScore;
    }

    public float GetManpower(CaptureOwner owner)
    {
        return owner == CaptureOwner.Yellow ? yellowManpower : redManpower;
    }

    public float GetRaiseCost(PointCaptureSpawner.RegimentType regimentType)
    {
        return regimentType == PointCaptureSpawner.RegimentType.Archer
            ? archerManpowerCost
            : infantryManpowerCost;
    }

    public bool TrySpendManpower(CaptureOwner owner, float cost)
    {
        if (!CaptureTeams.IsPlayerSide(owner) || cost <= 0f || !IsPlaying)
        {
            return false;
        }

        if (owner == CaptureOwner.Red)
        {
            if (redManpower < cost)
            {
                return false;
            }

            redManpower -= cost;
            return true;
        }

        if (yellowManpower < cost)
        {
            return false;
        }

        yellowManpower -= cost;
        return true;
    }

    public void DrainManpower(CaptureOwner owner, float amount)
    {
        if (!CaptureTeams.IsPlayerSide(owner) || amount <= 0f || !IsPlaying)
        {
            return;
        }

        if (owner == CaptureOwner.Red)
        {
            redManpower = Mathf.Max(0f, redManpower - amount);
            return;
        }

        yellowManpower = Mathf.Max(0f, yellowManpower - amount);
    }

    public void HandleTerritoryChanged()
    {
        TryEndIfSideHasNoVillages();
    }

    private void TickPlay(float deltaTime)
    {
        stateElapsed += deltaTime;
        int redVillages = board != null ? board.CountOwned(CaptureOwner.Red) : 0;
        int yellowVillages = board != null ? board.CountOwned(CaptureOwner.Yellow) : 0;

        redScore += scorePerVillagePerSecond * redVillages * deltaTime;
        yellowScore += scorePerVillagePerSecond * yellowVillages * deltaTime;

        manpowerIntervalElapsed += deltaTime;
        if (manpowerIntervalElapsed >= manpowerIncomeIntervalSeconds)
        {
            int intervals = Mathf.FloorToInt(manpowerIntervalElapsed / manpowerIncomeIntervalSeconds);
            manpowerIntervalElapsed -= intervals * manpowerIncomeIntervalSeconds;

            float redIncome = (baseManpowerPerTenSeconds + manpowerPerVillagePerTenSeconds * redVillages) * intervals;
            float yellowIncome = (baseManpowerPerTenSeconds + manpowerPerVillagePerTenSeconds * yellowVillages) * intervals;
            redManpower += redIncome;
            yellowManpower += yellowIncome;

            if (armyEconomy != null)
            {
                armyEconomy.TickIdleUpkeep(intervals);
            }
        }

        if (armyEconomy != null)
        {
            armyEconomy.TickRecovery(deltaTime);
        }

        if (TryEndIfSideHasNoVillages())
        {
            return;
        }

        if (stateElapsed >= matchDurationSeconds)
        {
            EndByScore();
        }
    }

    private bool TryEndIfSideHasNoVillages()
    {
        if (!IsPlaying || board == null)
        {
            return false;
        }

        int redVillages = board.CountOwned(CaptureOwner.Red);
        int yellowVillages = board.CountOwned(CaptureOwner.Yellow);
        if (redVillages == 0 && yellowVillages > 0)
        {
            EndMatch(CaptureOwner.Yellow, "Red has no villages left.");
            return true;
        }

        if (yellowVillages == 0 && redVillages > 0)
        {
            EndMatch(CaptureOwner.Red, "Yellow has no villages left.");
            return true;
        }

        return false;
    }

    private void EndByScore()
    {
        if (redScore > yellowScore)
        {
            EndMatch(CaptureOwner.Red, "Time is up. Red scored higher.");
            return;
        }

        if (yellowScore > redScore)
        {
            EndMatch(CaptureOwner.Yellow, "Time is up. Yellow scored higher.");
            return;
        }

        EndMatch(CaptureOwner.Neutral, "Time is up. The match is a draw.");
    }

    private void EndMatch(CaptureOwner matchWinner, string message)
    {
        if (currentState == MatchState.Ended)
        {
            return;
        }

        winner = matchWinner;
        resultMessage = message;
        yellowResetRequested = false;
        redResetRequested = false;
        nextReadyAnnounceTime = 0f;
        SetState(MatchState.Ended);
        Log(message + " Winner: " + CaptureTeams.GetDisplayName(matchWinner) + ".");
        PointCaptureNetworkSession.Instance?.NotifyMatchEnded(matchWinner, message);
    }

    public void ApplyNetworkEnd(CaptureOwner matchWinner, string message)
    {
        if (currentState == MatchState.Ended)
        {
            return;
        }

        winner = matchWinner;
        resultMessage = string.IsNullOrEmpty(message) ? "Match ended." : message;
        yellowResetRequested = false;
        redResetRequested = false;
        nextReadyAnnounceTime = 0f;
        SetState(MatchState.Ended);
        Log(resultMessage + " Winner: " + CaptureTeams.GetDisplayName(matchWinner) + ".");
    }

    private void ResetMatch(bool startImmediately)
    {
        redScore = 0f;
        yellowScore = 0f;
        redManpower = startingManpower;
        yellowManpower = startingManpower;
        manpowerIntervalElapsed = 0f;
        winner = CaptureOwner.Neutral;
        resultMessage = string.Empty;
        yellowReady = false;
        redReady = false;
        yellowResetRequested = false;
        redResetRequested = false;
        nextReadyAnnounceTime = 0f;
        SetState(MatchState.Waiting);

        if (board != null && board.Villages != null)
        {
            PointCaptureVillage[] villages = board.Villages;
            for (int i = 0; i < villages.Length; i++)
            {
                villages[i]?.ResetToStart();
            }

            board.NotifyTerritoryChanged();
        }

        if (startImmediately)
        {
            BeginPlay();
        }
    }

    private void SetState(MatchState next)
    {
        currentState = next;
        stateElapsed = 0f;
        StateChanged?.Invoke(currentState);
    }

    private void Log(string message)
    {
        if (!logMatchEvents)
        {
            return;
        }

        Debug.Log("[PointCapture] " + message, this);
    }

    private void TryBeginWhenBothReady()
    {
        if (currentState != MatchState.Waiting || !AreBothSidesReady)
        {
            return;
        }

        BroadcastReadyState();
        SendNetworkCommand("START");
        BeginCountdown();
    }

    private void TryAnnounceReady()
    {
        if (!HasConnectedPeer || Time.time < nextReadyAnnounceTime)
        {
            return;
        }

        nextReadyAnnounceTime = Time.time + ReadyAnnounceIntervalSeconds;
        if (yellowReady || redReady)
        {
            BroadcastReadyState();
        }
    }

    private void TryRestartWhenBothReset()
    {
        if (currentState != MatchState.Ended || !AreBothSidesResetRequested)
        {
            return;
        }

        BroadcastResetState();
        SendNetworkCommand("RESTART");
        Restart();
    }

    private void TryAnnounceReset()
    {
        if (!HasConnectedPeer || Time.time < nextReadyAnnounceTime)
        {
            return;
        }

        nextReadyAnnounceTime = Time.time + ReadyAnnounceIntervalSeconds;
        if (yellowResetRequested || redResetRequested)
        {
            BroadcastResetState();
        }
    }

    private void BindNetworking()
    {
        if (networking == null || networkingBound)
        {
            return;
        }

        previousReceivedHandler = networking.ReceivedMessage;
        networking.ReceivedMessage = HandleNetworkMessage;
        networkingBound = true;
    }

    private void UnbindNetworking()
    {
        if (networking == null || !networkingBound)
        {
            return;
        }

        networking.ReceivedMessage = previousReceivedHandler;
        previousReceivedHandler = null;
        networkingBound = false;
    }

    private void HandleNetworkMessage(string message)
    {
        previousReceivedHandler?.Invoke(message);
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        int index = message.IndexOf(ReadyMessagePrefix, StringComparison.Ordinal);
        if (index < 0)
        {
            return;
        }

        string protocol = index == 0 ? message : message.Substring(index);
        string[] parts = protocol.Split('|');
        if (parts.Length < 3 || parts[0] != "PC")
        {
            return;
        }

        string token = parts[2];
        if (!string.IsNullOrEmpty(localInstanceToken) && token == localInstanceToken)
        {
            return;
        }

        switch (parts[1])
        {
            case "READY":
                if (parts.Length >= 5)
                {
                    if (parts[3] == "1")
                    {
                        yellowReady = true;
                    }

                    if (parts[4] == "1")
                    {
                        redReady = true;
                    }

                    TryBeginWhenBothReady();
                }

                break;
            case "START":
                if (currentState == MatchState.Waiting)
                {
                    yellowReady = true;
                    redReady = true;
                    BeginCountdown();
                }

                break;
            case "RESET":
                if (currentState == MatchState.Ended && parts.Length >= 5)
                {
                    if (parts[3] == "1")
                    {
                        yellowResetRequested = true;
                    }

                    if (parts[4] == "1")
                    {
                        redResetRequested = true;
                    }

                    TryRestartWhenBothReset();
                }

                break;
            case "RESTART":
                if (currentState == MatchState.Ended)
                {
                    Restart();
                }

                break;
        }
    }

    private void BroadcastReadyState()
    {
        SendNetworkCommand(
            "READY",
            (yellowReady ? "1" : "0") + "|" + (redReady ? "1" : "0"));
    }

    private void BroadcastResetState()
    {
        SendNetworkCommand(
            "RESET",
            (yellowResetRequested ? "1" : "0") + "|" + (redResetRequested ? "1" : "0"));
    }

    private void SendNetworkCommand(string command, string payload = null)
    {
        if (networking == null)
        {
            return;
        }

        string token = string.IsNullOrEmpty(localInstanceToken) ? "local" : localInstanceToken;
        string message = string.IsNullOrEmpty(payload)
            ? ReadyMessagePrefix + command + "|" + token
            : ReadyMessagePrefix + command + "|" + token + "|" + payload;
        SendNetwork(message);
    }

    private void SendNetwork(string message)
    {
        if (networking == null || string.IsNullOrEmpty(message))
        {
            return;
        }

        List<int> peerIds = new List<int>(4);
        try
        {
            foreach (vGear_NetworkUser user in networking.GetAllNetworkUsers())
            {
                if (user != null && user.userID != networking.networkID)
                {
                    peerIds.Add((int)user.userID);
                }
            }
        }
        catch (Exception)
        {
        }

        try
        {
            if (peerIds.Count > 0)
            {
                networking.Send(message, false, false, peerIds.ToArray());
            }
            else
            {
                networking.Send(message, false, false);
            }
        }
        catch (Exception)
        {
            try
            {
                networking.Send(message);
            }
            catch (Exception)
            {
            }
        }
    }
}
