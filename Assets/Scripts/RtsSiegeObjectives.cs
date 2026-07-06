using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Battlefield objective references for AI: friendly cannons and retreat-route choke points.
/// </summary>
public class RtsSiegeObjectives : MonoBehaviour
{
    public static RtsSiegeObjectives Instance { get; private set; }

    [Header("Primary Objectives")]
    [SerializeField] private Transform[] friendlyCannons;

    [Header("Retreat Intercept Points")]
    [Tooltip("Optional choke points between the battlefield and the friendly camp gate.")]
    [SerializeField] private Transform[] friendlyRetreatInterceptPoints;

    [Header("Debug")]
    [SerializeField] private bool drawDebugGizmos = true;
    [SerializeField] private Color cannonGizmoColor = new Color(1f, 0.75f, 0.2f, 0.9f);
    [SerializeField] private Color interceptGizmoColor = new Color(0.95f, 0.35f, 0.55f, 0.9f);

    public IReadOnlyList<Transform> FriendlyCannons => friendlyCannons;
    public IReadOnlyList<Transform> FriendlyRetreatInterceptPoints => friendlyRetreatInterceptPoints;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple RtsSiegeObjectives instances found. Using the most recent one.", this);
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public bool HasCannons()
    {
        return GetValidTransforms(friendlyCannons).Count > 0;
    }

    public Vector3 GetNearestCannonPosition(Vector3 fromPosition)
    {
        List<Transform> cannons = GetValidTransforms(friendlyCannons);
        if (cannons.Count == 0)
        {
            return fromPosition;
        }

        Transform nearest = cannons[0];
        float nearestDistance = GetHorizontalDistanceSqr(fromPosition, nearest.position);
        for (int i = 1; i < cannons.Count; i++)
        {
            float distance = GetHorizontalDistanceSqr(fromPosition, cannons[i].position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = cannons[i];
            }
        }

        return nearest.position;
    }

    public Vector3 GetBestRetreatInterceptPosition(Vector3 fromPosition, Vector3 retreatingFriendlyPosition)
    {
        List<Transform> interceptPoints = GetValidTransforms(friendlyRetreatInterceptPoints);
        if (interceptPoints.Count == 0)
        {
            RtsCampManager campManager = RtsCampManager.Instance;
            if (campManager != null)
            {
                Vector3 gate = campManager.GetGateOutsidePosition(TroopCombat.Faction.Friendly);
                return Vector3.Lerp(retreatingFriendlyPosition, gate, 0.5f);
            }

            return retreatingFriendlyPosition;
        }

        Transform best = interceptPoints[0];
        float bestScore = float.MaxValue;
        for (int i = 0; i < interceptPoints.Count; i++)
        {
            Transform point = interceptPoints[i];
            float distanceToSelf = GetHorizontalDistanceSqr(fromPosition, point.position);
            float distanceToRetreater = GetHorizontalDistanceSqr(retreatingFriendlyPosition, point.position);
            float score = distanceToSelf + distanceToRetreater * 0.35f;
            if (score < bestScore)
            {
                bestScore = score;
                best = point;
            }
        }

        return best.position;
    }

    private static List<Transform> GetValidTransforms(Transform[] transforms)
    {
        List<Transform> results = new List<Transform>();
        if (transforms == null)
        {
            return results;
        }

        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i] != null)
            {
                results.Add(transforms[i]);
            }
        }

        return results;
    }

    private static float GetHorizontalDistanceSqr(Vector3 a, Vector3 b)
    {
        Vector3 offset = a - b;
        offset.y = 0f;
        return offset.sqrMagnitude;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos)
        {
            return;
        }

        if (friendlyCannons != null)
        {
            Gizmos.color = cannonGizmoColor;
            for (int i = 0; i < friendlyCannons.Length; i++)
            {
                Transform cannon = friendlyCannons[i];
                if (cannon == null)
                {
                    continue;
                }

                Gizmos.DrawWireSphere(cannon.position, 1.25f);
                Gizmos.DrawLine(cannon.position, cannon.position + Vector3.up * 2f);
            }
        }

        if (friendlyRetreatInterceptPoints != null)
        {
            Gizmos.color = interceptGizmoColor;
            for (int i = 0; i < friendlyRetreatInterceptPoints.Length; i++)
            {
                Transform point = friendlyRetreatInterceptPoints[i];
                if (point == null)
                {
                    continue;
                }

                Gizmos.DrawWireCube(point.position, new Vector3(1.5f, 0.2f, 1.5f));
            }
        }
    }
}
