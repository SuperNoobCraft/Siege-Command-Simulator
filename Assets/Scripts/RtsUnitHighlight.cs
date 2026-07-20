using UnityEngine;

public class RtsUnitHighlight : MonoBehaviour
{
    [Header("Colors")]
    [SerializeField] private Color hoverColor = Color.green;
    [SerializeField] private Color selectedColor = Color.red;

    [Header("Outline")]
    [SerializeField] private bool outlineOnly = true;
    [SerializeField, Min(0f)] private float hoverOutlineWidth = 3f;
    [SerializeField, Min(0f)] private float selectedOutlineWidth = 5f;
    [SerializeField] private float glowIntensity = 2.5f;
    [SerializeField] private bool scaleOutlineWithCameraDistance = true;
    [SerializeField, Min(0f)] private float outlineDistanceScale = 0.002f;
    [SerializeField, Min(0f)] private float outlineMinWorldWidth = 0.05f;
    [Tooltip("Project the footprint outline onto RTS_Ground like movement arrows.")]
    [SerializeField] private bool projectOutlineOntoGround = true;
    [SerializeField, Min(0f)] private float outlineGroundOffset = 0.08f;

    [Header("Targets")]
    [SerializeField] private bool preferColliderWireframe = true;
    [SerializeField] private bool includeChildRenderers;
    [SerializeField] private Renderer[] targetRenderers;
    [SerializeField] private Collider highlightBoundsCollider;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");

    private MaterialPropertyBlock propertyBlock;
    private LineRenderer wireframeOutline;
    private Vector3[] cachedLocalFootprintCorners;
    private Vector3[] projectedWorldPositions;
    private bool isHovered;
    private bool isSelected;
    private float activeOutlineWidth;
    private bool hasActiveOutline;

    private void Awake()
    {
        ResolveHighlightBoundsCollider();
        propertyBlock = new MaterialPropertyBlock();

        if (preferColliderWireframe && highlightBoundsCollider != null)
        {
            CacheWireframeGeometry();
            EnsureWireframeOutline();
        }
        else
        {
            ResolveTargetRenderers();
        }

        ApplyVisuals();
    }

    private void Start()
    {
        // TroopCombat creates SelectionVolume in Awake; resolve again in case we ran first.
        Collider previous = highlightBoundsCollider;
        ResolveHighlightBoundsCollider();
        if (preferColliderWireframe
            && highlightBoundsCollider != null
            && highlightBoundsCollider != previous)
        {
            CacheWireframeGeometry();
            EnsureWireframeOutline();
            RefreshProjectedOutlinePositions();
            ApplyVisuals();
        }
    }

    private void ResolveHighlightBoundsCollider()
    {
        if (highlightBoundsCollider != null)
        {
            return;
        }

        TroopCombat combat = GetComponent<TroopCombat>();
        if (combat != null)
        {
            Collider selection = combat.SelectionCollider;
            if (selection != null)
            {
                highlightBoundsCollider = selection;
                return;
            }
        }

        Transform selectionVolume = transform.Find("SelectionVolume");
        if (selectionVolume != null)
        {
            highlightBoundsCollider = selectionVolume.GetComponent<Collider>();
            if (highlightBoundsCollider != null)
            {
                return;
            }
        }

        highlightBoundsCollider = GetComponent<Collider>();
    }

    private void LateUpdate()
    {
        if (wireframeOutline == null || !wireframeOutline.enabled || !hasActiveOutline)
        {
            return;
        }

        wireframeOutline.widthMultiplier = GetEffectiveOutlineWidth(activeOutlineWidth);
        if (projectOutlineOntoGround)
        {
            RefreshProjectedOutlinePositions();
        }
    }

    private void OnValidate()
    {
        hoverOutlineWidth = Mathf.Max(0f, hoverOutlineWidth);
        selectedOutlineWidth = Mathf.Max(0f, selectedOutlineWidth);
        outlineDistanceScale = Mathf.Max(0f, outlineDistanceScale);
        outlineMinWorldWidth = Mathf.Max(0f, outlineMinWorldWidth);
        outlineGroundOffset = Mathf.Max(0f, outlineGroundOffset);

        if (isActiveAndEnabled)
        {
            ApplyVisuals();
        }
    }

    private void OnDisable()
    {
        ClearVisuals();
    }

    public void SetHovered(bool value)
    {
        if (isHovered == value)
        {
            return;
        }

        isHovered = value;
        ApplyVisuals();
    }

    public void SetSelected(bool value)
    {
        if (isSelected == value)
        {
            return;
        }

        isSelected = value;
        ApplyVisuals();
    }

