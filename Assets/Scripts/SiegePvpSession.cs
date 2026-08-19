using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using Votanic.vNet.Networking;
using Votanic.vXR.vCast;
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

    private const int PoseSyncFlagHasPath = 1;
    private const int PoseSyncFlagPathHalted = 2;
    private const int PoseSyncFlagAdvancing = 4;
    private const int PoseSyncFlagRetreatInvulnerable = 8;

    private const int CombatStateActive = 0;
    private const int CombatStateRetreat = 1;
    private const int CombatStateDead = 2;
    private const int CombatStateRegroup = 3;

    private const int DamageSyncFlagSuppressCounter = 1;

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
    [Tooltip("Seconds after both players Ready before GO. Defender regiments use this time to exit the gate and reach formation points.")]
    [SerializeField, Min(0f)] private float preMatchCountdownSeconds = 10f;
    [Tooltip("Runtime cache. Filled from SiegeGameManager on play/select unless a PVP button has Use Custom Pvp Tuning.")]
    [SerializeField, Min(30f)] private float matchDurationSeconds = 180f;
    [SerializeField, Range(0.1f, 2f)] private float moveSpeedScale = 1f;
    [Tooltip("Owner is canon for their faction. Peer follows the same PATH locally; POSE only soft-corrects XZ drift.")]
    [SerializeField] private bool enablePoseCorrection = true;
    [Tooltip("How often owned regiment poses are broadcast. ~13 units max — keep this frequent to avoid mid-path teleports.")]
    [SerializeField, Min(0.04f)] private float poseSyncIntervalSeconds = 0.05f;
    [SerializeField, Range(8, 48)] private int maxSyncedPathPoints = 28;
    [Tooltip("Douglas-Peucker epsilon (m) when a drawn path must be shortened for the network.")]
    [SerializeField, Min(0.05f)] private float pathNetworkSimplifyEpsilon = 0.35f;
    [Tooltip("Hard-snap peer XZ when horizontal error exceeds this (meters). Keep low so corrections stay small.")]
    [SerializeField, Min(0.25f)] private float authoritySnapDistance = 1.15f;
    [Tooltip("When the owner stops on a drawn path, snap the peer if drift exceeds this (meters).")]
    [SerializeField, Min(0.25f)] private float pathHaltResyncDistance = 0.75f;
    [SerializeField, Range(4, 20)] private int maxPoseUnitsPerPacket = 14;

    [Header("Defender Formation (countdown)")]
    [Tooltip("Go-to points for the two PVP infantry regiments (non-ranged IncludeInSiegePvp). Assigned by sorted name. Only XZ is used — Y is ignored and units stay on ground.")]
    [SerializeField] private Transform[] defenderInfantryFormationPoints = new Transform[2];
    [Tooltip("Go-to points for the two PVP archer regiments (ranged IncludeInSiegePvp). Assigned by sorted name. Only XZ is used — Y is ignored and units stay on ground.")]
    [SerializeField] private Transform[] defenderArcherFormationPoints = new Transform[2];
    [SerializeField] private bool drawFormationFootprintGizmos = true;
    [SerializeField] private Color infantryFormationGizmoColor = new Color(0.25f, 0.8f, 1f, 0.9f);
    [SerializeField] private Color archerFormationGizmoColor = new Color(1f, 0.55f, 0.15f, 0.9f);
    [SerializeField] private Vector2 fallbackFormationFootprintSize = new Vector2(6f, 6f);

    [Header("Prompts")]
    [SerializeField] private string selectedPrompt = "Siege PVP selected. Press Ready when both players are connected.";
    [SerializeField] private string readyWaitingPrompt = "Ready — waiting for the other player... (press again after a moment to cancel)";
    [SerializeField] private string peerSelectedPrompt = "Opponent selected Siege PVP. Press Ready.";
    [SerializeField] private string countdownFormat = "Siege PVP starts in {0:0}...";

    [Header("Debug")]
    [SerializeField] private bool logNetworkMessages = true;
    [Tooltip("When enabled, pressing Ready starts PVP without a second machine (for editor/host-only tests). Off by default.")]
    [SerializeField] private bool allowSoloTesting = false;
    [SerializeField] private bool logDefenderTeleport = true;

    [Header("Remote Opponent Avatar")]
    [Tooltip("Body shown when looking at the Host (Attacker). Client machines use this for their remote opponent.")]
    [SerializeField] private GameObject hostOpponentBodyPrefab;
    [Tooltip("Body shown when looking at the Client (Defender). Host machines use this for their remote opponent.")]
    [SerializeField] private GameObject clientOpponentBodyPrefab;
    [SerializeField] private Vector3 remoteBodyLocalPosition = new Vector3(0f, -1.55f, 0f);
    [SerializeField] private Vector3 remoteBodyLocalEulerAngles = Vector3.zero;
    [SerializeField] private Vector3 remoteBodyLocalScale = Vector3.one;
    [Tooltip("When a body prefab is set, hide Votanic floating head/hand meshes (nametag stays).")]
    [SerializeField] private bool hideFloatingPartsWhenBodyAttached = true;

    private LocalRole resolvedRole = LocalRole.Attacker;
    private LobbyPhase lobbyPhase = LobbyPhase.Idle;
    private bool localSelected;
    private bool peerSelected;
    private bool localReady;
    private bool peerReady;
    private bool matchRunning;
    private Coroutine countdownCoroutine;
    private Coroutine defenderTeleportCoroutine;
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
    private float readyGraceUntil;
    private float lastPathNotifyTime = -1f;
    private int lastPathNotifyMotorId = int.MinValue;
    private readonly Dictionary<int, Vector3> lastBroadcastPoseByIndex = new Dictionary<int, Vector3>();
    private readonly Dictionary<int, Vector3> lastRemotePoseByIndex = new Dictionary<int, Vector3>();
    private readonly Dictionary<int, int> remotePoseStableCountByIndex = new Dictionary<int, int>();

    public static SiegePvpSession Instance { get; private set; }
    public LocalRole Role => resolvedRole;
    public bool IsAttacker => resolvedRole == LocalRole.Attacker;
    public bool IsDefender => resolvedRole == LocalRole.Defender;
    public bool IsMatchRunning => matchRunning;
    public bool IsInPvpLobby => lobbyPhase == LobbyPhase.SelectedWaitingReady
        || lobbyPhase == LobbyPhase.ReadyWaitingPeer
        || lobbyPhase == LobbyPhase.Countdown;
    /// <summary>
    /// City defender should stay off the command-tower volume during PVP and through the end-game UI
    /// until they explicitly return to the menu spawn.
    /// </summary>
    public bool ShouldKeepDefenderOffCommandTower()
    {
        if (!IsDefender)
        {
            return false;
        }

        if (IsInPvpLobby || IsMatchRunning)
        {
            return true;
        }

        return IsDefenderViewingPostMatchResults();
    }

    private bool IsDefenderViewingPostMatchResults()
    {
        if (lobbyPhase != LobbyPhase.Playing || matchRunning)
        {
            return false;
        }

        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager == null)
        {
            return false;
        }

        SiegeGameManager.MatchState state = manager.CurrentState;
        return state == SiegeGameManager.MatchState.Won
            || state == SiegeGameManager.MatchState.Lost;
    }

    public bool BlocksModeSelect => IsInPvpLobby
        || matchRunning
        || pendingShowModeSelectAfterRelease
        || Time.unscaledTime < modeSelectUnlockTime;

    /// <summary>Remote peer head transform (for cosmetic aim / avatar attach). Null if not connected.</summary>
    public Transform TryGetRemoteOpponentHead()
    {
        vGear_NetworkUser remote = TryGetRemoteNetworkUser();
        return remote != null ? remote.head : null;
    }

    public bool TryGetRemoteOpponentAimPosition(out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;
        Transform head = TryGetRemoteOpponentHead();
        if (head == null)
        {
            return false;
        }

        worldPosition = head.position;
        return IsFinite(worldPosition);
    }

    private vGear_NetworkUser TryGetRemoteNetworkUser()
    {
        if (networking == null)
        {
            return null;
        }

        try
        {
            foreach (vGear_NetworkUser user in networking.GetAllNetworkUsers())
            {
                if (user != null && user.userID != networking.networkID)
                {
                    return user;
                }
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }

    private void EnsureRemoteOpponentVisualsConfigured()
    {
        if (networking == null)
        {
            networking = FindObjectOfType<vGear_Networking>();
        }

        if (networking == null)
        {
            return;
        }

        SiegeRemoteOpponentVisuals visuals = networking.GetComponent<SiegeRemoteOpponentVisuals>();
        if (visuals == null)
        {
            visuals = networking.gameObject.AddComponent<SiegeRemoteOpponentVisuals>();
        }

        visuals.ConfigureFromSession(
            hostOpponentBodyPrefab,
            clientOpponentBodyPrefab,
            remoteBodyLocalPosition,
            remoteBodyLocalEulerAngles,
            remoteBodyLocalScale,
            hideFloatingPartsWhenBodyAttached);
    }

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
        EnsureRemoteOpponentVisualsConfigured();
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
        EnsureRemoteOpponentVisualsConfigured();
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

        // Cancel only after a short grace — accidental click right after Ready was
        // kicking the ready player back to mode select (and broadcasting CANCEL).
        if (lobbyPhase == LobbyPhase.ReadyWaitingPeer && Time.unscaledTime >= readyGraceUntil)
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
        bool forceAfterTimeout = Time.unscaledTime >= modeSelectUnlockTime + 0.75f;
        if (!released && !forceAfterTimeout)
        {
            return;
        }

        if (Time.unscaledTime < modeSelectUnlockTime && !forceAfterTimeout)
        {
            return;
        }

        pendingShowModeSelectAfterRelease = false;
        if (SiegeMatchUi.Instance != null && !IsInPvpLobby)
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
        ApplyCommandFactionFilter();
    }

    public void NotifyLocalReady()
    {
        if (!localSelected)
        {
            return;
        }

        localReady = true;
        lobbyPhase = LobbyPhase.ReadyWaitingPeer;
        readyGraceUntil = Time.unscaledTime + 1.25f;
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

        // Defer mode-select unlock until pointer release + short lockout, so the same
        // cancel click cannot immediately confirm Demo / Full / Dodge.
        modeSelectUnlockTime = Time.unscaledTime + 0.3f;
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
            if (matchRunning
                || lobbyPhase == LobbyPhase.Countdown
                || lobbyPhase == LobbyPhase.Playing)
            {
                CleanupPvpPresentation(teleportDefenderHome: true);
            }

            ApplyCommandFactionFilter();
            return;
        }

        if (!matchRunning)
        {
            return;
        }

        // Won (cannons fired) is attacker-timer driven — only attacker broadcasts.
        // Lost (cannon overrun) is often detected first on the defender's machine where
        // their units actually stand on the site — that side MUST broadcast or the peer
        // keeps playing for seconds.
        if (state == SiegeGameManager.MatchState.Won)
        {
            if (!IsAttacker)
            {
                return;
            }

            matchRunning = false;
            SendCommand("FX", "FIRE");
            SendCommand("WIN", "attacker");
            PlayLocalFx("FIRE");
        }
        else if (state == SiegeGameManager.MatchState.Lost)
        {
            matchRunning = false;
            if (IsDefender)
            {
                ReleaseCommandTowerBoundaryForDefender();
            }

            string reason = SiegeGameManager.Instance != null
                ? SiegeGameManager.Instance.DefeatReason
                : "The cannons were overrun.";
            // Fall / arrow deaths are not cannon overrun — skip OVERRUN FX for those.
            bool isOverrun = reason.IndexOf("occupied", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("cannon", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isOverrun)
            {
                SendCommand("FX", "OVERRUN");
                PlayLocalFx("OVERRUN");
            }

            SendCommand("LOSE", Sanitize(reason));
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

        bool inPvpFlow = SiegeMatchSettings.IsSiegePvpMode
            || lobbyPhase != LobbyPhase.Idle;
        if (!inPvpFlow)
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
                // Never treat a late peer SELECT as a reason to leave Ready — only CANCEL does.
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
                if (!IsPeerLobbyMessageAllowed())
                {
                    break;
                }

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
            case "STOP":
                HandleRemoteStop(parts, p);
                break;
            case "POSE":
                HandleRemotePose(parts, p);
                break;
            case "DMG":
                HandleRemoteDamage(parts, p);
                break;
            case "VOLLEY":
                HandleRemoteVolley(parts, p);
                break;
            case "HAZARD":
                HandleRemoteCastleHazard(parts, p);
                break;
            case "FX":
                HandleRemoteFx(parts, p);
                break;
            case "WIN":
                ApplyRemoteOutcome(attackerWon: true, reason: string.Empty);
                break;
            case "LOSE":
                ApplyRemoteOutcome(attackerWon: false, reason: JoinLoseReason(parts, p));
                break;
            case "MENU":
                ReturnToMenuLocal(broadcast: false);
                break;
        }
    }

    private bool IsPeerLobbyMessageAllowed()
    {
        return localSelected
            && (lobbyPhase == LobbyPhase.SelectedWaitingReady
                || lobbyPhase == LobbyPhase.ReadyWaitingPeer);
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
            StopDefenderTeleportRoutine();
            defenderTeleportCoroutine = StartCoroutine(TeleportDefenderToSpawnWhenReady());
        }

        if (SiegeMatchUi.Instance != null)
        {
            SiegeMatchUi.Instance.HideDifficultyOptionsForPvpLobby();
            SiegeMatchUi.Instance.ApplyPvpRoleHud(IsDefender);
        }

        PreparePvpModeForCountdown();
        BeginDefenderGateExitDuringCountdown();

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

    private void PreparePvpModeForCountdown()
    {
        SiegeGameManager manager = SiegeGameManager.Instance;
        matchDurationSeconds = Mathf.Max(30f, matchDurationSeconds);
        moveSpeedScale = Mathf.Clamp(moveSpeedScale, 0.1f, 2f);
        SiegeMatchSettings.Configure(
            SiegeGameMode.SiegePvp,
            manager != null ? manager.DemoMoveSpeedScale : 0.6f,
            matchDurationSeconds,
            moveSpeedScale);
        ApplyCommandFactionFilter();
        ApplyMoveSpeedToAllTroops();
        ConfigureCityDefenderRegimentsForPvp();
    }

    private void BeginDefenderGateExitDuringCountdown()
    {
        if (EnemyWaveController.Instance == null)
        {
            return;
        }

        EnemyWaveController.Instance.DeployAllForSiegePvp();
        AssignDefenderFormationWaypoints();

        if (logNetworkMessages)
        {
            Debug.Log("SiegePvp defender regiments exiting gate toward formation points during countdown.", this);
        }
    }

    /// <summary>
    /// Map IncludeInSiegePvp regiments to infantry/archer formation Transforms (sorted by name for host/client parity).
    /// </summary>
    private void AssignDefenderFormationWaypoints()
    {
        List<EnemyRegimentAI> infantry = new List<EnemyRegimentAI>(2);
        List<EnemyRegimentAI> archers = new List<EnemyRegimentAI>(2);

        EnemyRegimentAI[] enemies = FindObjectsOfType<EnemyRegimentAI>(true);
        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyRegimentAI regiment = enemies[i];
            if (regiment == null || !regiment.IncludeInSiegePvp || !regiment.isActiveAndEnabled)
            {
                continue;
            }

            TroopCombat combat = regiment.GetComponent<TroopCombat>();
            if (combat == null || combat.TroopFaction != TroopCombat.Faction.Enemy)
            {
                continue;
            }

            if (combat.HasRangedAttack)
            {
                archers.Add(regiment);
            }
            else
            {
                infantry.Add(regiment);
            }
        }

        infantry.Sort(CompareRegimentName);
        archers.Sort(CompareRegimentName);

        AssignFormationPoints(infantry, defenderInfantryFormationPoints, "infantry");
        AssignFormationPoints(archers, defenderArcherFormationPoints, "archer");
    }

    private void AssignFormationPoints(
        List<EnemyRegimentAI> regiments,
        Transform[] points,
        string label)
    {
        if (regiments == null || regiments.Count == 0)
        {
            return;
        }

        int pointCount = points != null ? points.Length : 0;
        for (int i = 0; i < regiments.Count; i++)
        {
            Transform point = i < pointCount ? points[i] : null;
            if (point == null)
            {
                if (logNetworkMessages)
                {
                    Debug.LogWarning(
                        "SiegePvp missing " + label + " formation point [" + i + "] for '"
                        + regiments[i].name + "'.",
                        this);
                }

                continue;
            }

            regiments[i].SetSiegePvpFormationDestination(FlattenWaypointXZ(point.position));
            if (logNetworkMessages)
            {
                Debug.Log(
                    "SiegePvp " + label + " '" + regiments[i].name + "' -> formation '"
                    + point.name + "'.",
                    this);
            }
        }
    }

    private static Vector3 FlattenWaypointXZ(Vector3 worldPoint)
    {
        return new Vector3(worldPoint.x, 0f, worldPoint.z);
    }

    private static int CompareRegimentName(EnemyRegimentAI a, EnemyRegimentAI b)
    {
        string nameA = a != null ? a.name : string.Empty;
        string nameB = b != null ? b.name : string.Empty;
        return string.CompareOrdinal(nameA, nameB);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!drawFormationFootprintGizmos)
        {
            return;
        }

        DrawFormationFootprintGizmos();
    }

    private void DrawFormationFootprintGizmos()
    {
        CollectPvpDefenderRegiments(out List<EnemyRegimentAI> infantry, out List<EnemyRegimentAI> archers);
        DrawFormationGroupGizmos(infantry, defenderInfantryFormationPoints, infantryFormationGizmoColor, "Inf");
        DrawFormationGroupGizmos(archers, defenderArcherFormationPoints, archerFormationGizmoColor, "Arch");
    }

    private void DrawFormationGroupGizmos(
        List<EnemyRegimentAI> regiments,
        Transform[] points,
        Color color,
        string labelPrefix)
    {
        if (points == null)
        {
            return;
        }

        for (int i = 0; i < points.Length; i++)
        {
            Transform point = points[i];
            if (point == null)
            {
                continue;
            }

            Vector2 size = fallbackFormationFootprintSize;
            string label = labelPrefix + " " + (i + 1);
            if (regiments != null && i < regiments.Count && regiments[i] != null)
            {
                TroopCombat combat = regiments[i].GetComponent<TroopCombat>();
                if (combat != null && combat.TryGetFootprintSizeXZ(out Vector2 footprintSize))
                {
                    size = footprintSize;
                }

                label += "\n" + regiments[i].name;
            }

            Vector3 center = point.position;
            DrawFootprintSquareGizmo(center, size, color);
            UnityEditor.Handles.color = color;
            UnityEditor.Handles.Label(center + Vector3.up * 0.35f, label);
        }
    }

    private static void DrawFootprintSquareGizmo(Vector3 center, Vector2 sizeXZ, Color color)
    {
        float halfX = Mathf.Max(0.25f, sizeXZ.x) * 0.5f;
        float halfZ = Mathf.Max(0.25f, sizeXZ.y) * 0.5f;
        float y = center.y;

        Vector3 bl = new Vector3(center.x - halfX, y, center.z - halfZ);
        Vector3 br = new Vector3(center.x + halfX, y, center.z - halfZ);
        Vector3 tr = new Vector3(center.x + halfX, y, center.z + halfZ);
        Vector3 tl = new Vector3(center.x - halfX, y, center.z + halfZ);

        Gizmos.color = color;
        Gizmos.DrawLine(bl, br);
        Gizmos.DrawLine(br, tr);
        Gizmos.DrawLine(tr, tl);
        Gizmos.DrawLine(tl, bl);

        // Thin filled pad so the occupied area reads clearly from above.
        Color fill = color;
        fill.a = Mathf.Clamp01(color.a * 0.18f);
        Gizmos.color = fill;
        Gizmos.DrawCube(new Vector3(center.x, y, center.z), new Vector3(halfX * 2f, 0.05f, halfZ * 2f));
    }

    private void CollectPvpDefenderRegiments(out List<EnemyRegimentAI> infantry, out List<EnemyRegimentAI> archers)
    {
        infantry = new List<EnemyRegimentAI>(2);
        archers = new List<EnemyRegimentAI>(2);

        EnemyRegimentAI[] enemies = FindObjectsOfType<EnemyRegimentAI>();
        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyRegimentAI regiment = enemies[i];
            if (regiment == null || !regiment.IncludeInSiegePvp)
            {
                continue;
            }

            TroopCombat combat = regiment.GetComponent<TroopCombat>();
            if (combat == null || combat.TroopFaction != TroopCombat.Faction.Enemy)
            {
                continue;
            }

            if (combat.HasRangedAttack)
            {
                archers.Add(regiment);
            }
            else
            {
                infantry.Add(regiment);
            }
        }

        infantry.Sort(CompareRegimentName);
        archers.Sort(CompareRegimentName);
    }
