using UnityEngine;

/// <summary>
/// Manual keyboard navigation links for world-space menu buttons (e.g. <see cref="SiegeDifficultyOption"/>).
///
/// Attach this to a clickable option and assign which neighboring option should be selected
/// when pressing the arrow key.
/// </summary>
[DisallowMultipleComponent]
public class SiegeKeyboardUiNavLink : MonoBehaviour
{
    [Tooltip("Optional: mark this option as the default keyboard selection when entering the menu.")]
    [SerializeField] private bool isKeyboardStart = false;

    [Header("Neighbors (clockwise navigation uses your assignment)")]
    [SerializeField] private SiegeDifficultyOption up;
    [SerializeField] private SiegeDifficultyOption down;
    [SerializeField] private SiegeDifficultyOption left;
    [SerializeField] private SiegeDifficultyOption right;

    public bool IsKeyboardStart => isKeyboardStart;

    public SiegeDifficultyOption GetNeighbor(Vector2 direction)
    {
        // direction: (-1,0)=left, (1,0)=right, (0,1)=up, (0,-1)=down
        if (direction.x < 0f)
            return left;
        if (direction.x > 0f)
            return right;
        if (direction.y > 0f)
            return up;
        if (direction.y < 0f)
            return down;

        return null;
    }
}

