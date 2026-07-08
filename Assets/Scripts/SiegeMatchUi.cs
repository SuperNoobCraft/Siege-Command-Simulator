using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Drives match UI labels. Position each SiegeUiLabel yourself in the Canvas; this script only sets text/visibility.
/// </summary>
[DefaultExecutionOrder(-20)]
public class SiegeMatchUi : MonoBehaviour
{
    public enum EditorPreviewMode
    {
        Playing,
        Victory,
        Defeat
    }

    [Header("Labels - position freely in the Canvas")]
    [SerializeField] private SiegeUiLabel statusLabel;
    [SerializeField] private SiegeUiLabel countdownLabel;
    [FormerlySerializedAs("arrowHitsLabel")]
    [SerializeField] private SiegeUiLabel commanderHpLabel;
    [SerializeField] private SiegeUiLabel victoryLabel;
    [SerializeField] private SiegeUiLabel defeatLabel;
    [SerializeField] private SiegeUiLabel restartLabel;

    [Header("Optional Groups")]
    [Tooltip("Optional parent toggled on while playing. Leave empty to only toggle individual labels.")]
    [SerializeField] private GameObject playingGroup;
    [Tooltip("Optional parent toggled on for win/lose screens.")]
    [SerializeField] private GameObject endGameGroup;

    [Header("Restart")]
    [SerializeField] private bool enableClickToRestart = true;

    [Header("Messages")]
    [SerializeField] private string openingStatus = "Defend the cannons.";
    [SerializeField] private bool showOpeningStatusOnStart = true;
    [SerializeField, Min(0f)] private float openingStatusDuration = 3f;
    [SerializeField] private string commanderHpFormat = "{0}";
    [SerializeField] private string wave3CountdownFormat = "Cannons ready in {0:0}s";
    [SerializeField] private string victoryMessage = "The walls have fallen. Victory!";
    [SerializeField] private string arrowDefeatMessage = "The commander has fallen.";
    [SerializeField] private string cannonDefeatMessage = "The cannons were overrun.";
    [SerializeField] private string restartPrompt = "Click anywhere to restart.";

    [Header("Editor Preview")]
    [SerializeField] private bool previewInEditor = true;
    [SerializeField] private EditorPreviewMode editorPreview = EditorPreviewMode.Playing;

    private bool awaitingRestart;
    private bool isBoundToManager;
    private Coroutine openingStatusCoroutine;
    private SiegeGameManager boundManager;

    public static SiegeMatchUi Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
        ResolveMissingLabels();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
    private void OnEnable()
    {
        if (Application.isPlaying)
        {
            TryBindManager();
        }

        if (!Application.isPlaying)
        {
            ApplyEditorPreview();
        }
    }

    private void Start()
    {
        TryBindManager();

        ApplyPlayingLayout();
        awaitingRestart = false;
        RefreshCannonCountdown();
        RefreshCommanderHpDisplay();

        if (showOpeningStatusOnStart && !string.IsNullOrWhiteSpace(openingStatus))
        {
            SetLabelText(statusLabel, openingStatus, false);
            if (openingStatusDuration > 0f)
            {
                openingStatusCoroutine = StartCoroutine(ClearOpeningStatusAfterDelay());
            }
            else
            {
                SetLabelText(statusLabel, string.Empty, false);
            }
        }
    }

    private IEnumerator ClearOpeningStatusAfterDelay()
    {
        yield return new WaitForSeconds(openingStatusDuration);
        SetLabelText(statusLabel, string.Empty, false);
        openingStatusCoroutine = null;
    }

    private void Update()
    {
        TryBindManager();

        SiegeGameManager manager = boundManager;
        if (manager != null && manager.IsPlaying)
        {
            RefreshCannonCountdown();
        }

        if (!awaitingRestart || !enableClickToRestart)
        {
            return;
        }

        if (manager == null || manager.IsPlaying)
        {
            return;
        }

        if (WasRestartClickPressed())
        {
            manager.RestartMatch();
        }
    }

