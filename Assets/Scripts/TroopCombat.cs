using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DefaultExecutionOrder(200)]
public class TroopCombat : MonoBehaviour
{
    public static event System.Action<TroopCombat> RegimentEnteredRetreat;
    public static event System.Action<TroopCombat> RegimentRegroupCompleted;
    public static event System.Action<TroopCombat> RegimentPermanentlyDestroyed;
    public static event System.Action<TroopCombat, Vector3> MeleeAttackPerformed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetTroopCombatStatics()
    {
        RegimentEnteredRetreat = null;
        RegimentRegroupCompleted = null;
        RegimentPermanentlyDestroyed = null;
        MeleeAttackPerformed = null;
    }
    public enum Faction
    {
        Friendly,
        Enemy
    }

    public enum State
    {
        Idle,
        Fight,
        Retreat,
        Regroup,
        Dead
    }

    [Header("Identity")]
    [SerializeField] private Faction faction = Faction.Friendly;

    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private bool destroyOnDeath = true;

    [Header("Recovery")]
    [SerializeField, Min(0f)] private float idleRegenPerSecond = 2f;
    [SerializeField, Min(0f)] private float campRegenPerSecond = 25f;

    [Header("Retreat")]
    [SerializeField, Min(0f)] private float retreatInvulnerabilityDuration = 1.5f;
    [SerializeField, Min(1f)] private float retreatMoveSpeedMultiplier = 1.75f;
    [SerializeField, Min(0.05f)] private float retreatDestinationRefreshInterval = 0.5f;
    [Tooltip("When a retreating regiment is finished off, active troop visuals are hidden one-by-one in random order across this duration.")]
    [SerializeField, Min(0f)] private float retreatDeathDisappearSpan = 0.2f;

    [Header("Melee Combat")]
    [Tooltip("Close-quarters fallback for ranged units. Infantry uses this as their primary attack.")]
    [SerializeField] private float attackDamage = 10f;
    [Tooltip("Maximum distance for melee attacks. Ranged units cannot use ranged attacks at or inside this distance.")]
    [SerializeField] private float attackRange = 2.5f;
    [SerializeField] private float attackCooldown = 1.25f;

    [Header("Ranged Combat")]
    [SerializeField] private bool hasRangedAttack = false;
    [Tooltip("Only used while the target is outside melee range and within ranged attack range.")]
    [SerializeField] private float rangedAttackDamage = 8f;
    [SerializeField] private float rangedAttackRange = 12f;
    [SerializeField] private float rangedAttackCooldown = 1.5f;

    [Header("Ranged Vulnerability")]
    [SerializeField] private bool canBeKilledByRangedAttackWhileRetreating = false;

    [Header("Ranged Attack VFX")]
    [SerializeField] private GameObject rangedProjectilePrefab;
    [SerializeField, Min(0.1f)] private float rangedProjectileSpeed = 18f;
    [SerializeField, Range(0.05f, 1f)] private float rangedProjectileFrequency = 0.35f;
    [SerializeField, Min(0f)] private float rangedProjectileMinArcHeight = 1.25f;
    [SerializeField, Min(0f)] private float rangedProjectileMaxArcHeight = 2.75f;
    [SerializeField, Range(0f, 1f)] private float rangedProjectileDispersion = 0.35f;
    [SerializeField, Min(0f)] private float rangedProjectileMaxSpreadRadius = 2.5f;
    [SerializeField, Min(0f)] private float rangedProjectileLaunchHeight = 1.1f;

    [Header("Combat Targeting")]
    [SerializeField] private float targetScanInterval = 0.2f;
    [SerializeField] private LayerMask targetLayers = ~0;
    [SerializeField] private bool faceTargetWhileFighting = true;

    [Header("Combat Movement")]
    [Tooltip("Speed at full enemy footprint overlap (0 = standstill).")]
    [SerializeField, Range(0f, 1f)] private float combatMoveSpeedPercentage = 0f;
    [Tooltip("Speed as soon as any enemy overlap begins (before scaling down further with more overlap).")]
    [SerializeField, Range(0f, 1f)] private float combatOverlapStartSpeedPercentage = 0.5f;
    [SerializeField] private bool scaleCombatSpeedByEnemyOverlap = true;
    [SerializeField, Min(0f)] private float combatOverlapSmoothingSpeed = 8f;
    [SerializeField] private Collider footprintCollider;
    [SerializeField] private Collider movementBoundsCollider;
    [SerializeField, Range(0.15f, 1f)] private float movementBoundsScale = 0.55f;

    [Header("Selection Volume")]
    [Tooltip("Tall pick volume for wand/mouse selection. Separate from combat footprint.")]
    [SerializeField] private Collider selectionCollider;
    [SerializeField, Min(0.5f)] private float selectionVolumeHeight = 5f;
    [SerializeField, Min(0.5f)] private float selectionVolumeXZScale = 1.6f;
    [SerializeField] private bool autoCreateSelectionVolume = true;

    [Header("Regiment Visuals")]
    [SerializeField] private GameObject troopPrefab;
    [SerializeField] private GameObject flagHolderPrefab;
    [SerializeField] private GameObject defeatedFlagHolderPrefab;
    [SerializeField, Min(0)] private int maxUnitCount = 49;
    [SerializeField, Range(0f, 1f)] private float defeatedUnitPercentage = 0.4f;
    [SerializeField] private Transform troopVisualRoot;
    [SerializeField] private bool keepTroopVisualScaleIndependent = true;
    [SerializeField, Min(0.01f)] private float formationCellSpacing = 0.5f;
    [SerializeField, Range(0f, 0.5f)] private float formationJitterFraction = 0.2f;
    [SerializeField] private bool randomizeSpawnOrder = true;
    [SerializeField, Range(-180f, 180f)] private float troopFacingYawOffsetDegrees = 0f;
    [Header("Ground Projection")]
    [Tooltip("Snap the regiment root (models, footprint, selection, movement bounds) down onto RTS_Ground.")]
    [SerializeField] private bool snapRegimentRootToGround = true;
    [Tooltip("Also offset each troop mesh to the ground under its own slot (slopes across the formation).")]
    [SerializeField] private bool projectTroopVisualsToGround = true;
    [SerializeField] private LayerMask troopGroundLayers;
    [SerializeField] private float regimentGroundYOffset = 0f;
    [SerializeField] private float troopGroundYOffset = 0f;
    [SerializeField, Min(1f)] private float troopGroundRayStartHeight = 256f;
    [Tooltip("Max how far above the regiment plane a troop visual may climb (blocks cliff-top projection).")]
    [SerializeField, Min(0.1f)] private float maxTroopVisualStepHeight = 1.25f;
    [Tooltip("Keep root Y stuck to ground under the regiment while it moves.")]
    [SerializeField] private bool keepRegimentRootOnGroundWhileMoving = true;
    [Tooltip("Ignore ground Y changes smaller than this (meters) to stop idle flicker.")]
    [SerializeField, Min(0f)] private float groundSnapHysteresis = 0.12f;
    [Tooltip("Max upward root snap per frame while moving (prevents jumping to overlapping higher meshes).")]
    [SerializeField, Min(0.05f)] private float maxRegimentGroundRisePerSnap = 0.35f;
    [Tooltip("While moving, ease root Y toward ground instead of hard-snapping every frame.")]
    [SerializeField, Min(0f)] private float groundSnapSmoothSpeed = 0f;

    [Header("Debug Gizmos")]
    [SerializeField] private bool drawDebugGizmos = true;
    [SerializeField] private Transform debugAnchor;
    [SerializeField] private float debugLabelHeight = 2.2f;
    [SerializeField] private float debugHealthBarWidth = 1.2f;
    [SerializeField] private float debugHealthBarHeight = 0.08f;
    [SerializeField] private Color friendlyGizmoColor = new Color(0.2f, 0.8f, 0.35f, 1f);
    [SerializeField] private Color enemyGizmoColor = new Color(0.9f, 0.25f, 0.2f, 1f);
    [SerializeField] private Color idleGizmoColor = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private Color fightGizmoColor = new Color(1f, 0.82f, 0.2f, 1f);
    [SerializeField] private Color retreatGizmoColor = new Color(0.95f, 0.45f, 0.1f, 1f);
    [SerializeField] private Color regroupGizmoColor = new Color(0.35f, 0.75f, 1f, 1f);
    [SerializeField] private Color deadGizmoColor = new Color(0.4f, 0.4f, 0.4f, 1f);

    private enum RetreatPhase
    {
        ToGateOutside,
        ToGateInside,
        ToCamp
    }

    private RtsUnitMotor motor;
    private EnemyRegimentAI regimentAi;
    private TroopCombat currentTarget;
    private float currentHealth;
    private float nextAttackTime;
    private float nextScanTime;
    private float nextRetreatDestinationRefreshTime;
    private float nextRetreatUnstuckTime;
    private float lastCombatInteractionTime = float.NegativeInfinity;
    private int retreatUnstuckAttemptIndex;
    private float invulnerableUntil;
    private bool isPermanentlyEliminated;
    private bool retreatVisualsSyncedToDefeat;
    private Coroutine retreatDeathDisappearCoroutine;
    private RetreatPhase retreatPhase = RetreatPhase.ToGateOutside;
    private Vector3 troopPrefabScale = Vector3.one;
    private Vector3 flagHolderPrefabScale = Vector3.one;
    private bool flagHolderShowingDefeated;
    private bool networkOwnerPoseHalted;
    private Vector3 lastRegimentPosition;
    private float smoothedCombatOverlap;
    private float nextNetworkVolleyVisualTime;
    private const float MinimumFacingMovementDistance = 0.02f;
    private readonly List<TroopVisualInstance> troopVisuals = new List<TroopVisualInstance>();
    private int activeTroopVisualCount;
    private Vector3 lastGroundProjectionRegimentPosition;
    private bool troopGroundProjectionDirty = true;
    private bool loggedMissingGround;
    private float authoredYawDegrees;
    private Coroutine retryGroundSnapCoroutine;
    private Coroutine pointCaptureRaiseCoroutine;
    private bool pointCaptureRetreatTracking;
    private bool hasLockedRegimentGroundY;
    private float lockedRegimentGroundY;
    private Vector3 lockedRegimentGroundXZ;
    private bool hasLockedDeathPose;
    private Vector3 lockedDeathPosition;
    private Quaternion lockedDeathRotation;

    public Faction TroopFaction => faction;
    public State CurrentState { get; private set; } = State.Idle;
    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public float AttackRange => HasRangedAttack ? Mathf.Max(attackRange, rangedAttackRange) : attackRange;
    public float MeleeAttackRange => attackRange;
    public float RangedAttackRange => HasRangedAttack ? rangedAttackRange : 0f;
    public bool HasRangedAttack => hasRangedAttack && rangedAttackRange > 0f;
    public bool CanBeKilledByRangedAttackWhileRetreating => canBeKilledByRangedAttackWhileRetreating;
    public TroopCombat CurrentTarget => currentTarget;

    public void ClearCurrentTarget()
    {
        currentTarget = null;
        if (CurrentState == State.Fight)
        {
            CurrentState = State.Idle;
            smoothedCombatOverlap = 0f;
        }
    }

    public float HealthNormalized => Mathf.Clamp01(currentHealth / Mathf.Max(1f, maxHealth));
    public bool IsInCombat => CurrentState == State.Fight;
    public bool IsPointCaptureRaising { get; private set; }
    public int MaxUnitCount => maxUnitCount;
    public int ActiveUnitCount => activeTroopVisualCount;
    public int MinimumUnitCountAtDefeat => Mathf.RoundToInt(maxUnitCount * defeatedUnitPercentage);
    public bool IsCommandable => motor == null || motor.CanReceiveCommands;
    public bool IsRetreating => CurrentState == State.Retreat;
    public bool IsPermanentlyEliminated => isPermanentlyEliminated;
    public bool IsRetreatInvulnerable => CurrentState == State.Retreat && Time.time < invulnerableUntil;
    public bool IsTraversingGate
    {
        get
        {
            if (CurrentState != State.Retreat)
            {
                return false;
            }

            RtsCampManager camps = RtsCampManager.Instance;
            if (camps == null || !camps.HasGate(faction))
            {
                return false;
            }

            // Still approaching / inside the corridor waypoints.
            if (retreatPhase != RetreatPhase.ToCamp
                && camps.IsNearGateForOpening(transform.position, faction))
            {
                return true;
            }

            // After the inside waypoint they switch to ToCamp — keep the gate open until
            // they are physically clear of the arch, or they get crushed by the close.
            return camps.IsInGatePassage(transform.position, faction);
        }
    }
    public bool HoldsInCampUntilNextWave { get; private set; }
    public bool IsRegrouping => CurrentState == State.Regroup;
    public float CombatMoveSpeedMultiplier => GetCombatMoveSpeedMultiplier();
    public Collider FootprintCollider => footprintCollider;
    public Collider SelectionCollider
    {
        get
        {
            EnsureSelectionCollider();
            return selectionCollider;
        }
    }

    public bool IsSelectionCollider(Collider collider)
    {
        if (collider == null)
        {
            return false;
        }

        EnsureSelectionCollider();
        return selectionCollider != null && collider == selectionCollider;
    }

    public bool WasRecentlyInCombat(float recentSeconds)
    {
        return Time.time - lastCombatInteractionTime <= Mathf.Max(0f, recentSeconds);
    }

    private Vector3 matchStartPosition;
    private Quaternion matchStartRotation;
    private bool hasCachedMatchStartPose;

    public void SetHoldInCampUntilNextWave(bool holdInCamp)
    {
        HoldsInCampUntilNextWave = holdInCamp;
    }

    public void ResetForMatchStart()
    {
        if (retreatDeathDisappearCoroutine != null)
        {
            StopCoroutine(retreatDeathDisappearCoroutine);
            retreatDeathDisappearCoroutine = null;
        }

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        if (hasCachedMatchStartPose && ShouldRestoreMatchStartPose())
        {
            transform.position = new Vector3(matchStartPosition.x, matchStartPosition.y, matchStartPosition.z);
            transform.rotation = GetAuthoredLevelRotation();
        }

        isPermanentlyEliminated = false;
        retreatVisualsSyncedToDefeat = false;
        hasLockedDeathPose = false;
        currentHealth = Mathf.Max(1f, maxHealth);
        currentTarget = null;
        HoldsInCampUntilNextWave = false;
        CurrentState = State.Idle;
        retreatPhase = RetreatPhase.ToGateOutside;
        nextAttackTime = 0f;
        nextScanTime = 0f;
        invulnerableUntil = 0f;
        smoothedCombatOverlap = 0f;
        flagHolderShowingDefeated = false;

        if (motor != null)
        {
            motor.Stop();
            motor.CanReceiveCommands = motor.IsCommandUnit;
            motor.MoveSpeedMultiplier = SiegeMatchSettings.ActiveMoveSpeedScale;
            motor.SuppressLocalSimulation = false;
        }

        RestoreFlagHolderVisual();
        SyncTroopVisualsToHealth();
        ApplyTroopVisualFormation();
        RtsEnsureGroundColliders.EnsureSceneGroundColliders();
        Physics.SyncTransforms();
        SnapRegimentToGround(force: true);
        if (snapRegimentRootToGround && isActiveAndEnabled)
        {
            if (retryGroundSnapCoroutine != null)
            {
                StopCoroutine(retryGroundSnapCoroutine);
            }

            retryGroundSnapCoroutine = StartCoroutine(RetryGroundSnapRoutine());
        }

        lastRegimentPosition = transform.position;
    }

