using UnityEngine;

/// <summary>
/// Shows Timed / Endless / Back choices after the main "Dodge Arrows" button is clicked.
/// Assign options in the inspector, or leave empty to auto-bind by <see cref="SiegeGameMode"/>.
/// </summary>
[DefaultExecutionOrder(50)]
[DisallowMultipleComponent]
public class SiegeDodgeArrowsSubmenu : MonoBehaviour
{
    public static SiegeDodgeArrowsSubmenu Instance { get; private set; }

    [SerializeField] private SiegeDifficultyOption dodgeArrowsMenuButton;
    [SerializeField] private SiegeDifficultyOption timedOption;
    [SerializeField] private SiegeDifficultyOption endlessOption;
    [SerializeField] private SiegeDifficultyOption backOption;
    [Tooltip("Hidden while the Dodge Arrows submenu is open (Demo, Full, PVP, etc.). Auto-filled if empty.")]
    [SerializeField] private SiegeDifficultyOption[] mainMenuOptions;
    [Tooltip("After opening/closing the submenu, ignore selection until the pointer is released and this lockout elapses.")]
    [SerializeField, Min(0f)] private float submenuTransitionInputCooldownSeconds = 0.3f;

    private bool submenuOpen;
    private float selectionUnlockTime;
    private bool pendingSelectionAfterRelease;

    public bool IsSubmenuOpen => submenuOpen;
    public bool BlocksSelection =>
        pendingSelectionAfterRelease
        || Time.unscaledTime < selectionUnlockTime;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple SiegeDodgeArrowsSubmenu instances found.", this);
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

    /// <summary>Apply the correct main-menu or submenu visibility for every bound option.</summary>
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

        ApplySubmenuLayout();

        if (SiegeMatchUi.Instance != null)
        {
            SiegeMatchUi.Instance.ShowDodgeArrowsSubmenuPrompt();
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
                // Owned by SiegeCreditsPanel — hide while Dodge submenu is open.
                show = false;
            }
            else
            {
                show = option.IsDodgeSubmenuChoice ? submenuOpen : !submenuOpen;
            }

            SetOptionFullyVisible(option, show);
        }
    }

    private SiegeDifficultyOption[] GetMainMenuOptions()
    {
        return mainMenuOptions != null && mainMenuOptions.Length > 0
            ? mainMenuOptions
            : System.Array.Empty<SiegeDifficultyOption>();
    }

    private void AutoBindOptionsIfNeeded()
    {
        SiegeDifficultyOption[] all = FindObjectsOfType<SiegeDifficultyOption>(true);
        if (all == null || all.Length == 0)
        {
            return;
        }

        if (timedOption == null)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].IsDodgeSubmenuChoice && all[i].GameMode == SiegeGameMode.DodgeArrows)
                {
                    timedOption = all[i];
                    break;
                }
            }
        }

        if (endlessOption == null)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].GameMode == SiegeGameMode.DodgeArrowsEndless)
                {
                    endlessOption = all[i];
                    break;
                }
            }
        }

        if (backOption == null)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].IsSubmenuBackButton)
                {
                    backOption = all[i];
                    break;
                }
            }
        }

        if (dodgeArrowsMenuButton == null)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].OpensDodgeArrowsSubmenu)
                {
                    dodgeArrowsMenuButton = all[i];
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
                    || option == timedOption
                    || option == endlessOption
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
