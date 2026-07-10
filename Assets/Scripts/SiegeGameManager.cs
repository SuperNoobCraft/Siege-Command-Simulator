using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Central match flow: difficulty select, cannon occupation loss, commander arrow loss, and timed victory.
/// </summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(SiegePlayEnvironment))]
[RequireComponent(typeof(SiegeSceneBootstrap))]
public class SiegeGameManager : MonoBehaviour
{
    public enum MatchState
    {
        SelectingDifficulty,
        Playing,
        Won,
        Lost
    }

    public static SiegeGameManager Instance { get; private set; }
    public static int PlaySessionId => playSessionId;

    private static int playSessionId;

    [Header("Victory")]
    [Tooltip("Seconds after the final wave begins before friendly cannons auto-fire and the match is won.")]
    [SerializeField, Min(0f)] private float secondsAfterFinalWaveUntilCannonsFire = 50f;
    [Tooltip("Fallback if EnemyWaveController is missing.")]
    [SerializeField, Min(0f)] private float fallbackFinalWaveStartTimeSeconds = 130f;
    [SerializeField] private UnityEvent onCannonsFired;
    [SerializeField] private UnityEvent onVictory;

    [Header("Defeat")]
    [SerializeField, Min(0.1f)] private float cannonOccupationLossSeconds = 5f;
    [SerializeField] private UnityEvent onDefeat;

    [Header("Match End")]
    [SerializeField] private bool pauseTimeOnMatchEnd = true;
    [SerializeField, Min(0f)] private float matchEndPauseDelay = 1.25f;

    [Header("Debug")]
    [SerializeField] private bool logMatchEvents = true;

    private MatchState currentState = MatchState.SelectingDifficulty;
    private float matchElapsedSeconds;
    private int initializedPlaySessionId = -1;
    private string defeatReason;
    private Coroutine pauseCoroutine;

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    private static void RegisterEditorPlayModeCallbacks()
    {
        EditorApplication.playModeStateChanged -= HandleEditorPlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandleEditorPlayModeStateChanged;
    }

    private static void HandleEditorPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            playSessionId++;
            Instance = null;
        }
    }
#else
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void HandleSubsystemRegistration()
    {
        playSessionId++;
        Instance = null;
    }
