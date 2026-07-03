using UnityEngine;

public class RtsUnitHighlight : MonoBehaviour
{
    [Header("Colors")]
    [SerializeField] private Color hoverColor = Color.green;
    [SerializeField] private Color selectedColor = Color.red;
    [SerializeField] private float glowIntensity = 2.5f;

    [Header("Targets")]
    [SerializeField] private Renderer[] targetRenderers;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");

    private MaterialPropertyBlock propertyBlock;
    private bool isHovered;
    private bool isSelected;

    private void Awake()
    {
        if (targetRenderers == null || targetRenderers.Length == 0)
        {
            targetRenderers = GetComponentsInChildren<Renderer>();
        }

        propertyBlock = new MaterialPropertyBlock();
        ApplyVisuals();
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

    private void ClearVisuals()
    {
        if (targetRenderers == null)
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

            rendererRef.SetPropertyBlock(null);
        }
    }

    private void ApplyVisuals()
    {
        if (propertyBlock == null || targetRenderers == null)
        {
            return;
        }

        Color stateColor = Color.black;
        float outlineWidth = 0f;

        if (isSelected)
        {
            stateColor = selectedColor;
            outlineWidth = 2f;
        }
        else if (isHovered)
        {
            stateColor = hoverColor;
            outlineWidth = 1f;
        }

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer rendererRef = targetRenderers[i];
            if (rendererRef == null)
            {
                continue;
            }

            propertyBlock.Clear();
            propertyBlock.SetColor(EmissionColorId, stateColor * glowIntensity);
            propertyBlock.SetColor(OutlineColorId, stateColor);
            propertyBlock.SetFloat(OutlineWidthId, outlineWidth);
            rendererRef.SetPropertyBlock(propertyBlock);
        }
    }
}
