using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Small popup to raise Infantry or Archer when clicking controlled territory.
/// </summary>
[DisallowMultipleComponent]
public class PointCaptureRaiseMenu : MonoBehaviour
{
    [SerializeField] private PointCaptureMatch match;
    [SerializeField] private PointCaptureSpawner spawner;
    [SerializeField] private Canvas canvas;
    [SerializeField] private RectTransform panel;
    [SerializeField] private Text titleText;
    [SerializeField] private Button infantryButton;
    [SerializeField] private Button archerButton;
    [SerializeField] private Text infantryCostText;
    [SerializeField] private Text archerCostText;

    private Vector3 pendingWorldPosition;
    private CaptureOwner pendingOwner = CaptureOwner.Neutral;
    private Action<string> onRaiseFailed;
    private int openedOnFrame = -1;

    public bool IsOpen => panel != null && panel.gameObject.activeSelf;

    public void Configure(PointCaptureMatch captureMatch, PointCaptureSpawner captureSpawner)
    {
        match = captureMatch;
        spawner = captureSpawner;
        EnsureUi();
        Hide();
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

        EnsureUi();
        Hide();
    }

    private void Update()
    {
        if (!IsOpen || Time.frameCount == openedOnFrame)
        {
            return;
        }

        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
        {
            if (!IsPointerOverPanel())
            {
                Hide();
            }
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Hide();
        }

        ValidatePendingRaiseLocation();
    }

    public void Show(Vector3 worldPosition, CaptureOwner owner, Vector2 screenPosition)
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

        panel.gameObject.SetActive(true);
        openedOnFrame = Time.frameCount;
        LayoutPanelAt(screenPosition);
    }

    public void Hide()
    {
        if (panel != null)
        {
            panel.gameObject.SetActive(false);
        }
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
            Hide();
            return;
        }

        if (!spawner.TryRaise(pendingOwner, regimentType, pendingWorldPosition, out failReason))
        {
            onRaiseFailed?.Invoke(failReason);
        }

        Hide();
    }

    private bool IsPointerOverPanel()
    {
        if (panel == null)
        {
            return false;
        }

        return RectTransformUtility.RectangleContainsScreenPoint(panel, Input.mousePosition, null);
    }

    private void ValidatePendingRaiseLocation()
    {
        PointCaptureBoard board = PointCaptureBoard.Instance;
        if (board == null)
        {
            Hide();
            return;
        }

        if (board.CanRaiseAt(pendingOwner, pendingWorldPosition, out _))
        {
            return;
        }

        Hide();
        onRaiseFailed?.Invoke("This village can no longer raise regiments.");
    }

    private void LayoutPanelAt(Vector2 screenPosition)
    {
        const float width = 220f;
        const float height = 118f;
        Vector2 anchored = screenPosition;
        anchored.x = Mathf.Clamp(anchored.x, width * 0.5f + 8f, Screen.width - width * 0.5f - 8f);
        anchored.y = Mathf.Clamp(anchored.y, height * 0.5f + 8f, Screen.height - height * 0.5f - 8f);
        panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        panel.position = anchored;
    }

    private void EnsureUi()
    {
        if (panel != null)
        {
            return;
        }

        if (EventSystem.current == null)
        {
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }

        GameObject canvasObject = new GameObject("PointCaptureRaiseCanvas");
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject panelObject = new GameObject("RaisePanel");
        panelObject.transform.SetParent(canvasObject.transform, false);
        panel = panelObject.AddComponent<RectTransform>();
        Image panelImage = panelObject.AddComponent<Image>();
        panelImage.color = new Color(0.08f, 0.1f, 0.12f, 0.94f);

        titleText = CreateLabel(panel, "Title", new Vector2(0f, -14f), 20, TextAnchor.UpperCenter);
        infantryButton = CreateButton(panel, "InfantryButton", new Vector2(0f, -48f), OnInfantryClicked, out infantryCostText);
        archerButton = CreateButton(panel, "ArcherButton", new Vector2(0f, -88f), OnArcherClicked, out archerCostText);
    }

    private void OnInfantryClicked()
    {
        Raise(PointCaptureSpawner.RegimentType.Infantry);
    }

    private void OnArcherClicked()
    {
        Raise(PointCaptureSpawner.RegimentType.Archer);
    }

    private static Text CreateLabel(RectTransform parent, string name, Vector2 anchoredPosition, int fontSize, TextAnchor anchor)
    {
        GameObject labelObject = new GameObject(name);
        labelObject.transform.SetParent(parent, false);
        RectTransform rect = labelObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
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
        UnityEngine.Events.UnityAction onClick,
        out Text label)
    {
        GameObject buttonObject = new GameObject(name);
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(190f, 30f);
        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.22f, 0.28f, 0.34f, 1f);
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

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
}
