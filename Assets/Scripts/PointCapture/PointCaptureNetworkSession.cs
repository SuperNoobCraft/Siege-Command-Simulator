using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using Votanic.vNet.Networking;
using Votanic.vXR.vGear.Networking;

/// <summary>
/// Point Capture gameplay sync: unit paths/poses/combat, village ownership, spawns, and match end.
/// Host = Yellow authority for village capture state. Each machine owns its faction's troops.
/// </summary>
[DefaultExecutionOrder(-95)]
[DisallowMultipleComponent]
public class PointCaptureNetworkSession : MonoBehaviour
{
    private const string MessagePrefix = "PC|";

    private const int PoseSyncFlagHasPath = 1;
    private const int PoseSyncFlagPathHalted = 2;
    private const int PoseSyncFlagAdvancing = 4;
    private const int PoseSyncFlagRetreatInvulnerable = 8;

    private const int CombatStateActive = 0;
    private const int CombatStateRetreat = 1;
    private const int CombatStateDead = 2;
    private const int CombatStateRegroup = 3;

    private const int DamageSyncFlagSuppressCounter = 1;

    public static PointCaptureNetworkSession Instance { get; private set; }

    [Header("References")]
    [SerializeField] private PointCaptureMatch match;
    [SerializeField] private PointCaptureBoard board;
    [SerializeField] private PointCaptureSpawner spawner;
    [SerializeField] private VotanicWandRtsCommander wandCommander;
    [SerializeField] private vGear_Networking networking;

    [Header("Sync")]
    [SerializeField] private bool enablePoseCorrection = true;
    [SerializeField, Min(0.04f)] private float poseSyncIntervalSeconds = 0.05f;
    [SerializeField, Range(8, 48)] private int maxSyncedPathPoints = 28;
    [SerializeField, Min(0.05f)] private float pathNetworkSimplifyEpsilon = 0.35f;
    [SerializeField, Min(0.25f)] private float authoritySnapDistance = 1.15f;
    [SerializeField, Min(0.25f)] private float pathHaltResyncDistance = 0.75f;
    [SerializeField, Range(4, 20)] private int maxPoseUnitsPerPacket = 14;
    [SerializeField, Min(0.1f)] private float villageSyncIntervalSeconds = 0.2f;

    [Header("Debug")]
    [SerializeField] private bool logNetworkMessages;

    private bool matchRunning;
    private bool applyingRemotePath;
    private float nextPoseSyncTime;
    private float nextVillageSyncTime;
    private float lastPathNotifyTime = -1f;
    private int lastPathNotifyMotorId = int.MinValue;
    private NetworkManager.OnReceived previousReceivedHandler;
    private bool networkingBound;

    private readonly Dictionary<string, RtsUnitMotor> motorBySyncKey = new Dictionary<string, RtsUnitMotor>();
    private readonly List<RtsUnitMotor> syncMotorsByIndex = new List<RtsUnitMotor>();
    private readonly Dictionary<RtsUnitMotor, int> syncIndexByMotor = new Dictionary<RtsUnitMotor, int>();
    private readonly Dictionary<int, Vector3> lastBroadcastPoseByIndex = new Dictionary<int, Vector3>();
    private readonly Dictionary<int, Vector3> lastRemotePoseByIndex = new Dictionary<int, Vector3>();
    private readonly Dictionary<int, int> remotePoseStableCountByIndex = new Dictionary<int, int>();

    public bool IsSyncActive => matchRunning && match != null && match.HasConnectedPeer;
    public bool IsMatchRunning => matchRunning;

    private bool IsHost => match != null && match.IsNetworkHost;

    private void Awake()
    {
        Instance = this;
        ResolveReferences();
    }

    private void OnEnable()
    {
        Instance = this;
        ResolveReferences();
        EnsureWandBound();
        BindNetworking();
        if (match != null)
        {
            match.StateChanged += HandleMatchStateChanged;
            HandleMatchStateChanged(match.CurrentState);
        }
    }