#endif

    public MatchState CurrentState => currentState;
    public float CannonOccupationLossSeconds => cannonOccupationLossSeconds;
    public float SecondsAfterFinalWaveUntilCannonsFire => secondsAfterFinalWaveUntilCannonsFire;
    public string DefeatReason => defeatReason;
    public bool IsPlaying => currentState == MatchState.Playing;
    public float MatchElapsedSeconds => matchElapsedSeconds;
    public int MaxWaves => SiegeMatchSettings.MaxWaves;
    public float SecondsUntilCannonsFire =>
        Mathf.Max(0f, GetFinalWaveStartTimeSeconds() + secondsAfterFinalWaveUntilCannonsFire - MatchElapsedSeconds);
    public float TotalSecondsUntilCannonsFire =>
        Mathf.Max(0f, GetFinalWaveStartTimeSeconds() + secondsAfterFinalWaveUntilCannonsFire);
    public SiegePlayEnvironmentMode PlayEnvironment => SiegePlayEnvironment.ActiveMode;
    public bool UsesDesktopInput => SiegePlayEnvironment.IsDesktopInput;
    public bool UsesTrackedXr => SiegePlayEnvironment.IsTrackedXr;

    public event Action<MatchState> MatchStateChanged;
    public event Action<float> CannonFireCountdownUpdated;
    public event Action CannonsFired;
    public event Action CannonOverrun;

    private void Awake()
    {
        RegisterInstance();
        EnsureSessionInitialized();
    }

    private void RegisterInstance()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple SiegeGameManager instances found. Using the most recent one.", this);
        }

        Instance = this;
    }

    private void Start()
    {
        EnsureSessionInitialized();
    }

    private void EnsureSessionInitialized()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (initializedPlaySessionId == playSessionId)
        {
            return;
        }

        initializedPlaySessionId = playSessionId;
        SiegeMatchSettings.Reset();
        BeginDifficultySelection();

        EnemyWaveController waveController = EnemyWaveController.Instance;
        if (waveController != null)
        {
            waveController.PrepareForMatchStart();
        }
    }

    public void ConfirmDifficulty(int maxWaves)
    {
        if (currentState != MatchState.SelectingDifficulty)
        {
            return;
        }

        SiegeMatchSettings.Configure(maxWaves);
        BeginNewMatch();
        ResetCannonSites();

        EnemyWaveController waveController = EnemyWaveController.Instance;
        if (waveController != null)
        {
            waveController.PrepareForMatchStart();
        }

        if (logMatchEvents)
        {
            Debug.Log(
                "Siege match started with " + SiegeMatchSettings.MaxWaves + " waves. Session "
                + playSessionId + ". Cannons fire in "
                + TotalSecondsUntilCannonsFire.ToString("F0") + "s.",
                this);
        }

        CannonFireCountdownUpdated?.Invoke(SecondsUntilCannonsFire);
    }

    private void BeginDifficultySelection()
    {
        if (pauseCoroutine != null)
        {
            StopCoroutine(pauseCoroutine);
            pauseCoroutine = null;
        }

        Time.timeScale = 1f;
        matchElapsedSeconds = 0f;
        defeatReason = null;
        SetMatchState(MatchState.SelectingDifficulty);
    }

    private void BeginNewMatch()
    {
        if (pauseCoroutine != null)
        {
            StopCoroutine(pauseCoroutine);
            pauseCoroutine = null;
        }

        Time.timeScale = 1f;
        defeatReason = null;
        matchElapsedSeconds = 0f;
        SetMatchState(MatchState.Playing);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnEnable()
    {
        if (Application.isPlaying)
        {
            RegisterInstance();
            EnsureSessionInitialized();
        }

        EnemyWaveController.WaveDeployed += HandleWaveDeployed;
    }

    private void OnDisable()
    {
        EnemyWaveController.WaveDeployed -= HandleWaveDeployed;
    }

    private void Update()
    {
        RegisterInstance();
        EnsureSessionInitialized();

        if (currentState != MatchState.Playing)
        {
            return;
        }

        if (Time.timeScale <= 0f)
        {
            Time.timeScale = 1f;
        }

        matchElapsedSeconds += Time.unscaledDeltaTime;
        CannonFireCountdownUpdated?.Invoke(SecondsUntilCannonsFire);

        if (SecondsUntilCannonsFire <= 0f && HasFinalWaveStarted())
        {
            FireCannonsAndWin();
        }
    }

    private void HandleWaveDeployed(int waveNumber)
    {
        if (currentState != MatchState.Playing)
        {
            return;
        }

        EnemyWaveController waveController = EnemyWaveController.Instance;
        int finalWave = waveController != null ? waveController.FinalWaveNumber : SiegeMatchSettings.MaxWaves;
        if (waveNumber != finalWave)
        {
            return;
        }

        if (logMatchEvents)
        {
            Debug.Log(
                "Final wave (" + waveNumber + ") started. Cannons will fire in "
                + secondsAfterFinalWaveUntilCannonsFire.ToString("F1") + "s.",
                this);
        }

        CannonFireCountdownUpdated?.Invoke(SecondsUntilCannonsFire);
    }

    private float GetFinalWaveStartTimeSeconds()
    {
        EnemyWaveController waveController = EnemyWaveController.Instance;
        if (waveController != null)
        {
            return waveController.FinalWaveSpawnTimeSeconds;
        }

        return fallbackFinalWaveStartTimeSeconds;
    }

    private void ResetCannonSites()
    {
        SiegeCannonSite[] sites = FindObjectsOfType<SiegeCannonSite>(includeInactive: true);
        for (int i = 0; i < sites.Length; i++)
        {
            if (sites[i] != null)
            {
                sites[i].ResetForMatchStart();
            }
        }
    }

    private bool HasFinalWaveStarted()
    {
        EnemyWaveController waveController = EnemyWaveController.Instance;
        return waveController == null || waveController.HasFinalWaveStarted;
    }

    public void NotifyCommanderDefeated()
    {
        TriggerDefeat("The commander was struck down by enemy arrows.");
    }

    public void NotifyCannonOccupied(SiegeCannonSite cannonSite)
    {
        CannonOverrun?.Invoke();
        string cannonName = cannonSite != null ? cannonSite.name : "Cannon";
        TriggerDefeat("Enemy forces occupied " + cannonName + " for too long.");
    }

    public void FireCannonsAndWin()
    {
        if (currentState != MatchState.Playing)
        {
            return;
        }

        CannonsFired?.Invoke();
        onCannonsFired?.Invoke();

        if (logMatchEvents)
        {
            Debug.Log("Friendly cannons fired.", this);
        }

        SetMatchState(MatchState.Won);
        onVictory?.Invoke();
        PauseMatchIfNeeded();

        if (logMatchEvents)
        {
            Debug.Log("Match won.", this);
        }
    }

    public void TriggerDefeat(string reason)
    {
        if (currentState != MatchState.Playing)
        {
            return;
        }

        defeatReason = string.IsNullOrWhiteSpace(reason) ? "Defeat." : reason;
        SetMatchState(MatchState.Lost);
        onDefeat?.Invoke();
        PauseMatchIfNeeded();

        if (logMatchEvents)
        {
            Debug.Log("Match lost: " + defeatReason, this);
        }
    }

    private void SetMatchState(MatchState newState)
    {
        if (currentState == newState)
        {
            return;
        }

        currentState = newState;
        MatchStateChanged?.Invoke(currentState);
    }

    public void RestartMatch()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void PauseMatchIfNeeded()
    {
        if (!pauseTimeOnMatchEnd)
        {
            return;
        }

        if (matchEndPauseDelay <= 0f)
        {
            Time.timeScale = 0f;
            return;
        }

        pauseCoroutine = StartCoroutine(PauseAfterDelay(matchEndPauseDelay));
    }

    private IEnumerator PauseAfterDelay(float delaySeconds)
    {
        yield return new WaitForSecondsRealtime(delaySeconds);
        pauseCoroutine = null;
        if (currentState != MatchState.Playing)
        {
            Time.timeScale = 0f;
        }
    }
}
