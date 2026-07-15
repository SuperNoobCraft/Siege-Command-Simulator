using System;
using UnityEngine;
using UnityEngine.Rendering;

public class TroopRangedProjectile : MonoBehaviour
{
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private static MaterialPropertyBlock outlinePropertyBlock;

    private Vector3 startPosition;
    private Vector3 targetPosition;
    private float travelSpeed;
    private float arcHeight;
    private float traveledDistance;
    private float totalDistance;
    private Vector3 previousPosition;
    private bool isPlayerHazard;
    private float playerHazardHitRadius = 0.1f;
    private LayerMask playerHitLayers;
    private bool hasRegisteredHit;
    private bool isInitialized;
    private bool hasReportedCompletion;

    /// <summary>Args: world position, whether the projectile hit the commander.</summary>
    public event Action<Vector3, bool> Completed;

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
        LayerMask hitLayers,
        float hitRadius = 0.1f,
        bool enableOutline = false,
        Color outlineColor = default,
        float outlineScale = 1.14f)
    {
        TroopRangedProjectile projectile = Launch(prefab, start, target, speed, arcHeight);
        if (projectile != null)
        {
            projectile.ConfigurePlayerHazard(hitLayers, hitRadius, enableOutline, outlineColor, outlineScale);
        }

        return projectile;
    }

    public void Initialize(Vector3 start, Vector3 target, float speed, float arcHeight)
    {
        if (!IsValidPosition(start) || !IsValidPosition(target))
        {
            Debug.LogWarning("TroopRangedProjectile received an invalid start or target position.", this);
            Destroy(gameObject);
            return;
        }

        startPosition = start;
        targetPosition = target;
        travelSpeed = Mathf.Max(0.01f, speed);
        this.arcHeight = Mathf.Max(0f, arcHeight);
        traveledDistance = 0f;
        totalDistance = GetHorizontalDistance(startPosition, targetPosition);
        totalDistance = Mathf.Max(0.01f, totalDistance);
        previousPosition = startPosition;
        transform.position = startPosition;
        UpdateFacing(startPosition, GetPositionAtProgress(Mathf.Min(0.001f, 1f)));
        isInitialized = true;
    }

    public void ConfigurePlayerHazard(
        LayerMask hitLayers,
        float hitRadius = 0.1f,
        bool enableOutline = false,
        Color outlineColor = default,
        float outlineScale = 1.14f)
    {
        isPlayerHazard = true;
        playerHitLayers = hitLayers;
        playerHazardHitRadius = Mathf.Max(0.01f, hitRadius);
        EnsureHazardPhysics();

        if (enableOutline)
        {
            ApplyPlayerHazardOutline(outlineColor, outlineScale);
        }
    }

    private void Update()
    {
        if (!isInitialized)
        {
            return;
        }

        if (!IsValidPosition(startPosition) || !IsValidPosition(targetPosition))
        {
            Destroy(gameObject);
            return;
        }

        traveledDistance += travelSpeed * Time.deltaTime;
        float progress = Mathf.Clamp01(traveledDistance / totalDistance);
        Vector3 currentPosition = GetPositionAtProgress(progress);
        if (!IsValidPosition(currentPosition))
        {
            Destroy(gameObject);
            return;
        }

        if (isPlayerHazard)
        {
            if (TryDetectPlayerHit(previousPosition, currentPosition))
            {
                return;
            }
        }

        UpdateFacing(previousPosition, currentPosition);
        transform.position = currentPosition;
        previousPosition = currentPosition;

        if (progress >= 1f)
        {
            if (isPlayerHazard)
            {
                TryDetectPlayerOverlap(currentPosition);
                if (hasRegisteredHit)
                {
                    return;
                }
            }

            ReportCompleted(currentPosition, hitPlayer: false);
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

    private bool TryDetectPlayerHit(Vector3 from, Vector3 to)
    {
        if (hasRegisteredHit)
        {
            return true;
        }

        SiegeCommanderArrowHealth commanderHealth = SiegeCommanderArrowHealth.Instance;
        if (commanderHealth != null
            && commanderHealth.UsesCylinderDodgeHitTest
            && commanderHealth.TryEvaluateArrowSegmentHit(from, to, playerHazardHitRadius, out Vector3 hitPoint))
        {
            hasRegisteredHit = true;
            commanderHealth.RegisterArrowHit(hitPoint);
            ReportCompleted(hitPoint, hitPlayer: true);
            Destroy(gameObject);
            return true;
        }

        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
        {
            return false;
        }

        if (Physics.SphereCast(
                from,
                playerHazardHitRadius,
                delta.normalized,
                out RaycastHit hit,
                distance,
                playerHitLayers,
                QueryTriggerInteraction.Collide))
        {
            return TryRegisterPlayerHit(hit.collider);
        }

        return false;
    }

    private void TryDetectPlayerOverlap(Vector3 position)
    {
        if (hasRegisteredHit)
        {
            return;
        }

        SiegeCommanderArrowHealth commanderHealth = SiegeCommanderArrowHealth.Instance;
        if (commanderHealth != null
            && commanderHealth.UsesCylinderDodgeHitTest
            && commanderHealth.TryEvaluateArrowProximityHit(position, playerHazardHitRadius, out Vector3 hitPoint))
        {
            hasRegisteredHit = true;
            commanderHealth.RegisterArrowHit(hitPoint);
            ReportCompleted(hitPoint, hitPlayer: true);
            Destroy(gameObject);
            return;
        }

        Collider[] overlaps = Physics.OverlapSphere(
            position,
            playerHazardHitRadius,
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

        if (commanderHealth.UsesCylinderDodgeHitTest
            && !commanderHealth.TryEvaluateArrowProximityHit(transform.position, playerHazardHitRadius, out _))
        {
            return false;
        }

        hasRegisteredHit = true;
        commanderHealth.RegisterArrowHit(transform.position);
        ReportCompleted(transform.position, hitPlayer: true);
        Destroy(gameObject);
        return true;
    }

    private void ReportCompleted(Vector3 position, bool hitPlayer)
    {
        if (hasReportedCompletion)
        {
            return;
        }

        hasReportedCompletion = true;
        Completed?.Invoke(position, hitPlayer);
    }

    private void ApplyPlayerHazardOutline(Color outlineColor, float outlineScale)
    {
        if (outlineColor.a <= 0f)
        {
            outlineColor = new Color(1f, 0.12f, 0.12f, 1f);
        }

        outlineScale = Mathf.Max(1.01f, outlineScale);
        int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        Material redOutlineMaterial = CreateOutlineMaterial(outlineColor);

        if (outlinePropertyBlock == null)
        {
            outlinePropertyBlock = new MaterialPropertyBlock();
        }

        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter sourceFilter = meshFilters[i];
            if (sourceFilter == null
                || sourceFilter.sharedMesh == null
                || sourceFilter.name == "PlayerHazardOutline"
                || sourceFilter.transform.name == "PlayerHazardOutline")
            {
                continue;
            }

            MeshRenderer sourceRenderer = sourceFilter.GetComponent<MeshRenderer>();
            if (sourceRenderer == null || !sourceRenderer.enabled)
            {
                continue;
            }

            CreateMeshOutline(
                sourceFilter.transform,
                sourceFilter.sharedMesh,
                redOutlineMaterial,
                outlineColor,
                outlineScale,
                ignoreRaycastLayer >= 0 ? ignoreRaycastLayer : sourceFilter.gameObject.layer);
        }

        SkinnedMeshRenderer[] skinnedRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinnedRenderers.Length; i++)
        {
            SkinnedMeshRenderer sourceRenderer = skinnedRenderers[i];
            if (sourceRenderer == null
                || sourceRenderer.sharedMesh == null
                || !sourceRenderer.enabled
                || sourceRenderer.name == "PlayerHazardOutline")
            {
                continue;
            }

            CreateMeshOutline(
                sourceRenderer.transform,
                sourceRenderer.sharedMesh,
                redOutlineMaterial,
                outlineColor,
                outlineScale,
                ignoreRaycastLayer >= 0 ? ignoreRaycastLayer : sourceRenderer.gameObject.layer);
        }
    }

    private void CreateMeshOutline(
        Transform sourceTransform,
        Mesh mesh,
        Material outlineMat,
        Color outlineColor,
        float outlineScale,
        int layer)
    {
        GameObject outlineObject = new GameObject("PlayerHazardOutline");
        outlineObject.transform.SetParent(sourceTransform, false);
        outlineObject.transform.localPosition = Vector3.zero;
        outlineObject.transform.localRotation = Quaternion.identity;
        outlineObject.transform.localScale = Vector3.one * outlineScale;
        outlineObject.layer = layer;

        MeshFilter outlineFilter = outlineObject.AddComponent<MeshFilter>();
        outlineFilter.sharedMesh = mesh;

        MeshRenderer outlineRenderer = outlineObject.AddComponent<MeshRenderer>();
        outlineRenderer.sharedMaterial = outlineMat;
        outlineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        outlineRenderer.receiveShadows = false;
        outlineRenderer.allowOcclusionWhenDynamic = false;
        outlineRenderer.lightProbeUsage = LightProbeUsage.Off;
        outlineRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        outlinePropertyBlock.Clear();
        outlinePropertyBlock.SetColor(ColorId, outlineColor);
        outlineRenderer.SetPropertyBlock(outlinePropertyBlock);
    }

    private static Material CreateOutlineMaterial(Color outlineColor)
    {
        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        Material material = new Material(shader);
        material.color = outlineColor;
        if (material.HasProperty(ColorId))
        {
            material.SetColor(ColorId, outlineColor);
        }

        // Back-face shell so the slightly larger clone reads as an outline around the arrow mesh.
        material.SetInt("_Cull", (int)CullMode.Front);
        return material;
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
        collider.radius = playerHazardHitRadius;
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
        if (!IsValidDirection(direction))
        {
            return;
        }

        // Follow the full 3D arc so the tip points down on the descent.
        transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private static bool IsValidPosition(Vector3 position)
    {
        return float.IsFinite(position.x)
            && float.IsFinite(position.y)
            && float.IsFinite(position.z);
    }

    private static bool IsValidDirection(Vector3 direction)
    {
        return float.IsFinite(direction.x)
            && float.IsFinite(direction.y)
            && float.IsFinite(direction.z)
            && direction.sqrMagnitude >= 0.0001f;
    }

    private static float GetHorizontalDistance(Vector3 a, Vector3 b)
    {
        Vector3 offset = a - b;
        offset.y = 0f;
        return offset.magnitude;
    }
}
