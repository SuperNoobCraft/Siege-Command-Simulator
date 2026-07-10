using TMPro;
using UnityEngine;

/// <summary>
/// World-space floating sign label for CAVE / multi-screen setups where screen canvases are not visible.
/// Place in the scene as a physical sign; <see cref="SiegeMatchUi"/> can drive the same text as canvas labels.
/// </summary>
[DisallowMultipleComponent]
public class SiegeWorldUiLabel : MonoBehaviour
{
    [SerializeField] private TextMeshPro worldText;
    [SerializeField] private GameObject signRoot;
    [SerializeField] private bool hideWhenEmpty = true;
    [SerializeField] private bool faceActiveView = true;
    [SerializeField, Min(0f)] private float billboardLerpSpeed = 12f;

    public bool HideWhenEmpty => hideWhenEmpty;

    private void Reset()
    {
        worldText = GetComponentInChildren<TextMeshPro>();
        signRoot = gameObject;
    }

    private void OnValidate()
    {
        if (worldText == null)
        {
            worldText = GetComponentInChildren<TextMeshPro>();
        }

        if (signRoot == null)
        {
            signRoot = gameObject;
        }
    }

    private void LateUpdate()
    {
        if (!faceActiveView || !signRoot.activeInHierarchy)
        {
            return;
        }

        UnityEngine.Camera viewCamera = SiegePlayEnvironment.ResolveViewCamera();
        if (viewCamera == null)
        {
            return;
        }

        Vector3 toCamera = viewCamera.transform.position - signRoot.transform.position;
        toCamera.y = 0f;
        if (toCamera.sqrMagnitude < 0.0001f)
        {
            toCamera = viewCamera.transform.forward;
            toCamera.y = 0f;
        }

        if (toCamera.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
        signRoot.transform.rotation = Quaternion.Slerp(
            signRoot.transform.rotation,
            targetRotation,
            Time.deltaTime * billboardLerpSpeed);
    }

    public void SetText(string value, bool keepVisible)
    {
        string text = value ?? string.Empty;
        if (worldText != null)
        {
            worldText.text = text;
            worldText.enabled = true;
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
        if (signRoot != null)
        {
            if (signRoot.activeSelf != visible)
            {
                signRoot.SetActive(visible);
            }

            return;
        }

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
