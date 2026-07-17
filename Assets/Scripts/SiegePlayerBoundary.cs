using UnityEngine;

/// <summary>
/// Keeps the Votanic player inside an assigned boundary volume.
/// Assign a BoxCollider (trigger or solid) that defines the playable area.
/// </summary>
[DefaultExecutionOrder(200)]
public class SiegePlayerBoundary : MonoBehaviour
{
    [Header("Boundary Volume")]
    [Tooltip("BoxCollider or other collider that defines the playable area. Use a trigger for soft clamping.")]
    [SerializeField] private Collider boundaryCollider;
    [Tooltip("Shrinks the allowed area inward so the player body stays away from walls.")]
    [SerializeField, Min(0f)] private float boundaryPadding = 0.45f;
    [Tooltip("Only clamp X/Z by default. Leave Y alone so Votanic locomotion is not fighting vertical physics.")]
    [SerializeField] private bool clampHorizontalOnly = true;
    [SerializeField] private bool clampVertical = false;
    [SerializeField, Min(0f)] private float maxVerticalOffset = 0.35f;

    [Header("Response")]
    [SerializeField] private bool clampUserEveryFrame = false;
    [Tooltip("When enabled, walking off the platform kills the commander (SiegeCommandTowerFallDeath) instead of clamping/respawning.")]
    [SerializeField] private bool allowFallOffTowerDeath = true;
    [SerializeField] private bool onlyInTrackedXr = true;
    [Tooltip("Wait until Votanic user exists and this many seconds have passed before enforcing.")]
    [SerializeField, Min(0f)] private float enforcementDelaySeconds = 2f;
    [Tooltip("Optional point to snap the player to when they leave the boundary.")]
    [SerializeField] private Transform respawnPoint;
    [SerializeField, Min(0f)] private float respawnCooldown = 0.75f;

    [Header("Blocking Walls")]
    [Tooltip("Off by default. Enable only after verifying the boundary box is aligned with the floor.")]
    [SerializeField] private bool autoCreateBlockingWalls = false;
    [SerializeField] private Transform blockingWallsRoot;
    [SerializeField, Min(0.05f)] private float blockingWallThickness = 0.5f;
    [SerializeField] private string blockingWallLayerName = "Default";

    [Header("Siege PVP")]
    [Tooltip("When enabled, this volume is treated as the attacker command-tower play space and is released for the city defender during Siege PVP.")]
    [SerializeField] private bool attackerCommandTowerVolume = false;

    private float lastRespawnTime = -999f;
    private bool blockingWallsEnabled = true;
    private float enforcementStartTime = -999f;
    private bool enforcementStartTimeInitialized;
    private Bounds allowedBounds;
    private bool hasAllowedBounds;
    private static readonly System.Collections.Generic.HashSet<Collider> RegisteredBoundaryColliders =
        new System.Collections.Generic.HashSet<Collider>();

    public static bool IsBoundaryCollider(Collider collider)
    {
        if (collider == null)
        {
            return false;
        }

        if (RegisteredBoundaryColliders.Contains(collider))
        {
            return true;
        }

        return collider.GetComponentInParent<SiegePlayerBoundary>() != null;
    }

    public bool IsCommandTowerPlayArea => IsAttackerCommandTowerVolume();

    /// <summary>Random ground point anywhere inside this boundary volume (used for tower-wide punish shots).</summary>
    public bool TrySampleRandomPlayAreaGroundPoint(out Vector3 point)
    {
        point = Vector3.zero;
        ResolveBoundaryCollider();
        if (boundaryCollider == null)
        {
            return false;
        }

        Bounds bounds = boundaryCollider.bounds;
        if (bounds.size.x <= 0.05f || bounds.size.z <= 0.05f)
        {
            return false;
        }

        float x = Random.Range(bounds.min.x, bounds.max.x);
        float z = Random.Range(bounds.min.z, bounds.max.z);
        float y = bounds.min.y + 0.05f;
        point = new Vector3(x, y, z);
        return IsFinitePosition(point);
    }

