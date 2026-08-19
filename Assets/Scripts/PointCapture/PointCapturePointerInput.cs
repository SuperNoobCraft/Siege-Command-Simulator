using UnityEngine;

/// <summary>
/// Debounced pointer presses for Point Capture UI (CAVE controllers often double-fire).
/// </summary>
public static class PointCapturePointerInput
{
    private const float DefaultPressCooldownSeconds = 0.34f;
    private const float AfterMenuCloseCooldownSeconds = 0.28f;

    private static float lastAcceptedPressUnscaledTime = float.NegativeInfinity;
    private static float acceptPressAfterUnscaledTime;

    public static void BeginCooldown(float seconds)
    {
        if (seconds <= 0f)
        {
            return;
        }

        acceptPressAfterUnscaledTime = Mathf.Max(
            acceptPressAfterUnscaledTime,
            Time.unscaledTime + seconds);
    }

    public static void BeginAfterMenuCloseCooldown()
    {
        BeginCooldown(AfterMenuCloseCooldownSeconds);
    }

    public static bool WasPointerPressedThisFrame(bool consumePress = true)
    {
        if (Time.unscaledTime < acceptPressAfterUnscaledTime)
        {
            return false;
        }

        if (!SiegeVrInput.WasPointerPressedThisFrame())
        {
            return false;
        }

        if (Time.unscaledTime - lastAcceptedPressUnscaledTime < DefaultPressCooldownSeconds)
        {
            return false;
        }

        if (consumePress)
        {
            lastAcceptedPressUnscaledTime = Time.unscaledTime;
        }

        return true;
    }

    public static void Reset()
    {
        acceptPressAfterUnscaledTime = 0f;
        lastAcceptedPressUnscaledTime = float.NegativeInfinity;
    }
}
