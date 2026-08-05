using UnityEngine;

/// <summary>
/// Shows Demo / Full / Back after the main "Defend the Cannons" button is clicked.
/// Assign options in the inspector, or leave empty to auto-bind by <see cref="SiegeGameMode"/>.
/// </summary>
[DefaultExecutionOrder(49)]
[DisallowMultipleComponent]
public class SiegeDefendCannonsSubmenu : MonoBehaviour
{
    public static SiegeDefendCannonsSubmenu Instance { get; private set; }

    [SerializeField] private SiegeDifficultyOption defendCannonsMenuButton;
    [SerializeField] private SiegeDifficultyOption demoOption;
    [SerializeField] private SiegeDifficultyOption fullOption;
    [SerializeField] private SiegeDifficultyOption backOption;
    [Tooltip("Hidden while the Defend the Cannons submenu is open (Dodge Arrows, PVP, etc.). Auto-filled if empty.")]
    [SerializeField] private SiegeDifficultyOption[] mainMenuOptions;
    [Tooltip("After opening/closing the submenu, ignore selection until the pointer is released and this lockout elapses.")]
    [SerializeField, Min(0f)] private float submenuTransitionInputCooldownSeconds = 0.3f;

    private bool submenuOpen;
    private float selectionUnlockTime;
    private bool pendingSelectionAfterRelease;

    public bool IsSubmenuOpen => submenuOpen;
    public bool HasConfiguredMenuButton => defendCannonsMenuButton != null;

    /// <summary>True when a main-menu opener exists so Demo/Full move into the submenu.</summary>
    public static bool IsDefendCannonsSubmenuEnabled()
    {
        SiegeDifficultyOption[] all = FindObjectsOfType<SiegeDifficultyOption>(true);
        if (all == null)
        {
            return false;
        }

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].OpensDefendCannonsSubmenu)
            {
                return true;
            }
        }

        return false;
    }

    public bool BlocksSelection =>
        pendingSelectionAfterRelease
        || Time.unscaledTime < selectionUnlockTime;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple SiegeDefendCannonsSubmenu instances found.", this);
        }

        Instance = this;
        submenuOpen = false;
        AutoBindOptionsIfNeeded();
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

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void ApplyLayout()
    {
        AutoBindOptionsIfNeeded();
        if (submenuOpen)
        {
            ApplySubmenuLayout();
        }
        else
        {
            ApplyMainMenuLayout();
        }
    }

    public void OpenSubmenu()
    {
        AutoBindOptionsIfNeeded();
        BeginTransitionInputLock();
        submenuOpen = true;

        SiegeCreditsPanel credits = SiegeCreditsPanel.Instance;
        if (credits != null && credits.IsOpen)
        {
            credits.ClosePanel();
        }

        SiegeDodgeArrowsSubmenu dodgeSubmenu = SiegeDodgeArrowsSubmenu.Instance;
        if (dodgeSubmenu != null && dodgeSubmenu.IsSubmenuOpen)
        {
            dodgeSubmenu.CloseSubmenu();
        }

        ApplySubmenuLayout();

        if (SiegeMatchUi.Instance != null)
        {
            SiegeMatchUi.Instance.ShowDefendCannonsSubmenuPrompt();
            SiegeMatchUi.Instance.RefreshDifficultyOptionPresentation();
        }
    }

    public void CloseSubmenu()
    {
        BeginTransitionInputLock();
        submenuOpen = false;
        ApplyMainMenuLayout();

        if (SiegeMatchUi.Instance != null)
        {
            SiegeMatchUi.Instance.RestoreModeSelectStatus();
            SiegeMatchUi.Instance.RefreshDifficultyOptionPresentation();
        }
    }

    private void ApplyMainMenuLayout()
    {
        ApplyLayoutToAllDifficultyOptions(submenuOpen: false);
    }

    private void ApplySubmenuLayout()
    {
        ApplyLayoutToAllDifficultyOptions(submenuOpen: true);
    }

    private void ApplyLayoutToAllDifficultyOptions(bool submenuOpen)
    {
        SiegeDifficultyOption[] all = FindObjectsOfType<SiegeDifficultyOption>(true);
        if (all == null || all.Length == 0)
        {
            return;
        }

        bool submenuEnabled = IsDefendCannonsSubmenuEnabled();

        for (int i = 0; i < all.Length; i++)
        {
            SiegeDifficultyOption option = all[i];
            if (option == null)
            {
                continue;
            }

            bool show;
            if (option.OpensCreditsPanel || option.IsCreditsBackButton)
            {
                show = false;
            }
            else if (option.IsDefendCannonsSubmenuChoice && submenuEnabled)
            {
                show = submenuOpen;
            }
            else
            {
                show = !submenuOpen;
            }

            SetOptionFullyVisible(option, show);
        }
    }

    private void AutoBindOptionsIfNeeded()
    {
        SiegeDifficultyOption[] all = FindObjectsOfType<SiegeDifficultyOption>(true);
        if (all == null || all.Length == 0)
        {
            return;
        }

        if (demoOption == null)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null
                    && all[i].GameMode == SiegeGameMode.Demo
                    && !all[i].OpensDefendCannonsSubmenu)
                {
                    demoOption = all[i];
                    break;
                }
            }
        }

        if (fullOption == null)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null
                    && all[i].GameMode == SiegeGameMode.Full
                    && !all[i].OpensDefendCannonsSubmenu)
                {
                    fullOption = all[i];
                    break;
                }
            }
        }

        if (backOption == null)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].IsDefendCannonsSubmenuBackButton)
                {
                    backOption = all[i];
                    break;
                }
            }
        }

        if (defendCannonsMenuButton == null)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].OpensDefendCannonsSubmenu)
                {
                    defendCannonsMenuButton = all[i];
                    break;
                }
            }
        }

        if (mainMenuOptions == null || mainMenuOptions.Length == 0)
        {
            System.Collections.Generic.List<SiegeDifficultyOption> main =
                new System.Collections.Generic.List<SiegeDifficultyOption>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                SiegeDifficultyOption option = all[i];
                if (option == null
                    || option == demoOption
                    || option == fullOption
                    || option == backOption)
                {
                    continue;
                }

                main.Add(option);
            }

            mainMenuOptions = main.ToArray();
        }
    }

    private static void SetOptionFullyVisible(SiegeDifficultyOption option, bool visible)
    {
        if (option != null)
        {
            option.SetOptionFullyVisible(visible);
        }
    }

    private void BeginTransitionInputLock()
    {
        selectionUnlockTime = Time.unscaledTime + submenuTransitionInputCooldownSeconds;
        pendingSelectionAfterRelease = true;
        SiegeSceneBootstrap.BeginInputCooldown(submenuTransitionInputCooldownSeconds);
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
