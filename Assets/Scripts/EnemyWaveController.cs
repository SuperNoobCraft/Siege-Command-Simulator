using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives enemy wave timing and deployment. Place enemy regiments inside the gate and assign each regiment's wave.
/// </summary>
[DefaultExecutionOrder(110)]
public class EnemyWaveController : MonoBehaviour
{
    [System.Serializable]
    private struct WaveSchedule
    {
        public int waveNumber;
        [Min(0f)] public float spawnTimeSeconds;
    }

    public static EnemyWaveController Instance { get; private set; }
    public static event System.Action<int> WaveDeployed;
    public float MatchElapsedSeconds
    {
        get
        {
            SiegeGameManager manager = SiegeGameManager.Instance;
            if (manager != null)
            {
                return manager.MatchElapsedSeconds;
            }

            return localMatchElapsedSeconds;
        }
    }
    public bool HasWave3Started => triggeredWaves.Contains(wave3.waveNumber);
    public bool HasFinalWaveStarted => triggeredWaves.Contains(FinalWaveNumber);
    public int FinalWaveNumber => SiegeMatchSettings.MaxWaves >= wave3.waveNumber ? wave3.waveNumber : wave2.waveNumber;
    public float Wave3StartTime => wave3StartTime;
    public float Wave3SpawnTimeSeconds => wave3.spawnTimeSeconds;
    public float FinalWaveSpawnTimeSeconds =>
        FinalWaveNumber == wave3.waveNumber ? wave3.spawnTimeSeconds : wave2.spawnTimeSeconds;
    public bool AreAllWavesTriggered =>
        triggeredWaves.Contains(wave1.waveNumber)
        && triggeredWaves.Contains(wave2.waveNumber)
        && (SiegeMatchSettings.MaxWaves < wave3.waveNumber || triggeredWaves.Contains(wave3.waveNumber));

    [Header("Wave Timing")]
    [SerializeField] private WaveSchedule wave1 = new WaveSchedule { waveNumber = 1, spawnTimeSeconds = 10f };
    [SerializeField] private WaveSchedule wave2 = new WaveSchedule { waveNumber = 2, spawnTimeSeconds = 70f };
    [SerializeField] private WaveSchedule wave3 = new WaveSchedule { waveNumber = 3, spawnTimeSeconds = 130f };

    [Header("Tactics")]
    [SerializeField, Min(0.1f)] private float encirclementEvaluationInterval = 1.5f;

    [Header("Debug")]
    [SerializeField] private bool logWaveEvents = true;

