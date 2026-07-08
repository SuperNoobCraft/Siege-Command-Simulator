using UnityEngine;

public class TroopRangedProjectile : MonoBehaviour
{
    private const float HazardRadius = 0.2f;

    private Vector3 startPosition;
    private Vector3 targetPosition;
    private float travelSpeed;
    private float arcHeight;
    private float traveledDistance;
    private float totalDistance;
    private Vector3 previousPosition;
    private bool isPlayerHazard;
    private LayerMask playerHitLayers;
    private bool hasRegisteredHit;

    public static TroopRangedProjectile Launch(
        GameObject prefab,
        Vector3 start,
        Vector3 target,
        float speed,
        float arcHeight)
    {
        if (prefab == null)
        {
            return null;
        }

        GameObject instance = Instantiate(prefab, start, Quaternion.identity);
        TroopRangedProjectile projectile = instance.GetComponent<TroopRangedProjectile>();
        if (projectile == null)
        {
            projectile = instance.AddComponent<TroopRangedProjectile>();
        }

        projectile.Initialize(start, target, speed, arcHeight);
        return projectile;
    }

    public static TroopRangedProjectile LaunchPlayerHazard(
        GameObject prefab,
        Vector3 start,
        Vector3 target,
        float speed,
        float arcHeight,
        LayerMask hitLayers)
    {
        TroopRangedProjectile projectile = Launch(prefab, start, target, speed, arcHeight);
        if (projectile != null)
        {
            projectile.ConfigurePlayerHazard(hitLayers);
        }

        return projectile;
    }

    public void Initialize(Vector3 start, Vector3 target, float speed, float arcHeight)
    {
        startPosition = start;
        targetPosition = target;
        travelSpeed = Mathf.Max(0.01f, speed);
        this.arcHeight = Mathf.Max(0f, arcHeight);
        traveledDistance = 0f;
        totalDistance = GetHorizontalDistance(startPosition, targetPosition);
        totalDistance = Mathf.Max(0.01f, totalDistance);
        previousPosition = startPosition;
        transform.position = startPosition;
        UpdateFacing(startPosition, GetPositionAtProgress(0.001f));
    }

    public void ConfigurePlayerHazard(LayerMask hitLayers)
    {
        isPlayerHazard = true;
        playerHitLayers = hitLayers;
        EnsureHazardPhysics();
    }

    private void Update()
    {
        traveledDistance += travelSpeed * Time.deltaTime;
        float progress = Mathf.Clamp01(traveledDistance / totalDistance);
        Vector3 currentPosition = GetPositionAtProgress(progress);

        if (isPlayerHazard)
        {
            TryDetectPlayerHit(previousPosition, currentPosition);
        }

        UpdateFacing(previousPosition, currentPosition);
        transform.position = currentPosition;
        previousPosition = currentPosition;

        if (progress >= 1f)
        {
            if (isPlayerHazard)
            {
                TryDetectPlayerOverlap(currentPosition);
            }

            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TryRegisterPlayerHit(other);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null)
        {
            return;
        }

        TryRegisterPlayerHit(collision.collider);
    }

    private void TryDetectPlayerHit(Vector3 from, Vector3 to)
    {
        if (hasRegisteredHit)
        {
            return;
        }

        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
        {
            return;
        }

        if (Physics.SphereCast(
                from,
                HazardRadius,
                delta.normalized,
                out RaycastHit hit,
                distance,
                playerHitLayers,
                QueryTriggerInteraction.Collide))
        {
            TryRegisterPlayerHit(hit.collider);
        }
    }

    private void TryDetectPlayerOverlap(Vector3 position)
    {
        if (hasRegisteredHit)
        {
            return;
        }

        Collider[] overlaps = Physics.OverlapSphere(
            position,
            HazardRadius,
            playerHitLayers,
            QueryTriggerInteraction.Collide);

        if (overlaps == null)
        {
            return;
        }

        for (int i = 0; i < overlaps.Length; i++)
        {
            if (TryRegisterPlayerHit(overlaps[i]))
            {
                return;
            }
        }
    }

    private bool TryRegisterPlayerHit(Collider other)
    {
        if (!isPlayerHazard || hasRegisteredHit || other == null)
        {
            return false;
        }

        if ((playerHitLayers.value & (1 << other.gameObject.layer)) == 0)
        {
            return false;
        }

        if (!SiegeCommanderArrowHealth.TryResolveHitCollider(other, out SiegeCommanderArrowHealth commanderHealth))
        {
            return false;
        }

        if (commanderHealth.IsDefeated)
        {
            return false;
        }

        hasRegisteredHit = true;
        commanderHealth.RegisterArrowHit(transform.position);
        Destroy(gameObject);
        return true;
    }

    private void EnsureHazardPhysics()
    {
        Rigidbody body = GetComponent<Rigidbody>();
        if (body == null)
        {
            body = gameObject.AddComponent<Rigidbody>();
        }

        body.isKinematic = true;
        body.useGravity = false;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        SphereCollider collider = GetComponent<SphereCollider>();
        if (collider == null)
        {
            collider = gameObject.AddComponent<SphereCollider>();
        }

        collider.isTrigger = true;
        collider.radius = HazardRadius;
    }

    private Vector3 GetPositionAtProgress(float progress)
    {
        Vector3 linearPosition = Vector3.Lerp(startPosition, targetPosition, progress);
        linearPosition.y += arcHeight * 4f * progress * (1f - progress);
        return linearPosition;
    }

    private void UpdateFacing(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private static float GetHorizontalDistance(Vector3 a, Vector3 b)
    {
        Vector3 offset = a - b;
        offset.y = 0f;
        return offset.magnitude;
    }
}
