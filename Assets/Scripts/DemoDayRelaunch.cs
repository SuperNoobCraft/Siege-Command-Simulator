using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Demo-day helper: kill this process and relaunch <see cref="LaunchBatName"/>.
/// Uses the running player exe name (e.g. SiegeCommandSimulator.exe), not a hardcoded VotanicXR.exe.
/// Existing bats next to the build are preferred and never overwritten.
/// </summary>
public static class DemoDayRelaunch
{
    public const string LaunchBatName = "VotanicXR_[CAVE][XR] (Demo Day).bat";
    public const string KillerBatName = "RelaunchDemoDay.bat";
    public const string DefaultPlayerExeName = "SiegeCommandSimulator.exe";

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

    /// <summary>Idle / menu / between rounds with under 1 minute left.</summary>
    public static void NotifyForcedRelaunch()
    {
        if (!IsDemoDaySession())
        {
            return;
        }

        TryStartRelaunch("under 1 min left and not in an active match (menu/idle)");
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

    public static bool IsDemoDaySession()
    {
        return PissEasyMode.IsActive
               || (SiegeGameManager.Instance != null && SiegeGameManager.Instance.IsDemoDayMode);
    }

    /// <summary>Call once on boot so a killer bat exists beside the player before the idle window.</summary>
    public static void EnsureRelaunchScriptsPresent()
    {
        if (Application.isEditor)
        {
            return;
        }

        TryResolveBatPaths(out _, out _, out _, createIfMissing: true);
    }

    private static void TryStartRelaunch(string reason)
    {
        if (started)
        {
            return;
        }

        if (Application.isEditor)
        {
            UnityEngine.Debug.Log(
                "DemoDayRelaunch: would relaunch (" + reason + ") — skipped in Editor.");
            return;
        }

        if (!TryResolveBatPaths(out string dir, out string killer, out string launch, createIfMissing: true))
        {
            if (!TryRestartCurrentExecutable(reason))
            {
                UnityEngine.Debug.LogError(
                    "DemoDayRelaunch: cannot relaunch (" + reason
                    + "). Place '" + KillerBatName + "' and '" + LaunchBatName
                    + "' next to " + ResolvePlayerExeName() + ".");
            }

            return;
        }

        started = true;
        int pid = Process.GetCurrentProcess().Id;
        UnityEngine.Debug.Log(
            "DemoDayRelaunch: " + reason
            + " — dir=" + dir
            + " killer=" + killer
            + " launch=" + launch
            + " exe=" + ResolvePlayerExeName()
            + " pid=" + pid);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c start \"\" \"" + killer + "\" " + pid,
                WorkingDirectory = dir,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception exception)
        {
            started = false;
            UnityEngine.Debug.LogWarning(
                "DemoDayRelaunch failed to start killer bat: " + exception.Message
                + " — trying exe restart fallback.");
            TryRestartCurrentExecutable(reason);
            return;
        }

        Application.Quit();
    }

