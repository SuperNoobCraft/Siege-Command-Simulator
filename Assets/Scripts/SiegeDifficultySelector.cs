using UnityEngine;
using Votanic.vXR.vGear;

/// <summary>
/// Lets players pick Demo, Full, Dodge Arrows, or Siege PVP with the wand (or mouse on desktop).
/// Siege PVP is handled by <see cref="SiegePvpSession"/> (select → ready → countdown).
/// </summary>
[DefaultExecutionOrder(10)]
public class SiegeDifficultySelector : MonoBehaviour
{
    [SerializeField] private VotanicWandRtsCommander wandCommander;
    [SerializeField] private LayerMask optionLayers = ~0;
    [SerializeField] private float maxRayDistance = 120f;
    [SerializeField] private string selectCommandName = "Grab";

    [Header("Keyboard fallback (CAVE / no controller)")]
    [SerializeField] private bool enableKeyboardNavigation = true;
    [SerializeField] private KeyCode navUpKey = KeyCode.UpArrow;
    [SerializeField] private KeyCode navDownKey = KeyCode.DownArrow;
    [SerializeField] private KeyCode navLeftKey = KeyCode.LeftArrow;
    [SerializeField] private KeyCode navRightKey = KeyCode.RightArrow;
    [SerializeField] private bool preferKeyboardSelectionWhenSet = true;

    private SiegeDifficultyOption hoveredOption;
    private SiegeDifficultyOption lastHighlightedOption;
    private SiegeDifficultyOption keyboardSelectedOption;
    private bool keyboardStartResolved;
    private bool pendingKeyboardReSelect;

    private void Awake()
    {
        if (wandCommander == null)
        {
            wandCommander = FindObjectOfType<VotanicWandRtsCommander>();
        }
    }

    private void Update()
    {
        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager == null || manager.CurrentState != SiegeGameManager.MatchState.SelectingDifficulty)
        {
            ClearHighlight();
            hoveredOption = null;
            keyboardSelectedOption = null;
            keyboardStartResolved = false;
            pendingKeyboardReSelect = false;
            return;
        }

        SiegePvpSession pvp = SiegePvpSession.Instance;
        if (pvp != null && pvp.BlocksModeSelect)
        {
            ClearHighlight();
            hoveredOption = null;
            keyboardSelectedOption = null;
            keyboardStartResolved = false;
            pendingKeyboardReSelect = false;
            return;
        }

        SiegeDodgeArrowsSubmenu dodgeSubmenu = SiegeDodgeArrowsSubmenu.Instance;
        if (dodgeSubmenu != null && dodgeSubmenu.BlocksSelection)
        {
            ClearHighlight();
            hoveredOption = null;
            keyboardSelectedOption = null;
            keyboardStartResolved = false;
            pendingKeyboardReSelect = false;
            return;
        }

        SiegeCreditsPanel creditsPanel = SiegeCreditsPanel.Instance;
        if (creditsPanel != null && creditsPanel.BlocksSelection)
        {
            ClearHighlight();
            hoveredOption = null;
            keyboardSelectedOption = null;
            keyboardStartResolved = false;
            pendingKeyboardReSelect = false;
            return;
        }

        if (!SiegeVrInput.IsGameplayInputAllowed())
        {
            ClearHighlight();
            hoveredOption = null;
            keyboardSelectedOption = null;
            keyboardStartResolved = false;
            pendingKeyboardReSelect = false;
            return;
        }

        bool keyboardConfirmPressedThisFrame = IsKeyboardConfirmPressedThisFrame();
        bool pointerPressedThisFrame = SiegeVrInput.WasPointerPressedThisFrame();

        // If the click came from wand/mouse/controller (not keyboard), override any keyboard hover.
        if (enableKeyboardNavigation && pointerPressedThisFrame && !keyboardConfirmPressedThisFrame)
        {
            keyboardSelectedOption = null;
            keyboardStartResolved = false;
            pendingKeyboardReSelect = false;
        }

