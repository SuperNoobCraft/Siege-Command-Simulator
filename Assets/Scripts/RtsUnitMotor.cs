using UnityEngine;

[DefaultExecutionOrder(50)]
public class RtsUnitMotor : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private bool isCommandUnit = true;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 2.5f;
    [SerializeField] private float stoppingDistance = 0.05f;

    [Header("Solid Obstacles")]
    [SerializeField] private LayerMask solidObstacleLayers;
    [SerializeField] private Collider movementBoundsCollider;
    [SerializeField] private Vector3 fallbackCastHalfExtents = new Vector3(1f, 0.5f, 1f);
    [SerializeField, Min(0f)] private float obstacleSkin = 0.05f;
    [SerializeField] private bool stopImmediatelyOnWallHit = true;
    [SerializeField] private bool drawCollisionDebug;

    private Collider[] ownColliders;
    private bool hasDestination;
    private Vector3 destination;

    public bool IsCommandUnit => isCommandUnit;
    public bool CanReceiveCommands { get; set; } = true;
    public float MoveSpeedMultiplier { get; set; } = 1f;
    public bool HasDestination => hasDestination;
    public Vector3 MoveDirection { get; private set; } = Vector3.forward;
    public bool IsBlockedBySolidObstacle { get; private set; }

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
        destination = new Vector3(worldPoint.x, transform.position.y, worldPoint.z);
        hasDestination = true;
        IsBlockedBySolidObstacle = false;
    }

    public void Stop()
    {
        hasDestination = false;
        IsBlockedBySolidObstacle = false;
    }

    private void Update()
    {
        if (!hasDestination)
        {
            IsBlockedBySolidObstacle = false;
            return;
        }

        if (solidObstacleLayers != 0 && IsCurrentlyOverlappingObstacle())
        {
            StopOnWall();
            return;
        }

        Vector3 offset = destination - transform.position;
        offset.y = 0f;

        if (offset.sqrMagnitude <= stoppingDistance * stoppingDistance)
        {
            hasDestination = false;
            IsBlockedBySolidObstacle = false;
            return;
        }

        Vector3 direction = offset.normalized;
        if (direction.sqrMagnitude > 0.0001f)
        {
            MoveDirection = direction;
        }

        float stepDistance = moveSpeed * Mathf.Max(0f, MoveSpeedMultiplier) * Time.deltaTime;
        Vector3 desiredDelta = direction * stepDistance;

        if (!TryGetAllowedDelta(desiredDelta, out Vector3 allowedDelta))
        {
            StopOnWall();
            return;
        }

        if (allowedDelta.sqrMagnitude > 0f)
        {
            transform.position += allowedDelta;
        }

        Vector3 remaining = destination - transform.position;
        remaining.y = 0f;
        if (remaining.sqrMagnitude <= stoppingDistance * stoppingDistance)
        {
            transform.position = new Vector3(destination.x, transform.position.y, destination.z);
            hasDestination = false;
            IsBlockedBySolidObstacle = false;
        }
    }

    private void StopOnWall()
    {
        if (stopImmediatelyOnWallHit)
        {
            hasDestination = false;
        }

        IsBlockedBySolidObstacle = true;
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
