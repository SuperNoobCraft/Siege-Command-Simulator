using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(50)]
public class RtsUnitMotor : MonoBehaviour
{
    private enum MovementMode
    {
        None,
        Direct,
        Path
    }

    [Header("Identity")]
    [SerializeField] private bool isCommandUnit = true;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 2.5f;
    [SerializeField, Min(0.05f)] private float stoppingDistance = 0.15f;
    [SerializeField, Min(0.05f)] private float arrivalRadius = 0.45f;
    [SerializeField, Min(0.05f)] private float arrivalSlowdownRadius = 1.5f;
    [SerializeField, Min(0.05f)] private float waypointPassRadius = 0.5f;
    [SerializeField] private bool scaleWaypointPassRadiusWithSpeed = true;
    [SerializeField, Min(0f)] private float waypointPassRadiusPerSpeed = 0.15f;

    [Header("Solid Obstacles")]
    [SerializeField] private LayerMask solidObstacleLayers;
    [Tooltip("Shrunk hull for sliding along walls on drawn paths. Solid blocking uses the full footprint instead.")]
    [SerializeField] private Collider movementBoundsCollider;
    [Tooltip("Full regiment footprint used to block cliffs/walls so edge troops cannot mount solids.")]
    [SerializeField] private Collider solidBlockCollider;
    [SerializeField] private Vector3 fallbackCastHalfExtents = new Vector3(1f, 0.5f, 1f);
    [SerializeField, Min(0f)] private float obstacleSkin = 0.05f;
    [Tooltip("Extra horizontal padding on solid casts so thin walls/cliffs still register.")]
    [SerializeField, Min(0f)] private float solidThicknessPadding = 0.2f;
    [Tooltip("Max cast step length (m). Larger frame steps are subdivided so thin solids cannot be tunneled.")]
    [SerializeField, Min(0.05f)] private float maxSolidCastStep = 0.2f;
    [Tooltip("Steeper than this across the footprint is treated as an unwalkable ledge (gentle hills are fine).")]
    [SerializeField, Range(15f, 80f)] private float maxWalkableSlopeDegrees = 55f;
    [Tooltip("Max vertical rise between current and next center ground samples (blocks cliffs/ledges).")]
    [SerializeField, Min(0.1f)] private float maxStepHeight = 2f;
    [Tooltip("Ground normals steeper than this are treated as unwalkable cliff faces.")]
    [SerializeField, Range(30f, 85f)] private float maxGroundNormalAngleDegrees = 60f;
    [Tooltip("Solid casts/overlaps only block within this height band above the regiment plane (ignores gate roofs).")]
    [SerializeField, Min(0.25f)] private float solidBodyHeight = 1.1f;
    [Tooltip("Bottom of the solid-check band above the regiment plane.")]
    [SerializeField, Min(0f)] private float solidBodyBottomClearance = 0.35f;
    [SerializeField] private bool stopImmediatelyOnWallHit = true;
    [SerializeField, Min(0f)] private float wallEscapeGraceDuration = 2.75f;
    [SerializeField, Range(0f, 1f)] private float wallEscapeDirectionThreshold = 0.1f;
    [SerializeField] private bool enableObstacleAvoidance = true;
    [Tooltip("How far ahead to steer away from walls before contact.")]
    [SerializeField, Min(0.5f)] private float obstacleLookaheadDistance = 2.75f;
    [Tooltip("When blocked on a direct move, try sliding along the wall like drawn paths.")]
    [SerializeField] private bool slideAlongWallOnDirectMove = true;
    [SerializeField, Min(0.1f)] private float stuckDetectionTime = 0.6f;
    [SerializeField, Min(0.05f)] private float stuckProgressDistance = 0.12f;
    [SerializeField, Range(0.1f, 1f)] private float stuckProgressSpeedFactor = 0.35f;
    [SerializeField, Min(0.5f)] private float detourProbeDistance = 4f;
    [SerializeField, Min(0.25f)] private float detourWaypointSpacing = 1.5f;

    [Header("Drawn Path Following")]
    [Tooltip("Shrinks the collision hull while following a drawn path so slight arcs near walls can still pass.")]
    [SerializeField, Min(0f)] private float pathCollisionBuffer = 0.45f;
    [Tooltip("Drop path waypoints closer than this (m) — noisy XR paths otherwise stall between micro segments.")]
    [SerializeField, Min(0.05f)] private float pathMinSegmentLength = 0.25f;
    [Tooltip("When blocked on a drawn path, try sliding along the wall instead of pushing into it.")]
    [SerializeField] private bool slideAlongWallOnPath = true;
    [SerializeField] private bool drawCollisionDebug;

    private static readonly float[] AvoidanceAngleOffsets =
    {
        0f, 20f, -20f, 40f, -40f, 60f, -60f, 80f, -80f, 100f, -100f, 120f, -120f
    };

    private Collider[] ownColliders;
    private MovementMode movementMode = MovementMode.None;
    private bool hasDestination;
    private Vector3 destination;
    private Vector3 finalDestination;
    private bool usingDetour;
    private readonly List<Vector3> pathWaypoints = new List<Vector3>();
    private int pathWaypointIndex;
    private float wallEscapeGraceEndTime;
    private Vector3 stuckSamplePosition;
    private float stuckSampleTime;
    private float stuckSampleGoalDistance;
    private float nextDetourAttemptTime;
    private int pathStuckConfirmCount;
    private int radialEscapeAttemptIndex;
    private int overlapStuckFrames;
    private float nextForcedEscapeAttemptTime;

    private static readonly float[] RadialEscapeAngleOffsets =
    {
        180f, -150f, 150f, -120f, 120f, -90f, 90f, -60f, 60f, -45f, 45f, -30f, 30f, 0f,
        165f, -165f, 135f, -135f, 105f, -105f, 75f, -75f, 15f, -15f
    };

    public bool IsCommandUnit => isCommandUnit;
    public bool CanReceiveCommands { get; set; } = true;
    /// <summary>True if this motor applied an XZ move during the last Update.</summary>
    public bool MovedHorizontallyThisFrame { get; private set; }

    public void SetIsCommandUnit(bool value)
    {
        isCommandUnit = value;
    }
    public float MoveSpeedMultiplier { get; set; } = 1f;
    public bool HasDestination => hasDestination;
    public bool HasActivePath => movementMode == MovementMode.Path && pathWaypoints.Count > 0;
    public bool HasActivePathForDisplay => HasActivePath;
    public Vector3 MoveDirection { get; private set; } = Vector3.forward;
    public bool IsBlockedBySolidObstacle { get; private set; }
    public bool IsStuck { get; private set; }

    /// <summary>
    /// While true, ignore overhead solids and soften cliff checks so units can walk under open gate roofs.
    /// </summary>
    public bool TraverseGateCorridor { get; set; }

    /// <summary>Fired when a drawn path is abandoned (wall block or no progress). PVP sends STOP.</summary>
    public event Action<RtsUnitMotor> PathFollowingAborted;

    private void Awake()
    {
        if (solidObstacleLayers == 0)
        {
            solidObstacleLayers = RtsGroundUtility.DefaultSolidMask;
        }

        ResolveMovementBoundsCollider();
        ownColliders = GetComponentsInChildren<Collider>(includeInactive: false);
    }

    public void RefreshOwnColliders()
    {
        ownColliders = GetComponentsInChildren<Collider>(includeInactive: false);
    }

    public void SetMovementBoundsCollider(Collider collider)
    {
        movementBoundsCollider = collider;
    }

    public void SetSolidBlockCollider(Collider collider)
    {
        solidBlockCollider = collider;
    }

    public void MoveTo(Vector3 worldPoint)
    {
        Vector3 flat = FlattenToGround(worldPoint);
        const float sameDestinationRadiusSqr = 0.4f * 0.4f;
        if (hasDestination
            && movementMode == MovementMode.Direct
            && HorizontalDistanceSqr(finalDestination, flat) <= sameDestinationRadiusSqr)
        {
            // Keep blocked/stuck/detour state — re-issuing the same order should not reset avoidance.
            finalDestination = flat;
            if (!usingDetour)
            {
                destination = flat;
            }

            return;
        }

        ClearPath();
        movementMode = MovementMode.Direct;
        destination = flat;
        finalDestination = destination;
        usingDetour = false;
        hasDestination = true;
        IsBlockedBySolidObstacle = false;
        IsStuck = false;
        pathStuckConfirmCount = 0;
        ResetStuckTracking();
    }

    /// <summary>True when the regiment footprint can reach worldPoint without crossing RTS_Solid.</summary>
    public bool IsDirectPathClear(Vector3 worldPoint)
    {
        return HasLineOfMovement(transform.position, FlattenToGround(worldPoint));
    }