        if (pendingKeyboardReSelect)
        {
            keyboardSelectedOption = null;
            keyboardStartResolved = false;
            pendingKeyboardReSelect = false;
        }

        // If keyboard navigation is enabled and we already have a selected element, keep
        // highlighting it even if the pointer ray isn't hitting anything.
        bool usingKeyboardSelection = enableKeyboardNavigation
            && preferKeyboardSelectionWhenSet
            && keyboardSelectedOption != null;

        // Process arrow navigation first (only on key-down).
        if (enableKeyboardNavigation)
        {
            bool moved = TryHandleKeyboardNavigation();
            if (moved)
            {
                usingKeyboardSelection = true;
            }
        }

        if (!usingKeyboardSelection)
        {
            keyboardSelectedOption = null;
            keyboardStartResolved = false;

            Ray ray = BuildSelectionRay();
            UpdateHoveredOption(ray);
        }
        else
        {
            hoveredOption = keyboardSelectedOption;
            if (hoveredOption != null && hoveredOption != lastHighlightedOption)
            {
                lastHighlightedOption?.SetHighlighted(false);
                hoveredOption.SetHighlighted(true);
                lastHighlightedOption = hoveredOption;
            }
        }

        if (!pointerPressedThisFrame || hoveredOption == null)
        {
            return;
        }

        if (hoveredOption.OpensCreditsPanel)
        {
            SiegeCreditsPanel credits = SiegeCreditsPanel.Instance;
            if (credits != null)
            {
                credits.OpenPanel();
            }
            else
            {
                Debug.LogWarning("Credits selected but SiegeCreditsPanel is missing from the scene.");
            }

            pendingKeyboardReSelect = true;
            return;
        }

        if (hoveredOption.IsCreditsBackButton)
        {
            SiegeCreditsPanel credits = SiegeCreditsPanel.Instance;
            if (credits != null)
            {
                credits.ClosePanel();
            }

            pendingKeyboardReSelect = true;
            return;
        }

        if (hoveredOption.OpensDodgeArrowsSubmenu)
        {
            SiegeDodgeArrowsSubmenu submenu = SiegeDodgeArrowsSubmenu.Instance;
            if (submenu != null)
            {
                submenu.OpenSubmenu();
            }
            else
            {
                Debug.LogWarning("Dodge Arrows submenu selected but SiegeDodgeArrowsSubmenu is missing from the scene.");
            }

            pendingKeyboardReSelect = true;
            return;
        }

        if (hoveredOption.IsSubmenuBackButton)
        {
            SiegeDodgeArrowsSubmenu submenu = SiegeDodgeArrowsSubmenu.Instance;
            if (submenu != null)
            {
                submenu.CloseSubmenu();
            }

            pendingKeyboardReSelect = true;
            return;
        }

        if (hoveredOption.GameMode == SiegeGameMode.SiegePvp)
        {
            if (pvp != null)
            {
                if (hoveredOption.UseCustomPvpTuning)
                {
                    pvp.NotifyLocalSelectedPvp(
                        hoveredOption.PvpMatchDurationSeconds,
                        hoveredOption.PvpMoveSpeedScale);
                }
                else
                {
                    pvp.NotifyLocalSelectedPvp();
                }
            }
            else
            {
                Debug.LogWarning("Siege PVP selected but SiegePvpSession is missing from the scene.");
            }

            return;
        }

