using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Drives world-space match UI labels. Place <see cref="SiegeWorldUiLabel"/> signs in the scene.
/// </summary>
[DefaultExecutionOrder(100)]
public class SiegeMatchUi : MonoBehaviour
{
    public enum EditorPreviewMode
    {
        SelectingDifficulty,
        DefendCannonsSubmenu,
        DodgeArrowsSubmenu,
        Credits,
        Playing,
        Victory,
        Defeat
    }

    [Header("World Labels (Attacker / default)")]
    [FormerlySerializedAs("canvasRoot")]
    [SerializeField] private GameObject legacyCanvasRoot;
    [SerializeField] private GameObject worldUiRoot;
    [SerializeField] private SiegeWorldUiLabel worldStatusLabel;
    [SerializeField] private SiegeWorldUiLabel worldCountdownLabel;
    [SerializeField] private SiegeWorldUiLabel worldCommanderHpLabel;
    [SerializeField] private SiegeWorldUiLabel worldVictoryLabel;
    [SerializeField] private SiegeWorldUiLabel worldDefeatLabel;
    [SerializeField] private SiegeWorldUiLabel worldRestartLabel;

    [Header("World Labels (Defender PVP duplicate — assign a second set at the defender viewpoint)")]
    [Tooltip("Optional root toggled on for the defender during Siege PVP. Leave empty to reuse attacker labels.")]
    [SerializeField] private GameObject defenderWorldUiRoot;
    [SerializeField] private SiegeWorldUiLabel defenderStatusLabel;
    [SerializeField] private SiegeWorldUiLabel defenderCountdownLabel;
    [SerializeField] private SiegeWorldUiLabel defenderCommanderHpLabel;
    [SerializeField] private SiegeWorldUiLabel defenderVictoryLabel;
    [SerializeField] private SiegeWorldUiLabel defenderDefeatLabel;
    [SerializeField] private SiegeWorldUiLabel defenderRestartLabel;

    [Header("Difficulty Select Labels")]
    [Tooltip("Drag the Demo / Full world-space signs here. Also auto-found by name if left empty.")]
    [SerializeField] private SiegeWorldUiLabel[] difficultyOptionLabels;
    [Tooltip("Optional fallback for plain 3D TextMeshPro objects without SiegeWorldUiLabel.")]
    [SerializeField] private GameObject[] difficultyOptionRoots;

    [Header("Credits Panel (world-space 3D text)")]
    [Tooltip("Optional parent toggled while credits are open (place your 3D TMP under this).")]
    [SerializeField] private GameObject creditsPanelRoot;
    [Tooltip("Preferred: world-space SiegeWorldUiLabel sign for the credits body.")]
    [SerializeField] private SiegeWorldUiLabel creditsBodyLabel;
    [Tooltip("Or assign a plain 3D TextMeshPro directly (CAVE-safe; not UI Canvas).")]
    [SerializeField] private TextMeshPro creditsBodyText;
    [TextArea(12, 40)]
    [SerializeField] private string creditsText =
        "Credits\n\n"
        + "Votanic Siege\n\n"
        + "Edit this text on SiegeMatchUi in the Inspector.";

    [Header("Optional Groups")]
    [Tooltip("Optional parent toggled on while choosing difficulty.")]
    [SerializeField] private GameObject difficultySelectGroup;
    [Tooltip("Optional parent toggled on while playing. Leave empty to only toggle individual labels.")]
    [SerializeField] private GameObject playingGroup;
    [Tooltip("Optional parent toggled on for win/lose screens.")]
    [SerializeField] private GameObject endGameGroup;

    [Header("Restart")]
    [SerializeField] private bool enableClickToRestart = true;
    [SerializeField, Min(0f)] private float restartDelaySeconds = 2f;

    [Header("Messages")]
    [SerializeField] private string difficultySelectPrompt = "Select Defend the Cannons, Dodge Arrows, or Siege PVP.";
    [Tooltip("Shown on the networking client / CAVE defender at mode select. PVP works here; other modes are tuned for DASE Cave.")]
    [SerializeField] private string caveClientEnvironmentNote =
        "Note: Defend the Cannons and Dodge Arrows are designed for DASE Cave. For the intended full experience, use the other CAVE.";
    [SerializeField] private string openingStatus = "Defend the cannons.";
    [SerializeField] private string dodgeArrowsOpeningStatus = "Dodge the arrows until your cannons are ready!";
    [SerializeField] private string dodgeArrowsSubmenuPrompt = "Dodge Arrows — choose Timed or Endless Survival.";
    [SerializeField] private string defendCannonsSubmenuPrompt = "Defend the Cannons — choose Demo or Full.";
    [SerializeField] private string creditsPanelPrompt = "Credits";
    [SerializeField] private string dodgeArrowsEndlessOpeningStatus = "Endless Survival — dodge as long as you can. One hit ends the run.";
    [SerializeField] private string dodgeArrowsEndlessHudFormat = "Time {0:0.00}s";
    [SerializeField] private string dodgeArrowsEndlessDefeatFormat = "You survived {0:0.00} seconds";
    [SerializeField] private string dodgeArrowsEndlessBestFormat = "All-time best: {0:0.00} seconds";
    [SerializeField] private string dodgeArrowsEndlessNewBestFormat = "New all-time best: {0:0.00} seconds!";
    [SerializeField] private string siegePvpAttackerOpeningStatus = "Siege the walls — hold until the cannons are ready!";
    [SerializeField] private string siegePvpDefenderOpeningStatus = "Stop the siege — break through and silence the cannons!";
    [SerializeField] private bool showOpeningStatusOnStart = true;
    [SerializeField, Min(0f)] private float openingStatusDuration = 3f;
    [SerializeField] private string commanderHpFormat = "{0}";
    [SerializeField] private string wave3CountdownFormat = "Cannons ready in {0:0}s";
    [SerializeField] private string victoryMessage = "The walls have fallen. Victory!";
    [Tooltip("Defender HUD when attacker wins (GM Won / walls fallen).")]
    [SerializeField] private string defenderDefeatWhenAttackerWinsMessage = "The walls have fallen. Defeat.";
    [Tooltip("Defender HUD when cannons are silenced (GM Lost / overrun).")]
    [SerializeField] private string defenderVictoryWhenCannonsSilencedMessage = "The cannons are silenced. Victory!";
    [Tooltip("Defender HUD when the attacking commander is killed by arrows.")]
    [SerializeField] private string defenderVictoryWhenCommanderArrowedMessage = "The commander has fallen. Victory!";
    [Tooltip("Defender HUD when the attacking commander falls from the tower.")]
    [SerializeField] private string defenderVictoryWhenCommanderFellMessage = "The commander fell from the tower. Victory!";
    [SerializeField] private string arrowDefeatMessage = "The commander has fallen.";
    [SerializeField] private string fallDefeatMessage = "The commander fell from the tower.";
    [SerializeField] private string cannonDefeatMessage = "The cannons were overrun.";
    [SerializeField] private string restartPrompt = "Click anywhere to restart.";
    [SerializeField] private string trackedRestartPrompt = "Press any wand button to restart.";

    [Header("Editor Preview")]
    [SerializeField] private bool previewInEditor = true;
    [SerializeField] private EditorPreviewMode editorPreview = EditorPreviewMode.Playing;

    private bool awaitingRestart;
    private bool isBoundToManager;
    private bool modeSelectLayoutApplied;
    private bool pvpLobbyPresentationActive;
    private Coroutine openingStatusCoroutine;
    private Coroutine restartDelayCoroutine;
    private SiegeGameManager boundManager;
    private SiegeDifficultyOption[] difficultyOptions;
    private readonly List<SiegeWorldUiLabel> resolvedDifficultyLabels = new List<SiegeWorldUiLabel>();
    private readonly List<GameObject> resolvedDifficultyRoots = new List<GameObject>();
    private readonly List<SiegeWorldUiLabel> resolvedPlayingLabels = new List<SiegeWorldUiLabel>();
    private readonly List<GameObject> resolvedPlayingRoots = new List<GameObject>();
    private bool pvpHudForDefender;

