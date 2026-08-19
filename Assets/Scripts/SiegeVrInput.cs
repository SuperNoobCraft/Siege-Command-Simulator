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

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            return true;
        }

        if (WasAnyVrButtonPressedThisFrame())
        {
            return true;
        }

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

    public static bool IsPointerHeld()
    {
        if (!IsGameplayInputAllowed())
        {
            return false;
        }

        if (IsAnyVrButtonHeld())
        {
            return true;
        }

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

    public static string BuildInputDebugText()
    {
        System.Text.StringBuilder text = new System.Text.StringBuilder(256);
        text.Append("input allowed=").Append(IsGameplayInputAllowed());
        text.Append("  mouse=").Append(Input.GetMouseButton(0) ? "held" : "-");
        text.Append("  vrHeld=").Append(IsAnyVrButtonHeld() ? "Y" : "N");
        text.Append('\n');
        text.Append("ctrlBtn ");
        for (int i = 0; i < VirtualButtonCount; i++)
        {
            bool down = false;
            bool held = false;
            try
            {
                down = vGear.Ctrl.ButtonDown(i);
                held = vGear.Ctrl.ButtonPress(i);
            }
            catch (System.Exception)
            {
            }

            if (down || held)
            {
                text.Append(i).Append(down ? "D" : "H").Append(' ');
            }
        }

        text.Append('\n').Append("cmds:");
        try
        {
            foreach (string command in vGear.Cmd.AllReceived())
            {
                if (string.IsNullOrEmpty(command))
                {
                    continue;
                }

                text.Append(' ').Append(command).Append('=').Append(vGear.Cmd.Value(command).ToString("0.00"));
            }
        }
        catch (System.Exception exception)
        {
            text.Append(" err ").Append(exception.Message);
        }

        return text.ToString();
    }
}
