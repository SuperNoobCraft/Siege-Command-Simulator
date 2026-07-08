using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// Central match flow: cannon occupation loss, commander arrow loss, and timed victory after wave 3.
/// </summary>
[DefaultExecutionOrder(100)]
public class SiegeGameManager : MonoBehaviour
{
    public enum MatchState
    {
        Playing,
        Won,
        Lost
    }

    public static SiegeGameManager Instance { get; private set; }

    [Header("Victory")]
    [Tooltip("Seconds after wave 3 begins before friendly cannons auto-fire and the match is won.")]
    [SerializeField, Min(0f)] private float secondsAfterWave3UntilCannonsFire = 50f;
    [Tooltip("Fallback if EnemyWaveController is missing.")]
    [SerializeField, Min(0f)] private float fallbackWave3StartTimeSeconds = 130f;
    [SerializeField] private UnityEvent onCannonsFired;
    [SerializeField] private UnityEvent onVictory;

    [Header("Defeat")]
    [SerializeField, Min(0.1f)] private float cannonOccupationLossSeconds = 5f;
    [SerializeField] private UnityEvent onDefeat;

    [Header("Match End")]
    [SerializeField] private bool pauseTimeOnMatchEnd = true;

    [Header("Debug")]
    [SerializeField] private bool logMatchEvents = true;

    private MatchState currentState = MatchState.Playing;
    private float matchStartTime;
    private string defeatReason;

    public MatchState CurrentState => currentState;
    public float CannonOccupationLossSeconds => cannonOccupationLossSeconds;
    public float SecondsAfterWave3UntilCannonsFire => secondsAfterWave3UntilCannonsFire;
    public string DefeatReason => defeatReason;
    public bool IsPlaying => currentState == MatchState.Playing;
    public float SecondsUntilCannonsFire => Mathf.Max(0f, GetCannonsFireTime() - Time.time);
    public float TotalSecondsUntilCannonsFire
    {
        get
        {
            float duration = GetCannonsFireTime() - matchStartTime;
            return Mathf.Max(0f, duration);
        }
    }

    public event Action<MatchState> MatchStateChanged;
    public event Action<float> CannonFireCountdownUpdated;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple SiegeGameManager instances found. Using the most recent one.", this);
        }

        Instance = this;
        matchStartTime = Time.time;
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
        EnemyWaveController.WaveDeployed += HandleWaveDeployed;
    }

    private void OnDisable()
    {
        EnemyWaveController.WaveDeployed -= HandleWaveDeployed;
    }

    private void Start()
    {
        CannonFireCountdownUpdated?.Invoke(SecondsUntilCannonsFire);
    }

    private void Update()
    {
        if (currentState != MatchState.Playing)
        {
            return;
        }

        CannonFireCountdownUpdated?.Invoke(SecondsUntilCannonsFire);

        if (SecondsUntilCannonsFire <= 0f && HasWave3Started())
        {
            FireCannonsAndWin();
        }
    }

    private void HandleWaveDeployed(int waveNumber)
    {
        if (currentState != MatchState.Playing || waveNumber != 3)
        {
            return;
        }

        if (logMatchEvents)
        {
            Debug.Log(
                "Wave 3 started. Cannons will fire in "
                + secondsAfterWave3UntilCannonsFire.ToString("F1") + "s.",
                this);
        }

        CannonFireCountdownUpdated?.Invoke(SecondsUntilCannonsFire);
    }

    private float GetWave3StartTimeSeconds()
    {
        EnemyWaveController waveController = EnemyWaveController.Instance;
        if (waveController != null)
        {
            return waveController.Wave3SpawnTimeSeconds;
        }

        return fallbackWave3StartTimeSeconds;
    }

    private float GetCannonsFireTime()
    {
        return matchStartTime + GetWave3StartTimeSeconds() + secondsAfterWave3UntilCannonsFire;
    }

    private bool HasWave3Started()
    {
        EnemyWaveController waveController = EnemyWaveController.Instance;
        return waveController == null || waveController.HasWave3Started;
    }

    public void NotifyCommanderDefeated()
    {
        TriggerDefeat("The commander was struck down by enemy arrows.");
    }

    public void NotifyCannonOccupied(SiegeCannonSite cannonSite)
    {
        string cannonName = cannonSite != null ? cannonSite.name : "Cannon";
        TriggerDefeat("Enemy forces occupied " + cannonName + " for too long.");
    }

    public void FireCannonsAndWin()
    {
        if (currentState != MatchState.Playing)
        {
            return;
        }

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
        if (pauseTimeOnMatchEnd)
        {
            Time.timeScale = 0f;
        }
    }
}
