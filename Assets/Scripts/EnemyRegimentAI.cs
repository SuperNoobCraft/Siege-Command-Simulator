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
        WaitingAtGateOutside,
        Advancing,
        Kiting,
        CampingRetreatRoute,
        Flanking
    }

    [Header("Wave Assignment")]
    [SerializeField] private AssignedWave assignedWave = AssignedWave.Wave1;
    [Tooltip("When enabled, this regiment can deploy in Siege PVP. Wave modes ignore this and use Assigned Wave.")]
    [SerializeField] private bool includeInSiegePvp = false;

    [Header("Behavior")]
    [SerializeField] private AiMode aiMode = AiMode.Advanced;
    [SerializeField, Min(1f)] private float visionRange = 24f;
    [SerializeField, Min(0.1f)] private float decisionInterval = 0.35f;
    [SerializeField, Min(0.1f)] private float destinationRefreshInterval = 0.75f;
    [SerializeField, Min(0f)] private float formationSpreadRadius = 2.5f;
    [SerializeField, Min(0f)] private float formationJitterRadius = 1.25f;
    [SerializeField, Range(0f, 1f)] private float advanceLateralSweepChance = 0.35f;
    [SerializeField, Min(0f)] private float advanceLateralSweepDistance = 4f;
    [SerializeField] private LayerMask visionLayers = ~0;

    [Header("Range Advantage (Archer Logic)")]
    [SerializeField] private bool useRangeAdvantageKiting = true;
    [SerializeField, Min(0f)] private float kiteRangeBuffer = 0.75f;

    [Header("Advanced Tactics")]
    [SerializeField, Range(0f, 1f)] private float retreatCampChance = 0.2f;
    [SerializeField, Range(0f, 1f)] private float encirclementChance = 0.12f;
    [SerializeField, Min(1)] private int encirclementMinFriendlyCount = 2;
    [SerializeField, Min(1)] private int encirclementMinEnemyCount = 2;
    [SerializeField, Min(2f)] private float encirclementFlankDistance = 8f;

    [Header("Cannon Focus")]
    [Tooltip("Keep marching on cannons when friendlies are merely spotted. Only fight blockers or cannon defenders.")]
    [SerializeField] private bool prioritizeCannonsOverTroops = true;
    [Tooltip("When a friendly unit is within this distance of a cannon, AI may treat them as a cannon defender.")]
    [SerializeField, Min(0f)] private float friendlyCannonProximityRadius = 12f;
    [Tooltip("Stop and hold once this close to the assigned cannon objective.")]
    [SerializeField, Min(0.5f)] private float objectiveArrivalRadius = 3.5f;
    [Tooltip("Ignored while Prioritize Cannons Over Troops is enabled.")]
    [SerializeField, Range(0f, 1f)] private float unitInterceptChanceNearCannon = 0.08f;
    [Tooltip("Width of the corridor treated as a direct blocking path to the cannon.")]
    [SerializeField, Min(0.5f)] private float directPathBlockWidth = 3.5f;

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
    private bool hasAssignedCannonObjective;
    private Vector3 assignedCannonObjective;
    private float formationAngleOffset;
    private float formationRadiusMultiplier;
    private int formationSlot;
    private Vector2 formationJitter;
    private float advanceSweepSign;
    private bool useAdvanceLateralSweep;
    private bool hasCachedObjectiveDestination;
    private bool isHoldingAtObjective;
    private Vector3 cachedObjectiveDestination;
    private Vector3 tacticalDestination;
    private readonly List<TroopCombat> visibleFriendlies = new List<TroopCombat>();
    private readonly List<TroopCombat> visibleEnemies = new List<TroopCombat>();
    private bool siegePvpPlayerControlled;
    private bool stagingToGateAfterRegroup;

    public AssignedWave WaveAssignment => assignedWave;
    public int AssignedWaveNumber => (int)assignedWave;
    public bool IncludeInSiegePvp => includeInSiegePvp;
    public bool HasEnteredBattlefield => hasEnteredBattlefield;
    public bool IsWaitingInCamp => phase == AiPhase.WaitingInCamp;
    public bool IsExitingGate => phase == AiPhase.ExitingGate;
    public bool IsWaitingAtGateOutside => phase == AiPhase.WaitingAtGateOutside;
    public bool IsDeployedOnField => hasEnteredBattlefield
        && phase != AiPhase.WaitingInCamp
        && phase != AiPhase.ExitingGate
        && phase != AiPhase.WaitingAtGateOutside;
    public bool IsPlayerControlledInSiegePvp => siegePvpPlayerControlled;

    public void ResetForMatchStart()
    {
        if (combat == null)
        {
            combat = GetComponent<TroopCombat>();
        }

        if (motor == null)
        {
            motor = GetComponent<RtsUnitMotor>();
        }

        if (combat != null)
        {
            combat.SetHoldInCampUntilNextWave(false);
        }

        hasEnteredBattlefield = false;
        hasAssignedCannonObjective = false;
        hasCachedObjectiveDestination = false;
        isHoldingAtObjective = false;
        unstuckAttemptIndex = 0;
        nextDecisionTime = 0f;
        nextDestinationRefreshTime = 0f;
        nextUnstuckAttemptTime = 0f;
        tacticalDestination = Vector3.zero;
        assignedCannonObjective = Vector3.zero;
        phase = AiPhase.WaitingInCamp;
        visibleFriendlies.Clear();
        visibleEnemies.Clear();
        siegePvpPlayerControlled = false;
        stagingToGateAfterRegroup = false;

        if (motor != null)
        {
            motor.Stop();
            motor.CanReceiveCommands = false;
        }
    }

    public void ConfigureForSiegePvp()
    {
        siegePvpPlayerControlled = true;
        stagingToGateAfterRegroup = false;
        if (combat != null)
        {
            combat.SetHoldInCampUntilNextWave(false);
        }

        if (motor != null)
        {
            motor.SetIsCommandUnit(true);
            // Enabled after staging at gate / on deploy finish; disabled while exiting gate.
            motor.CanReceiveCommands = phase == AiPhase.WaitingAtGateOutside
                || (hasEnteredBattlefield && phase != AiPhase.ExitingGate && phase != AiPhase.WaitingInCamp);
        }
    }

    public void ClearSiegePvpControl()
    {
        siegePvpPlayerControlled = false;
        stagingToGateAfterRegroup = false;
        if (motor != null && !SiegeMatchSettings.IsSiegePvpMode)
        {
            motor.CanReceiveCommands = false;
        }
    }

    private void Awake()
    {
        combat = GetComponent<TroopCombat>();
        motor = GetComponent<RtsUnitMotor>();
        motor.CanReceiveCommands = false;
        formationAngleOffset = Random.Range(0f, Mathf.PI * 2f);
        formationRadiusMultiplier = Random.Range(0.65f, 1.35f);
        formationSlot = Random.Range(0, 8);
        formationJitter = Random.insideUnitCircle;
        advanceSweepSign = Random.value < 0.5f ? -1f : 1f;

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
        TroopCombat.RegimentRegroupCompleted += HandleRegimentRegroupCompleted;
    }

    private void OnDisable()
    {
        EnemyWaveController.Unregister(this);
        TroopCombat.RegimentRegroupCompleted -= HandleRegimentRegroupCompleted;
    }

    private void Update()
    {
        if (SiegeMatchSettings.IsArenaSurvivalMode)
        {
            return;
        }

        if (combat == null || combat.TroopFaction != TroopCombat.Faction.Enemy)
        {
            return;
        }

        if (combat.CurrentState == TroopCombat.State.Dead)
        {
            return;
        }

        if (siegePvpPlayerControlled || SiegeMatchSettings.IsSiegePvpMode)
        {
            UpdateSiegePvpPlayerControl();
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

        UpdateObjectiveHoldState();

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
        EnforceFriendlyCampBoundary();
        EnforceCannonObjectivePriority();
    }

    private void UpdateSiegePvpPlayerControl()
    {
        TrackRegroupTransitions();

        if (combat.IsRetreating || combat.IsRegrouping)
        {
            return;
        }

        if (stagingToGateAfterRegroup || phase == AiPhase.ExitingGate)
        {
            UpdateGateExit();
            return;
        }

        if (phase == AiPhase.WaitingAtGateOutside || phase == AiPhase.WaitingInCamp)
        {
            if (motor != null && motor.IsCommandUnit)
            {
                motor.CanReceiveCommands = true;
            }

            // Player has taken over — leave the wait-at-gate loop. Staying here and calling
            // Stop() whenever HasDestination flickers was causing defender-side jitter.
            if (motor != null && (motor.HasActivePath || motor.HasDestination))
            {
                phase = AiPhase.Advancing;
                return;
            }

            if (phase == AiPhase.WaitingAtGateOutside && motor != null && !motor.HasDestination)
            {
                motor.Stop();
            }

            return;
        }

        // After the player has issued orders, leave path following to the motor/combat.
        if (motor != null && motor.IsCommandUnit)
        {
            motor.CanReceiveCommands = true;
        }
    }

    private void HandleRegimentRegroupCompleted(TroopCombat regiment)
    {
        if (regiment != combat || !SiegeMatchSettings.IsSiegePvpMode)
        {
            return;
        }

        StageOutsideGateAfterRegroup();
    }

    /// <summary>
    /// After regroup in PVP: leave camp, exit the gate, and wait at the outside waypoint for the city defender's orders.
    /// </summary>
    public void StageOutsideGateAfterRegroup()
    {
        if (combat == null || combat.CurrentState == TroopCombat.State.Dead)
        {
            return;
        }

        combat.SetHoldInCampUntilNextWave(false);
        hasEnteredBattlefield = true;
        stagingToGateAfterRegroup = true;
        phase = AiPhase.ExitingGate;
        if (motor != null)
        {
            motor.CanReceiveCommands = false;
        }

        BeginGateExit();
    }

    public bool ShouldEngageFriendlyForCombat(TroopCombat friendly)
    {
        if (friendly == null || friendly.TroopFaction != TroopCombat.Faction.Friendly)
        {
            return false;
        }

        if (friendly.IsProtectedByFriendlyCamp())
        {
            return false;
        }

        RtsCampManager campManager = RtsCampManager.Instance;
        if (campManager != null && campManager.IsInFriendlyProtectedZone(friendly.transform.position))
        {
            return false;
        }

        RtsSiegeObjectives objectives = RtsSiegeObjectives.Instance;
        if (objectives == null || !objectives.HasCannons())
        {
            return false;
        }

        Vector3 friendlyPosition = friendly.transform.position;
        Vector3 cannonPosition = GetCannonObjectivePosition();
        float friendlyDistanceToCannon = GetHorizontalDistance(friendlyPosition, cannonPosition);

        if (campManager != null)
        {
            float friendlyDistanceToCamp = GetHorizontalDistance(
                friendlyPosition,
                campManager.GetCampCenter(TroopCombat.Faction.Friendly));
            if (friendlyDistanceToCamp + 2f < friendlyDistanceToCannon)
            {
                return false;
            }
        }

        return ShouldPrioritizeFriendlyUnit(friendlyPosition);
    }

    private void EnforceCannonObjectivePriority()
    {
        if (phase == AiPhase.WaitingInCamp || phase == AiPhase.ExitingGate)
        {
            return;
        }

        TroopCombat target = combat.CurrentTarget;
        if (target == null || target.TroopFaction != TroopCombat.Faction.Friendly)
        {
            return;
        }

        if (ShouldEngageFriendlyForCombat(target))
        {
            return;
        }

        combat.ClearCurrentTarget();

        if (ShouldHoldAtObjective())
        {
            return;
        }

        if (phase == AiPhase.Kiting || phase == AiPhase.CampingRetreatRoute)
        {
            phase = AiPhase.Advancing;
        }

        IssueMoveOrder(GetPrimaryObjectivePosition());
    }

    private void EnforceFriendlyCampBoundary()
    {
        if (phase == AiPhase.WaitingInCamp || phase == AiPhase.ExitingGate)
        {
            return;
        }

        RtsCampManager campManager = RtsCampManager.Instance;
        if (campManager == null || !campManager.IsInFriendlyProtectedZone(transform.position))
        {
            return;
        }

        if (combat.CurrentState == TroopCombat.State.Fight)
        {
            combat.ClearCurrentTarget();
        }

        if (phase == AiPhase.Kiting || phase == AiPhase.CampingRetreatRoute)
        {
            phase = AiPhase.Advancing;
        }

        IssueMoveOrder(campManager.GetGateOutsidePosition(TroopCombat.Faction.Friendly));
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
        stagingToGateAfterRegroup = SiegeMatchSettings.IsSiegePvpMode;
        phase = AiPhase.ExitingGate;
        nextDecisionTime = 0f;
        nextDestinationRefreshTime = 0f;
        useAdvanceLateralSweep = Random.value < advanceLateralSweepChance;
        AssignRandomCannonObjective();
        CacheObjectiveDestination();
        if (SiegeMatchSettings.IsSiegePvpMode && motor != null)
        {
            motor.CanReceiveCommands = false;
        }

        BeginGateExit();
    }

    private void CacheObjectiveDestination()
    {
        cachedObjectiveDestination = BuildFormationDestination(GetCannonObjectivePosition());
        hasCachedObjectiveDestination = true;
        isHoldingAtObjective = false;
    }

    private void UpdateObjectiveHoldState()
    {
        if (!prioritizeCannonsOverTroops || phase != AiPhase.Advancing)
        {
            isHoldingAtObjective = false;
            return;
        }

        if (combat.CurrentState == TroopCombat.State.Fight)
        {
            return;
        }

        if (isHoldingAtObjective)
        {
            if (!IsNearObjectiveArea())
            {
                isHoldingAtObjective = false;
                return;
            }

            motor.Stop();
            return;
        }

        if (IsNearObjectiveArea())
        {
            isHoldingAtObjective = true;
            motor.Stop();
        }
    }

    private bool IsNearObjectiveArea()
    {
        float arrivalRadiusSqr = objectiveArrivalRadius * objectiveArrivalRadius;
        if (hasCachedObjectiveDestination
            && GetHorizontalDistanceSqr(transform.position, cachedObjectiveDestination) <= arrivalRadiusSqr)
        {
            return true;
        }

        return GetHorizontalDistanceSqr(transform.position, GetCannonObjectivePosition()) <= arrivalRadiusSqr;
    }

    private bool ShouldHoldAtObjective()
    {
        return prioritizeCannonsOverTroops
            && phase == AiPhase.Advancing
            && isHoldingAtObjective
            && combat.CurrentState != TroopCombat.State.Fight;
    }

    private void AssignRandomCannonObjective()
    {
        RtsSiegeObjectives objectives = RtsSiegeObjectives.Instance;
        if (objectives == null || !objectives.HasCannons())
        {
            hasAssignedCannonObjective = false;
            return;
        }

        assignedCannonObjective = objectives.GetRandomCannonPosition(transform.position);
        hasAssignedCannonObjective = true;
    }

    private Vector3 GetCannonObjectivePosition()
    {
        if (hasAssignedCannonObjective)
        {
            return assignedCannonObjective;
        }

        RtsSiegeObjectives objectives = RtsSiegeObjectives.Instance;
        if (objectives != null && objectives.HasCannons())
        {
            return objectives.GetRandomCannonPosition(transform.position);
        }

        return transform.position + transform.forward * 10f;
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
            FinishGateExit();
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
            FinishGateExit();
            return;
        }

        if (campManager.IsAtGateOutside(transform.position, TroopCombat.Faction.Enemy))
        {
            FinishGateExit();
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

    private void FinishGateExit()
    {
        if (SiegeMatchSettings.IsSiegePvpMode || stagingToGateAfterRegroup)
        {
            phase = AiPhase.WaitingAtGateOutside;
            stagingToGateAfterRegroup = false;
            if (motor != null)
            {
                motor.Stop();
                if (motor.IsCommandUnit)
                {
                    motor.CanReceiveCommands = true;
                }
            }

            return;
        }

        phase = AiPhase.Advancing;
        RefreshMovementDestination();
    }

    private void UpdateObstacleRecovery()
    {
        if (phase == AiPhase.WaitingInCamp)
        {
            return;
        }

        if (ShouldHoldAtObjective())
        {
            unstuckAttemptIndex = 0;
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

            if (troop.TroopFaction == TroopCombat.Faction.Friendly && troop.IsProtectedByFriendlyCamp())
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

        if (!prioritizeCannonsOverTroops)
        {
            if (useRangeAdvantageKiting && TryUpdateRangeAdvantageKite())
            {
                return;
            }

            if (aiMode == AiMode.Advanced && TryChooseRetreatCamp())
            {
                return;
            }
        }

        if (phase == AiPhase.Kiting || (prioritizeCannonsOverTroops && phase == AiPhase.CampingRetreatRoute))
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

        if (!IsRetreatInterceptWorthwhile(retreatingFriendly))
        {
            return false;
        }

        if (!ShouldEngageFriendlyForCombat(retreatingFriendly))
        {
            return false;
        }

        if (!ShouldPrioritizeFriendlyUnit(retreatingFriendly.transform.position))
        {
            return false;
        }

        RtsSiegeObjectives objectives = RtsSiegeObjectives.Instance;
        tacticalDestination = objectives != null
            ? objectives.GetBestRetreatInterceptPosition(transform.position, retreatingFriendly.transform.position)
            : retreatingFriendly.transform.position;

        RtsCampManager campManager = RtsCampManager.Instance;
        if (campManager != null)
        {
            tacticalDestination = campManager.ClampOutsideFriendlyProtectedZone(tacticalDestination);
        }

        phase = AiPhase.CampingRetreatRoute;
        IssueMoveOrder(tacticalDestination);
        return true;
    }

    private TroopCombat FindVisibleRetreatingFriendly()
    {
        RtsCampManager campManager = RtsCampManager.Instance;
        for (int i = 0; i < visibleFriendlies.Count; i++)
        {
            TroopCombat friendly = visibleFriendlies[i];
            if (friendly == null || !friendly.IsRetreating)
            {
                continue;
            }

            if (campManager != null
                && (campManager.IsInFriendlyProtectedZone(friendly.transform.position)
                    || friendly.IsProtectedByFriendlyCamp()))
            {
                continue;
            }

            return friendly;
        }

        return null;
    }

    private void RefreshMovementDestination()
    {
        if (phase == AiPhase.WaitingInCamp || phase == AiPhase.ExitingGate)
        {
            return;
        }

        if (ShouldHoldAtObjective())
        {
            motor.Stop();
            return;
        }

        if (prioritizeCannonsOverTroops)
        {
            if (phase == AiPhase.Kiting || phase == AiPhase.CampingRetreatRoute || phase == AiPhase.Flanking)
            {
                phase = AiPhase.Advancing;
            }
        }
        else if (useRangeAdvantageKiting && TryUpdateRangeAdvantageKite())
        {
            return;
        }

        if (!prioritizeCannonsOverTroops && phase == AiPhase.Kiting)
        {
            phase = AiPhase.Advancing;
        }

        if (combat.CurrentState == TroopCombat.State.Fight
            && phase != AiPhase.CampingRetreatRoute
            && phase != AiPhase.Flanking)
        {
            TroopCombat target = combat.CurrentTarget;
            if (target != null && ShouldEngageFriendlyForCombat(target))
            {
                return;
            }

            phase = AiPhase.Advancing;
            IssueMoveOrder(GetPrimaryObjectivePosition());
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
        if (prioritizeCannonsOverTroops)
        {
            return false;
        }

        TroopCombat target = GetRangeAdvantageTarget();
        if (target == null)
        {
            if (phase == AiPhase.Kiting)
            {
                phase = AiPhase.Advancing;
            }

            return false;
        }

        if (!ShouldPrioritizeFriendlyUnit(target.transform.position))
        {
            if (phase == AiPhase.Kiting)
            {
                phase = AiPhase.Advancing;
            }

            return false;
        }

        if (!ShouldEngageFriendlyForCombat(target))
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
        RtsCampManager campManager = RtsCampManager.Instance;
        if (campManager != null)
        {
            tacticalDestination = campManager.ClampOutsideFriendlyProtectedZone(tacticalDestination);
        }

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

        RtsCampManager campManager = RtsCampManager.Instance;
        if (campManager != null
            && (campManager.IsInFriendlyProtectedZone(target.transform.position)
                || target.IsProtectedByFriendlyCamp()))
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
        if (hasCachedObjectiveDestination)
        {
            return cachedObjectiveDestination;
        }

        return BuildFormationDestination(GetCannonObjectivePosition());
    }

    private Vector3 BuildFormationDestination(Vector3 cannonPosition)
    {
        Vector3 destination = cannonPosition;
        if (formationSpreadRadius <= 0f)
        {
            return destination;
        }

        float angle = formationAngleOffset + formationSlot * Mathf.PI * 0.25f;
        float radius = formationSpreadRadius * formationRadiusMultiplier;
        Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

        if (formationJitterRadius > 0f)
        {
            offset += new Vector3(formationJitter.x, 0f, formationJitter.y) * formationJitterRadius;
        }

        destination += offset;

        if (useAdvanceLateralSweep)
        {
            Vector3 approachDirection = cannonPosition - transform.position;
            approachDirection.y = 0f;
            if (approachDirection.sqrMagnitude < 0.0001f)
            {
                approachDirection = transform.forward;
            }

            approachDirection.Normalize();
            Vector3 tangent = new Vector3(-approachDirection.z, 0f, approachDirection.x);
            destination += tangent * (advanceLateralSweepDistance * advanceSweepSign);
        }

        return destination;
    }

    private bool ShouldPrioritizeFriendlyUnit(Vector3 friendlyPosition)
    {
        RtsCampManager campManager = RtsCampManager.Instance;
        if (campManager != null
            && campManager.IsInFriendlyProtectedZone(friendlyPosition))
        {
            return false;
        }

        RtsSiegeObjectives objectives = RtsSiegeObjectives.Instance;
        if (objectives == null || !objectives.HasCannons())
        {
            return false;
        }

        Vector3 cannonPosition = GetCannonObjectivePosition();

        if (IsBlockingDirectPathToCannon(friendlyPosition, cannonPosition))
        {
            return true;
        }

        return IsDefendingCannon(friendlyPosition, cannonPosition);
    }

    private bool IsDefendingCannon(Vector3 friendlyPosition, Vector3 cannonPosition)
    {
        return GetHorizontalDistance(friendlyPosition, cannonPosition) <= friendlyCannonProximityRadius;
    }

    private bool IsRetreatInterceptWorthwhile(TroopCombat retreatingFriendly)
    {
        if (retreatingFriendly == null)
        {
            return false;
        }

        RtsSiegeObjectives objectives = RtsSiegeObjectives.Instance;
        if (objectives == null || !objectives.HasCannons())
        {
            return false;
        }

        Vector3 cannonPosition = GetCannonObjectivePosition();
        Vector3 retreatPosition = retreatingFriendly.transform.position;

        RtsCampManager campManager = RtsCampManager.Instance;
        if (campManager != null)
        {
            float distanceToCamp = GetHorizontalDistance(
                retreatPosition,
                campManager.GetCampCenter(TroopCombat.Faction.Friendly));
            float distanceToCannon = GetHorizontalDistance(retreatPosition, cannonPosition);
            if (distanceToCamp + 2f < distanceToCannon)
            {
                return false;
            }
        }

        if (IsBlockingDirectPathToCannon(retreatPosition, cannonPosition))
        {
            return true;
        }

        return false;
    }

    private bool IsBlockingDirectPathToCannon(Vector3 friendlyPosition, Vector3 cannonPosition)
    {
        RtsCampManager campManager = RtsCampManager.Instance;
        if (campManager != null && campManager.IsInFriendlyProtectedZone(friendlyPosition))
        {
            return false;
        }

        Vector3 path = cannonPosition - transform.position;
        path.y = 0f;
        float pathLength = path.magnitude;
        if (pathLength <= 0.01f)
        {
            return false;
        }

        Vector3 pathDirection = path / pathLength;
        Vector3 toFriendly = friendlyPosition - transform.position;
        toFriendly.y = 0f;
        float forwardDistance = Vector3.Dot(toFriendly, pathDirection);
        if (forwardDistance <= 0f || forwardDistance >= pathLength)
        {
            return false;
        }

        Vector3 closestPoint = transform.position + pathDirection * forwardDistance;
        float lateralDistance = GetHorizontalDistance(closestPoint, friendlyPosition);
        return lateralDistance <= directPathBlockWidth;
    }

    private void IssueMoveOrder(Vector3 destination)
    {
        if (ShouldHoldAtObjective())
        {
            motor.Stop();
            return;
        }

        destination.y = transform.position.y;
        RtsCampManager campManager = RtsCampManager.Instance;
        if (campManager != null)
        {
            destination = campManager.ClampOutsideFriendlyProtectedZone(destination);
        }

        tacticalDestination = destination;

        float arrivalRadiusSqr = objectiveArrivalRadius * objectiveArrivalRadius;
        if (GetHorizontalDistanceSqr(transform.position, destination) <= arrivalRadiusSqr)
        {
            if (prioritizeCannonsOverTroops && phase == AiPhase.Advancing)
            {
                isHoldingAtObjective = true;
            }

            motor.Stop();
            return;
        }

        if (!motor.HasDestination
            || motor.IsBlockedBySolidObstacle
            || motor.IsStuck
            || GetHorizontalDistanceSqr(transform.position, destination) > arrivalRadiusSqr)
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
        if (prioritizeCannonsOverTroops)
        {
            return false;
        }

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
