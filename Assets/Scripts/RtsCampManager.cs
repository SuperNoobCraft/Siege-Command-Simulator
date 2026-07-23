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

    [Header("Gate Waypoints")]
    [Tooltip("City-side waypoint just inside the gate. Troops retreat here before entering camp.")]
    [SerializeField] private Transform friendlyGateInside;
    [Tooltip("Battlefield-side waypoint just outside the gate. Troops approach here before entering.")]
    [SerializeField] private Transform friendlyGateOutside;
    [Tooltip("City-side waypoint just inside the gate. Troops retreat here before entering camp.")]
    [SerializeField] private Transform enemyGateInside;
    [Tooltip("Battlefield-side waypoint just outside the gate. Troops approach here before entering.")]
    [SerializeField] private Transform enemyGateOutside;

    [Header("Arrival Zones")]
    [SerializeField, Min(0.1f)] private float gateArrivalRadius = 2f;
    [Tooltip("How close a retreating regiment must be before its gate opens.")]
    [SerializeField, Min(0.1f)] private float gateOpenRadius = 12f;
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
    public float GateOpenRadius => gateOpenRadius;
    public Transform FriendlyCamp => friendlyCamp;
    public Transform EnemyCamp => enemyCamp;
    public Transform FriendlyGate => friendlyGate;
    public Transform EnemyGate => enemyGate;
    public Transform FriendlyGateInside => friendlyGateInside;
    public Transform FriendlyGateOutside => friendlyGateOutside;
    public Transform EnemyGateInside => enemyGateInside;
    public Transform EnemyGateOutside => enemyGateOutside;

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
        return GetGateOutsidePosition(faction);
    }

    public Vector3 GetGateInsidePosition(TroopCombat.Faction faction)
    {
        Transform gateInside = GetGateInsideTransform(faction);
        if (gateInside != null)
        {
            return gateInside.position;
        }

        Transform camp = GetCampTransform(faction);
        if (camp != null)
        {
            Vector3 outside = GetGateOutsidePosition(faction);
            Vector3 towardCamp = camp.position - outside;
            towardCamp.y = 0f;
            if (towardCamp.sqrMagnitude > 0.0001f)
            {
                return outside + towardCamp.normalized * Mathf.Max(gateArrivalRadius, 1f);
            }
        }

        return GetGateOutsidePosition(faction);
    }

    public Vector3 GetGateOutsidePosition(TroopCombat.Faction faction)
    {
        Transform gateOutside = GetGateOutsideTransform(faction);
        if (gateOutside != null)
        {
            return gateOutside.position;
        }

        Transform gate = GetGateTransform(faction);
        if (gate != null)
        {
            return gate.position;
        }

        return GetCampCenter(faction);
    }

    public bool HasGateWaypoints(TroopCombat.Faction faction)
    {
        return GetGateInsideTransform(faction) != null || GetGateOutsideTransform(faction) != null || GetGateTransform(faction) != null;
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

    public Vector3 ClampOutsideCampZone(Vector3 worldPosition, TroopCombat.Faction faction)
    {
        if (faction == TroopCombat.Faction.Friendly)
        {
            return ClampOutsideFriendlyProtectedZone(worldPosition);
        }

        Transform camp = GetCampTransform(faction);
        if (camp == null || !IsInCampZone(worldPosition, faction))
        {
            return worldPosition;
        }

        Vector3 campPosition = camp.position;
        Vector3 away = worldPosition - campPosition;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f)
        {
            Vector3 gateOutside = GetGateOutsidePosition(faction);
            away = gateOutside - campPosition;
            away.y = 0f;
        }

        if (away.sqrMagnitude < 0.0001f)
        {
            away = Vector3.forward;
        }

        away.Normalize();
        float outsideRadius = campZoneRadius + Mathf.Max(gateArrivalRadius * 0.5f, 0.5f);
        Vector3 clamped = campPosition + away * outsideRadius;
        clamped.y = worldPosition.y;
        return clamped;
    }

    public bool IsInFriendlyProtectedZone(Vector3 worldPosition)
    {
        if (IsInCampZone(worldPosition, TroopCombat.Faction.Friendly))
        {
            return true;
        }

        if (IsAtGateInside(worldPosition, TroopCombat.Faction.Friendly))
        {
            return true;
        }

        Transform camp = friendlyCamp;
        if (camp != null)
        {
            float protectedRadius = GetFriendlyProtectedRadius();
            if (IsWithinHorizontalRadius(worldPosition, camp.position, protectedRadius))
            {
                return true;
            }
        }

        Vector3 gateInside = GetGateInsidePosition(TroopCombat.Faction.Friendly);
        if (IsWithinHorizontalRadius(worldPosition, gateInside, gateArrivalRadius))
        {
            return true;
        }

        return false;
    }

    public bool IsFriendlyRegimentProtected(Vector3 regimentPosition, Bounds footprint)
    {
        if (footprint.size.sqrMagnitude > 0.0001f)
        {
            if (IsFootprintOverlappingFriendlyProtectedZone(footprint))
            {
                return true;
            }
        }

        return IsInFriendlyProtectedZone(regimentPosition);
    }

    public bool IsFootprintOverlappingFriendlyProtectedZone(Bounds footprint)
    {
        if (IsFootprintOverlappingCampZone(footprint, TroopCombat.Faction.Friendly))
        {
            return true;
        }

        Transform camp = friendlyCamp;
        if (camp == null)
        {
            return false;
        }

        float protectedRadius = GetFriendlyProtectedRadius();
        Vector3 campPosition = camp.position;
        if (IsWithinHorizontalRadius(footprint.center, campPosition, protectedRadius + footprint.extents.magnitude))
        {
            float horizontalDistance = GetHorizontalDistanceToBounds(campPosition, footprint);
            if (horizontalDistance <= protectedRadius)
            {
                return true;
            }
        }

        Vector3 gateInside = GetGateInsidePosition(TroopCombat.Faction.Friendly);
        if (IsWithinHorizontalRadius(footprint.center, gateInside, gateArrivalRadius + footprint.extents.magnitude))
        {
            float horizontalDistance = GetHorizontalDistanceToBounds(gateInside, footprint);
            if (horizontalDistance <= gateArrivalRadius)
            {
                return true;
            }
        }

        return false;
    }

    public Vector3 ClampOutsideFriendlyProtectedZone(Vector3 worldPosition)
    {
        if (!IsInFriendlyProtectedZone(worldPosition))
        {
            return worldPosition;
        }

        Vector3 gateOutside = GetGateOutsidePosition(TroopCombat.Faction.Friendly);
        Vector3 away = worldPosition - gateOutside;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f)
        {
            Transform camp = friendlyCamp;
            if (camp != null)
            {
                away = gateOutside - camp.position;
                away.y = 0f;
            }
        }

        if (away.sqrMagnitude < 0.0001f)
        {
            away = Vector3.forward;
        }

        away.Normalize();
        Vector3 clamped = gateOutside + away * Mathf.Max(gateArrivalRadius * 0.5f, 0.75f);
        clamped.y = worldPosition.y;
        return clamped;
    }

    public float GetFriendlyProtectedRadius()
    {
        return campZoneRadius + gateArrivalRadius;
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
        return IsAtGateOutside(worldPosition, faction);
    }

    public bool IsAtGateInside(Vector3 worldPosition, TroopCombat.Faction faction)
    {
        Vector3 gateInside = GetGateInsidePosition(faction);
        return IsWithinHorizontalRadius(worldPosition, gateInside, gateArrivalRadius);
    }

    public bool IsAtGateOutside(Vector3 worldPosition, TroopCombat.Faction faction)
    {
        Vector3 gateOutside = GetGateOutsidePosition(faction);
        return IsWithinHorizontalRadius(worldPosition, gateOutside, gateArrivalRadius);
    }

    public bool IsNearGateForOpening(Vector3 worldPosition, TroopCombat.Faction faction)
    {
        if (!HasGate(faction))
        {
            return false;
        }

        if (IsWithinHorizontalRadius(worldPosition, GetGateOutsidePosition(faction), gateOpenRadius))
        {
            return true;
        }

        if (IsWithinHorizontalRadius(worldPosition, GetGateInsidePosition(faction), gateOpenRadius))
        {
            return true;
        }

        Transform gate = GetGateTransform(faction);
        return gate != null && IsWithinHorizontalRadius(worldPosition, gate.position, gateOpenRadius);
    }

    /// <summary>
    /// True while a regiment is still under/through the gate arch (tighter than <see cref="gateOpenRadius"/>).
    /// Used so retreat can keep the gate open after switching to the camp phase at the inside waypoint.
    /// </summary>
    public bool IsInGatePassage(Vector3 worldPosition, TroopCombat.Faction faction)
    {
        if (!HasGate(faction))
        {
            return false;
        }

        Vector3 inside = GetGateInsidePosition(faction);
        Vector3 outside = GetGateOutsidePosition(faction);
        float passageRadius = Mathf.Max(gateArrivalRadius * 2.75f, 3.5f);

        if (IsWithinHorizontalRadius(worldPosition, outside, passageRadius)
            || IsWithinHorizontalRadius(worldPosition, inside, passageRadius))
        {
            return true;
        }

        Transform gate = GetGateTransform(faction);
        if (gate != null && IsWithinHorizontalRadius(worldPosition, gate.position, passageRadius))
        {
            return true;
        }

        Vector3 span = outside - inside;
        span.y = 0f;
        float spanLength = span.magnitude;
        if (spanLength < 0.05f)
        {
            return false;
        }

        Vector3 direction = span / spanLength;
        Vector3 fromInside = worldPosition - inside;
        fromInside.y = 0f;
        float along = Vector3.Dot(fromInside, direction);
        if (along < -passageRadius * 0.35f || along > spanLength + passageRadius * 0.35f)
        {
            return false;
        }

        Vector3 closest = inside + direction * Mathf.Clamp(along, 0f, spanLength);
        return IsWithinHorizontalRadius(worldPosition, closest, passageRadius);
    }

    private Transform GetCampTransform(TroopCombat.Faction faction)
    {
        return faction == TroopCombat.Faction.Friendly ? friendlyCamp : enemyCamp;
    }

    private Transform GetGateTransform(TroopCombat.Faction faction)
    {
        return faction == TroopCombat.Faction.Friendly ? friendlyGate : enemyGate;
    }

    private Transform GetGateInsideTransform(TroopCombat.Faction faction)
    {
        return faction == TroopCombat.Faction.Friendly ? friendlyGateInside : enemyGateInside;
    }

    private Transform GetGateOutsideTransform(TroopCombat.Faction faction)
    {
        return faction == TroopCombat.Faction.Friendly ? friendlyGateOutside : enemyGateOutside;
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
        DrawGateGizmo(friendlyGateOutside != null ? friendlyGateOutside : friendlyGate, friendlyGateColor);
        DrawGateGizmo(enemyGateOutside != null ? enemyGateOutside : enemyGate, enemyGateColor);
        DrawGateInsideGizmo(friendlyGateInside, friendlyGateColor);
        DrawGateInsideGizmo(enemyGateInside, enemyGateColor);

        DrawGateRouteGizmo(
            friendlyCamp,
            friendlyGateInside,
            friendlyGateOutside != null ? friendlyGateOutside : friendlyGate,
            friendlyGateColor);
        DrawGateRouteGizmo(
            enemyCamp,
            enemyGateInside,
            enemyGateOutside != null ? enemyGateOutside : enemyGate,
            enemyGateColor);
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

    private void DrawGateInsideGizmo(Transform gateInside, Color color)
    {
        if (gateInside == null)
        {
            return;
        }

        Gizmos.color = new Color(color.r, color.g, color.b, 0.65f);
        Gizmos.DrawWireSphere(gateInside.position, gateArrivalRadius * 0.75f);
        Gizmos.DrawLine(gateInside.position, gateInside.position + gateInside.forward * 1f);
    }

    private static void DrawGateRouteGizmo(Transform camp, Transform gateInside, Transform gateOutside, Color color)
    {
        if (camp == null)
        {
            return;
        }

        Gizmos.color = color;
        Vector3 previous = camp.position;

        if (gateInside != null)
        {
            Gizmos.DrawLine(previous, gateInside.position);
            previous = gateInside.position;
        }

        if (gateOutside != null)
        {
            Gizmos.DrawLine(previous, gateOutside.position);
        }
    }
}
