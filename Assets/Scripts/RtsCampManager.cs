using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Assign camp safe zones and gate entry points for both factions. Attach to a GameManager (or any scene root) object.
/// Retreating troops route to their gate first, then into the camp center.
/// </summary>
public class RtsCampManager : MonoBehaviour
{
    public static RtsCampManager Instance { get; private set; }

    [Header("Camp Locations")]
    [SerializeField] private Transform friendlyCamp;
    [SerializeField] private Transform enemyCamp;

    [Header("Gate Entry Points")]
    [SerializeField] private Transform friendlyGate;
    [SerializeField] private Transform enemyGate;

    [Header("Arrival Zones")]
    [SerializeField, Min(0.1f)] private float gateArrivalRadius = 2f;
    [FormerlySerializedAs("campArrivalRadius")]
    [SerializeField, Min(0.1f)] private float campZoneRadius = 3f;
    [SerializeField, Min(0.05f)] private float campCenterArrivalRadius = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool drawDebugGizmos = true;
    [SerializeField] private Color friendlyCampColor = new Color(0.2f, 0.55f, 1f, 0.35f);
    [SerializeField] private Color enemyCampColor = new Color(1f, 0.3f, 0.25f, 0.35f);
    [SerializeField] private Color friendlyGateColor = new Color(0.2f, 0.9f, 0.45f, 0.9f);
    [SerializeField] private Color enemyGateColor = new Color(1f, 0.55f, 0.2f, 0.9f);

    public float CampZoneRadius => campZoneRadius;
    public float CampCenterArrivalRadius => campCenterArrivalRadius;
    public float GateArrivalRadius => gateArrivalRadius;
    public Transform FriendlyCamp => friendlyCamp;
    public Transform EnemyCamp => enemyCamp;
    public Transform FriendlyGate => friendlyGate;
    public Transform EnemyGate => enemyGate;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple RtsCampManager instances found. Using the most recent one.", this);
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

    public bool HasGate(TroopCombat.Faction faction)
    {
        return GetGateTransform(faction) != null;
    }

    public Vector3 GetCampCenter(TroopCombat.Faction faction)
    {
        Transform camp = GetCampTransform(faction);
        if (camp == null)
        {
            Debug.LogWarning("No camp assigned for faction " + faction + ".", this);
            return transform.position;
        }

        return camp.position;
    }

    public Vector3 GetGatePosition(TroopCombat.Faction faction)
    {
        Transform gate = GetGateTransform(faction);
        if (gate == null)
        {
            return GetCampCenter(faction);
        }

        return gate.position;
    }

    public bool IsInCampZone(Vector3 worldPosition, TroopCombat.Faction faction)
    {
        Transform camp = GetCampTransform(faction);
        if (camp == null)
        {
            return false;
        }

        return IsWithinHorizontalRadius(worldPosition, camp.position, campZoneRadius);
    }

    public bool IsFootprintOverlappingCampZone(Bounds footprint, TroopCombat.Faction faction)
    {
        Transform camp = GetCampTransform(faction);
        if (camp == null)
        {
            return false;
        }

        Vector3 campPosition = camp.position;
        if (IsWithinHorizontalRadius(footprint.center, campPosition, campZoneRadius + footprint.extents.magnitude))
        {
            float horizontalDistance = GetHorizontalDistanceToBounds(campPosition, footprint);
            return horizontalDistance <= campZoneRadius;
        }

        return false;
    }

    public bool HasReachedCampCenter(Vector3 worldPosition, TroopCombat.Faction faction)
    {
        Transform camp = GetCampTransform(faction);
        if (camp == null)
        {
            return false;
        }

        return IsWithinHorizontalRadius(worldPosition, camp.position, campCenterArrivalRadius);
    }

    public bool IsAtGate(Vector3 worldPosition, TroopCombat.Faction faction)
    {
        Transform gate = GetGateTransform(faction);
        if (gate == null)
        {
            return true;
        }

        return IsWithinHorizontalRadius(worldPosition, gate.position, gateArrivalRadius);
    }

    private Transform GetCampTransform(TroopCombat.Faction faction)
    {
        return faction == TroopCombat.Faction.Friendly ? friendlyCamp : enemyCamp;
    }

    private Transform GetGateTransform(TroopCombat.Faction faction)
    {
        return faction == TroopCombat.Faction.Friendly ? friendlyGate : enemyGate;
    }

    private static bool IsWithinHorizontalRadius(Vector3 worldPosition, Vector3 targetPosition, float radius)
    {
        Vector3 offset = worldPosition - targetPosition;
        offset.y = 0f;
        return offset.sqrMagnitude <= radius * radius;
    }

    private static float GetHorizontalDistanceToBounds(Vector3 point, Bounds bounds)
    {
        float offsetX = 0f;
        if (point.x < bounds.min.x)
        {
            offsetX = bounds.min.x - point.x;
        }
        else if (point.x > bounds.max.x)
        {
            offsetX = point.x - bounds.max.x;
        }

        float offsetZ = 0f;
        if (point.z < bounds.min.z)
        {
            offsetZ = bounds.min.z - point.z;
        }
        else if (point.z > bounds.max.z)
        {
            offsetZ = point.z - bounds.max.z;
        }

        return Mathf.Sqrt(offsetX * offsetX + offsetZ * offsetZ);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos)
        {
            return;
        }

        DrawCampZoneGizmo(friendlyCamp, friendlyCampColor);
        DrawCampZoneGizmo(enemyCamp, enemyCampColor);
        DrawCampCenterGizmo(friendlyCamp, friendlyCampColor);
        DrawCampCenterGizmo(enemyCamp, enemyCampColor);
        DrawGateGizmo(friendlyGate, friendlyGateColor);
        DrawGateGizmo(enemyGate, enemyGateColor);

        if (friendlyGate != null && friendlyCamp != null)
        {
            Gizmos.color = friendlyGateColor;
            Gizmos.DrawLine(friendlyGate.position, friendlyCamp.position);
        }

        if (enemyGate != null && enemyCamp != null)
        {
            Gizmos.color = enemyGateColor;
            Gizmos.DrawLine(enemyGate.position, enemyCamp.position);
        }
    }

    private void DrawCampZoneGizmo(Transform camp, Color fillColor)
    {
        if (camp == null)
        {
            return;
        }

        Gizmos.color = fillColor;
        Gizmos.DrawSphere(camp.position, campZoneRadius);
        Gizmos.color = new Color(fillColor.r, fillColor.g, fillColor.b, 1f);
        Gizmos.DrawWireSphere(camp.position, campZoneRadius);
    }

    private void DrawCampCenterGizmo(Transform camp, Color fillColor)
    {
        if (camp == null)
        {
            return;
        }

        Gizmos.color = new Color(fillColor.r, fillColor.g, fillColor.b, 1f);
        Gizmos.DrawWireSphere(camp.position, campCenterArrivalRadius);
    }

    private void DrawGateGizmo(Transform gate, Color color)
    {
        if (gate == null)
        {
            return;
        }

        Gizmos.color = color;
        Gizmos.DrawWireSphere(gate.position, gateArrivalRadius);
        Gizmos.DrawLine(gate.position, gate.position + gate.forward * 1.5f);
    }
}
