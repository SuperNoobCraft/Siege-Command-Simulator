using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// World-space popup to raise Infantry or Archer at a clicked village disc.
/// Stays at the raise point so first-person / CAVE view can aim at it with mouse or wand.
/// </summary>
[DisallowMultipleComponent]
public class PointCaptureRaiseMenu : MonoBehaviour
{
    [SerializeField] private PointCaptureMatch match;
    [SerializeField] private PointCaptureSpawner spawner;
    [SerializeField] private VotanicWandRtsCommander wandCommander;
    [SerializeField] private Canvas canvas;
    [SerializeField] private RectTransform panel;
    [SerializeField] private Text titleText;
    [SerializeField] private Button infantryButton;
    [SerializeField] private Button archerButton;
    [SerializeField] private Text infantryCostText;
    [SerializeField] private Text archerCostText;
    [SerializeField, Min(0.001f)] private float worldScale = 0.008f;
    [SerializeField, Min(0.2f)] private float worldHeight = 1.35f;
    [SerializeField, Min(0f)] private float pullTowardViewer = 0.7f;
    [SerializeField, Min(1f)] private float referenceViewDistance = 10f;
    [SerializeField, Min(0.001f)] private float minWorldScale = 0.007f;
    [SerializeField, Min(0.001f)] private float maxWorldScale = 0.04f;
    [SerializeField, Min(0.1f)] private float menuInputGraceSeconds = 0.42f;

    private Vector3 pendingWorldPosition;
    private CaptureOwner pendingOwner = CaptureOwner.Neutral;
    private Action<string> onRaiseFailed;
    private float ignoreMenuInputUntilUnscaledTime;
    private Collider infantryCollider;
    private Collider archerCollider;
    private Collider panelCollider;
    private Image infantryImage;
    private Image archerImage;
    private int lastClosedFrame = -1;

    private static readonly Color ButtonNormal = new Color(0.18f, 0.22f, 0.28f, 0.96f);
    private static readonly Color ButtonHover = new Color(1f, 0.86f, 0.22f, 1f);
    private static readonly Color ButtonHoverText = new Color(0.12f, 0.1f, 0.02f, 1f);

    public bool IsOpen => canvas != null && canvas.gameObject.activeSelf && panel != null && panel.gameObject.activeSelf;
    public bool ClosedThisFrame => lastClosedFrame == Time.frameCount;

    public void Configure(PointCaptureMatch captureMatch, PointCaptureSpawner captureSpawner)
    {
        match = captureMatch;
        spawner = captureSpawner;
        EnsureUi();
    }

    public void SetWandCommander(VotanicWandRtsCommander commander)
    {
        if (commander != null)
        {
            wandCommander = commander;
        }
    }

    public void SetFailureHandler(Action<string> handler)
    {
        onRaiseFailed = handler;
    }

    private void Awake()
    {
        if (match == null)
        {
            match = PointCaptureMatch.Instance;
        }

        if (spawner == null)
        {
            spawner = FindObjectOfType<PointCaptureSpawner>();
        }

        if (wandCommander == null)
        {
            wandCommander = FindObjectOfType<VotanicWandRtsCommander>();
        }

        EnsureUi();
        Hide();
    }

    private void Update()
    {
        if (!IsOpen)
        {
            return;
        }

        FaceViewer();
        ValidatePendingRaiseLocation();
        if (!IsOpen)
        {
            return;
        }

        UpdateButtonHover();

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Hide(suppressReopen: true);
            return;
        }

        if (!PointCapturePointerInput.WasPointerPressedThisFrame())
        {
            return;
        }

        Ray ray = BuildGameplayRay();
        if (RayHitsCollider(ray, infantryCollider))
        {
            Raise(PointCaptureSpawner.RegimentType.Infantry);
            return;
        }

        if (RayHitsCollider(ray, archerCollider))
        {
            Raise(PointCaptureSpawner.RegimentType.Archer);
            return;
        }

        if (Time.unscaledTime < ignoreMenuInputUntilUnscaledTime)
        {
            return;
        }

