using UnityEngine;

/// <summary>
/// On-screen countdown for VotanicXR Evaluation License play-time limit (15 minutes).
/// Official docs: Unregistered = 1 min, Evaluation = 15 min, Academic/Pro = unlimited.
/// Scene reload does NOT reset the license clock — only a full app restart does.
///
/// Demo Day: while on the mode-select menu (or any non-Playing state) with under 1 minute
/// left, forces a full app relaunch so the license clock resets.
/// </summary>
public class VotanicTrialWatch : MonoBehaviour
{
    public const float EvaluationPlaySeconds = 15f * 60f;
    public const float UnregisteredPlaySeconds = 60f;
    public const float IdleRelaunchBelowSeconds = 60f;

    [Tooltip("Evaluation license session length (Votanic docs: 15 minutes).")]
    [SerializeField, Min(30f)] private float sessionSeconds = EvaluationPlaySeconds;
    [Tooltip("Warn helpers when remaining time drops to this.")]
    [SerializeField, Min(5f)] private float warnBelowSeconds = 120f;
    [Tooltip("When not Playing (menu / post-match) and remaining time is at or below this, relaunch.")]
    [SerializeField, Min(5f)] private float idleRelaunchBelowSeconds = IdleRelaunchBelowSeconds;
    [SerializeField] private bool showOnDesktop = true;
    [SerializeField] private int fontSize = 22;
    [SerializeField] private Vector2 screenOffset = new Vector2(16f, 16f);

    private static VotanicTrialWatch instance;
    private float sessionStartUnscaled;
    private bool loggedIdleRelaunch;
    private bool batsEnsured;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
        {
            return;
        }

        GameObject go = new GameObject("VotanicTrialWatch");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<VotanicTrialWatch>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        sessionStartUnscaled = Time.unscaledTime;
        PissEasyMode.EnsureResolved();
        Debug.Log(
            "VotanicTrialWatch: Evaluation play-time limit is "
            + (sessionSeconds / 60f).ToString("0.#")
            + " minutes from app start. demoDayFlag="
            + PissEasyMode.IsActive
            + ". Reloading the scene will NOT reset this — quit and relaunch.",
            this);
    }

    private void Start()
    {
        TryEnsureBats();
    }

    private static bool IsDemoDaySession()
    {
        return DemoDayRelaunch.IsDemoDaySession();
    }

    public float SecondsRemaining => Mathf.Max(0f, sessionSeconds - (Time.unscaledTime - sessionStartUnscaled));

    public bool IsInWarnWindow => SecondsRemaining <= warnBelowSeconds;

    public static bool IsWarnWindow => instance != null && instance.IsInWarnWindow;

    public static float RemainingSeconds =>
        instance != null ? instance.SecondsRemaining : float.MaxValue;

    private void Update()
    {
        if (!IsDemoDaySession())
        {
            return;
        }

        TryEnsureBats();

        float remaining = SecondsRemaining;
        if (remaining <= 0.05f)
        {
            DemoDayRelaunch.NotifyTimerExpired();
            return;
        }

        if (remaining > idleRelaunchBelowSeconds)
        {
            return;
        }

        // Menu / difficulty select / win-loss screens: not Playing → relaunch now.
        // Only stay alive through the last minute while a match is actively Playing.
        if (IsMatchActivelyPlaying())
        {
            return;
        }

        if (!loggedIdleRelaunch)
        {
            loggedIdleRelaunch = true;
            SiegeGameManager manager = SiegeGameManager.Instance;
            string state = manager != null ? manager.CurrentState.ToString() : "no-manager";
            Debug.Log(
                "VotanicTrialWatch: idle/menu (state=" + state
                + ") with " + remaining.ToString("0.0")
                + "s left — requesting Demo Day relaunch.",
                this);
        }

        DemoDayRelaunch.NotifyForcedRelaunch();
    }

    /// <summary>
    /// True only during an in-progress match. Mode select / menu / won / lost = idle.
    /// </summary>
    private static bool IsMatchActivelyPlaying()
    {
        SiegeGameManager manager = SiegeGameManager.Instance;
        return manager != null && manager.IsPlaying;
    }

    private void TryEnsureBats()
    {
        if (batsEnsured || !IsDemoDaySession())
        {
            return;
        }

        batsEnsured = true;
        DemoDayRelaunch.EnsureRelaunchScriptsPresent();
    }

    private void OnGUI()
    {
        if (!showOnDesktop && !Application.isEditor)
        {
            // Still draw in builds — operators need the countdown on CAVE desktop mirror.
        }

        float remaining = SecondsRemaining;
        int minutes = Mathf.FloorToInt(remaining / 60f);
        int seconds = Mathf.FloorToInt(remaining % 60f);
        string suffix = string.Empty;
        if (remaining <= 0.05f)
        {
            suffix = string.Empty;
        }
        else if (remaining <= idleRelaunchBelowSeconds && IsDemoDaySession())
        {
            suffix = IsMatchActivelyPlaying()
                ? "  — will relaunch after this round"
                : "  — relaunching (menu idle)…";
        }
        else if (IsInWarnWindow)
        {
            suffix = "  — RESTART SOON";
        }

        string label = remaining <= 0.05f
            ? "LICENSE TIMER EXPIRED — RESTART APP NOW"
            : "License timer  " + minutes.ToString("00") + ":" + seconds.ToString("00") + suffix;

        Color prev = GUI.color;
        GUI.color = remaining <= 0.05f
            ? Color.red
            : (IsInWarnWindow ? new Color(1f, 0.55f, 0.1f) : new Color(0.85f, 0.95f, 1f));

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperLeft
        };

        float width = 620f;
        float height = fontSize + 16f;
        GUI.Label(new Rect(screenOffset.x, screenOffset.y, width, height), label, style);
        GUI.color = prev;
    }
}