    /// <summary>Random ground point on the command tower play area (not the player dodge ring).</summary>
    public static bool TrySampleRandomCommandTowerGroundPoint(out Vector3 point)
    {
        point = Vector3.zero;
        SiegePlayerBoundary boundary = FindBestCommandTowerBoundary();
        if (boundary != null && boundary.TrySampleRandomPlayAreaGroundPoint(out point))
        {
            return true;
        }

        SiegeCommandTowerFallDeath[] fallDeathVolumes = FindObjectsOfType<SiegeCommandTowerFallDeath>(true);
        for (int i = 0; i < fallDeathVolumes.Length; i++)
        {
            SiegeCommandTowerFallDeath fallDeath = fallDeathVolumes[i];
            if (fallDeath != null && fallDeath.TrySampleRandomTowerFloorPoint(out point))
            {
                return true;
            }
        }

        return false;
    }

    private static SiegePlayerBoundary FindBestCommandTowerBoundary()
    {
        SiegePlayerBoundary[] boundaries = FindObjectsOfType<SiegePlayerBoundary>(true);
        SiegePlayerBoundary fallback = null;
        for (int i = 0; i < boundaries.Length; i++)
        {
            SiegePlayerBoundary candidate = boundaries[i];
            if (candidate == null)
            {
                continue;
            }

            if (fallback == null)
            {
                fallback = candidate;
            }

            if (candidate.IsCommandTowerPlayArea)
            {
                return candidate;
            }

            if (candidate.GetComponent<SiegeCommandTowerFallDeath>() != null
                || candidate.GetComponentInParent<SiegeCommandTowerFallDeath>() != null)
            {
                return candidate;
            }
        }

        return fallback;
    }

    private static bool IsFinitePosition(Vector3 position)
    {
        return float.IsFinite(position.x)
            && float.IsFinite(position.y)
            && float.IsFinite(position.z);
    }

    /// <summary>
    /// Immediately disable command-tower blocking walls before a defender teleport in tracked XR.
    /// </summary>
    public void ForceReleaseForPvpDefender()
    {
        if (!IsAttackerCommandTowerVolume())
        {
            return;
        }

        blockingWallsEnabled = false;
        SetBlockingWallsEnabled(false);
    }

    /// <summary>
    /// Re-enable tower walls after the defender returns to the menu spawn.
    /// </summary>
    public void RestoreAfterPvpDefender()
    {
        if (!IsAttackerCommandTowerVolume())
        {
            return;
        }

        blockingWallsEnabled = true;
        SetBlockingWallsEnabled(true);
    }

    private void Awake()
    {
        ResolveBoundaryCollider();
        RebuildAllowedBounds();
        RegisterBoundaryColliders();
    }

    private void OnDestroy()
    {
        UnregisterBoundaryColliders();
    }

    private void Start()
    {
        enforcementStartTime = Time.time;
        enforcementStartTimeInitialized = true;

        if (autoCreateBlockingWalls)
        {
            EnsureBlockingWalls();
        }

        RegisterBoundaryColliders();
    }

    private void LateUpdate()
    {
        SyncBlockingWallsForPvpDefender();

        if (!clampUserEveryFrame || !ShouldEnforceBoundary())
        {
            return;
        }

        Transform userTransform = ResolveUserTransform();
        if (userTransform == null)
        {
            return;
        }

        if (!TryClampPosition(userTransform.position, out Vector3 clampedPosition))
        {
            return;
        }

        ApplyClampedPosition(userTransform, clampedPosition);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!ShouldEnforceBoundary() || boundaryCollider == null || !boundaryCollider.isTrigger)
        {
            return;
        }

        Transform userTransform = ResolveUserTransform();
        if (userTransform == null || other.transform != userTransform && !other.transform.IsChildOf(userTransform))
        {
            return;
        }

        if (Time.time < lastRespawnTime + respawnCooldown)
        {
            return;
        }

        lastRespawnTime = Time.time;
        if (respawnPoint != null)
        {
            userTransform.SetPositionAndRotation(respawnPoint.position, respawnPoint.rotation);
            return;
        }