    private void OnDisable()
    {
        if (openingStatusCoroutine != null)
        {
            StopCoroutine(openingStatusCoroutine);
            openingStatusCoroutine = null;
        }

        if (Application.isPlaying && boundManager != null)
        {
            Unbind(boundManager);
        }
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            ApplyEditorPreview();
        }
    }

    private void TryBindManager()
    {
        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager == boundManager)
        {
            return;
        }

        if (boundManager != null)
        {
            Unbind(boundManager);
        }

        if (manager != null)
        {
            Bind(manager);
        }
    }

    private void Bind(SiegeGameManager manager)
    {
        if (isBoundToManager && boundManager == manager)
        {
            return;
        }

        boundManager = manager;
        isBoundToManager = true;
        manager.MatchStateChanged += HandleMatchStateChanged;
        manager.CannonFireCountdownUpdated += HandleCannonCountdownUpdated;
        HandleMatchStateChanged(manager.CurrentState);
        HandleCannonCountdownUpdated(manager.SecondsUntilCannonsFire);
    }

    private void Unbind(SiegeGameManager manager)
    {
        if (!isBoundToManager || manager == null)
        {
            boundManager = null;
            isBoundToManager = false;
            return;
        }

        manager.MatchStateChanged -= HandleMatchStateChanged;
        manager.CannonFireCountdownUpdated -= HandleCannonCountdownUpdated;
        boundManager = null;
        isBoundToManager = false;
    }

    public void UpdateCommanderHp(int hitsRemaining, int maxHits)
    {
        if (commanderHpLabel == null)
        {
            return;
        }

        if (SiegeGameManager.Instance != null
            && SiegeGameManager.Instance.CurrentState != SiegeGameManager.MatchState.Playing)
        {
            return;
        }

        string message = string.Format(commanderHpFormat, hitsRemaining);
        commanderHpLabel.SetText(message, true);
        commanderHpLabel.SetVisible(true);
    }

    private void ResolveMissingLabels()
    {
        SiegeUiLabel[] labels = GetComponentsInChildren<SiegeUiLabel>(true);
        for (int i = 0; i < labels.Length; i++)
        {
            SiegeUiLabel label = labels[i];
            if (label == null)
            {
                continue;
            }

            string name = label.gameObject.name;
            if (commanderHpLabel == null && ContainsNameToken(name, "commanderhp", "arrowhit", "hp"))
            {
                commanderHpLabel = label;
            }
            else if (countdownLabel == null && ContainsNameToken(name, "countdown", "cannon"))
            {
                countdownLabel = label;
            }
            else if (statusLabel == null && ContainsNameToken(name, "status", "opening"))
            {
                statusLabel = label;
            }
        }
    }

    private static bool ContainsNameToken(string objectName, params string[] tokens)
    {
        string normalized = objectName.Replace(" ", string.Empty).ToLowerInvariant();
        for (int i = 0; i < tokens.Length; i++)
        {
            if (normalized.Contains(tokens[i]))
            {
                return true;
            }
        }

        return false;
    }

    private void HandleMatchStateChanged(SiegeGameManager.MatchState state)
    {
        awaitingRestart = state != SiegeGameManager.MatchState.Playing;

        switch (state)
        {
            case SiegeGameManager.MatchState.Won:
                ApplyVictoryLayout();
                break;
            case SiegeGameManager.MatchState.Lost:
                ApplyDefeatLayout(SiegeGameManager.Instance != null ? SiegeGameManager.Instance.DefeatReason : string.Empty);
                break;
            default:
                ApplyPlayingLayout();
                RefreshCommanderHpDisplay();
                RefreshCannonCountdown();
                break;
        }
    }

    private void HandleCannonCountdownUpdated(float secondsRemaining)
    {
        if (SiegeGameManager.Instance != null
            && SiegeGameManager.Instance.CurrentState != SiegeGameManager.MatchState.Playing)
        {
            return;
        }

        SetLabelText(countdownLabel, string.Format(wave3CountdownFormat, secondsRemaining), true);
        if (countdownLabel != null)
        {
            countdownLabel.SetVisible(true);
        }
    }

    private void RefreshCannonCountdown()
    {
        SiegeGameManager manager = boundManager;
        if (manager != null && manager.CurrentState != SiegeGameManager.MatchState.Playing)
        {
            return;
        }

        float secondsRemaining = manager != null ? manager.SecondsUntilCannonsFire : 0f;
        SetLabelText(countdownLabel, string.Format(wave3CountdownFormat, secondsRemaining), true);
    }

    private void RefreshCommanderHpDisplay()
    {
        SiegeCommanderArrowHealth commanderHealth = SiegeCommanderArrowHealth.Instance;
        int maxHits = commanderHealth != null ? commanderHealth.MaxHits : 3;
        int hitsRemaining = commanderHealth != null ? commanderHealth.HitsRemaining : maxHits;
        UpdateCommanderHp(hitsRemaining, maxHits);
    }

    private void ApplyPlayingLayout()
    {
        SetGroupActive(playingGroup, true);
        SetGroupActive(endGameGroup, false);

        if (!showOpeningStatusOnStart)
        {
            SetLabelText(statusLabel, string.Empty, false);
        }

        SetLabelText(victoryLabel, string.Empty, false);
        SetLabelText(defeatLabel, string.Empty, false);
        SetLabelText(restartLabel, string.Empty, false);
        RefreshCommanderHpDisplay();
    }

    private void ApplyVictoryLayout()
    {
        SetGroupActive(playingGroup, false);
        SetGroupActive(endGameGroup, true);

        SetLabelText(statusLabel, string.Empty, false);
        SetLabelText(commanderHpLabel, string.Empty, true);
        SetLabelText(victoryLabel, victoryMessage, false);
        SetLabelText(defeatLabel, string.Empty, false);
        SetLabelText(restartLabel, enableClickToRestart ? restartPrompt : string.Empty, false);
    }

    private void ApplyDefeatLayout(string reason)
    {
        SetGroupActive(playingGroup, false);
        SetGroupActive(endGameGroup, true);

        SetLabelText(statusLabel, string.Empty, false);
        SetLabelText(commanderHpLabel, string.Empty, true);
        SetLabelText(victoryLabel, string.Empty, false);
        SetLabelText(defeatLabel, ResolveDefeatMessage(reason), false);
        SetLabelText(restartLabel, enableClickToRestart ? restartPrompt : string.Empty, false);
    }

    private void ApplyEditorPreview()
    {
        if (!previewInEditor || Application.isPlaying)
        {
            return;
        }

        switch (editorPreview)
        {
            case EditorPreviewMode.Victory:
                ApplyVictoryLayout();
                break;
            case EditorPreviewMode.Defeat:
                ApplyDefeatLayout(cannonDefeatMessage);
                break;
            default:
                ApplyPlayingLayout();
                SetLabelText(statusLabel, openingStatus, false);
                SetLabelText(commanderHpLabel, string.Format(commanderHpFormat, 3), true);
                SetLabelText(countdownLabel, string.Format(wave3CountdownFormat, 42f), true);
                break;
        }
    }

    private string ResolveDefeatMessage(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "Defeat.";
        }

        if (reason.IndexOf("arrow", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return arrowDefeatMessage;
        }

        if (reason.IndexOf("cannon", System.StringComparison.OrdinalIgnoreCase) >= 0
            || reason.IndexOf("occupied", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return cannonDefeatMessage;
        }

        return reason;
    }

    private static void SetLabelText(SiegeUiLabel label, string message, bool keepVisible)
    {
        if (label == null)
        {
            return;
        }

        label.SetText(message, keepVisible);
    }

    private static void SetGroupActive(GameObject group, bool active)
    {
        if (group != null)
        {
            group.SetActive(active);
        }
    }

    private static bool WasRestartClickPressed()
    {
        if (Input.GetMouseButtonDown(0))
        {
            return true;
        }

        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            return touch.phase == TouchPhase.Began;
        }

        return false;
    }
}
