using System;
using UnityEngine;

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
    [SerializeField] private bool autoStartOnPlay = true;
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

    private MatchState currentState = MatchState.Waiting;
    private float stateElapsed;
    private float redScore;
    private float yellowScore;
    private float redManpower;
    private float yellowManpower;
    private CaptureOwner winner = CaptureOwner.Neutral;
    private string resultMessage = string.Empty;
    private float manpowerIntervalElapsed;

    public MatchState CurrentState => currentState;
    public bool IsPlaying => currentState == MatchState.Playing;
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

    public event Action<MatchState> StateChanged;

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
    }

    private void Start()
    {
        if (board != null)
        {
            board.BindVillages();
            BindHomeCamps();
        }

        if (autoStartOnPlay)
        {
            BeginCountdown();
        }
        else
        {
            ResetMatch(startImmediately: false);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
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
        BeginCountdown();
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
        if (!IsPlaying || board == null)
        {
            return;
        }

        int redVillages = board.CountOwned(CaptureOwner.Red);
        int yellowVillages = board.CountOwned(CaptureOwner.Yellow);
        if (redVillages == 0)
        {
            EndMatch(CaptureOwner.Yellow, "Yellow holds every remaining village.");
        }
        else if (yellowVillages == 0)
        {
            EndMatch(CaptureOwner.Red, "Red holds every remaining village.");
        }
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

        if (redVillages == 0)
        {
            EndMatch(CaptureOwner.Yellow, "Yellow holds every remaining village.");
            return;
        }

        if (yellowVillages == 0)
        {
            EndMatch(CaptureOwner.Red, "Red holds every remaining village.");
            return;
        }

        if (stateElapsed >= matchDurationSeconds)
        {
            EndByScore();
        }
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
        SetState(MatchState.Ended);
        Log(message + " Winner: " + CaptureTeams.GetDisplayName(matchWinner) + ".");
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
}
