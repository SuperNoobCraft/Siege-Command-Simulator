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
    [SerializeField] private Collider movementBoundsCollider;
    [SerializeField] private Vector3 fallbackCastHalfExtents = new Vector3(1f, 0.5f, 1f);
    [SerializeField, Min(0f)] private float obstacleSkin = 0.05f;
    [SerializeField] private bool stopImmediatelyOnWallHit = true;
    [SerializeField, Min(0f)] private float wallEscapeGraceDuration = 2.75f;
    [SerializeField, Range(0f, 1f)] private float wallEscapeDirectionThreshold = 0.1f;
    [SerializeField] private bool enableObstacleAvoidance = true;
    [SerializeField, Min(0.1f)] private float stuckDetectionTime = 0.6f;
    [SerializeField, Min(0.05f)] private float stuckProgressDistance = 0.12f;
    [SerializeField, Range(0.1f, 1f)] private float stuckProgressSpeedFactor = 0.35f;
    [SerializeField, Min(0.5f)] private float detourProbeDistance = 4f;
    [SerializeField, Min(0.25f)] private float detourWaypointSpacing = 1.5f;

    [Header("Drawn Path Following")]
    [Tooltip("Shrinks the collision hull while following a drawn path so slight arcs near walls can still pass.")]
    [SerializeField, Min(0f)] private float pathCollisionBuffer = 0.35f;
    [Tooltip("Seconds a drawn-path unit can remain blocked before movement is halted.")]
    [SerializeField, Min(0f)] private float pathBlockedHaltDelay = 0.45f;
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
    private float nextDetourAttemptTime;
    private float pathBlockedSinceTime = -1f;
    private bool pathMovementHalted;

    public bool IsCommandUnit => isCommandUnit;
    public bool CanReceiveCommands { get; set; } = true;
    public float MoveSpeedMultiplier { get; set; } = 1f;
    public bool HasDestination => hasDestination;
    public bool HasActivePath => movementMode == MovementMode.Path && pathWaypoints.Count > 0;
    public bool HasActivePathForDisplay => HasActivePath && !pathMovementHalted;
    public Vector3 MoveDirection { get; private set; } = Vector3.forward;
    public bool IsBlockedBySolidObstacle { get; private set; }
    public bool IsStuck { get; private set; }

    private void Awake()
    {
        ResolveMovementBoundsCollider();
        ownColliders = GetComponentsInChildren<Collider>(includeInactive: false);
    }

    public void SetMovementBoundsCollider(Collider collider)
    {
        movementBoundsCollider = collider;
    }

    public void MoveTo(Vector3 worldPoint)
    {
        ClearPath();
        movementMode = MovementMode.Direct;
        destination = FlattenToGround(worldPoint);
        finalDestination = destination;
        usingDetour = false;
        hasDestination = true;
        IsBlockedBySolidObstacle = false;
        IsStuck = false;
        pathBlockedSinceTime = -1f;
        pathMovementHalted = false;
        ResetStuckTracking();
    }

    public void FollowPath(IReadOnlyList<Vector3> waypoints)
    {
        ClearPath();
        if (waypoints == null || waypoints.Count == 0)
        {
            return;
        }

        for (int i = 0; i < waypoints.Count; i++)
        {
            pathWaypoints.Add(FlattenToGround(waypoints[i]));
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
        pathBlockedSinceTime = -1f;
        pathMovementHalted = false;
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
        if (!hasDestination)
        {
            if (!pathMovementHalted)
            {
                IsBlockedBySolidObstacle = false;
                IsStuck = false;
            }

            wallEscapeGraceEndTime = 0f;
            return;
        }

        UpdateStuckDetection();

        if (IsFollowingPath() && hasDestination && ShouldHaltBlockedPath())
        {
            HaltBlockedPath();
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
            return;
        }

        Vector3 direction = offset.normalized;
        if (direction.sqrMagnitude > 0.0001f)
        {
            MoveDirection = direction;
        }

        float stepDistance = moveSpeed * Mathf.Max(0f, MoveSpeedMultiplier) * Time.deltaTime;

        if (IsFollowingPath() && IsBlockedBySolidObstacle && !IsInWallEscapeGrace)
        {
            if (ShouldHaltBlockedPath())
            {
                HaltBlockedPath();
                return;
            }

            if (slideAlongWallOnPath && TrySlideAlongWall(direction, stepDistance))
            {
                return;
            }

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

        float distanceToTarget = offset.magnitude;
        if (distanceToTarget <= arrivalSlowdownRadius && arrivalSlowdownRadius > 0.01f)
        {
            float slowdown = Mathf.Clamp01(distanceToTarget / arrivalSlowdownRadius);
            stepDistance *= Mathf.Max(0.12f, slowdown);
        }

        Vector3 desiredDelta = direction * stepDistance;
        bool useWallEscapeGrace = isOverlapping && movingAwayFromWall && IsInWallEscapeGrace;

        if (useWallEscapeGrace)
        {
            transform.position += desiredDelta;
            pathBlockedSinceTime = -1f;
            IsBlockedBySolidObstacle = false;
            IsStuck = false;
            ResetStuckTracking();
        }
        else if (!TryGetAllowedDelta(desiredDelta, out Vector3 allowedDelta))
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
            if (allowedDelta.sqrMagnitude > 0f)
            {
                transform.position += allowedDelta;
            }

            pathBlockedSinceTime = -1f;
            IsBlockedBySolidObstacle = false;
            IsStuck = false;
            ResetStuckTracking();
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
        if (segmentLength < 0.01f)
        {
            return destination;
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
                MoveDirection = allowedEscape.normalized;
                pathBlockedSinceTime = -1f;
                IsBlockedBySolidObstacle = false;
                return true;
            }
        }

        if (TryGetBestAvoidanceDelta(desiredDirection, goalDirection, stepDistance, out Vector3 avoidanceDelta))
        {
            transform.position += avoidanceDelta;
            MoveDirection = avoidanceDelta.normalized;
            pathBlockedSinceTime = -1f;
            IsBlockedBySolidObstacle = false;
            return true;
        }

        if (IsStuck && Time.time >= nextDetourAttemptTime && TryInsertDetourWaypoint(goalDirection))
        {
            nextDetourAttemptTime = Time.time + stuckDetectionTime;
            IsBlockedBySolidObstacle = false;
            return true;
        }

        IsBlockedBySolidObstacle = true;
        return false;
    }

    private bool TrySlideAlongWall(Vector3 desiredDirection, float stepDistance)
    {
        if (!TryGetOverlappingEscapeDirection(out Vector3 escapeDirection))
        {
            return false;
        }

        Vector3 goalDirection = GetGoalDirection();
        Vector3 tangent = Vector3.ProjectOnPlane(goalDirection.sqrMagnitude > 0.0001f ? goalDirection : desiredDirection, escapeDirection);
        tangent.y = 0f;
        if (tangent.sqrMagnitude < 0.0001f)
        {
            tangent = new Vector3(-escapeDirection.z, 0f, escapeDirection.x);
        }

        tangent.Normalize();
        Vector3[] slideDirections = { tangent, -tangent };

        for (int i = 0; i < slideDirections.Length; i++)
        {
            Vector3 slideDelta = slideDirections[i] * stepDistance;
            if (!TryGetAllowedDelta(slideDelta, out Vector3 allowedSlide) || allowedSlide.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            transform.position += allowedSlide;
            MoveDirection = allowedSlide.normalized;
            pathBlockedSinceTime = -1f;
            IsBlockedBySolidObstacle = false;
            IsStuck = false;
            ResetStuckTracking();
            return true;
        }

        return false;
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
            float score = progressScore * 2f + distanceScore;
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
        float[] sideMultipliers = { 1f, -1f, 1.75f, -1.75f, 2.5f, -2.5f };

        for (int i = 0; i < sideMultipliers.Length; i++)
        {
            Vector3 detourPoint = transform.position
                + tangent * (detourProbeDistance * sideMultipliers[i])
                + goalDirection * detourWaypointSpacing;
            detourPoint = FlattenToGround(detourPoint);

            if (!IsPositionClear(detourPoint))
            {
                continue;
            }

            if (!HasLineOfMovement(transform.position, detourPoint))
            {
                continue;
            }

            finalDestination = usingDetour ? finalDestination : destination;
            destination = detourPoint;
            usingDetour = true;
            IsStuck = false;
            ResetStuckTracking();
            return true;
        }

        return false;
    }

    private void ResumeAfterDetour()
    {
        destination = finalDestination;
        usingDetour = false;
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

        float movedDistanceSqr = HorizontalDistanceSqr(transform.position, stuckSamplePosition);
        float progressThreshold = GetStuckProgressThreshold();
        IsStuck = movedDistanceSqr < progressThreshold * progressThreshold;
        stuckSamplePosition = transform.position;
        stuckSampleTime = Time.time;
    }

    private float GetStuckProgressThreshold()
    {
        float speedScaledDistance = GetCurrentMoveSpeed() * stuckDetectionTime * stuckProgressSpeedFactor;
        return Mathf.Max(stuckProgressDistance, speedScaledDistance);
    }

    private void ResetStuckTracking()
    {
        stuckSamplePosition = transform.position;
        stuckSampleTime = Time.time;
        IsStuck = false;
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

    private bool IsInWallEscapeGrace => wallEscapeGraceDuration > 0f && Time.time < wallEscapeGraceEndTime;

    private bool ShouldHaltBlockedPath()
    {
        if (IsBlockedBySolidObstacle)
        {
            if (pathBlockedSinceTime < 0f)
            {
                pathBlockedSinceTime = Time.time;
            }

            return pathBlockedHaltDelay <= 0f || Time.time - pathBlockedSinceTime >= pathBlockedHaltDelay;
        }

        if (IsStuck && (IsBlockedBySolidObstacle || pathBlockedSinceTime >= 0f))
        {
            return true;
        }

        if (!IsBlockedBySolidObstacle)
        {
            pathBlockedSinceTime = -1f;
        }

        return false;
    }

    private void MarkPathBlockedThisFrame()
    {
        if (pathBlockedSinceTime < 0f)
        {
            pathBlockedSinceTime = Time.time;
        }

        IsBlockedBySolidObstacle = true;
    }

    private void HaltBlockedPath()
    {
        movementMode = MovementMode.None;
        hasDestination = false;
        usingDetour = false;
        ClearPath();
        wallEscapeGraceEndTime = 0f;
        pathBlockedSinceTime = -1f;
        pathMovementHalted = true;
        IsBlockedBySolidObstacle = true;
        IsStuck = true;
        ResetStuckTracking();
    }

    private void StopOnWall()
    {
        if (IsFollowingPath())
        {
            MarkPathBlockedThisFrame();
            if (ShouldHaltBlockedPath())
            {
                HaltBlockedPath();
            }

            return;
        }

        bool shouldClearMovement = stopImmediatelyOnWallHit && !enableObstacleAvoidance;
        if (shouldClearMovement)
        {
            ClearMovement();
        }

        IsBlockedBySolidObstacle = true;
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
        pathBlockedSinceTime = -1f;
        pathMovementHalted = false;
        ResetStuckTracking();
    }

    private Vector3 FlattenToGround(Vector3 worldPoint)
    {
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
        if (desiredDelta.sqrMagnitude < 0.000001f)
        {
            allowedDelta = desiredDelta;
            return true;
        }

        if (solidObstacleLayers == 0)
        {
            allowedDelta = desiredDelta;
            return true;
        }

        GetMovementCastShape(out Vector3 castCenter, out Vector3 halfExtents);

        Vector3 direction = desiredDelta.normalized;
        float distance = desiredDelta.magnitude;
        float nearestBlockedDistance = GetNearestObstacleDistance(castCenter, halfExtents, direction, distance);

        if (nearestBlockedDistance >= distance)
        {
            Vector3 destinationCenter = castCenter + direction * distance;
            if (!IsOverlappingSolidObstacle(destinationCenter, halfExtents))
            {
                allowedDelta = desiredDelta;
                return true;
            }

            nearestBlockedDistance = 0f;
        }

        float safeDistance = Mathf.Max(0f, nearestBlockedDistance - obstacleSkin);
        if (safeDistance <= 0.001f)
        {
            return false;
        }

        allowedDelta = direction * safeDistance;
        return true;
    }

    private bool IsCurrentlyOverlappingObstacle()
    {
        GetMovementCastShape(out Vector3 castCenter, out Vector3 halfExtents);
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
        GetMovementCastShape(out Vector3 castCenter, out Vector3 halfExtents);

        Collider[] overlaps = Physics.OverlapBox(
            castCenter,
            halfExtents,
            Quaternion.identity,
            solidObstacleLayers,
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

    private float GetNearestObstacleDistance(Vector3 castCenter, Vector3 halfExtents, Vector3 direction, float maxDistance)
    {
        float nearest = maxDistance;
        RaycastHit[] hits = Physics.BoxCastAll(
            castCenter,
            halfExtents,
            direction,
            Quaternion.identity,
            maxDistance,
            solidObstacleLayers,
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

            nearest = Mathf.Min(nearest, hit.distance);
        }

        return nearest;
    }

    private bool IsOverlappingSolidObstacle(Vector3 castCenter, Vector3 halfExtents)
    {
        Collider[] overlaps = Physics.OverlapBox(
            castCenter,
            halfExtents,
            Quaternion.identity,
            solidObstacleLayers,
            QueryTriggerInteraction.Ignore);

        if (overlaps == null || overlaps.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider overlap = overlaps[i];
            if (overlap != null && !IsOwnCollider(overlap))
            {
                return true;
            }
        }

        return false;
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

    private void GetMovementCastShape(out Vector3 castCenter, out Vector3 halfExtents)
    {
        castCenter = transform.position + Vector3.up * fallbackCastHalfExtents.y;
        halfExtents = new Vector3(
            Mathf.Max(0.05f, fallbackCastHalfExtents.x - obstacleSkin),
            Mathf.Max(0.05f, fallbackCastHalfExtents.y - obstacleSkin),
            Mathf.Max(0.05f, fallbackCastHalfExtents.z - obstacleSkin));

        if (movementBoundsCollider == null)
        {
            return;
        }

        if (movementBoundsCollider is BoxCollider boxCollider)
        {
            castCenter = movementBoundsCollider.transform.TransformPoint(boxCollider.center);
            halfExtents = Vector3.Scale(boxCollider.size * 0.5f, movementBoundsCollider.transform.lossyScale);
        }
        else
        {
            Bounds bounds = movementBoundsCollider.bounds;
            castCenter = bounds.center;
            halfExtents = bounds.extents;
        }

        halfExtents.x = Mathf.Max(0.05f, halfExtents.x - obstacleSkin);
        halfExtents.y = Mathf.Max(0.05f, halfExtents.y - obstacleSkin);
        halfExtents.z = Mathf.Max(0.05f, halfExtents.z - obstacleSkin);

        if (IsFollowingPath() && pathCollisionBuffer > 0f)
        {
            halfExtents.x = Mathf.Max(0.05f, halfExtents.x - pathCollisionBuffer);
            halfExtents.z = Mathf.Max(0.05f, halfExtents.z - pathCollisionBuffer);
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
