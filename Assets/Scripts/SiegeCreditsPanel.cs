using UnityEngine;

/// <summary>
/// Main-menu Credits flow: hide mode buttons, show world-space credits body + Back.
/// Assign the 3D credits TextMeshPro / SiegeWorldUiLabel on <see cref="SiegeMatchUi"/>
/// (same pattern as other match UI signs). Edit the credits string there too.
/// </summary>
[DefaultExecutionOrder(51)]
[DisallowMultipleComponent]
public class SiegeCreditsPanel : MonoBehaviour
{
    public static SiegeCreditsPanel Instance { get; private set; }

    [Header("Buttons (world-space SiegeDifficultyOption)")]
    [SerializeField] private SiegeDifficultyOption creditsMenuButton;
    [SerializeField] private SiegeDifficultyOption backButton;

    [Header("Input")]
    [SerializeField, Min(0f)] private float panelTransitionInputCooldownSeconds = 0.3f;

    private bool panelOpen;
    private float selectionUnlockTime;
    private bool pendingSelectionAfterRelease;

    public bool IsOpen => panelOpen;
    public bool BlocksSelection =>
        pendingSelectionAfterRelease
        || Time.unscaledTime < selectionUnlockTime;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple SiegeCreditsPanel instances found.", this);
        }

        Instance = this;
        panelOpen = false;
        AutoBindButtonsIfNeeded();
        ApplyLayout();
    }

    private void Start()
    {
        ApplyLayout();
    }

    private void Update()
    {
        TryClearPendingSelectionLock();
    }

    private void OnValidate()
    {
        AutoBindButtonsIfNeeded();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void ApplyLayout()
    {
        AutoBindButtonsIfNeeded();

        SiegeMatchUi matchUi = SiegeMatchUi.Instance;
        if (matchUi != null)
        {
            if (panelOpen)
            {
                matchUi.ApplyCreditsBodyText();
                matchUi.SetCreditsPanelVisible(true);
            }
            else
            {
                matchUi.HideCreditsPanelContent();
            }
        }

        if (backButton != null)
        {
            backButton.SetOptionFullyVisible(panelOpen);
        }

        if (creditsMenuButton != null)
        {
            creditsMenuButton.SetOptionFullyVisible(!panelOpen);
        }
    }

    public void OpenPanel()
    {
        AutoBindButtonsIfNeeded();
        BeginTransitionInputLock();
        panelOpen = true;

        SiegeDodgeArrowsSubmenu dodgeSubmenu = SiegeDodgeArrowsSubmenu.Instance;
        if (dodgeSubmenu != null && dodgeSubmenu.IsSubmenuOpen)
        {
            dodgeSubmenu.CloseSubmenu();
        }

        ApplyLayout();

        if (SiegeMatchUi.Instance != null)
        {
            SiegeMatchUi.Instance.ShowCreditsPanelPrompt();
            SiegeMatchUi.Instance.RefreshDifficultyOptionPresentation();
        }
    }

    public void ClosePanel()
    {
        if (!panelOpen)
        {
            ApplyLayout();
            return;
        }

        BeginTransitionInputLock();
        panelOpen = false;
        ApplyLayout();

        if (SiegeMatchUi.Instance != null)
        {
            SiegeMatchUi.Instance.RestoreModeSelectStatus();
            SiegeMatchUi.Instance.RefreshDifficultyOptionPresentation();
        }
    }

    private void AutoBindButtonsIfNeeded()
    {
        SiegeDifficultyOption[] all = FindObjectsOfType<SiegeDifficultyOption>(true);
        if (all == null || all.Length == 0)
        {
            return;
        }

        if (creditsMenuButton == null)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].OpensCreditsPanel)
                {
                    creditsMenuButton = all[i];
                    break;
                }
            }
        }

        if (backButton == null)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].IsCreditsBackButton)
                {
                    backButton = all[i];
                    break;
                }
            }
        }
    }

    private void BeginTransitionInputLock()
    {
        selectionUnlockTime = Time.unscaledTime + panelTransitionInputCooldownSeconds;
        pendingSelectionAfterRelease = true;
        SiegeSceneBootstrap.BeginInputCooldown(panelTransitionInputCooldownSeconds);
    }

    private void TryClearPendingSelectionLock()
    {
        if (!pendingSelectionAfterRelease)
        {
            return;
        }

        bool released = !SiegeVrInput.IsPointerHeld();
        bool forceAfterTimeout = Time.unscaledTime >= selectionUnlockTime + 0.75f;
        if (!released && !forceAfterTimeout)
        {
            return;
        }

        if (Time.unscaledTime < selectionUnlockTime && !forceAfterTimeout)
        {
            return;
        }

        pendingSelectionAfterRelease = false;
    }
}
