using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using Votanic.vNet.Networking;
using Votanic.vXR.vGear;
using Votanic.vXR.vGear.Networking;

/// <summary>
/// Single-scene siege PVP.
/// Attacker (host): commands the siege army and stalls until the cannons are ready.
/// Defender (client): commands the city regiments and tries to stop the cannons.
/// Lobby: select → ready → countdown → play.
/// </summary>
[DefaultExecutionOrder(-40)]
[DisallowMultipleComponent]
public class SiegePvpSession : MonoBehaviour
{
    private const string MessagePrefix = "SP|";

    public enum RoleMode
    {
        AutoFromNetwork = 0,
        ForceAttacker = 1,
        ForceDefender = 2
    }

    public enum LocalRole
    {
        Attacker = 0,
        Defender = 1
    }

    private enum LobbyPhase
    {
        Idle = 0,
        SelectedWaitingReady = 1,
        ReadyWaitingPeer = 2,
        Countdown = 3,
        Playing = 4
    }

    [Header("References")]
    [SerializeField] private vGear_Networking networking;
    [SerializeField] private VotanicWandRtsCommander wandCommander;
    [Tooltip("Optional city-defender viewpoint. Client is teleported here for PVP; leave empty to keep current pose.")]
    [UnityEngine.Serialization.FormerlySerializedAs("attackerSpawnPoint")]
    [SerializeField] private Transform defenderSpawnPoint;
    [Tooltip("Menu / siege command-tower return spawn (attacker stays here; defender returns here after the match).")]
    [SerializeField] private Transform menuSpawnPoint;

    [Header("Role")]
    [Tooltip("Auto: Host = Attacker (stall for cannons), Client = Defender (stop cannons).")]
    [SerializeField] private RoleMode roleMode = RoleMode.AutoFromNetwork;
    [SerializeField] private bool autoConnectOnStart = false;
    [SerializeField] private bool autoHost = true;
    [SerializeField] private string clientHostAddress = "127.0.0.1";
    [SerializeField] private int networkPort = 7777;

    [Header("Match Defaults")]
    [Tooltip("Runtime cache. Filled from SiegeGameManager on play/select unless a PVP button has Use Custom Pvp Tuning.")]
    [SerializeField, Min(0f)] private float preMatchCountdownSeconds = 5f;
    [SerializeField, Min(30f)] private float matchDurationSeconds = 180f;
    [SerializeField, Range(0.1f, 2f)] private float moveSpeedScale = 1f;
    [Tooltip("Path commands are always synced on issue. Pose is a low-rate safety net for drift.")]
    [SerializeField] private bool enablePoseCorrection = true;
    [SerializeField, Min(0.1f)] private float poseSyncIntervalSeconds = 0.35f;
    [SerializeField, Range(2, 16)] private int maxSyncedPathPoints = 10;

    [Header("Prompts")]
    [SerializeField] private string selectedPrompt = "Siege PVP selected. Press Ready when both players are connected.";
    [SerializeField] private string readyWaitingPrompt = "Ready — waiting for the other player... Press again to cancel.";
    [SerializeField] private string peerSelectedPrompt = "Opponent selected Siege PVP. Press Ready.";
    [SerializeField] private string countdownFormat = "Siege PVP starts in {0:0}...";

    [Header("Debug")]
    [SerializeField] private bool logNetworkMessages = true;
    [Tooltip("When enabled, pressing Ready starts PVP without a second machine (for editor/host-only tests). Off by default.")]
    [SerializeField] private bool allowSoloTesting = false;

    private LocalRole resolvedRole = LocalRole.Attacker;
    private LobbyPhase lobbyPhase = LobbyPhase.Idle;
    private bool localSelected;
    private bool peerSelected;
    private bool localReady;
    private bool peerReady;
    private bool matchRunning;
    private Coroutine countdownCoroutine;
    private NetworkManager.OnReceived previousReceivedHandler;
    private bool suppressMenuBroadcast;
    private bool applyingRemotePath;
    private float nextPoseSyncTime;
    private float nextLobbyAnnounceTime;
    private float modeSelectUnlockTime;
    private bool pendingShowModeSelectAfterRelease;
    private string localInstanceToken;
    private readonly Dictionary<string, RtsUnitMotor> motorBySyncKey = new Dictionary<string, RtsUnitMotor>();
    private readonly List<RtsUnitMotor> syncMotorsByIndex = new List<RtsUnitMotor>();
    private readonly Dictionary<RtsUnitMotor, int> syncIndexByMotor = new Dictionary<RtsUnitMotor, int>();
    private float lastPathNotifyTime = -1f;
    private int lastPathNotifyMotorId = int.MinValue;