#endif

    private void BeginGameplay()
    {
        StopCountdown();
        lobbyPhase = LobbyPhase.Playing;
        matchRunning = true;
        nextPoseSyncTime = 0f;
        lastBroadcastPoseByIndex.Clear();
        lastRemotePoseByIndex.Clear();
        remotePoseStableCountByIndex.Clear();
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
        ApplyNetworkAuthorityRoles();
        EnsureWandBound();
        ApplyLocalHudViewpoint();

        if (logNetworkMessages)
        {
            Debug.Log(
                "SiegePvp gameplay as " + resolvedRole
                + " with " + syncMotorsByIndex.Count + " sync units.",
                this);
        }
    }

    private void ApplyNetworkAuthorityRoles()
    {
        for (int i = 0; i < syncMotorsByIndex.Count; i++)
        {
            RtsUnitMotor motor = syncMotorsByIndex[i];
            if (motor == null)
            {
                continue;
            }

            bool locallyOwned = IsLocallyOwnedMotor(motor);
            motor.SuppressLocalSimulation = !locallyOwned;

            TroopCombat troop = motor.GetComponent<TroopCombat>();
            if (troop != null && !locallyOwned)
            {
                troop.SetNetworkOwnerPoseHalted(!motor.HasActivePath && !motor.HasDestination);
            }
        }
    }

    private void ClearNetworkAuthorityRoles()
    {
        UnbindMotorSyncEvents();
        lastBroadcastPoseByIndex.Clear();
        lastRemotePoseByIndex.Clear();
        remotePoseStableCountByIndex.Clear();

        for (int i = 0; i < syncMotorsByIndex.Count; i++)
        {
            if (syncMotorsByIndex[i] != null)
            {
                syncMotorsByIndex[i].SuppressLocalSimulation = false;
            }
        }

        RtsUnitMotor[] all = FindObjectsOfType<RtsUnitMotor>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null)
            {
                all[i].SuppressLocalSimulation = false;
            }
        }
    }

    private void ApplyLocalHudViewpoint()
    {
        if (SiegeMatchUi.Instance == null)
        {
            return;
        }

        SiegeMatchUi.Instance.ApplyPvpRoleHud(IsDefender);
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

    /// <summary>
    /// Click-to-stop (no path drawn) — peer must stop the same unit.
    /// </summary>
    public void NotifyStopFromCommander(RtsUnitMotor motor)
    {
        if (!matchRunning || applyingRemotePath || motor == null)
        {
            return;
        }

        int syncIndex = ResolveSyncIndex(motor);
        if (syncIndex < 0)
        {
            return;
        }

        string token = string.IsNullOrEmpty(localInstanceToken) ? "local" : localInstanceToken;
        Send(string.Format(
            CultureInfo.InvariantCulture,
            "SP|STOP|{0}|{1}",
            token,
            syncIndex));

        BroadcastOwnedMotorPoseImmediate(motor, syncIndex, forceHaltedFlag: true);

        if (logNetworkMessages)
        {
            Debug.Log("SiegePvp STOP send unit#" + syncIndex + " '" + motor.name + "'", this);
        }
    }

    private void RebuildUnitSyncTable()
    {
        UnbindMotorSyncEvents();
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

        BindMotorSyncEvents();
    }

    private void BindMotorSyncEvents()
    {
        for (int i = 0; i < syncMotorsByIndex.Count; i++)
        {
            RtsUnitMotor motor = syncMotorsByIndex[i];
            if (motor == null)
            {
                continue;
            }

            motor.PathFollowingAborted -= HandleOwnedPathFollowingAborted;
            motor.PathFollowingAborted += HandleOwnedPathFollowingAborted;
        }
    }

    private void UnbindMotorSyncEvents()
    {
        for (int i = 0; i < syncMotorsByIndex.Count; i++)
        {
            RtsUnitMotor motor = syncMotorsByIndex[i];
            if (motor != null)
            {
                motor.PathFollowingAborted -= HandleOwnedPathFollowingAborted;
            }
        }
    }

    private void HandleOwnedPathFollowingAborted(RtsUnitMotor motor)
    {
        if (!matchRunning || applyingRemotePath || motor == null || !IsLocallyOwnedMotor(motor))
        {
            return;
        }

        int syncIndex = ResolveSyncIndex(motor);
        if (syncIndex < 0)
        {
            return;
        }

        BroadcastOwnedStop(syncIndex, motor, reason: "path-aborted");
        BroadcastOwnedMotorPoseImmediate(motor, syncIndex, forceHaltedFlag: true);
    }

    private void BroadcastOwnedStop(int syncIndex, RtsUnitMotor motor, string reason)
    {
        string token = string.IsNullOrEmpty(localInstanceToken) ? "local" : localInstanceToken;
        Send(string.Format(
            CultureInfo.InvariantCulture,
            "SP|STOP|{0}|{1}",
            token,
            syncIndex));

        if (logNetworkMessages)
        {
            Debug.Log(
                "SiegePvp STOP send (" + reason + ") unit#" + syncIndex + " '" + motor.name + "'",
                this);
        }
    }

    private void BroadcastOwnedMotorPoseImmediate(RtsUnitMotor motor, int syncIndex, bool forceHaltedFlag)
    {
        if (motor == null)
        {
            return;
        }

        TroopCombat troop = motor.GetComponent<TroopCombat>();
        Vector3 position = motor.transform.position;
        int syncFlags = BuildPoseSyncFlags(motor, syncIndex, position, forceHaltedFlag);
        int stateCode = EncodeCombatStateCode(troop);
        float hp = troop != null ? troop.CurrentHealth : 0f;
        string token = string.IsNullOrEmpty(localInstanceToken) ? "local" : localInstanceToken;

        Send(string.Format(
            CultureInfo.InvariantCulture,
            "SP|POSE|{0}|{1},{2:0.###},{3:0.###},{4:0.###},{5:0.##},{6},{7}",
            token,
            syncIndex,
            position.x,
            position.y,
            position.z,
            hp,
            stateCode,
            syncFlags));

        lastBroadcastPoseByIndex[syncIndex] = position;
    }

    private int BuildPoseSyncFlags(RtsUnitMotor motor, int syncIndex, Vector3 position, bool forceHaltedFlag)
    {
        int flags = 0;
        bool hasPath = motor.HasActivePath || motor.HasDestination;
        bool halted = forceHaltedFlag || !hasPath;

        if (hasPath && !halted)
        {
            flags |= PoseSyncFlagHasPath;
        }

        if (halted)
        {
            flags |= PoseSyncFlagPathHalted;
        }

        TroopCombat troop = motor.GetComponent<TroopCombat>();
        if (troop != null && troop.IsRetreatInvulnerable)
        {
            flags |= PoseSyncFlagRetreatInvulnerable;
        }

        if (!halted)
        {
            if (lastBroadcastPoseByIndex.TryGetValue(syncIndex, out Vector3 previous))
            {
                Vector3 delta = position - previous;
                delta.y = 0f;
                if (delta.sqrMagnitude >= 0.0225f) // ~0.15m
                {
                    flags |= PoseSyncFlagAdvancing;
                }
            }
            else
            {
                flags |= PoseSyncFlagAdvancing;
            }
        }

        return flags;
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

        List<Vector3> compressed = CanonicalizePathForMatch(path);
        if (compressed.Count < 2)
        {
            return;
        }
        StringBuilder builder = new StringBuilder(64 + compressed.Count * 24);
        string token = string.IsNullOrEmpty(localInstanceToken) ? "local" : localInstanceToken;
        builder.Append("SP|PATH|").Append(token).Append('|').Append(syncIndex);
        for (int i = 0; i < compressed.Count; i++)
        {
            Vector3 point = compressed[i];
            builder.Append('|')
                .Append(point.x.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(point.y.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(point.z.ToString("0.###", CultureInfo.InvariantCulture));
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

    private void TryBroadcastOwnedUnitPoses()
    {
        if (!enablePoseCorrection || Time.time < nextPoseSyncTime)
        {
            return;
        }

        nextPoseSyncTime = Time.time + Mathf.Max(0.04f, poseSyncIntervalSeconds);

        TroopCombat.Faction ownedFaction = IsDefender
            ? TroopCombat.Faction.Enemy
            : TroopCombat.Faction.Friendly;

        if (syncMotorsByIndex.Count == 0)
        {
            RebuildUnitSyncTable();
        }

        string token = string.IsNullOrEmpty(localInstanceToken) ? "local" : localInstanceToken;
        int packetCap = Mathf.Max(4, maxPoseUnitsPerPacket);

        // Collect owned motors: in combat first, then movers, then idle.
        List<int> ordered = new List<int>(syncMotorsByIndex.Count);
        for (int pass = 0; pass < 3; pass++)
        {
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

                bool inCombat = troop.CurrentState == TroopCombat.State.Fight
                    || troop.IsRetreating
                    || troop.CurrentState == TroopCombat.State.Regroup
                    || troop.CurrentState == TroopCombat.State.Dead;
                bool moving = motor.HasActivePath || motor.HasDestination;

                bool include = pass switch
                {
                    0 => inCombat,
                    1 => !inCombat && moving,
                    _ => !inCombat && !moving
                };

                if (!include || ordered.Contains(i))
                {
                    continue;
                }

                ordered.Add(i);
            }
        }

        // Multiple packets so every owned unit is corrected every interval (no silent drop).
        for (int start = 0; start < ordered.Count; start += packetCap)
        {
            StringBuilder builder = new StringBuilder(128 + packetCap * 28);
            builder.Append("SP|POSE|").Append(token);
            int end = Mathf.Min(start + packetCap, ordered.Count);
            for (int n = start; n < end; n++)
            {
                int i = ordered[n];
                RtsUnitMotor motor = syncMotorsByIndex[i];
                TroopCombat troop = motor != null ? motor.GetComponent<TroopCombat>() : null;
                Vector3 p = motor.transform.position;
                int stateCode = EncodeCombatStateCode(troop);
                float hp = troop != null ? troop.CurrentHealth : 0f;
                int syncFlags = BuildPoseSyncFlags(motor, i, p, forceHaltedFlag: false);
                builder.Append('|').Append(i).Append(',')
                    .Append(p.x.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                    .Append(p.y.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                    .Append(p.z.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                    .Append(hp.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                    .Append(stateCode.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(syncFlags.ToString(CultureInfo.InvariantCulture));
                lastBroadcastPoseByIndex[i] = p;
            }

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

        if (IsLocallyOwnedMotor(motor))
        {
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
            TroopCombat troop = motor.GetComponent<TroopCombat>();
            if (troop != null)
            {
                troop.SetNetworkOwnerPoseHalted(false);
            }

            motor.SuppressLocalSimulation = false;
            Vector3 pathStart = path[0];
            Vector3 currentPos = motor.transform.position;
            Vector3 startDelta = pathStart - currentPos;
            startDelta.y = 0f;
            if (startDelta.sqrMagnitude > 1f)
            {
                motor.ApplyNetworkPose(
                    pathStart,
                    motor.transform.rotation,
                    forceAuthority: true,
                    authoritySnapDistance: Mathf.Min(authoritySnapDistance, 1.25f));
            }

            motor.FollowPath(path);

            if (troop != null)
            {
                troop.NotifyNetworkPositionApplied(hardSnap: false);
            }

            if (logNetworkMessages)
            {
                Debug.Log(
                    "SiegePvp remote PATH '" + idPart + "' -> " + motor.name
                    + " (pts=" + path.Count + ").",
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
        // SP|POSE|token|index,x,y,z[,hp,state[,syncFlags]]|...
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

                Vector3 remotePos = new Vector3(x, y, z);
                TroopCombat troop = motor.GetComponent<TroopCombat>();

                float authorityHealth = 0f;
                int authorityState = 0;
                int syncFlags = 0;

                bool hasAuthority = packed.Length >= 6
                    && TryParseFloat(packed[4], out authorityHealth)
                    && int.TryParse(packed[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out authorityState);

                if (packed.Length >= 7)
                {
                    int.TryParse(packed[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out syncFlags);
                }

                bool ownerHasPath = (syncFlags & PoseSyncFlagHasPath) != 0;
                bool ownerHalted = (syncFlags & PoseSyncFlagPathHalted) != 0;
                bool ownerAdvancing = (syncFlags & PoseSyncFlagAdvancing) != 0;
                bool ownerRetreatInvulnerable = (syncFlags & PoseSyncFlagRetreatInvulnerable) != 0;

                if (troop != null)
                {
                    troop.SetNetworkOwnerPoseHalted(ownerHalted);
                }

                motor.SuppressLocalSimulation = ownerHalted;

                if (ownerHalted)
                {
                    if (motor.HasActivePath || motor.HasDestination)
                    {
                        motor.Stop();
                    }

                    motor.SnapNetworkPosition(remotePos);

                    if (hasAuthority && troop != null)
                    {
                        troop.ApplyNetworkCombatAuthority(
                            authorityHealth,
                            authorityState,
                            remotePos,
                            authoritySnapDistance,
                            ownerRetreatInvulnerable);
                    }
                    else if (troop != null)
                    {
                        troop.NotifyNetworkPositionApplied(hardSnap: true);
                    }

                    continue;
                }

                motor.SuppressLocalSimulation = false;

                Vector3 currentPos = motor.transform.position;
                Vector3 delta = remotePos - currentPos;
                delta.y = 0f;
                float horizontalErrorSqr = delta.sqrMagnitude;
                float haltResyncSqr = pathHaltResyncDistance * pathHaltResyncDistance;

                UpdateRemotePoseStability(syncIndex, remotePos, ownerAdvancing, ownerHasPath);

                bool ownerStoppedFollowingPath = ownerHalted
                    || (!ownerHasPath && (!ownerAdvancing || GetRemotePoseStableCount(syncIndex) >= 3));
                bool peerDriftedWhileOwnerStopped = ownerStoppedFollowingPath
                    && (motor.HasActivePath || motor.HasDestination)
                    && horizontalErrorSqr >= haltResyncSqr;

                // Peer frozen / lagging while owner keeps advancing — correct early with soft
                // blends; only hard-snap when error is already large (avoids mid-path teleports).
                float peerSoftCatchUpSqr = 0.35f * 0.35f;
                float peerHardCatchUpSqr = Mathf.Max(
                    authoritySnapDistance * authoritySnapDistance,
                    haltResyncSqr);
                bool peerLaggingBehindMovingOwner = ownerHasPath
                    && ownerAdvancing
                    && horizontalErrorSqr >= peerSoftCatchUpSqr;

                if (peerDriftedWhileOwnerStopped)
                {
                    if (motor.HasActivePath || motor.HasDestination)
                    {
                        motor.Stop();
                    }

                    motor.SuppressLocalSimulation = true;
                    if (troop != null)
                    {
                        troop.SetNetworkOwnerPoseHalted(true);
                    }

                    motor.SnapNetworkPosition(remotePos);

                    if (hasAuthority && troop != null)
                    {
                        troop.ApplyNetworkCombatAuthority(
                            authorityHealth,
                            authorityState,
                            remotePos,
                            authoritySnapDistance,
                            ownerRetreatInvulnerable);
                    }
                    else if (troop != null)
                    {
                        troop.NotifyNetworkPositionApplied(hardSnap: true);
                    }

                    continue;
                }

                if (peerLaggingBehindMovingOwner)
                {
                    if (horizontalErrorSqr >= peerHardCatchUpSqr)
                    {
                        motor.SnapNetworkPosition(remotePos);
                    }
                    else
                    {
                        motor.ApplyNetworkPose(
                            remotePos,
                            motor.transform.rotation,
                            forceAuthority: true,
                            authoritySnapDistance: Mathf.Min(authoritySnapDistance, 0.85f));
                    }

                    if (hasAuthority && troop != null)
                    {
                        troop.ApplyNetworkCombatAuthority(
                            authorityHealth,
                            authorityState,
                            remotePos,
                            authoritySnapDistance,
                            ownerRetreatInvulnerable);
                    }
                    else if (troop != null)
                    {
                        troop.NotifyNetworkPositionApplied(hardSnap: horizontalErrorSqr >= peerHardCatchUpSqr);
                    }

                    continue;
                }

                if (hasAuthority && troop != null)
                {
                    troop.ApplyNetworkCombatAuthority(
                        authorityHealth,
                        authorityState,
                        remotePos,
                        authoritySnapDistance,
                        ownerRetreatInvulnerable);
                    continue;
                }

                motor.ApplyNetworkPose(
                    remotePos,
                    motor.transform.rotation,
                    forceAuthority: true,
                    authoritySnapDistance: authoritySnapDistance);

                if (troop != null)
                {
                    troop.NotifyNetworkPositionApplied(hardSnap: false);
                }
            }
        }
        finally
        {
            applyingRemotePath = false;
        }
    }

    private void UpdateRemotePoseStability(
        int syncIndex,
        Vector3 remotePos,
        bool ownerAdvancing,
        bool ownerHasPath)
    {
        if (!lastRemotePoseByIndex.TryGetValue(syncIndex, out Vector3 previous))
        {
            lastRemotePoseByIndex[syncIndex] = remotePos;
            remotePoseStableCountByIndex[syncIndex] = 0;
            return;
        }

        if (ownerHasPath)
        {
            remotePoseStableCountByIndex[syncIndex] = 0;
            lastRemotePoseByIndex[syncIndex] = remotePos;
            return;
        }

        Vector3 delta = remotePos - previous;
        delta.y = 0f;
        if (!ownerAdvancing || delta.sqrMagnitude < 0.0225f)
        {
            remotePoseStableCountByIndex[syncIndex] = GetRemotePoseStableCount(syncIndex) + 1;
        }
        else
        {
            remotePoseStableCountByIndex[syncIndex] = 0;
        }

        lastRemotePoseByIndex[syncIndex] = remotePos;
    }

    private int GetRemotePoseStableCount(int syncIndex)
    {
        return remotePoseStableCountByIndex.TryGetValue(syncIndex, out int count) ? count : 0;
    }

    private static int EncodeCombatStateCode(TroopCombat troop)
    {
        if (troop == null)
        {
            return CombatStateActive;
        }

        if (troop.CurrentState == TroopCombat.State.Dead || troop.IsPermanentlyEliminated)
        {
            return CombatStateDead;
        }

        if (troop.CurrentState == TroopCombat.State.Regroup)
        {
            return CombatStateRegroup;
        }

        if (troop.IsRetreating)
        {
            return CombatStateRetreat;
        }

        return CombatStateActive;
    }

    private void HandleRemoteStop(string[] parts, int payloadStart)
    {
        if (parts.Length <= payloadStart)
        {
            return;
        }

        if (!int.TryParse(parts[payloadStart], NumberStyles.Integer, CultureInfo.InvariantCulture, out int syncIndex))
        {
            return;
        }

        RtsUnitMotor motor = FindMotorBySyncIndex(syncIndex);
        if (motor == null || IsLocallyOwnedMotor(motor))
        {
            return;
        }

        applyingRemotePath = true;
        try
        {
            motor.Stop();
            motor.SuppressLocalSimulation = true;
            TroopCombat troop = motor.GetComponent<TroopCombat>();
            if (troop != null)
            {
                troop.SetNetworkOwnerPoseHalted(true);
            }

            if (logNetworkMessages)
            {
                Debug.Log("SiegePvp applied remote STOP unit#" + syncIndex + " -> " + motor.name, this);
            }
        }
        finally
        {
            applyingRemotePath = false;
        }
    }

    private void HandleRemoteFx(string[] parts, int payloadStart)
    {
        string fx = parts.Length > payloadStart ? parts[payloadStart] : string.Empty;
        PlayLocalFx(fx);
    }

    private static void PlayLocalFx(string fx)
    {
        if (string.IsNullOrEmpty(fx))
        {
            return;
        }

        SiegeRevealChildren[] reveals = FindObjectsOfType<SiegeRevealChildren>(true);
        for (int i = 0; i < reveals.Length; i++)
        {
            SiegeRevealChildren reveal = reveals[i];
            if (reveal == null)
            {
                continue;
            }

            if (string.Equals(fx, "FIRE", StringComparison.OrdinalIgnoreCase))
            {
                reveal.RevealCannonFireEffects();
            }
            else if (string.Equals(fx, "OVERRUN", StringComparison.OrdinalIgnoreCase))
            {
                reveal.RevealCannonOverrunEffects();
            }
        }
    }

    public bool IsLocallyOwnedMotor(RtsUnitMotor motor)
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

    /// <summary>True when this machine is the network authority for the regiment.</summary>
    public bool IsLocallyOwnedTroop(TroopCombat troop)
    {
        if (troop == null)
        {
            return false;
        }

        RtsUnitMotor unitMotor = troop.GetComponent<RtsUnitMotor>();
        return unitMotor != null && IsLocallyOwnedMotor(unitMotor);
    }

    /// <summary>
    /// PVP: owner and peer must simulate the same waypoint list (not a richer local-only path).
    /// </summary>
    public List<Vector3> CanonicalizePathForMatch(IReadOnlyList<Vector3> path)
    {
        if (path == null || path.Count == 0)
        {
            return new List<Vector3>();
        }

        List<Vector3> sanitized = RtsPathUtility.SanitizeDrawnPath(path);
        sanitized = RtsPathUtility.RemoveShortSegments(sanitized, pathNetworkSimplifyEpsilon);
        return RtsPathUtility.PreparePathForNetworkSync(
            sanitized,
            Mathf.Max(2, maxSyncedPathPoints),
            pathNetworkSimplifyEpsilon);
    }

    /// <summary>
    /// Owner retreat issues a direct MoveTo — mirror it as a short PATH so peers simulate at full speed.
    /// </summary>
    public void NotifyRetreatDestinationFromAuthority(RtsUnitMotor motor, Vector3 destination)
    {
        if (!matchRunning || applyingRemotePath || motor == null || !IsLocallyOwnedMotor(motor))
        {
            return;
        }

        Vector3 start = motor.transform.position;
        List<Vector3> path = new List<Vector3>(2) { start, destination };
        HandleLocalPathCommand(motor, path);
    }

    /// <summary>Push HP/retreat/death immediately after local combat resolves on the authority machine.</summary>
    public void NotifyOwnedTroopCombatChanged(TroopCombat troop)
    {
        if (!matchRunning || troop == null || !IsLocallyOwnedTroop(troop))
        {
            return;
        }

        RtsUnitMotor motor = troop.GetComponent<RtsUnitMotor>();
        if (motor == null)
        {
            return;
        }

        int syncIndex = ResolveSyncIndex(motor);
        if (syncIndex < 0)
        {
            return;
        }

        BroadcastOwnedMotorPoseImmediate(motor, syncIndex, forceHaltedFlag: false);
    }

    /// <summary>
    /// Owned attacker hit a peer-owned victim — apply damage on the victim owner's machine.
    /// </summary>
    public void NotifyInflictedDamage(
        TroopCombat attacker,
        TroopCombat victim,
        float amount,
        bool isRangedAttack,
        bool suppressCounterReply = false)
    {
        if (!matchRunning || victim == null || amount <= 0f)
        {
            return;
        }

        // Counter-strikes must still send while applying remote combat/path updates.
        if (applyingRemotePath && !suppressCounterReply)
        {
            return;
        }

        if (IsLocallyOwnedTroop(victim))
        {
            victim.TakeDamage(amount, attacker, isRangedAttack);
            return;
        }

        RtsUnitMotor victimMotor = victim.GetComponent<RtsUnitMotor>();
        int victimIndex = ResolveSyncIndex(victimMotor);
        if (victimIndex < 0)
        {
            return;
        }

        int attackerIndex = -1;
        if (attacker != null)
        {
            RtsUnitMotor attackerMotor = attacker.GetComponent<RtsUnitMotor>();
            if (attackerMotor != null)
            {
                attackerIndex = ResolveSyncIndex(attackerMotor);
            }
        }

        int dmgFlags = suppressCounterReply ? DamageSyncFlagSuppressCounter : 0;
        string token = string.IsNullOrEmpty(localInstanceToken) ? "local" : localInstanceToken;
        Send(string.Format(
            CultureInfo.InvariantCulture,
            "SP|DMG|{0}|{1}|{2:0.##}|{3}|{4}|{5}",
            token,
            victimIndex,
            amount,
            isRangedAttack ? 1 : 0,
            attackerIndex,
            dmgFlags));

        if (logNetworkMessages)
        {
            Debug.Log(
                "SiegePvp DMG send victim#" + victimIndex
                + " amount=" + amount.ToString("0.##", CultureInfo.InvariantCulture)
                + " ranged=" + isRangedAttack,
                this);
        }
    }

    public void ClearLocalAggressorsTargeting(TroopCombat target)
    {
        if (!matchRunning || target == null)
        {
            return;
        }

        TroopCombat.Faction ownedFaction = IsDefender
            ? TroopCombat.Faction.Enemy
            : TroopCombat.Faction.Friendly;

        for (int i = 0; i < syncMotorsByIndex.Count; i++)
        {
            RtsUnitMotor motor = syncMotorsByIndex[i];
            if (motor == null)
            {
                continue;
            }

            TroopCombat troop = motor.GetComponent<TroopCombat>();
            if (troop == null
                || troop.TroopFaction != ownedFaction
                || troop.CurrentTarget != target)
            {
                continue;
            }

            troop.ClearCurrentTarget();
        }
    }

    /// <summary>
    /// Owner fired a ranged volley — peer should spawn the same arrow FX (damage is separate via DMG).
    /// </summary>
    public void NotifyRangedVolley(TroopCombat attacker, TroopCombat victim, int arrowCount)
    {
        if (!matchRunning || attacker == null || victim == null || !IsLocallyOwnedTroop(attacker))
        {
            return;
        }

        RtsUnitMotor attackerMotor = attacker.GetComponent<RtsUnitMotor>();
        RtsUnitMotor victimMotor = victim.GetComponent<RtsUnitMotor>();
        int attackerIndex = ResolveSyncIndex(attackerMotor);
        int victimIndex = ResolveSyncIndex(victimMotor);
        if (attackerIndex < 0 || victimIndex < 0)
        {
            return;
        }

        string token = string.IsNullOrEmpty(localInstanceToken) ? "local" : localInstanceToken;
        Send(string.Format(
            CultureInfo.InvariantCulture,
            "SP|VOLLEY|{0}|{1}|{2}|{3}",
            token,
            attackerIndex,
            victimIndex,
            Mathf.Max(1, arrowCount)));
    }

    private void HandleRemoteVolley(string[] parts, int payloadStart)
    {
        // SP|VOLLEY|token|attackerIndex|victimIndex|arrowCount
        if (parts.Length < payloadStart + 3)
        {
            return;
        }

        if (!int.TryParse(parts[payloadStart], NumberStyles.Integer, CultureInfo.InvariantCulture, out int attackerIndex)
            || !int.TryParse(parts[payloadStart + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int victimIndex)
            || !int.TryParse(parts[payloadStart + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int arrowCount))
        {
            return;
        }

        RtsUnitMotor attackerMotor = FindMotorBySyncIndex(attackerIndex);
        RtsUnitMotor victimMotor = FindMotorBySyncIndex(victimIndex);
        if (attackerMotor == null || victimMotor == null)
        {
            return;
        }

        // Owner already spawned locally — only the peer needs FX.
        if (IsLocallyOwnedMotor(attackerMotor))
        {
            return;
        }

        TroopCombat attacker = attackerMotor.GetComponent<TroopCombat>();
        TroopCombat victim = victimMotor.GetComponent<TroopCombat>();
        if (attacker == null || victim == null || !attacker.HasRangedAttack)
        {
            return;
        }

        attacker.SpawnRangedAttackVolleyVisual(victim, arrowCount);
    }

    /// <summary>
    /// Attacker fired a castle flaming arrow — send the same start/target/speed/arc so the
    /// defender can spawn a visual-only copy and watch the dodge in sync.
    /// </summary>
    public void NotifyCastleHazardArrow(
        Vector3 start,
        Vector3 target,
        float speed,
        float arcHeight,
        bool enableOutline,
        Color outlineColor,
        float outlineScale)
    {
        if (!matchRunning || IsDefender)
        {
            return;
        }

        string token = string.IsNullOrEmpty(localInstanceToken) ? "local" : localInstanceToken;
        Send(string.Format(
            CultureInfo.InvariantCulture,
            "SP|HAZARD|{0}|{1:0.###},{2:0.###},{3:0.###}|{4:0.###},{5:0.###},{6:0.###}|{7:0.##}|{8:0.##}|{9}|{10:0.###},{11:0.###},{12:0.###},{13:0.###}|{14:0.##}",
            token,
            start.x,
            start.y,
            start.z,
            target.x,
            target.y,
            target.z,
            speed,
            arcHeight,
            enableOutline ? 1 : 0,
            outlineColor.r,
            outlineColor.g,
            outlineColor.b,
            outlineColor.a,
            outlineScale));
    }

    private void HandleRemoteCastleHazard(string[] parts, int payloadStart)
    {
        // Attacker already spawned the real hazard locally; only the peer needs the visual.
        if (IsAttacker)
        {
            return;
        }

        // SP|HAZARD|token|sx,sy,sz|tx,ty,tz|speed|arc|outline|r,g,b,a|outlineScale
        if (parts.Length < payloadStart + 5)
        {
            return;
        }

        string[] startParts = parts[payloadStart].Split(',');
        string[] targetParts = parts[payloadStart + 1].Split(',');
        if (startParts.Length < 3
            || targetParts.Length < 3
            || !TryParseFloat(startParts[0], out float sx)
            || !TryParseFloat(startParts[1], out float sy)
            || !TryParseFloat(startParts[2], out float sz)
            || !TryParseFloat(targetParts[0], out float tx)
            || !TryParseFloat(targetParts[1], out float ty)
            || !TryParseFloat(targetParts[2], out float tz)
            || !TryParseFloat(parts[payloadStart + 2], out float speed)
            || !TryParseFloat(parts[payloadStart + 3], out float arc))
        {
            return;
        }

        bool enableOutline = parts.Length > payloadStart + 4
            && int.TryParse(parts[payloadStart + 4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int outlineFlag)
            && outlineFlag != 0;

        Color outlineColor = new Color(1f, 0.15f, 0.05f, 1f);
        float outlineScale = 1.14f;
        if (parts.Length > payloadStart + 5)
        {
            string[] colorParts = parts[payloadStart + 5].Split(',');
            if (colorParts.Length >= 4
                && TryParseFloat(colorParts[0], out float r)
                && TryParseFloat(colorParts[1], out float g)
                && TryParseFloat(colorParts[2], out float b)
                && TryParseFloat(colorParts[3], out float a))
            {
                outlineColor = new Color(r, g, b, a);
            }
        }

        if (parts.Length > payloadStart + 6)
        {
            TryParseFloat(parts[payloadStart + 6], out outlineScale);
        }

        GameObject prefab = ResolveCastleArrowPrefab();
        if (prefab == null)
        {
            return;
        }

        // Visual-only: do NOT ConfigurePlayerHazard — would hit the defender's local commander rig.
        TroopRangedProjectile projectile = TroopRangedProjectile.Launch(
            prefab,
            new Vector3(sx, sy, sz),
            new Vector3(tx, ty, tz),
            speed,
            arc);

        if (projectile != null)
        {
            float visualScale = Mathf.Max(1.25f, outlineScale * 1.15f);
            if (enableOutline)
            {
                projectile.ApplyVisualOutlineOnly(outlineColor, visualScale);
            }

            projectile.transform.localScale *= 1.35f;
        }

        SiegeSoundEffects soundEffects = SiegeSoundEffects.Instance;
        if (soundEffects != null)
        {
            soundEffects.PlayArrowShoot(new Vector3(sx, sy, sz));
        }
    }

    private static GameObject ResolveCastleArrowPrefab()
    {
        CastleArcherGuards[] guards = FindObjectsOfType<CastleArcherGuards>(true);
        for (int i = 0; i < guards.Length; i++)
        {
            if (guards[i] != null && guards[i].ArrowPrefab != null)
            {
                return guards[i].ArrowPrefab;
            }
        }

        return null;
    }

    private void HandleRemoteDamage(string[] parts, int payloadStart)
    {
        // SP|DMG|token|victimIndex|amount|isRanged|attackerIndex|flags
        if (parts.Length < payloadStart + 2)
        {
            return;
        }

        if (!int.TryParse(parts[payloadStart], NumberStyles.Integer, CultureInfo.InvariantCulture, out int victimIndex)
            || !TryParseFloat(parts[payloadStart + 1], out float amount))
        {
            return;
        }

        bool isRanged = parts.Length > payloadStart + 2
            && int.TryParse(parts[payloadStart + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rangedFlag)
            && rangedFlag != 0;

        TroopCombat attacker = null;
        if (parts.Length > payloadStart + 3
            && int.TryParse(parts[payloadStart + 3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int attackerIndex)
            && attackerIndex >= 0)
        {
            RtsUnitMotor attackerMotor = FindMotorBySyncIndex(attackerIndex);
            if (attackerMotor != null)
            {
                attacker = attackerMotor.GetComponent<TroopCombat>();
            }
        }

        int dmgFlags = 0;
        if (parts.Length > payloadStart + 4)
        {
            int.TryParse(parts[payloadStart + 4], NumberStyles.Integer, CultureInfo.InvariantCulture, out dmgFlags);
        }

        bool suppressCounter = (dmgFlags & DamageSyncFlagSuppressCounter) != 0;

        RtsUnitMotor victimMotor = FindMotorBySyncIndex(victimIndex);
        if (victimMotor == null)
        {
            return;
        }

        TroopCombat victim = victimMotor.GetComponent<TroopCombat>();
        if (victim == null || !IsLocallyOwnedTroop(victim))
        {
            return;
        }

        applyingRemotePath = true;
        try
        {
            // Peer-owned archers never run UpdateCombat here — spawn FX when their damage arrives
            // in case VOLLEY was dropped. Owner already spawned locally and skips via ownership.
            if (isRanged && attacker != null && attacker.HasRangedAttack && !IsLocallyOwnedTroop(attacker))
            {
                int visualCount = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1, attacker.ActiveUnitCount) * 0.35f));
                attacker.SpawnRangedAttackVolleyVisual(victim, visualCount);
            }

            if (!isRanged && !suppressCounter && attacker != null)
            {
                victim.TryPerformImmediateMeleeCounter(attacker, suppressCounterReply: true);
            }

            victim.TakeDamage(amount, attacker, isRanged);

            if (logNetworkMessages)
            {
                Debug.Log(
                    "SiegePvp DMG applied victim#" + victimIndex
                    + " '" + victim.name
                    + "' hp=" + victim.CurrentHealth.ToString("0.##", CultureInfo.InvariantCulture),
                    this);
            }
        }
        finally
        {
            applyingRemotePath = false;
        }
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
        if (IsDefender)
        {
            ReleaseCommandTowerBoundaryForDefender();
        }

        string resolvedReason = string.IsNullOrWhiteSpace(reason) ? "The cannons were overrun." : reason;
        bool isOverrun = resolvedReason.IndexOf("occupied", StringComparison.OrdinalIgnoreCase) >= 0
            || resolvedReason.IndexOf("cannon", StringComparison.OrdinalIgnoreCase) >= 0;
        if (attackerWon)
        {
            PlayLocalFx("FIRE");
        }
        else if (isOverrun)
        {
            PlayLocalFx("OVERRUN");
        }

        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager == null || !manager.IsPlaying)
        {
            return;
        }

        // Attacker win = cannons ready (FireCannonsAndWin). Attacker lose = TriggerDefeat.
        if (attackerWon)
        {
            manager.FireCannonsAndWin();
        }
        else
        {
            manager.TriggerDefeat(resolvedReason);
        }
    }

    /// <summary>
    /// LOSE payload is the defeat reason (may be split across |). Legacy "attacker|reason" accepted.
    /// </summary>
    private static string JoinLoseReason(string[] parts, int payloadStart)
    {
        if (parts == null || parts.Length <= payloadStart)
        {
            return "The cannons were overrun.";
        }

        int start = payloadStart;
        if (string.Equals(parts[start], "attacker", StringComparison.OrdinalIgnoreCase)
            || string.Equals(parts[start], "defender", StringComparison.OrdinalIgnoreCase))
        {
            start++;
        }

        if (start >= parts.Length)
        {
            return "The cannons were overrun.";
        }

        return string.Join("|", parts, start, parts.Length - start).Trim();
    }

    private void ReturnToMenuLocal(bool broadcast)
    {
        if (broadcast)
        {
            SendCommand("CANCEL");
            SendCommand("MENU");
        }

        CleanupPvpPresentation(teleportDefenderHome: true);

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
        ClearLobbyFlagsOnly();

        StopDefenderTeleportRoutine();
        StopCountdown();
        matchRunning = false;

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
            RestoreCommandTowerBoundaryAfterDefender();
        }

        if (SiegeMatchUi.Instance != null)
        {
            SiegeMatchUi.Instance.ApplyPvpRoleHud(false);
            SiegeMatchUi.Instance.RestorePlayingHudAnchors();
        }

        ClearNetworkAuthorityRoles();
        motorBySyncKey.Clear();
        syncMotorsByIndex.Clear();
        syncIndexByMotor.Clear();

        if (wandCommander != null)
        {
            wandCommander.SetControllableFaction(TroopCombat.Faction.Friendly);
        }

        // After cleanup / restart, still defer mode buttons until pointer is up.
        modeSelectUnlockTime = Mathf.Max(modeSelectUnlockTime, Time.unscaledTime + 0.25f);
        pendingShowModeSelectAfterRelease = true;

        if (SiegeMatchUi.Instance != null)
        {
            SiegeMatchUi.Instance.ShowModeSelect();
        }
    }

    private void ClearLobbyFlagsOnly()
    {
        localSelected = false;
        peerSelected = false;
        localReady = false;
        peerReady = false;
        lobbyPhase = LobbyPhase.Idle;
        nextLobbyAnnounceTime = Time.time + 1.5f;
    }

    private void ResetLobbyFlags()
    {
        ClearLobbyFlagsOnly();

        if (SiegeMatchUi.Instance != null)
        {
            SiegeMatchUi.Instance.ShowModeSelect();
        }
    }

    private void TeleportUser(Transform destination)
    {
        TryTeleportUser(destination, logFailure: logDefenderTeleport);
    }

    private void StopDefenderTeleportRoutine()
    {
        if (defenderTeleportCoroutine == null)
        {
            return;
        }

        StopCoroutine(defenderTeleportCoroutine);
        defenderTeleportCoroutine = null;
    }

    private IEnumerator TeleportDefenderToSpawnWhenReady()
    {
        ReleaseCommandTowerBoundaryForDefender();

        float timeoutAt = Time.unscaledTime + 20f;
        while (!SiegeSceneBootstrap.IsWarmUpComplete && Time.unscaledTime < timeoutAt)
        {
            yield return null;
        }

        for (int attempt = 0; attempt < 8; attempt++)
        {
            if (TryTeleportUser(defenderSpawnPoint, logFailure: attempt >= 7))
            {
                if (logDefenderTeleport)
                {
                    Debug.Log(
                        "SiegePvp defender teleported to spawn '"
                        + defenderSpawnPoint.name
                        + "' on attempt "
                        + (attempt + 1).ToString(CultureInfo.InvariantCulture)
                        + ".",
                        this);
                }

                yield break;
            }

            yield return new WaitForSecondsRealtime(0.35f);
        }

        if (logDefenderTeleport)
        {
            Debug.LogWarning(
                "SiegePvp failed to teleport defender to '"
                + defenderSpawnPoint.name
                + "'. Command-tower boundary was released so walking is not blocked.",
                this);
        }
    }

    private static void ReleaseCommandTowerBoundaryForDefender()
    {
        SiegePlayerBoundary[] boundaries = FindObjectsOfType<SiegePlayerBoundary>(true);
        for (int i = 0; i < boundaries.Length; i++)
        {
            SiegePlayerBoundary boundary = boundaries[i];
            if (boundary != null)
            {
                boundary.ForceReleaseForPvpDefender();
            }
        }
    }

    private static void RestoreCommandTowerBoundaryAfterDefender()
    {
        SiegePlayerBoundary[] boundaries = FindObjectsOfType<SiegePlayerBoundary>(true);
        for (int i = 0; i < boundaries.Length; i++)
        {
            SiegePlayerBoundary boundary = boundaries[i];
            if (boundary != null)
            {
                boundary.RestoreAfterPvpDefender();
            }
        }
    }

    private bool TryTeleportUser(Transform destination, bool logFailure)
    {
        if (destination == null)
        {
            return false;
        }

        ReleaseCommandTowerBoundaryForDefender();

        try
        {
            if (vGear.user != null)
            {
                vGear.user.Transform(destination.position, destination.eulerAngles);
                return true;
            }
        }
        catch (Exception exception)
        {
            if (logFailure)
            {
                Debug.LogWarning("SiegePvp vGear.user.Transform failed: " + exception.Message, this);
            }
        }

        try
        {
            if (vCast.user != null)
            {
                vCast.user.Transform(destination);
                return true;
            }
        }
        catch (Exception exception)
        {
            if (logFailure)
            {
                Debug.LogWarning("SiegePvp vCast.user.Transform failed: " + exception.Message, this);
            }
        }

        Transform user = SiegePlayEnvironment.ResolveUserTransform();
        if (user != null)
        {
            user.SetPositionAndRotation(destination.position, destination.rotation);
            return true;
        }

        if (logFailure)
        {
            Debug.LogWarning("SiegePvp teleport fallback failed: no Votanic user transform available.", this);
        }

        return false;
    }

    private void RefreshLobbyStatus()
    {
        if (lobbyPhase == LobbyPhase.Countdown || lobbyPhase == LobbyPhase.Playing)
        {
            return;
        }

        if (!localSelected && !localReady && lobbyPhase == LobbyPhase.Idle)
        {
            peerReady = false;
            if (peerSelected)
            {
                ShowStatus(peerSelectedPrompt);
            }
            else if (SiegeMatchUi.Instance != null)
            {
                SiegeMatchUi.Instance.ShowModeSelect();
            }

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
    }

    private void ShowStatus(string message)
    {
        if (SiegeMatchUi.Instance != null)
        {
            SiegeMatchUi.Instance.SetLobbyStatusMessage(message);
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
