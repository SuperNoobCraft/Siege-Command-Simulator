using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Desktop overlay for scores, manpower, timer, and raise prompts.
/// </summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public class PointCaptureHud : MonoBehaviour
{
    [SerializeField] private PointCaptureMatch match;
    [SerializeField] private PointCaptureLocalInput localInput;
    [SerializeField] private Text statusText;
    [SerializeField] private Text shadowText;
    [SerializeField] private bool showAimDebug = true;

    public void Configure(PointCaptureMatch captureMatch, PointCaptureLocalInput input, Text text, Text shadow)
    {
        match = captureMatch;
        localInput = input;
        statusText = text;
        shadowText = shadow;
    }

    private void Awake()
    {
        if (match == null)
        {
            match = PointCaptureMatch.Instance;
        }

        ExpandDebugHud();
    }

    private void ExpandDebugHud()
    {
        if (!showAimDebug || statusText == null)
        {
            return;
        }

        statusText.fontSize = 18;
        statusText.rectTransform.sizeDelta = new Vector2(980f, 780f);
        if (shadowText != null)
        {
            shadowText.fontSize = 18;
            shadowText.rectTransform.sizeDelta = statusText.rectTransform.sizeDelta;
        }
    }

    private void Update()
    {
        if (statusText == null || match == null)
        {
            return;
        }

        string status = BuildStatus();
        if (showAimDebug)
        {
            status += "\n\n" + BuildAimDebug();
        }

        statusText.text = status;
        if (shadowText != null)
        {
            shadowText.text = status;
        }
    }

    private string BuildStatus()
    {
        PointCaptureBoard board = match.Board;
        int redVillages = board != null ? board.CountOwned(CaptureOwner.Red) : 0;
        int yellowVillages = board != null ? board.CountOwned(CaptureOwner.Yellow) : 0;
        string factionLine = localInput != null
            ? "Commanding " + CaptureTeams.GetDisplayName(localInput.CommandFaction)
            : string.Empty;

        switch (match.CurrentState)
        {
            case PointCaptureMatch.MatchState.Waiting:
                return BuildReadyStatus(factionLine);
            case PointCaptureMatch.MatchState.Countdown:
                return "Point Capture\nStarting in " + Mathf.CeilToInt(match.RemainingSeconds) + "...\n" + factionLine;
            case PointCaptureMatch.MatchState.Ended:
                return "Point Capture\n"
                    + match.ResultMessage + "\n"
                    + FormatScores(redVillages, yellowVillages) + "\n"
                    + "Yellow reset: " + (match.IsYellowResetRequested ? "Yes" : "Waiting")
                    + "    Red reset: " + (match.IsRedResetRequested ? "Yes" : "Waiting") + "\n"
                    + factionLine + "\n"
                    + (match.HasConnectedPeer
                        ? "Press any button to reset this cave. Both sides needed."
                        : "Press any button to reset this side. Tab to the other side, then press again.");
            default:
                string fail = localInput != null ? localInput.LastFailMessage : string.Empty;
                string failLine = string.IsNullOrEmpty(fail) ? string.Empty : "\n" + fail;
                return "Point Capture   "
                    + FormatClock(match.RemainingSeconds) + "\n"
                    + FormatScores(redVillages, yellowVillages) + "\n"
                    + factionLine + "\n"
                    + (match.HasConnectedPeer
                        ? "Click a village disc to raise\n"
                        : "Tab switches side and spawn   Click a village disc to raise\n")
                    + "Income 5+2/village per 10s   Upkeep 1/10s (3/s recovering)"
                    + failLine;
        }
    }

    private string BuildReadyStatus(string factionLine)
    {
        string yellow = match.IsYellowReady ? "Ready" : "Waiting";
        string red = match.IsRedReady ? "Ready" : "Waiting";
        bool networked = match.HasConnectedPeer;
        string howToReady = networked
            ? "Press any button to ready this cave.\nHost is Yellow, client is Red."
            : "Press any button to ready this side.\nTab to the other side, then press again.";
        return "Point Capture\n"
            + "Yellow: " + yellow + "    Red: " + red + "\n"
            + factionLine + "\n"
            + howToReady;
    }

    private string FormatScores(int redVillages, int yellowVillages)
    {
        return "Red  " + match.RedScore.ToString("0") + "  (" + redVillages + " villages, "
            + match.RedManpower.ToString("0") + " MP)\n"
            + "Yellow  " + match.YellowScore.ToString("0") + "  (" + yellowVillages + " villages, "
            + match.YellowManpower.ToString("0") + " MP)";
    }

    private static string FormatClock(float seconds)
    {
        int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
        int minutes = total / 60;
        int remainder = total % 60;
        return minutes.ToString() + ":" + remainder.ToString("00");
    }

    private string BuildAimDebug()
    {
        string env = "env " + SiegePlayEnvironment.ActiveMode
            + " cfg=" + (SiegePlayEnvironment.Instance != null
                ? SiegePlayEnvironment.Instance.ConfiguredMode.ToString()
                : "none")
            + " vCast=" + SiegePlayEnvironment.DescribeVcastEnvironment()
            + " desk=" + (SiegePlayEnvironment.IsDesktopInput ? "Y" : "N")
            + " xr=" + (SiegePlayEnvironment.IsTrackedXr ? "Y" : "N");

        VotanicWandRtsCommander commander = localInput != null ? localInput.DebugWandCommander : null;
        string aim = commander != null ? commander.BuildAimDebugText() : "no wand commander";
        string raise = localInput != null ? localInput.BuildRaiseAimDebug() : string.Empty;
        return env + "\n" + SiegeVrInput.BuildInputDebugText() + "\n" + aim + "\n" + raise;
    }
}
