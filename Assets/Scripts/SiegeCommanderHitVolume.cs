using UnityEngine;

/// <summary>
/// Marks a collider on the commander rig as hittable by enemy arrows.
/// Add to the user body / frame collider if SiegeCommanderArrowHealth lives on a parent.
/// </summary>
public class SiegeCommanderHitVolume : MonoBehaviour
{
    [SerializeField] private SiegeCommanderArrowHealth health;

    public SiegeCommanderArrowHealth Health => health != null ? health : SiegeCommanderArrowHealth.Instance;

    private void Awake()
    {
        if (health == null)
        {
            health = GetComponentInParent<SiegeCommanderArrowHealth>();
        }

        if (health == null)
        {
            health = SiegeCommanderArrowHealth.Instance;
        }
    }

    public bool TryRegisterHit(Vector3 hitPoint)
    {
        SiegeCommanderArrowHealth resolvedHealth = Health;
        if (resolvedHealth == null || !resolvedHealth.CanReceiveCommanderDamage)
        {
            return false;
        }

        resolvedHealth.RegisterArrowHit(hitPoint);
        return true;
    }
}
