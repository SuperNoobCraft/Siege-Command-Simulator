using UnityEngine;

/// <summary>
/// Session match configuration chosen before gameplay begins.
/// </summary>
public enum SiegeGameMode
{
    Demo = 0,
    Full = 1
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
    public static bool IsConfigured { get; private set; }

    public static bool IsDemoMode => GameMode == SiegeGameMode.Demo;
    public static float ActiveMoveSpeedScale => IsDemoMode ? DemoMoveSpeedScale : 1f;

    public static void Configure(SiegeGameMode gameMode, float demoMoveSpeedScale)
    {
        GameMode = gameMode;
        DemoMoveSpeedScale = Mathf.Clamp(demoMoveSpeedScale, 0.1f, 1f);
        MaxWaves = gameMode == SiegeGameMode.Demo ? DemoWaveCount : FullWaveCount;
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
    }
}