    private void ResolveTargetRenderers()
    {
        if (targetRenderers != null && targetRenderers.Length > 0)
        {
            return;
        }

        if (includeChildRenderers)
        {
            targetRenderers = GetComponentsInChildren<Renderer>(includeInactive: false);
            return;
        }

        Renderer selfRenderer = GetComponent<Renderer>();
        targetRenderers = selfRenderer != null ? new[] { selfRenderer } : System.Array.Empty<Renderer>();
    }

    private void CacheWireframeGeometry()
    {
        if (highlightBoundsCollider == null)
        {
            return;
        }

        // Footprint rectangle only (bottom face) — projects cleanly onto ground like path arrows.
        Vector3[] corners = GetFixedLocalCornerPoints(highlightBoundsCollider);
        cachedLocalFootprintCorners = new[]
        {
            corners[0], corners[1], corners[2], corners[3], corners[0]
        };
        projectedWorldPositions = new Vector3[cachedLocalFootprintCorners.Length];
    }

    private static Vector3[] GetFixedLocalCornerPoints(Collider boundsSource)
    {
        if (boundsSource is BoxCollider boxCollider)
        {
            return GetBoxLocalCorners(boxCollider.center, boxCollider.size * 0.5f);
        }

        if (boundsSource is SphereCollider sphereCollider)
        {
            float radius = sphereCollider.radius;
            return GetBoxLocalCorners(sphereCollider.center, new Vector3(radius, radius, radius));
        }

        if (boundsSource is CapsuleCollider capsuleCollider)
        {
            float radius = capsuleCollider.radius;
            float halfHeight = Mathf.Max(radius, capsuleCollider.height * 0.5f);
            return GetBoxLocalCorners(capsuleCollider.center, new Vector3(radius, halfHeight, radius));
        }

        Bounds localBounds = GetLocalBounds(boundsSource);
        return GetBoxLocalCorners(localBounds.center, localBounds.extents);
    }

    private static Bounds GetLocalBounds(Collider boundsSource)
    {
        Bounds worldBounds = boundsSource.bounds;
        Transform sourceTransform = boundsSource.transform;
        Vector3 localCenter = sourceTransform.InverseTransformPoint(worldBounds.center);
        Vector3 worldExtentX = sourceTransform.InverseTransformVector(new Vector3(worldBounds.extents.x, 0f, 0f));
        Vector3 worldExtentY = sourceTransform.InverseTransformVector(new Vector3(0f, worldBounds.extents.y, 0f));
        Vector3 worldExtentZ = sourceTransform.InverseTransformVector(new Vector3(0f, 0f, worldBounds.extents.z));
        Vector3 localExtents = new Vector3(
            Mathf.Abs(worldExtentX.x) + Mathf.Abs(worldExtentY.x) + Mathf.Abs(worldExtentZ.x),
            Mathf.Abs(worldExtentX.y) + Mathf.Abs(worldExtentY.y) + Mathf.Abs(worldExtentZ.y),
            Mathf.Abs(worldExtentX.z) + Mathf.Abs(worldExtentY.z) + Mathf.Abs(worldExtentZ.z));

        return new Bounds(localCenter, localExtents * 2f);
    }

    private static Vector3[] GetBoxLocalCorners(Vector3 center, Vector3 extents)
    {
        return new[]
        {
            center + new Vector3(-extents.x, -extents.y, -extents.z),
            center + new Vector3(extents.x, -extents.y, -extents.z),
            center + new Vector3(extents.x, -extents.y, extents.z),
            center + new Vector3(-extents.x, -extents.y, extents.z),
            center + new Vector3(-extents.x, extents.y, -extents.z),
            center + new Vector3(extents.x, extents.y, -extents.z),
            center + new Vector3(extents.x, extents.y, extents.z),
            center + new Vector3(-extents.x, extents.y, extents.z)
        };
    }

    private void EnsureWireframeOutline()
    {
        if (wireframeOutline != null || cachedLocalFootprintCorners == null)
        {
            return;
        }

        GameObject outlineObject = new GameObject("ParentSelectionOutline");
        outlineObject.transform.SetParent(transform, false);
        outlineObject.transform.localPosition = Vector3.zero;
        outlineObject.transform.localRotation = Quaternion.identity;
        outlineObject.transform.localScale = Vector3.one;
        int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        outlineObject.layer = ignoreRaycastLayer >= 0 ? ignoreRaycastLayer : gameObject.layer;

        wireframeOutline = outlineObject.AddComponent<LineRenderer>();
        wireframeOutline.useWorldSpace = true;
        wireframeOutline.loop = false;
        wireframeOutline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        wireframeOutline.receiveShadows = false;
        wireframeOutline.allowOcclusionWhenDynamic = false;
        wireframeOutline.textureMode = LineTextureMode.Stretch;
        wireframeOutline.alignment = LineAlignment.View;
        wireframeOutline.numCornerVertices = 4;
        wireframeOutline.numCapVertices = 4;
        wireframeOutline.positionCount = cachedLocalFootprintCorners.Length;
        wireframeOutline.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        RefreshProjectedOutlinePositions();
    }