        manager.ConfirmPlayMode(hoveredOption.GameMode);
    }

    private Ray BuildSelectionRay()
    {
        if (SiegePlayEnvironment.IsDesktopInput)
        {
            UnityEngine.Camera viewCamera = SiegePlayEnvironment.ResolveViewCamera();
            if (viewCamera != null)
            {
                return viewCamera.ScreenPointToRay(Input.mousePosition);
            }
        }

        if (wandCommander != null)
        {
            return wandCommander.BuildGameplayRay();
        }

        Transform controller = vGear.controller != null ? vGear.controller.transform : transform;
        return new Ray(controller.position, controller.forward);
    }

    private void UpdateHoveredOption(Ray ray)
    {
        hoveredOption = null;

        RaycastHit[] hits = Physics.RaycastAll(ray, maxRayDistance, optionLayers, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0)
        {
            ClearHighlight();
            return;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            SiegeDifficultyOption option = hits[i].collider.GetComponentInParent<SiegeDifficultyOption>();
            if (option != null)
            {
                hoveredOption = option;
                break;
            }
        }

        if (lastHighlightedOption != null && lastHighlightedOption != hoveredOption)
        {
            lastHighlightedOption.SetHighlighted(false);
        }

        if (hoveredOption != null)
        {
            hoveredOption.SetHighlighted(true);
            lastHighlightedOption = hoveredOption;
        }
    }

    private void ClearHighlight()
    {
        if (lastHighlightedOption != null)
        {
            lastHighlightedOption.SetHighlighted(false);
            lastHighlightedOption = null;
        }
    }

    private static bool IsKeyboardConfirmPressedThisFrame()
    {
        return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
    }

    private bool TryHandleKeyboardNavigation()
    {
        if (keyboardStartResolved == false)
        {
            ResolveKeyboardStartOptionIfAny();
            keyboardStartResolved = true;
        }

        if (keyboardSelectedOption == null)
        {
            // No selection yet, can't navigate.
            return false;
        }

        Vector2 direction = Vector2.zero;
        bool pressed = false;

        if (Input.GetKeyDown(navUpKey))
        {
            direction = new Vector2(0f, 1f);
            pressed = true;
        }
        else if (Input.GetKeyDown(navDownKey))
        {
            direction = new Vector2(0f, -1f);
            pressed = true;
        }
        else if (Input.GetKeyDown(navLeftKey))
        {
            direction = new Vector2(-1f, 0f);
            pressed = true;
        }
        else if (Input.GetKeyDown(navRightKey))
        {
            direction = new Vector2(1f, 0f);
            pressed = true;
        }

        if (!pressed)
        {
            return false;
        }

        SiegeKeyboardUiNavLink link = keyboardSelectedOption.GetComponentInChildren<SiegeKeyboardUiNavLink>(true);
        if (link == null)
        {
            return false;
        }

        SiegeDifficultyOption neighbor = link.GetNeighbor(direction);
        if (neighbor == null)
        {
            return false;
        }

        SetKeyboardSelectedOption(neighbor);
        return true;
    }

    private void ResolveKeyboardStartOptionIfAny()
    {
        // Prefer an explicitly marked start element.
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

            SiegeKeyboardUiNavLink nav = option.GetComponentInChildren<SiegeKeyboardUiNavLink>(true);
            if (nav != null && nav.IsKeyboardStart)
            {
                if (TrySetKeyboardSelectedOption(option))
                {
                    return;
                }
            }
        }

        // Fallback: first enabled option in the scene.
        for (int i = 0; i < all.Length; i++)
        {
            SiegeDifficultyOption option = all[i];
            if (option == null)
            {
                continue;
            }

            // If the collider is disabled the option won't receive pointer input.
            // Keyboard can still select it, but using only active elements avoids surprises.
            if (option.gameObject.activeInHierarchy)
            {
                if (TrySetKeyboardSelectedOption(option))
                {
                    return;
                }
            }
        }
    }

    private void SetKeyboardSelectedOption(SiegeDifficultyOption option)
    {
        TrySetKeyboardSelectedOption(option);
    }

    private bool TrySetKeyboardSelectedOption(SiegeDifficultyOption option)
    {
        if (option == null)
        {
            return false;
        }

        // Don't switch to hidden options when submenu hides UI.
        if (!option.gameObject.activeInHierarchy)
        {
            return false;
        }

        keyboardSelectedOption = option;
        if (keyboardSelectedOption != lastHighlightedOption)
        {
            lastHighlightedOption?.SetHighlighted(false);
            keyboardSelectedOption.SetHighlighted(true);
            lastHighlightedOption = keyboardSelectedOption;
        }

        return true;
    }
}
