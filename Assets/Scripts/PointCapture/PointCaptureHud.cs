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
    }

    private void Update()
    {
        if (statusText == null || match == null)
        {
            return;
        }

        string status = BuildStatus();
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
                return "Point Capture\nPress Space to start.\n" + factionLine;
            case PointCaptureMatch.MatchState.Countdown:
                return "Point Capture\nStarting in " + Mathf.CeilToInt(match.RemainingSeconds) + "...\n" + factionLine;
            case PointCaptureMatch.MatchState.Ended:
                return "Point Capture\n"
                    + match.ResultMessage + "\n"
                    + FormatScores(redVillages, yellowVillages)
                    + "\nPress R to restart.";
            default:
                string fail = localInput != null ? localInput.LastFailMessage : string.Empty;
                string failLine = string.IsNullOrEmpty(fail) ? string.Empty : "\n" + fail;
                return "Point Capture   "
                    + FormatClock(match.RemainingSeconds) + "\n"
                    + FormatScores(redVillages, yellowVillages) + "\n"
                    + factionLine + "\n"
                    + "Tab faction   Right-click a village disc to raise\n"
                    + "Income 5+2/village per 10s   Upkeep 1/10s (3/s recovering)"
                    + failLine;
        }
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
}