    private void RefreshProjectedOutlinePositions()
    {
        if (wireframeOutline == null || cachedLocalFootprintCorners == null)
        {
            return;
        }

        Transform source = highlightBoundsCollider != null ? highlightBoundsCollider.transform : transform;
        if (projectedWorldPositions == null || projectedWorldPositions.Length != cachedLocalFootprintCorners.Length)
        {
            projectedWorldPositions = new Vector3[cachedLocalFootprintCorners.Length];
        }

        for (int i = 0; i < cachedLocalFootprintCorners.Length; i++)
        {
            Vector3 world = source.TransformPoint(cachedLocalFootprintCorners[i]);
            if (projectOutlineOntoGround)
            {
                world = RtsGroundUtility.ProjectPointOntoGround(world, outlineGroundOffset, preferredY: transform.position.y);
            }

            projectedWorldPositions[i] = world;
        }

        wireframeOutline.positionCount = projectedWorldPositions.Length;
        wireframeOutline.SetPositions(projectedWorldPositions);
    }

    private void ClearVisuals()
    {
        if (targetRenderers != null)
        {
            for (int i = 0; i < targetRenderers.Length; i++)
            {
                Renderer rendererRef = targetRenderers[i];
                if (rendererRef == null)
                {
                    continue;
                }

                rendererRef.SetPropertyBlock(null);
            }
        }

        if (wireframeOutline != null)
        {
            wireframeOutline.enabled = false;
        }
    }

    private void ApplyVisuals()
    {
        Color stateColor = Color.clear;
        float outlineWidth = 0f;
        bool isActive = false;

        if (isSelected)
        {
            stateColor = selectedColor;
            outlineWidth = selectedOutlineWidth;
            isActive = true;
        }
        else if (isHovered)
        {
            stateColor = hoverColor;
            outlineWidth = hoverOutlineWidth;
            isActive = true;
        }

        if (wireframeOutline != null)
        {
            ApplyWireframeOutline(stateColor, outlineWidth, isActive);
        }
        else
        {
            ApplyRendererOutline(stateColor, outlineWidth, isActive);
        }

        activeOutlineWidth = outlineWidth;
        hasActiveOutline = isActive;
    }

    private float GetEffectiveOutlineWidth(float outlineWidth)
    {
        if (outlineWidth <= 0f)
        {
            return 0f;
        }

        // CAVE / multi-view: distance scaling against one camera makes outlines vanish
        // for the other player. Keep a solid world width for selection feedback.
        float minWidth = Mathf.Max(0.08f, outlineMinWorldWidth);
        if (!scaleOutlineWithCameraDistance)
        {
            return Mathf.Max(minWidth, outlineWidth * 0.02f);
        }

        UnityEngine.Camera camera = SiegePlayEnvironment.ResolveViewCamera();
        if (camera == null)
        {
            return Mathf.Max(minWidth, outlineWidth * 0.02f);
        }

        float distance = Vector3.Distance(camera.transform.position, transform.position);
        float scaledWidth = outlineWidth * distance * outlineDistanceScale;
        return Mathf.Max(minWidth, scaledWidth);
    }

    private void ApplyRendererOutline(Color stateColor, float outlineWidth, bool isActive)
    {
        if (propertyBlock == null || targetRenderers == null)
        {
            return;
        }

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer rendererRef = targetRenderers[i];
            if (rendererRef == null)
            {
                continue;
            }

            if (!isActive)
            {
                rendererRef.SetPropertyBlock(null);
                continue;
            }

            propertyBlock.Clear();
            if (!outlineOnly)
            {
                propertyBlock.SetColor(EmissionColorId, stateColor * glowIntensity);
            }

            propertyBlock.SetColor(OutlineColorId, stateColor);
            propertyBlock.SetFloat(OutlineWidthId, outlineWidth);
            rendererRef.SetPropertyBlock(propertyBlock);
        }
    }

    private void ApplyWireframeOutline(Color stateColor, float outlineWidth, bool isActive)
    {
        if (wireframeOutline == null)
        {
            return;
        }

        wireframeOutline.enabled = isActive;
        if (!isActive)
        {
            return;
        }

        wireframeOutline.startColor = stateColor;
        wireframeOutline.endColor = stateColor;
        wireframeOutline.widthMultiplier = GetEffectiveOutlineWidth(outlineWidth);
        RefreshProjectedOutlinePositions();
    }
}
