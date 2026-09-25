using System;
using UnityEngine;

/// <summary>
/// Demo-day launch flag. Enable with <c>-demoDay</c> / <c>-pissEasy</c>,
/// env <c>SIEGE_DEMO_DAY=1</c>, or the Demo Day checkbox on SiegeGameManager.
/// </summary>
public static class PissEasyMode
{
    public const string CommandFlag = "-demoDay";
    public const string EnvVar = "SIEGE_DEMO_DAY";

    private static bool resolved;
    private static bool active;

    public static bool IsActive
    {
        get
        {
            EnsureResolved();
            return active;
        }
    }

    public static void EnsureResolved()
    {
        if (resolved)
        {
            return;
        }

        resolved = true;
        active = DetectFromEnvironment() || DetectFromCommandLine();
        if (active)
        {
            Debug.Log("Demo day launch flag detected (command line / env).");
        }
    }

    private static bool DetectFromEnvironment()
    {
        try
        {
            string value = Environment.GetEnvironmentVariable(EnvVar);
            if (string.IsNullOrEmpty(value))
            {
                value = Environment.GetEnvironmentVariable("DRAGONSHOT_PISS_EASY");
            }

            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            value = value.Trim();
            return value == "1"
                   || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("demoDay", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("pissEasy", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectFromCommandLine()
    {
        string[] args;
        try
        {
            args = Environment.GetCommandLineArgs();
        }
        catch
        {
            return false;
        }

        if (args == null)
        {
            return false;
        }

        for (int i = 0; i < args.Length; i++)
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
                || trimmed.Equals("piss-easy", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("pisseasymode", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