    private void Awake()
    {
        motor = GetComponent<RtsUnitMotor>();
        regimentAi = GetComponent<EnemyRegimentAI>();
        CacheMatchStartPose();
        currentHealth = Mathf.Max(1f, maxHealth);

        if (troopGroundLayers == 0)
        {
            troopGroundLayers = RtsGroundUtility.DefaultGroundMask;
        }

        if (footprintCollider == null)
        {
            footprintCollider = GetComponent<Collider>();
        }

        EnsureMovementBoundsCollider();
        EnsureSelectionCollider();

        if (motor != null)
        {
            motor.CanReceiveCommands = motor.IsCommandUnit;
            if (movementBoundsCollider != null)
            {
                motor.SetMovementBoundsCollider(movementBoundsCollider);
            }
            else if (footprintCollider != null)
            {
                motor.SetMovementBoundsCollider(footprintCollider);
            }

            if (footprintCollider != null)
            {
                motor.SetSolidBlockCollider(footprintCollider);
            }

            motor.RefreshOwnColliders();
        }

        CacheTroopPrefabScale();
        CacheFlagHolderPrefabScale();
        EnsureTroopVisuals();
        ApplyTroopVisualFormation();
        lastRegimentPosition = transform.position;

        EnsureFormationRootLevel();
        if (troopVisualRoot != null)
        {
            troopVisualRoot.localScale = Vector3.one;
        }
    }

    private void Start()
    {
        // Battlefield imports often ship with renderers only — add MeshColliders before snapping.
        RtsEnsureGroundColliders.EnsureSceneGroundColliders();
        Physics.SyncTransforms();
        SnapRegimentToGround(force: true);
        if (snapRegimentRootToGround)
        {
            if (retryGroundSnapCoroutine != null)
            {
                StopCoroutine(retryGroundSnapCoroutine);
            }

            retryGroundSnapCoroutine = StartCoroutine(RetryGroundSnapRoutine());
        }
    }

    private System.Collections.IEnumerator RetryGroundSnapRoutine()
    {
        // Mesh colliders / streamed terrain may not be queryable on the first frame.
        for (int i = 0; i < 30; i++)
        {
            yield return null;
            RtsEnsureGroundColliders.EnsureSceneGroundColliders();
            Physics.SyncTransforms();
            if (SnapRegimentRootToGround(smooth: false, force: true))
            {
                troopGroundProjectionDirty = true;
                ProjectTroopVisualsToGround();
                retryGroundSnapCoroutine = null;
                yield break;
            }
        }

        retryGroundSnapCoroutine = null;
    }

    private void CacheMatchStartPose()
    {
        matchStartPosition = transform.position;
        // Keep editor yaw (map-facing); strip pitch/roll so slopes never tilt the regiment.
        authoredYawDegrees = transform.eulerAngles.y;
        matchStartRotation = GetAuthoredLevelRotation();
        hasCachedMatchStartPose = true;
        transform.rotation = matchStartRotation;
    }

    private Quaternion GetAuthoredLevelRotation()
    {
        return Quaternion.Euler(0f, authoredYawDegrees, 0f);
    }

    private bool ShouldRestoreMatchStartPose()
    {
        // Never recenter the tracked CAVE/HMD player (or anything parented under them).
        if (GetComponent<SiegeCommanderArrowHealth>() != null
            || GetComponentInParent<SiegeCommanderArrowHealth>() != null)
        {
            return false;
        }

        Transform player = SiegePlayEnvironment.ResolvePlayerTransform();
        if (player == null)
        {
            return true;
        }

        return transform != player
            && !transform.IsChildOf(player)
            && !player.IsChildOf(transform);
    }

    private void EnsureMovementBoundsCollider()
    {
        if (movementBoundsCollider != null)
        {
            return;
        }

        Transform existing = transform.Find("MovementBounds");
        if (existing != null)
        {
            movementBoundsCollider = existing.GetComponent<Collider>();
            if (movementBoundsCollider != null)
            {
                movementBoundsCollider.isTrigger = true;
                return;
            }
        }

        if (footprintCollider == null)
        {
            footprintCollider = GetComponent<Collider>();
        }

        GameObject boundsObject = new GameObject("MovementBounds");
        boundsObject.transform.SetParent(transform, false);

        BoxCollider movementBox = boundsObject.AddComponent<BoxCollider>();
        // Trigger only — used for cast shape, must never physically push the regiment.
        movementBox.isTrigger = true;

        if (footprintCollider is BoxCollider footprintBox)
        {
            movementBox.center = footprintBox.center;
            movementBox.size = footprintBox.size * movementBoundsScale;
        }
        else if (footprintCollider != null)
        {
            Bounds bounds = footprintCollider.bounds;
            movementBox.center = transform.InverseTransformPoint(bounds.center);
            Vector3 localSize = transform.InverseTransformVector(bounds.size) * movementBoundsScale;
            movementBox.size = new Vector3(
                Mathf.Abs(localSize.x),
                Mathf.Max(0.5f, Mathf.Abs(localSize.y)),
                Mathf.Abs(localSize.z));
        }
        else
        {
            movementBox.size = new Vector3(1.2f, 1f, 1.2f) * movementBoundsScale;
        }

        movementBoundsCollider = movementBox;
    }

    private void EnsureSelectionCollider()
    {
        if (selectionCollider != null)
        {
            return;
        }

        Transform existing = transform.Find("SelectionVolume");
        if (existing != null)
        {
            selectionCollider = existing.GetComponent<Collider>();
            if (selectionCollider != null)
            {
                ConfigureSelectionCollider(selectionCollider);
                return;
            }
        }

        if (!autoCreateSelectionVolume)
        {
            return;
        }

        if (footprintCollider == null)
        {
            footprintCollider = GetComponent<Collider>();
        }

        GameObject selectionObject = new GameObject("SelectionVolume");
        selectionObject.transform.SetParent(transform, false);

        BoxCollider selectionBox = selectionObject.AddComponent<BoxCollider>();
        selectionBox.isTrigger = true;
        ApplySelectionBoxShape(selectionBox);

        selectionCollider = selectionBox;
        ConfigureSelectionCollider(selectionCollider);
    }

    private void ApplySelectionBoxShape(BoxCollider selectionBox)
    {
        float height = Mathf.Max(0.5f, selectionVolumeHeight);
        float xzScale = Mathf.Max(0.5f, selectionVolumeXZScale);

        if (footprintCollider is BoxCollider footprintBox)
        {
            Vector3 size = footprintBox.size;
            selectionBox.center = new Vector3(
                footprintBox.center.x,
                height * 0.5f,
                footprintBox.center.z);
            selectionBox.size = new Vector3(
                Mathf.Max(0.25f, size.x * xzScale),
                height,
                Mathf.Max(0.25f, size.z * xzScale));
            return;
        }

        if (footprintCollider != null)
        {
            Bounds bounds = footprintCollider.bounds;
            Vector3 localCenter = transform.InverseTransformPoint(bounds.center);
            Vector3 localSize = transform.InverseTransformVector(bounds.size);
            selectionBox.center = new Vector3(localCenter.x, height * 0.5f, localCenter.z);
            selectionBox.size = new Vector3(
                Mathf.Max(0.25f, Mathf.Abs(localSize.x) * xzScale),
                height,
                Mathf.Max(0.25f, Mathf.Abs(localSize.z) * xzScale));
            return;
        }

        selectionBox.center = new Vector3(0f, height * 0.5f, 0f);
        selectionBox.size = new Vector3(2f * xzScale, height, 2f * xzScale);
    }

    private void ConfigureSelectionCollider(Collider collider)
    {
        if (collider == null)
        {
            return;
        }

        collider.isTrigger = true;
        int unitLayer = LayerMask.NameToLayer("RTS_Unit");
        if (unitLayer >= 0)
        {
            collider.gameObject.layer = unitLayer;
        }

        if (collider is BoxCollider box)
        {
            ApplySelectionBoxShape(box);
        }
    }

    private void OnValidate()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        attackDamage = Mathf.Max(0f, attackDamage);
        attackRange = Mathf.Max(0f, attackRange);
        attackCooldown = Mathf.Max(0.05f, attackCooldown);
        rangedAttackDamage = Mathf.Max(0f, rangedAttackDamage);
        rangedAttackRange = Mathf.Max(0f, rangedAttackRange);
        rangedAttackCooldown = Mathf.Max(0.05f, rangedAttackCooldown);
        if (hasRangedAttack)
        {
            rangedAttackRange = Mathf.Max(rangedAttackRange, attackRange + 0.01f);
        }