    public static SiegeMatchUi Instance { get; private set; }

    private bool HasDefenderHudAssigned =>
        defenderWorldUiRoot != null
        || defenderStatusLabel != null
        || defenderVictoryLabel != null
        || defenderDefeatLabel != null;

    private bool UseDefenderHud => pvpHudForDefender && HasDefenderHudAssigned;

    private SiegeWorldUiLabel ActiveStatusLabel =>
        UseDefenderHud && defenderStatusLabel != null ? defenderStatusLabel : worldStatusLabel;

    private SiegeWorldUiLabel ActiveCountdownLabel =>
        UseDefenderHud && defenderCountdownLabel != null ? defenderCountdownLabel : worldCountdownLabel;

    private SiegeWorldUiLabel ActiveCommanderHpLabel =>
        UseDefenderHud && defenderCommanderHpLabel != null ? defenderCommanderHpLabel : worldCommanderHpLabel;

    private SiegeWorldUiLabel ActiveVictoryLabel =>
        UseDefenderHud && defenderVictoryLabel != null ? defenderVictoryLabel : worldVictoryLabel;

    private SiegeWorldUiLabel ActiveDefeatLabel =>
        UseDefenderHud && defenderDefeatLabel != null ? defenderDefeatLabel : worldDefeatLabel;

    private SiegeWorldUiLabel ActiveRestartLabel =>
        UseDefenderHud && defenderRestartLabel != null ? defenderRestartLabel : worldRestartLabel;

    private void Awake()
    {
        Instance = this;
        EnsureDodgeArrowsSubmenuExists();
        EnsureDefendCannonsSubmenuExists();
        EnsureCreditsPanelExists();
        ResolveMissingWorldLabels();
        ResolvePresentationTargets();
        ApplyUiPresentationMode();

        if (Application.isPlaying)
        {
            SiegeGameManager manager = SiegeGameManager.Instance;
            if (manager != null && manager.CurrentState == SiegeGameManager.MatchState.SelectingDifficulty)
            {
                ApplyDifficultySelectLayout();
            }

            StartCoroutine(RefreshModeSelectAfterWarmUp());
        }
    }

    private void ResolvePresentationTargets()
    {
        CacheDifficultyOptions();
        ResolveDifficultyPresentationTargets();
        ResolvePlayingPresentationTargets();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        UnsubscribeFromEnvironmentEvents();
    }

    private void OnEnable()
    {
        SubscribeToEnvironmentEvents();

        if (Application.isPlaying)
        {
            TryBindManager();
        }

        if (!Application.isPlaying)
        {
            ApplyEditorPreview();
        }
    }

    private void OnDisable()
    {
        UnsubscribeFromEnvironmentEvents();

        if (openingStatusCoroutine != null)
        {
            StopCoroutine(openingStatusCoroutine);
            openingStatusCoroutine = null;
        }

        if (restartDelayCoroutine != null)
        {
            StopCoroutine(restartDelayCoroutine);
            restartDelayCoroutine = null;
        }

        if (Application.isPlaying && boundManager != null)
        {
            Unbind(boundManager);
        }
    }

    private void SubscribeToEnvironmentEvents()
    {
        SiegePlayEnvironment.EnvironmentChanged += HandlePlayEnvironmentChanged;
    }

    private void UnsubscribeFromEnvironmentEvents()
    {
        SiegePlayEnvironment.EnvironmentChanged -= HandlePlayEnvironmentChanged;
    }

    private void HandlePlayEnvironmentChanged()
    {
        ApplyUiPresentationMode();

        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager != null && manager.CurrentState == SiegeGameManager.MatchState.SelectingDifficulty)
        {
            if (!IsPvpLobbyActive())
            {
                ApplyDifficultySelectLayout();
            }

            return;
        }