    /// <summary>Insert a short lateral detour toward the current final destination. Returns false during drawn paths.</summary>
    public bool TryRequestDetour()
    {
        if (!hasDestination || IsFollowingPath())
        {
            return false;
        }

        Vector3 goalDirection = GetGoalDirection();
        if (goalDirection.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        if (!TryInsertDetourWaypoint(goalDirection))
        {
            return false;
        }

        IsBlockedBySolidObstacle = false;
        pathStuckConfirmCount = 0;
        return true;
    }

    /// <summary>True when the regiment footprint fits at worldPoint without overlapping RTS_Solid.</summary>
    public bool IsWorldPositionClear(Vector3 worldPoint)
    {
        return IsPositionClear(FlattenToGround(worldPoint));
    }

    /// <summary>
    /// Push away from an overlapping solid or step back from a wall ahead.
    /// Used by AI/retreat recovery when sliding/detours alone are not enough.
    /// </summary>
    public bool TryEscapeFromSolid()
    {
        bool wasFollowingPath = IsFollowingPath();
        bool restoreGoal = hasDestination || wasFollowingPath;
        Vector3 savedGoal = finalDestination;
        if (wasFollowingPath && pathWaypoints.Count > 0)
        {
            savedGoal = pathWaypoints[pathWaypoints.Count - 1];
        }

        if (wasFollowingPath)
        {
            ConvertPathToDirectMove(savedGoal);
        }

        float stepDistance = Mathf.Max(GetCurrentMoveSpeed() * Time.deltaTime, 0.08f);
        stepDistance = Mathf.Max(stepDistance, obstacleSkin + 0.15f);
        float escapeDistance = stepDistance * 2.5f;
        bool overlapping = IsCurrentlyOverlappingObstacle();
        if (overlapping)
        {
            escapeDistance = Mathf.Max(escapeDistance, detourWaypointSpacing * 0.85f);
        }

        if (TryGetOverlappingEscapeDirection(out Vector3 escapeDirection)
            && TryApplyEscapeDelta(escapeDirection * escapeDistance))
        {
            RestoreGoalAfterEscape(savedGoal, restoreGoal);
            return true;
        }

        Vector3 probeDirection = MoveDirection.sqrMagnitude > 0.0001f ? MoveDirection : GetGoalDirection();
        if (TryGetAheadSolidHit(probeDirection, obstacleLookaheadDistance, out RaycastHit aheadHit))
        {
            Vector3 wallNormal = aheadHit.normal;
            wallNormal.y = 0f;
            if (wallNormal.sqrMagnitude > 0.0001f)
            {
                wallNormal.Normalize();
                Vector3 backAway = wallNormal * escapeDistance;
                if (TryApplyEscapeDelta(backAway))
                {
                    RestoreGoalAfterEscape(savedGoal, restoreGoal);
                    return true;
                }

                Vector3 tangent = new Vector3(-wallNormal.z, 0f, wallNormal.x);
                Vector3[] slides = { tangent, -tangent, (tangent + wallNormal * 0.5f).normalized, (-tangent + wallNormal * 0.5f).normalized };
                for (int i = 0; i < slides.Length; i++)
                {
                    if (TryApplyEscapeDelta(slides[i] * escapeDistance))
                    {
                        RestoreGoalAfterEscape(savedGoal, restoreGoal);
                        return true;
                    }
                }
            }
        }

        if (TryRadialWallEscape(stepDistance, overlapping))
        {
            RestoreGoalAfterEscape(savedGoal, restoreGoal);
            return true;
        }

        if (restoreGoal)
        {
            RestoreGoalAfterEscape(savedGoal, restoreGoal);
        }

        return false;
    }

    private bool TryApplyEscapeDelta(Vector3 escapeDelta)
    {
        escapeDelta.y = 0f;
        if (escapeDelta.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        if (TryGetAllowedDelta(escapeDelta, out Vector3 allowedEscape) && allowedEscape.sqrMagnitude > 0.0001f)
        {
            transform.position += allowedEscape;
            MovedHorizontallyThisFrame = true;
            MoveDirection = allowedEscape.normalized;
            IsBlockedBySolidObstacle = false;
            IsStuck = false;
            pathStuckConfirmCount = 0;
            ResetStuckTracking();
            return true;
        }

        return false;
    }

    private bool TryRadialWallEscape(float stepDistance, bool overlapping)
    {
        Vector3 referenceDirection = GetGoalDirection();
        if (referenceDirection.sqrMagnitude < 0.0001f)
        {
            referenceDirection = MoveDirection.sqrMagnitude > 0.0001f ? MoveDirection : transform.forward;
        }

        referenceDirection.y = 0f;
        if (referenceDirection.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        referenceDirection.Normalize();
        float escapeDistance = Mathf.Max(stepDistance * 2.5f, detourWaypointSpacing * (overlapping ? 0.9f : 0.55f));
        int startIndex = radialEscapeAttemptIndex % RadialEscapeAngleOffsets.Length;
        radialEscapeAttemptIndex++;

        float bestScore = float.MinValue;
        Vector3 bestDelta = Vector3.zero;
        for (int attempt = 0; attempt < RadialEscapeAngleOffsets.Length; attempt++)
        {
            float angle = RadialEscapeAngleOffsets[(startIndex + attempt) % RadialEscapeAngleOffsets.Length];
            Vector3 candidateDirection = Quaternion.Euler(0f, angle, 0f) * referenceDirection;
            candidateDirection.y = 0f;
            if (candidateDirection.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            candidateDirection.Normalize();
            Vector3 candidateDelta = candidateDirection * escapeDistance;
            if (!TryGetAllowedDelta(candidateDelta, out Vector3 allowedDelta) || allowedDelta.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            Vector3 allowedDirection = allowedDelta.normalized;
            float clearanceScore = 1f;
            if (TryGetAheadSolidHit(allowedDirection, obstacleLookaheadDistance, out RaycastHit aheadHit))
            {
                clearanceScore = Mathf.Clamp01(aheadHit.distance / Mathf.Max(0.5f, obstacleLookaheadDistance));
            }

            float overlapBonus = 0f;
            if (overlapping && TryGetOverlappingEscapeDirection(out Vector3 overlapDirection))
            {
                overlapBonus = Mathf.Max(0f, Vector3.Dot(allowedDirection, overlapDirection)) * 2f;
            }

            float backtrackBonus = Mathf.Max(0f, -Vector3.Dot(allowedDirection, referenceDirection));
            float score = allowedDelta.magnitude + clearanceScore * 1.5f + overlapBonus + backtrackBonus;
            if (score > bestScore)
            {
                bestScore = score;
                bestDelta = allowedDelta;
            }
        }

        if (bestScore <= float.MinValue)
        {
            return false;
        }

        transform.position += bestDelta;
        MovedHorizontallyThisFrame = true;
        MoveDirection = bestDelta.normalized;
        IsBlockedBySolidObstacle = false;
        IsStuck = false;
        pathStuckConfirmCount = 0;
        ResetStuckTracking();
        return true;
    }

    private void ConvertPathToDirectMove(Vector3 goal)
    {
        ClearPath();
        movementMode = MovementMode.Direct;
        destination = goal;
        finalDestination = goal;
        hasDestination = true;
        usingDetour = false;
    }

    private void RestoreGoalAfterEscape(Vector3 goal, bool restoreGoal)
    {
        if (!restoreGoal)
        {
            return;
        }

        ConvertPathToDirectMove(goal);
    }

    public void FollowPath(IReadOnlyList<Vector3> waypoints)
    {
        ClearPath();
        if (waypoints == null || waypoints.Count == 0)
        {
            return;
        }

        List<Vector3> sanitized = RtsPathUtility.RemoveShortSegments(waypoints, pathMinSegmentLength);
        for (int i = 0; i < sanitized.Count; i++)
        {
            pathWaypoints.Add(FlattenToGround(sanitized[i]));
        }

        while (pathWaypoints.Count > 0
            && HorizontalDistanceSqr(transform.position, pathWaypoints[0]) <= GetWaypointPassRadius() * GetWaypointPassRadius())
        {
            pathWaypoints.RemoveAt(0);
        }

        if (pathWaypoints.Count == 0)
        {
            ClearMovement();
            return;
        }

        movementMode = MovementMode.Path;
        pathWaypointIndex = 0;
        destination = pathWaypoints[0];
        finalDestination = destination;
        usingDetour = false;
        hasDestination = true;
        IsBlockedBySolidObstacle = false;
        IsStuck = false;
        pathStuckConfirmCount = 0;
        ResetStuckTracking();
    }

    /// <summary>
    /// PVP peer view: do not simulate pathing locally — owner POSE stream is authoritative.
    /// </summary>
    public bool SuppressLocalSimulation { get; set; }

    /// <summary>
    /// Soft XZ correction toward the owning client's pose. Never applies rotation — regiment
    /// roots stay upright and TroopCombat owns facing. Does not clear an active path.
    /// </summary>
    public void ApplyNetworkPose(
        Vector3 worldPosition,
        Quaternion worldRotation,
        bool forceAuthority = false,
        float authoritySnapDistance = 1.5f)
    {
        Vector3 flat = FlattenToGround(worldPosition);
        Vector3 current = transform.position;
        float horizontalDeltaSqr = HorizontalDistanceSqr(current, flat);
        // Wider deadzone while idle so pose sync doesn't creep stopped units.
        float deadzone = hasDestination ? 0.3f : 0.75f;
        float deadzoneSqr = deadzone * deadzone;
        if (horizontalDeltaSqr < deadzoneSqr)
        {
            return;
        }

        float snapSqr = Mathf.Max(0.25f, authoritySnapDistance * authoritySnapDistance);
        Vector3 next = current;
        if (forceAuthority && horizontalDeltaSqr >= snapSqr)
        {
            next.x = flat.x;
            next.z = flat.z;
        }
        else
        {
            float blend = forceAuthority ? 0.55f : 0.35f;
            next.x = Mathf.Lerp(current.x, flat.x, blend);
            next.z = Mathf.Lerp(current.z, flat.z, blend);
        }

        // Network sync is XZ only — local TroopCombat owns ground Y.
        next.y = current.y;
        if (HorizontalDistanceSqr(current, next) > 0.0001f)
        {
            transform.position = next;
            MovedHorizontallyThisFrame = true;
        }

        // Unstick obstacle flags without overriding an explicit path halt (PVP peers mirror owner halt).
        IsBlockedBySolidObstacle = false;
        IsStuck = false;
        pathStuckConfirmCount = 0;
        ResetStuckTracking();
    }

    /// <summary>Hard snap to owner position (used when authority reports dead / large error).</summary>
    public void SnapNetworkPosition(Vector3 worldPosition)
    {
        Vector3 flat = FlattenToGround(worldPosition);
        Vector3 current = transform.position;
        flat.y = current.y;
        if (HorizontalDistanceSqr(current, flat) > 0.0001f)
        {
            transform.position = flat;
            MovedHorizontallyThisFrame = true;
        }
        IsBlockedBySolidObstacle = false;
        IsStuck = false;
        pathStuckConfirmCount = 0;
        ResetStuckTracking();
    }

    public void Stop()
    {
        ClearMovement();
    }

    public void GetActivePathPoints(List<Vector3> results)
    {
        results.Clear();
        if (!HasActivePath)
        {
            return;
        }

        results.Add(transform.position);
        for (int i = pathWaypointIndex; i < pathWaypoints.Count; i++)
        {
            results.Add(pathWaypoints[i]);
        }
    }

    private void Update()
    {
        MovedHorizontallyThisFrame = false;

        if (SuppressLocalSimulation)
        {
            IsBlockedBySolidObstacle = false;
            IsStuck = false;
            return;
        }

        if (!hasDestination)
        {
            IsBlockedBySolidObstacle = false;
            IsStuck = false;
            pathStuckConfirmCount = 0;
            wallEscapeGraceEndTime = 0f;
            return;
        }

        if (IsFollowingPath())
        {
            SkipDegeneratePathSegments();
        }

        UpdateStuckDetection();

        if (solidObstacleLayers != 0 && IsCurrentlyOverlappingObstacle())
        {
            overlapStuckFrames++;
            if (overlapStuckFrames >= 2 && Time.time >= nextForcedEscapeAttemptTime)
            {
                nextForcedEscapeAttemptTime = Time.time + 0.2f;
                if (TryEscapeFromSolid())
                {
                    overlapStuckFrames = 0;
                }
            }
        }
        else
        {
            overlapStuckFrames = 0;
        }

        if (IsFollowingPath() && TryAbortPathIfStuck())
        {
            return;
        }

        ConsumePassedPathWaypoints();

        Vector3 offset = GetMovementTarget() - transform.position;
        offset.y = 0f;

        if (usingDetour && offset.sqrMagnitude <= stoppingDistance * stoppingDistance)
        {
            ResumeAfterDetour();
            return;
        }

        if (offset.sqrMagnitude <= GetFinalArrivalDistanceSqr()
            && (!IsFollowingPath() || pathWaypointIndex >= pathWaypoints.Count - 1))
        {
            if (!TryAdvanceToNextPathWaypoint())
            {
                ClearMovement();
                return;
            }

            finalDestination = destination;
            usingDetour = false;
            ResetStuckTracking();
            offset = destination - transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude <= GetFinalArrivalDistanceSqr())
            {
                return;
            }
        }
        else if (offset.sqrMagnitude < 0.000001f)
        {
            if (IsFollowingPath() && TryAdvancePastDegenerateSegment())
            {
                offset = GetMovementTarget() - transform.position;
                offset.y = 0f;
                if (offset.sqrMagnitude < 0.000001f)
                {
                    AbortPathFollowing();
                    return;
                }
            }
            else
            {
                return;
            }
        }

        Vector3 direction = offset.normalized;
        if (direction.sqrMagnitude > 0.0001f)
        {
            MoveDirection = direction;
        }

        float stepDistance = moveSpeed * Mathf.Max(0f, MoveSpeedMultiplier) * Time.deltaTime;
        Vector3 goalDirection = GetGoalDirection();

        if (IsFollowingPath() && IsBlockedBySolidObstacle && !IsInWallEscapeGrace)
        {
            if (slideAlongWallOnPath && TrySlideAlongWall(direction, stepDistance))
            {
                return;
            }

            if (TryEscapeFromSolid())
            {
                return;
            }

            AbortPathFollowing();
            return;
        }

        bool isOverlapping = solidObstacleLayers != 0 && IsCurrentlyOverlappingObstacle();
        bool movingAwayFromWall = isOverlapping && IsMovingAwayFromObstacle(direction);
        bool useAvoidance = enableObstacleAvoidance && solidObstacleLayers != 0;

        if (isOverlapping)
        {
            if (movingAwayFromWall)
            {
                wallEscapeGraceEndTime = Time.time + wallEscapeGraceDuration;
            }
            else if (!IsInWallEscapeGrace)
            {
                if (IsFollowingPath() && slideAlongWallOnPath && TrySlideAlongWall(direction, stepDistance))
                {
                    return;
                }

                if (!useAvoidance || !TryMoveWithObstacleAvoidance(direction, isOverlapping))
                {
                    StopOnWall();
                }

                return;
            }
            else
            {
                wallEscapeGraceEndTime = 0f;
                if (!useAvoidance || !TryMoveWithObstacleAvoidance(direction, isOverlapping))
                {
                    StopOnWall();
                }

                return;
            }
        }
        else
        {
            wallEscapeGraceEndTime = 0f;
        }

        // Steer away from walls before contact so units don't march straight into solids.
        if (useAvoidance && !isOverlapping && direction.sqrMagnitude > 0.0001f)
        {
            direction = GetObstacleSteeredDirection(direction, goalDirection);
            MoveDirection = direction;
        }

        float distanceToTarget = offset.magnitude;
        bool slowForFinalArrival = !IsFollowingPath() || pathWaypointIndex >= pathWaypoints.Count - 1;
        if (slowForFinalArrival
            && distanceToTarget <= arrivalSlowdownRadius
            && arrivalSlowdownRadius > 0.01f)
        {
            float slowdown = Mathf.Clamp01(distanceToTarget / arrivalSlowdownRadius);
            stepDistance *= Mathf.Max(0.12f, slowdown);
        }

        Vector3 desiredDelta = direction * stepDistance;
        bool useWallEscapeGrace = isOverlapping && movingAwayFromWall && IsInWallEscapeGrace;

        if (useWallEscapeGrace)
        {
            if (TryGetAllowedDelta(desiredDelta, out Vector3 escapeDelta) && escapeDelta.sqrMagnitude > 0.0001f)
            {
                transform.position += escapeDelta;
                MovedHorizontallyThisFrame = true;
                IsBlockedBySolidObstacle = false;
            }
            else if (!useAvoidance || !TryMoveWithObstacleAvoidance(direction, isOverlapping))
            {
                StopOnWall();
            }
        }
        else if (!TryGetAllowedDelta(desiredDelta, out Vector3 allowedDelta))
        {
            if (slideAlongWallOnPath && IsFollowingPath() && TrySlideAlongWall(direction, stepDistance))
            {
                return;
            }

            if (slideAlongWallOnDirectMove && !IsFollowingPath() && TrySlideAlongWall(direction, stepDistance))
            {
                return;
            }

            if (!useAvoidance || !TryMoveWithObstacleAvoidance(direction, isOverlapping))
            {
                StopOnWall();
            }

            return;
        }
        else
        {
            if (allowedDelta.sqrMagnitude > 0f)
            {
                Vector3 previousPosition = transform.position;
                transform.position += allowedDelta;
                if (GetSolidQueryMask() != 0 && IsCurrentlyOverlappingObstacle())
                {
                    // Tunneled into a thin solid — revert the step.
                    transform.position = previousPosition;
                    if (!useAvoidance || !TryMoveWithObstacleAvoidance(direction, isOverlapping: true))
                    {
                        StopOnWall();
                    }

                    return;
                }

                MovedHorizontallyThisFrame = true;
            }

            IsBlockedBySolidObstacle = false;
            // Do not reset stuck tracking here — wall slides / micro-moves must still
            // count as stuck when they fail to advance toward the goal.
        }

        Vector3 remaining = destination - transform.position;
        remaining.y = 0f;
        if (remaining.sqrMagnitude <= GetFinalArrivalDistanceSqr()
            && (!IsFollowingPath() || pathWaypointIndex >= pathWaypoints.Count - 1))
        {
            if (usingDetour)
            {
                ResumeAfterDetour();
                return;
            }

            if (!TryAdvanceToNextPathWaypoint())
            {
                ClearMovement();
            }
            else
            {
                finalDestination = destination;
                usingDetour = false;
                ResetStuckTracking();
            }
        }
    }

    private float GetFinalArrivalDistance()
    {
        return Mathf.Max(stoppingDistance, arrivalRadius);
    }

    private float GetFinalArrivalDistanceSqr()
    {
        float distance = GetFinalArrivalDistance();
        return distance * distance;
    }

    private bool IsFollowingPath()
    {
        return movementMode == MovementMode.Path && pathWaypoints.Count > 0;
    }

    private float GetCurrentMoveSpeed()
    {
        return moveSpeed * Mathf.Max(0f, MoveSpeedMultiplier);
    }

    private float GetWaypointPassRadius()
    {
        float radius = waypointPassRadius;
        if (scaleWaypointPassRadiusWithSpeed)
        {
            radius = Mathf.Max(radius, GetCurrentMoveSpeed() * waypointPassRadiusPerSpeed);
        }

        return radius;
    }

    private Vector3 GetMovementTarget()
    {
        if (!IsFollowingPath() || pathWaypointIndex >= pathWaypoints.Count - 1)
        {
            return destination;
        }

        if (HasSharpUpcomingTurn(pathWaypointIndex, 90f))
        {
            return pathWaypoints[pathWaypointIndex];
        }

        Vector3 currentWaypoint = pathWaypoints[pathWaypointIndex];
        Vector3 nextWaypoint = pathWaypoints[pathWaypointIndex + 1];
        Vector3 segment = nextWaypoint - currentWaypoint;
        segment.y = 0f;

        float segmentLength = segment.magnitude;
        if (segmentLength < pathMinSegmentLength)
        {
            return nextWaypoint;
        }

        Vector3 segmentDirection = segment / segmentLength;
        Vector3 fromCurrentWaypoint = transform.position - currentWaypoint;
        fromCurrentWaypoint.y = 0f;
        float traveledAlongSegment = Vector3.Dot(fromCurrentWaypoint, segmentDirection);
        float lookahead = Mathf.Max(GetWaypointPassRadius() * 1.5f, GetCurrentMoveSpeed() * 0.35f);
        float targetDistance = Mathf.Clamp(traveledAlongSegment + lookahead, 0f, segmentLength);
        return currentWaypoint + segmentDirection * targetDistance;
    }

    private bool HasSharpUpcomingTurn(int waypointIndex, float sharpAngleDegrees)
    {
        if (waypointIndex <= 0 || waypointIndex >= pathWaypoints.Count - 1)
        {
            return false;
        }

        Vector3 previous = pathWaypoints[waypointIndex - 1];
        Vector3 corner = pathWaypoints[waypointIndex];
        Vector3 next = pathWaypoints[waypointIndex + 1];
        Vector3 incoming = corner - previous;
        Vector3 outgoing = next - corner;
        incoming.y = 0f;
        outgoing.y = 0f;
        if (incoming.sqrMagnitude < 0.0001f || outgoing.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        return Vector3.Angle(incoming.normalized, outgoing.normalized) > sharpAngleDegrees;
    }

    private void ConsumePassedPathWaypoints()
    {
        if (!IsFollowingPath() || usingDetour)
        {
            return;
        }

        float passRadiusSqr = GetWaypointPassRadius() * GetWaypointPassRadius();

        while (pathWaypointIndex < pathWaypoints.Count - 1)
        {
            Vector3 currentWaypoint = pathWaypoints[pathWaypointIndex];
            if (HorizontalDistanceSqr(transform.position, currentWaypoint) > passRadiusSqr)
            {
                break;
            }

            Vector3 nextWaypoint = pathWaypoints[pathWaypointIndex + 1];
            Vector3 segment = nextWaypoint - currentWaypoint;
            Vector3 fromWaypoint = transform.position - currentWaypoint;
            segment.y = 0f;
            fromWaypoint.y = 0f;

            if (segment.sqrMagnitude > 0.0001f
                && fromWaypoint.sqrMagnitude > 0.0001f
                && Vector3.Dot(fromWaypoint, segment) < 0f)
            {
                break;
            }

            pathWaypointIndex++;
            destination = pathWaypoints[pathWaypointIndex];
            finalDestination = destination;
        }
    }

    private bool TryMoveWithObstacleAvoidance(Vector3 desiredDirection, bool isOverlapping)
    {
        float stepDistance = moveSpeed * Mathf.Max(0f, MoveSpeedMultiplier) * Time.deltaTime;
        Vector3 goalDirection = GetGoalDirection();

        if (isOverlapping && TryGetOverlappingEscapeDirection(out Vector3 escapeDirection))
        {
            Vector3 escapeDelta = escapeDirection * stepDistance;
            if (TryGetAllowedDelta(escapeDelta, out Vector3 allowedEscape) && allowedEscape.sqrMagnitude > 0.0001f)
            {
                transform.position += allowedEscape;
                MovedHorizontallyThisFrame = true;
                MoveDirection = allowedEscape.normalized;
                IsBlockedBySolidObstacle = false;
                return true;
            }
        }

        if (TryGetBestAvoidanceDelta(desiredDirection, goalDirection, stepDistance, out Vector3 avoidanceDelta))
        {
            transform.position += avoidanceDelta;
            MovedHorizontallyThisFrame = true;
            MoveDirection = avoidanceDelta.normalized;
            IsBlockedBySolidObstacle = false;
            return true;
        }

        // Detours are local-only and diverge from the synced drawn path — allow when deeply stuck in solids.
        bool allowDetourDuringPath = IsFollowingPath()
            && pathStuckConfirmCount >= 2
            && (IsBlockedBySolidObstacle || IsCurrentlyOverlappingObstacle());
        if ((!IsFollowingPath() || allowDetourDuringPath)
            && Time.time >= nextDetourAttemptTime
            && (IsBlockedBySolidObstacle || IsStuck)
            && TryInsertDetourWaypoint(goalDirection))
        {
            nextDetourAttemptTime = Time.time + stuckDetectionTime * 0.5f;
            IsBlockedBySolidObstacle = false;
            return true;
        }

        IsBlockedBySolidObstacle = true;
        return false;
    }

    private Vector3 GetObstacleSteeredDirection(Vector3 desiredDirection, Vector3 goalDirection)
    {
        desiredDirection.y = 0f;
        if (desiredDirection.sqrMagnitude < 0.0001f)
        {
            return desiredDirection;
        }

        desiredDirection.Normalize();
        if (!TryGetAheadSolidHit(desiredDirection, obstacleLookaheadDistance, out RaycastHit aheadHit))
        {
            return desiredDirection;
        }

        Vector3 wallNormal = aheadHit.normal;
        wallNormal.y = 0f;
        if (wallNormal.sqrMagnitude < 0.0001f)
        {
            return desiredDirection;
        }

        wallNormal.Normalize();
        Vector3 tangentA = new Vector3(-wallNormal.z, 0f, wallNormal.x);
        Vector3 tangentB = -tangentA;
        Vector3 goal = goalDirection.sqrMagnitude > 0.0001f ? goalDirection.normalized : desiredDirection;
        Vector3 tangent = Vector3.Dot(tangentA, goal) >= Vector3.Dot(tangentB, goal) ? tangentA : tangentB;

        float proximity = 1f - Mathf.Clamp01(aheadHit.distance / Mathf.Max(0.5f, obstacleLookaheadDistance));
        float steerWeight = Mathf.Clamp01(proximity * proximity + proximity * 0.35f);
        // When nearly touching the wall, commit to sliding along it instead of grazing into it.
        Vector3 steered = proximity > 0.7f
            ? tangent
            : Vector3.Slerp(desiredDirection, tangent, steerWeight * 0.95f);
        steered.y = 0f;
        return steered.sqrMagnitude > 0.0001f ? steered.normalized : desiredDirection;
    }

    private bool TryGetAheadSolidHit(Vector3 direction, float maxDistance, out RaycastHit bestHit)
    {
        bestHit = default;
        LayerMask mask = GetSolidQueryMask();
        if (mask == 0 || maxDistance < 0.05f)
        {
            return false;
        }

        GetSolidBlockCastShape(out Vector3 castCenter, out Vector3 halfExtents);
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        direction.Normalize();
        RaycastHit[] hits = Physics.BoxCastAll(
            castCenter,
            halfExtents,
            direction,
            Quaternion.identity,
            maxDistance,
            mask,
            QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
        {
            return false;
        }

        float nearest = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || IsOwnCollider(hit.collider))
            {
                continue;
            }

            if (!IsSolidHitBlockingHorizontalMove(hit, castCenter))
            {
                continue;
            }

            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                bestHit = hit;
                found = true;
            }
        }

        return found;
    }

    private bool TrySlideAlongWall(Vector3 desiredDirection, float stepDistance)
    {
        if (!TryGetSlideWallNormal(desiredDirection, out Vector3 wallNormal))
        {
            return false;
        }

        Vector3 goalDirection = GetGoalDirection();
        Vector3 tangent = Vector3.ProjectOnPlane(goalDirection.sqrMagnitude > 0.0001f ? goalDirection : desiredDirection, wallNormal);
        tangent.y = 0f;
        if (tangent.sqrMagnitude < 0.0001f)
        {
            tangent = new Vector3(-wallNormal.z, 0f, wallNormal.x);
        }

        tangent.Normalize();
        Vector3[] slideDirections =
        {
            tangent,
            -tangent,
            (tangent + wallNormal * 0.35f).normalized,
            (-tangent + wallNormal * 0.35f).normalized
        };

        for (int i = 0; i < slideDirections.Length; i++)
        {
            Vector3 slideDelta = slideDirections[i] * stepDistance;
            if (!TryGetAllowedDelta(slideDelta, out Vector3 allowedSlide) || allowedSlide.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            transform.position += allowedSlide;
            MovedHorizontallyThisFrame = true;
            MoveDirection = allowedSlide.normalized;
            IsBlockedBySolidObstacle = false;
            return true;
        }

        return false;
    }

    private bool TryGetSlideWallNormal(Vector3 preferredDirection, out Vector3 wallNormal)
    {
        wallNormal = Vector3.zero;
        if (TryGetOverlappingEscapeDirection(out Vector3 escapeDirection))
        {
            wallNormal = escapeDirection;
            return true;
        }

        preferredDirection.y = 0f;
        Vector3 probeDirection = preferredDirection.sqrMagnitude > 0.0001f
            ? preferredDirection.normalized
            : (MoveDirection.sqrMagnitude > 0.0001f ? MoveDirection : GetGoalDirection());
        if (probeDirection.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        if (!TryGetAheadSolidHit(probeDirection, obstacleLookaheadDistance, out RaycastHit aheadHit))
        {
            return false;
        }

        wallNormal = aheadHit.normal;
        wallNormal.y = 0f;
        if (wallNormal.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        wallNormal.Normalize();
        return true;
    }

    private bool TryGetBestAvoidanceDelta(
        Vector3 desiredDirection,
        Vector3 goalDirection,
        float stepDistance,
        out Vector3 bestDelta)
    {
        bestDelta = Vector3.zero;
        float bestScore = float.MinValue;

        for (int i = 0; i < AvoidanceAngleOffsets.Length; i++)
        {
            Vector3 candidateDirection = Quaternion.Euler(0f, AvoidanceAngleOffsets[i], 0f) * desiredDirection;
            candidateDirection.y = 0f;
            if (candidateDirection.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            candidateDirection.Normalize();
            Vector3 candidateDelta = candidateDirection * stepDistance;
            if (!TryGetAllowedDelta(candidateDelta, out Vector3 allowedDelta) || allowedDelta.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            Vector3 allowedDirection = allowedDelta.normalized;
            float progressScore = Mathf.Max(0f, Vector3.Dot(allowedDirection, goalDirection));
            float distanceScore = allowedDelta.magnitude / Mathf.Max(stepDistance, 0.0001f);
            float clearanceScore = 0f;
            if (TryGetAheadSolidHit(allowedDirection, obstacleLookaheadDistance, out RaycastHit aheadHit))
            {
                clearanceScore = Mathf.Clamp01(aheadHit.distance / Mathf.Max(0.5f, obstacleLookaheadDistance));
            }
            else
            {
                clearanceScore = 1f;
            }

            float score = progressScore * 2f + distanceScore + clearanceScore * 1.5f;
            if (score > bestScore)
            {
                bestScore = score;
                bestDelta = allowedDelta;
            }
        }

        return bestScore > float.MinValue;
    }

    private bool TryInsertDetourWaypoint(Vector3 goalDirection)
    {
        if (goalDirection.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        goalDirection.Normalize();
        Vector3 tangent = new Vector3(-goalDirection.z, 0f, goalDirection.x);
        bool needsEscape = IsBlockedBySolidObstacle || IsStuck || IsCurrentlyOverlappingObstacle();
        float[] sideMultipliers = { 0.75f, -0.75f, 1.5f, -1.5f, 2.25f, -2.25f, 3f, -3f, 4f, -4f };
        float[] forwardMultipliers = needsEscape
            ? new[] { -0.6f, -0.25f, 0f, 0.35f, 0.65f, 1f, 1.4f }
            : new[] { 0.65f, 1f, 1.4f, 1.8f };
        Vector3 goal = usingDetour ? finalDestination : destination;
        float currentGoalDistance = HorizontalDistanceSqr(transform.position, goal);
        Vector3 wallNormal = Vector3.zero;
        bool hasWallNormal = TryGetSlideWallNormal(goalDirection, out wallNormal);
        Vector3 bestDetour = Vector3.zero;
        float bestScore = float.MinValue;
        float minProgress = needsEscape
            ? -detourProbeDistance * detourProbeDistance * 4f
            : -0.5f;

        for (int f = 0; f < forwardMultipliers.Length; f++)
        {
            float forwardDistance = detourWaypointSpacing * forwardMultipliers[f];
            for (int i = 0; i < sideMultipliers.Length; i++)
            {
                Vector3 detourPoint = transform.position
                    + tangent * (detourProbeDistance * sideMultipliers[i])
                    + goalDirection * forwardDistance;
                if (needsEscape && hasWallNormal && forwardMultipliers[f] <= 0f)
                {
                    detourPoint += wallNormal * (detourWaypointSpacing * 0.75f);
                }

                detourPoint = FlattenToGround(detourPoint);

                if (!IsPositionClear(detourPoint))
                {
                    continue;
                }

                if (!HasLineOfMovement(transform.position, detourPoint))
                {
                    continue;
                }

                float goalDistance = HorizontalDistanceSqr(detourPoint, goal);
                float progressScore = currentGoalDistance - goalDistance;
                if (progressScore < minProgress)
                {
                    continue;
                }

                float clearanceScore = 0f;
                Vector3 detourDirection = detourPoint - transform.position;
                detourDirection.y = 0f;
                if (detourDirection.sqrMagnitude > 0.0001f)
                {
                    detourDirection.Normalize();
                    if (TryGetAheadSolidHit(detourDirection, obstacleLookaheadDistance, out RaycastHit aheadHit))
                    {
                        clearanceScore = Mathf.Clamp01(aheadHit.distance / Mathf.Max(0.5f, obstacleLookaheadDistance));
                    }
                    else
                    {
                        clearanceScore = 1f;
                    }
                }

                float escapeBonus = 0f;
                if (needsEscape && hasWallNormal && detourDirection.sqrMagnitude > 0.0001f)
                {
                    escapeBonus = Mathf.Max(0f, Vector3.Dot(detourDirection, wallNormal)) * 2f;
                }

                float finalLegBonus = HasLineOfMovement(detourPoint, goal) ? 1f : 0f;
                float lateralPenalty = Mathf.Abs(sideMultipliers[i]) * 0.08f;
                float score = progressScore * 2f + clearanceScore * 1.5f + finalLegBonus + escapeBonus - lateralPenalty;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestDetour = detourPoint;
                }
            }
        }

        if (bestScore <= float.MinValue)
        {
            return false;
        }

        finalDestination = usingDetour ? finalDestination : destination;
        destination = bestDetour;
        usingDetour = true;
        IsStuck = false;
        ResetStuckTracking();
        return true;
    }

    private void ResumeAfterDetour()
    {
        usingDetour = false;
        destination = finalDestination;
        // Don't march straight back into the same solid after a successful side-step.
        if (!HasLineOfMovement(transform.position, finalDestination)
            && TryInsertDetourWaypoint(GetGoalDirection()))
        {
            return;
        }

        IsStuck = false;
        ResetStuckTracking();
    }

    private Vector3 GetGoalDirection()
    {
        Vector3 offset = finalDestination - transform.position;
        offset.y = 0f;
        if (offset.sqrMagnitude < 0.0001f)
        {
            return MoveDirection;
        }

        return offset.normalized;
    }

    private void UpdateStuckDetection()
    {
        if (Time.time < stuckSampleTime + stuckDetectionTime)
        {
            return;
        }

        Vector3 goal = usingDetour ? destination : finalDestination;
        float goalDistance = Mathf.Sqrt(HorizontalDistanceSqr(transform.position, goal));
        float progressThreshold = GetStuckProgressThreshold();
        float goalProgress = stuckSampleGoalDistance - goalDistance;

        // Near arrival, slowdown makes tiny steps look like stuck — ignore that case.
        // Sliding along a wall without closing on the goal still counts as stuck.
        float arrivalGrace = Mathf.Max(arrivalRadius * 2f, arrivalSlowdownRadius);
        bool nearArrival = !usingDetour && goalDistance <= arrivalGrace;
        bool stuckThisSample = !nearArrival && goalProgress < progressThreshold * 0.5f;
        IsStuck = stuckThisSample;
        if (stuckThisSample)
        {
            pathStuckConfirmCount++;
        }
        else
        {
            pathStuckConfirmCount = 0;
        }

        stuckSamplePosition = transform.position;
        stuckSampleGoalDistance = goalDistance;
        stuckSampleTime = Time.time;
    }

    private float GetStuckProgressThreshold()
    {
        float speedScaledDistance = GetCurrentMoveSpeed() * stuckDetectionTime * stuckProgressSpeedFactor;
        float threshold = Mathf.Max(stuckProgressDistance, speedScaledDistance);
        if (IsFollowingPath())
        {
            threshold = Mathf.Max(threshold, stuckProgressDistance * 2f);
        }

        return threshold;
    }

    private void ResetStuckTracking()
    {
        stuckSamplePosition = transform.position;
        Vector3 goal = usingDetour ? destination : finalDestination;
        stuckSampleGoalDistance = hasDestination
            ? Mathf.Sqrt(HorizontalDistanceSqr(transform.position, goal))
            : 0f;
        stuckSampleTime = Time.time;
        IsStuck = false;
        pathStuckConfirmCount = 0;
    }

    private void SkipDegeneratePathSegments()
    {
        float minSegmentSqr = pathMinSegmentLength * pathMinSegmentLength;
        while (pathWaypointIndex < pathWaypoints.Count - 1)
        {
            Vector3 current = pathWaypoints[pathWaypointIndex];
            Vector3 next = pathWaypoints[pathWaypointIndex + 1];
            if (HorizontalDistanceSqr(current, next) >= minSegmentSqr)
            {
                break;
            }

            pathWaypointIndex++;
            destination = pathWaypoints[pathWaypointIndex];
            finalDestination = destination;
        }
    }

    private bool TryAdvancePastDegenerateSegment()
    {
        if (!IsFollowingPath() || pathWaypointIndex >= pathWaypoints.Count - 1)
        {
            return false;
        }

        SkipDegeneratePathSegments();
        return pathWaypointIndex < pathWaypoints.Count - 1;
    }

    private bool TryAbortPathIfStuck()
    {
        bool overlapping = solidObstacleLayers != 0 && IsCurrentlyOverlappingObstacle();
        if (overlapping || (IsBlockedBySolidObstacle && pathStuckConfirmCount >= 1))
        {
            Vector3 goal = pathWaypoints.Count > 0 ? pathWaypoints[pathWaypoints.Count - 1] : finalDestination;
            AbortPathFollowing();
            ConvertPathToDirectMove(goal);
            TryEscapeFromSolid();
            return true;
        }

        if (pathStuckConfirmCount < 2)
        {
            return false;
        }

        AbortPathFollowing();
        return true;
    }

    private void AbortPathFollowing()
    {
        if (!HasActivePath && !hasDestination)
        {
            return;
        }

        ClearMovement();
        PathFollowingAborted?.Invoke(this);
    }

    private bool IsInWallEscapeGrace => wallEscapeGraceDuration > 0f && Time.time < wallEscapeGraceEndTime;

    private void StopOnWall()
    {
        if (IsFollowingPath())
        {
            if (slideAlongWallOnPath)
            {
                float stepDistance = GetCurrentMoveSpeed() * Time.deltaTime;
                Vector3 slideDirection = GetGoalDirection();
                if (slideDirection.sqrMagnitude < 0.0001f)
                {
                    slideDirection = MoveDirection;
                }

                if (TrySlideAlongWall(slideDirection, stepDistance))
                {
                    IsBlockedBySolidObstacle = false;
                    return;
                }
            }

            if (TryEscapeFromSolid())
            {
                return;
            }

            AbortPathFollowing();
            if (TryEscapeFromSolid())
            {
                return;
            }

            return;
        }

        if (slideAlongWallOnDirectMove)
        {
            float stepDistance = GetCurrentMoveSpeed() * Time.deltaTime;
            Vector3 slideDirection = GetGoalDirection();
            if (slideDirection.sqrMagnitude < 0.0001f)
            {
                slideDirection = MoveDirection;
            }

            if (TrySlideAlongWall(slideDirection, stepDistance))
            {
                IsBlockedBySolidObstacle = false;
                return;
            }
        }

        if (TryEscapeFromSolid())
        {
            return;
        }

        if (!IsFollowingPath()
            && Time.time >= nextDetourAttemptTime
            && TryInsertDetourWaypoint(GetGoalDirection()))
        {
            nextDetourAttemptTime = Time.time + stuckDetectionTime * 0.5f;
            IsBlockedBySolidObstacle = false;
            return;
        }

        bool shouldClearMovement = stopImmediatelyOnWallHit && !enableObstacleAvoidance;
        if (shouldClearMovement)
        {
            ClearMovement();
        }

        IsBlockedBySolidObstacle = true;
    }

    private bool IsPositionClear(Vector3 worldPosition)
    {
        GetMovementCastShape(out Vector3 castCenter, out Vector3 halfExtents);
        Vector3 offset = worldPosition - transform.position;
        castCenter += new Vector3(offset.x, 0f, offset.z);
        return !IsOverlappingSolidObstacle(castCenter, halfExtents);
    }

    private bool HasLineOfMovement(Vector3 from, Vector3 to)
    {
        Vector3 offset = to - from;
        offset.y = 0f;
        float distance = offset.magnitude;
        if (distance < 0.05f)
        {
            return true;
        }

        GetMovementCastShape(out Vector3 castCenter, out Vector3 halfExtents);
        castCenter = from + Vector3.up * (castCenter.y - transform.position.y);
        float blockedDistance = GetNearestObstacleDistance(castCenter, halfExtents, offset / distance, distance);
        return blockedDistance >= distance - 0.05f;
    }

    private bool TryAdvanceToNextPathWaypoint()
    {
        if (movementMode != MovementMode.Path)
        {
            return false;
        }

        pathWaypointIndex++;
        if (pathWaypointIndex >= pathWaypoints.Count)
        {
            return false;
        }

        destination = pathWaypoints[pathWaypointIndex];
        finalDestination = destination;
        return true;
    }

    private void ClearPath()
    {
        pathWaypoints.Clear();
        pathWaypointIndex = 0;
    }

    private void ClearMovement()
    {
        movementMode = MovementMode.None;
        hasDestination = false;
        usingDetour = false;
        ClearPath();
        IsBlockedBySolidObstacle = false;
        IsStuck = false;
        wallEscapeGraceEndTime = 0f;
        pathStuckConfirmCount = 0;
        overlapStuckFrames = 0;
        ResetStuckTracking();
    }

    private Vector3 FlattenToGround(Vector3 worldPoint)
    {
        // Logical movement plane only — elevation is ignored for pathing/speed.
        // TroopCombat snaps root Y onto ground separately.
        return new Vector3(worldPoint.x, transform.position.y, worldPoint.z);
    }

    private static float HorizontalDistanceSqr(Vector3 a, Vector3 b)
    {
        Vector3 offset = a - b;
        offset.y = 0f;
        return offset.sqrMagnitude;
    }

    private bool TryGetAllowedDelta(Vector3 desiredDelta, out Vector3 allowedDelta)
    {
        allowedDelta = Vector3.zero;
        desiredDelta.y = 0f;
        if (desiredDelta.sqrMagnitude < 0.000001f)
        {
            allowedDelta = desiredDelta;
            return true;
        }

        Vector3 direction = desiredDelta.normalized;
        float distance = desiredDelta.magnitude;

        // Binary-ish shrink: full step, then half, then quarter if formation/solid rejects.
        float[] fractions = { 1f, 0.5f, 0.25f, 0.1f };
        for (int i = 0; i < fractions.Length; i++)
        {
            float step = distance * fractions[i];
            Vector3 candidateDelta = direction * step;
            Vector3 nextPosition = transform.position + candidateDelta;
            if (!IsFormationMoveBlocked(nextPosition, candidateDelta))
            {
                allowedDelta = candidateDelta;
                return allowedDelta.sqrMagnitude > 0.000001f;
            }
        }

        return false;
    }

    /// <summary>
    /// Blocks moves where the full footprint hits RTS_Solid or the formation would span a cliff ledge.
    /// Gentle slopes are allowed; only steep discontinuities count as ledges.
    /// </summary>
    private bool IsFormationMoveBlocked(Vector3 nextPosition, Vector3 moveDelta)
    {
        GetSolidBlockCastShape(out Vector3 castCenter, out Vector3 halfExtents);
        Vector3 nextCenter = new Vector3(nextPosition.x, castCenter.y, nextPosition.z);

        if (GetSolidQueryMask() != 0)
        {
            float castDistance = moveDelta.magnitude;
            if (castDistance > 0.0001f)
            {
                Vector3 direction = moveDelta.normalized;
                float step = Mathf.Max(0.05f, maxSolidCastStep);
                float traveled = 0f;
                while (traveled < castDistance - 0.0001f)
                {
                    float segment = Mathf.Min(step, castDistance - traveled);
                    Vector3 segmentOrigin = castCenter + direction * traveled;
                    float blockedAt = GetNearestObstacleDistance(
                        segmentOrigin,
                        halfExtents,
                        direction,
                        segment);
                    if (blockedAt < segment - 0.001f)
                    {
                        return true;
                    }

                    traveled += segment;
                }

                // Thin walls can slip between boxcast samples — also probe with thin rays.
                if (HasThinSolidBlockingRay(castCenter, halfExtents, direction, castDistance))
                {
                    return true;
                }
            }

            if (IsOverlappingSolidObstacle(nextCenter, halfExtents))
            {
                return true;
            }
        }

        return !IsFormationGroundWalkable(nextPosition, halfExtents);
    }

    private bool IsFormationGroundWalkable(Vector3 nextPosition, Vector3 halfExtents)
    {
        // Use a compact probe radius — solidThicknessPadding is for wall hits only and made
        // slope probes falsely fail across gentle hills with a wide formation.
        float probeRadiusX = Mathf.Max(0.2f, Mathf.Min(halfExtents.x, fallbackCastHalfExtents.x));
        float probeRadiusZ = Mathf.Max(0.2f, Mathf.Min(halfExtents.z, fallbackCastHalfExtents.z));
        Vector3[] probes =
        {
            Vector3.zero,
            new Vector3(probeRadiusX, 0f, probeRadiusZ),
            new Vector3(-probeRadiusX, 0f, probeRadiusZ),
            new Vector3(probeRadiusX, 0f, -probeRadiusZ),
            new Vector3(-probeRadiusX, 0f, -probeRadiusZ)
        };

        bool hasCenter = false;
        float centerY = nextPosition.y;
        // Cliffs only. Gentle hills must not freeze movement — TroopCombat owns Y snap.
        float cliffStep = Mathf.Max(1.5f, maxStepHeight);
        float cliffNormalAngle = Mathf.Clamp(Mathf.Max(maxGroundNormalAngleDegrees, 55f), 45f, 85f);
        if (TraverseGateCorridor)
        {
            cliffStep = Mathf.Max(cliffStep, 3f);
            cliffNormalAngle = Mathf.Max(cliffNormalAngle, 70f);
        }

        if (!RtsGroundUtility.TrySampleGround(
                nextPosition.x,
                nextPosition.z,
                RtsGroundUtility.DefaultGroundMask,
                256f,
                0f,
                out float nextCenterGroundY,
                out Vector3 nextCenterNormal,
                preferredY: nextPosition.y,
                maxVerticalSnap: 16f))
        {
            // Missing ground under center — allow move (mesh gaps) rather than freeze.
            return true;
        }

        hasCenter = true;
        centerY = nextCenterGroundY;

        if (Vector3.Angle(nextCenterNormal, Vector3.up) > cliffNormalAngle)
        {
            return false;
        }

        if (RtsGroundUtility.TrySampleGround(
                transform.position.x,
                transform.position.z,
                RtsGroundUtility.DefaultGroundMask,
                256f,
                0f,
                out float currentGroundY,
                out _,
                preferredY: transform.position.y,
                maxVerticalSnap: 16f))
        {
            // Only block a true ledge climb — descending is always fine.
            if (nextCenterGroundY - currentGroundY > cliffStep)
            {
                return false;
            }
        }

        // Corner checks: only reject when a corner sits on a near-vertical cliff face or a
        // much higher ledge than the formation center. Mild slopes across the footprint are OK.
        for (int i = 1; i < probes.Length; i++)
        {
            float x = nextPosition.x + probes[i].x;
            float z = nextPosition.z + probes[i].z;
            if (!RtsGroundUtility.TrySampleGround(
                    x,
                    z,
                    RtsGroundUtility.DefaultGroundMask,
                    256f,
                    0f,
                    out float groundY,
                    out Vector3 groundNormal,
                    preferredY: centerY,
                    maxVerticalSnap: 16f))
            {
                continue;
            }

            if (Vector3.Angle(groundNormal, Vector3.up) > cliffNormalAngle)
            {
                return false;
            }

            if (groundY - centerY > cliffStep)
            {
                return false;
            }
        }

        return hasCenter;
    }

    private bool IsCurrentlyOverlappingObstacle()
    {
        GetSolidBlockCastShape(out Vector3 castCenter, out Vector3 halfExtents);
        return IsOverlappingSolidObstacle(castCenter, halfExtents);
    }

    private bool IsMovingAwayFromObstacle(Vector3 moveDirection)
    {
        if (!TryGetOverlappingEscapeDirection(out Vector3 escapeDirection))
        {
            return false;
        }

        moveDirection.y = 0f;
        if (moveDirection.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        return Vector3.Dot(moveDirection.normalized, escapeDirection) >= wallEscapeDirectionThreshold;
    }

    private bool TryGetOverlappingEscapeDirection(out Vector3 escapeDirection)
    {
        escapeDirection = Vector3.zero;
        GetSolidBlockCastShape(out Vector3 castCenter, out Vector3 halfExtents);

        Collider[] overlaps = Physics.OverlapBox(
            castCenter,
            halfExtents,
            Quaternion.identity,
            GetSolidQueryMask(),
            QueryTriggerInteraction.Ignore);

        if (overlaps == null || overlaps.Length == 0)
        {
            return false;
        }

        Vector3 accumulatedEscape = Vector3.zero;

        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider overlap = overlaps[i];
            if (overlap == null || IsOwnCollider(overlap))
            {
                continue;
            }

            if (!IsSolidColliderBlockingHorizontalMove(overlap, castCenter))
            {
                continue;
            }

            if (movementBoundsCollider != null
                && Physics.ComputePenetration(
                    movementBoundsCollider,
                    movementBoundsCollider.transform.position,
                    movementBoundsCollider.transform.rotation,
                    overlap,
                    overlap.transform.position,
                    overlap.transform.rotation,
                    out Vector3 separationDirection,
                    out float separationDistance))
            {
                Vector3 flatSeparation = separationDirection;
                flatSeparation.y = 0f;
                if (flatSeparation.sqrMagnitude > 0.0001f)
                {
                    accumulatedEscape += flatSeparation.normalized * separationDistance;
                    continue;
                }
            }

            Vector3 closestPoint = overlap.ClosestPoint(castCenter);
            Vector3 awayFromObstacle = castCenter - closestPoint;
            awayFromObstacle.y = 0f;
            if (awayFromObstacle.sqrMagnitude > 0.0001f)
            {
                accumulatedEscape += awayFromObstacle;
            }
        }

        if (accumulatedEscape.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        escapeDirection = accumulatedEscape.normalized;
        return true;
    }

    private LayerMask GetSolidQueryMask()
    {
        LayerMask mask = solidObstacleLayers;
        if (mask == 0)
        {
            return mask;
        }

        // Never treat walkable ground / unit volumes as solid blockers (breaks slopes if mask is broad).
        int groundLayer = LayerMask.NameToLayer("RTS_Ground");
        if (groundLayer >= 0)
        {
            mask &= ~(1 << groundLayer);
        }

        int unitLayer = RtsGroundUtility.UnitLayer;
        if (unitLayer >= 0)
        {
            mask &= ~(1 << unitLayer);
        }

        int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
        if (ignoreRaycast >= 0)
        {
            mask &= ~(1 << ignoreRaycast);
        }

        return mask;
    }

    private float GetNearestObstacleDistance(Vector3 castCenter, Vector3 halfExtents, Vector3 direction, float maxDistance)
    {
        LayerMask mask = GetSolidQueryMask();
        if (mask == 0)
        {
            return maxDistance;
        }

        float nearest = maxDistance;
        RaycastHit[] hits = Physics.BoxCastAll(
            castCenter,
            halfExtents,
            direction,
            Quaternion.identity,
            maxDistance,
            mask,
            QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
        {
            return nearest;
        }

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || IsOwnCollider(hit.collider))
            {
                continue;
            }

            if (!IsSolidHitBlockingHorizontalMove(hit, castCenter))
            {
                continue;
            }

            nearest = Mathf.Min(nearest, hit.distance);
        }

        return nearest;
    }

    private bool HasThinSolidBlockingRay(Vector3 castCenter, Vector3 halfExtents, Vector3 direction, float maxDistance)
    {
        LayerMask mask = GetSolidQueryMask();
        if (mask == 0 || maxDistance < 0.0001f)
        {
            return false;
        }

        float bodyMidY = transform.position.y + solidBodyBottomClearance + solidBodyHeight * 0.5f;
        Vector3 right = new Vector3(-direction.z, 0f, direction.x);
        if (right.sqrMagnitude < 0.0001f)
        {
            right = Vector3.right;
        }

        right.Normalize();
        float lateral = Mathf.Max(0.05f, halfExtents.x * 0.85f);

        Vector3[] origins =
        {
            new Vector3(castCenter.x, bodyMidY, castCenter.z),
            new Vector3(castCenter.x, bodyMidY, castCenter.z) + right * lateral,
            new Vector3(castCenter.x, bodyMidY, castCenter.z) - right * lateral,
            new Vector3(castCenter.x, GetSolidBodyFeetY() + solidBodyHeight * 0.25f, castCenter.z),
            new Vector3(castCenter.x, GetSolidBodyFeetY() + solidBodyHeight * 0.75f, castCenter.z)
        };

        for (int i = 0; i < origins.Length; i++)
        {
            if (Physics.Raycast(
                    origins[i],
                    direction,
                    out RaycastHit hit,
                    maxDistance,
                    mask,
                    QueryTriggerInteraction.Ignore)
                && hit.collider != null
                && !IsOwnCollider(hit.collider)
                && IsSolidHitBlockingHorizontalMove(hit, castCenter)
                && hit.distance < maxDistance - 0.001f)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsOverlappingSolidObstacle(Vector3 castCenter, Vector3 halfExtents)
    {
        LayerMask mask = GetSolidQueryMask();
        if (mask == 0)
        {
            return false;
        }

        Collider[] overlaps = Physics.OverlapBox(
            castCenter,
            halfExtents,
            Quaternion.identity,
            mask,
            QueryTriggerInteraction.Ignore);

        if (overlaps == null || overlaps.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider overlap = overlaps[i];
            if (overlap != null
                && !IsOwnCollider(overlap)
                && IsSolidColliderBlockingHorizontalMove(overlap, castCenter))
            {
                return true;
            }
        }

        return false;
    }

    private float GetSolidBodyFeetY()
    {
        return transform.position.y + solidBodyBottomClearance;
    }

    private float GetSolidBodyTopY()
    {
        return GetSolidBodyFeetY() + solidBodyHeight;
    }

    /// <summary>
    /// True when a solid collider blocks horizontal movement at body height (not overhead roofs).
    /// </summary>
    private bool IsSolidColliderBlockingHorizontalMove(Collider collider, Vector3 probeCenter)
    {
        if (collider == null)
        {
            return false;
        }

        float feetY = GetSolidBodyFeetY();
        float topY = GetSolidBodyTopY();

        // Fully overhead — e.g. gate roof above headroom.
        if (collider.bounds.min.y > topY + 0.05f)
        {
            return false;
        }

        // Fully underfoot — mis-tagged ground slab.
        if (collider.bounds.max.y < feetY - 0.05f)
        {
            return false;
        }

        // During gate traversal: anything whose lowest point is above waist is treated as roof/arch.
        if (TraverseGateCorridor && collider.bounds.min.y > feetY + solidBodyHeight * 0.45f)
        {
            return false;
        }

        Vector3 closest = collider.ClosestPoint(probeCenter);
        if (closest.y > topY + 0.15f)
        {
            return false;
        }

        if (TraverseGateCorridor && closest.y > feetY + solidBodyHeight * 0.55f)
        {
            return false;
        }

        if (closest.y < feetY - 0.25f)
        {
            return false;
        }

        // Vertical contact only — ignore mostly downward normals (underside of roofs).
        Vector3 away = probeCenter - closest;
        if (away.sqrMagnitude > 0.0001f && away.normalized.y < -0.55f)
        {
            return false;
        }

        return true;
    }

    private bool IsSolidHitBlockingHorizontalMove(RaycastHit hit, Vector3 castCenter)
    {
        if (hit.collider == null)
        {
            return false;
        }

        // Ceiling / overhead lip — should not block walking under it.
        if (hit.normal.y < -0.35f)
        {
            return false;
        }

        float topY = GetSolidBodyTopY();
        float feetY = GetSolidBodyFeetY();
        if (hit.point.y > topY + 0.15f)
        {
            return false;
        }

        if (TraverseGateCorridor && hit.point.y > feetY + solidBodyHeight * 0.55f)
        {
            return false;
        }

        if (hit.point.y < feetY - 0.25f)
        {
            return false;
        }

        return IsSolidColliderBlockingHorizontalMove(hit.collider, castCenter);
    }

    private bool IsOwnCollider(Collider candidate)
    {
        if (candidate == null)
        {
            return true;
        }

        if (movementBoundsCollider != null && candidate == movementBoundsCollider)
        {
            return true;
        }

        if (ownColliders == null || ownColliders.Length == 0)
        {
            return candidate.transform == transform || candidate.transform.IsChildOf(transform);
        }

        for (int i = 0; i < ownColliders.Length; i++)
        {
            if (ownColliders[i] == candidate)
            {
                return true;
            }
        }

        return false;
    }

    private void ResolveMovementBoundsCollider()
    {
        if (movementBoundsCollider != null)
        {
            return;
        }

        movementBoundsCollider = GetComponent<Collider>();
    }

    private void GetSolidBlockCastShape(out Vector3 castCenter, out Vector3 halfExtents)
    {
        Collider source = solidBlockCollider != null ? solidBlockCollider : movementBoundsCollider;
        castCenter = transform.position + Vector3.up * fallbackCastHalfExtents.y;
        halfExtents = new Vector3(
            Mathf.Max(0.05f, fallbackCastHalfExtents.x - obstacleSkin + solidThicknessPadding),
            solidBodyHeight * 0.5f,
            Mathf.Max(0.05f, fallbackCastHalfExtents.z - obstacleSkin + solidThicknessPadding));

        if (source == null)
        {
            castCenter.y = transform.position.y + solidBodyBottomClearance + halfExtents.y;
            return;
        }

        if (source is BoxCollider boxCollider)
        {
            castCenter = source.transform.TransformPoint(boxCollider.center);
            halfExtents = Vector3.Scale(boxCollider.size * 0.5f, source.transform.lossyScale);
        }
        else
        {
            Bounds bounds = source.bounds;
            castCenter = bounds.center;
            halfExtents = bounds.extents;
        }

        halfExtents.x = Mathf.Max(0.05f, Mathf.Abs(halfExtents.x) - obstacleSkin + solidThicknessPadding);
        halfExtents.z = Mathf.Max(0.05f, Mathf.Abs(halfExtents.z) - obstacleSkin + solidThicknessPadding);
        // Horizontal blocking only — ignore overhead solids (gate roofs, archways).
        halfExtents.y = solidBodyHeight * 0.5f;
        castCenter.y = transform.position.y + solidBodyBottomClearance + halfExtents.y;
    }

    private void GetMovementCastShape(out Vector3 castCenter, out Vector3 halfExtents)
    {
        // Path wall-slide still uses the shrunk movement hull; solid blocking uses GetSolidBlockCastShape.
        GetSolidBlockCastShape(out castCenter, out halfExtents);

        if (movementBoundsCollider != null && movementBoundsCollider != solidBlockCollider
            && movementBoundsCollider is BoxCollider boxCollider)
        {
            Vector3 shrunk = Vector3.Scale(boxCollider.size * 0.5f, movementBoundsCollider.transform.lossyScale);
            halfExtents.x = Mathf.Max(0.05f, Mathf.Abs(shrunk.x) - obstacleSkin);
            halfExtents.z = Mathf.Max(0.05f, Mathf.Abs(shrunk.z) - obstacleSkin);
            castCenter = movementBoundsCollider.transform.TransformPoint(boxCollider.center);
            castCenter.y = transform.position.y + halfExtents.y;
        }

        if (IsFollowingPath() && pathCollisionBuffer > 0f)
        {
            halfExtents.x = Mathf.Max(0.05f, halfExtents.x - pathCollisionBuffer * 0.25f);
            halfExtents.z = Mathf.Max(0.05f, halfExtents.z - pathCollisionBuffer * 0.25f);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawCollisionDebug)
        {
            return;
        }

        GetMovementCastShape(out Vector3 castCenter, out Vector3 halfExtents);
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.35f);
        Gizmos.DrawWireCube(castCenter, halfExtents * 2f);
    }
}
