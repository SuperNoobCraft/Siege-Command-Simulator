using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-enemy-regiment AI. Units are placed inside the enemy camp and deploy on wave timing.
/// </summary>
[DefaultExecutionOrder(75)]
[RequireComponent(typeof(TroopCombat))]
[RequireComponent(typeof(RtsUnitMotor))]
public class EnemyRegimentAI : MonoBehaviour
{
    public enum AssignedWave
    {
        Wave1 = 1,
        Wave2 = 2,
        Wave3 = 3
    }

    public enum AiMode
    {
        Standard,
        Advanced
    }

    private enum AiPhase
    {
        WaitingInCamp,
        ExitingGate,
        Advancing,
        Kiting,
        CampingRetreatRoute,
        Flanking
    }

    [Header("Wave Assignment")]
    [SerializeField] private AssignedWave assignedWave = AssignedWave.Wave1;

    [Header("Behavior")]
    [SerializeField] private AiMode aiMode = AiMode.Advanced;
    [SerializeField, Min(1f)] private float visionRange = 24f;
    [SerializeField, Min(0.1f)] private float decisionInterval = 0.35f;
    [SerializeField, Min(0.1f)] private float destinationRefreshInterval = 0.75f;
    [SerializeField, Min(0f)] private float formationSpreadRadius = 2.5f;
    [SerializeField] private LayerMask visionLayers = ~0;

    [Header("Range Advantage (Archer Logic)")]
    [SerializeField] private bool useRangeAdvantageKiting = true;
    [SerializeField, Min(0f)] private float kiteRangeBuffer = 0.75f;

    [Header("Advanced Tactics")]
    [SerializeField, Range(0f, 1f)] private float retreatCampChance = 0.65f;
    [SerializeField, Range(0f, 1f)] private float encirclementChance = 0.45f;
    [SerializeField, Min(1)] private int encirclementMinFriendlyCount = 2;
    [SerializeField, Min(1)] private int encirclementMinEnemyCount = 2;
    [SerializeField, Min(2f)] private float encirclementFlankDistance = 8f;

    [Header("Obstacle Recovery")]
    [SerializeField, Min(0.1f)] private float unstuckRetryInterval = 0.5f;
    [SerializeField, Min(0.5f)] private float unstuckOffsetDistance = 3f;

    [Header("Debug")]
    [SerializeField] private bool drawDebugGizmos = true;
    [SerializeField] private Color visionGizmoColor = new Color(1f, 0.35f, 0.2f, 0.25f);

    private TroopCombat combat;
    private RtsUnitMotor motor;
    private AiPhase phase = AiPhase.WaitingInCamp;
    private float nextDecisionTime;
    private float nextDestinationRefreshTime;
    private float nextUnstuckAttemptTime;
    private int unstuckAttemptIndex;
    private bool hasEnteredBattlefield;
    private Vector3 tacticalDestination;
    private readonly List<TroopCombat> visibleFriendlies = new List<TroopCombat>();
    private readonly List<TroopCombat> visibleEnemies = new List<TroopCombat>();

    public AssignedWave WaveAssignment => assignedWave;
    public int AssignedWaveNumber => (int)assignedWave;
    public bool HasEnteredBattlefield => hasEnteredBattlefield;
    public bool IsWaitingInCamp => phase == AiPhase.WaitingInCamp;
    public bool IsExitingGate => phase == AiPhase.ExitingGate;
    public bool IsDeployedOnField => hasEnteredBattlefield && phase != AiPhase.WaitingInCamp && phase != AiPhase.ExitingGate;

    private void Awake()
    {
        combat = GetComponent<TroopCombat>();
        motor = GetComponent<RtsUnitMotor>();
        motor.CanReceiveCommands = false;

        if (combat != null && combat.TroopFaction != TroopCombat.Faction.Enemy)
        {
            Debug.LogWarning(
                "EnemyRegimentAI on '" + name + "' requires TroopCombat faction Enemy.",
                this);
        }
    }

    private void Start()
    {
        EnemyWaveController.Register(this);
    }

