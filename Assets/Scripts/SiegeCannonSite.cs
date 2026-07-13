using UnityEngine;

/// <summary>
/// Tracks enemy occupation of a friendly cannon. Continuous occupation for the configured duration triggers defeat.
/// </summary>
public class SiegeCannonSite : MonoBehaviour
{
    [Header("Zone")]
    [SerializeField] private Collider occupationZone;
    [SerializeField, Min(0.1f)] private float occupationRadius = 3f;
    [SerializeField] private LayerMask enemyRegimentLayers = ~0;

    [Header("Debug")]
    [SerializeField] private bool drawDebugGizmos = true;
    [SerializeField] private Color safeGizmoColor = new Color(0.2f, 0.75f, 1f, 0.35f);
    [SerializeField] private Color dangerGizmoColor = new Color(1f, 0.25f, 0.15f, 0.55f);

    private float occupationTime;
    private bool hasTriggeredDefeat;

    public float OccupationTime => occupationTime;
    public float OccupationProgress
    {
        get
        {
            SiegeGameManager manager = SiegeGameManager.Instance;
            float required = manager != null ? manager.CannonOccupationLossSeconds : 5f;
            return required <= 0f ? 0f : Mathf.Clamp01(occupationTime / required);
        }
    }

    public bool IsUnderAttack => occupationTime > 0f;

    public void ResetForMatchStart()
    {
        occupationTime = 0f;
        hasTriggeredDefeat = false;
    }

    private void Awake()
    {
        if (occupationZone == null)
        {
            occupationZone = GetComponent<Collider>();
        }
    }

    private void Update()
    {
        if (hasTriggeredDefeat)
        {
            return;
        }

        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager == null || !manager.IsPlaying || SiegeMatchSettings.IsDodgeArrowsMode)
        {
            return;
        }

        if (IsEnemyOccupying())
        {
            occupationTime += Time.deltaTime;
            if (occupationTime >= manager.CannonOccupationLossSeconds)
            {
                hasTriggeredDefeat = true;
                manager.NotifyCannonOccupied(this);
            }
        }
        else
        {
            occupationTime = 0f;
        }
    }

    private bool IsEnemyOccupying()
    {
        Vector3 center = GetZoneCenter();
        float radius = GetZoneRadius();

        Collider[] hits = Physics.OverlapSphere(center, radius, enemyRegimentLayers, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null)
            {
                continue;
            }

            TroopCombat regiment = hit.GetComponentInParent<TroopCombat>();
            if (regiment == null
                || regiment.TroopFaction != TroopCombat.Faction.Enemy
                || regiment.CurrentState == TroopCombat.State.Dead
                || regiment.IsRegrouping
                || regiment.IsRetreating)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private Vector3 GetZoneCenter()
    {
        if (occupationZone != null)
        {
            return occupationZone.bounds.center;
        }

        return transform.position;
    }

    private float GetZoneRadius()
    {
        if (occupationZone != null)
        {
            Bounds bounds = occupationZone.bounds;
            return Mathf.Max(bounds.extents.x, bounds.extents.z, occupationRadius * 0.5f);
        }

        return occupationRadius;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos)
        {
            return;
        }

        Gizmos.color = occupationTime > 0f ? dangerGizmoColor : safeGizmoColor;
        Gizmos.DrawWireSphere(GetZoneCenter(), GetZoneRadius());
    }
}
