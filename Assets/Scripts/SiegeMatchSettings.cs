using UnityEngine;

/// <summary>
/// Session match configuration chosen before gameplay begins.
/// </summary>
public static class SiegeMatchSettings
{
    public const int MinWaves = 2;
    public const int MaxSupportedWaves = 3;

    public static int MaxWaves { get; private set; } = MaxSupportedWaves;
    public static bool IsConfigured { get; private set; }

    public static void Configure(int maxWaves)
    {
        MaxWaves = Mathf.Clamp(maxWaves, MinWaves, MaxSupportedWaves);
        IsConfigured = true;
    }

    public static void Reset()
    {
        IsConfigured = false;
        MaxWaves = MaxSupportedWaves;
    }
}
