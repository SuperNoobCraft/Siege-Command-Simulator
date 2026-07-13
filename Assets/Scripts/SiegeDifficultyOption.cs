using TMPro;
using UnityEngine;

/// <summary>
/// World-space Demo / Full / Dodge Arrows mode button. Place a collider in the scene and wire a label.
/// </summary>
[DisallowMultipleComponent]
public class SiegeDifficultyOption : MonoBehaviour
{
    [SerializeField] private SiegeGameMode gameMode = SiegeGameMode.Demo;
    [SerializeField] private Collider hitCollider;
    [SerializeField] private SiegeWorldUiLabel label;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color highlightedColor = new Color(0.35f, 1f, 0.45f, 1f);

    public SiegeGameMode GameMode => gameMode;
    public int WaveCount => gameMode switch
    {
        SiegeGameMode.Demo => SiegeMatchSettings.DemoWaveCount,
        SiegeGameMode.DodgeArrows => 0,
        _ => SiegeMatchSettings.FullWaveCount
    };

    public SiegeWorldUiLabel GetPresentationLabel()
    {
        if (label != null)
        {
            return label;
        }

        return GetComponentInChildren<SiegeWorldUiLabel>(true);
    }

    public TextMeshPro GetPresentationText()
    {
        if (label != null)
        {
            return label.GetComponentInChildren<TextMeshPro>(true);
        }

        return GetComponentInChildren<TextMeshPro>(true);
    }

    private void Reset()
    {
        hitCollider = GetComponentInChildren<Collider>();
        label = GetComponentInChildren<SiegeWorldUiLabel>();
    }

    private void OnValidate()
    {
        if (hitCollider == null)
        {
            hitCollider = GetComponentInChildren<Collider>();
        }

        if (label == null)
        {
            label = GetComponentInChildren<SiegeWorldUiLabel>();
        }
    }

    public bool TryGetHitCollider(out Collider collider)
    {
        collider = hitCollider != null ? hitCollider : GetComponentInChildren<Collider>();
        return collider != null;
    }

    public void SetHighlighted(bool highlighted)
    {
        if (label == null)
        {
            return;
        }

        label.SetHighlightColor(highlighted ? highlightedColor : normalColor);
    }

    public void SetOptionVisible(bool visible)
    {
        SiegeWorldUiLabel presentationLabel = GetPresentationLabel();
        if (presentationLabel != null)
        {
            presentationLabel.SetVisible(visible);
        }

        TextMeshPro presentationText = GetPresentationText();
        if (presentationText != null)
        {
            presentationText.enabled = visible;
            if (presentationLabel == null)
            {
                presentationText.gameObject.SetActive(visible);
            }
        }
        else if (presentationLabel == null)
        {
            gameObject.SetActive(visible);
        }

        Collider collider = hitCollider != null ? hitCollider : GetComponentInChildren<Collider>();
        if (collider != null)
        {
            collider.enabled = visible;
        }

        if (!visible)
        {
            SetHighlighted(false);
        }
    }
}
