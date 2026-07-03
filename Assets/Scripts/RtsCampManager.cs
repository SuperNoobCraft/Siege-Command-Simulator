using UnityEngine;

/// <summary>
/// Assign camp safe zones for both factions. Attach to a GameManager (or any scene root) object.
/// </summary>
public class RtsCampManager : MonoBehaviour
{
    public static RtsCampManager Instance { get; private set; }

    [Header("Camp Locations")]
    [SerializeField] private Transform friendlyCamp;
    [SerializeField] private Transform enemyCamp;

    [Header("Camp Zone")]
    [SerializeField, Min(0.1f)] private float campArrivalRadius = 3f;

    [Header("Debug")]
    [SerializeField] private bool drawDebugGizmos = true;
    [SerializeField] private Color friendlyCampColor = new Color(0.2f, 0.55f, 1f, 0.35f);
    [SerializeField] private Color enemyCampColor = new Color(1f, 0.3f, 0.25f, 0.35f);

    public float CampArrivalRadius => campArrivalRadius;
    public Transform FriendlyCamp => friendlyCamp;
    public Transform EnemyCamp => enemyCamp;

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

    public Vector3 GetCampPosition(TroopCombat.Faction faction)
    {
        Transform camp = faction == TroopCombat.Faction.Friendly ? friendlyCamp : enemyCamp;
        if (camp == null)
        {
            Debug.LogWarning("No camp assigned for faction " + faction + ".", this);
            return transform.position;
        }

        return camp.position;
    }

    public bool IsAtCamp(Vector3 worldPosition, TroopCombat.Faction faction)
    {
        Vector3 campPosition = GetCampPosition(faction);
        Vector3 offset = worldPosition - campPosition;
        offset.y = 0f;
        return offset.sqrMagnitude <= campArrivalRadius * campArrivalRadius;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos)
        {
            return;
        }

        DrawCampGizmo(friendlyCamp, friendlyCampColor);
        DrawCampGizmo(enemyCamp, enemyCampColor);
    }

    private void DrawCampGizmo(Transform camp, Color fillColor)
    {
        if (camp == null)
        {
            return;
        }

        Gizmos.color = fillColor;
        Gizmos.DrawSphere(camp.position, campArrivalRadius);
        Gizmos.color = new Color(fillColor.r, fillColor.g, fillColor.b, 1f);
        Gizmos.DrawWireSphere(camp.position, campArrivalRadius);
    }
}