    private void OnEnable()
    {
        EnemyWaveController.Register(this);
    }

    private void OnDisable()
    {
        EnemyWaveController.Unregister(this);
    }

    private void Update()
    {
        if (combat == null || combat.TroopFaction != TroopCombat.Faction.Enemy)
        {
            return;
        }

        if (combat.CurrentState == TroopCombat.State.Dead)
        {
            return;
        }

        TrackRegroupTransitions();

        if (combat.IsRetreating || combat.IsRegrouping)
        {
            if (phase != AiPhase.WaitingInCamp)
            {
                phase = AiPhase.WaitingInCamp;
                motor.Stop();
            }

            return;
        }

        if (combat.HoldsInCampUntilNextWave)
        {
            EnemyWaveController waveController = EnemyWaveController.Instance;
            if (waveController != null && waveController.AreAllWavesTriggered)
            {
                DeployFromCamp();
                return;
            }

            if (phase != AiPhase.WaitingInCamp)
            {
                phase = AiPhase.WaitingInCamp;
                motor.Stop();
            }

            return;
        }

        if (phase == AiPhase.WaitingInCamp)
        {
            motor.Stop();
            return;
        }

        if (Time.time >= nextDecisionTime)
        {
            nextDecisionTime = Time.time + decisionInterval;
            RefreshVision();
            ChooseTacticalPhase();
        }

        if (Time.time >= nextDestinationRefreshTime)
        {
            nextDestinationRefreshTime = Time.time + destinationRefreshInterval;
            RefreshMovementDestination();
        }

        UpdateGateExit();
        UpdateObstacleRecovery();
    }

    public bool CanDeployForWave(int currentWaveNumber)
    {
        if (combat == null || combat.CurrentState == TroopCombat.State.Dead)
        {
            return false;
        }

        if (AssignedWaveNumber > currentWaveNumber)
        {
            return false;
        }

        return IsWaitingInCamp;
    }

    public void DeployFromCamp()
    {
        if (combat.CurrentState == TroopCombat.State.Dead)
        {
            return;
        }

        combat.SetHoldInCampUntilNextWave(false);
        hasEnteredBattlefield = true;
        phase = AiPhase.ExitingGate;
        nextDecisionTime = 0f;
        nextDestinationRefreshTime = 0f;
        BeginGateExit();
    }

    public void AssignEncirclementDestination(Vector3 targetCenter, bool useLeftFlank)
    {
        if (phase == AiPhase.WaitingInCamp || phase == AiPhase.ExitingGate)
        {
            return;
        }

        Vector3 flankDirection = GetFlankDirection(targetCenter, useLeftFlank);
        tacticalDestination = targetCenter + flankDirection * encirclementFlankDistance;
        phase = AiPhase.Flanking;
        IssueMoveOrder(tacticalDestination);
    }

    private void TrackRegroupTransitions()
    {
        if (!hasEnteredBattlefield)
        {
            return;
        }

        if (combat.HoldsInCampUntilNextWave)
        {
            phase = AiPhase.WaitingInCamp;
            motor.Stop();
        }
    }

    private void BeginGateExit()
    {
        RtsCampManager campManager = RtsCampManager.Instance;
        if (campManager == null)
        {
            phase = AiPhase.Advancing;
            RefreshMovementDestination();
            return;
        }

        if (campManager.IsAtGateOutside(transform.position, TroopCombat.Faction.Enemy))
        {
            phase = AiPhase.Advancing;
            RefreshMovementDestination();
            return;
        }

        if (!campManager.IsAtGateInside(transform.position, TroopCombat.Faction.Enemy))
        {
            IssueMoveOrder(campManager.GetGateInsidePosition(TroopCombat.Faction.Enemy));
            return;
        }

        IssueMoveOrder(campManager.GetGateOutsidePosition(TroopCombat.Faction.Enemy));
    }