        RefreshCommanderHpDisplay();
        RefreshCannonCountdown();
    }

    private void Start()
    {
        ApplyUiPresentationMode();
        TryBindManager();

        awaitingRestart = false;

        SiegeGameManager manager = boundManager;
        if (manager != null && manager.CurrentState == SiegeGameManager.MatchState.SelectingDifficulty)
        {
            ApplyDifficultySelectLayout();
            return;
        }

        BeginPlayingPresentation();
        StartCoroutine(RefreshModeSelectAfterWarmUp());
    }

    private IEnumerator RefreshModeSelectAfterWarmUp()
    {
        while (!SiegeSceneBootstrap.IsWarmUpComplete)
        {
            yield return null;
        }

        yield return null;

        if (pvpLobbyPresentationActive || IsPvpLobbyActive())
        {
            yield break;
        }

        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager != null && manager.CurrentState == SiegeGameManager.MatchState.SelectingDifficulty)
        {
            ApplyDifficultySelectLayout();
        }
    }

    private static bool IsPvpLobbyActive()
    {
        SiegePvpSession pvp = SiegePvpSession.Instance;
        return pvp != null && pvp.IsInPvpLobby;
    }

    private void BeginPlayingPresentation()
    {
        ApplyPlayingLayout();
        RefreshCommanderHpDisplay();
        RefreshCannonCountdown();

        if (!showOpeningStatusOnStart || string.IsNullOrWhiteSpace(GetActiveOpeningStatus()))
        {
            return;
        }

        SetWorldLabelText(ActiveStatusLabel, GetActiveOpeningStatus(), false);
        if (openingStatusDuration > 0f)
        {
            if (openingStatusCoroutine != null)
            {
                StopCoroutine(openingStatusCoroutine);
            }

            openingStatusCoroutine = StartCoroutine(ClearOpeningStatusAfterDelay());
        }
        else
        {
            SetWorldLabelText(ActiveStatusLabel, string.Empty, false);
        }
    }

    private IEnumerator ClearOpeningStatusAfterDelay()
    {
        yield return new WaitForSeconds(openingStatusDuration);
        SetWorldLabelText(ActiveStatusLabel, string.Empty, false);
        openingStatusCoroutine = null;
    }

    private void Update()
    {
        TryBindManager();

        SiegeGameManager manager = boundManager;
        if (manager != null
            && manager.CurrentState == SiegeGameManager.MatchState.SelectingDifficulty
            && SiegeSceneBootstrap.IsWarmUpComplete
            && !modeSelectLayoutApplied
            && !pvpLobbyPresentationActive
            && !IsPvpLobbyActive())
        {
            ApplyDifficultySelectLayout();
        }

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
            SiegePvpSession pvp = SiegePvpSession.Instance;
            if (SiegeMatchSettings.IsSiegePvpMode || (pvp != null && pvp.BlocksModeSelect))
            {
                if (pvp != null)
                {
                    pvp.NotifyLocalReturnToMenu();
                }
                else
                {
                    manager.RestartMatch();
                }
            }
            else
            {
                manager.RestartMatch();
            }
        }
    }

    private bool WasRestartClickPressed()
    {
        return SiegeVrInput.WasPointerPressedThisFrame();
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
        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager != null && manager.CurrentState != SiegeGameManager.MatchState.Playing)
        {
            SetLabelVisible(ActiveCommanderHpLabel, false);
            HidePlayingHudTargets();
            return;
        }

        string message = string.Format(commanderHpFormat, hitsRemaining);
        SetWorldLabelText(ActiveCommanderHpLabel, message, true);
        SetLabelVisible(ActiveCommanderHpLabel, true);
    }

    private void ResolveMissingWorldLabels()
    {
        SiegeWorldUiLabel[] labels = GetComponentsInChildren<SiegeWorldUiLabel>(true);
        for (int i = 0; i < labels.Length; i++)
        {
            SiegeWorldUiLabel label = labels[i];
            if (label == null)
            {
                continue;
            }

            string name = label.gameObject.name;
            if (worldCommanderHpLabel == null && ContainsNameToken(name, "commanderhp", "arrowhit", "hp"))
            {
                worldCommanderHpLabel = label;
            }
            else if (worldCountdownLabel == null && ContainsNameToken(name, "countdown", "cannon"))
            {
                worldCountdownLabel = label;
            }
            else if (worldStatusLabel == null && ContainsNameToken(name, "status", "opening"))
            {
                worldStatusLabel = label;
            }
            else if (worldVictoryLabel == null && ContainsNameToken(name, "victory", "win"))
            {
                worldVictoryLabel = label;
            }
            else if (worldDefeatLabel == null && ContainsNameToken(name, "defeat", "lose"))
            {
                worldDefeatLabel = label;
            }
            else if (worldRestartLabel == null && ContainsNameToken(name, "restart"))
            {
                worldRestartLabel = label;
            }
        }
    }

    private void ApplyUiPresentationMode()
    {
        if (legacyCanvasRoot != null)
        {
            legacyCanvasRoot.SetActive(false);
        }

        if (worldUiRoot != null)
        {
            worldUiRoot.SetActive(true);
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

    public void ShowModeSelect()
    {
        pvpLobbyPresentationActive = false;
        CancelRestartDelay();
        if (openingStatusCoroutine != null)
        {
            StopCoroutine(openingStatusCoroutine);
            openingStatusCoroutine = null;
        }

        awaitingRestart = false;
        SiegeDodgeArrowsSubmenu dodgeSubmenu = SiegeDodgeArrowsSubmenu.Instance;
        if (dodgeSubmenu != null && dodgeSubmenu.IsSubmenuOpen)
        {
            dodgeSubmenu.CloseSubmenu();
        }

        SiegeDefendCannonsSubmenu defendSubmenu = SiegeDefendCannonsSubmenu.Instance;
        if (defendSubmenu != null && defendSubmenu.IsSubmenuOpen)
        {
            defendSubmenu.CloseSubmenu();
        }

        if (SiegeCreditsPanel.Instance != null)
        {
            SiegeCreditsPanel.Instance.ClosePanel();
        }

        CacheDifficultyOptions();
        ResolveDifficultyPresentationTargets();
        ApplyDifficultySelectLayout();
    }

    /// <summary>Restore the default mode-select status line (clears stale PVP lobby text).</summary>
    public void RestoreModeSelectStatus()
    {
        ResolvePresentationTargets();
        EnsureModeSelectStatusVisible(BuildDifficultySelectPrompt());
    }

    public void ShowDodgeArrowsSubmenuPrompt()
    {
        ResolvePresentationTargets();
        EnsureModeSelectStatusVisible(dodgeArrowsSubmenuPrompt);
    }

    public void ShowDefendCannonsSubmenuPrompt()
    {
        ResolvePresentationTargets();
        EnsureModeSelectStatusVisible(defendCannonsSubmenuPrompt);
    }

    public void ShowCreditsPanelPrompt()
    {
        ResolvePresentationTargets();
        ApplyCreditsBodyText();
        SetCreditsPanelVisible(true);
        EnsureModeSelectStatusVisible(creditsPanelPrompt);
    }

    public void HideCreditsPanelContent()
    {
        SetCreditsPanelVisible(false);
    }

    public void ApplyCreditsBodyText()
    {
        string text = creditsText ?? string.Empty;

        if (creditsBodyText == null && creditsBodyLabel != null)
        {
            creditsBodyText = creditsBodyLabel.GetComponentInChildren<TextMeshPro>(true);
        }

        if (creditsBodyLabel != null)
        {
            // Only update the string — visibility is owned by SetCreditsPanelVisible.
            TextMeshPro labelTmp = creditsBodyLabel.GetComponentInChildren<TextMeshPro>(true);
            if (labelTmp != null)
            {
                labelTmp.text = text;
            }
        }

        if (creditsBodyText != null)
        {
            creditsBodyText.text = text;
        }
    }

    public void SetCreditsPanelVisible(bool visible)
    {
        if (visible)
        {
            ApplyCreditsBodyText();
        }

        if (creditsPanelRoot != null && creditsPanelRoot.activeSelf != visible)
        {
            creditsPanelRoot.SetActive(visible);
        }

        if (creditsBodyLabel != null)
        {
            creditsBodyLabel.SetVisible(visible);
        }

        if (creditsBodyText != null)
        {
            creditsBodyText.enabled = visible;
            if (creditsBodyLabel == null
                && (creditsPanelRoot == null
                    || !creditsBodyText.transform.IsChildOf(creditsPanelRoot.transform))
                && creditsBodyText.gameObject.activeSelf != visible)
            {
                creditsBodyText.gameObject.SetActive(visible);
            }
        }
    }

    public void SetStatusMessage(string message)
    {
        SetLobbyStatusMessage(message ?? string.Empty);
    }

    /// <summary>PVP lobby / countdown lines — does not restore the main menu layout.</summary>
    public void SetLobbyStatusMessage(string message)
    {
        if (worldUiRoot != null)
        {
            worldUiRoot.SetActive(true);
        }

        SiegeWorldUiLabel status = ActiveStatusLabel;
        SetWorldLabelText(status, message, keepVisible: true);
        SetLabelVisible(status, true);
    }

    private void EnsureModeSelectStatusVisible(string message)
    {
        if (worldUiRoot != null)
        {
            worldUiRoot.SetActive(true);
        }

        if (defenderWorldUiRoot != null && !pvpHudForDefender)
        {
            defenderWorldUiRoot.SetActive(false);
        }

        SiegeWorldUiLabel status = ActiveStatusLabel;
        SetWorldLabelText(status, message, keepVisible: true);
        SetLabelVisible(status, true);
    }

    /// <summary>
    /// Show attacker or defender world HUD for Siege PVP. Assign defender labels in the inspector.
    /// </summary>
    public void ApplyPvpRoleHud(bool forDefender)
    {
        pvpHudForDefender = forDefender && HasDefenderHudAssigned;
        ApplyRoleHudVisibility();
    }

    /// <summary>Legacy no-op kept so older callers compile; prefer <see cref="ApplyPvpRoleHud"/>.</summary>
    public void AnchorPlayingHudToViewpoint(Transform viewpoint, float forwardOffset = 3.5f, float upOffset = 1.5f)
    {
        ApplyPvpRoleHud(true);
    }

    /// <summary>Legacy alias — returns to attacker/default HUD.</summary>
    public void RestorePlayingHudAnchors()
    {
        ApplyPvpRoleHud(false);
    }

    private void ApplyRoleHudVisibility()
    {
        bool defender = UseDefenderHud;
        if (worldUiRoot != null && defenderWorldUiRoot != null)
        {
            worldUiRoot.SetActive(!defender);
            defenderWorldUiRoot.SetActive(defender);
        }
        else if (defenderWorldUiRoot != null)
        {
            defenderWorldUiRoot.SetActive(defender);
        }

        // End-game labels stay hidden until ApplyVictory/DefeatLayout. Activating the
        // defender root must not reveal scene-default victory/defeat text mid-match.
        SetLabelVisible(worldVictoryLabel, false);
        SetLabelVisible(worldDefeatLabel, false);
        SetLabelVisible(defenderVictoryLabel, false);
        SetLabelVisible(defenderDefeatLabel, false);

        if (defender)
        {
            SetLabelVisible(ActiveRestartLabel, false);
            SetLabelVisible(ActiveCountdownLabel, false);
            SetLabelVisible(ActiveCommanderHpLabel, false);
        }
        else
        {
            SetLabelVisible(defenderRestartLabel, false);
            SetLabelVisible(defenderCountdownLabel, false);
            SetLabelVisible(defenderCommanderHpLabel, false);
        }
    }

    public void HideDifficultyOptionsForPvpLobby()
    {
        pvpLobbyPresentationActive = true;
        modeSelectLayoutApplied = true;
        ResolvePresentationTargets();
        SetDifficultyPresentationVisible(false);
        SetGroupActive(difficultySelectGroup, false);
        SetGroupActive(playingGroup, false);
        SetGroupActive(endGameGroup, false);
        if (defenderWorldUiRoot != null)
        {
            defenderWorldUiRoot.SetActive(false);
        }

        HidePlayingHudTargets();
    }

    private void HandleMatchStateChanged(SiegeGameManager.MatchState state)
    {
        CancelRestartDelay();

        switch (state)
        {
            case SiegeGameManager.MatchState.SelectingDifficulty:
                awaitingRestart = false;
                if (IsPvpLobbyActive())
                {
                    break;
                }

                pvpLobbyPresentationActive = false;
                modeSelectLayoutApplied = false;
                RestorePlayingHudAnchors();
                ApplyDifficultySelectLayout();
                break;
            case SiegeGameManager.MatchState.Won:
                awaitingRestart = false;
                ApplyVictoryLayout();
                BeginRestartDelay();
                break;
            case SiegeGameManager.MatchState.Lost:
                awaitingRestart = false;
                ApplyDefeatLayout(SiegeGameManager.Instance != null ? SiegeGameManager.Instance.DefeatReason : string.Empty);
                BeginRestartDelay();
                break;
            default:
                awaitingRestart = false;
                BeginPlayingPresentation();
                break;
        }
    }

    private void BeginRestartDelay()
    {
        CancelRestartDelay();
        if (restartDelaySeconds <= 0f)
        {
            awaitingRestart = enableClickToRestart;
            RefreshEndGameRestartPrompt();
            return;
        }

        restartDelayCoroutine = StartCoroutine(EnableRestartAfterDelay());
    }

    private IEnumerator EnableRestartAfterDelay()
    {
        SetWorldLabelText(ActiveRestartLabel, string.Empty, false);
        yield return new WaitForSecondsRealtime(restartDelaySeconds);
        restartDelayCoroutine = null;
        awaitingRestart = enableClickToRestart;
        RefreshEndGameRestartPrompt();
    }

    private void CancelRestartDelay()
    {
        if (restartDelayCoroutine != null)
        {
            StopCoroutine(restartDelayCoroutine);
            restartDelayCoroutine = null;
        }
    }

    private void RefreshEndGameRestartPrompt()
    {
        if (!awaitingRestart || !enableClickToRestart)
        {
            SetWorldLabelText(ActiveRestartLabel, string.Empty, false);
            return;
        }

        SetWorldLabelText(ActiveRestartLabel, GetActiveRestartPrompt(), false);
    }

    private void HandleCannonCountdownUpdated(float secondsRemaining)
    {
        if (SiegeGameManager.Instance != null
            && SiegeGameManager.Instance.CurrentState != SiegeGameManager.MatchState.Playing)
        {
            return;
        }

        SetCountdownLabelForActiveMode(secondsRemaining);
        if (ActiveCountdownLabel != null)
        {
            ActiveCountdownLabel.SetVisible(true);
        }
    }

    private void RefreshCannonCountdown()
    {
        SiegeGameManager manager = boundManager;
        if (manager != null && manager.CurrentState != SiegeGameManager.MatchState.Playing)
        {
            return;
        }

        float timerValue = manager != null ? manager.GetDodgeArrowsHudTimerSeconds() : 0f;
        SetCountdownLabelForActiveMode(timerValue);
    }

    private void SetCountdownLabelForActiveMode(float timerValue)
    {
        string format = wave3CountdownFormat;
        if (SiegeMatchSettings.IsDodgeArrowsEndlessMode)
        {
            format = dodgeArrowsEndlessHudFormat;
        }
        else if (SiegeMatchSettings.IsDodgeArrowsTimedMode)
        {
            format = wave3CountdownFormat;
        }

        SetWorldLabelText(ActiveCountdownLabel, string.Format(format, timerValue), true);
    }

    private void RefreshCommanderHpDisplay()
    {
        SiegeCommanderArrowHealth commanderHealth = SiegeCommanderArrowHealth.Instance;
        int maxHits = commanderHealth != null ? commanderHealth.MaxHits : 3;
        int hitsRemaining = commanderHealth != null ? commanderHealth.HitsRemaining : maxHits;
        UpdateCommanderHp(hitsRemaining, maxHits);
    }

    private void ApplyDifficultySelectLayout()
    {
        ResolvePresentationTargets();
        pvpHudForDefender = false;
        ApplyRoleHudVisibility();

        SetGroupActive(difficultySelectGroup, true);
        SetGroupActive(playingGroup, false);
        SetGroupActive(endGameGroup, false);

        SetDifficultyPresentationVisible(true);
        HidePlayingHudTargets();

        EnsureModeSelectStatusVisible(BuildDifficultySelectPrompt());
        if (worldStatusLabel != null)
        {
            SetWorldLabelText(worldStatusLabel, BuildDifficultySelectPrompt(), keepVisible: true);
            SetLabelVisible(worldStatusLabel, true);
        }

        modeSelectLayoutApplied = true;
        SetWorldLabelText(worldVictoryLabel, string.Empty, false);
        SetWorldLabelText(worldDefeatLabel, string.Empty, false);
        SetWorldLabelText(defenderVictoryLabel, string.Empty, false);
        SetWorldLabelText(defenderDefeatLabel, string.Empty, false);
        SetWorldLabelText(ActiveRestartLabel, string.Empty, false);
        SetLabelVisible(worldVictoryLabel, false);
        SetLabelVisible(worldDefeatLabel, false);
        SetLabelVisible(defenderVictoryLabel, false);
        SetLabelVisible(defenderDefeatLabel, false);
    }

    private void EnsureDodgeArrowsSubmenuExists()
    {
        if (FindObjectOfType<SiegeDodgeArrowsSubmenu>(true) == null)
        {
            gameObject.AddComponent<SiegeDodgeArrowsSubmenu>();
        }
    }

    private void EnsureDefendCannonsSubmenuExists()
    {
        if (FindObjectOfType<SiegeDefendCannonsSubmenu>(true) == null)
        {
            gameObject.AddComponent<SiegeDefendCannonsSubmenu>();
        }
    }

    private void EnsureCreditsPanelExists()
    {
        if (FindObjectOfType<SiegeCreditsPanel>(true) == null)
        {
            gameObject.AddComponent<SiegeCreditsPanel>();
        }
    }

    private void HidePlayingHudTargets()
    {
        SetLabelVisible(worldCountdownLabel, false);
        SetLabelVisible(worldCommanderHpLabel, false);
        SetLabelVisible(worldVictoryLabel, false);
        SetLabelVisible(worldDefeatLabel, false);
        SetLabelVisible(worldRestartLabel, false);
        SetLabelVisible(defenderCountdownLabel, false);
        SetLabelVisible(defenderCommanderHpLabel, false);
        SetLabelVisible(defenderVictoryLabel, false);
        SetLabelVisible(defenderDefeatLabel, false);
        SetLabelVisible(defenderRestartLabel, false);

        SetWorldLabelText(worldCountdownLabel, string.Empty, false);
        SetWorldLabelText(worldCommanderHpLabel, string.Empty, false);
        SetWorldLabelText(worldVictoryLabel, string.Empty, false);
        SetWorldLabelText(worldDefeatLabel, string.Empty, false);
        SetWorldLabelText(defenderCountdownLabel, string.Empty, false);
        SetWorldLabelText(defenderCommanderHpLabel, string.Empty, false);
        SetWorldLabelText(defenderVictoryLabel, string.Empty, false);
        SetWorldLabelText(defenderDefeatLabel, string.Empty, false);

        for (int i = 0; i < resolvedPlayingLabels.Count; i++)
        {
            SiegeWorldUiLabel label = resolvedPlayingLabels[i];
            if (label == null || label == worldStatusLabel || IsDifficultyLabel(label))
            {
                continue;
            }

            label.SetVisible(false);
        }

        for (int i = 0; i < resolvedPlayingRoots.Count; i++)
        {
            GameObject root = resolvedPlayingRoots[i];
            if (root == null || IsDifficultyRoot(root))
            {
                continue;
            }

            SetRootVisible(root, false);
        }
    }

    private void ShowPlayingHudTargets()
    {
        ApplyRoleHudVisibility();
        SetLabelVisible(ActiveCountdownLabel, true);
        SetLabelVisible(ActiveCommanderHpLabel, true);

        for (int i = 0; i < resolvedPlayingLabels.Count; i++)
        {
            SiegeWorldUiLabel label = resolvedPlayingLabels[i];
            if (label == null
                || label == worldStatusLabel
                || IsDifficultyLabel(label)
                || IsEndGameHudLabel(label))
            {
                continue;
            }

            label.SetVisible(true);
        }

        for (int i = 0; i < resolvedPlayingRoots.Count; i++)
        {
            GameObject root = resolvedPlayingRoots[i];
            if (root == null || IsDifficultyRoot(root) || IsEndGameHudObjectName(root.name))
            {
                continue;
            }

            SetRootVisible(root, true);
        }
    }

    private void SetDifficultyPresentationVisible(bool visible)
    {
        CacheDifficultyOptions();

        if (!visible)
        {
            for (int i = 0; i < resolvedDifficultyLabels.Count; i++)
            {
                SiegeWorldUiLabel label = resolvedDifficultyLabels[i];
                if (label != null)
                {
                    label.SetVisible(false);
                }
            }

            for (int i = 0; i < resolvedDifficultyRoots.Count; i++)
            {
                SetRootVisible(resolvedDifficultyRoots[i], false);
            }

            if (difficultyOptions != null)
            {
                for (int i = 0; i < difficultyOptions.Length; i++)
                {
                    if (difficultyOptions[i] != null)
                    {
                        difficultyOptions[i].SetOptionFullyVisible(false);
                    }
                }
            }

            HideCreditsPanelContent();
            return;
        }

        RefreshDifficultyOptionPresentation();
    }

    /// <summary>Re-applies main-menu vs Dodge/Credits panel visibility without showing every option at once.</summary>
    public void RefreshDifficultyOptionPresentation()
    {
        CacheDifficultyOptions();

        SiegeDodgeArrowsSubmenu submenu = SiegeDodgeArrowsSubmenu.Instance;
        SiegeDefendCannonsSubmenu defendSubmenu = SiegeDefendCannonsSubmenu.Instance;
        SiegeCreditsPanel credits = SiegeCreditsPanel.Instance;
        bool dodgeSubmenuOpen = submenu != null && submenu.IsSubmenuOpen;
        bool defendSubmenuOpen = defendSubmenu != null && defendSubmenu.IsSubmenuOpen;
        bool creditsOpen = credits != null && credits.IsOpen;
        bool defendSubmenuConfigured = SiegeDefendCannonsSubmenu.IsDefendCannonsSubmenuEnabled();

        if (submenu != null)
        {
            submenu.ApplyLayout();
        }

        if (defendSubmenu != null)
        {
            defendSubmenu.ApplyLayout();
        }

        if (credits != null)
        {
            credits.ApplyLayout();
        }
        else if (creditsOpen)
        {
            SetCreditsPanelVisible(true);
        }
        else
        {
            // Editor preview / missing panel component — still hide assigned 3D credits text.
            HideCreditsPanelContent();
        }

        ApplyForcedDifficultyPresentation(dodgeSubmenuOpen, defendSubmenuOpen, creditsOpen, defendSubmenuConfigured);
    }

    private void EnforceDifficultyOptionVisibility(
        bool dodgeSubmenuOpen,
        bool defendSubmenuOpen,
        bool creditsOpen,
        bool defendSubmenuConfigured)
    {
        if (difficultyOptions == null)
        {
            return;
        }

        for (int i = 0; i < difficultyOptions.Length; i++)
        {
            SiegeDifficultyOption option = difficultyOptions[i];
            if (option == null)
            {
                continue;
            }

            bool show;
            if (option.IsCreditsBackButton)
            {
                show = creditsOpen && !dodgeSubmenuOpen && !defendSubmenuOpen;
            }
            else if (option.OpensCreditsPanel)
            {
                show = !creditsOpen && !dodgeSubmenuOpen && !defendSubmenuOpen;
            }
            else if (option.IsDodgeSubmenuChoice)
            {
                show = dodgeSubmenuOpen && !creditsOpen && !defendSubmenuOpen;
            }
            else if (option.IsDefendCannonsSubmenuChoice && defendSubmenuConfigured)
            {
                show = defendSubmenuOpen && !creditsOpen && !dodgeSubmenuOpen;
            }
            else
            {
                show = !dodgeSubmenuOpen && !defendSubmenuOpen && !creditsOpen;
            }

            option.SetOptionFullyVisible(show);
        }
    }

    private bool IsOwnedByDifficultyOption(SiegeWorldUiLabel label)
    {
        if (label == null || difficultyOptions == null)
        {
            return false;
        }

        for (int i = 0; i < difficultyOptions.Length; i++)
        {
            SiegeDifficultyOption option = difficultyOptions[i];
            if (option == null)
            {
                continue;
            }

            if (option.GetPresentationLabel() == label || label.transform.IsChildOf(option.transform))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsOwnedByDifficultyOption(GameObject root)
    {
        if (root == null || difficultyOptions == null)
        {
            return false;
        }

        for (int i = 0; i < difficultyOptions.Length; i++)
        {
            SiegeDifficultyOption option = difficultyOptions[i];
            if (option == null)
            {
                continue;
            }

            if (option.gameObject == root)
            {
                return true;
            }

            SiegeWorldUiLabel optionLabel = option.GetPresentationLabel();
            if (optionLabel != null && optionLabel.gameObject == root)
            {
                return true;
            }

            TextMeshPro optionText = option.GetPresentationText();
            if (optionText != null && optionText.gameObject == root)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldShowUnmanagedDifficultyLabel(SiegeWorldUiLabel label, bool dodgeSubmenuOpen, bool defendSubmenuOpen, bool creditsOpen)
    {
        return ShouldShowUnmanagedDifficultyObject(label.gameObject.name, dodgeSubmenuOpen, defendSubmenuOpen, creditsOpen);
    }

    private static bool ShouldShowUnmanagedDifficultyRoot(GameObject root, bool dodgeSubmenuOpen, bool defendSubmenuOpen, bool creditsOpen)
    {
        return ShouldShowUnmanagedDifficultyObject(root.name, dodgeSubmenuOpen, defendSubmenuOpen, creditsOpen);
    }

    private static bool ShouldShowUnmanagedDifficultyObject(string objectName, bool dodgeSubmenuOpen, bool defendSubmenuOpen, bool creditsOpen)
    {
        if (IsCreditsPanelOnlyObjectName(objectName))
        {
            return creditsOpen && !dodgeSubmenuOpen && !defendSubmenuOpen;
        }

        if (IsDodgeSubmenuOnlyObjectName(objectName))
        {
            return dodgeSubmenuOpen && !creditsOpen && !defendSubmenuOpen;
        }

        if (IsDefendCannonsSubmenuOnlyObjectName(objectName))
        {
            return defendSubmenuOpen && !creditsOpen && !dodgeSubmenuOpen;
        }

        if ((dodgeSubmenuOpen || defendSubmenuOpen || creditsOpen) && IsMainMenuDifficultyObjectName(objectName))
        {
            return false;
        }

        return !creditsOpen;
    }

    private static bool IsCreditsPanelOnlyObjectName(string objectName)
    {
        return ContainsNameToken(
            objectName,
            "credits",
            "creditspanel",
            "creditsback",
            "creditsbody",
            "creditstext");
    }

    private static bool IsDodgeSubmenuOnlyObjectName(string objectName)
    {
        if (IsCreditsPanelOnlyObjectName(objectName))
        {
            return false;
        }

        return ContainsNameToken(
            objectName,
            "timed",
            "endless",
            "back",
            "submenuback",
            "dodgearrowstimed",
            "dodgearrowsendless",
            "endlesssurvival",
            "survivalmode",
            "survival");
    }

    private static bool IsDefendCannonsSubmenuOnlyObjectName(string objectName)
    {
        if (IsCreditsPanelOnlyObjectName(objectName) || IsDodgeSubmenuOnlyObjectName(objectName))
        {
            return false;
        }

        return ContainsNameToken(
            objectName,
            "demo",
            "full",
            "fullversion",
            "demoversion",
            "defendcannonsback",
            "defendthecannonsback",
            "cannonsback");
    }

    private static bool IsMainMenuDifficultyObjectName(string objectName)
    {
        if (IsDodgeSubmenuOnlyObjectName(objectName) || IsCreditsPanelOnlyObjectName(objectName))
        {
            return false;
        }

        return ContainsNameToken(
            objectName,
            "defendthecannons",
            "defendcannons",
            "dodge",
            "dodgearrows",
            "arrowdodge",
            "siegepvp",
            "pvp",
            "easy",
            "hard");
    }

    private void CacheDifficultyOptions()
    {
        difficultyOptions = FindObjectsOfType<SiegeDifficultyOption>(true);
    }

    private void ResolveDifficultyPresentationTargets()
    {
        resolvedDifficultyLabels.Clear();
        resolvedDifficultyRoots.Clear();

        AddDifficultyLabel(worldStatusLabel, allow: false);

        if (difficultyOptionLabels != null)
        {
            for (int i = 0; i < difficultyOptionLabels.Length; i++)
            {
                AddDifficultyLabel(difficultyOptionLabels[i]);
            }
        }

        if (difficultyOptionRoots != null)
        {
            for (int i = 0; i < difficultyOptionRoots.Length; i++)
            {
                AddDifficultyRoot(difficultyOptionRoots[i]);
            }
        }

        if (difficultyOptions != null)
        {
            for (int i = 0; i < difficultyOptions.Length; i++)
            {
                SiegeDifficultyOption option = difficultyOptions[i];
                if (option == null)
                {
                    continue;
                }

                SiegeWorldUiLabel optionLabel = option.GetPresentationLabel();
                if (optionLabel != null)
                {
                    AddDifficultyLabel(optionLabel);
                }
                else
                {
                    AddDifficultyRoot(option.gameObject);
                }
            }
        }

        SiegeWorldUiLabel[] sceneLabels = FindObjectsOfType<SiegeWorldUiLabel>(true);
        for (int i = 0; i < sceneLabels.Length; i++)
        {
            SiegeWorldUiLabel label = sceneLabels[i];
            if (label != null && IsDifficultyLabel(label))
            {
                AddDifficultyLabel(label);
            }
        }

        TextMeshPro[] sceneTexts = FindObjectsOfType<TextMeshPro>(true);
        for (int i = 0; i < sceneTexts.Length; i++)
        {
            TextMeshPro text = sceneTexts[i];
            if (text != null && IsDifficultyObjectName(text.gameObject.name))
            {
                AddDifficultyRoot(text.gameObject);
            }
        }
    }

    private void ResolvePlayingPresentationTargets()
    {
        resolvedPlayingLabels.Clear();
        resolvedPlayingRoots.Clear();

        AddPlayingLabel(worldCountdownLabel);
        AddPlayingLabel(worldCommanderHpLabel);
        // Victory / defeat / restart are end-game only — never auto-shown during play.

        SiegeWorldUiLabel[] sceneLabels = FindObjectsOfType<SiegeWorldUiLabel>(true);
        for (int i = 0; i < sceneLabels.Length; i++)
        {
            SiegeWorldUiLabel label = sceneLabels[i];
            if (label != null
                && IsPlayingHudLabel(label)
                && !IsDifficultyLabel(label)
                && !IsCreditsPresentationLabel(label))
            {
                AddPlayingLabel(label);
            }
        }

        TextMeshPro[] sceneTexts = FindObjectsOfType<TextMeshPro>(true);
        for (int i = 0; i < sceneTexts.Length; i++)
        {
            TextMeshPro text = sceneTexts[i];
            if (text != null
                && IsPlayingHudObjectName(text.gameObject.name)
                && !IsDifficultyObjectName(text.gameObject.name)
                && !IsCreditsPresentationObject(text.gameObject))
            {
                AddPlayingRoot(text.gameObject);
            }
        }
    }

    private void AddDifficultyLabel(SiegeWorldUiLabel label, bool allow = true)
    {
        if (!allow || label == null || resolvedDifficultyLabels.Contains(label))
        {
            return;
        }

        resolvedDifficultyLabels.Add(label);
    }

    private void AddDifficultyRoot(GameObject root)
    {
        if (root == null || resolvedDifficultyRoots.Contains(root))
        {
            return;
        }

        resolvedDifficultyRoots.Add(root);
    }

    private void AddPlayingLabel(SiegeWorldUiLabel label)
    {
        if (label == null || resolvedPlayingLabels.Contains(label))
        {
            return;
        }

        resolvedPlayingLabels.Add(label);
    }

    private void AddPlayingRoot(GameObject root)
    {
        if (root == null || resolvedPlayingRoots.Contains(root))
        {
            return;
        }

        resolvedPlayingRoots.Add(root);
    }

    private bool IsDifficultyLabel(SiegeWorldUiLabel label)
    {
        return label != null
            && (IsDifficultyObjectName(label.gameObject.name) || IsCreditsPresentationLabel(label));
    }

    private bool IsDifficultyRoot(GameObject root)
    {
        return root != null
            && (IsDifficultyObjectName(root.name) || IsCreditsPresentationObject(root));
    }

    private bool IsCreditsPresentationLabel(SiegeWorldUiLabel label)
    {
        return label != null && label == creditsBodyLabel;
    }

    private bool IsCreditsPresentationObject(GameObject go)
    {
        if (go == null)
        {
            return false;
        }

        if (creditsPanelRoot != null
            && (go == creditsPanelRoot || go.transform.IsChildOf(creditsPanelRoot.transform)))
        {
            return true;
        }

        if (creditsBodyLabel != null
            && (go == creditsBodyLabel.gameObject
                || go.transform.IsChildOf(creditsBodyLabel.transform)))
        {
            return true;
        }

        if (creditsBodyText != null && go == creditsBodyText.gameObject)
        {
            return true;
        }

        return IsCreditsPanelOnlyObjectName(go.name);
    }

    private static bool IsDifficultyObjectName(string objectName)
    {
        string normalized = NormalizeObjectName(objectName);
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        if (ContainsAny(normalized, "countdown", "commander", "arrowhit", "victory", "defeat", "restart", "opening", "status"))
        {
            return false;
        }

        if (IsCreditsPanelOnlyObjectName(objectName))
        {
            return true;
        }

        return ContainsAny(
            normalized,
            "demo",
            "full",
            "fullversion",
            "demoversion",
            "dodge",
            "dodgearrows",
            "arrowdodge",
            "timed",
            "endless",
            "back",
            "siegepvp",
            "pvp",
            "survive",
            "2wave",
            "3wave",
            "2waves",
            "3waves",
            "twowave",
            "threewave",
            "wave2",
            "wave3",
            "difficulty2",
            "difficulty3",
            "difficultyoption",
            "easy",
            "hard");
    }

    private static bool IsPlayingHudLabel(SiegeWorldUiLabel label)
    {
        return label != null && IsPlayingHudObjectName(label.gameObject.name);
    }

    private static bool IsEndGameHudLabel(SiegeWorldUiLabel label)
    {
        if (label == null)
        {
            return false;
        }

        return IsEndGameHudObjectName(label.gameObject.name);
    }

    private static bool IsEndGameHudObjectName(string objectName)
    {
        string normalized = NormalizeObjectName(objectName);
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        return ContainsAny(normalized, "victory", "defeat", "restart", "win", "lose");
    }

    private static bool IsPlayingHudObjectName(string objectName)
    {
        string normalized = NormalizeObjectName(objectName);
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        if (IsEndGameHudObjectName(objectName))
        {
            return false;
        }

        return ContainsAny(
            normalized,
            "countdown",
            "cannon",
            "commander",
            "arrowhit",
            "hp",
            "opening",
            "status");
    }

    private static string NormalizeObjectName(string objectName)
    {
        return (objectName ?? string.Empty).Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
    }

    private static bool ContainsAny(string normalized, params string[] tokens)
    {
        for (int i = 0; i < tokens.Length; i++)
        {
            if (normalized.Contains(tokens[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static void SetLabelVisible(SiegeWorldUiLabel label, bool visible)
    {
        if (label != null)
        {
            label.SetVisible(visible);
        }
    }

    private static void SetRootVisible(GameObject root, bool visible)
    {
        if (root == null)
        {
            return;
        }

        if (root.activeSelf != visible)
        {
            root.SetActive(visible);
        }

        TextMeshPro text = root.GetComponent<TextMeshPro>();
        if (text != null)
        {
            text.enabled = visible;
        }
    }

    private string GetActiveOpeningStatus()
    {
        if (SiegeMatchSettings.IsDodgeArrowsEndlessMode)
        {
            return dodgeArrowsEndlessOpeningStatus;
        }

        if (SiegeMatchSettings.IsDodgeArrowsTimedMode)
        {
            return dodgeArrowsOpeningStatus;
        }

        if (SiegeMatchSettings.IsSiegePvpMode)
        {
            SiegePvpSession pvp = SiegePvpSession.Instance;
            if (pvp != null && pvp.IsDefender)
            {
                return siegePvpDefenderOpeningStatus;
            }

            return siegePvpAttackerOpeningStatus;
        }

        return openingStatus;
    }

    private string BuildDifficultySelectPrompt()
    {
        if (!ShouldShowCaveClientEnvironmentNote()
            || string.IsNullOrWhiteSpace(caveClientEnvironmentNote))
        {
            return difficultySelectPrompt;
        }

        return difficultySelectPrompt + "\n\n" + caveClientEnvironmentNote.Trim();
    }

    private static bool ShouldShowCaveClientEnvironmentNote()
    {
        if (!SiegePlayEnvironment.IsCaveMode)
        {
            return false;
        }

        SiegePvpSession pvp = SiegePvpSession.Instance;
        return pvp != null && pvp.IsDefender;
    }

    private void ApplyPlayingLayout()
    {
        SetGroupActive(difficultySelectGroup, false);
        SetGroupActive(playingGroup, true);
        SetGroupActive(endGameGroup, false);
        SetDifficultyPresentationVisible(false);
        ShowPlayingHudTargets();

        if (!showOpeningStatusOnStart)
        {
            SetWorldLabelText(ActiveStatusLabel, string.Empty, false);
        }

        SetWorldLabelText(worldVictoryLabel, string.Empty, false);
        SetWorldLabelText(worldDefeatLabel, string.Empty, false);
        SetWorldLabelText(defenderVictoryLabel, string.Empty, false);
        SetWorldLabelText(defenderDefeatLabel, string.Empty, false);
        SetWorldLabelText(ActiveRestartLabel, string.Empty, false);
        SetLabelVisible(worldVictoryLabel, false);
        SetLabelVisible(worldDefeatLabel, false);
        SetLabelVisible(defenderVictoryLabel, false);
        SetLabelVisible(defenderDefeatLabel, false);
        RefreshCommanderHpDisplay();
    }

    private void ApplyVictoryLayout()
    {
        // GM Won = attacker victory / defender defeat.
        SetGroupActive(difficultySelectGroup, false);
        SetGroupActive(playingGroup, false);
        SetGroupActive(endGameGroup, true);
        SetDifficultyPresentationVisible(false);
        HidePlayingHudTargets();
        ApplyRoleHudVisibility();

        SetWorldLabelText(ActiveStatusLabel, string.Empty, false);
        SetWorldLabelText(ActiveCommanderHpLabel, string.Empty, false);
        SetWorldLabelText(ActiveRestartLabel, string.Empty, false);

        if (UseDefenderHud)
        {
            SetWorldLabelText(ActiveVictoryLabel, string.Empty, false);
            SetLabelVisible(ActiveVictoryLabel, false);
            SetWorldLabelText(ActiveDefeatLabel, defenderDefeatWhenAttackerWinsMessage, false);
            SetLabelVisible(ActiveDefeatLabel, true);
        }
        else
        {
            SetWorldLabelText(ActiveDefeatLabel, string.Empty, false);
            SetLabelVisible(ActiveDefeatLabel, false);
            SetWorldLabelText(ActiveVictoryLabel, victoryMessage, false);
            SetLabelVisible(ActiveVictoryLabel, true);
        }
    }

    private void ApplyDefeatLayout(string reason)
    {
        // GM Lost = attacker defeat / defender victory.
        SetGroupActive(difficultySelectGroup, false);
        SetGroupActive(playingGroup, false);
        SetGroupActive(endGameGroup, true);
        SetDifficultyPresentationVisible(false);
        HidePlayingHudTargets();
        ApplyRoleHudVisibility();

        SetWorldLabelText(ActiveStatusLabel, string.Empty, false);
        SetWorldLabelText(ActiveCommanderHpLabel, string.Empty, false);
        SetWorldLabelText(ActiveRestartLabel, string.Empty, false);

        if (UseDefenderHud)
        {
            SetWorldLabelText(ActiveDefeatLabel, string.Empty, false);
            SetLabelVisible(ActiveDefeatLabel, false);
            SetWorldLabelText(ActiveVictoryLabel, ResolveDefenderVictoryMessage(reason), false);
            SetLabelVisible(ActiveVictoryLabel, true);
        }
        else
        {
            SetWorldLabelText(ActiveVictoryLabel, string.Empty, false);
            SetLabelVisible(ActiveVictoryLabel, false);
            SetWorldLabelText(ActiveDefeatLabel, ResolveDefeatMessage(reason), false);
            SetLabelVisible(ActiveDefeatLabel, true);
        }
    }

    public void RefreshEditorPreview()
    {
        ApplyEditorPreview();
    }

    private void ApplyEditorPreview()
    {
        if (!previewInEditor || Application.isPlaying)
        {
            return;
        }

        switch (editorPreview)
        {
            case EditorPreviewMode.SelectingDifficulty:
                HideCreditsPanelContent();
                ApplyDifficultySelectLayout();
                break;
            case EditorPreviewMode.DefendCannonsSubmenu:
                ApplyDefendCannonsSelectLayout();
                break;
            case EditorPreviewMode.DodgeArrowsSubmenu:
                ApplyDodgeArrowsSelectLayout();
                break;
            case EditorPreviewMode.Credits:
                ApplyCreditsSelectLayout();
                break;
            case EditorPreviewMode.Victory:
                HideCreditsPanelContent();
                ApplyVictoryLayout();
                break;
            case EditorPreviewMode.Defeat:
                HideCreditsPanelContent();
                ApplyDefeatLayout(cannonDefeatMessage);
                break;
            default:
                HideCreditsPanelContent();
                ApplyPlayingLayout();
                SetWorldLabelText(ActiveStatusLabel, openingStatus, false);
                SetWorldLabelText(ActiveCommanderHpLabel, string.Format(commanderHpFormat, 3), true);
                SetWorldLabelText(ActiveCountdownLabel, string.Format(wave3CountdownFormat, 42f), true);
                break;
        }
    }

    /// <summary>Editor/runtime helper: mode-select chrome with credits body + Back visible.</summary>
    private void ApplyCreditsSelectLayout()
    {
        ResolvePresentationTargets();
        CacheDifficultyOptions();
        ResolveDifficultyPresentationTargets();
        pvpHudForDefender = false;
        ApplyRoleHudVisibility();

        SetGroupActive(difficultySelectGroup, true);
        SetGroupActive(playingGroup, false);
        SetGroupActive(endGameGroup, false);

        HidePlayingHudTargets();
        ShowCreditsPanelPrompt();
        ApplyForcedDifficultyPresentation(
            dodgeSubmenuOpen: false,
            defendSubmenuOpen: false,
            creditsOpen: true,
            defendSubmenuConfigured: SiegeDefendCannonsSubmenu.IsDefendCannonsSubmenuEnabled());

        modeSelectLayoutApplied = true;
        SetWorldLabelText(worldVictoryLabel, string.Empty, false);
        SetWorldLabelText(worldDefeatLabel, string.Empty, false);
        SetWorldLabelText(defenderVictoryLabel, string.Empty, false);
        SetWorldLabelText(defenderDefeatLabel, string.Empty, false);
        SetWorldLabelText(ActiveRestartLabel, string.Empty, false);
        SetLabelVisible(worldVictoryLabel, false);
        SetLabelVisible(worldDefeatLabel, false);
        SetLabelVisible(defenderVictoryLabel, false);
        SetLabelVisible(defenderDefeatLabel, false);
    }

    /// <summary>Editor/runtime helper: mode-select chrome with Demo / Full / Back visible.</summary>
    private void ApplyDefendCannonsSelectLayout()
    {
        ResolvePresentationTargets();
        CacheDifficultyOptions();
        ResolveDifficultyPresentationTargets();
        pvpHudForDefender = false;
        ApplyRoleHudVisibility();

        SetGroupActive(difficultySelectGroup, true);
        SetGroupActive(playingGroup, false);
        SetGroupActive(endGameGroup, false);

        HidePlayingHudTargets();
        EnsureModeSelectStatusVisible(defendCannonsSubmenuPrompt);
        ApplyForcedDifficultyPresentation(
            dodgeSubmenuOpen: false,
            defendSubmenuOpen: true,
            creditsOpen: false,
            defendSubmenuConfigured: true);

        modeSelectLayoutApplied = true;
        SetWorldLabelText(worldVictoryLabel, string.Empty, false);
        SetWorldLabelText(worldDefeatLabel, string.Empty, false);
        SetWorldLabelText(defenderVictoryLabel, string.Empty, false);
        SetWorldLabelText(defenderDefeatLabel, string.Empty, false);
        SetWorldLabelText(ActiveRestartLabel, string.Empty, false);
        SetLabelVisible(worldVictoryLabel, false);
        SetLabelVisible(worldDefeatLabel, false);
        SetLabelVisible(defenderVictoryLabel, false);
        SetLabelVisible(defenderDefeatLabel, false);
    }

    /// <summary>Editor/runtime helper: mode-select chrome with Timed / Endless / Back visible.</summary>
    private void ApplyDodgeArrowsSelectLayout()
    {
        ResolvePresentationTargets();
        CacheDifficultyOptions();
        ResolveDifficultyPresentationTargets();
        pvpHudForDefender = false;
        ApplyRoleHudVisibility();

        SetGroupActive(difficultySelectGroup, true);
        SetGroupActive(playingGroup, false);
        SetGroupActive(endGameGroup, false);

        HidePlayingHudTargets();
        EnsureModeSelectStatusVisible(dodgeArrowsSubmenuPrompt);
        ApplyForcedDifficultyPresentation(
            dodgeSubmenuOpen: true,
            defendSubmenuOpen: false,
            creditsOpen: false,
            defendSubmenuConfigured: SiegeDefendCannonsSubmenu.IsDefendCannonsSubmenuEnabled());

        modeSelectLayoutApplied = true;
        SetWorldLabelText(worldVictoryLabel, string.Empty, false);
        SetWorldLabelText(worldDefeatLabel, string.Empty, false);
        SetWorldLabelText(defenderVictoryLabel, string.Empty, false);
        SetWorldLabelText(defenderDefeatLabel, string.Empty, false);
        SetWorldLabelText(ActiveRestartLabel, string.Empty, false);
        SetLabelVisible(worldVictoryLabel, false);
        SetLabelVisible(worldDefeatLabel, false);
        SetLabelVisible(defenderVictoryLabel, false);
        SetLabelVisible(defenderDefeatLabel, false);
    }

    private void ApplyForcedDifficultyPresentation(
        bool dodgeSubmenuOpen,
        bool defendSubmenuOpen,
        bool creditsOpen,
        bool defendSubmenuConfigured)
    {
        EnforceDifficultyOptionVisibility(dodgeSubmenuOpen, defendSubmenuOpen, creditsOpen, defendSubmenuConfigured);

        for (int i = 0; i < resolvedDifficultyLabels.Count; i++)
        {
            SiegeWorldUiLabel label = resolvedDifficultyLabels[i];
            if (label == null || IsOwnedByDifficultyOption(label))
            {
                continue;
            }

            label.SetVisible(ShouldShowUnmanagedDifficultyLabel(label, dodgeSubmenuOpen, defendSubmenuOpen, creditsOpen));
        }

        for (int i = 0; i < resolvedDifficultyRoots.Count; i++)
        {
            GameObject root = resolvedDifficultyRoots[i];
            if (root == null || IsOwnedByDifficultyOption(root))
            {
                continue;
            }

            SetRootVisible(root, ShouldShowUnmanagedDifficultyRoot(root, dodgeSubmenuOpen, defendSubmenuOpen, creditsOpen));
        }
    }

    private string ResolveDefeatMessage(string reason)
    {
        if (SiegeMatchSettings.IsDodgeArrowsEndlessMode && SiegeGameManager.Instance != null)
        {
            float survivedSeconds = SiegeGameManager.Instance.MatchElapsedSeconds;
            string survivedLine = string.Format(dodgeArrowsEndlessDefeatFormat, survivedSeconds);
            if (!SiegeEndlessSurvivalRecord.TryGetBestSeconds(out float bestSeconds))
            {
                return survivedLine;
            }

            string bestFormat = SiegeEndlessSurvivalRecord.LastRunWasNewBest
                ? dodgeArrowsEndlessNewBestFormat
                : dodgeArrowsEndlessBestFormat;
            return survivedLine + "\n" + string.Format(bestFormat, bestSeconds);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return "Defeat.";
        }

        if (reason.IndexOf("fell", System.StringComparison.OrdinalIgnoreCase) >= 0
            || reason.IndexOf("tower", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return fallDefeatMessage;
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

    private string ResolveDefenderVictoryMessage(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return defenderVictoryWhenCannonsSilencedMessage;
        }

        if (reason.IndexOf("fell", System.StringComparison.OrdinalIgnoreCase) >= 0
            || reason.IndexOf("tower", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return defenderVictoryWhenCommanderFellMessage;
        }

        if (reason.IndexOf("arrow", System.StringComparison.OrdinalIgnoreCase) >= 0
            || reason.IndexOf("struck", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return defenderVictoryWhenCommanderArrowedMessage;
        }

        return defenderVictoryWhenCannonsSilencedMessage;
    }

    private static void SetWorldLabelText(SiegeWorldUiLabel label, string message, bool keepVisible)
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

    private string GetActiveRestartPrompt()
    {
        return SiegePlayEnvironment.GetRestartPrompt(restartPrompt, trackedRestartPrompt);
    }
}
