using UnityEngine;

/// <summary>
/// Session match configuration chosen before gameplay begins.
/// </summary>
public enum SiegeGameMode
{
    Demo = 0,
    Full = 1,
    DodgeArrows = 2,
    SiegePvp = 3,
    DodgeArrowsEndless = 4,
    CapturePvP = 5
}

public static class SiegeMatchSettings
{
    public const int DemoWaveCount = 2;
    public const int FullWaveCount = 3;
    public const int MinWaves = DemoWaveCount;
    public const int MaxSupportedWaves = FullWaveCount;

    public static SiegeGameMode GameMode { get; private set; } = SiegeGameMode.Full;
    public static int MaxWaves { get; private set; } = FullWaveCount;
    public static float DemoMoveSpeedScale { get; private set; } = 0.6f;
    public static float PvpMoveSpeedScale { get; private set; } = 1f;
    public static float PvpMatchDurationSeconds { get; private set; } = 180f;
    public static bool IsConfigured { get; private set; }

    public static bool IsDemoMode => GameMode == SiegeGameMode.Demo;
    public static bool IsDodgeArrowsMode => IsDodgeArrowsTimedMode || IsDodgeArrowsEndlessMode;
    public static bool IsDodgeArrowsTimedMode => GameMode == SiegeGameMode.DodgeArrows;
    public static bool IsDodgeArrowsEndlessMode => GameMode == SiegeGameMode.DodgeArrowsEndless;
    public static bool IsSiegePvpMode => GameMode == SiegeGameMode.SiegePvp || GameMode == SiegeGameMode.CapturePvP;
    public static bool IsArenaSurvivalMode => IsDodgeArrowsMode;
    public static bool HasTroopCombat => !IsDodgeArrowsMode;
    public static float ActiveMoveSpeedScale
    {
        get
        {
            if (IsDemoMode)
            {
                return DemoMoveSpeedScale;
            }

            if (IsSiegePvpMode)
            {
                return PvpMoveSpeedScale;
            }

            return 1f;
        }
    }

    public static void Configure(SiegeGameMode gameMode, float demoMoveSpeedScale)
    {
        Configure(gameMode, demoMoveSpeedScale, PvpMatchDurationSeconds, PvpMoveSpeedScale);
    }

    public static void Configure(
        SiegeGameMode gameMode,
        float demoMoveSpeedScale,
        float pvpMatchDurationSeconds,
        float pvpMoveSpeedScale)
    {
        GameMode = gameMode;
        DemoMoveSpeedScale = Mathf.Clamp(demoMoveSpeedScale, 0.1f, 1f);
        PvpMatchDurationSeconds = Mathf.Max(30f, pvpMatchDurationSeconds);
        PvpMoveSpeedScale = Mathf.Clamp(pvpMoveSpeedScale, 0.1f, 2f);
        MaxWaves = gameMode switch
        {
            SiegeGameMode.Demo => DemoWaveCount,
            SiegeGameMode.DodgeArrows => 0,
            SiegeGameMode.DodgeArrowsEndless => 0,
            SiegeGameMode.SiegePvp => 0,
            SiegeGameMode.CapturePvP => 0,
            _ => FullWaveCount
        };
        IsConfigured = true;
    }

    public static void Configure(int maxWaves)
    {
        Configure(
            maxWaves <= DemoWaveCount ? SiegeGameMode.Demo : SiegeGameMode.Full,
            DemoMoveSpeedScale);
    }

    public static void Reset()
    {
        IsConfigured = false;
        GameMode = SiegeGameMode.Full;
        MaxWaves = FullWaveCount;
        PvpMoveSpeedScale = 1f;
        PvpMatchDurationSeconds = 180f;
    }
}