    private void UpdateGateExit()
    {
        if (phase != AiPhase.ExitingGate)
        {
            return;
        }

        RtsCampManager campManager = RtsCampManager.Instance;
        if (campManager == null)
        {
            phase = AiPhase.Advancing;
            return;
        }

        if (campManager.IsAtGateOutside(transform.position, TroopCombat.Faction.Enemy))
        {
            phase = AiPhase.Advancing;
            RefreshMovementDestination();
            return;
        }

        if (!motor.HasDestination && !motor.IsBlockedBySolidObstacle && !motor.IsStuck)
        {
            BeginGateExit();
        }
        else if (motor.IsBlockedBySolidObstacle || motor.IsStuck)
        {
            TryRecoverFromObstacle(campManager);
        }
    }

    private void UpdateObstacleRecovery()
    {
        if (phase == AiPhase.WaitingInCamp)
        {
            return;
        }

        if (!motor.IsBlockedBySolidObstacle && !motor.IsStuck)
        {
            unstuckAttemptIndex = 0;
            return;
        }

        if (Time.time < nextUnstuckAttemptTime)
        {
            return;
        }

        nextUnstuckAttemptTime = Time.time + unstuckRetryInterval;
        TryRecoverFromObstacle(RtsCampManager.Instance);
    }

    private void TryRecoverFromObstacle(RtsCampManager campManager)
    {
        if (phase == AiPhase.ExitingGate && campManager != null)
        {
            TryUnstuckGateExit(campManager);
            return;
        }

        Vector3 recoveryDestination = GetObstacleRecoveryDestination();
        IssueMoveOrder(recoveryDestination);
    }

    private Vector3 GetObstacleRecoveryDestination()
    {
        Vector3 goal = tacticalDestination;
        if (goal == Vector3.zero)
        {
            goal = GetPrimaryObjectivePosition();
        }

        Vector3 toGoal = goal - transform.position;
        toGoal.y = 0f;
        if (toGoal.sqrMagnitude < 0.0001f)
        {
            toGoal = transform.forward;
        }

        Vector3 forward = toGoal.normalized;
        Vector3 tangent = new Vector3(-forward.z, 0f, forward.x);
        float[] sideMultipliers = { 1f, -1f, 1.5f, -1.5f, 2f, -2f };
        int sideIndex = unstuckAttemptIndex % sideMultipliers.Length;
        unstuckAttemptIndex++;

        Vector3 offset = tangent * (unstuckOffsetDistance * sideMultipliers[sideIndex]);
        offset += forward * (unstuckOffsetDistance * 0.35f);
        return goal + offset;
    }

    private void TryUnstuckGateExit(RtsCampManager campManager)
    {
        Vector3 campCenter = campManager.GetCampCenter(TroopCombat.Faction.Enemy);
        if (GetHorizontalDistanceSqr(transform.position, campCenter) > 1f)
        {
            IssueMoveOrder(campCenter);
            return;
        }

        IssueMoveOrder(campManager.GetGateOutsidePosition(TroopCombat.Faction.Enemy));
    }