    public static SiegePvpSession Instance { get; private set; }
    public LocalRole Role => resolvedRole;
    public bool IsAttacker => resolvedRole == LocalRole.Attacker;
    public bool IsDefender => resolvedRole == LocalRole.Defender;
    public bool IsMatchRunning => matchRunning;
    public bool IsInPvpLobby => lobbyPhase == LobbyPhase.SelectedWaitingReady
        || lobbyPhase == LobbyPhase.ReadyWaitingPeer
        || lobbyPhase == LobbyPhase.Countdown;
    public bool BlocksModeSelect => IsInPvpLobby
        || matchRunning
        || pendingShowModeSelectAfterRelease
        || Time.unscaledTime < modeSelectUnlockTime;

    private void Awake()
    {
        Instance = this;
        localInstanceToken = Guid.NewGuid().ToString("N").Substring(0, 8);
        if (networking == null)
        {
            networking = FindObjectOfType<vGear_Networking>();
        }

        if (wandCommander == null)
        {
            wandCommander = FindObjectOfType<VotanicWandRtsCommander>();
        }

        ResolveRole();
        PullDefaultsFromGameManager();
    }

    private void PullDefaultsFromGameManager()
    {
        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager == null)
        {
            return;
        }

        matchDurationSeconds = manager.SiegePvpMatchDurationSeconds;
        moveSpeedScale = manager.SiegePvpMoveSpeedScale;
    }

    private void EnsureWandBound()
    {
        if (wandCommander == null)
        {
            wandCommander = FindObjectOfType<VotanicWandRtsCommander>();
        }

        if (wandCommander == null)
        {
            return;
        }

        wandCommander.PathCommandIssued -= HandleLocalPathCommand;
        wandCommander.PathCommandIssued -= NotifyPathFromCommander;
        wandCommander.PathCommandIssued += NotifyPathFromCommander;
    }

    private void OnEnable()
    {
        Instance = this;
        EnsureWandBound();
        BindNetworking();

        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager != null)
        {
            manager.MatchStateChanged += HandleMatchStateChanged;
        }
    }

    private void OnDisable()
    {
        if (wandCommander != null)
        {
            wandCommander.PathCommandIssued -= HandleLocalPathCommand;
            wandCommander.PathCommandIssued -= NotifyPathFromCommander;
        }

        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager != null)
        {
            manager.MatchStateChanged -= HandleMatchStateChanged;
        }

        StopCountdown();
        UnbindNetworking();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        ResolveRole();
        PullDefaultsFromGameManager();
        EnsureWandBound();
        if (autoConnectOnStart)
        {
            TryAutoConnect();
        }

        ApplyCommandFactionFilter();
    }

    private void Update()
    {
        ResolveRole();
        if (networking != null && networking.ReceivedMessage == null)
        {
            BindNetworking();
        }

        TryFlushPendingModeSelect();

        if (IsInPvpLobby)
        {
            TryLobbyAnnounce();
        }

        if (matchRunning && enablePoseCorrection)
        {
            TryBroadcastOwnedUnitPoses();
        }

        if (!SiegeVrInput.IsGameplayInputAllowed() || !SiegeVrInput.WasPointerPressedThisFrame())
        {
            return;
        }

        if (lobbyPhase == LobbyPhase.SelectedWaitingReady && !localReady)
        {
            NotifyLocalReady();
            return;
        }

        // Escape softlock: cancel lobby while waiting for the other player.
        if (lobbyPhase == LobbyPhase.ReadyWaitingPeer)
        {
            CancelPvpLobbyToMenu(broadcast: true);
        }
    }

    private void TryFlushPendingModeSelect()
    {
        if (!pendingShowModeSelectAfterRelease)
        {
            return;
        }

        bool released = !SiegeVrInput.IsPointerHeld();
        bool forceAfterTimeout = Time.unscaledTime >= modeSelectUnlockTime + 1.5f;
        if (!released && !forceAfterTimeout)
        {
            return;
        }

        if (Time.unscaledTime < modeSelectUnlockTime && !forceAfterTimeout)
        {
            return;
        }

        pendingShowModeSelectAfterRelease = false;
        if (SiegeMatchUi.Instance != null)
        {
            SiegeMatchUi.Instance.ShowModeSelect();
        }
    }

    public void NotifyLocalSelectedPvp(float durationSeconds = -1f, float speedScale = -1f)
    {
        if (lobbyPhase != LobbyPhase.Idle && lobbyPhase != LobbyPhase.SelectedWaitingReady)
        {
            return;
        }

        // Always start from GameManager, then optionally override from the difficulty button.
        PullDefaultsFromGameManager();
        if (durationSeconds > 0f)
        {
            matchDurationSeconds = durationSeconds;
        }

        if (speedScale > 0f)
        {
            moveSpeedScale = speedScale;
        }

        ResolveRole();
        localSelected = true;
        lobbyPhase = LobbyPhase.SelectedWaitingReady;
        nextLobbyAnnounceTime = 0f;
        BroadcastSelect();

        if (SiegeMatchUi.Instance != null)
        {
            SiegeMatchUi.Instance.HideDifficultyOptionsForPvpLobby();
        }

        RefreshLobbyStatus();
    }

    public void NotifyLocalReady()
    {
        if (!localSelected)
        {
            return;
        }

        localReady = true;
        lobbyPhase = LobbyPhase.ReadyWaitingPeer;
        nextLobbyAnnounceTime = 0f;
        BroadcastReady();
        RefreshLobbyStatus();

        if (allowSoloTesting)
        {
            peerSelected = true;
            peerReady = true;
        }

        TryBeginSharedSetup();
    }

    public void NotifyLocalReturnToMenu()
    {
        if (suppressMenuBroadcast)
        {
            ReturnToMenuLocal(broadcast: false);
            return;
        }

        ReturnToMenuLocal(broadcast: true);
    }

    /// <summary>
    /// Leave the PVP ready lobby and restore mode select (before the match countdown).
    /// </summary>
    public void CancelPvpLobbyToMenu(bool broadcast)
    {
        if (lobbyPhase != LobbyPhase.SelectedWaitingReady
            && lobbyPhase != LobbyPhase.ReadyWaitingPeer)
        {
            return;
        }

        if (broadcast)
        {
            SendCommand("CANCEL");
        }

        StopCountdown();
        matchRunning = false;
        ResetLobbyFlags();
        ApplyCommandFactionFilter();

        // Defer mode-select restore until pointer release + short lockout, so the same
        // cancel click cannot immediately confirm Demo / Full / Dodge.
        modeSelectUnlockTime = Time.unscaledTime + 0.85f;
        pendingShowModeSelectAfterRelease = true;

        if (logNetworkMessages)
        {
            Debug.Log("SiegePvp lobby cancelled — mode select unlocks after release.", this);
        }
    }

    private void HandleMatchStateChanged(SiegeGameManager.MatchState state)
    {
        if (state == SiegeGameManager.MatchState.SelectingDifficulty)
        {
            if (lobbyPhase != LobbyPhase.Idle || matchRunning)
            {
                CleanupPvpPresentation(teleportDefenderHome: true);
                ResetLobbyFlags();
            }

            ApplyCommandFactionFilter();
            return;
        }

        if (!IsAttacker || !matchRunning)
        {
            return;
        }

        // Attacker (host / siege side) owns outcome broadcast.
        if (state == SiegeGameManager.MatchState.Won)
        {
            matchRunning = false;
            SendCommand("WIN", "attacker");
        }
        else if (state == SiegeGameManager.MatchState.Lost)
        {
            matchRunning = false;
            string reason = SiegeGameManager.Instance != null
                ? SiegeGameManager.Instance.DefeatReason
                : "The cannons were overrun.";
            SendCommand("LOSE", "attacker|" + Sanitize(reason));
        }
    }

    private void ResolveRole()
    {
        switch (roleMode)
        {
            case RoleMode.ForceAttacker:
                resolvedRole = LocalRole.Attacker;
                return;
            case RoleMode.ForceDefender:
                resolvedRole = LocalRole.Defender;
                return;
        }

        if (networking != null)
        {
            // Host sieges (stall for cannons); client defends the city (stop cannons).
            resolvedRole = networking.type == UserType.Host ? LocalRole.Attacker : LocalRole.Defender;
            return;
        }

        resolvedRole = LocalRole.Attacker;
    }

    private void ApplyCommandFactionFilter()
    {
        if (wandCommander == null)
        {
            return;
        }

        if (!SiegeMatchSettings.IsSiegePvpMode && lobbyPhase == LobbyPhase.Idle)
        {
            wandCommander.SetControllableFaction(TroopCombat.Faction.Friendly);
            return;
        }

        // Attacker commands the siege army (Friendly). Defender commands the city force (Enemy).
        wandCommander.SetControllableFaction(
            IsDefender ? TroopCombat.Faction.Enemy : TroopCombat.Faction.Friendly);
    }

    private void TryAutoConnect()
    {
        if (networking == null)
        {
            return;
        }

        networking.port = networkPort;
        if (networking.uNetManager != null)
        {
            networking.uNetManager.networkPort = networkPort;
        }

        if (autoHost || roleMode == RoleMode.ForceAttacker)
        {
            networking.Host();
        }
        else
        {
            networking.Connect(clientHostAddress, networkPort);
        }
    }

    private void BindNetworking()
    {
        if (networking == null)
        {
            return;
        }

        previousReceivedHandler = networking.ReceivedMessage;
        networking.ReceivedMessage = HandleNetworkMessage;
    }

    private void UnbindNetworking()
    {
        if (networking == null)
        {
            return;
        }

        networking.ReceivedMessage = previousReceivedHandler;
        previousReceivedHandler = null;
    }

    private void HandleNetworkMessage(string message)
    {
        previousReceivedHandler?.Invoke(message);
        if (!TryExtractProtocolMessage(message, out string protocol))
        {
            return;
        }

        if (logNetworkMessages && !protocol.StartsWith("SP|POSE", StringComparison.Ordinal))
        {
            Debug.Log("SiegePvp recv: " + protocol, this);
        }

        string[] parts = protocol.Split('|');
        if (parts.Length < 2)
        {
            return;
        }

        // Protocol: SP|<CMD>|<instanceToken>|...payload
        if (!TryGetPeerPayloadStart(parts, out int p))
        {
            return;
        }

        switch (parts[1])
        {
            case "SELECT":
                peerSelected = true;
                if (parts.Length >= p + 2
                    && TryParseFloat(parts[p], out float peerDuration)
                    && TryParseFloat(parts[p + 1], out float peerSpeed))
                {
                    // Prefer host (attacker / siege) match settings when both selected.
                    if (!IsAttacker || !localSelected)
                    {
                        matchDurationSeconds = peerDuration;
                        moveSpeedScale = peerSpeed;
                    }
                }

                RefreshLobbyStatus();
                TryBeginSharedSetup();
                break;
            case "READY":
                peerReady = true;
                RefreshLobbyStatus();
                TryBeginSharedSetup();
                break;
            case "CANCEL":
                CancelPvpLobbyToMenu(broadcast: false);
                break;
            case "SETUP":
                BeginSharedSetup();
                break;
            case "COUNT":
                if (parts.Length > p
                    && int.TryParse(parts[p], NumberStyles.Integer, CultureInfo.InvariantCulture, out int secondsLeft))
                {
                    ShowStatus(string.Format(CultureInfo.InvariantCulture, countdownFormat, secondsLeft));
                }

                break;
            case "GO":
                if (parts.Length >= p + 2
                    && TryParseFloat(parts[p], out float duration)
                    && TryParseFloat(parts[p + 1], out float speed))
                {
                    matchDurationSeconds = duration;
                    moveSpeedScale = speed;
                }

                BeginGameplay();
                break;
            case "PATH":
                HandleRemotePath(parts, p);
                break;
            case "POSE":
                HandleRemotePose(parts, p);
                break;
            case "WIN":
                ApplyRemoteOutcome(attackerWon: true, reason: string.Empty);
                break;
            case "LOSE":
                ApplyRemoteOutcome(
                    attackerWon: false,
                    reason: parts.Length > p + 1 ? parts[p + 1] : "The cannons were overrun.");
                break;
            case "MENU":
                ReturnToMenuLocal(broadcast: false);
                break;
        }
    }

    /// <summary>
    /// Votanic may wrap payloads as "[Name]SP|..." when Send(..., withName:true).
    /// Tutorial07 uses Contains; we extract from the SP| marker.
    /// </summary>
    private static bool TryExtractProtocolMessage(string raw, out string protocol)
    {
        protocol = null;
        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        int index = raw.IndexOf(MessagePrefix, StringComparison.Ordinal);
        if (index < 0)
        {
            return false;
        }

        protocol = index == 0 ? raw : raw.Substring(index);
        return true;
    }

    /// <summary>
    /// Returns false when the message is from this machine (local echo).
    /// payloadStart is the first index after SP|CMD|token.
    /// </summary>
    private bool TryGetPeerPayloadStart(string[] parts, out int payloadStart)
    {
        payloadStart = 2;
        if (parts.Length < 3)
        {
            return true;
        }

        string token = parts[2];
        if (string.IsNullOrEmpty(localInstanceToken) || token != localInstanceToken)
        {
            // Numeric legacy sender ids also accepted as peer when not our token.
            payloadStart = 3;
            return true;
        }

        // Our own echo.
        return false;
    }

    private void TryLobbyAnnounce()
    {
        if (Time.time < nextLobbyAnnounceTime)
        {
            return;
        }

        nextLobbyAnnounceTime = Time.time + 1f;

        if (localSelected)
        {
            BroadcastSelect();
        }

        if (localReady)
        {
            BroadcastReady();
        }
    }

    private void BroadcastSelect()
    {
        SendCommand(
            "SELECT",
            string.Format(
                CultureInfo.InvariantCulture,
                "{0:0.###}|{1:0.###}",
                matchDurationSeconds,
                moveSpeedScale));
    }

    private void BroadcastReady()
    {
        SendCommand("READY");
    }

    private void SendCommand(string command, string payload = null)
    {
        string token = string.IsNullOrEmpty(localInstanceToken) ? "local" : localInstanceToken;
        if (string.IsNullOrEmpty(payload))
        {
            Send(string.Format(CultureInfo.InvariantCulture, "SP|{0}|{1}", command, token));
        }
        else
        {
            Send(string.Format(CultureInfo.InvariantCulture, "SP|{0}|{1}|{2}", command, token, payload));
        }
    }

    private void TryBeginSharedSetup()
    {
        if (lobbyPhase == LobbyPhase.Countdown || lobbyPhase == LobbyPhase.Playing || matchRunning)
        {
            return;
        }

        bool bothSelected = localSelected && (peerSelected || allowSoloTesting);
        bool bothReady = localReady && (peerReady || allowSoloTesting);
        if (!bothSelected || !bothReady)
        {
            if (logNetworkMessages && localReady)
            {
                Debug.Log(
                    "SiegePvp waiting start — peerSelected=" + peerSelected
                    + " peerReady=" + peerReady
                    + " role=" + resolvedRole,
                    this);
            }

            return;
        }

        if (!IsAttacker && networking != null)
        {
            return;
        }

        SendCommand("SETUP");
        BeginSharedSetup();
    }

    private void BeginSharedSetup()
    {
        if (lobbyPhase == LobbyPhase.Countdown || lobbyPhase == LobbyPhase.Playing)
        {
            return;
        }

        lobbyPhase = LobbyPhase.Countdown;

        if (IsDefender && defenderSpawnPoint != null)
        {
            TeleportUser(defenderSpawnPoint);
        }

        if (SiegeMatchUi.Instance != null)
        {
            SiegeMatchUi.Instance.HideDifficultyOptionsForPvpLobby();
        }

        StopCountdown();
        if (IsAttacker || networking == null)
        {
            countdownCoroutine = StartCoroutine(HostCountdownAndGo());
        }
        else
        {
            ShowStatus(string.Format(CultureInfo.InvariantCulture, countdownFormat, preMatchCountdownSeconds));
        }
    }

    private IEnumerator HostCountdownAndGo()
    {
        int total = Mathf.Max(0, Mathf.RoundToInt(preMatchCountdownSeconds));
        for (int i = total; i > 0; i--)
        {
            ShowStatus(string.Format(CultureInfo.InvariantCulture, countdownFormat, i));
            SendCommand("COUNT", i.ToString(CultureInfo.InvariantCulture));
            yield return new WaitForSecondsRealtime(1f);
        }

        SendCommand(
            "GO",
            string.Format(
                CultureInfo.InvariantCulture,
                "{0:0.###}|{1:0.###}",
                matchDurationSeconds,
                moveSpeedScale));
        BeginGameplay();
    }

    private void BeginGameplay()
    {
        StopCountdown();
        lobbyPhase = LobbyPhase.Playing;
        matchRunning = true;
        nextPoseSyncTime = 0f;
        motorBySyncKey.Clear();
        EnsureWandBound();

        matchDurationSeconds = Mathf.Max(30f, matchDurationSeconds);
        moveSpeedScale = Mathf.Clamp(moveSpeedScale, 0.1f, 2f);

        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager != null && manager.CurrentState == SiegeGameManager.MatchState.SelectingDifficulty)
        {
            manager.ConfirmPlayMode(
                SiegeGameMode.SiegePvp,
                matchDurationSeconds,
                moveSpeedScale);
        }
        else
        {
            // Ensure timer uses the selected duration even if ConfirmPlayMode was already called.
            SiegeMatchSettings.Configure(
                SiegeGameMode.SiegePvp,
                manager != null ? manager.DemoMoveSpeedScale : 0.6f,
                matchDurationSeconds,
                moveSpeedScale);
        }

        ApplyMoveSpeedToAllTroops();

        if (logNetworkMessages)
        {
            Debug.Log(
                "SiegePvp duration=" + matchDurationSeconds.ToString("F0")
                + "s, speed=" + moveSpeedScale.ToString("F2")
                + ", settings=" + SiegeMatchSettings.PvpMatchDurationSeconds.ToString("F0") + "s.",
                this);
        }

        ApplyCommandFactionFilter();
        ConfigureCityDefenderRegimentsForPvp();

        if (EnemyWaveController.Instance != null)
        {
            EnemyWaveController.Instance.DeployAllForSiegePvp();
        }

        ApplyMoveSpeedToAllTroops();
        RebuildUnitSyncTable();
        EnsureWandBound();

        if (logNetworkMessages)
        {
            Debug.Log(
                "SiegePvp gameplay as " + resolvedRole
                + " with " + syncMotorsByIndex.Count + " sync units.",
                this);
        }
    }

    private static void ApplyMoveSpeedToAllTroops()
    {
        TroopCombat[] troops = FindObjectsOfType<TroopCombat>(true);
        for (int i = 0; i < troops.Length; i++)
        {
            if (troops[i] == null)
            {
                continue;
            }

            RtsUnitMotor motor = troops[i].GetComponent<RtsUnitMotor>();
            if (motor != null)
            {
                motor.MoveSpeedMultiplier = SiegeMatchSettings.ActiveMoveSpeedScale;
            }
        }
    }

    private void ConfigureCityDefenderRegimentsForPvp()
    {
        EnemyRegimentAI[] enemies = FindObjectsOfType<EnemyRegimentAI>(true);
        for (int i = 0; i < enemies.Length; i++)
        {
            if (enemies[i] != null && enemies[i].IncludeInSiegePvp)
            {
                enemies[i].ConfigureForSiegePvp();
            }
        }
    }

    /// <summary>
    /// Called from the wand commander after a local path is issued (also hooked via event).
    /// </summary>
    public void NotifyPathFromCommander(RtsUnitMotor motor, IReadOnlyList<Vector3> path)
    {
        if (motor == null)
        {
            return;
        }

        int motorId = motor.GetInstanceID();
        if (Mathf.Abs(Time.unscaledTime - lastPathNotifyTime) < 0.05f && motorId == lastPathNotifyMotorId)
        {
            return;
        }

        lastPathNotifyTime = Time.unscaledTime;
        lastPathNotifyMotorId = motorId;
        HandleLocalPathCommand(motor, path);
    }

    private void RebuildUnitSyncTable()
    {
        syncMotorsByIndex.Clear();
        syncIndexByMotor.Clear();
        motorBySyncKey.Clear();

        RtsUnitMotor[] found = FindObjectsOfType<RtsUnitMotor>(true);
        List<RtsUnitMotor> sorted = new List<RtsUnitMotor>(found.Length);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null && found[i].IsCommandUnit)
            {
                sorted.Add(found[i]);
            }
        }

        sorted.Sort((a, b) => string.CompareOrdinal(
            GetUnitSyncKey(a.transform),
            GetUnitSyncKey(b.transform)));

        for (int i = 0; i < sorted.Count; i++)
        {
            RtsUnitMotor motor = sorted[i];
            syncMotorsByIndex.Add(motor);
            syncIndexByMotor[motor] = i;
            motorBySyncKey[i.ToString(CultureInfo.InvariantCulture)] = motor;
            motorBySyncKey[SanitizeSyncKey(GetUnitSyncKey(motor.transform))] = motor;
            motorBySyncKey[motor.gameObject.name] = motor;
        }
    }

    private int ResolveSyncIndex(RtsUnitMotor motor)
    {
        if (motor == null)
        {
            return -1;
        }

        if (syncIndexByMotor.TryGetValue(motor, out int index))
        {
            return index;
        }

        RebuildUnitSyncTable();
        return syncIndexByMotor.TryGetValue(motor, out index) ? index : -1;
    }

    private RtsUnitMotor FindMotorBySyncIndex(int index)
    {
        if (index < 0)
        {
            return null;
        }

        if (index < syncMotorsByIndex.Count)
        {
            return syncMotorsByIndex[index];
        }

        RebuildUnitSyncTable();
        return index < syncMotorsByIndex.Count ? syncMotorsByIndex[index] : null;
    }

    private void HandleLocalPathCommand(RtsUnitMotor motor, IReadOnlyList<Vector3> path)
    {
        if (!matchRunning || applyingRemotePath || motor == null || path == null || path.Count == 0)
        {
            return;
        }

        int syncIndex = ResolveSyncIndex(motor);
        if (syncIndex < 0)
        {
            Debug.LogWarning("SiegePvp PATH: motor not in sync table: " + motor.name, this);
            return;
        }

        List<Vector3> compressed = CompressPath(path, Mathf.Max(2, maxSyncedPathPoints));
        StringBuilder builder = new StringBuilder(64 + compressed.Count * 20);
        string token = string.IsNullOrEmpty(localInstanceToken) ? "local" : localInstanceToken;
        builder.Append("SP|PATH|").Append(token).Append('|').Append(syncIndex);
        for (int i = 0; i < compressed.Count; i++)
        {
            Vector3 point = compressed[i];
            builder.Append('|')
                .Append(point.x.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                .Append(point.y.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                .Append(point.z.ToString("0.##", CultureInfo.InvariantCulture));
        }

        if (logNetworkMessages)
        {
            Debug.Log(
                "SiegePvp PATH send unit#" + syncIndex + " '" + motor.name
                + "' pts=" + compressed.Count + "/" + path.Count,
                this);
        }

        Send(builder.ToString());
    }

    private static List<Vector3> CompressPath(IReadOnlyList<Vector3> path, int maxPoints)
    {
        List<Vector3> result = new List<Vector3>(maxPoints);
        if (path == null || path.Count == 0)
        {
            return result;
        }

        if (path.Count <= maxPoints)
        {
            for (int i = 0; i < path.Count; i++)
            {
                result.Add(path[i]);
            }

            return result;
        }

        result.Add(path[0]);
        for (int i = 1; i < maxPoints - 1; i++)
        {
            float t = i / (float)(maxPoints - 1);
            int idx = Mathf.Clamp(Mathf.RoundToInt(t * (path.Count - 1)), 0, path.Count - 1);
            result.Add(path[idx]);
        }

        result.Add(path[path.Count - 1]);
        return result;
    }

    private void TryBroadcastOwnedUnitPoses()
    {
        if (!enablePoseCorrection || Time.time < nextPoseSyncTime)
        {
            return;
        }

        nextPoseSyncTime = Time.time + Mathf.Max(0.05f, poseSyncIntervalSeconds);

        TroopCombat.Faction ownedFaction = IsDefender
            ? TroopCombat.Faction.Enemy
            : TroopCombat.Faction.Friendly;

        if (syncMotorsByIndex.Count == 0)
        {
            RebuildUnitSyncTable();
        }

        string token = string.IsNullOrEmpty(localInstanceToken) ? "local" : localInstanceToken;
        StringBuilder builder = new StringBuilder(128);
        builder.Append("SP|POSE|").Append(token);
        int count = 0;

        for (int i = 0; i < syncMotorsByIndex.Count; i++)
        {
            RtsUnitMotor motor = syncMotorsByIndex[i];
            if (motor == null || !motor.gameObject.activeInHierarchy)
            {
                continue;
            }

            TroopCombat troop = motor.GetComponent<TroopCombat>();
            if (troop == null || troop.TroopFaction != ownedFaction)
            {
                continue;
            }

            if (!motor.HasActivePath && count > 8)
            {
                continue;
            }

            Vector3 p = motor.transform.position;
            builder.Append('|').Append(i).Append(',')
                .Append(p.x.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                .Append(p.y.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                .Append(p.z.ToString("0.##", CultureInfo.InvariantCulture));
            count++;
        }

        if (count > 0)
        {
            Send(builder.ToString());
        }
    }

    private void HandleRemotePath(string[] parts, int payloadStart)
    {
        // SP|PATH|token|<index>|x,y,z|...
        if (parts.Length < payloadStart + 2)
        {
            return;
        }

        RtsUnitMotor motor = null;
        string idPart = parts[payloadStart];
        if (int.TryParse(idPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int syncIndex))
        {
            motor = FindMotorBySyncIndex(syncIndex);
        }

        if (motor == null)
        {
            motor = FindMotorBySyncKey(idPart);
        }

        if (motor == null)
        {
            Debug.LogWarning("SiegePvp remote PATH: no motor for '" + idPart + "'.", this);
            return;
        }

        List<Vector3> path = new List<Vector3>(parts.Length - payloadStart - 1);
        for (int i = payloadStart + 1; i < parts.Length; i++)
        {
            string[] xyz = parts[i].Split(',');
            if (xyz.Length < 3)
            {
                continue;
            }

            if (TryParseFloat(xyz[0], out float x)
                && TryParseFloat(xyz[1], out float y)
                && TryParseFloat(xyz[2], out float z))
            {
                path.Add(new Vector3(x, y, z));
            }
        }

        if (path.Count == 0)
        {
            return;
        }

        applyingRemotePath = true;
        try
        {
            motor.FollowPath(path);
            if (logNetworkMessages)
            {
                Debug.Log(
                    "SiegePvp applied remote PATH '" + idPart + "' -> " + motor.name
                    + " (" + path.Count + " pts).",
                    this);
            }
        }
        finally
        {
            applyingRemotePath = false;
        }
    }

    private void HandleRemotePose(string[] parts, int payloadStart)
    {
        // Compact: SP|POSE|token|index,x,y,z|index,x,y,z|...
        if (parts.Length <= payloadStart)
        {
            return;
        }

        applyingRemotePath = true;
        try
        {
            for (int i = payloadStart; i < parts.Length; i++)
            {
                string[] packed = parts[i].Split(',');
                if (packed.Length < 4)
                {
                    continue;
                }

                if (!int.TryParse(packed[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int syncIndex)
                    || !TryParseFloat(packed[1], out float x)
                    || !TryParseFloat(packed[2], out float y)
                    || !TryParseFloat(packed[3], out float z))
                {
                    continue;
                }

                RtsUnitMotor motor = FindMotorBySyncIndex(syncIndex);
                if (motor == null || IsLocallyOwnedMotor(motor))
                {
                    continue;
                }

                motor.ApplyNetworkPose(new Vector3(x, y, z), motor.transform.rotation);
            }
        }
        finally
        {
            applyingRemotePath = false;
        }
    }

    private bool IsLocallyOwnedMotor(RtsUnitMotor motor)
    {
        if (motor == null)
        {
            return false;
        }

        TroopCombat troop = motor.GetComponent<TroopCombat>();
        if (troop == null)
        {
            return false;
        }

        TroopCombat.Faction owned = IsDefender
            ? TroopCombat.Faction.Enemy
            : TroopCombat.Faction.Friendly;
        return troop.TroopFaction == owned;
    }

    private RtsUnitMotor FindMotorBySyncKey(string syncKey)
    {
        if (string.IsNullOrEmpty(syncKey))
        {
            return null;
        }

        if (motorBySyncKey.TryGetValue(syncKey, out RtsUnitMotor cached) && cached != null)
        {
            return cached;
        }

        RtsUnitMotor[] motors = FindObjectsOfType<RtsUnitMotor>(true);
        for (int i = 0; i < motors.Length; i++)
        {
            RtsUnitMotor motor = motors[i];
            if (motor == null)
            {
                continue;
            }

            string key = SanitizeSyncKey(GetUnitSyncKey(motor.transform));
            motorBySyncKey[key] = motor;
            if (key == syncKey || motor.gameObject.name == syncKey)
            {
                return motor;
            }
        }

        return null;
    }

    private static string GetUnitSyncKey(Transform unit)
    {
        if (unit == null)
        {
            return string.Empty;
        }

        StringBuilder path = new StringBuilder(unit.name.Length * 2);
        Transform current = unit;
        while (current != null)
        {
            if (path.Length > 0)
            {
                path.Insert(0, '/');
            }

            path.Insert(0, current.name);
            current = current.parent;
        }

        return path.ToString();
    }

    private static string SanitizeSyncKey(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace("|", "/");
    }

    private void ApplyRemoteOutcome(bool attackerWon, string reason)
    {
        matchRunning = false;
        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager == null || !manager.IsPlaying)
        {
            return;
        }

        // Attacker win = cannons ready (FireCannonsAndWin). Attacker lose = cannons stopped (TriggerDefeat).
        if (attackerWon)
        {
            manager.FireCannonsAndWin();
        }
        else
        {
            manager.TriggerDefeat(string.IsNullOrWhiteSpace(reason) ? "The cannons were overrun." : reason);
        }
    }

    private void ReturnToMenuLocal(bool broadcast)
    {
        if (broadcast)
        {
            SendCommand("MENU");
        }

        CleanupPvpPresentation(teleportDefenderHome: true);
        ResetLobbyFlags();

        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager == null)
        {
            return;
        }

        if (manager.CurrentState != SiegeGameManager.MatchState.SelectingDifficulty)
        {
            suppressMenuBroadcast = true;
            try
            {
                manager.RestartMatch();
            }
            finally
            {
                suppressMenuBroadcast = false;
            }
        }
    }

    private void CleanupPvpPresentation(bool teleportDefenderHome)
    {
        StopCountdown();
        matchRunning = false;
        lobbyPhase = LobbyPhase.Idle;

        EnemyRegimentAI[] enemies = FindObjectsOfType<EnemyRegimentAI>(true);
        for (int i = 0; i < enemies.Length; i++)
        {
            if (enemies[i] != null)
            {
                enemies[i].ClearSiegePvpControl();
            }
        }

        if (teleportDefenderHome && IsDefender)
        {
            TeleportUser(menuSpawnPoint);
        }

        motorBySyncKey.Clear();
        syncMotorsByIndex.Clear();
        syncIndexByMotor.Clear();

        if (wandCommander != null)
        {
            wandCommander.SetControllableFaction(TroopCombat.Faction.Friendly);
        }

        // After cleanup / restart, still defer mode buttons until pointer is up.
        modeSelectUnlockTime = Mathf.Max(modeSelectUnlockTime, Time.unscaledTime + 0.35f);
        pendingShowModeSelectAfterRelease = true;
    }

    private void ResetLobbyFlags()
    {
        localSelected = false;
        peerSelected = false;
        localReady = false;
        peerReady = false;
        lobbyPhase = LobbyPhase.Idle;
        nextLobbyAnnounceTime = 0f;
    }

    private void TeleportUser(Transform destination)
    {
        if (destination == null)
        {
            return;
        }

        try
        {
            if (vGear.user != null)
            {
                vGear.user.Transform(destination);
                return;
            }
        }
        catch (Exception)
        {
        }

        Transform user = SiegePlayEnvironment.ResolveUserTransform();
        if (user != null)
        {
            user.SetPositionAndRotation(destination.position, destination.rotation);
        }
    }

    private void RefreshLobbyStatus()
    {
        if (lobbyPhase == LobbyPhase.Countdown || lobbyPhase == LobbyPhase.Playing)
        {
            return;
        }

        if (localReady)
        {
            if (peerReady && peerSelected)
            {
                ShowStatus("Both ready — starting as " + resolvedRole + "...");
            }
            else if (peerSelected)
            {
                ShowStatus(readyWaitingPrompt + " [" + resolvedRole + "]");
            }
            else
            {
                ShowStatus("Ready — waiting for opponent to select Siege PVP... [" + resolvedRole + "]");
            }

            return;
        }

        if (localSelected)
        {
            ShowStatus(peerSelected ? peerSelectedPrompt : selectedPrompt);
            return;
        }

        if (peerSelected)
        {
            ShowStatus(peerSelectedPrompt);
        }
    }

    private void ShowStatus(string message)
    {
        if (SiegeMatchUi.Instance != null)
        {
            SiegeMatchUi.Instance.SetStatusMessage(message);
        }
    }

    private void StopCountdown()
    {
        if (countdownCoroutine != null)
        {
            StopCoroutine(countdownCoroutine);
            countdownCoroutine = null;
        }
    }

    private void Send(string message)
    {
        if (networking == null || string.IsNullOrEmpty(message))
        {
            return;
        }

        if (logNetworkMessages && !message.StartsWith("SP|POSE", StringComparison.Ordinal))
        {
            Debug.Log(
                "SiegePvp send [" + networking.type + " id=" + networking.networkID
                + " token=" + localInstanceToken + "]: " + message,
                this);
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

        // withName MUST be false — otherwise Votanic prefixes "[Name]" and lobby cmds
        // never match if parsed with StartsWith("SP|"). Tutorial07 Send("Greeting") keeps defaults.
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
            catch (Exception sendEx)
            {
                Debug.LogWarning("SiegePvp Send failed: " + sendEx.Message, this);
            }
        }
    }

    private static bool TryParseFloat(string value, out float result)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace("|", "/");
    }
}
