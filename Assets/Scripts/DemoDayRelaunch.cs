using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

/// <summary>
/// Demo-day helper: kill this process and relaunch <see cref="LaunchBatName"/>.
/// Bats sit next to the Windows player / CAVE launch bats.
/// </summary>
public static class DemoDayRelaunch
{
    public const string LaunchBatName = "VotanicXR_[CAVE][XR] (Demo Day).bat";
    public const string KillerBatName = "RelaunchDemoDay.bat";

    public const string LaunchBatContents =
        "@echo off\r\n"
        + "cd /d \"%~dp0\"\r\n"
        + "start \"\" \"VotanicXR.exe\" --config \"%VOTANIC_PATH%\\Configs\\ConfigCAVE.vxrc\" "
        + "--setting \"VotanicXR/Settings/Setting.vxrs\" --option \"VotanicXR/Options/Option.vxro\" "
        + "--ref \"null\" --version \"2020.10.2\" -popupwindow -demoDay\r\n";

    public const string KillerBatContents =
        "@echo off\r\n"
        + "cd /d \"%~dp0\"\r\n"
        + "set \"PID=%~1\"\r\n"
        + "timeout /t 3 /nobreak >nul\r\n"
        + "if not \"%PID%\"==\"\" taskkill /F /PID %PID% >nul 2>&1\r\n"
        + "taskkill /F /IM VotanicXR.exe >nul 2>&1\r\n"
        + "timeout /t 2 /nobreak >nul\r\n"
        + "start \"\" \"%~dp0VotanicXR_[CAVE][XR] (Demo Day).bat\"\r\n"
        + "exit /b 0\r\n";

    private static bool started;
    private static string loggedMissingDir;

    /// <summary>After a round ends with under 2 minutes of license time left.</summary>
    public static void NotifyGameEnded()
    {
        if (!IsDemoDaySession() || !VotanicTrialWatch.IsWarnWindow)
        {
            return;
        }

        TryStartRelaunch("round ended with under 2 min left");
    }

    /// <summary>Idle / between rounds / mode select with under 1 minute left.</summary>
    public static void NotifyForcedRelaunch()
    {
        if (!IsDemoDaySession())
        {
            return;
        }

        TryStartRelaunch("under 1 min left and not in an active timed round");
    }

    /// <summary>License clock hit zero — quit even mid-fight.</summary>
    public static void NotifyTimerExpired()
    {
        if (!IsDemoDaySession())
        {
            return;
        }

        TryStartRelaunch("license timer expired");
    }

    private static bool IsDemoDaySession()
    {
        return PissEasyMode.IsActive
               || (SiegeGameManager.Instance != null && SiegeGameManager.Instance.IsDemoDayMode);
    }

    private static void TryStartRelaunch(string reason)
    {
        if (started || Application.isEditor)
        {
            return;
        }

        if (!TryResolveBatPaths(out string dir, out string killer, out string launch))
        {
            return;
        }

        started = true;
        int pid = Process.GetCurrentProcess().Id;
        UnityEngine.Debug.Log(
            "DemoDayRelaunch: " + reason
            + " — dir=" + dir
            + " killer=" + killer
            + " pid=" + pid);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = killer,
                Arguments = pid.ToString(),
                WorkingDirectory = dir,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            started = false;
            UnityEngine.Debug.LogWarning("DemoDayRelaunch failed to start killer bat: " + exception.Message);
            return;
        }

        Application.Quit();
    }

    private static bool TryResolveBatPaths(out string dir, out string killer, out string launch)
    {
        dir = null;
        killer = null;
        launch = null;

        string[] candidates = BuildSearchDirs();
        for (int i = 0; i < candidates.Length; i++)
        {
            string candidate = candidates[i];
            if (string.IsNullOrEmpty(candidate) || !Directory.Exists(candidate))
            {
                continue;
            }

            string k = Path.Combine(candidate, KillerBatName);
            string l = Path.Combine(candidate, LaunchBatName);
            if (File.Exists(k) && File.Exists(l))
            {
                dir = candidate;
                killer = k;
                launch = l;
                return true;
            }
        }

        string joined = string.Join(" | ", candidates);
        if (loggedMissingDir != joined)
        {
            loggedMissingDir = joined;
            UnityEngine.Debug.LogWarning(
                "DemoDayRelaunch: missing '" + KillerBatName + "' / '" + LaunchBatName
                + "'. Searched: " + joined);
        }

        return false;
    }

    private static string[] BuildSearchDirs()
    {
        string dataParent = null;
        try
        {
            dataParent = Directory.GetParent(Application.dataPath)?.FullName;
        }
        catch
        {
        }

        string exeDir = null;
        try
        {
            string[] args = Environment.GetCommandLineArgs();
            if (args != null && args.Length > 0 && !string.IsNullOrEmpty(args[0]))
            {
                exeDir = Path.GetDirectoryName(Path.GetFullPath(args[0]));
            }
        }
        catch
        {
        }

        string processDir = null;
        try
        {
            processDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
        }
        catch
        {
        }

        string cwd = null;
        try
        {
            cwd = Directory.GetCurrentDirectory();
        }
        catch
        {
        }

        return new[]
        {
            dataParent,
            exeDir,
            processDir,
            cwd,
            Application.dataPath
        };
    }
}
