using UnityEngine;

/// <summary>
/// Animates a city gate up while troops enter or leave through it, and keeps it closed otherwise.
/// Attach to the moving gate mesh/pivot and assign the transform that should rise on the local Z axis.
/// When open, disables the moving gate colliders AND nearby RTS_Solid (static roofs/arches) in the corridor.
/// </summary>
public class RtsCityGateController : MonoBehaviour
{
    [Header("Gate")]
    [SerializeField] private Transform gateTransform;
    [SerializeField] private TroopCombat.Faction gateFaction = TroopCombat.Faction.Enemy;

    [Header("Animation")]
    [Tooltip("How far the gate rises on local Z when open.")]
    [SerializeField] private float openHeight = -4f;
    [SerializeField, Min(0.1f)] private float moveSpeed = 6f;
    [SerializeField, Min(0.05f)] private float troopScanInterval = 0.15f;
    [Tooltip("When open this far (0-1), solid colliders on the gate are disabled so troops can pass.")]
    [SerializeField, Range(0.1f, 1f)] private float passableOpenAmount = 0.35f;
    [Tooltip("Keep the gate fully open at least this long after the last troop no longer needs it.")]
    [SerializeField, Min(0f)] private float closeHoldSeconds = 1.25f;

    [Header("Corridor Solids")]
    [Tooltip("Also disable RTS_Solid colliders near the gate opening (static roofs/arches not parented to the moving gate).")]
    [SerializeField] private bool disableCorridorSolidsWhenOpen = true;
    [SerializeField, Min(0.5f)] private float corridorSolidRadius = 4f;
    [SerializeField, Min(1f)] private float corridorSolidHeight = 8f;

    private Vector3 closedLocalPosition;
    private float openLocalZ;
    private float currentOpenAmount;
    private float nextTroopScanTime;
    private bool shouldBeOpen;
    private float keepOpenUntilTime;
    private Collider[] gateColliders;
    private bool[] gateColliderWasEnabled;
    private bool gateCollidersPassable;
    private Collider[] corridorSolidColliders;
    private bool[] corridorSolidWasEnabled;

    /// <summary>True once the gate has risen enough for regiments to path through.</summary>
    public bool IsPassable => currentOpenAmount >= passableOpenAmount;

    public TroopCombat.Faction GateFaction => gateFaction;

    public Vector3 GateWorldPosition
    {
        get
        {
            if (gateTransform != null)
            {
                return gateTransform.position;
            }

            return transform.position;
        }
    }

    public void ResetForMatchStart()
    {
        currentOpenAmount = 0f;
        shouldBeOpen = false;
        keepOpenUntilTime = 0f;
        nextTroopScanTime = 0f;

        if (gateTransform != null)
        {
            gateTransform.localPosition = closedLocalPosition;
        }

        SetGateCollidersPassable(false);
    }

    private void Awake()
    {
        if (gateTransform == null)
        {
            gateTransform = transform;
        }

        closedLocalPosition = gateTransform.localPosition;
        openLocalZ = closedLocalPosition.z + openHeight;
        CacheGateColliders();
    }

    private void OnValidate()
    {
        moveSpeed = Mathf.Max(0.1f, moveSpeed);
        troopScanInterval = Mathf.Max(0.05f, troopScanInterval);
        passableOpenAmount = Mathf.Clamp(passableOpenAmount, 0.1f, 1f);
        closeHoldSeconds = Mathf.Max(0f, closeHoldSeconds);
        corridorSolidRadius = Mathf.Max(0.5f, corridorSolidRadius);
        corridorSolidHeight = Mathf.Max(1f, corridorSolidHeight);
    }

    private void Update()
    {
        if (Time.time >= nextTroopScanTime)
        {
            nextTroopScanTime = Time.time + troopScanInterval;
            bool needsOpen = EvaluateShouldBeOpen();
            if (needsOpen)
            {
                shouldBeOpen = true;
                keepOpenUntilTime = Time.time + closeHoldSeconds;
            }
            else if (Time.time >= keepOpenUntilTime)
            {
                shouldBeOpen = false;
            }
        }

        float targetOpenAmount = shouldBeOpen ? 1f : 0f;
        currentOpenAmount = Mathf.MoveTowards(currentOpenAmount, targetOpenAmount, moveSpeed * Time.deltaTime);

        Vector3 localPosition = gateTransform.localPosition;
        localPosition.z = Mathf.Lerp(closedLocalPosition.z, openLocalZ, currentOpenAmount);
        gateTransform.localPosition = localPosition;

        SetGateCollidersPassable(IsPassable);
    }

    private void CacheGateColliders()
    {
        // Include the controller root AND the moving gate — roofs are often siblings of the door mesh.
        System.Collections.Generic.List<Collider> collected = new System.Collections.Generic.List<Collider>();
        CollectColliders(transform, collected);
        if (gateTransform != null && gateTransform != transform)
        {
            CollectColliders(gateTransform, collected);
        }

        gateColliders = collected.ToArray();
        gateColliderWasEnabled = new bool[gateColliders.Length];
        for (int i = 0; i < gateColliders.Length; i++)
        {
            gateColliderWasEnabled[i] = gateColliders[i] != null && gateColliders[i].enabled;
        }
    }

