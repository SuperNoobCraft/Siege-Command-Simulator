using UnityEngine;

/// <summary>
/// On-screen countdown for VotanicXR Evaluation License play-time limit (15 minutes).
/// Official docs: Unregistered = 1 min, Evaluation = 15 min, Academic/Pro = unlimited.
/// Scene reload does NOT reset the license clock — only a full app restart does.
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
    [SerializeField, Min(5f)] private float idleRelaunchBelowSeconds = IdleRelaunchBelowSeconds;
    [SerializeField] private bool showOnDesktop = true;
    [SerializeField] private int fontSize = 22;
    [SerializeField] private Vector2 screenOffset = new Vector2(16f, 16f);

    private static VotanicTrialWatch instance;
    private float sessionStartUnscaled;

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
            + " minutes from app start. demoDay="
            + IsDemoDaySession()
            + ". Reloading the scene will NOT reset this — quit and relaunch.",
            this);
    }

    private static bool IsDemoDaySession()
    {
        return PissEasyMode.IsActive
               || (SiegeGameManager.Instance != null && SiegeGameManager.Instance.IsDemoDayMode);
    }

    public float SecondsRemaining => Mathf.Max(0f, sessionSeconds - (Time.unscaledTime - sessionStartUnscaled));

    public bool IsInWarnWindow => SecondsRemaining <= warnBelowSeconds;

    public static bool IsWarnWindow => instance != null && instance.IsInWarnWindow;

    public static float RemainingSeconds =>
        instance != null ? instance.SecondsRemaining : float.MaxValue;

    private void Update()
    {
        if (!IsDemoDaySession() || Application.isEditor)
        {
            return;
        }

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

        // Only skip relaunch while a Timed Challenge match is actively Playing.
        if (!IsTimedRoundInProgress())
        {
            DemoDayRelaunch.NotifyForcedRelaunch();
        }
    }

    private static bool IsTimedRoundInProgress()
    {
        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager == null || !manager.IsPlaying)
        {
            return false;
        }

        // Demo Day only offers Timed Challenge; also treat any Playing state as in-game.
        return SiegeMatchSettings.IsDodgeArrowsTimedMode
               || manager.IsDemoDayMode
               || PissEasyMode.IsActive;
    }

    private void OnGUI()
    {
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
            suffix = IsTimedRoundInProgress()
                ? "  — will relaunch after this round"
                : "  — relaunching…";
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

        float width = 560f;
        float height = fontSize + 16f;
        GUI.Label(new Rect(screenOffset.x, screenOffset.y, width, height), label, style);
        GUI.color = prev;
    }
}