        rangedProjectileSpeed = Mathf.Max(0.1f, rangedProjectileSpeed);
        rangedProjectileFrequency = Mathf.Clamp(rangedProjectileFrequency, 0.05f, 1f);
        rangedProjectileMinArcHeight = Mathf.Max(0f, rangedProjectileMinArcHeight);
        rangedProjectileMaxArcHeight = Mathf.Max(rangedProjectileMinArcHeight, rangedProjectileMaxArcHeight);
        rangedProjectileDispersion = Mathf.Clamp01(rangedProjectileDispersion);
        rangedProjectileMaxSpreadRadius = Mathf.Max(0f, rangedProjectileMaxSpreadRadius);
        rangedProjectileLaunchHeight = Mathf.Max(0f, rangedProjectileLaunchHeight);
        targetScanInterval = Mathf.Max(0.05f, targetScanInterval);
        maxUnitCount = Mathf.Max(0, maxUnitCount);
        defeatedUnitPercentage = Mathf.Clamp01(defeatedUnitPercentage);
        formationCellSpacing = Mathf.Max(0.01f, formationCellSpacing);
        formationJitterFraction = Mathf.Clamp(formationJitterFraction, 0f, 0.5f);
        idleRegenPerSecond = Mathf.Max(0f, idleRegenPerSecond);
        campRegenPerSecond = Mathf.Max(0f, campRegenPerSecond);
        retreatInvulnerabilityDuration = Mathf.Max(0f, retreatInvulnerabilityDuration);
        retreatMoveSpeedMultiplier = Mathf.Max(1f, retreatMoveSpeedMultiplier);
        retreatDestinationRefreshInterval = Mathf.Max(0.05f, retreatDestinationRefreshInterval);
        retreatDeathDisappearSpan = Mathf.Max(0f, retreatDeathDisappearSpan);
        combatMoveSpeedPercentage = Mathf.Clamp01(combatMoveSpeedPercentage);
        combatOverlapStartSpeedPercentage = Mathf.Clamp(
            combatOverlapStartSpeedPercentage,
            combatMoveSpeedPercentage,
            1f);
        combatOverlapSmoothingSpeed = Mathf.Max(0f, combatOverlapSmoothingSpeed);
        selectionVolumeHeight = Mathf.Max(0.5f, selectionVolumeHeight);
        selectionVolumeXZScale = Mathf.Max(0.5f, selectionVolumeXZScale);
        troopGroundRayStartHeight = Mathf.Max(1f, troopGroundRayStartHeight);
        maxTroopVisualStepHeight = Mathf.Max(0.1f, maxTroopVisualStepHeight);
        maxRegimentGroundRisePerSnap = Mathf.Max(0.05f, maxRegimentGroundRisePerSnap);
        groundSnapHysteresis = Mathf.Max(0f, groundSnapHysteresis);
        groundSnapSmoothSpeed = Mathf.Max(0f, groundSnapSmoothSpeed);
    }

    private void Update()
    {
        if (CurrentState == State.Dead || !SiegeMatchSettings.HasTroopCombat || IsPointCaptureRaising)
        {
            return;
        }

        SyncTroopVisualScale();

        switch (CurrentState)
        {
            case State.Retreat:
                UpdateRetreat();
                break;
            case State.Regroup:
                if (!UsesRemoteCombatAuthority())
                {
                    UpdateRegroup();
                }

                break;
            default:
                if (!UsesRemoteCombatAuthority())
                {
                    UpdateRecovery();
                    UpdateCombat();
                }

                break;
        }

        if ((CurrentState == State.Retreat || CurrentState == State.Regroup) && !UsesRemoteCombatAuthority())
        {
            UpdateRecovery();
        }

        UpdateMovementSpeed();
    }

    private void LateUpdate()
    {
        if (isPermanentlyEliminated || CurrentState == State.Dead)
        {
            // Hold the corpse rock-steady while troop visuals disappear one-by-one.
            MaintainLockedDeathPose();
            return;
        }

        // Level root (no pitch/roll) but keep the editor-authored yaw.
        EnsureFormationRootLevel();

        if (motor != null)
        {
            motor.TraverseGateCorridor = IsTraversingGate
                || (regimentAi != null && regimentAi.IsExitingGate);
        }

        bool isRegimentIdle = IsRegimentMovementIdle();
        bool moved = motor != null && motor.MovedHorizontallyThisFrame;
        bool blockedOrStuck = motor != null && (motor.IsBlockedBySolidObstacle || motor.IsStuck);

        if (isRegimentIdle)
        {
            MaintainIdleGroundLock();
        }
        else
        {
            ClearIdleGroundLock();

            // Skip ground snap while jammed — vibrating against solids was climbing bad probes (float-up).
            if (keepRegimentRootOnGroundWhileMoving && moved && !blockedOrStuck)
            {
                SnapRegimentRootToGround(smooth: false, force: false);
                EnsureFormationRootLevel();
            }
        }

        UpdateTroopFacing();

        // Never re-project while idle — that was causing troops to slowly crawl downhill.
        if ((moved && !blockedOrStuck) || troopGroundProjectionDirty)
        {
            ProjectTroopVisualsToGround();
        }
    }

    private bool IsRegimentMovementIdle()
    {
        if (networkOwnerPoseHalted && UsesRemoteCombatAuthority())
        {
            return true;
        }

        return motor == null || (!motor.HasDestination && !motor.MovedHorizontallyThisFrame);
    }

    /// <summary>
    /// PVP peer view: owner says this regiment is stopped — mirror solo idle hardening.
    /// </summary>
    public void SetNetworkOwnerPoseHalted(bool halted)
    {
        if (networkOwnerPoseHalted == halted)
        {
            return;
        }

        networkOwnerPoseHalted = halted;
        if (halted)
        {
            if (motor != null)
            {
                motor.Stop();
            }

            ClearIdleGroundLock();
        }
    }

    /// <summary>After a network pose snap/lerp, refresh ground height so peers don't sink or float.</summary>
    public void NotifyNetworkPositionApplied(bool hardSnap)
    {
        if (!UsesRemoteCombatAuthority())
        {
            return;
        }

        ClearIdleGroundLock();
        if (keepRegimentRootOnGroundWhileMoving || hardSnap)
        {
            SnapRegimentRootToGround(smooth: !hardSnap, force: hardSnap);
            EnsureFormationRootLevel();
            troopGroundProjectionDirty = true;
        }
    }

    private void MaintainIdleGroundLock()
    {
        // Remote peer: when owner is halted, use the same full pose lock as solo.
        bool freezeFullPose = !UsesRemoteCombatAuthority() || networkOwnerPoseHalted;
        Vector3 position = transform.position;

        if (!hasLockedRegimentGroundY)
        {
            SnapRegimentRootToGround(smooth: false, force: false);
            position = transform.position;
            lockedRegimentGroundY = position.y;
            lockedRegimentGroundXZ = new Vector3(position.x, 0f, position.z);
            hasLockedRegimentGroundY = true;
            troopGroundProjectionDirty = true;
            return;
        }

        if (freezeFullPose)
        {
            Vector3 locked = new Vector3(lockedRegimentGroundXZ.x, lockedRegimentGroundY, lockedRegimentGroundXZ.z);
            float divergenceSqr = (position - locked).sqrMagnitude;
            // Network snap while halted — adopt the new pose instead of snapping back to the old lock.
            if (divergenceSqr > 1f)
            {
                SnapRegimentRootToGround(smooth: false, force: false);
                position = transform.position;
                lockedRegimentGroundY = position.y;
                lockedRegimentGroundXZ = new Vector3(position.x, 0f, position.z);
                troopGroundProjectionDirty = true;
                return;
            }

            if (divergenceSqr > 0.0000001f)
            {
                transform.position = locked;
            }

            return;
        }

        Vector3 xzDelta = new Vector3(position.x, 0f, position.z) - lockedRegimentGroundXZ;
        if (xzDelta.sqrMagnitude > 0.04f)
        {
            SnapRegimentRootToGround(smooth: false, force: false);
            position = transform.position;
            lockedRegimentGroundY = position.y;
            lockedRegimentGroundXZ = new Vector3(position.x, 0f, position.z);
            troopGroundProjectionDirty = true;
            return;
        }

        if (Mathf.Abs(position.y - lockedRegimentGroundY) > 0.0001f)
        {
            position.y = lockedRegimentGroundY;
            transform.position = position;
        }
    }

    private void ClearIdleGroundLock()
    {
        hasLockedRegimentGroundY = false;
    }

    private void SnapRegimentToGround(bool force)
    {
        ClearIdleGroundLock();

        if (!snapRegimentRootToGround)
        {
            if (force || projectTroopVisualsToGround)
            {
                troopGroundProjectionDirty = true;
                ProjectTroopVisualsToGround();
            }

            return;
        }

        SnapRegimentRootToGround(smooth: false, force: true);
        EnsureFormationRootLevel();
        troopGroundProjectionDirty = true;
        ProjectTroopVisualsToGround();
    }

    private bool SnapRegimentRootToGround(bool smooth, bool force)
    {
        LayerMask groundMask = ResolveGroundMask();
        Vector3 position = transform.position;
        // Moving: stay near current height. Force/init: allow large drops from authored y≈100.
        float maxVerticalSnap = force ? 512f : 8f;
        if (!RtsGroundUtility.TrySampleGroundY(
                position.x,
                position.z,
                groundMask,
                troopGroundRayStartHeight,
                regimentGroundYOffset,
                out float groundY,
                preferredY: position.y,
                maxVerticalSnap: maxVerticalSnap))
        {
            if (!loggedMissingGround)
            {
                loggedMissingGround = true;
                Debug.LogWarning(
                    "TroopCombat could not find walkable ground under '"
                    + name
                    + "' at XZ ("
                    + position.x.ToString("F1")
                    + ", "
                    + position.z.ToString("F1")
                    + "). Add a MeshCollider/TerrainCollider on the battlefield (layer RTS_Ground preferred).",
                    this);
            }

            return false;
        }

        loggedMissingGround = false;

        float rise = groundY - position.y;
        if (!force && rise > maxRegimentGroundRisePerSnap)
        {
            // Far above = cliff ledge. Leave Y alone rather than jump.
            if (rise > Mathf.Max(2f, maxRegimentGroundRisePerSnap * 4f))
            {
                return true;
            }

            // Gentle uphill: chase ground gradually so we never lag underground for long.
            groundY = position.y + maxRegimentGroundRisePerSnap;
        }

        float delta = groundY - position.y;
        if (!force && Mathf.Abs(delta) < groundSnapHysteresis)
        {
            return true;
        }

        if (smooth && groundSnapSmoothSpeed > 0f && !force)
        {
            float maxStep = groundSnapSmoothSpeed * Time.deltaTime;
            position.y = Mathf.MoveTowards(position.y, groundY, maxStep);
        }
        else
        {
            position.y = groundY;
        }

        if (Mathf.Abs(transform.position.y - position.y) > 0.0001f)
        {
            transform.position = position;
            troopGroundProjectionDirty = true;
        }

        return true;
    }

    private bool SnapRegimentRootToGround()
    {
        return SnapRegimentRootToGround(smooth: false, force: false);
    }

    private LayerMask ResolveGroundMask()
    {
        return troopGroundLayers.value != 0
            ? troopGroundLayers
            : RtsGroundUtility.DefaultGroundMask;
    }

    private void ProjectTroopVisualsToGround()
    {
        if (!projectTroopVisualsToGround || troopVisuals.Count == 0)
        {
            troopGroundProjectionDirty = false;
            return;
        }

        Vector3 regimentPosition = transform.position;
        LayerMask groundMask = ResolveGroundMask();
        LayerMask solidMask = RtsGroundUtility.DefaultSolidMask;
        float planeY = regimentPosition.y;
        float hysteresis = Mathf.Max(0.01f, groundSnapHysteresis);

        for (int i = 0; i < troopVisuals.Count; i++)
        {
            TroopVisualInstance troopVisual = troopVisuals[i];
            if (troopVisual.Instance == null || !troopVisual.Instance.activeSelf)
            {
                continue;
            }

            Transform visualTransform = troopVisual.Instance.transform;
            Transform parent = visualTransform.parent;
            Vector3 local = visualTransform.localPosition;

            Vector3 worldSlot = parent != null
                ? parent.TransformPoint(new Vector3(local.x, 0f, local.z))
                : new Vector3(visualTransform.position.x, planeY, visualTransform.position.z);

            float groundY = planeY;
            bool sampled = RtsGroundUtility.TrySampleGroundY(
                worldSlot.x,
                worldSlot.z,
                groundMask,
                troopGroundRayStartHeight,
                troopGroundYOffset,
                out groundY,
                preferredY: planeY,
                maxVerticalSnap: 8f);

            if (!sampled)
            {
                groundY = planeY;
            }

            // Outer troops over a solid: stay on the regiment plane (may sit inside the solid)
            // instead of climbing onto the obstacle top.
            if (sampled
                && groundY > planeY + 0.15f
                && WouldElevateOntoSolid(worldSlot.x, worldSlot.z, planeY, groundY, solidMask))
            {
                groundY = planeY;
            }

            // Same-mesh cliff tops are still RTS_Ground — clamp visual climb so troops don't mount ledges.
            float maxVisualRise = Mathf.Max(0.1f, maxTroopVisualStepHeight);
            if (groundY > planeY + maxVisualRise)
            {
                groundY = planeY;
            }

            float currentWorldY = parent != null
                ? parent.TransformPoint(local).y
                : visualTransform.position.y;
            if (Mathf.Abs(currentWorldY - groundY) < hysteresis && !troopGroundProjectionDirty)
            {
                continue;
            }

            if (parent != null)
            {
                Vector3 targetWorld = new Vector3(worldSlot.x, groundY, worldSlot.z);
                float localY = parent.InverseTransformPoint(targetWorld).y;
                visualTransform.localPosition = new Vector3(local.x, localY, local.z);
            }
            else
            {
                Vector3 world = visualTransform.position;
                world.y = groundY;
                visualTransform.position = world;
            }
        }

        lastGroundProjectionRegimentPosition = new Vector3(
            regimentPosition.x,
            transform.position.y,
            regimentPosition.z);
        troopGroundProjectionDirty = false;
    }

    private static bool WouldElevateOntoSolid(
        float worldX,
        float worldZ,
        float planeY,
        float groundY,
        LayerMask solidMask)
    {
        if (solidMask == 0)
        {
            return false;
        }

        float midY = (planeY + groundY) * 0.5f;
        float halfHeight = Mathf.Max(0.35f, (groundY - planeY) * 0.5f + 0.2f);
        if (Physics.CheckBox(
                new Vector3(worldX, midY, worldZ),
                new Vector3(0.4f, halfHeight, 0.4f),
                Quaternion.identity,
                solidMask,
                QueryTriggerInteraction.Ignore))
        {
            return true;
        }

        return Physics.CheckSphere(
            new Vector3(worldX, groundY + 0.25f, worldZ),
            0.45f,
            solidMask,
            QueryTriggerInteraction.Ignore);
    }

    private void UpdateMovementSpeed()
    {
        if (motor == null)
        {
            return;
        }

        float baseMultiplier;
        if (regimentAi != null && regimentAi.IsExitingGate)
        {
            // Full march speed through the gate — never apply Fight slowdown here.
            baseMultiplier = 1f;
        }
        else
        {
            switch (CurrentState)
            {
                case State.Retreat:
                    baseMultiplier = retreatMoveSpeedMultiplier;
                    break;
                case State.Fight:
                    baseMultiplier = GetCombatMoveSpeedMultiplier();
                    break;
                default:
                    baseMultiplier = 1f;
                    break;
            }
        }

        motor.MoveSpeedMultiplier = baseMultiplier * SiegeMatchSettings.ActiveMoveSpeedScale;
    }

    private float GetCombatMoveSpeedMultiplier()
    {
        if (CurrentState != State.Fight)
        {
            return 1f;
        }

        if (!scaleCombatSpeedByEnemyOverlap)
        {
            return combatOverlapStartSpeedPercentage;
        }

        float targetOverlap = GetEnemyOverlapRatio();
        smoothedCombatOverlap = Mathf.MoveTowards(
            smoothedCombatOverlap,
            targetOverlap,
            combatOverlapSmoothingSpeed * Time.deltaTime);

        // No contact yet — full march speed. Any overlap drops into the combat band
        // starting at combatOverlapStartSpeedPercentage, then down to the min with more overlap.
        if (smoothedCombatOverlap <= 0.001f)
        {
            return 1f;
        }

        float startSpeed = Mathf.Max(combatOverlapStartSpeedPercentage, combatMoveSpeedPercentage);
        return Mathf.Lerp(startSpeed, combatMoveSpeedPercentage, smoothedCombatOverlap);
    }

    private float GetEnemyOverlapRatio()
    {
        if (!TryGetFootprintBoundsXZ(out Vector2 selfCenter, out Vector2 selfHalfExtents))
        {
            return currentTarget != null ? 1f : 0f;
        }

        float selfArea = (selfHalfExtents.x * 2f) * (selfHalfExtents.y * 2f);
        if (selfArea < 0.0001f)
        {
            return currentTarget != null ? 1f : 0f;
        }

        float scanRadius = Mathf.Max(GetTargetAcquisitionRange(), Mathf.Max(selfHalfExtents.x, selfHalfExtents.y));
        Collider[] hits = Physics.OverlapSphere(transform.position, scanRadius, targetLayers, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
        {
            return 0f;
        }

        float totalOverlapArea = 0f;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null)
            {
                continue;
            }

            TroopCombat enemy = hit.GetComponentInParent<TroopCombat>();
            if (enemy == null || enemy == this || enemy.faction == faction)
            {
                continue;
            }

            if (!CanBeTargetedBy(enemy))
            {
                continue;
            }

            if (!enemy.TryGetFootprintBoundsXZ(out Vector2 enemyCenter, out Vector2 enemyHalfExtents))
            {
                continue;
            }

            totalOverlapArea += CalculateRectOverlapArea(selfCenter, selfHalfExtents, enemyCenter, enemyHalfExtents);
        }

        return Mathf.Clamp01(totalOverlapArea / selfArea);
    }

    public bool TryGetFootprintBoundsXZ(out Vector2 center, out Vector2 halfExtents)
    {
        center = Vector2.zero;
        halfExtents = Vector2.zero;

        if (footprintCollider is BoxCollider boxCollider)
        {
            Vector3 worldCenter = footprintCollider.transform.TransformPoint(boxCollider.center);
            Vector3 worldHalfExtents = Vector3.Scale(boxCollider.size * 0.5f, footprintCollider.transform.lossyScale);
            center = new Vector2(worldCenter.x, worldCenter.z);
            halfExtents = new Vector2(Mathf.Abs(worldHalfExtents.x), Mathf.Abs(worldHalfExtents.z));
            return true;
        }

        if (footprintCollider != null)
        {
            Bounds bounds = footprintCollider.bounds;
            center = new Vector2(bounds.center.x, bounds.center.z);
            halfExtents = new Vector2(bounds.extents.x, bounds.extents.z);
            return true;
        }

        return false;
    }

    /// <summary>XZ footprint size (full width/depth) for editor gizmos and layout helpers.</summary>
    public bool TryGetFootprintSizeXZ(out Vector2 size)
    {
        if (TryGetFootprintBoundsXZ(out _, out Vector2 halfExtents))
        {
            size = halfExtents * 2f;
            return true;
        }

        size = Vector2.zero;
        return false;
    }

    private static float CalculateRectOverlapArea(
        Vector2 centerA,
        Vector2 halfExtentsA,
        Vector2 centerB,
        Vector2 halfExtentsB)
    {
        float overlapWidth = Mathf.Max(
            0f,
            Mathf.Min(centerA.x + halfExtentsA.x, centerB.x + halfExtentsB.x)
            - Mathf.Max(centerA.x - halfExtentsA.x, centerB.x - halfExtentsB.x));
        float overlapDepth = Mathf.Max(
            0f,
            Mathf.Min(centerA.y + halfExtentsA.y, centerB.y + halfExtentsB.y)
            - Mathf.Max(centerA.y - halfExtentsA.y, centerB.y - halfExtentsB.y));

        return overlapWidth * overlapDepth;
    }

    private bool TryGetFootprintBounds(out Bounds bounds)
    {
        if (footprintCollider != null)
        {
            bounds = footprintCollider.bounds;
            return true;
        }

        bounds = default;
        return false;
    }

    private void UpdateRecovery()
    {
        if (PointCaptureMatch.Instance != null)
        {
            return;
        }
        if (currentHealth >= maxHealth)
        {
            if (CurrentState == State.Regroup)
            {
                CompleteRegroup();
            }

            return;
        }

        RtsCampManager campManager = RtsCampManager.Instance;
        bool inCampZone = IsOverlappingCampZone(campManager);
        float recoveryRate = GetRecoveryRate(inCampZone);
        if (recoveryRate <= 0f)
        {
            return;
        }

        currentHealth = Mathf.Min(maxHealth, currentHealth + recoveryRate * Time.deltaTime);
        SyncTroopVisualsToHealth();

        if (CurrentState == State.Regroup && currentHealth >= maxHealth)
        {
            CompleteRegroup();
        }
    }

    private float GetRecoveryRate(bool inCampZone)
    {
        if (currentHealth <= 0f)
        {
            if (CurrentState == State.Regroup)
            {
                return campRegenPerSecond;
            }

            return 0f;
        }

        return inCampZone ? campRegenPerSecond : idleRegenPerSecond;
    }

    private bool IsOverlappingCampZone(RtsCampManager campManager)
    {
        if (campManager == null)
        {
            return false;
        }

        if (TryGetFootprintBounds(out Bounds footprintBounds))
        {
            return campManager.IsFootprintOverlappingCampZone(footprintBounds, faction);
        }

        return campManager.IsInCampZone(transform.position, faction);
    }

    private void UpdateCombat()
    {
        // Gate exit must not enter Fight — ranged units especially acquire through walls and
        // combat slowdown (often 0%) freezes them in ExitingGate with the gate held open.
        if (regimentAi != null && regimentAi.IsExitingGate)
        {
            currentTarget = null;
            if (CurrentState == State.Fight)
            {
                CurrentState = State.Idle;
            }

            smoothedCombatOverlap = 0f;
            return;
        }

        if (currentTarget != null && !currentTarget.CanBeTargetedBy(this))
        {
            currentTarget = null;
            CurrentState = State.Idle;
            smoothedCombatOverlap = 0f;
            return;
        }

        if (currentTarget != null
            && (currentTarget.IsRetreating
                || currentTarget.CurrentState == State.Dead
                || currentTarget.CurrentState == State.Regroup))
        {
            currentTarget = null;
            CurrentState = State.Idle;
            smoothedCombatOverlap = 0f;
            return;
        }

        if (currentTarget != null && !ShouldMaintainCombatTarget(currentTarget))
        {
            currentTarget = null;
            CurrentState = State.Idle;
            smoothedCombatOverlap = 0f;
            return;
        }

        if (Time.time >= nextScanTime)
        {
            nextScanTime = Time.time + Mathf.Max(0.05f, targetScanInterval);
            currentTarget = FindTargetInRange();
        }

        if (currentTarget == null)
        {
            CurrentState = State.Idle;
            smoothedCombatOverlap = 0f;
            return;
        }

        if (!TryGetAttackProfileForTarget(currentTarget, out AttackProfile attackProfile))
        {
            currentTarget = null;
            CurrentState = State.Idle;
            smoothedCombatOverlap = 0f;
            return;
        }

        Vector3 toTarget = currentTarget.transform.position - transform.position;
        toTarget.y = 0f;

        CurrentState = State.Fight;

        if (faceTargetWhileFighting && toTarget.sqrMagnitude > 0.0001f)
        {
            ApplyFacingToTroopVisuals(GetTroopFacingRotation(toTarget.normalized));
        }

        if (Time.time < nextAttackTime)
        {
            return;
        }

        nextAttackTime = Time.time + Mathf.Max(0.05f, attackProfile.Cooldown);
        if (attackProfile.IsRanged)
        {
            SpawnRangedAttackVolley(currentTarget, notifyNetwork: true);
        }
        else
        {
            MeleeAttackPerformed?.Invoke(this, transform.position);
        }

        TryApplyCombatDamage(currentTarget, attackProfile.Damage, attackProfile.IsRanged);
    }

    private void TryApplyCombatDamage(TroopCombat target, float amount, bool isRangedAttack)
    {
        if (target == null || amount <= 0f)
        {
            return;
        }

        lastCombatInteractionTime = Time.time;
        target.lastCombatInteractionTime = Time.time;

        SiegePvpSession session = SiegePvpSession.Instance;
        if (session != null
            && session.IsMatchRunning
            && !session.IsLocallyOwnedTroop(target))
        {
            session.NotifyInflictedDamage(this, target, amount, isRangedAttack);
            return;
        }

        target.TakeDamage(amount, this, isRangedAttack);
    }

    private void SpawnRangedAttackVolley(TroopCombat target, bool notifyNetwork)
    {
        if (rangedProjectilePrefab == null || target == null)
        {
            return;
        }

        int arrowCount = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1, activeTroopVisualCount) * rangedProjectileFrequency));
        SpawnRangedAttackVolleyVisual(target, arrowCount);

        if (notifyNetwork)
        {
            SiegePvpSession session = SiegePvpSession.Instance;
            if (session != null && session.IsMatchRunning)
            {
                session.NotifyRangedVolley(this, target, arrowCount);
            }
        }
    }

    /// <summary>
    /// Visual-only volley (local or peer FX). Damage is applied separately via combat / DMG sync.
    /// </summary>
    public void SpawnRangedAttackVolleyVisual(TroopCombat target, int arrowCount)
    {
        if (rangedProjectilePrefab == null || target == null)
        {
            return;
        }

        // VOLLEY + DMG can both request FX on the peer — keep one volley per shot window.
        if (Time.unscaledTime < nextNetworkVolleyVisualTime
            && UsesRemoteCombatAuthority())
        {
            return;
        }

        if (UsesRemoteCombatAuthority())
        {
            nextNetworkVolleyVisualTime = Time.unscaledTime + 0.12f;
        }

        int count = Mathf.Max(1, arrowCount);
        List<Vector3> launchPoints = GetRangedLaunchPoints(count);
        Vector3 targetCenter = ResolveRangedAimPoint(target);
        float spreadRadius = rangedProjectileDispersion * rangedProjectileMaxSpreadRadius;

        for (int i = 0; i < launchPoints.Count; i++)
        {
            Vector2 impactOffset = Random.insideUnitCircle * spreadRadius;
            Vector3 impactPoint = new Vector3(
                targetCenter.x + impactOffset.x,
                targetCenter.y,
                targetCenter.z + impactOffset.y);

            Vector3 launchPoint = launchPoints[i];
            TroopRangedProjectile.Launch(
                rangedProjectilePrefab,
                launchPoint,
                impactPoint,
                rangedProjectileSpeed,
                Random.Range(rangedProjectileMinArcHeight, rangedProjectileMaxArcHeight));
        }
    }

    private Vector3 ResolveRangedAimPoint(TroopCombat target)
    {
        Vector3 aim = target.transform.position;
        if (RtsGroundUtility.TrySampleWalkableGroundForMovement(
                aim.x,
                aim.z,
                preferredY: aim.y,
                out float groundY,
                out _))
        {
            aim.y = groundY;
        }

        // Aim at roughly chest height so arcs stay visible above ground.
        aim.y += Mathf.Max(0.35f, rangedProjectileLaunchHeight * 0.35f);
        return aim;
    }

    private List<Vector3> GetRangedLaunchPoints(int desiredCount)
    {
        List<Vector3> availablePoints = new List<Vector3>();
        for (int i = 0; i < troopVisuals.Count; i++)
        {
            TroopVisualInstance troopVisual = troopVisuals[i];
            if (troopVisual.IsFlagHolder || troopVisual.Instance == null || !troopVisual.Instance.activeSelf)
            {
                continue;
            }

            availablePoints.Add(ResolveRangedLaunchPoint(troopVisual.Instance.transform.position));
        }

        if (availablePoints.Count == 0)
        {
            availablePoints.Add(ResolveRangedLaunchPoint(transform.position));
        }

        ShuffleLaunchPoints(availablePoints);

        List<Vector3> selectedPoints = new List<Vector3>(desiredCount);
        for (int i = 0; i < desiredCount; i++)
        {
            selectedPoints.Add(availablePoints[i % availablePoints.Count]);
        }

        return selectedPoints;
    }

    private Vector3 ResolveRangedLaunchPoint(Vector3 worldPoint)
    {
        Vector3 launch = worldPoint;
        if (RtsGroundUtility.TrySampleWalkableGroundForMovement(
                launch.x,
                launch.z,
                preferredY: launch.y,
                out float groundY,
                out _))
        {
            launch.y = groundY + rangedProjectileLaunchHeight;
        }
        else
        {
            // Before ground projection / if visuals are still at a bad Y, prefer regiment root.
            float fallbackY = transform.position.y + rangedProjectileLaunchHeight;
            if (!IsFinite(launch.y) || Mathf.Abs(launch.y - transform.position.y) > 25f)
            {
                launch.y = fallbackY;
            }
            else
            {
                launch.y += rangedProjectileLaunchHeight;
            }
        }

        return launch;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static void ShuffleLaunchPoints(List<Vector3> points)
    {
        for (int i = points.Count - 1; i > 0; i--)
        {
            int swapIndex = Random.Range(0, i + 1);
            Vector3 temp = points[i];
            points[i] = points[swapIndex];
            points[swapIndex] = temp;
        }
    }

    private void UpdateRetreat()
    {
        if (UsesRemoteCombatAuthority())
        {
            return;
        }

        RtsCampManager campManager = RtsCampManager.Instance;
        bool pointCaptureRetreat = PointCaptureMatch.Instance != null;

        if (HasReachedRetreatCamp(campManager))
        {
            if (pointCaptureRetreat)
            {
                CompletePointCaptureRetreatArrival();
            }
            else
            {
                EnterRegroup();
            }

            return;
        }

        if (!pointCaptureRetreat && campManager != null && retreatPhase == RetreatPhase.ToGateOutside && campManager.IsAtGateOutside(transform.position, faction))
        {
            retreatPhase = ShouldUseGateInsideWaypoint(campManager)
                ? RetreatPhase.ToGateInside
                : RetreatPhase.ToCamp;
            if (motor != null)
            {
                IssueRetreatMoveTo(GetRetreatDestination(campManager));
            }
        }
        else if (!pointCaptureRetreat && campManager != null && retreatPhase == RetreatPhase.ToGateInside && campManager.IsAtGateInside(transform.position, faction))
        {
            retreatPhase = RetreatPhase.ToCamp;
            if (motor != null)
            {
                IssueRetreatMoveTo(GetRetreatDestination(campManager));
            }
        }
        else if (motor != null)
        {
            if (!pointCaptureRetreat
                && campManager != null
                && (retreatPhase == RetreatPhase.ToGateOutside || retreatPhase == RetreatPhase.ToGateInside))
            {
                if (!motor.HasDestination && !motor.IsBlockedBySolidObstacle)
                {
                    IssueRetreatMoveTo(GetRetreatDestination(campManager));
                }
                else if (motor.IsBlockedBySolidObstacle || motor.IsStuck)
                {
                    TryRecoverRetreatMovement(campManager);
                }
            }
            else if (!HasReachedRetreatCamp(campManager)
                && !motor.HasDestination
                && !motor.IsBlockedBySolidObstacle)
            {
                IssueRetreatMoveTo(GetRetreatDestination(campManager));
            }
            else if (motor.IsBlockedBySolidObstacle || motor.IsStuck)
            {
                TryRecoverRetreatMovement(campManager);
            }
        }

        if (Time.time >= nextRetreatDestinationRefreshTime && motor != null)
        {
            nextRetreatDestinationRefreshTime = Time.time + retreatDestinationRefreshInterval;
            if (motor.IsBlockedBySolidObstacle || motor.IsStuck)
            {
                TryRecoverRetreatMovement(campManager);
            }
            else
            {
                UpdateRetreatDestination();
            }
        }

        if (Time.time >= invulnerableUntil && IsInterceptedByEnemy())
        {
            PermanentDestroy();
        }
    }

    private void UpdateRegroup()
    {
        if (motor != null)
        {
            motor.Stop();
        }
    }

    private void UpdateRetreatDestination()
    {
        if (motor == null)
        {
            return;
        }

        RtsCampManager campManager = RtsCampManager.Instance;
        if (campManager == null)
        {
            return;
        }

        IssueRetreatMoveTo(GetRetreatDestination(campManager));
    }

    private void IssueRetreatMoveTo(Vector3 destination)
    {
        if (motor == null)
        {
            return;
        }

        motor.MoveTo(destination);
        BroadcastOwnedRetreatDestination(destination);
    }

    private void BroadcastOwnedRetreatDestination(Vector3 destination)
    {
        if (UsesRemoteCombatAuthority() || CurrentState != State.Retreat)
        {
            return;
        }

        SiegePvpSession session = SiegePvpSession.Instance;
        if (session != null && session.IsMatchRunning)
        {
            session.NotifyRetreatDestinationFromAuthority(motor, destination);
        }
    }

    private void TryRecoverRetreatMovement(RtsCampManager campManager)
    {
        if (motor == null || campManager == null)
        {
            return;
        }

        if (Time.time < nextRetreatUnstuckTime)
        {
            return;
        }

        nextRetreatUnstuckTime = Time.time + 0.3f;

        if (motor.TryEscapeFromSolid())
        {
            return;
        }

        if (motor.TryRequestDetour())
        {
            return;
        }

        Vector3 goal = GetRetreatDestination(campManager);
        Vector3 recovery = GetRetreatRecoveryDestination(goal);
        if (!motor.IsWorldPositionClear(recovery))
        {
            // Try a few more offsets before committing to a blocked point.
            for (int i = 0; i < 4; i++)
            {
                recovery = GetRetreatRecoveryDestination(goal);
                if (motor.IsWorldPositionClear(recovery))
                {
                    break;
                }
            }
        }

        IssueRetreatMoveTo(recovery);
    }

    private Vector3 GetRetreatRecoveryDestination(Vector3 goal)
    {
        Vector3 toGoal = goal - transform.position;
        toGoal.y = 0f;
        if (toGoal.sqrMagnitude < 0.0001f)
        {
            toGoal = transform.forward;
        }

        Vector3 forward = toGoal.normalized;
        Vector3 tangent = new Vector3(-forward.z, 0f, forward.x);
        float[] sideMultipliers = { 1f, -1f, 1.5f, -1.5f, 2.5f, -2.5f, 3.5f, -3.5f };
        int sideIndex = retreatUnstuckAttemptIndex % sideMultipliers.Length;
        retreatUnstuckAttemptIndex++;

        Vector3 recovery = transform.position
            + tangent * (4f * sideMultipliers[sideIndex])
            + forward * 1.25f;
        recovery.y = transform.position.y;
        return recovery;
    }

    private Vector3 GetRetreatDestination(RtsCampManager campManager)
    {
        if (PointCaptureMatch.Instance != null)
        {
            PointCaptureBoard board = PointCaptureBoard.Instance;
            if (board != null)
            {
                CaptureOwner owner = CaptureTeams.FromTroopFaction(faction);
                if (board.TryGetRecoveryDestination(owner, transform.position, out Vector3 recoveryDestination))
                {
                    return recoveryDestination;
                }
            }

            if (campManager != null)
            {
                return campManager.GetCampCenter(faction);
            }

            return transform.position;
        }

        if (campManager == null)
        {
            return transform.position;
        }

        switch (retreatPhase)
        {
            case RetreatPhase.ToGateOutside:
                return campManager.GetGateOutsidePosition(faction);
            case RetreatPhase.ToGateInside:
                return campManager.GetGateInsidePosition(faction);
            default:
                return campManager.GetCampCenter(faction);
        }
    }

    private bool HasReachedRetreatCamp(RtsCampManager campManager)
    {
        if (PointCaptureMatch.Instance != null)
        {
            PointCaptureBoard board = PointCaptureBoard.Instance;
            if (board != null)
            {
                CaptureOwner owner = CaptureTeams.FromTroopFaction(faction);
                return board.IsInsideOwnedVillageDisc(owner, transform.position);
            }

            return false;
        }

        if (campManager == null)
        {
            return false;
        }

        return campManager.HasReachedCampCenter(transform.position, faction);
    }

    private bool ShouldUseGateInsideWaypoint(RtsCampManager campManager)
    {
        if (campManager == null)
        {
            return false;
        }

        return faction == Faction.Friendly
            ? campManager.FriendlyGateInside != null
            : campManager.EnemyGateInside != null;
    }

    private void UpdateTroopFacing()
    {
        if (troopVisuals.Count == 0 || isPermanentlyEliminated || CurrentState == State.Dead)
        {
            return;
        }

        if (networkOwnerPoseHalted && UsesRemoteCombatAuthority())
        {
            return;
        }

        if (motor != null && motor.IsBlockedBySolidObstacle)
        {
            return;
        }

        // Ignore microscopic idle noise — that was re-applying facing and zeroing root rotation every few frames.
        if (motor == null || !motor.MovedHorizontallyThisFrame)
        {
            return;
        }

        Vector3 facingDirection;
        if (CurrentState == State.Retreat)
        {
            // Face the retreat goal, not slide/escape MoveDirection. Cut-off recovery was
            // reversing MoveDirection and spinning the whole remnant before they disappear.
            facingDirection = GetRetreatFacingDirection();
        }
        else
        {
            facingDirection = motor.MoveDirection;
            facingDirection.y = 0f;
            if (facingDirection.sqrMagnitude < 0.0001f)
            {
                Vector3 regimentDelta = transform.position - lastRegimentPosition;
                regimentDelta.y = 0f;
                if (regimentDelta.sqrMagnitude < MinimumFacingMovementDistance * MinimumFacingMovementDistance)
                {
                    lastRegimentPosition = transform.position;
                    return;
                }

                facingDirection = regimentDelta.normalized;
            }
        }

        if (facingDirection.sqrMagnitude < 0.0001f)
        {
            lastRegimentPosition = transform.position;
            return;
        }

        lastRegimentPosition = transform.position;
        ApplyFacingToTroopVisuals(GetTroopFacingRotation(facingDirection));
    }

    private Vector3 GetRetreatFacingDirection()
    {
        RtsCampManager campManager = RtsCampManager.Instance;
        if (campManager != null)
        {
            Vector3 toGoal = GetRetreatDestination(campManager) - transform.position;
            toGoal.y = 0f;
            if (toGoal.sqrMagnitude > 0.0001f)
            {
                return toGoal;
            }
        }

        Vector3 fallback = motor != null ? motor.MoveDirection : transform.forward;
        fallback.y = 0f;
        return fallback;
    }

    private void EnsureFormationRootLevel()
    {
        // Keep editor yaw; never pitch/roll with terrain.
        Quaternion target = GetAuthoredLevelRotation();
        if (Quaternion.Angle(transform.rotation, target) > 0.01f)
        {
            transform.rotation = target;
        }

        if (troopVisualRoot != null
            && Quaternion.Angle(troopVisualRoot.localRotation, Quaternion.identity) > 0.01f)
        {
            troopVisualRoot.localRotation = Quaternion.identity;
        }
    }

    private void ApplyFacingToTroopVisuals(Quaternion facing)
    {
        if (isPermanentlyEliminated || CurrentState == State.Dead)
        {
            return;
        }

        EnsureFormationRootLevel();

        // Yaw each soldier in local space only — never rotate the regiment root.
        for (int i = 0; i < troopVisuals.Count; i++)
        {
            TroopVisualInstance troopVisual = troopVisuals[i];
            if (troopVisual.Instance == null || !troopVisual.Instance.activeSelf)
            {
                continue;
            }

            troopVisual.Instance.transform.localRotation = facing;
        }
    }

    private Quaternion GetTroopFacingRotation(Vector3 worldDirection)
    {
        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude < 0.0001f)
        {
            return Quaternion.identity;
        }

        float worldYaw = Mathf.Atan2(worldDirection.x, worldDirection.z) * Mathf.Rad2Deg
            + troopFacingYawOffsetDegrees;
        // Root already has authoredYaw — troop local yaw is relative to that.
        float localYaw = Mathf.DeltaAngle(authoredYawDegrees, worldYaw);
        return Quaternion.Euler(0f, localYaw, 0f);
    }

    private bool IsInterceptedByEnemy()
    {
        float scanRadius = Mathf.Max(GetTargetAcquisitionRange(), 8f);
        Collider[] hits = Physics.OverlapSphere(transform.position, scanRadius, targetLayers, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null)
            {
                continue;
            }

            TroopCombat enemy = hit.GetComponentInParent<TroopCombat>();
            if (enemy == null || enemy == this || enemy.faction == faction)
            {
                continue;
            }

            if (!CanBeTargetedBy(enemy))
            {
                continue;
            }

            float interceptRange = GetRetreatInterceptRangeFrom(enemy);
            if (GetHorizontalDistance(transform.position, enemy.transform.position) <= interceptRange)
            {
                return true;
            }
        }

        return false;
    }

    private float GetRetreatInterceptRangeFrom(TroopCombat enemy)
    {
        if (enemy == null)
        {
            return 0f;
        }

        if (!canBeKilledByRangedAttackWhileRetreating)
        {
            return enemy.MeleeAttackRange;
        }

        return enemy.AttackRange;
    }

    private void EnterRetreat()
    {
        CurrentState = State.Retreat;
        currentHealth = 0f;
        currentTarget = null;
        invulnerableUntil = Time.time + retreatInvulnerabilityDuration;
        nextRetreatDestinationRefreshTime = 0f;
        nextRetreatUnstuckTime = 0f;
        retreatUnstuckAttemptIndex = 0;

        RtsCampManager campManager = RtsCampManager.Instance;
        retreatPhase = PointCaptureMatch.Instance != null || campManager == null || !campManager.HasGate(faction)
            ? RetreatPhase.ToCamp
            : RetreatPhase.ToGateOutside;

        if (PointCaptureMatch.Instance != null)
        {
            BeginPointCaptureRetreatTracking();
        }

        if (motor != null)
        {
            motor.CanReceiveCommands = false;
            motor.MoveSpeedMultiplier = retreatMoveSpeedMultiplier;
            UpdateRetreatDestination();
        }

        SyncTroopVisualsToHealth(forceMinimum: true);
        retreatVisualsSyncedToDefeat = true;
        SetFlagHolderDefeatedVisual();
        RegimentEnteredRetreat?.Invoke(this);
        NotifyOwnedCombatAuthorityChanged();
        ClearLocalAggressorsTargetingMe();
    }

    private void OnDisable()
    {
        EndPointCaptureRetreatTracking();
    }

    private void BeginPointCaptureRetreatTracking()
    {
        if (pointCaptureRetreatTracking)
        {
            return;
        }

        PointCaptureBoard board = PointCaptureBoard.Instance;
        if (board == null)
        {
            return;
        }

        board.TerritoryChanged += HandlePointCaptureTerritoryChanged;
        pointCaptureRetreatTracking = true;
    }

    private void EndPointCaptureRetreatTracking()
    {
        if (!pointCaptureRetreatTracking)
        {
            return;
        }

        PointCaptureBoard board = PointCaptureBoard.Instance;
        if (board != null)
        {
            board.TerritoryChanged -= HandlePointCaptureTerritoryChanged;
        }

        pointCaptureRetreatTracking = false;
    }

    private void HandlePointCaptureTerritoryChanged()
    {
        if (CurrentState != State.Retreat)
        {
            return;
        }

        nextRetreatDestinationRefreshTime = 0f;
    }

    internal bool TryPerformImmediateMeleeCounter(TroopCombat attacker, bool suppressCounterReply)
    {
        if (attacker == null
            || CurrentState == State.Dead
            || CurrentState == State.Retreat
            || CurrentState == State.Regroup
            || attacker.CurrentState == State.Dead
            || !IsEngagedForImmediateMeleeCounter(attacker))
        {
            return false;
        }

        if (!TryGetAttackProfileForTarget(attacker, out AttackProfile attackProfile) || attackProfile.IsRanged)
        {
            return false;
        }

        SiegePvpSession session = SiegePvpSession.Instance;
        if (session == null || !session.IsMatchRunning)
        {
            return false;
        }

        nextAttackTime = Time.time + Mathf.Max(0.05f, attackProfile.Cooldown);
        session.NotifyInflictedDamage(this, attacker, attackProfile.Damage, isRangedAttack: false, suppressCounterReply);
        return true;
    }

    private void ClearLocalAggressorsTargetingMe()
    {
        if (!SiegeMatchSettings.IsSiegePvpMode)
        {
            return;
        }

        SiegePvpSession session = SiegePvpSession.Instance;
        if (session == null || !session.IsMatchRunning)
        {
            return;
        }

        session.ClearLocalAggressorsTargeting(this);
    }

    private bool IsEngagedForImmediateMeleeCounter(TroopCombat attacker)
    {
        if (attacker == null)
        {
            return false;
        }

        if (currentTarget == attacker)
        {
            return true;
        }

        if (currentTarget != null)
        {
            return false;
        }

        if (!TryGetAttackProfileForTarget(attacker, out AttackProfile attackProfile) || attackProfile.IsRanged)
        {
            return false;
        }

        return IsWithinMeleeAttackRange(GetHorizontalDistance(transform.position, attacker.transform.position));
    }

    private void EnterRegroup()
    {
        EndPointCaptureRetreatTracking();
        CurrentState = State.Regroup;
        currentTarget = null;
        invulnerableUntil = float.PositiveInfinity;

        if (motor != null)
        {
            motor.Stop();
            motor.CanReceiveCommands = false;
            motor.MoveSpeedMultiplier = 1f;

            if (!UsesRemoteCombatAuthority())
            {
                SiegePvpSession session = SiegePvpSession.Instance;
                if (session != null && session.IsMatchRunning)
                {
                    session.NotifyStopFromCommander(motor);
                }
            }
        }

        NotifyOwnedCombatAuthorityChanged();
    }

    /// <summary>
    /// Point Capture: retreat ends in a safe village disc — become a normal regiment again at current HP.
    /// Village recovery handles healing; no invulnerable regroup state.
    /// </summary>
    private void CompletePointCaptureRetreatArrival()
    {
        EndPointCaptureRetreatTracking();
        CurrentState = State.Idle;
        currentTarget = null;
        invulnerableUntil = 0f;

        if (motor != null)
        {
            motor.Stop();
            motor.MoveSpeedMultiplier = 1f;
            motor.CanReceiveCommands = motor.IsCommandUnit;
        }

        RegimentRegroupCompleted?.Invoke(this);
        NotifyOwnedCombatAuthorityChanged();
    }

    private void EnterRegroupFromNetworkAuthority()
    {
        if (CurrentState == State.Regroup)
        {
            invulnerableUntil = float.PositiveInfinity;
            return;
        }

        EnterRegroup();
    }

    private void CompleteRegroup()
    {
        if (CurrentState != State.Regroup)
        {
            return;
        }

        currentHealth = maxHealth;
        CurrentState = State.Idle;
        RestoreFlagHolderVisual();
        SyncTroopVisualsToHealth();

        if (motor != null)
        {
            bool canCommand = motor.IsCommandUnit && (
                faction == Faction.Friendly
                || (faction == Faction.Enemy && SiegeMatchSettings.IsSiegePvpMode));
            motor.CanReceiveCommands = canCommand;
            motor.MoveSpeedMultiplier = 1f;
        }

        if (faction == Faction.Enemy)
        {
            // In PVP the city defender stages outside the gate after regroup instead of sitting behind walls.
            if (!SiegeMatchSettings.IsSiegePvpMode)
            {
                SetHoldInCampUntilNextWave(true);
            }
            else
            {
                SetHoldInCampUntilNextWave(false);
            }
        }

        RegimentRegroupCompleted?.Invoke(this);
        NotifyOwnedCombatAuthorityChanged();
    }

    private void CompleteRegroupFromNetworkAuthority(float authorityHealth)
    {
        if (CurrentState == State.Regroup && authorityHealth >= maxHealth - 0.5f)
        {
            CompleteRegroup();
            return;
        }

        if (CurrentState != State.Regroup)
        {
            EnterRegroupFromNetworkAuthority();
        }

        currentHealth = Mathf.Max(0f, authorityHealth);
        SyncTroopVisualsToHealth();
    }

    private void ApplyPermanentDestroyFromNetworkAuthority()
    {
        if (!isPermanentlyEliminated && CurrentState != State.Dead)
        {
            PermanentDestroy(null);
            return;
        }

        if (retreatDeathDisappearCoroutine != null)
        {
            return;
        }

        if (!gameObject.activeSelf)
        {
            return;
        }

        if (HasActiveTroopVisuals())
        {
            if (!hasLockedDeathPose)
            {
                EnsureFormationRootLevel();
                LockDeathPose();
            }

            if (motor != null)
            {
                motor.Stop();
                motor.SuppressLocalSimulation = true;
            }

            retreatDeathDisappearCoroutine = StartCoroutine(PlayRetreatDeathDisappearSequence());
            return;
        }

        if (!isPermanentlyEliminated)
        {
            isPermanentlyEliminated = true;
            CurrentState = State.Dead;
        }

        CompletePermanentDestroy();
    }

    private void ApplyActiveStateFromNetworkAuthority(float authorityHealth)
    {
        if (isPermanentlyEliminated || CurrentState == State.Dead)
        {
            return;
        }

        if (CurrentState == State.Regroup || CurrentState == State.Retreat)
        {
            CurrentState = State.Idle;
            currentTarget = null;
            invulnerableUntil = 0f;
            retreatVisualsSyncedToDefeat = false;
            RestoreFlagHolderVisual();
        }

        if (Mathf.Abs(currentHealth - authorityHealth) > 0.25f)
        {
            currentHealth = Mathf.Max(0f, authorityHealth);
            SyncTroopVisualsToHealth();
        }
    }

    public void TakeDamage(float amount, TroopCombat attacker = null, bool isRangedAttack = false)
    {
        if (CurrentState == State.Dead || IsPointCaptureRaising)
        {
            return;
        }

        if (ShouldIgnoreLocalCombatDamage())
        {
            return;
        }

        lastCombatInteractionTime = Time.time;
        if (attacker != null)
        {
            attacker.lastCombatInteractionTime = Time.time;
        }

        if (CurrentState == State.Regroup)
        {
            return;
        }

        if (CurrentState == State.Retreat)
        {
            if (Time.time < invulnerableUntil)
            {
                return;
            }

            if (attacker != null && attacker.CurrentState == State.Retreat)
            {
                return;
            }

            if (attacker != null && !CanBeFinishedByAttackerWhileRetreating(attacker, isRangedAttack))
            {
                return;
            }

            PermanentDestroy(attacker);
            return;
        }

        currentHealth = Mathf.Max(0f, currentHealth - Mathf.Max(0f, amount));

        if (troopVisuals.Count > 0)
        {
            ApplyCasualtyLoss();
        }

        if (currentHealth <= 0f)
        {
            if (ShouldForceMutualRetreatInPointCapture(attacker))
            {
                attacker.EnterRetreat();
            }

            EnterRetreat();
        }

        NotifyOwnedCombatAuthorityChanged();
    }

    private bool ShouldForceMutualRetreatInPointCapture(TroopCombat attacker)
    {
        if (PointCaptureMatch.Instance == null
            || attacker == null
            || attacker == this
            || attacker.faction == faction
            || attacker.CurrentState == State.Dead
            || attacker.CurrentState == State.Retreat
            || attacker.CurrentState == State.Regroup)
        {
            return false;
        }

        // "Equal sides" in Point Capture means mirrored regiments with effectively the same combat profile.
        if (attacker.maxUnitCount != maxUnitCount
            || attacker.hasRangedAttack != hasRangedAttack
            || Mathf.Abs(attacker.maxHealth - maxHealth) > 0.01f
            || Mathf.Abs(attacker.attackDamage - attackDamage) > 0.01f
            || Mathf.Abs(attacker.attackCooldown - attackCooldown) > 0.01f
            || Mathf.Abs(attacker.attackRange - attackRange) > 0.01f)
        {
            return false;
        }

        if (hasRangedAttack)
        {
            if (Mathf.Abs(attacker.rangedAttackDamage - rangedAttackDamage) > 0.01f
                || Mathf.Abs(attacker.rangedAttackCooldown - rangedAttackCooldown) > 0.01f
                || Mathf.Abs(attacker.rangedAttackRange - rangedAttackRange) > 0.01f)
            {
                return false;
            }
        }

        // Only apply when they are actually fighting each other, not from unrelated splash / third-party pressure.
        return attacker.currentTarget == this || currentTarget == attacker;
    }

    /// <summary>
    /// Point Capture: heal while resting inside a friendly village disc.
    /// </summary>
    public void ApplyPointCaptureVillageRecovery(float healthPerSecond, float deltaTime)
    {
        if (deltaTime <= 0f
            || CurrentState == State.Dead
            || IsPermanentlyEliminated
            || CurrentState == State.Fight
            || CurrentState == State.Retreat
            || currentHealth >= maxHealth)
        {
            return;
        }

        currentHealth = Mathf.Min(maxHealth, currentHealth + healthPerSecond * deltaTime);
        SyncTroopVisualsToHealth();

        if (currentHealth >= maxHealth)
        {
            RestoreFlagHolderVisual();
        }
    }

    public void BeginPointCaptureRaiseReveal(float durationSeconds)
    {
        if (pointCaptureRaiseCoroutine != null)
        {
            StopCoroutine(pointCaptureRaiseCoroutine);
        }

        pointCaptureRaiseCoroutine = StartCoroutine(PointCaptureRaiseRevealRoutine(Mathf.Max(0.1f, durationSeconds)));
    }

    private System.Collections.IEnumerator PointCaptureRaiseRevealRoutine(float durationSeconds)
    {
        IsPointCaptureRaising = true;
        EnsureTroopVisuals();
        if (motor != null)
        {
            motor.CanReceiveCommands = false;
            motor.Stop();
        }

        List<int> appearOrder = BuildRandomRaiseAppearOrder();
        for (int i = 0; i < troopVisuals.Count; i++)
        {
            if (troopVisuals[i].Instance != null)
            {
                troopVisuals[i].Instance.SetActive(false);
            }
        }

        currentHealth = 0.01f;
        invulnerableUntil = Time.time + durationSeconds + 0.15f;
        activeTroopVisualCount = 0;

        int appearCount = appearOrder.Count;
        if (appearCount == 0)
        {
            currentHealth = maxHealth;
            IsPointCaptureRaising = false;
            if (motor != null)
            {
                motor.CanReceiveCommands = motor.IsCommandUnit;
            }

            pointCaptureRaiseCoroutine = null;
            yield break;
        }

        float interval = durationSeconds / appearCount;
        for (int i = 0; i < appearCount; i++)
        {
            int visualIndex = appearOrder[i];
            if (visualIndex >= 0 && visualIndex < troopVisuals.Count && troopVisuals[visualIndex].Instance != null)
            {
                troopVisuals[visualIndex].Instance.SetActive(true);
                activeTroopVisualCount++;
            }

            currentHealth = Mathf.Lerp(0.01f, maxHealth, (i + 1) / (float)appearCount);
            yield return new WaitForSeconds(interval);
        }

        currentHealth = maxHealth;
        SyncTroopVisualsToHealth();
        IsPointCaptureRaising = false;
        if (motor != null)
        {
            motor.CanReceiveCommands = motor.IsCommandUnit;
        }

        pointCaptureRaiseCoroutine = null;
    }

    private List<int> BuildRandomRaiseAppearOrder()
    {
        List<int> regularTroops = new List<int>();
        int flagHolderIndex = -1;
        for (int i = 0; i < troopVisuals.Count; i++)
        {
            if (troopVisuals[i].Instance == null)
            {
                continue;
            }

            if (troopVisuals[i].IsFlagHolder)
            {
                flagHolderIndex = i;
                continue;
            }

            regularTroops.Add(i);
        }

        for (int i = regularTroops.Count - 1; i > 0; i--)
        {
            int swapIndex = Random.Range(0, i + 1);
            int temp = regularTroops[i];
            regularTroops[i] = regularTroops[swapIndex];
            regularTroops[swapIndex] = temp;
        }

        if (flagHolderIndex >= 0)
        {
            regularTroops.Add(flagHolderIndex);
        }

        return regularTroops;
    }

    /// <summary>
    /// Siege PVP: non-owner applies owner authority for HP, retreat, and elimination.
    /// </summary>
    public void ApplyNetworkCombatAuthority(
        float authorityHealth,
        int authorityStateCode,
        Vector3 worldPosition,
        float authoritySnapDistance = 2.5f,
        bool authorityRetreatInvulnerable = false)
    {
        // Retreat uses HP=0 while still alive — only state code 2 means eliminated (regroup is 3).
        const int networkCombatStateDead = 2;
        const int networkCombatStateRegroup = 3;
        bool authorityDead = authorityStateCode == networkCombatStateDead;
        bool authorityRegrouping = authorityStateCode == networkCombatStateRegroup;
        bool authorityRetreating = authorityStateCode == 1;

        if (authorityDead)
        {
            // Freeze first on the peer, then start disappear — do not keep snapping pose mid-death.
            if (!isPermanentlyEliminated && CurrentState != State.Dead)
            {
                ApplyNetworkAuthorityPosition(worldPosition, authoritySnapDistance, hardSnap: true);
            }

            ApplyPermanentDestroyFromNetworkAuthority();
            return;
        }

        if (isPermanentlyEliminated || CurrentState == State.Dead)
        {
            return;
        }

        if (authorityRegrouping)
        {
            CompleteRegroupFromNetworkAuthority(authorityHealth);
            ApplyNetworkAuthorityPosition(worldPosition, authoritySnapDistance, hardSnap: true);
            return;
        }

        if (authorityRetreating)
        {
            if (CurrentState != State.Retreat && CurrentState != State.Regroup)
            {
                EnterRetreatFromNetworkAuthority();
            }

            currentHealth = 0f;
            if (!retreatVisualsSyncedToDefeat)
            {
                SyncTroopVisualsToHealth(forceMinimum: true);
                SetFlagHolderDefeatedVisual();
                retreatVisualsSyncedToDefeat = true;
            }

            ClearLocalAggressorsTargetingMe();

            if (authorityRetreatInvulnerable)
            {
                invulnerableUntil = Time.time + retreatInvulnerabilityDuration;
            }

            ApplyNetworkAuthorityPosition(worldPosition, authoritySnapDistance, hardSnap: false);
            return;
        }

        ApplyActiveStateFromNetworkAuthority(authorityHealth);
        ApplyNetworkAuthorityPosition(worldPosition, authoritySnapDistance, hardSnap: false);
    }

    private void EnterRetreatFromNetworkAuthority()
    {
        if (UsesRemoteCombatAuthority())
        {
            CurrentState = State.Retreat;
            currentHealth = 0f;
            currentTarget = null;
            invulnerableUntil = Time.time + retreatInvulnerabilityDuration;
            nextRetreatDestinationRefreshTime = 0f;
            nextRetreatUnstuckTime = 0f;
            retreatUnstuckAttemptIndex = 0;

            RtsCampManager campManager = RtsCampManager.Instance;
            retreatPhase = campManager != null && campManager.HasGate(faction)
                ? RetreatPhase.ToGateOutside
                : RetreatPhase.ToCamp;

            if (motor != null)
            {
                motor.CanReceiveCommands = false;
                motor.MoveSpeedMultiplier = retreatMoveSpeedMultiplier;
                motor.Stop();
            }

            SetNetworkOwnerPoseHalted(false);
            ClearLocalAggressorsTargetingMe();
            return;
        }

        EnterRetreat();
    }

    private void NotifyOwnedCombatAuthorityChanged()
    {
        if (!SiegeMatchSettings.IsSiegePvpMode || UsesRemoteCombatAuthority())
        {
            return;
        }

        SiegePvpSession session = SiegePvpSession.Instance;
        if (session != null && session.IsMatchRunning)
        {
            session.NotifyOwnedTroopCombatChanged(this);
        }
    }

    private bool UsesRemoteCombatAuthority()
    {
        return ShouldIgnoreLocalCombatDamage();
    }

    private void ApplyNetworkAuthorityPosition(Vector3 worldPosition, float authoritySnapDistance, bool hardSnap)
    {
        if (isPermanentlyEliminated || CurrentState == State.Dead)
        {
            return;
        }

        if (motor != null)
        {
            bool remoteVisual = UsesRemoteCombatAuthority();
            if (remoteVisual && networkOwnerPoseHalted)
            {
                motor.SnapNetworkPosition(worldPosition);
                NotifyNetworkPositionApplied(hardSnap: true);
                return;
            }

            if (!hardSnap && (motor.HasActivePath || motor.HasDestination))
            {
                Vector3 delta = worldPosition - transform.position;
                delta.y = 0f;
                float drift = delta.magnitude;
                // Tight while pathing so a locally stuck peer cannot trail the owner for long.
                float driftThreshold = remoteVisual
                    ? Mathf.Max(0.28f, authoritySnapDistance * 0.22f)
                    : Mathf.Max(0.35f, authoritySnapDistance * 0.35f);
                if (drift < driftThreshold)
                {
                    return;
                }
            }

            if (hardSnap)
            {
                motor.SnapNetworkPosition(worldPosition);
            }
            else
            {
                motor.ApplyNetworkPose(
                    worldPosition,
                    transform.rotation,
                    forceAuthority: true,
                    authoritySnapDistance: authoritySnapDistance);
            }

            if (remoteVisual)
            {
                NotifyNetworkPositionApplied(hardSnap);
            }

            return;
        }

        Vector3 flat = worldPosition;
        flat.y = transform.position.y;
        transform.position = flat;
    }

    private bool ShouldIgnoreLocalCombatDamage()
    {
        if (!SiegeMatchSettings.IsSiegePvpMode)
        {
            return false;
        }

        SiegePvpSession session = SiegePvpSession.Instance;
        return session != null && session.IsMatchRunning && !session.IsLocallyOwnedTroop(this);
    }

    private bool CanBeFinishedByAttackerWhileRetreating(TroopCombat attacker, bool isRangedAttack)
    {
        if (attacker == null)
        {
            return true;
        }

        float distance = GetHorizontalDistance(transform.position, attacker.transform.position);

        if (isRangedAttack)
        {
            if (!canBeKilledByRangedAttackWhileRetreating)
            {
                return false;
            }

            return distance <= attacker.RangedAttackRange + 0.01f;
        }

        return distance <= attacker.MeleeAttackRange + 0.01f;
    }

    public bool CanBeTargetedBy(TroopCombat other)
    {
        if (other == null || CurrentState == State.Dead || CurrentState == State.Regroup || IsPointCaptureRaising)
        {
            return false;
        }

        if (CurrentState == State.Retreat && other.CurrentState == State.Retreat)
        {
            return false;
        }

        if (faction == Faction.Friendly && other.faction == Faction.Enemy)
        {
            if (IsProtectedByFriendlyCamp())
            {
                return false;
            }
        }

        return faction != other.faction;
    }

    public bool IsProtectedByFriendlyCamp()
    {
        if (faction != Faction.Friendly)
        {
            return false;
        }

        if (PointCaptureMatch.Instance != null)
        {
            return false;
        }

        RtsCampManager campManager = RtsCampManager.Instance;
        if (campManager == null)
        {
            return false;
        }

        if (TryGetFootprintBounds(out Bounds footprintBounds))
        {
            return campManager.IsFriendlyRegimentProtected(transform.position, footprintBounds);
        }

        return campManager.IsInFriendlyProtectedZone(transform.position);
    }

    private bool ShouldMaintainCombatTarget(TroopCombat target)
    {
        return ShouldEngageCandidateForCombat(target);
    }

    private bool ShouldEngageCandidateForCombat(TroopCombat candidate)
    {
        if (candidate == null || faction != Faction.Enemy || candidate.faction != Faction.Friendly)
        {
            return true;
        }

        EnemyRegimentAI enemyAi = GetComponent<EnemyRegimentAI>();
        if (enemyAi == null || !enemyAi.enabled)
        {
            return true;
        }

        return enemyAi.ShouldEngageFriendlyForCombat(candidate);
    }

    private TroopCombat FindTargetInRange()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, GetTargetAcquisitionRange(), targetLayers, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
        {
            return null;
        }

        TroopCombat bestTarget = null;
        float bestDistanceSqr = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null)
            {
                continue;
            }

            TroopCombat candidate = hit.GetComponentInParent<TroopCombat>();
            if (candidate == null || candidate == this || !candidate.CanBeTargetedBy(this))
            {
                continue;
            }

            if (!TryGetAttackProfileForTarget(candidate, out _))
            {
                continue;
            }

            if (!ShouldEngageCandidateForCombat(candidate))
            {
                continue;
            }

            Vector3 offset = candidate.transform.position - transform.position;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;

            if (distanceSqr < bestDistanceSqr)
            {
                bestDistanceSqr = distanceSqr;
                bestTarget = candidate;
            }
        }

        return bestTarget;
    }

    private float GetTargetAcquisitionRange()
    {
        return HasRangedAttack ? Mathf.Max(attackRange, rangedAttackRange) : attackRange;
    }

    private bool TryGetAttackProfileForTarget(TroopCombat target, out AttackProfile attackProfile)
    {
        attackProfile = default;
        if (target == null)
        {
            return false;
        }

        float distance = GetHorizontalDistance(transform.position, target.transform.position);

        if (IsWithinMeleeAttackRange(distance))
        {
            attackProfile = new AttackProfile(attackDamage, attackRange, attackCooldown, false);
            return attackProfile.IsValid;
        }

        if (!IsWithinRangedAttackBand(distance))
        {
            return false;
        }

        if (target.IsRetreating && !target.canBeKilledByRangedAttackWhileRetreating)
        {
            return false;
        }

        attackProfile = new AttackProfile(rangedAttackDamage, rangedAttackRange, rangedAttackCooldown, true);
        return attackProfile.IsValid;
    }

    private bool IsWithinMeleeAttackRange(float distance)
    {
        return distance <= attackRange + 0.001f;
    }

    private bool IsWithinRangedAttackBand(float distance)
    {
        if (!HasRangedAttack)
        {
            return false;
        }

        return distance > attackRange + 0.001f && distance <= rangedAttackRange + 0.001f;
    }

    private static float GetHorizontalDistance(Vector3 a, Vector3 b)
    {
        return Mathf.Sqrt(GetHorizontalDistanceSqr(a, b));
    }

    private static float GetHorizontalDistanceSqr(Vector3 a, Vector3 b)
    {
        Vector3 offset = a - b;
        offset.y = 0f;
        return offset.sqrMagnitude;
    }

    private void PermanentDestroy(TroopCombat attacker = null)
    {
        if (isPermanentlyEliminated)
        {
            return;
        }

        EndPointCaptureRetreatTracking();
        isPermanentlyEliminated = true;
        retreatVisualsSyncedToDefeat = false;
        CurrentState = State.Dead;
        currentTarget = null;
        ClearIdleGroundLock();

        if (motor != null)
        {
            motor.Stop();
            motor.CanReceiveCommands = false;
            motor.MoveSpeedMultiplier = 1f;
            motor.SuppressLocalSimulation = true;
        }

        // Level pitch/roll, then hard-freeze the root for the whole disappear sequence.
        EnsureFormationRootLevel();
        LockDeathPose();

        if (retreatDeathDisappearCoroutine != null)
        {
            StopCoroutine(retreatDeathDisappearCoroutine);
            retreatDeathDisappearCoroutine = null;
        }

        if (HasActiveTroopVisuals())
        {
            NotifyOwnedCombatAuthorityChanged();
            retreatDeathDisappearCoroutine = StartCoroutine(PlayRetreatDeathDisappearSequence());
            return;
        }

        CompletePermanentDestroy();
        NotifyOwnedCombatAuthorityChanged();
    }

    private void LockDeathPose()
    {
        lockedDeathPosition = transform.position;
        lockedDeathRotation = GetAuthoredLevelRotation();
        transform.rotation = lockedDeathRotation;
        if (troopVisualRoot != null)
        {
            troopVisualRoot.localRotation = Quaternion.identity;
        }

        hasLockedDeathPose = true;
    }

    private void MaintainLockedDeathPose()
    {
        if (!hasLockedDeathPose)
        {
            LockDeathPose();
        }

        if ((transform.position - lockedDeathPosition).sqrMagnitude > 0.0000001f)
        {
            transform.position = lockedDeathPosition;
        }

        if (Quaternion.Angle(transform.rotation, lockedDeathRotation) > 0.01f)
        {
            transform.rotation = lockedDeathRotation;
        }

        if (troopVisualRoot != null
            && Quaternion.Angle(troopVisualRoot.localRotation, Quaternion.identity) > 0.01f)
        {
            troopVisualRoot.localRotation = Quaternion.identity;
        }

        if (motor != null)
        {
            motor.SuppressLocalSimulation = true;
            if (motor.HasDestination || motor.HasActivePath)
            {
                motor.Stop();
            }
        }
    }

    private bool HasActiveTroopVisuals()
    {
        for (int i = 0; i < troopVisuals.Count; i++)
        {
            TroopVisualInstance troopVisual = troopVisuals[i];
            if (troopVisual.Instance != null && troopVisual.Instance.activeSelf)
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerator PlayRetreatDeathDisappearSequence()
    {
        MaintainLockedDeathPose();

        List<int> disappearOrder = BuildRandomRetreatDeathDisappearOrder();
        int disappearCount = disappearOrder.Count;
        if (disappearCount == 0)
        {
            CompletePermanentDestroy();
            yield break;
        }

        float span = Mathf.Max(0f, retreatDeathDisappearSpan);
        if (span <= 0f || disappearCount == 1)
        {
            for (int i = 0; i < disappearCount; i++)
            {
                MaintainLockedDeathPose();
                HideTroopVisualAt(disappearOrder[i]);
            }
        }
        else
        {
            float interval = span / disappearCount;
            for (int i = 0; i < disappearCount; i++)
            {
                MaintainLockedDeathPose();
                HideTroopVisualAt(disappearOrder[i]);
                if (i < disappearCount - 1)
                {
                    yield return new WaitForSeconds(interval);
                }
            }
        }

        retreatDeathDisappearCoroutine = null;
        CompletePermanentDestroy();
        NotifyOwnedCombatAuthorityChanged();
    }

    private List<int> BuildRandomRetreatDeathDisappearOrder()
    {
        List<int> regularTroops = new List<int>();
        int flagHolderIndex = -1;

        for (int i = 0; i < troopVisuals.Count; i++)
        {
            TroopVisualInstance troopVisual = troopVisuals[i];
            if (troopVisual.Instance == null || !troopVisual.Instance.activeSelf)
            {
                continue;
            }

            if (troopVisual.IsFlagHolder)
            {
                flagHolderIndex = i;
                continue;
            }

            regularTroops.Add(i);
        }

        for (int i = regularTroops.Count - 1; i > 0; i--)
        {
            int swapIndex = Random.Range(0, i + 1);
            int temp = regularTroops[i];
            regularTroops[i] = regularTroops[swapIndex];
            regularTroops[swapIndex] = temp;
        }

        if (flagHolderIndex >= 0)
        {
            regularTroops.Add(flagHolderIndex);
        }

        return regularTroops;
    }

    private void HideTroopVisualAt(int index)
    {
        if (index < 0 || index >= troopVisuals.Count)
        {
            return;
        }

        TroopVisualInstance troopVisual = troopVisuals[index];
        if (troopVisual.Instance != null && troopVisual.Instance.activeSelf)
        {
            troopVisual.Instance.SetActive(false);
            activeTroopVisualCount = Mathf.Max(0, activeTroopVisualCount - 1);
        }
    }

    private void CompletePermanentDestroy()
    {
        RegimentPermanentlyDestroyed?.Invoke(this);

        if (destroyOnDeath)
        {
            gameObject.SetActive(false);
            return;
        }

        nextAttackTime = 0f;
    }

    private void EnsureTroopVisuals()
    {
        if (maxUnitCount <= 0)
        {
            return;
        }

        Transform parent = troopVisualRoot != null ? troopVisualRoot : transform;
        bool layoutChanged = false;
        bool wantsFlagHolder = flagHolderPrefab != null || defeatedFlagHolderPrefab != null;

        while (GetRegularTroopVisualCount() < maxUnitCount - (wantsFlagHolder ? 1 : 0))
        {
            if (troopPrefab == null)
            {
                break;
            }

            GameObject instance = Instantiate(troopPrefab, parent);
            instance.name = troopPrefab.name + "_" + (troopVisuals.Count + 1);
            ApplyTroopVisualScale(instance.transform, troopPrefabScale);

            troopVisuals.Add(new TroopVisualInstance
            {
                Instance = instance,
                SlotIndex = troopVisuals.Count,
                IsFlagHolder = false
            });

            layoutChanged = true;
        }

        if (wantsFlagHolder && GetFlagHolderVisual() == null)
        {
            GameObject flagPrefab = GetActiveFlagHolderPrefab();
            if (flagPrefab != null)
            {
                GameObject instance = Instantiate(flagPrefab, parent);
                instance.name = flagPrefab.name + "_FlagHolder";
                ApplyTroopVisualScale(instance.transform, flagHolderPrefabScale);

                troopVisuals.Add(new TroopVisualInstance
                {
                    Instance = instance,
                    SlotIndex = GetCenterSlotIndex(),
                    IsFlagHolder = true
                });

                layoutChanged = true;
            }
        }

        while (GetRegularTroopVisualCount() > maxUnitCount - (wantsFlagHolder ? 1 : 0))
        {
            int removeIndex = FindLastRegularTroopVisualIndex();
            if (removeIndex < 0)
            {
                break;
            }

            RemoveTroopVisualAt(removeIndex);
            layoutChanged = true;
        }

        if (randomizeSpawnOrder)
        {
            ShuffleTroopVisualSlots();
            layoutChanged = true;
        }
        else
        {
            AssignFormationSlots();
            layoutChanged = true;
        }

        if (layoutChanged)
        {
            ApplyTroopVisualFormation();
        }
    }

    private void AssignFormationSlots()
    {
        int centerSlot = GetCenterSlotIndex();
        int slotCursor = 0;
        List<int> availableSlots = BuildNonCenterSlots();

        for (int i = 0; i < troopVisuals.Count; i++)
        {
            TroopVisualInstance troopVisual = troopVisuals[i];
            if (troopVisual.IsFlagHolder)
            {
                troopVisual.SlotIndex = centerSlot;
                continue;
            }

            if (slotCursor < availableSlots.Count)
            {
                troopVisual.SlotIndex = availableSlots[slotCursor++];
            }
        }
    }

    private List<int> BuildNonCenterSlots()
    {
        int centerSlot = GetCenterSlotIndex();
        List<int> slots = new List<int>();
        for (int slot = 0; slot < maxUnitCount; slot++)
        {
            if (slot != centerSlot)
            {
                slots.Add(slot);
            }
        }

        return slots;
    }

    private int GetFormationGridSize()
    {
        int gridSize = Mathf.RoundToInt(Mathf.Sqrt(maxUnitCount));
        return Mathf.Max(1, gridSize);
    }

    private int GetCenterSlotIndex()
    {
        int gridSize = GetFormationGridSize();
        int center = gridSize / 2;
        return center * gridSize + center;
    }

    private TroopVisualInstance GetFlagHolderVisual()
    {
        for (int i = 0; i < troopVisuals.Count; i++)
        {
            TroopVisualInstance troopVisual = troopVisuals[i];
            if (troopVisual.IsFlagHolder)
            {
                return troopVisual;
            }
        }

        return null;
    }

    private int GetRegularTroopVisualCount()
    {
        int count = 0;
        for (int i = 0; i < troopVisuals.Count; i++)
        {
            if (!troopVisuals[i].IsFlagHolder)
            {
                count++;
            }
        }

        return count;
    }

    private int FindLastRegularTroopVisualIndex()
    {
        for (int i = troopVisuals.Count - 1; i >= 0; i--)
        {
            if (!troopVisuals[i].IsFlagHolder)
            {
                return i;
            }
        }

        return -1;
    }

    private void RemoveTroopVisualAt(int index)
    {
        TroopVisualInstance troopVisual = troopVisuals[index];
        troopVisuals.RemoveAt(index);

        if (troopVisual != null && troopVisual.Instance != null)
        {
            if (Application.isPlaying)
            {
                Destroy(troopVisual.Instance);
            }
            else
            {
                DestroyImmediate(troopVisual.Instance);
            }
        }
    }

    private GameObject GetActiveFlagHolderPrefab()
    {
        if (flagHolderShowingDefeated && defeatedFlagHolderPrefab != null)
        {
            return defeatedFlagHolderPrefab;
        }

        return flagHolderPrefab;
    }

    private void SetFlagHolderDefeatedVisual()
    {
        if (GetFlagHolderVisual() == null)
        {
            return;
        }

        flagHolderShowingDefeated = true;
        RefreshFlagHolderVisual();
    }

    private void RestoreFlagHolderVisual()
    {
        if (GetFlagHolderVisual() == null)
        {
            return;
        }

        flagHolderShowingDefeated = false;
        RefreshFlagHolderVisual();
    }

    private void RefreshFlagHolderVisual()
    {
        TroopVisualInstance flagHolder = GetFlagHolderVisual();
        GameObject prefab = GetActiveFlagHolderPrefab();
        if (flagHolder == null || prefab == null || flagHolder.Instance == null)
        {
            return;
        }

        Transform parent = flagHolder.Instance.transform.parent;
        bool wasActive = flagHolder.Instance.activeSelf;
        int slotIndex = flagHolder.SlotIndex;

        if (Application.isPlaying)
        {
            Destroy(flagHolder.Instance);
        }
        else
        {
            DestroyImmediate(flagHolder.Instance);
        }

        GameObject instance = Instantiate(prefab, parent);
        instance.name = prefab.name + "_FlagHolder";
        ApplyTroopVisualScale(instance.transform, flagHolderPrefabScale);
        instance.SetActive(wasActive);

        flagHolder.Instance = instance;
        flagHolder.IsFlagHolder = true;
        flagHolder.SlotIndex = slotIndex;
        ApplyTroopVisualFormation();
    }

    private void ShuffleTroopVisualSlots()
    {
        int centerSlot = GetCenterSlotIndex();
        List<int> availableSlots = BuildNonCenterSlots();
        for (int i = availableSlots.Count - 1; i > 0; i--)
        {
            int swapIndex = Random.Range(0, i + 1);
            int temp = availableSlots[i];
            availableSlots[i] = availableSlots[swapIndex];
            availableSlots[swapIndex] = temp;
        }

        int slotCursor = 0;
        for (int i = 0; i < troopVisuals.Count; i++)
        {
            TroopVisualInstance troopVisual = troopVisuals[i];
            if (troopVisual.IsFlagHolder)
            {
                troopVisual.SlotIndex = centerSlot;
                continue;
            }

            if (slotCursor < availableSlots.Count)
            {
                troopVisual.SlotIndex = availableSlots[slotCursor++];
            }
        }
    }

    private int GetTargetActiveVisualCount(float healthRatio)
    {
        bool hasFlagHolder = GetFlagHolderVisual() != null;
        int maxRegularTroops = maxUnitCount - (hasFlagHolder ? 1 : 0);
        int minimumRegularTroops = Mathf.Clamp(Mathf.RoundToInt(maxRegularTroops * defeatedUnitPercentage), 0, maxRegularTroops);
        int targetRegularTroops = Mathf.Clamp(
            Mathf.RoundToInt(Mathf.Lerp(minimumRegularTroops, maxRegularTroops, healthRatio)),
            minimumRegularTroops,
            maxRegularTroops);

        return targetRegularTroops + (hasFlagHolder ? 1 : 0);
    }

    private void ApplyCasualtyLoss()
    {
        int activeCount = 0;
        for (int i = 0; i < troopVisuals.Count; i++)
        {
            TroopVisualInstance troopVisual = troopVisuals[i];
            if (troopVisual.Instance != null && troopVisual.Instance.activeSelf)
            {
                activeCount++;
            }
        }

        int targetActiveCount = GetTargetActiveVisualCount(HealthNormalized);

        if (activeCount <= targetActiveCount)
        {
            activeTroopVisualCount = activeCount;
            return;
        }

        List<int> candidates = new List<int>();
        for (int i = 0; i < troopVisuals.Count; i++)
        {
            TroopVisualInstance troopVisual = troopVisuals[i];
            if (troopVisual.IsFlagHolder)
            {
                continue;
            }

            if (troopVisual.Instance != null && troopVisual.Instance.activeSelf)
            {
                candidates.Add(i);
            }
        }

        int lossesNeeded = Mathf.Min(activeCount - targetActiveCount, candidates.Count);
        for (int i = 0; i < lossesNeeded; i++)
        {
            int pickedIndex = Random.Range(i, candidates.Count);
            int troopIndex = candidates[pickedIndex];
            candidates[pickedIndex] = candidates[i];
            candidates[i] = troopIndex;

            TroopVisualInstance troopVisual = troopVisuals[troopIndex];
            if (troopVisual.Instance != null)
            {
                troopVisual.Instance.SetActive(false);
            }
        }

        activeTroopVisualCount = activeCount - lossesNeeded;
        EnsureFlagHolderActive();
    }

    private void SyncTroopVisualsToHealth(bool forceMinimum = false)
    {
        if (troopVisuals.Count == 0)
        {
            return;
        }

        float healthRatio = forceMinimum ? 0f : HealthNormalized;
        int targetActiveCount = GetTargetActiveVisualCount(healthRatio);

        int activeCount = 0;
        for (int i = 0; i < troopVisuals.Count; i++)
        {
            TroopVisualInstance troopVisual = troopVisuals[i];
            if (troopVisual.Instance != null && troopVisual.Instance.activeSelf)
            {
                activeCount++;
            }
        }

        if (activeCount < targetActiveCount)
        {
            for (int i = 0; i < troopVisuals.Count && activeCount < targetActiveCount; i++)
            {
                TroopVisualInstance troopVisual = troopVisuals[i];
                if (troopVisual.IsFlagHolder)
                {
                    continue;
                }

                if (troopVisual.Instance != null && !troopVisual.Instance.activeSelf)
                {
                    troopVisual.Instance.SetActive(true);
                    activeCount++;
                }
            }

            ApplyTroopVisualFormation();
        }
        else if (activeCount > targetActiveCount)
        {
            List<int> candidates = new List<int>();
            for (int i = 0; i < troopVisuals.Count; i++)
            {
                TroopVisualInstance troopVisual = troopVisuals[i];
                if (troopVisual.IsFlagHolder)
                {
                    continue;
                }

                if (troopVisual.Instance != null && troopVisual.Instance.activeSelf)
                {
                    candidates.Add(i);
                }
            }

            int lossesNeeded = Mathf.Min(activeCount - targetActiveCount, candidates.Count);
            for (int i = 0; i < lossesNeeded; i++)
            {
                int pickedIndex = Random.Range(i, candidates.Count);
                int troopIndex = candidates[pickedIndex];
                candidates[pickedIndex] = candidates[i];
                candidates[i] = troopIndex;

                TroopVisualInstance troopVisual = troopVisuals[troopIndex];
                if (troopVisual.Instance != null)
                {
                    troopVisual.Instance.SetActive(false);
                }
            }
        }

        activeTroopVisualCount = targetActiveCount;
        EnsureFlagHolderActive();
    }

    private void EnsureFlagHolderActive()
    {
        TroopVisualInstance flagHolder = GetFlagHolderVisual();
        if (flagHolder != null && flagHolder.Instance != null && !flagHolder.Instance.activeSelf)
        {
            flagHolder.Instance.SetActive(true);
            ApplyTroopVisualFormation();
        }
    }

    private void ApplyTroopVisualFormation()
    {
        if (troopVisuals.Count == 0)
        {
            return;
        }

        int gridSize = GetFormationGridSize();
        int gridColumns = gridSize;
        int gridRows = gridSize;
        Vector2 gridCenter = new Vector2((gridColumns - 1) * 0.5f, (gridRows - 1) * 0.5f);
        float jitterAmount = formationCellSpacing * formationJitterFraction;

        for (int i = 0; i < troopVisuals.Count; i++)
        {
            TroopVisualInstance troopVisual = troopVisuals[i];
            if (troopVisual.Instance == null)
            {
                continue;
            }

            if (!troopVisual.Instance.activeSelf)
            {
                continue;
            }

            Vector3 localPosition = GetFormationPosition(troopVisual.SlotIndex, gridColumns, gridRows, gridCenter, jitterAmount);
            ApplyTroopVisualLocalPosition(troopVisual.Instance.transform, localPosition);

            Vector3 visualScale = troopVisual.IsFlagHolder ? flagHolderPrefabScale : troopPrefabScale;
            ApplyTroopVisualScale(troopVisual.Instance.transform, visualScale);
        }

        // Local Y stays 0 here. Root snap + LateUpdate project meshes after the regiment is on ground.
        troopGroundProjectionDirty = true;
    }

    private void SyncTroopVisualScale()
    {
        if (!keepTroopVisualScaleIndependent || troopVisuals.Count == 0)
        {
            return;
        }

        for (int i = 0; i < troopVisuals.Count; i++)
        {
            TroopVisualInstance troopVisual = troopVisuals[i];
            if (troopVisual.Instance == null)
            {
                continue;
            }

            ApplyTroopVisualScale(troopVisual.Instance.transform, troopVisual.IsFlagHolder ? flagHolderPrefabScale : troopPrefabScale);
        }
    }

    private void ApplyTroopVisualScale(Transform visualTransform, Vector3 desiredScale)
    {
        if (visualTransform == null || !keepTroopVisualScaleIndependent)
        {
            return;
        }

        Transform parent = visualTransform.parent;
        if (parent == null)
        {
            visualTransform.localScale = desiredScale;
            return;
        }

        Vector3 parentScale = parent.localScale;
        visualTransform.localScale = new Vector3(
            SafeDivide(desiredScale.x, parentScale.x),
            SafeDivide(desiredScale.y, parentScale.y),
            SafeDivide(desiredScale.z, parentScale.z));
    }

    private void ApplyTroopVisualScale(Transform visualTransform)
    {
        ApplyTroopVisualScale(visualTransform, troopPrefabScale);
    }

    private void ApplyTroopVisualLocalPosition(Transform visualTransform, Vector3 desiredWorldOffset)
    {
        if (visualTransform == null)
        {
            return;
        }

        Transform parent = visualTransform.parent;
        if (parent == null)
        {
            visualTransform.localPosition = desiredWorldOffset;
            return;
        }

        Vector3 parentScale = parent.localScale;
        visualTransform.localPosition = new Vector3(
            SafeDivide(desiredWorldOffset.x, parentScale.x),
            SafeDivide(desiredWorldOffset.y, parentScale.y),
            SafeDivide(desiredWorldOffset.z, parentScale.z));
    }

    private void CacheTroopPrefabScale()
    {
        if (troopPrefab == null)
        {
            troopPrefabScale = Vector3.one;
            return;
        }

        troopPrefabScale = troopPrefab.transform.localScale;
    }

    private void CacheFlagHolderPrefabScale()
    {
        GameObject sourcePrefab = flagHolderPrefab != null ? flagHolderPrefab : defeatedFlagHolderPrefab;
        if (sourcePrefab == null)
        {
            flagHolderPrefabScale = troopPrefabScale;
            return;
        }

        flagHolderPrefabScale = sourcePrefab.transform.localScale;
    }

    private static float SafeDivide(float numerator, float denominator)
    {
        if (Mathf.Abs(denominator) < 0.0001f)
        {
            return numerator;
        }

        return numerator / denominator;
    }

    private Vector3 GetFormationPosition(int slotIndex, int gridColumns, int gridRows, Vector2 gridCenter, float jitterAmount)
    {
        int column = Mathf.Clamp(slotIndex % gridColumns, 0, gridColumns - 1);
        int row = Mathf.Clamp(slotIndex / gridColumns, 0, gridRows - 1);

        float x = (column - gridCenter.x) * formationCellSpacing;
        float z = (gridCenter.y - row) * formationCellSpacing;

        Vector2 jitter = slotIndex == GetCenterSlotIndex()
            ? Vector2.zero
            : GetDeterministicJitter(slotIndex) * jitterAmount;
        Vector3 localPosition = new Vector3(x + jitter.x, 0f, z + jitter.y);

        float halfWidth = (gridColumns - 1) * 0.5f * formationCellSpacing;
        float halfDepth = (gridRows - 1) * 0.5f * formationCellSpacing;
        localPosition.x = Mathf.Clamp(localPosition.x, -halfWidth, halfWidth);
        localPosition.z = Mathf.Clamp(localPosition.z, -halfDepth, halfDepth);

        return localPosition;
    }

    private Vector2 GetDeterministicJitter(int seed)
    {
        float xNoise = Mathf.Sin((seed + 1) * 12.9898f) * 43758.5453f;
        float yNoise = Mathf.Sin((seed + 1) * 78.233f + 19.19f) * 43758.5453f;

        return new Vector2(RepeatSigned01(xNoise), RepeatSigned01(yNoise));
    }

    private static float RepeatSigned01(float value)
    {
        float repeat = value - Mathf.Floor(value);
        return repeat * 2f - 1f;
    }

    private void OnDrawGizmos()
    {
        if (!drawDebugGizmos)
        {
            return;
        }

        Vector3 anchor = GetDebugAnchorPosition();
        Color teamColor = GetTeamColor();
        Color stateColor = GetStateColor();
        Color combinedColor = Color.Lerp(teamColor, stateColor, 0.45f);

        Gizmos.color = combinedColor;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        if (HasRangedAttack)
        {
            Gizmos.color = new Color(combinedColor.r, combinedColor.g, combinedColor.b, combinedColor.a * 0.65f);
            Gizmos.DrawWireSphere(transform.position, rangedAttackRange);
        }

        DrawHealthGizmo(anchor, combinedColor);

#if UNITY_EDITOR
        string debugText = BuildDebugLabel();
        if (!string.IsNullOrEmpty(debugText))
        {
            GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = combinedColor }
            };
            Handles.Label(anchor + Vector3.up * debugLabelHeight, debugText, style);
        }
#endif
    }

    private void DrawHealthGizmo(Vector3 anchor, Color tint)
    {
        float clampedMaxHealth = Mathf.Max(1f, maxHealth);
        float healthRatio = Mathf.Clamp01(currentHealth / clampedMaxHealth);

        Vector3 left = anchor + Vector3.left * (debugHealthBarWidth * 0.5f);
        Vector3 right = anchor + Vector3.right * (debugHealthBarWidth * 0.5f);
        Vector3 barTop = Vector3.up * debugLabelHeight;
        Vector3 barBottom = barTop + Vector3.up * debugHealthBarHeight;

        Gizmos.color = new Color(0f, 0f, 0f, 0.6f);
        Gizmos.DrawLine(left + barTop, right + barTop);
        Gizmos.DrawLine(left + barBottom, right + barBottom);
        Gizmos.DrawLine(left + barTop, left + barBottom);
        Gizmos.DrawLine(right + barTop, right + barBottom);

        Vector3 fillRight = Vector3.Lerp(left + barTop, right + barTop, healthRatio);
        Gizmos.color = Color.Lerp(deadGizmoColor, tint, healthRatio);
        Gizmos.DrawLine(left + barTop, fillRight);
    }

    private Vector3 GetDebugAnchorPosition()
    {
        if (debugAnchor != null)
        {
            return debugAnchor.position;
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers != null && renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                Renderer rendererRef = renderers[i];
                if (rendererRef != null)
                {
                    bounds.Encapsulate(rendererRef.bounds);
                }
            }

            return bounds.center + Vector3.up * bounds.extents.y;
        }

        Collider[] colliders = GetComponentsInChildren<Collider>();
        if (colliders != null && colliders.Length > 0)
        {
            Bounds bounds = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++)
            {
                Collider colliderRef = colliders[i];
                if (colliderRef != null)
                {
                    bounds.Encapsulate(colliderRef.bounds);
                }
            }

            return bounds.center + Vector3.up * bounds.extents.y;
        }

        return transform.position + Vector3.up * debugLabelHeight;
    }

    private Color GetTeamColor()
    {
        return faction == Faction.Friendly ? friendlyGizmoColor : enemyGizmoColor;
    }

    private Color GetStateColor()
    {
        switch (CurrentState)
        {
            case State.Fight:
                return fightGizmoColor;
            case State.Retreat:
                return retreatGizmoColor;
            case State.Regroup:
                return regroupGizmoColor;
            case State.Dead:
                return deadGizmoColor;
            default:
                return idleGizmoColor;
        }
    }

    private string BuildDebugLabel()
    {
        float clampedMaxHealth = Mathf.Max(1f, maxHealth);
        float healthPercent = Mathf.Clamp01(currentHealth / clampedMaxHealth) * 100f;
        string targetName = currentTarget != null ? currentTarget.name : "none";
        string moveSpeedLabel = CurrentState == State.Fight
            ? " | Move " + (CombatMoveSpeedMultiplier * 100f).ToString("F0") + "%"
            : string.Empty;

        string attackLabel = HasRangedAttack
            ? " | Melee " + attackDamage.ToString("F0") + "@" + attackRange.ToString("F1")
              + " | Ranged " + rangedAttackDamage.ToString("F0") + "@" + rangedAttackRange.ToString("F1")
            : " | ATK " + attackDamage.ToString("F0") + " | RNG " + attackRange.ToString("F1");

        return faction + " | " + CurrentState + " | HP " + currentHealth.ToString("F0") + "/" + clampedMaxHealth.ToString("F0") + " (" + healthPercent.ToString("F0") + "%)" + " | Units " + activeTroopVisualCount + "/" + maxUnitCount + " (min " + MinimumUnitCountAtDefeat + ")" + attackLabel + " | Target " + targetName + moveSpeedLabel;
    }

    private struct AttackProfile
    {
        public readonly float Damage;
        public readonly float Range;
        public readonly float Cooldown;
        public readonly bool IsRanged;

        public AttackProfile(float damage, float range, float cooldown, bool isRanged)
        {
            Damage = damage;
            Range = range;
            Cooldown = cooldown;
            IsRanged = isRanged;
        }

        public bool IsValid => Range > 0f && Damage > 0f;
    }

    [System.Serializable]
    private class TroopVisualInstance
    {
        public GameObject Instance;
        public int SlotIndex;
        public bool IsFlagHolder;
    }
}