    private void RefreshVision()
    {
        visibleFriendlies.Clear();
        visibleEnemies.Clear();

        Collider[] hits = Physics.OverlapSphere(transform.position, visionRange, visionLayers, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
        {
            return;
        }

        for (int i = 0; i < hits.Length; i++)
        {
            TroopCombat troop = hits[i].GetComponentInParent<TroopCombat>();
            if (troop == null || troop == combat || troop.CurrentState == TroopCombat.State.Dead)
            {
                continue;
            }

            if (!troop.CanBeTargetedBy(combat) && troop.TroopFaction == TroopCombat.Faction.Friendly)
            {
                continue;
            }

            if (troop.TroopFaction == TroopCombat.Faction.Friendly)
            {
                if (!visibleFriendlies.Contains(troop))
                {
                    visibleFriendlies.Add(troop);
                }
            }
            else if (!visibleEnemies.Contains(troop))
            {
                visibleEnemies.Add(troop);
            }
        }
    }

    private void ChooseTacticalPhase()
    {
        if (phase == AiPhase.ExitingGate)
        {
            return;
        }

        if (useRangeAdvantageKiting && TryUpdateRangeAdvantageKite())
        {
            return;
        }

        if (aiMode == AiMode.Advanced && TryChooseRetreatCamp())
        {
            return;
        }

        if (phase == AiPhase.Kiting)
        {
            phase = AiPhase.Advancing;
        }
    }

    private bool TryChooseRetreatCamp()
    {
        TroopCombat retreatingFriendly = FindVisibleRetreatingFriendly();
        if (retreatingFriendly == null)
        {
            if (phase == AiPhase.CampingRetreatRoute)
            {
                phase = AiPhase.Advancing;
            }

            return false;
        }

        if (phase != AiPhase.CampingRetreatRoute && Random.value > retreatCampChance)
        {
            return false;
        }

        RtsSiegeObjectives objectives = RtsSiegeObjectives.Instance;
        tacticalDestination = objectives != null
            ? objectives.GetBestRetreatInterceptPosition(transform.position, retreatingFriendly.transform.position)
            : retreatingFriendly.transform.position;

        phase = AiPhase.CampingRetreatRoute;
        IssueMoveOrder(tacticalDestination);
        return true;
    }

    private TroopCombat FindVisibleRetreatingFriendly()
    {
        for (int i = 0; i < visibleFriendlies.Count; i++)
        {
            TroopCombat friendly = visibleFriendlies[i];
            if (friendly != null && friendly.IsRetreating)
            {
                return friendly;
            }
        }

        return null;
    }

    private void RefreshMovementDestination()
    {
        if (phase == AiPhase.WaitingInCamp || phase == AiPhase.ExitingGate)
        {
            return;
        }

        if (useRangeAdvantageKiting && TryUpdateRangeAdvantageKite())
        {
            return;
        }

        if (phase == AiPhase.Kiting)
        {
            phase = AiPhase.Advancing;
        }

        if (combat.CurrentState == TroopCombat.State.Fight
            && phase != AiPhase.CampingRetreatRoute
            && phase != AiPhase.Flanking)
        {
            return;
        }

        switch (phase)
        {
            case AiPhase.Kiting:
                IssueMoveOrder(tacticalDestination);
                break;
            case AiPhase.CampingRetreatRoute:
            case AiPhase.Flanking:
                IssueMoveOrder(tacticalDestination);
                break;
            default:
                IssueMoveOrder(GetPrimaryObjectivePosition());
                break;
        }
    }

    private bool TryUpdateRangeAdvantageKite()
    {
        TroopCombat target = GetRangeAdvantageTarget();
        if (target == null)
        {
            if (phase == AiPhase.Kiting)
            {
                phase = AiPhase.Advancing;
            }

            return false;
        }

        float myRange = combat.AttackRange;
        float theirRange = target.AttackRange;
        if (myRange <= theirRange + 0.01f)
        {
            if (phase == AiPhase.Kiting)
            {
                phase = AiPhase.Advancing;
            }

            return false;
        }

        float minSafeDistance = theirRange + kiteRangeBuffer;
        float maxKiteDistance = Mathf.Max(minSafeDistance, myRange - kiteRangeBuffer);
        float idealDistance = maxKiteDistance;
        float currentDistance = GetHorizontalDistance(transform.position, target.transform.position);

        phase = AiPhase.Kiting;

        if (currentDistance >= minSafeDistance && currentDistance <= myRange)
        {
            motor.Stop();
            return true;
        }

        tacticalDestination = GetKitePosition(target.transform.position, idealDistance);
        IssueMoveOrder(tacticalDestination);
        return true;
    }

    private TroopCombat GetRangeAdvantageTarget()
    {
        TroopCombat currentTarget = combat.CurrentTarget;
        if (IsValidKiteTarget(currentTarget))
        {
            return currentTarget;
        }

        TroopCombat bestTarget = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < visibleFriendlies.Count; i++)
        {
            TroopCombat friendly = visibleFriendlies[i];
            if (!IsValidKiteTarget(friendly))
            {
                continue;
            }

            float distance = GetHorizontalDistance(transform.position, friendly.transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestTarget = friendly;
            }
        }

        return bestTarget;
    }