    private static void CollectColliders(Transform root, System.Collections.Generic.List<Collider> into)
    {
        if (root == null)
        {
            return;
        }

        Collider[] found = root.GetComponentsInChildren<Collider>(includeInactive: true);
        for (int i = 0; i < found.Length; i++)
        {
            Collider candidate = found[i];
            if (candidate == null || into.Contains(candidate))
            {
                continue;
            }

            into.Add(candidate);
        }
    }

    private void CacheCorridorSolidColliders()
    {
        int solidLayer = LayerMask.NameToLayer("RTS_Solid");
        if (solidLayer < 0)
        {
            corridorSolidColliders = System.Array.Empty<Collider>();
            corridorSolidWasEnabled = System.Array.Empty<bool>();
            return;
        }

        Vector3 center = ResolveCorridorCenter();
        Vector3 halfExtents = new Vector3(corridorSolidRadius, corridorSolidHeight * 0.5f, corridorSolidRadius);
        Collider[] hits = Physics.OverlapBox(
            center + Vector3.up * (corridorSolidHeight * 0.25f),
            halfExtents,
            Quaternion.identity,
            1 << solidLayer,
            QueryTriggerInteraction.Ignore);

        System.Collections.Generic.List<Collider> solids = new System.Collections.Generic.List<Collider>();
        if (hits != null)
        {
            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                if (hit == null)
                {
                    continue;
                }

                // Skip moving-gate colliders already tracked.
                if (IsTrackedGateCollider(hit))
                {
                    continue;
                }

                solids.Add(hit);
            }
        }

        corridorSolidColliders = solids.ToArray();
        corridorSolidWasEnabled = new bool[corridorSolidColliders.Length];
        for (int i = 0; i < corridorSolidColliders.Length; i++)
        {
            corridorSolidWasEnabled[i] = corridorSolidColliders[i] != null && corridorSolidColliders[i].enabled;
        }
    }

    private Vector3 ResolveCorridorCenter()
    {
        RtsCampManager camps = RtsCampManager.Instance;
        if (camps != null)
        {
            Vector3 inside = camps.GetGateInsidePosition(gateFaction);
            Vector3 outside = camps.GetGateOutsidePosition(gateFaction);
            return (inside + outside) * 0.5f;
        }

        return GateWorldPosition;
    }

    private bool IsTrackedGateCollider(Collider candidate)
    {
        if (gateColliders == null || candidate == null)
        {
            return false;
        }

        for (int i = 0; i < gateColliders.Length; i++)
        {
            if (gateColliders[i] == candidate)
            {
                return true;
            }
        }

        return false;
    }

    private void SetGateCollidersPassable(bool passable)
    {
        if (gateCollidersPassable == passable)
        {
            return;
        }

        gateCollidersPassable = passable;
        if (gateColliders == null || gateColliders.Length == 0)
        {
            CacheGateColliders();
        }

        SetColliderGroupEnabled(gateColliders, gateColliderWasEnabled, enabled: !passable);

        if (disableCorridorSolidsWhenOpen)
        {
            if (passable)
            {
                CacheCorridorSolidColliders();
                SetColliderGroupEnabled(corridorSolidColliders, corridorSolidWasEnabled, enabled: false);
            }
            else if (corridorSolidColliders != null)
            {
                RestoreColliderGroup(corridorSolidColliders, corridorSolidWasEnabled);
            }
        }

        Physics.SyncTransforms();
    }

    private static void SetColliderGroupEnabled(Collider[] colliders, bool[] wasEnabled, bool enabled)
    {
        if (colliders == null)
        {
            return;
        }

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null)
            {
                continue;
            }

            if (!enabled)
            {
                if (wasEnabled != null && i < wasEnabled.Length)
                {
                    wasEnabled[i] = collider.enabled;
                }

                collider.enabled = false;
            }
            else if (wasEnabled != null && i < wasEnabled.Length)
            {
                collider.enabled = wasEnabled[i];
            }
        }
    }

    private static void RestoreColliderGroup(Collider[] colliders, bool[] wasEnabled)
    {
        if (colliders == null || wasEnabled == null)
        {
            return;
        }

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null && i < wasEnabled.Length)
            {
                colliders[i].enabled = wasEnabled[i];
            }
        }
    }

    /// <summary>Find the gate controller for a faction, if any.</summary>
    public static RtsCityGateController FindForFaction(TroopCombat.Faction faction)
    {
        RtsCityGateController[] gates = FindObjectsByType<RtsCityGateController>(FindObjectsSortMode.None);
        for (int i = 0; i < gates.Length; i++)
        {
            if (gates[i] != null && gates[i].gateFaction == faction)
            {
                return gates[i];
            }
        }

        return null;
    }

    private bool EvaluateShouldBeOpen()
    {
        TroopCombat[] troops = FindObjectsByType<TroopCombat>(FindObjectsSortMode.None);
        for (int i = 0; i < troops.Length; i++)
        {
            TroopCombat troop = troops[i];
            if (troop == null || troop.TroopFaction != gateFaction || troop.CurrentState == TroopCombat.State.Dead)
            {
                continue;
            }

            if (troop.IsTraversingGate)
            {
                return true;
            }

            EnemyRegimentAI regimentAi = troop.GetComponent<EnemyRegimentAI>();
            if (regimentAi != null && regimentAi.IsExitingGate)
            {
                return true;
            }
        }

        return false;
    }
}