    private void OnDisable()
    {
        if (wandCommander != null)
        {
            wandCommander.PathCommandIssued -= NotifyPathFromCommander;
        }

        if (match != null)
        {
            match.StateChanged -= HandleMatchStateChanged;
        }

        UnbindNetworking();
        UnbindMotorSyncEvents();
        SetVillageCaptureAuthority(false);
        matchRunning = false;

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (!matchRunning || !IsSyncActive)
        {
            return;
        }

        if (enablePoseCorrection)
        {
            TryBroadcastOwnedUnitPoses();
        }

        if (IsHost && Time.time >= nextVillageSyncTime)
        {
            nextVillageSyncTime = Time.time + villageSyncIntervalSeconds;
            BroadcastVillageSnapshot();
        }
    }

    private void ResolveReferences()
    {
        if (match == null)
        {
            match = PointCaptureMatch.Instance;
        }

        if (board == null)
        {
            board = PointCaptureBoard.Instance;
        }

        if (spawner == null)
        {
            spawner = match != null ? match.Spawner : null;
        }

        if (networking == null)
        {
            networking = FindObjectOfType<vGear_Networking>();
        }

        if (wandCommander == null)
        {
            wandCommander = FindObjectOfType<VotanicWandRtsCommander>();
        }
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

        wandCommander.PathCommandIssued -= NotifyPathFromCommander;
        wandCommander.PathCommandIssued += NotifyPathFromCommander;
    }

    private void HandleMatchStateChanged(PointCaptureMatch.MatchState state)
    {
        matchRunning = state == PointCaptureMatch.MatchState.Playing;
        if (matchRunning)
        {
            RebuildUnitSyncTable();
            SetVillageCaptureAuthority(IsSyncActive && !IsHost);
            nextVillageSyncTime = 0f;
            nextPoseSyncTime = 0f;
            if (IsHost && IsSyncActive)
            {
                BroadcastVillageSnapshot();
            }
        }
        else
        {
            SetVillageCaptureAuthority(false);
            UnbindMotorSyncEvents();
            syncMotorsByIndex.Clear();
            syncIndexByMotor.Clear();
            motorBySyncKey.Clear();
            lastBroadcastPoseByIndex.Clear();
            lastRemotePoseByIndex.Clear();
            remotePoseStableCountByIndex.Clear();
        }
    }

    private void SetVillageCaptureAuthority(bool clientDefersToHost)
    {
        if (board == null || board.Villages == null)
        {
            return;
        }

        PointCaptureVillage[] villages = board.Villages;
        for (int i = 0; i < villages.Length; i++)
        {
            villages[i]?.SetDeferCaptureSimulation(clientDefersToHost);
        }
    }

    public bool IsLocallyOwnedMotor(RtsUnitMotor motor)
    {
        if (motor == null || match == null)
        {
            return false;
        }

        TroopCombat troop = motor.GetComponent<TroopCombat>();
        if (troop == null)
        {
            return false;
        }

        CaptureOwner localFaction = match.NetworkLocalFaction;
        CaptureOwner troopOwner = CaptureTeams.FromTroopFaction(troop.TroopFaction);
        return troopOwner == localFaction;
    }

    public bool IsLocallyOwnedTroop(TroopCombat troop)
    {
        if (troop == null)
        {
            return false;
        }

        RtsUnitMotor unitMotor = troop.GetComponent<RtsUnitMotor>();
        return unitMotor != null && IsLocallyOwnedMotor(unitMotor);
    }

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