    private bool IsValidKiteTarget(TroopCombat target)
    {
        if (target == null || target.CurrentState == TroopCombat.State.Dead || target.IsRetreating)
        {
            return false;
        }

        return combat.AttackRange > target.AttackRange + 0.01f;
    }

    private Vector3 GetKitePosition(Vector3 targetPosition, float idealDistance)
    {
        Vector3 offset = transform.position - targetPosition;
        offset.y = 0f;

        if (offset.sqrMagnitude < 0.0001f)
        {
            offset = Vector3.back;
        }

        Vector3 direction = offset.normalized;
        Vector3 destination = targetPosition + direction * idealDistance;
        destination.y = transform.position.y;
        return destination;
    }

    private static float GetHorizontalDistance(Vector3 a, Vector3 b)
    {
        Vector3 offset = a - b;
        offset.y = 0f;
        return offset.magnitude;
    }

    private Vector3 GetPrimaryObjectivePosition()
    {
        RtsSiegeObjectives objectives = RtsSiegeObjectives.Instance;
        Vector3 baseDestination = objectives != null && objectives.HasCannons()
            ? objectives.GetNearestCannonPosition(transform.position)
            : transform.position + transform.forward * 10f;

        return ApplyFormationOffset(baseDestination);
    }

    private Vector3 ApplyFormationOffset(Vector3 destination)
    {
        if (formationSpreadRadius <= 0f)
        {
            return destination;
        }

        int slot = Mathf.Abs(GetInstanceID()) % 8;
        float angle = slot * Mathf.PI * 0.25f;
        Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * formationSpreadRadius;
        return destination + offset;
    }

    private void IssueMoveOrder(Vector3 destination)
    {
        destination.y = transform.position.y;
        tacticalDestination = destination;

        if (!motor.HasDestination
            || motor.IsBlockedBySolidObstacle
            || motor.IsStuck
            || GetHorizontalDistanceSqr(transform.position, destination) > 1f)
        {
            motor.MoveTo(destination);
        }
    }

    private static Vector3 GetFlankDirection(Vector3 targetCenter, bool useLeftFlank)
    {
        RtsCampManager campManager = RtsCampManager.Instance;
        Vector3 referenceDirection = Vector3.forward;
        if (campManager != null && campManager.EnemyGateOutside != null && campManager.FriendlyCamp != null)
        {
            referenceDirection = campManager.FriendlyCamp.position - campManager.EnemyGateOutside.position;
        }

        referenceDirection.y = 0f;
        if (referenceDirection.sqrMagnitude < 0.0001f)
        {
            referenceDirection = Vector3.forward;
        }

        referenceDirection.Normalize();
        Vector3 flank = useLeftFlank
            ? new Vector3(-referenceDirection.z, 0f, referenceDirection.x)
            : new Vector3(referenceDirection.z, 0f, -referenceDirection.x);
        return flank.normalized;
    }

    private static float GetHorizontalDistanceSqr(Vector3 a, Vector3 b)
    {
        Vector3 offset = a - b;
        offset.y = 0f;
        return offset.sqrMagnitude;
    }

    internal bool CanParticipateInEncirclement()
    {
        return aiMode == AiMode.Advanced
            && IsDeployedOnField
            && combat.CurrentState != TroopCombat.State.Fight
            && combat.CurrentState != TroopCombat.State.Retreat
            && phase != AiPhase.CampingRetreatRoute
            && phase != AiPhase.Kiting;
    }

    internal float EncirclementChance => encirclementChance;
    internal int EncirclementMinFriendlyCount => encirclementMinFriendlyCount;
    internal int EncirclementMinEnemyCount => encirclementMinEnemyCount;
    internal float VisionRange => visionRange;

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos)
        {
            return;
        }

        Gizmos.color = visionGizmoColor;
        Gizmos.DrawWireSphere(transform.position, visionRange);

        if (Application.isPlaying && tacticalDestination != Vector3.zero)
        {
            Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.9f);
            Gizmos.DrawLine(transform.position, tacticalDestination);
            Gizmos.DrawWireSphere(tacticalDestination, 0.75f);
        }
    }
}
