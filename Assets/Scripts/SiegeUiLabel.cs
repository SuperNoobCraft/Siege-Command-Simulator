using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Place on any UI text you want to position yourself. SiegeMatchUi only changes text/visibility.
/// </summary>
[DisallowMultipleComponent]
public class SiegeUiLabel : MonoBehaviour
{
    [SerializeField] private Text legacyText;
    [SerializeField] private TextMeshProUGUI tmpText;
    [SerializeField] private bool hideWhenEmpty = true;

    public bool HideWhenEmpty => hideWhenEmpty;

    private void Reset()
    {
        legacyText = GetComponent<Text>();
        tmpText = GetComponent<TextMeshProUGUI>();
    }

    private void OnValidate()
    {
        if (legacyText == null)
        {
            legacyText = GetComponent<Text>();
        }

        if (tmpText == null)
        {
            tmpText = GetComponent<TextMeshProUGUI>();
        }
    }

    public void SetText(string value, bool keepVisible)
    {
        string text = value ?? string.Empty;
        if (legacyText != null)
        {
            legacyText.text = text;
            legacyText.enabled = true;
        }

        if (tmpText != null)
        {
            tmpText.text = text;
            tmpText.enabled = true;
        }

        if (keepVisible)
        {
            SetVisible(true);
            return;
        }

        if (hideWhenEmpty)
        {
            SetVisible(!string.IsNullOrEmpty(text));
        }
    }

    public void SetText(string value)
    {
        SetText(value, false);
    }

    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible)
        {
            gameObject.SetActive(visible);
        }
    }

    public void Clear()
    {
        SetText(string.Empty);
    }
}
