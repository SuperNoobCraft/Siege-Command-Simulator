using System;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Persists the Endless Survival all-time best to a txt file next to the game
/// (project root in the editor, folder with the .exe in standalone builds).
/// </summary>
public static class SiegeEndlessSurvivalRecord
{
    public const string FileName = "endless-survival-best.txt";

    private static float cachedBestSeconds;
    private static bool hasCachedBest;
    private static bool cacheLoaded;

    public static bool LastRunWasNewBest { get; private set; }

    public static string GetFilePath()
    {
        try
        {
            DirectoryInfo dataDir = new DirectoryInfo(Application.dataPath);
            string root = dataDir.Parent != null ? dataDir.Parent.FullName : Application.dataPath;
            return Path.Combine(root, FileName);
        }
        catch
        {
            return Path.Combine(Application.persistentDataPath, FileName);
        }
    }

    public static bool TryGetBestSeconds(out float bestSeconds)
    {
        EnsureCacheLoaded();
        bestSeconds = cachedBestSeconds;
        return hasCachedBest;
    }

    public static void SubmitRun(float seconds)
    {
        float runSeconds = Mathf.Max(0f, seconds);
        EnsureCacheLoaded();

        bool isNewBest = !hasCachedBest || runSeconds > cachedBestSeconds;
        LastRunWasNewBest = isNewBest;
        if (!isNewBest)
        {
            return;
        }

        cachedBestSeconds = runSeconds;
        hasCachedBest = true;
        WriteBestToFile(runSeconds);
    }

    private static void EnsureCacheLoaded()
    {
        if (cacheLoaded)
        {
            return;
        }

        cacheLoaded = true;
        hasCachedBest = TryReadBestFromFile(GetFilePath(), out cachedBestSeconds);
    }

    private static bool TryReadBestFromFile(string path, out float bestSeconds)
    {
        bestSeconds = 0f;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return false;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("SiegeEndlessSurvivalRecord: could not read '" + path + "': " + exception.Message);
            return false;
        }

        string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (float.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out bestSeconds)
                && bestSeconds >= 0f)
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteBestToFile(float bestSeconds)
    {
        string path = GetFilePath();
        string contents =
            "# Votanic Siege — Endless Survival all-time best" + Environment.NewLine
            + bestSeconds.ToString("0.0000", CultureInfo.InvariantCulture) + Environment.NewLine;

        try
        {
            File.WriteAllText(path, contents);
        }
        catch (Exception exception)
        {
            string fallbackPath = Path.Combine(Application.persistentDataPath, FileName);
            if (string.Equals(path, fallbackPath, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning(
                    "SiegeEndlessSurvivalRecord: could not write '" + path + "': " + exception.Message);
                return;
            }

            try
            {
                File.WriteAllText(fallbackPath, contents);
                Debug.LogWarning(
                    "SiegeEndlessSurvivalRecord: could not write '" + path + "', saved to '"
                    + fallbackPath + "' instead.");
            }
            catch (Exception fallbackException)
            {
                Debug.LogWarning(
                    "SiegeEndlessSurvivalRecord: could not write best time: " + fallbackException.Message);
            }
        }
    }
}
