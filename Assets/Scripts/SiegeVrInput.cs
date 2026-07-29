using System.Collections.Generic;
using UnityEngine;
using Votanic.vXR.vGear;

/// <summary>
/// Shared CAVE / desktop pointer input for UI and wand commands.
/// </summary>
public static class SiegeVrInput
{
    private const int VirtualButtonCount = 8;

    private static readonly string[] CommonCommandNames =
    {
        "Grab",
        "Trigger",
        "Select",
        "Button",
        "Shoot",
    };

    private static readonly HashSet<string> IgnoredCommandNames = new HashSet<string>
    {
        "Move",
    };

    public static bool IsGameplayInputAllowed()
    {
        return SiegeSceneBootstrap.IsGameplayInputAllowed;
    }

    public static bool WasPointerPressedThisFrame()
    {
        if (!IsGameplayInputAllowed())
        {
            return false;
        }

        // Keyboard fallback for CAVE / broken controller: treat Enter as a pointer press.
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            return true;
        }

        if (SiegePlayEnvironment.IsDesktopInput)
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

        return WasAnyVrButtonPressedThisFrame();
    }

    public static bool IsPointerHeld()
    {
        if (!IsGameplayInputAllowed())
        {
            return false;
        }

        if (SiegePlayEnvironment.IsDesktopInput)
        {
            if (Input.GetMouseButton(0))
            {
                return true;
            }

            if (Input.touchCount > 0)
            {
                TouchPhase phase = Input.GetTouch(0).phase;
                return phase != TouchPhase.Ended && phase != TouchPhase.Canceled;
            }

            return false;
        }

        return IsAnyVrButtonHeld();
    }

    public static bool WasAnyVrButtonPressedThisFrame()
    {
        for (int i = 0; i < VirtualButtonCount; i++)
        {
            if (vGear.Ctrl.ButtonDown(i))
            {
                return true;
            }
        }

        foreach (string command in vGear.Cmd.AllReceived())
        {
            if (ShouldTreatAsButtonCommand(command) && vGear.Cmd.Received(command))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsAnyVrButtonHeld()
    {
        for (int i = 0; i < VirtualButtonCount; i++)
        {
            if (vGear.Ctrl.ButtonPress(i))
            {
                return true;
            }
        }

        foreach (string command in vGear.Cmd.AllReceived())
        {
            if (ShouldTreatAsButtonCommand(command) && vGear.Cmd.Value(command) > 0.5f)
            {
                return true;
            }
        }

        for (int i = 0; i < CommonCommandNames.Length; i++)
        {
            if (vGear.Cmd.Value(CommonCommandNames[i]) > 0.5f)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldTreatAsButtonCommand(string command)
    {
        return !string.IsNullOrEmpty(command) && !IgnoredCommandNames.Contains(command);
    }
}
