using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Keeps the Game view focused and hides the cursor for desktop PC testing.
/// Disabled automatically in CAVE/HMD play environments.
/// </summary>
public class FocusFix : MonoBehaviour
{
    [SerializeField] private bool onlyOnDesktop = true;

    private void Awake()
    {
        if (onlyOnDesktop && !SiegePlayEnvironment.IsDesktopInput)
        {
            enabled = false;
            return;
        }

#if UNITY_EDITOR
        var gameWindow = EditorWindow
            .GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
        gameWindow.Focus();
        gameWindow.SendEvent(new Event
        {
            button = 0,
            clickCount = 1,
            type = EventType.MouseDown,
            mousePosition = gameWindow.rootVisualElement.contentRect.center
        });
#endif

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
