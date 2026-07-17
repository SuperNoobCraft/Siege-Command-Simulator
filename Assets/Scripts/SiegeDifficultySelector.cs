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

    private SiegeDifficultyOption hoveredOption;
    private SiegeDifficultyOption lastHighlightedOption;

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
            return;
        }

        SiegePvpSession pvp = SiegePvpSession.Instance;
        if (pvp != null && pvp.BlocksModeSelect)
        {
            ClearHighlight();
            hoveredOption = null;
            return;
        }

        SiegeDodgeArrowsSubmenu dodgeSubmenu = SiegeDodgeArrowsSubmenu.Instance;
        if (dodgeSubmenu != null && dodgeSubmenu.BlocksSelection)
        {
            ClearHighlight();
            hoveredOption = null;
            return;
        }

        if (!SiegeVrInput.IsGameplayInputAllowed())
        {
            ClearHighlight();
            hoveredOption = null;
            return;
        }

        Ray ray = BuildSelectionRay();
        UpdateHoveredOption(ray);

        if (!SiegeVrInput.WasPointerPressedThisFrame() || hoveredOption == null)
        {
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

            return;
        }

        if (hoveredOption.IsSubmenuBackButton)
        {
            SiegeDodgeArrowsSubmenu submenu = SiegeDodgeArrowsSubmenu.Instance;
            if (submenu != null)
            {
                submenu.CloseSubmenu();
            }

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
}
