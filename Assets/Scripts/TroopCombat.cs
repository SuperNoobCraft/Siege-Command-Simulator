using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DefaultExecutionOrder(-50)]
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
    [SerializeField, Min(0f)] private float rangedProjectileArcHeight = 2f;
    [SerializeField, Range(0f, 1f)] private float rangedProjectileDispersion = 0.35f;
    [SerializeField, Min(0f)] private float rangedProjectileMaxSpreadRadius = 2.5f;
    [SerializeField, Min(0f)] private float rangedProjectileLaunchHeight = 1.1f;

    [Header("Combat Targeting")]
    [SerializeField] private float targetScanInterval = 0.2f;
    [SerializeField] private LayerMask targetLayers = ~0;
    [SerializeField] private bool faceTargetWhileFighting = true;

    [Header("Combat Movement")]
    [SerializeField, Range(0f, 1f)] private float combatMoveSpeedPercentage = 0.2f;
    [SerializeField] private bool scaleCombatSpeedByEnemyOverlap = true;
    [SerializeField, Min(0f)] private float combatOverlapSmoothingSpeed = 8f;
    [SerializeField] private Collider footprintCollider;

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
    private TroopCombat currentTarget;
    private float currentHealth;
    private float nextAttackTime;
    private float nextScanTime;
    private float nextRetreatDestinationRefreshTime;
    private float invulnerableUntil;
    private bool isPermanentlyEliminated;
    private Coroutine retreatDeathDisappearCoroutine;
    private RetreatPhase retreatPhase = RetreatPhase.ToGateOutside;
    private Vector3 troopPrefabScale = Vector3.one;
    private Vector3 flagHolderPrefabScale = Vector3.one;
    private bool flagHolderShowingDefeated;
    private Vector3 lastRegimentPosition;
    private float smoothedCombatOverlap;
    private const float MinimumFacingMovementDistance = 0.02f;
    private readonly List<TroopVisualInstance> troopVisuals = new List<TroopVisualInstance>();
    private int activeTroopVisualCount;

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
    public int MaxUnitCount => maxUnitCount;
    public int ActiveUnitCount => activeTroopVisualCount;
    public int MinimumUnitCountAtDefeat => Mathf.RoundToInt(maxUnitCount * defeatedUnitPercentage);
    public bool IsCommandable => motor == null || motor.CanReceiveCommands;
    public bool IsRetreating => CurrentState == State.Retreat;
    public bool IsTraversingGate =>
        CurrentState == State.Retreat
        && retreatPhase != RetreatPhase.ToCamp
        && RtsCampManager.Instance != null
        && RtsCampManager.Instance.HasGate(faction)
        && RtsCampManager.Instance.IsNearGateForOpening(transform.position, faction);
    public bool HoldsInCampUntilNextWave { get; private set; }
    public bool IsRegrouping => CurrentState == State.Regroup;
    public float CombatMoveSpeedMultiplier => GetCombatMoveSpeedMultiplier();

    public void SetHoldInCampUntilNextWave(bool holdInCamp)
    {
        HoldsInCampUntilNextWave = holdInCamp;
    }

    private void Awake()
    {
        motor = GetComponent<RtsUnitMotor>();
        currentHealth = Mathf.Max(1f, maxHealth);

        if (footprintCollider == null)
        {
            footprintCollider = GetComponent<Collider>();
        }

        if (motor != null)
        {
            motor.CanReceiveCommands = motor.IsCommandUnit;
            if (footprintCollider != null)
            {
                motor.SetMovementBoundsCollider(footprintCollider);
            }
        }

        CacheTroopPrefabScale();
        CacheFlagHolderPrefabScale();
        EnsureTroopVisuals();
        ApplyTroopVisualFormation();
        lastRegimentPosition = transform.position;

        EnsureFormationRootUpright();
        if (troopVisualRoot != null)
        {
            troopVisualRoot.localScale = Vector3.one;
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
        rangedProjectileArcHeight = Mathf.Max(0f, rangedProjectileArcHeight);
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
        combatOverlapSmoothingSpeed = Mathf.Max(0f, combatOverlapSmoothingSpeed);
    }

    private void Update()
    {
        if (CurrentState == State.Dead)
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
                UpdateRegroup();
                break;
            default:
                UpdateRecovery();
                UpdateCombat();
                break;
        }

        if (CurrentState == State.Retreat || CurrentState == State.Regroup)
        {
            UpdateRecovery();
        }

        UpdateMovementSpeed();
    }

    private void LateUpdate()
    {
        if (CurrentState == State.Dead)
        {
            return;
        }

        UpdateTroopFacing();
    }

    private void UpdateMovementSpeed()
    {
        if (motor == null)
        {
            return;
        }

        switch (CurrentState)
        {
            case State.Retreat:
                motor.MoveSpeedMultiplier = retreatMoveSpeedMultiplier;
                break;
            case State.Fight:
                motor.MoveSpeedMultiplier = GetCombatMoveSpeedMultiplier();
                break;
            default:
                motor.MoveSpeedMultiplier = 1f;
                break;
        }
    }

    private float GetCombatMoveSpeedMultiplier()
    {
        if (CurrentState != State.Fight)
        {
            return 1f;
        }

        if (!scaleCombatSpeedByEnemyOverlap)
        {
            return combatMoveSpeedPercentage;
        }

        float targetOverlap = GetEnemyOverlapRatio();
        smoothedCombatOverlap = Mathf.MoveTowards(
            smoothedCombatOverlap,
            targetOverlap,
            combatOverlapSmoothingSpeed * Time.deltaTime);

        return Mathf.Lerp(1f, combatMoveSpeedPercentage, smoothedCombatOverlap);
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

    private bool TryGetFootprintBoundsXZ(out Vector2 center, out Vector2 halfExtents)
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
        if (currentTarget != null && !currentTarget.CanBeTargetedBy(this))
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
            SpawnRangedAttackVolley(currentTarget);
        }
        else
        {
            MeleeAttackPerformed?.Invoke(this, transform.position);
        }

        currentTarget.TakeDamage(attackProfile.Damage, this, attackProfile.IsRanged);
    }

    private void SpawnRangedAttackVolley(TroopCombat target)
    {
        if (rangedProjectilePrefab == null || target == null)
        {
            return;
        }

        int arrowCount = Mathf.Max(1, Mathf.RoundToInt(activeTroopVisualCount * rangedProjectileFrequency));
        List<Vector3> launchPoints = GetRangedLaunchPoints(arrowCount);
        Vector3 targetCenter = target.transform.position;
        float spreadRadius = rangedProjectileDispersion * rangedProjectileMaxSpreadRadius;

        for (int i = 0; i < launchPoints.Count; i++)
        {
            Vector2 impactOffset = Random.insideUnitCircle * spreadRadius;
            Vector3 impactPoint = new Vector3(
                targetCenter.x + impactOffset.x,
                targetCenter.y,
                targetCenter.z + impactOffset.y);

            Vector3 launchPoint = launchPoints[i];
            launchPoint.y += rangedProjectileLaunchHeight;

            TroopRangedProjectile.Launch(
                rangedProjectilePrefab,
                launchPoint,
                impactPoint,
                rangedProjectileSpeed,
                rangedProjectileArcHeight);
        }
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

            availablePoints.Add(troopVisual.Instance.transform.position);
        }

        if (availablePoints.Count == 0)
        {
            availablePoints.Add(transform.position);
        }

        ShuffleLaunchPoints(availablePoints);

        List<Vector3> selectedPoints = new List<Vector3>(desiredCount);
        for (int i = 0; i < desiredCount; i++)
        {
            selectedPoints.Add(availablePoints[i % availablePoints.Count]);
        }

        return selectedPoints;
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
        RtsCampManager campManager = RtsCampManager.Instance;

        if (campManager != null && retreatPhase == RetreatPhase.ToCamp && campManager.HasReachedCampCenter(transform.position, faction))
        {
            EnterRegroup();
            return;
        }

        if (campManager != null && retreatPhase == RetreatPhase.ToGateOutside && campManager.IsAtGateOutside(transform.position, faction))
        {
            retreatPhase = ShouldUseGateInsideWaypoint(campManager)
                ? RetreatPhase.ToGateInside
                : RetreatPhase.ToCamp;
            if (motor != null)
            {
                motor.MoveTo(GetRetreatDestination(campManager));
            }
        }
        else if (campManager != null && retreatPhase == RetreatPhase.ToGateInside && campManager.IsAtGateInside(transform.position, faction))
        {
            retreatPhase = RetreatPhase.ToCamp;
            if (motor != null)
            {
                motor.MoveTo(campManager.GetCampCenter(faction));
            }
        }
        else if (motor != null && campManager != null)
        {
            if (retreatPhase == RetreatPhase.ToGateOutside || retreatPhase == RetreatPhase.ToGateInside)
            {
                if (!motor.HasDestination && !motor.IsBlockedBySolidObstacle)
                {
                    motor.MoveTo(GetRetreatDestination(campManager));
                }
            }
            else if (!campManager.HasReachedCampCenter(transform.position, faction)
                && !motor.HasDestination
                && !motor.IsBlockedBySolidObstacle)
            {
                motor.MoveTo(campManager.GetCampCenter(faction));
            }
        }

        if (Time.time >= nextRetreatDestinationRefreshTime && motor != null && !motor.IsBlockedBySolidObstacle)
        {
            nextRetreatDestinationRefreshTime = Time.time + retreatDestinationRefreshInterval;
            UpdateRetreatDestination();
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

        motor.MoveTo(GetRetreatDestination(campManager));
    }

    private Vector3 GetRetreatDestination(RtsCampManager campManager)
    {
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
        if (troopVisuals.Count == 0)
        {
            return;
        }

        if (motor != null && motor.IsBlockedBySolidObstacle)
        {
            return;
        }

        Vector3 regimentDelta = transform.position - lastRegimentPosition;
        lastRegimentPosition = transform.position;
        regimentDelta.y = 0f;

        Vector3 facingDirection = Vector3.zero;
        if (regimentDelta.sqrMagnitude >= MinimumFacingMovementDistance * MinimumFacingMovementDistance)
        {
            facingDirection = regimentDelta.normalized;
        }
        else if (motor != null && motor.HasDestination)
        {
            facingDirection = motor.MoveDirection;
            facingDirection.y = 0f;
        }

        if (facingDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        ApplyFacingToTroopVisuals(GetTroopFacingRotation(facingDirection));
    }

    private void EnsureFormationRootUpright()
    {
        transform.rotation = Quaternion.identity;
        if (troopVisualRoot != null)
        {
            troopVisualRoot.localRotation = Quaternion.identity;
        }
    }

    private void ApplyFacingToTroopVisuals(Quaternion facing)
    {
        EnsureFormationRootUpright();

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

        float yaw = Mathf.Atan2(worldDirection.x, worldDirection.z) * Mathf.Rad2Deg + troopFacingYawOffsetDegrees;
        return Quaternion.Euler(0f, yaw, 0f);
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

        RtsCampManager campManager = RtsCampManager.Instance;
        retreatPhase = campManager != null && campManager.HasGate(faction)
            ? RetreatPhase.ToGateOutside
            : RetreatPhase.ToCamp;

        if (motor != null)
        {
            motor.CanReceiveCommands = false;
            motor.MoveSpeedMultiplier = retreatMoveSpeedMultiplier;
            UpdateRetreatDestination();
        }

        SyncTroopVisualsToHealth(forceMinimum: true);
        SetFlagHolderDefeatedVisual();
        RegimentEnteredRetreat?.Invoke(this);
    }

    private void EnterRegroup()
    {
        CurrentState = State.Regroup;
        currentTarget = null;
        invulnerableUntil = float.PositiveInfinity;

        if (motor != null)
        {
            motor.Stop();
            motor.CanReceiveCommands = false;
            motor.MoveSpeedMultiplier = 1f;
        }
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
            motor.CanReceiveCommands = faction == Faction.Friendly && motor.IsCommandUnit;
            motor.MoveSpeedMultiplier = 1f;
        }

        if (faction == Faction.Enemy)
        {
            SetHoldInCampUntilNextWave(true);
        }

        RegimentRegroupCompleted?.Invoke(this);
    }

    public void TakeDamage(float amount, TroopCombat attacker = null, bool isRangedAttack = false)
    {
        if (CurrentState == State.Dead)
        {
            return;
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
            EnterRetreat();
        }
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
        if (other == null || CurrentState == State.Dead || CurrentState == State.Regroup)
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
        if (enemyAi == null)
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

        isPermanentlyEliminated = true;
        CurrentState = State.Dead;
        currentTarget = null;

        if (motor != null)
        {
            motor.Stop();
            motor.CanReceiveCommands = false;
            motor.MoveSpeedMultiplier = 1f;
        }

        if (retreatDeathDisappearCoroutine != null)
        {
            StopCoroutine(retreatDeathDisappearCoroutine);
            retreatDeathDisappearCoroutine = null;
        }

        if (HasActiveTroopVisuals())
        {
            retreatDeathDisappearCoroutine = StartCoroutine(PlayRetreatDeathDisappearSequence());
            return;
        }

        CompletePermanentDestroy();
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
                HideTroopVisualAt(disappearOrder[i]);
            }
        }
        else
        {
            float interval = span / disappearCount;
            for (int i = 0; i < disappearCount; i++)
            {
                HideTroopVisualAt(disappearOrder[i]);
                if (i < disappearCount - 1)
                {
                    yield return new WaitForSeconds(interval);
                }
            }
        }

        retreatDeathDisappearCoroutine = null;
        CompletePermanentDestroy();
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
            Destroy(gameObject);
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