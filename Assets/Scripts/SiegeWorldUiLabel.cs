using TMPro;
using UnityEngine;

/// <summary>
/// World-space floating sign label for CAVE / multi-screen setups where screen canvases are not visible.
/// Place in the scene as a physical sign at a fixed world position; <see cref="SiegeMatchUi"/> drives the text.
/// </summary>
[DisallowMultipleComponent]
public class SiegeWorldUiLabel : MonoBehaviour
{
    [SerializeField] private TextMeshPro worldText;
    [SerializeField] private GameObject signRoot;
    [SerializeField] private bool hideWhenEmpty = true;
    [SerializeField] private bool faceActiveView = true;
    [Tooltip("In CAVE/HMD, keep the sign at its scene placement instead of billboarding to one wall camera.")]
    [SerializeField] private bool billboardOnlyOnDesktop = true;
    [Tooltip("Detach from the player/camera rig if parented there so the sign stays at its assigned world position.")]
    [SerializeField] private bool detachFromPlayerRig = true;
    [SerializeField, Min(0f)] private float billboardLerpSpeed = 12f;

    public bool HideWhenEmpty => hideWhenEmpty;

    private bool subscribedToEnvironmentEvents;

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

    private void Awake()
    {
        ApplyPresentationMode();
    }

    private void OnEnable()
    {
        SubscribeToEnvironmentEvents();
        ApplyPresentationMode();
        EnsureWorldSpaceAnchor();
    }

    private void OnDisable()
    {
        UnsubscribeFromEnvironmentEvents();
    }

    private void Start()
    {
        ApplyPresentationMode();
        EnsureWorldSpaceAnchor();
    }

    private void SubscribeToEnvironmentEvents()
    {
        if (subscribedToEnvironmentEvents)
        {
            return;
        }

        SiegePlayEnvironment.EnvironmentChanged += HandleEnvironmentChanged;
        subscribedToEnvironmentEvents = true;
    }

    private void UnsubscribeFromEnvironmentEvents()
    {
        if (!subscribedToEnvironmentEvents)
        {
            return;
        }

        SiegePlayEnvironment.EnvironmentChanged -= HandleEnvironmentChanged;
        subscribedToEnvironmentEvents = false;
    }

    private void HandleEnvironmentChanged()
    {
        ApplyPresentationMode();
        EnsureWorldSpaceAnchor();
    }

    private void ApplyPresentationMode()
    {
        if (!billboardOnlyOnDesktop)
        {
            return;
        }

        if (SiegePlayEnvironment.IsTrackedXr)
        {
            faceActiveView = false;
        }
    }

    private void EnsureWorldSpaceAnchor()
    {
        if (!detachFromPlayerRig || !SiegePlayEnvironment.IsTrackedXr)
        {
            return;
        }

        Transform playerTransform = SiegePlayEnvironment.ResolvePlayerTransform();
        if (playerTransform == null)
        {
            return;
        }

        Transform anchor = signRoot != null ? signRoot.transform : transform;
        if (!anchor.IsChildOf(playerTransform))
        {
            return;
        }

        anchor.SetParent(null, true);
        Debug.LogWarning(
            "SiegeWorldUiLabel was parented to the player rig and has been detached to stay at its world position.",
            this);
    }

    private void LateUpdate()
    {
        if (!ShouldBillboard() || !signRoot.activeInHierarchy)
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

    private bool ShouldBillboard()
    {
        if (!faceActiveView)
        {
            return false;
        }

        return !billboardOnlyOnDesktop || !SiegePlayEnvironment.IsTrackedXr;
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
        }
        else if (gameObject.activeSelf != visible)
        {
            gameObject.SetActive(visible);
        }

        if (worldText == null)
        {
            worldText = GetComponentInChildren<TextMeshPro>(true);
        }

        if (worldText != null)
        {
            worldText.enabled = visible;
        }
    }

    public void Clear()
    {
        SetText(string.Empty);
    }

    public void SetHighlightColor(Color color)
    {
        if (worldText != null)
        {
            worldText.color = color;
        }
    }
}