    public void NotifyPathFromCommander(RtsUnitMotor motor, IReadOnlyList<Vector3> path)
    {
        if (!IsSyncActive || motor == null)
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

    public void NotifyStopFromCommander(RtsUnitMotor motor)
    {
        if (!IsSyncActive || applyingRemotePath || motor == null || !IsLocallyOwnedMotor(motor))
        {
            return;
        }

        int syncIndex = ResolveSyncIndex(motor);
        if (syncIndex < 0)
        {
            return;
        }

        Send(string.Format(
            CultureInfo.InvariantCulture,
            "PC|STOP|{0}|{1}",
            GetLocalToken(),
            syncIndex));

        BroadcastOwnedMotorPoseImmediate(motor, syncIndex, forceHaltedFlag: true);
    }

    public void NotifyRetreatDestinationFromAuthority(RtsUnitMotor motor, Vector3 destination)
    {
        if (!IsSyncActive || applyingRemotePath || motor == null || !IsLocallyOwnedMotor(motor))
        {
            return;
        }

        Vector3 start = motor.transform.position;
        List<Vector3> path = new List<Vector3>(2) { start, destination };
        HandleLocalPathCommand(motor, path);
    }

    public void NotifyOwnedTroopCombatChanged(TroopCombat troop)
    {
        if (!IsSyncActive || troop == null || !IsLocallyOwnedTroop(troop))
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

    public void NotifyInflictedDamage(
        TroopCombat attacker,
        TroopCombat victim,
        float amount,
        bool isRangedAttack,
        bool suppressCounterReply = false)
    {
        if (!IsSyncActive || victim == null || amount <= 0f)
        {
            return;
        }

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
        Send(string.Format(
            CultureInfo.InvariantCulture,
            "PC|DMG|{0}|{1}|{2:0.##}|{3}|{4}|{5}",
            GetLocalToken(),
            victimIndex,
            amount,
            isRangedAttack ? 1 : 0,
            attackerIndex,
            dmgFlags));
    }

    public void NotifyLocalRegimentSpawned(
        CaptureOwner owner,
        PointCaptureSpawner.RegimentType regimentType,
        Vector3 worldPosition,
        int regimentSpawnId)
    {
        if (!IsSyncActive || regimentSpawnId < 0)
        {
            return;
        }

        CaptureOwner localFaction = match != null ? match.NetworkLocalFaction : CaptureOwner.Yellow;
        if (owner != localFaction)
        {
            return;
        }

        Vector3 position = worldPosition;
        Send(string.Format(
            CultureInfo.InvariantCulture,
            "PC|SPAWN|{0}|{1}|{2}|{3:0.###}|{4:0.###}|{5:0.###}|{6}",
            GetLocalToken(),
            (int)owner,
            (int)regimentType,
            position.x,
            position.y,
            position.z,
            regimentSpawnId));

        RebuildUnitSyncTable();
    }

    public void NotifyMatchEnded(CaptureOwner winner, string reason)
    {
        if (!IsSyncActive || !IsHost)
        {
            return;
        }

        string safeReason = string.IsNullOrEmpty(reason) ? "Match ended." : reason.Replace("|", "/");
        Send(string.Format(
            CultureInfo.InvariantCulture,
            "PC|END|{0}|{1}|{2}",
            GetLocalToken(),
            (int)winner,
            safeReason));
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
        if (string.IsNullOrEmpty(message) || !IsSyncActive)
        {
            return;
        }

        int index = message.IndexOf(MessagePrefix, StringComparison.Ordinal);
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
        if (!string.IsNullOrEmpty(GetLocalToken()) && token == GetLocalToken())
        {
            return;
        }

        switch (parts[1])
        {
            case "PATH":
                HandleRemotePath(parts, 3);
                break;
            case "STOP":
                HandleRemoteStop(parts, 3);
                break;
            case "POSE":
                HandleRemotePose(parts, 3);
                break;
            case "DMG":
                HandleRemoteDamage(parts, 3);
                break;
            case "VIL":
                HandleRemoteVillages(parts, 3);
                break;
            case "SPAWN":
                HandleRemoteSpawn(parts, 3);
                break;
            case "END":
                HandleRemoteEnd(parts, 3);
                break;
        }
    }

    private void HandleLocalPathCommand(RtsUnitMotor motor, IReadOnlyList<Vector3> path)
    {
        if (!IsSyncActive || applyingRemotePath || motor == null || path == null || path.Count == 0)
        {
            return;
        }

        if (!IsLocallyOwnedMotor(motor))
        {
            return;
        }

        int syncIndex = ResolveSyncIndex(motor);
        if (syncIndex < 0)
        {
            RebuildUnitSyncTable();
            syncIndex = ResolveSyncIndex(motor);
            if (syncIndex < 0)
            {
                return;
            }
        }

        List<Vector3> compressed = CanonicalizePathForMatch(path);
        if (compressed.Count < 2)
        {
            return;
        }

        StringBuilder builder = new StringBuilder(64 + compressed.Count * 24);
        builder.Append("PC|PATH|").Append(GetLocalToken()).Append('|').Append(syncIndex);
        for (int i = 0; i < compressed.Count; i++)
        {
            Vector3 point = compressed[i];
            builder.Append('|')
                .Append(point.x.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(point.y.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(point.z.ToString("0.###", CultureInfo.InvariantCulture));
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
        if (syncMotorsByIndex.Count == 0)
        {
            RebuildUnitSyncTable();
        }

        CaptureOwner localFaction = match.NetworkLocalFaction;
        int packetCap = Mathf.Max(4, maxPoseUnitsPerPacket);
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
                if (troop == null || CaptureTeams.FromTroopFaction(troop.TroopFaction) != localFaction)
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

        for (int start = 0; start < ordered.Count; start += packetCap)
        {
            StringBuilder builder = new StringBuilder(128 + packetCap * 28);
            builder.Append("PC|POSE|").Append(GetLocalToken());
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

    private void BroadcastVillageSnapshot()
    {
        if (board == null || board.Villages == null)
        {
            return;
        }

        StringBuilder builder = new StringBuilder(96);
        builder.Append("PC|VIL|").Append(GetLocalToken());
        PointCaptureVillage[] villages = board.Villages;
        for (int i = 0; i < villages.Length; i++)
        {
            PointCaptureVillage village = villages[i];
            if (village == null)
            {
                continue;
            }

            builder.Append('|').Append(i).Append(',')
                .Append((int)village.CurrentOwner).Append(',')
                .Append((int)village.CapturingOwner).Append(',')
                .Append(village.CaptureProgress.ToString("0.###", CultureInfo.InvariantCulture));
        }

        Send(builder.ToString());
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

        Send(string.Format(
            CultureInfo.InvariantCulture,
            "PC|POSE|{0}|{1},{2:0.###},{3:0.###},{4:0.###},{5:0.##},{6},{7}",
            GetLocalToken(),
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
                if (delta.sqrMagnitude >= 0.0225f)
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
            motorBySyncKey[SanitizeSyncKey(GetUnitSyncKey(motor.transform))] = motor;
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
        if (!IsSyncActive || applyingRemotePath || motor == null || !IsLocallyOwnedMotor(motor))
        {
            return;
        }

        int syncIndex = ResolveSyncIndex(motor);
        if (syncIndex < 0)
        {
            return;
        }

        Send(string.Format(
            CultureInfo.InvariantCulture,
            "PC|STOP|{0}|{1}",
            GetLocalToken(),
            syncIndex));
        BroadcastOwnedMotorPoseImmediate(motor, syncIndex, forceHaltedFlag: true);
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

    private void HandleRemotePath(string[] parts, int payloadStart)
    {
        if (parts.Length < payloadStart + 2)
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
            troop?.SetNetworkOwnerPoseHalted(false);
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
            troop?.NotifyNetworkPositionApplied(hardSnap: false);
        }
        finally
        {
            applyingRemotePath = false;
        }
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
            troop?.SetNetworkOwnerPoseHalted(true);
        }
        finally
        {
            applyingRemotePath = false;
        }
    }

    private void HandleRemotePose(string[] parts, int payloadStart)
    {
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

                troop?.SetNetworkOwnerPoseHalted(ownerHalted);
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
                    else
                    {
                        troop?.NotifyNetworkPositionApplied(hardSnap: true);
                    }

                    continue;
                }

                motor.SuppressLocalSimulation = false;
                Vector3 delta = remotePos - motor.transform.position;
                delta.y = 0f;
                float horizontalErrorSqr = delta.sqrMagnitude;
                float haltResyncSqr = pathHaltResyncDistance * pathHaltResyncDistance;
                UpdateRemotePoseStability(syncIndex, remotePos, ownerAdvancing, ownerHasPath);

                bool ownerStoppedFollowingPath = ownerHalted
                    || (!ownerHasPath && (!ownerAdvancing || GetRemotePoseStableCount(syncIndex) >= 3));
                bool peerDriftedWhileOwnerStopped = ownerStoppedFollowingPath
                    && (motor.HasActivePath || motor.HasDestination)
                    && horizontalErrorSqr >= haltResyncSqr;

                if (peerDriftedWhileOwnerStopped)
                {
                    motor.Stop();
                    motor.SuppressLocalSimulation = true;
                    troop?.SetNetworkOwnerPoseHalted(true);
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
                    else
                    {
                        troop?.NotifyNetworkPositionApplied(hardSnap: true);
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
                troop?.NotifyNetworkPositionApplied(hardSnap: false);
            }
        }
        finally
        {
            applyingRemotePath = false;
        }
    }

    private void HandleRemoteDamage(string[] parts, int payloadStart)
    {
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
            attacker = attackerMotor != null ? attackerMotor.GetComponent<TroopCombat>() : null;
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
            if (!isRanged && !suppressCounter && attacker != null)
            {
                victim.TryPerformImmediateMeleeCounter(attacker, suppressCounterReply: true);
            }

            victim.TakeDamage(amount, attacker, isRanged);
        }
        finally
        {
            applyingRemotePath = false;
        }
    }

    private void HandleRemoteVillages(string[] parts, int payloadStart)
    {
        if (board == null || board.Villages == null || IsHost)
        {
            return;
        }

        for (int i = payloadStart; i < parts.Length; i++)
        {
            string[] packed = parts[i].Split(',');
            if (packed.Length < 4)
            {
                continue;
            }

            if (!int.TryParse(packed[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int villageIndex)
                || !int.TryParse(packed[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ownerCode)
                || !int.TryParse(packed[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int capturingCode)
                || !TryParseFloat(packed[3], out float progress))
            {
                continue;
            }

            if (villageIndex < 0 || villageIndex >= board.Villages.Length)
            {
                continue;
            }

            PointCaptureVillage village = board.Villages[villageIndex];
            village?.ApplyNetworkCaptureState(
                (CaptureOwner)ownerCode,
                (CaptureOwner)capturingCode,
                progress);
        }
    }

    private void HandleRemoteSpawn(string[] parts, int payloadStart)
    {
        if (spawner == null || parts.Length < payloadStart + 6)
        {
            return;
        }

        if (!int.TryParse(parts[payloadStart], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ownerCode)
            || !int.TryParse(parts[payloadStart + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int typeCode)
            || !TryParseFloat(parts[payloadStart + 2], out float x)
            || !TryParseFloat(parts[payloadStart + 3], out float y)
            || !TryParseFloat(parts[payloadStart + 4], out float z)
            || !int.TryParse(parts[payloadStart + 5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int regimentSpawnId))
        {
            return;
        }

        CaptureOwner owner = (CaptureOwner)ownerCode;
        if (match != null && owner == match.NetworkLocalFaction)
        {
            return;
        }

        Vector3 position = new Vector3(x, y, z);
        spawner.SpawnRegimentFromNetwork(
            owner,
            (PointCaptureSpawner.RegimentType)typeCode,
            position,
            regimentSpawnId);
        RebuildUnitSyncTable();
    }

    private void HandleRemoteEnd(string[] parts, int payloadStart)
    {
        if (match == null || IsHost || parts.Length <= payloadStart)
        {
            return;
        }

        if (!int.TryParse(parts[payloadStart], NumberStyles.Integer, CultureInfo.InvariantCulture, out int winnerCode))
        {
            return;
        }

        string reason = parts.Length > payloadStart + 1 ? parts[payloadStart + 1] : "Match ended.";
        match.ApplyNetworkEnd((CaptureOwner)winnerCode, reason);
    }

    private void UpdateRemotePoseStability(int syncIndex, Vector3 remotePos, bool ownerAdvancing, bool ownerHasPath)
    {
        if (!lastRemotePoseByIndex.TryGetValue(syncIndex, out Vector3 previous))
        {
            lastRemotePoseByIndex[syncIndex] = remotePos;
            remotePoseStableCountByIndex[syncIndex] = 0;
            return;
        }

        Vector3 delta = remotePos - previous;
        delta.y = 0f;
        bool stable = delta.sqrMagnitude < 0.04f && !ownerAdvancing && !ownerHasPath;
        remotePoseStableCountByIndex[syncIndex] = stable
            ? GetRemotePoseStableCount(syncIndex) + 1
            : 0;
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
        return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("|", "/");
    }

    private static bool TryParseFloat(string text, out float value)
    {
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private string GetLocalToken()
    {
        return match != null ? match.LocalNetworkToken : "local";
    }

    private void Send(string message)
    {
        if (match != null)
        {
            match.SendNetworkMessage(message);
            return;
        }

        if (networking == null || string.IsNullOrEmpty(message))
        {
            return;
        }

        try
        {
            networking.Send(message);
        }
        catch (Exception)
        {
        }
    }
}