    private static readonly List<EnemyRegimentAI> RegisteredRegiments = new List<EnemyRegimentAI>();
    private readonly HashSet<int> triggeredWaves = new HashSet<int>();
    private float nextEncirclementEvaluationTime;
    private float localMatchElapsedSeconds;
    private int preparedPlaySessionId = -1;
    private float wave3StartTime = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
        RegisteredRegiments.Clear();
    }

    private void Awake()
    {
        RegisterInstance();
        EnsurePreparedForSession();
    }

    private void RegisterInstance()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple EnemyWaveController instances found. Using the most recent one.", this);
        }

        Instance = this;
    }

    private void OnValidate()
    {
        wave1.spawnTimeSeconds = Mathf.Max(0f, wave1.spawnTimeSeconds);
        wave2.spawnTimeSeconds = Mathf.Max(wave1.spawnTimeSeconds, wave2.spawnTimeSeconds);
        wave3.spawnTimeSeconds = Mathf.Max(wave2.spawnTimeSeconds, wave3.spawnTimeSeconds);
        wave1.waveNumber = 1;
        wave2.waveNumber = 2;
        wave3.waveNumber = 3;
    }

    public void PrepareForMatchStart()
    {
        EnsurePreparedForSession();
    }

    private void Start()
    {
        EnsurePreparedForSession();

        if (logWaveEvents)
        {
            Debug.Log(
                "EnemyWaveController ready with " + RegisteredRegiments.Count + " regiment(s). Wave 1 at "
                + wave1.spawnTimeSeconds + "s. Match elapsed "
                + MatchElapsedSeconds.ToString("F1") + "s.",
                this);
        }
    }

    private void EnsurePreparedForSession()
    {
        if (!Application.isPlaying || preparedPlaySessionId == SiegeGameManager.PlaySessionId)
        {
            return;
        }

        ResetForNewMatch();
    }

    private void ResetForNewMatch()
    {
        EnforceWaveSchedule();
        triggeredWaves.Clear();
        wave3StartTime = -1f;
        localMatchElapsedSeconds = 0f;
        RegisteredRegiments.Clear();
        RefreshRegisteredRegiments();
        preparedPlaySessionId = SiegeGameManager.PlaySessionId;
    }

    private void EnforceWaveSchedule()
    {
        wave1.spawnTimeSeconds = Mathf.Max(0f, wave1.spawnTimeSeconds);
        wave2.spawnTimeSeconds = Mathf.Max(wave1.spawnTimeSeconds, wave2.spawnTimeSeconds);
        wave3.spawnTimeSeconds = Mathf.Max(wave2.spawnTimeSeconds, wave3.spawnTimeSeconds);
        wave1.waveNumber = 1;
        wave2.waveNumber = 2;
        wave3.waveNumber = 3;
    }

    private static void RefreshRegisteredRegiments()
    {
        EnemyRegimentAI[] regiments = FindObjectsOfType<EnemyRegimentAI>(includeInactive: true);
        for (int i = 0; i < regiments.Length; i++)
        {
            Register(regiments[i]);
        }
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
            EnsurePreparedForSession();
        }
    }

    private void Update()
    {
        RegisterInstance();
        EnsurePreparedForSession();

        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager != null && !manager.IsPlaying)
        {
            return;
        }

        if (manager == null)
        {
            if (Time.timeScale <= 0f)
            {
                Time.timeScale = 1f;
            }

            localMatchElapsedSeconds += Time.unscaledDeltaTime;
        }

        EvaluateWave(wave1);
        EvaluateWave(wave2);
        if (SiegeMatchSettings.MaxWaves >= wave3.waveNumber)
        {
            EvaluateWave(wave3);
        }

        if (Time.time >= nextEncirclementEvaluationTime)
        {
            nextEncirclementEvaluationTime = Time.time + encirclementEvaluationInterval;
            EvaluateEncirclementTactics();
        }
    }

    public static void Register(EnemyRegimentAI regimentAi)
    {
        if (regimentAi == null || RegisteredRegiments.Contains(regimentAi))
        {
            return;
        }

        RegisteredRegiments.Add(regimentAi);
    }

    public static void Unregister(EnemyRegimentAI regimentAi)
    {
        if (regimentAi == null)
        {
            return;
        }

        RegisteredRegiments.Remove(regimentAi);
    }

    private void EvaluateWave(WaveSchedule schedule)
    {
        if (triggeredWaves.Contains(schedule.waveNumber))
        {
            return;
        }

        if (MatchElapsedSeconds < schedule.spawnTimeSeconds)
        {
            return;
        }

        triggeredWaves.Add(schedule.waveNumber);
        if (schedule.waveNumber == wave3.waveNumber)
        {
            wave3StartTime = Time.time;
        }

        DeployWave(schedule.waveNumber);
        WaveDeployed?.Invoke(schedule.waveNumber);
    }

    private void DeployWave(int waveNumber)
    {
        RefreshRegisteredRegiments();

        int deployedCount = 0;

        for (int i = 0; i < RegisteredRegiments.Count; i++)
        {
            EnemyRegimentAI regiment = RegisteredRegiments[i];
            if (regiment == null || !regiment.isActiveAndEnabled)
            {
                continue;
            }

            if (!regiment.CanDeployForWave(waveNumber))
            {
                continue;
            }

            regiment.DeployFromCamp();
            deployedCount++;
        }

        if (logWaveEvents)
        {
            Debug.Log(
                "Enemy wave " + waveNumber + " deployed " + deployedCount + " regiment(s) at t="
                + MatchElapsedSeconds.ToString("F1") + "s.",
                this);
        }
    }

    private void EvaluateEncirclementTactics()
    {
        List<EnemyRegimentAI> candidates = new List<EnemyRegimentAI>();
        List<TroopCombat> visibleFriendlies = new List<TroopCombat>();

        for (int i = 0; i < RegisteredRegiments.Count; i++)
        {
            EnemyRegimentAI regiment = RegisteredRegiments[i];
            if (regiment == null || !regiment.CanParticipateInEncirclement())
            {
                continue;
            }

            candidates.Add(regiment);
            AppendVisibleFriendlies(regiment, visibleFriendlies);
        }

        if (candidates.Count == 0)
        {
            return;
        }

        int requiredFriendlies = candidates[0].EncirclementMinFriendlyCount;
        int requiredEnemies = candidates[0].EncirclementMinEnemyCount;
        if (visibleFriendlies.Count < requiredFriendlies || candidates.Count < requiredEnemies)
        {
            return;
        }

        if (Random.value > candidates[0].EncirclementChance)
        {
            return;
        }

        Vector3 clusterCenter = GetClusterCenter(visibleFriendlies);
        candidates.Sort((a, b) => GetHorizontalDistanceSqr(a.transform.position, clusterCenter)
            .CompareTo(GetHorizontalDistanceSqr(b.transform.position, clusterCenter)));

        int flankCount = Mathf.Min(2, candidates.Count);
        for (int i = 0; i < flankCount; i++)
        {
            candidates[i].AssignEncirclementDestination(clusterCenter, useLeftFlank: i == 0);
        }
    }

    private static void AppendVisibleFriendlies(EnemyRegimentAI regiment, List<TroopCombat> visibleFriendlies)
    {
        Collider[] hits = Physics.OverlapSphere(
            regiment.transform.position,
            regiment.VisionRange,
            ~0,
            QueryTriggerInteraction.Ignore);

        if (hits == null)
        {
            return;
        }

        for (int i = 0; i < hits.Length; i++)
        {
            TroopCombat troop = hits[i].GetComponentInParent<TroopCombat>();
            if (troop == null
                || troop.TroopFaction != TroopCombat.Faction.Friendly
                || troop.CurrentState == TroopCombat.State.Dead
                || troop.IsRetreating)
            {
                continue;
            }

            if (!visibleFriendlies.Contains(troop))
            {
                visibleFriendlies.Add(troop);
            }
        }
    }

    private static Vector3 GetClusterCenter(List<TroopCombat> friendlies)
    {
        Vector3 center = Vector3.zero;
        int count = 0;
        for (int i = 0; i < friendlies.Count; i++)
        {
            if (friendlies[i] == null)
            {
                continue;
            }

            center += friendlies[i].transform.position;
            count++;
        }

        if (count == 0)
        {
            return Vector3.zero;
        }

        center /= count;
        return center;
    }

    private static float GetHorizontalDistanceSqr(Vector3 a, Vector3 b)
    {
        Vector3 offset = a - b;
        offset.y = 0f;
        return offset.sqrMagnitude;
    }
}
