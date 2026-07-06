using UnityEngine;

public class TroopRangedProjectile : MonoBehaviour
{
    private Vector3 startPosition;
    private Vector3 targetPosition;
    private float travelSpeed;
    private float arcHeight;
    private float traveledDistance;
    private float totalDistance;
    private Vector3 previousPosition;

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

    private void Update()
    {
        traveledDistance += travelSpeed * Time.deltaTime;
        float progress = Mathf.Clamp01(traveledDistance / totalDistance);
        Vector3 currentPosition = GetPositionAtProgress(progress);
        UpdateFacing(previousPosition, currentPosition);
        transform.position = currentPosition;
        previousPosition = currentPosition;

        if (progress >= 1f)
        {
            Destroy(gameObject);
        }
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