        if (TryClampPosition(userTransform.position, out Vector3 clampedPosition))
        {
            ApplyClampedPosition(userTransform, clampedPosition);
        }
    }

    private bool ShouldEnforceBoundary()
    {
        if (ShouldIgnoreForPvpDefender())
        {
            return false;
        }

        if (allowFallOffTowerDeath)
        {
            SiegeGameManager manager = SiegeGameManager.Instance;
            if (manager != null && manager.IsPlaying)
            {
                // Let SiegeCommandTowerFallDeath handle leaving the tower during play.
                return false;
            }
        }

        if (onlyInTrackedXr && !SiegePlayEnvironment.IsTrackedXr)
        {
            return false;
        }

        if (!enforcementStartTimeInitialized)
        {
            enforcementStartTime = Time.time;
            enforcementStartTimeInitialized = true;
        }

        if (Time.time < enforcementStartTime + enforcementDelaySeconds)
        {
            return false;
        }

        return ResolveUserTransform() != null;
    }

    private bool ShouldIgnoreForPvpDefender()
    {
        if (!IsAttackerCommandTowerVolume() || !SiegeMatchSettings.IsSiegePvpMode)
        {
            return false;
        }

        SiegePvpSession pvp = SiegePvpSession.Instance;
        return pvp != null && pvp.ShouldKeepDefenderOffCommandTower();
    }

    private bool IsAttackerCommandTowerVolume()
    {
        if (attackerCommandTowerVolume)
        {
            return true;
        }

        return GetComponent<SiegeCommandTowerFallDeath>() != null
            || GetComponentInParent<SiegeCommandTowerFallDeath>() != null;
    }

    private void SyncBlockingWallsForPvpDefender()
    {
        bool shouldEnable = !ShouldIgnoreForPvpDefender();
        if (shouldEnable == blockingWallsEnabled)
        {
            return;
        }

        blockingWallsEnabled = shouldEnable;
        SetBlockingWallsEnabled(shouldEnable);
    }

    private void SetBlockingWallsEnabled(bool enabled)
    {
        if (blockingWallsRoot != null)
        {
            Collider[] wallColliders = blockingWallsRoot.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < wallColliders.Length; i++)
            {
                if (wallColliders[i] != null)
                {
                    wallColliders[i].enabled = enabled;
                }
            }
        }

        if (boundaryCollider != null && !boundaryCollider.isTrigger)
        {
            boundaryCollider.enabled = enabled;
        }
    }

    private void ApplyClampedPosition(Transform userTransform, Vector3 clampedPosition)
    {
        if (clampHorizontalOnly)
        {
            Vector3 current = userTransform.position;
            userTransform.position = new Vector3(clampedPosition.x, current.y, clampedPosition.z);
            return;
        }

        userTransform.position = clampedPosition;
    }

    private void ResolveBoundaryCollider()
    {
        if (boundaryCollider != null)
        {
            return;
        }

        boundaryCollider = GetComponent<Collider>();
    }

    private void RebuildAllowedBounds()
    {
        if (boundaryCollider == null)
        {
            hasAllowedBounds = false;
            return;
        }

        allowedBounds = boundaryCollider.bounds;
        if (boundaryPadding > 0f)
        {
            allowedBounds.Expand(-boundaryPadding * 2f);
        }

        hasAllowedBounds = allowedBounds.size.x > 0.05f && allowedBounds.size.z > 0.05f;
    }

    private bool TryClampPosition(Vector3 position, out Vector3 clampedPosition)
    {
        clampedPosition = position;
        if (!hasAllowedBounds)
        {
            RebuildAllowedBounds();
            if (!hasAllowedBounds)
            {
                return false;
            }
        }

        bool insideX = position.x >= allowedBounds.min.x && position.x <= allowedBounds.max.x;
        bool insideZ = position.z >= allowedBounds.min.z && position.z <= allowedBounds.max.z;
        bool insideY = position.y >= allowedBounds.min.y && position.y <= allowedBounds.max.y;

        if (clampHorizontalOnly)
        {
            if (insideX && insideZ)
            {
                return false;
            }

            clampedPosition = new Vector3(
                Mathf.Clamp(position.x, allowedBounds.min.x, allowedBounds.max.x),
                position.y,
                Mathf.Clamp(position.z, allowedBounds.min.z, allowedBounds.max.z));
            return true;
        }

        if (insideX && insideZ && insideY)
        {
            return false;
        }

        clampedPosition = new Vector3(
            Mathf.Clamp(position.x, allowedBounds.min.x, allowedBounds.max.x),
            Mathf.Clamp(position.y, allowedBounds.min.y, allowedBounds.max.y),
            Mathf.Clamp(position.z, allowedBounds.min.z, allowedBounds.max.z));

        if (clampVertical)
        {
            float centerY = allowedBounds.center.y;
            clampedPosition.y = Mathf.Clamp(clampedPosition.y, centerY - maxVerticalOffset, centerY + maxVerticalOffset);
        }

        return true;
    }

    private static Transform ResolveUserTransform()
    {
        Transform user = SiegePlayEnvironment.ResolveUserTransform();
        if (user != null)
        {
            return user;
        }

        return SiegePlayEnvironment.ResolvePlayerTransform();
    }

    private void EnsureBlockingWalls()
    {
        if (boundaryCollider == null || !(boundaryCollider is BoxCollider boxCollider))
        {
            return;
        }

        if (blockingWallsRoot == null)
        {
            GameObject rootObject = new GameObject("PlayerBoundaryWalls");
            rootObject.transform.SetParent(transform, false);
            blockingWallsRoot = rootObject.transform;
        }

        for (int i = blockingWallsRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(blockingWallsRoot.GetChild(i).gameObject);
        }

        int layer = LayerMask.NameToLayer(blockingWallLayerName);
        if (layer < 0)
        {
            layer = gameObject.layer;
        }

        Vector3 center = boxCollider.center;
        Vector3 size = boxCollider.size;
        Transform boundaryTransform = boundaryCollider.transform;
        float thickness = blockingWallThickness;

        CreateWall(
            "BoundaryWall_North",
            boundaryTransform,
            center + new Vector3(0f, 0f, size.z * 0.5f + thickness * 0.5f),
            new Vector3(size.x + thickness * 2f, size.y, thickness),
            layer);
        CreateWall(
            "BoundaryWall_South",
            boundaryTransform,
            center + new Vector3(0f, 0f, -size.z * 0.5f - thickness * 0.5f),
            new Vector3(size.x + thickness * 2f, size.y, thickness),
            layer);
        CreateWall(
            "BoundaryWall_East",
            boundaryTransform,
            center + new Vector3(size.x * 0.5f + thickness * 0.5f, 0f, 0f),
            new Vector3(thickness, size.y, size.z + thickness * 2f),
            layer);
        CreateWall(
            "BoundaryWall_West",
            boundaryTransform,
            center + new Vector3(-size.x * 0.5f - thickness * 0.5f, 0f, 0f),
            new Vector3(thickness, size.y, size.z + thickness * 2f),
            layer);
    }

    private void CreateWall(string wallName, Transform parent, Vector3 localCenter, Vector3 localSize, int layer)
    {
        GameObject wallObject = new GameObject(wallName);
        wallObject.transform.SetParent(blockingWallsRoot, false);
        wallObject.transform.localPosition = parent.localPosition;
        wallObject.transform.localRotation = parent.localRotation;
        wallObject.transform.localScale = parent.localScale;
        wallObject.layer = layer;

        GameObject colliderObject = new GameObject("Collider");
        colliderObject.transform.SetParent(wallObject.transform, false);
        colliderObject.transform.localPosition = localCenter;
        colliderObject.transform.localRotation = Quaternion.identity;
        colliderObject.transform.localScale = Vector3.one;
        colliderObject.layer = layer;

        BoxCollider wallCollider = colliderObject.AddComponent<BoxCollider>();
        wallCollider.size = localSize;
        wallCollider.isTrigger = false;
        RegisteredBoundaryColliders.Add(wallCollider);
    }

    private void RegisterBoundaryColliders()
    {
        if (boundaryCollider != null)
        {
            RegisteredBoundaryColliders.Add(boundaryCollider);
        }

        if (blockingWallsRoot == null)
        {
            return;
        }

        Collider[] wallColliders = blockingWallsRoot.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < wallColliders.Length; i++)
        {
            if (wallColliders[i] != null)
            {
                RegisteredBoundaryColliders.Add(wallColliders[i]);
            }
        }
    }

    private void UnregisterBoundaryColliders()
    {
        if (boundaryCollider != null)
        {
            RegisteredBoundaryColliders.Remove(boundaryCollider);
        }

        if (blockingWallsRoot == null)
        {
            return;
        }

        Collider[] wallColliders = blockingWallsRoot.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < wallColliders.Length; i++)
        {
            if (wallColliders[i] != null)
            {
                RegisteredBoundaryColliders.Remove(wallColliders[i]);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        ResolveBoundaryCollider();
        RebuildAllowedBounds();
        if (!hasAllowedBounds)
        {
            return;
        }

        Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.35f);
        Gizmos.DrawWireCube(allowedBounds.center, allowedBounds.size);
    }
}