    private static bool TryRestartCurrentExecutable(string reason)
    {
        try
        {
            if (!TryResolvePlayerExePath(out string exePath))
            {
                return false;
            }

            string args = BuildRestartArguments();
            string dir = Path.GetDirectoryName(exePath) ?? string.Empty;

            started = true;
            UnityEngine.Debug.Log(
                "DemoDayRelaunch: " + reason + " — exe fallback " + exePath + " " + args);

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                WorkingDirectory = dir,
                UseShellExecute = true
            });
            Application.Quit();
            return true;
        }
        catch (Exception exception)
        {
            started = false;
            UnityEngine.Debug.LogWarning("DemoDayRelaunch exe fallback failed: " + exception.Message);
            return false;
        }
    }

    private static string BuildRestartArguments()
    {
        string[] args;
        try
        {
            args = Environment.GetCommandLineArgs();
        }
        catch
        {
            return "-demoDay";
        }

        var builder = new StringBuilder();
        bool hasDemoDay = false;
        for (int i = 1; i < (args?.Length ?? 0); i++)
        {
            string arg = args[i];
            if (string.IsNullOrEmpty(arg))
            {
                continue;
            }

            string trimmed = arg.Trim().TrimStart('-');
            if (trimmed.Equals("demoDay", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("demo-day", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("pissEasy", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("piss-easy", StringComparison.OrdinalIgnoreCase))
            {
                hasDemoDay = true;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(QuoteArg(arg));
        }

        if (!hasDemoDay)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append("-demoDay");
        }

        return builder.ToString();
    }

    private static string QuoteArg(string arg)
    {
        if (arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
        {
            return arg;
        }

        return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }

    private static bool TryResolveBatPaths(
        out string dir,
        out string killer,
        out string launch,
        bool createIfMissing)
    {
        dir = null;
        killer = null;
        launch = null;

        string[] candidates = BuildSearchDirs();

        // Prefer an existing pair (user-edited bats) — never overwrite them.
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

        // Launch bat alone is enough if we can generate a matching killer.
        string launchOnlyDir = null;
        string launchOnlyPath = null;
        for (int i = 0; i < candidates.Length; i++)
        {
            string candidate = candidates[i];
            if (string.IsNullOrEmpty(candidate) || !Directory.Exists(candidate))
            {
                continue;
            }

            string l = Path.Combine(candidate, LaunchBatName);
            if (File.Exists(l))
            {
                launchOnlyDir = candidate;
                launchOnlyPath = l;
                break;
            }
        }

        if (!createIfMissing)
        {
            LogMissing(candidates);
            return false;
        }

        string playerExe = ResolvePlayerExeName();

        if (!string.IsNullOrEmpty(launchOnlyDir))
        {
            string k = Path.Combine(launchOnlyDir, KillerBatName);
            if (File.Exists(k) || TryWriteKillerBat(k, playerExe))
            {
                dir = launchOnlyDir;
                killer = k;
                launch = launchOnlyPath;
                UnityEngine.Debug.Log(
                    "DemoDayRelaunch: using existing launch bat, killer=" + killer
                    + " (exe=" + playerExe + ")");
                return true;
            }
        }

        int preferred = IndexOfPlayerExeDir(candidates, playerExe);
        if (preferred >= 0
            && TryWriteBatPair(candidates[preferred], playerExe, out killer, out launch))
        {
            dir = candidates[preferred];
            UnityEngine.Debug.Log(
                "DemoDayRelaunch: wrote relaunch bats to " + dir + " for " + playerExe);
            return true;
        }

        for (int i = 0; i < candidates.Length; i++)
        {
            if (i == preferred)
            {
                continue;
            }

            string candidate = candidates[i];
            if (string.IsNullOrEmpty(candidate) || !Directory.Exists(candidate))
            {
                continue;
            }

            if (TryWriteBatPair(candidate, playerExe, out killer, out launch))
            {
                dir = candidate;
                UnityEngine.Debug.Log(
                    "DemoDayRelaunch: wrote relaunch bats to " + dir + " for " + playerExe);
                return true;
            }
        }

        LogMissing(candidates);
        return false;
    }

    private static void LogMissing(string[] candidates)
    {
        string joined = string.Join(" | ", candidates);
        if (loggedMissingDir == joined)
        {
            return;
        }

        loggedMissingDir = joined;
        UnityEngine.Debug.LogWarning(
            "DemoDayRelaunch: missing '" + KillerBatName + "' / '" + LaunchBatName
            + "'. Searched: " + joined);
    }

    private static int IndexOfPlayerExeDir(string[] candidates, string playerExeName)
    {
        for (int i = 0; i < candidates.Length; i++)
        {
            string candidate = candidates[i];
            if (string.IsNullOrEmpty(candidate) || !Directory.Exists(candidate))
            {
                continue;
            }

            if (File.Exists(Path.Combine(candidate, playerExeName)))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryWriteBatPair(
        string dir,
        string playerExeName,
        out string killer,
        out string launch)
    {
        killer = Path.Combine(dir, KillerBatName);
        launch = Path.Combine(dir, LaunchBatName);

        // Never clobber a working launch bat the operator already edited.
        if (File.Exists(launch) && File.Exists(killer))
        {
            return true;
        }

        try
        {
            if (!File.Exists(launch))
            {
                File.WriteAllText(launch, BuildLaunchBatContents(playerExeName));
            }

            if (!File.Exists(killer))
            {
                File.WriteAllText(killer, BuildKillerBatContents(playerExeName));
            }

            return File.Exists(killer) && File.Exists(launch);
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning(
                "DemoDayRelaunch: could not write bats to " + dir + ": " + exception.Message);
            killer = null;
            launch = null;
            return false;
        }
    }

    private static bool TryWriteKillerBat(string killerPath, string playerExeName)
    {
        try
        {
            File.WriteAllText(killerPath, BuildKillerBatContents(playerExeName));
            return File.Exists(killerPath);
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning(
                "DemoDayRelaunch: could not write killer bat: " + exception.Message);
            return false;
        }
    }

    private static string BuildLaunchBatContents(string playerExeName)
    {
        return "@echo off\r\n"
               + "cd /d \"%~dp0\"\r\n"
               + "start \"\" \"" + playerExeName + "\" --config \"%VOTANIC_PATH%\\Configs\\ConfigCAVE.vxrc\" "
               + "--setting \"VotanicXR/Settings/Setting.vxrs\" --option \"VotanicXR/Options/Option.vxro\" "
               + "--ref \"null\" --version \"2020.10.2\" -popupwindow -demoDay\r\n";
    }

    private static string BuildKillerBatContents(string playerExeName)
    {
        return "@echo off\r\n"
               + "cd /d \"%~dp0\"\r\n"
               + "set \"PID=%~1\"\r\n"
               + "timeout /t 3 /nobreak >nul\r\n"
               + "if not \"%PID%\"==\"\" taskkill /F /PID %PID% >nul 2>&1\r\n"
               + "taskkill /F /IM " + playerExeName + " >nul 2>&1\r\n"
               + "timeout /t 2 /nobreak >nul\r\n"
               + "start \"\" \"%~dp0" + LaunchBatName + "\"\r\n"
               + "exit /b 0\r\n";
    }

    private static string ResolvePlayerExeName()
    {
        if (TryResolvePlayerExePath(out string path))
        {
            return Path.GetFileName(path);
        }

        return DefaultPlayerExeName;
    }

    private static bool TryResolvePlayerExePath(out string exePath)
    {
        exePath = null;
        try
        {
            exePath = Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch
        {
        }

        if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
        {
            return true;
        }

        try
        {
            string[] args = Environment.GetCommandLineArgs();
            if (args != null && args.Length > 0 && !string.IsNullOrEmpty(args[0]))
            {
                exePath = Path.GetFullPath(args[0]);
                if (File.Exists(exePath))
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        try
        {
            string parent = Directory.GetParent(Application.dataPath)?.FullName;
            if (!string.IsNullOrEmpty(parent))
            {
                string siege = Path.Combine(parent, DefaultPlayerExeName);
                if (File.Exists(siege))
                {
                    exePath = siege;
                    return true;
                }

                // Legacy / shared CAVE folder name — only as last resort.
                string votanic = Path.Combine(parent, "VotanicXR.exe");
                if (File.Exists(votanic))
                {
                    exePath = votanic;
                    return true;
                }
            }
        }
        catch
        {
        }

        exePath = null;
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
            if (TryResolvePlayerExePath(out string exePath))
            {
                exeDir = Path.GetDirectoryName(exePath);
            }
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
            exeDir,
            dataParent,
            cwd,
            Application.dataPath
        };
    }
}