        Hide(suppressReopen: true);
    }

    public void Show(Vector3 worldPosition, CaptureOwner owner)
    {
        if (match == null || spawner == null || !match.IsPlaying)
        {
            return;
        }

        EnsureUi();
        pendingWorldPosition = worldPosition;
        pendingOwner = owner;

        float infantryCost = match.GetRaiseCost(PointCaptureSpawner.RegimentType.Infantry);
        float archerCost = match.GetRaiseCost(PointCaptureSpawner.RegimentType.Archer);
        if (titleText != null)
        {
            titleText.text = "Raise " + CaptureTeams.GetDisplayName(owner);
        }

        if (infantryCostText != null)
        {
            infantryCostText.text = "Infantry (" + infantryCost.ToString("0") + " MP)";
        }

        if (archerCostText != null)
        {
            archerCostText.text = "Archer (" + archerCost.ToString("0") + " MP)";
        }

        canvas.gameObject.SetActive(true);
        panel.gameObject.SetActive(true);
        ignoreMenuInputUntilUnscaledTime = Time.unscaledTime + Mathf.Max(0.1f, menuInputGraceSeconds);
        PlaceInWorld(worldPosition);
        FaceViewer();
        UpdateButtonHover();
    }

    public void Hide()
    {
        Hide(suppressReopen: false);
    }

    public void Hide(bool suppressReopen)
    {
        bool wasOpen = IsOpen;
        ResetButtonHover();
        if (panel != null)
        {
            panel.gameObject.SetActive(false);
        }

        if (canvas != null)
        {
            canvas.gameObject.SetActive(false);
        }

        if (wasOpen)
        {
            lastClosedFrame = Time.frameCount;
            ignoreMenuInputUntilUnscaledTime = 0f;
            if (suppressReopen)
            {
                PointCapturePointerInput.BeginAfterMenuCloseCooldown();
                SuppressReopen();
            }
        }
    }

    public bool IsPointerOverMenu()
    {
        if (!IsOpen)
        {
            return false;
        }

        Ray ray = BuildGameplayRay();
        return RayHitsCollider(ray, panelCollider)
            || RayHitsCollider(ray, infantryCollider)
            || RayHitsCollider(ray, archerCollider);
    }

    private void Raise(PointCaptureSpawner.RegimentType regimentType)
    {
        if (spawner == null)
        {
            return;
        }

        PointCaptureBoard board = PointCaptureBoard.Instance;
        string failReason = string.Empty;
        if (board == null || !board.CanRaiseAt(pendingOwner, pendingWorldPosition, out failReason))
        {
            onRaiseFailed?.Invoke(string.IsNullOrEmpty(failReason)
                ? "This village can no longer raise regiments."
                : failReason);
            Hide(suppressReopen: true);
            return;
        }

        if (!spawner.TryRaise(pendingOwner, regimentType, pendingWorldPosition, out failReason))
        {
            onRaiseFailed?.Invoke(failReason);
        }

        Hide(suppressReopen: true);
    }

    private void ValidatePendingRaiseLocation()
    {
        PointCaptureBoard board = PointCaptureBoard.Instance;
        if (board == null)
        {
            Hide(suppressReopen: true);
            return;
        }

        if (board.CanRaiseAt(pendingOwner, pendingWorldPosition, out _))
        {
            return;
        }

        Hide(suppressReopen: true);
        onRaiseFailed?.Invoke("This village can no longer raise regiments.");
    }

    private void PlaceInWorld(Vector3 worldPosition)
    {
        Transform viewer = ResolveViewer();
        Vector3 towardViewer = Vector3.forward;
        if (viewer != null)
        {
            towardViewer = viewer.position - worldPosition;
            towardViewer.y = 0f;
        }

        if (towardViewer.sqrMagnitude < 0.01f)
        {
            towardViewer = Vector3.forward;
        }

        Vector3 menuPosition = worldPosition + Vector3.up * worldHeight + towardViewer.normalized * pullTowardViewer;
        canvas.transform.position = menuPosition;
        ApplyDistanceScale(viewer);
        canvas.worldCamera = SiegePlayEnvironment.ResolveViewCamera();
    }

    private void FaceViewer()
    {
        if (canvas == null || !canvas.gameObject.activeSelf)
        {
            return;
        }

        Transform viewer = ResolveViewer();
        if (viewer == null)
        {
            return;
        }

        Vector3 toViewer = viewer.position - canvas.transform.position;
        toViewer.y = 0f;
        if (toViewer.sqrMagnitude < 0.0001f)
        {
            return;
        }

        canvas.transform.rotation = Quaternion.LookRotation(-toViewer.normalized, Vector3.up);
        ApplyDistanceScale(viewer);
    }

    private void UpdateButtonHover()
    {
        if (!IsOpen)
        {
            return;
        }

        Ray ray = BuildGameplayRay();
        bool hoverInfantry = RayHitsCollider(ray, infantryCollider);
        bool hoverArcher = RayHitsCollider(ray, archerCollider);
        ApplyButtonHover(infantryButton, infantryImage, infantryCostText, hoverInfantry);
        ApplyButtonHover(archerButton, archerImage, archerCostText, hoverArcher);
    }

    private void ResetButtonHover()
    {
        ApplyButtonHover(infantryButton, infantryImage, infantryCostText, false);
        ApplyButtonHover(archerButton, archerImage, archerCostText, false);
    }

    private void ApplyButtonHover(Button button, Image image, Text label, bool hovered)
    {
        if (image != null)
        {
            Color team = CaptureTeams.GetColor(pendingOwner);
            image.color = hovered
                ? Color.Lerp(ButtonHover, team, 0.35f)
                : ButtonNormal;
        }

        if (label != null)
        {
            label.color = hovered ? ButtonHoverText : Color.white;
            label.fontStyle = hovered ? FontStyle.Bold : FontStyle.Normal;
            label.fontSize = hovered ? 22 : 18;
        }

        if (button != null)
        {
            button.transform.localScale = hovered ? Vector3.one * 1.16f : Vector3.one;
        }
    }

    private static void SuppressReopen()
    {
        PointCaptureLocalInput input = PointCaptureLocalInput.Instance != null
            ? PointCaptureLocalInput.Instance
            : FindObjectOfType<PointCaptureLocalInput>();
        input?.SuppressRaiseMenu(0.6f);
    }

    private void ApplyDistanceScale(Transform viewer)
    {
        if (canvas == null)
        {
            return;
        }

        canvas.transform.localScale = Vector3.one * GetDistanceScale(viewer);
    }

    private float GetDistanceScale(Transform viewer)
    {
        if (viewer == null)
        {
            return worldScale;
        }

        float distance = Vector3.Distance(viewer.position, canvas.transform.position);
        float scaled = worldScale * (distance / referenceViewDistance);
        return Mathf.Clamp(scaled, minWorldScale, maxWorldScale);
    }

    private Ray BuildGameplayRay()
    {
        if (wandCommander == null)
        {
            wandCommander = FindObjectOfType<VotanicWandRtsCommander>();
        }

        if (wandCommander != null)
        {
            return wandCommander.BuildGameplayRay();
        }

        Camera viewCamera = SiegePlayEnvironment.ResolveViewCamera();
        if (viewCamera != null)
        {
            return viewCamera.ScreenPointToRay(Input.mousePosition);
        }

        return new Ray(transform.position, transform.forward);
    }

    private static Transform ResolveViewer()
    {
        Transform player = SiegePlayEnvironment.ResolvePlayerTransform();
        if (player != null)
        {
            return player;
        }

        Transform user = SiegePlayEnvironment.ResolveUserTransform();
        if (user != null)
        {
            return user;
        }

        Camera viewCamera = SiegePlayEnvironment.ResolveViewCamera();
        return viewCamera != null ? viewCamera.transform : null;
    }

    private static bool RayHitsCollider(Ray ray, Collider hitCollider)
    {
        if (hitCollider == null || !hitCollider.enabled || !hitCollider.gameObject.activeInHierarchy)
        {
            return false;
        }

        return hitCollider.Raycast(ray, out _, 2000f);
    }

    private void EnsureUi()
    {
        if (panel != null && canvas != null)
        {
            if (canvas.transform.parent != null)
            {
                canvas.transform.SetParent(null, true);
            }

            ApplyWorldSpaceCanvas();
            EnsureMenuColliders();
            return;
        }

        if (EventSystem.current == null)
        {
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }

        GameObject canvasObject = new GameObject("PointCaptureRaiseCanvas");
        canvasObject.transform.SetParent(null, true);
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 80;
        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject panelObject = new GameObject("RaisePanel");
        panelObject.transform.SetParent(canvasObject.transform, false);
        panel = panelObject.AddComponent<RectTransform>();
        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(220f, 118f);
        Image panelImage = panelObject.AddComponent<Image>();
        panelImage.color = new Color(0.08f, 0.1f, 0.12f, 0.94f);
        panelImage.raycastTarget = true;
        panelCollider = EnsureBoxCollider(panelObject, panel.sizeDelta);

        titleText = CreateLabel(panel, "Title", new Vector2(0f, 42f), 20, TextAnchor.MiddleCenter);
        infantryButton = CreateButton(panel, "InfantryButton", new Vector2(0f, 6f), out infantryCostText);
        archerButton = CreateButton(panel, "ArcherButton", new Vector2(0f, -36f), out archerCostText);
        infantryImage = infantryButton != null ? infantryButton.GetComponent<Image>() : null;
        archerImage = archerButton != null ? archerButton.GetComponent<Image>() : null;
        infantryCollider = infantryButton != null ? infantryButton.GetComponent<Collider>() : null;
        archerCollider = archerButton != null ? archerButton.GetComponent<Collider>() : null;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = panel.sizeDelta;
        ApplyWorldSpaceCanvas();
        EnsureMenuColliders();
    }

    private void ApplyWorldSpaceCanvas()
    {
        if (canvas == null)
        {
            return;
        }

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = SiegePlayEnvironment.ResolveViewCamera();
        canvas.transform.localScale = Vector3.one * worldScale;
    }

    private void EnsureMenuColliders()
    {
        if (panel != null)
        {
            panelCollider = EnsureBoxCollider(panel.gameObject, panel.sizeDelta);
        }

        if (infantryButton != null)
        {
            RectTransform infantryRect = infantryButton.transform as RectTransform;
            infantryCollider = EnsureBoxCollider(
                infantryButton.gameObject,
                infantryRect != null ? infantryRect.sizeDelta : new Vector2(200f, 36f));
            infantryImage = infantryButton.GetComponent<Image>();
        }

        if (archerButton != null)
        {
            RectTransform archerRect = archerButton.transform as RectTransform;
            archerCollider = EnsureBoxCollider(
                archerButton.gameObject,
                archerRect != null ? archerRect.sizeDelta : new Vector2(200f, 36f));
            archerImage = archerButton.GetComponent<Image>();
        }
    }

    private static Text CreateLabel(RectTransform parent, string name, Vector2 anchoredPosition, int fontSize, TextAnchor anchor)
    {
        GameObject labelObject = new GameObject(name);
        labelObject.transform.SetParent(parent, false);
        RectTransform rect = labelObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(200f, 28f);
        Text text = labelObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static Button CreateButton(
        RectTransform parent,
        string name,
        Vector2 anchoredPosition,
        out Text label)
    {
        GameObject buttonObject = new GameObject(name);
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(200f, 36f);
        Image image = buttonObject.AddComponent<Image>();
        image.color = ButtonNormal;
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        EnsureBoxCollider(buttonObject, rect.sizeDelta);

        GameObject labelObject = new GameObject("Label");
        labelObject.transform.SetParent(buttonObject.transform, false);
        RectTransform labelRect = labelObject.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        label = labelObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.fontSize = 18;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.raycastTarget = false;
        return button;
    }

    private static BoxCollider EnsureBoxCollider(GameObject target, Vector2 size)
    {
        BoxCollider box = target.GetComponent<BoxCollider>();
        if (box == null)
        {
            box = target.AddComponent<BoxCollider>();
        }

        box.size = new Vector3(size.x, size.y, 24f);
        box.center = Vector3.zero;
        return box;
    }
}
