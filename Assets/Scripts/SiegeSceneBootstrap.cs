using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Waits for Votanic/vGear to warm up, then reloads the scene once per play session.
/// After the reload, waits again before input is allowed (see <see cref="IsWarmUpComplete"/>).
/// Auto-added to <see cref="SiegeGameManager"/>.
/// </summary>
[DefaultExecutionOrder(-250)]
public class SiegeSceneBootstrap : MonoBehaviour
{
    [Header("Bootstrap")]
    [Tooltip("Run the warm-up + one-time reload flow.")]
    [SerializeField] private bool enableBootstrap = true;
    [Tooltip("Only run in tracked CAVE/HMD. Desktop PC play skips bootstrap.")]
    [SerializeField] private bool onlyWhenTrackedXr = true;

    [Header("Warm Up")]
    [Tooltip("Seconds to wait on the first scene load before the one-time reload.")]
    [SerializeField, Min(0f)] private float warmUpSecondsBeforeReload = 3f;
    [Tooltip("Seconds to wait after the reload before wand/UI input is allowed.")]
    [SerializeField, Min(0f)] private float warmUpSecondsAfterReload = 2f;

    [Header("Debug")]
    [SerializeField] private bool logBootstrapEvents = true;

    private static bool hasReloadedThisPlaySession;
    private static bool firstLoadWarmUpStarted;
    private static bool postReloadWarmUpStarted;
    private static GameObject warmUpRunner;

    public static bool IsWarmUpComplete { get; private set; } = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPlaySessionState()
    {
        hasReloadedThisPlaySession = false;
        firstLoadWarmUpStarted = false;
        postReloadWarmUpStarted = false;
        IsWarmUpComplete = true;
        DestroyWarmUpRunner();
        SiegeMatchSettings.Reset();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void Start()
    {
        RequestBootstrap(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RequestBootstrap(scene, mode);
    }

    private void RequestBootstrap(Scene scene, LoadSceneMode mode)
    {
        if (!Application.isPlaying || mode != LoadSceneMode.Single || !scene.IsValid())
        {
            return;
        }

        SiegeSceneBootstrap bootstrap = FindBootstrapInstance();
        if (bootstrap == null || !bootstrap.ShouldRunBootstrap())
        {
            IsWarmUpComplete = true;
            bootstrap?.Log("Bootstrap skipped for scene '" + scene.name + "'.");
            return;
        }

        EnsureWarmUpRunner();
        SiegeSceneBootstrapRunner runner = warmUpRunner.GetComponent<SiegeSceneBootstrapRunner>();

        if (!hasReloadedThisPlaySession)
        {
            if (firstLoadWarmUpStarted)
            {
                return;
            }

            firstLoadWarmUpStarted = true;
            IsWarmUpComplete = false;
            bootstrap.Log(
                "First load warm-up started (" + bootstrap.warmUpSecondsBeforeReload.ToString("F1")
                + "s) before one-time reload.");
            runner.Begin(bootstrap.StartFirstLoadWarmUp(scene.buildIndex));
            return;
        }

        if (postReloadWarmUpStarted)
        {
            return;
        }

        postReloadWarmUpStarted = true;
        IsWarmUpComplete = false;
        bootstrap.Log(
            "Post-reload warm-up started (" + bootstrap.warmUpSecondsAfterReload.ToString("F1")
            + "s) before input unlock.");
        runner.Begin(bootstrap.StartPostReloadWarmUp());
    }

    private bool ShouldRunBootstrap()
    {
        if (!enableBootstrap)
        {
            return false;
        }

        return !onlyWhenTrackedXr || SiegePlayEnvironment.IsTrackedXr;
    }

    private IEnumerator StartFirstLoadWarmUp(int sceneBuildIndex)
    {
        if (warmUpSecondsBeforeReload > 0f)
        {
            yield return new WaitForSecondsRealtime(warmUpSecondsBeforeReload);
        }

        hasReloadedThisPlaySession = true;
        Log("Reloading scene (build index " + sceneBuildIndex + ") after warm-up.");
        SceneManager.LoadScene(sceneBuildIndex);
    }

    private IEnumerator StartPostReloadWarmUp()
    {
        if (warmUpSecondsAfterReload > 0f)
        {
            yield return new WaitForSecondsRealtime(warmUpSecondsAfterReload);
        }

        IsWarmUpComplete = true;
        postReloadWarmUpStarted = false;
        Log("Warm-up complete. Input unlocked.");
    }

    private void Log(string message)
    {
        if (!logBootstrapEvents)
        {
            return;
        }

        Debug.Log("[SiegeSceneBootstrap] " + message, this);
    }

    private static SiegeSceneBootstrap FindBootstrapInstance()
    {
        SiegeSceneBootstrap[] bootstraps = FindObjectsOfType<SiegeSceneBootstrap>(true);
        for (int i = 0; i < bootstraps.Length; i++)
        {
            if (bootstraps[i] != null && bootstraps[i].enableBootstrap)
            {
                return bootstraps[i];
            }
        }

        return bootstraps.Length > 0 ? bootstraps[0] : null;
    }

    private static void EnsureWarmUpRunner()
    {
        if (warmUpRunner != null)
        {
            return;
        }

        warmUpRunner = new GameObject(nameof(SiegeSceneBootstrapRunner));
        warmUpRunner.AddComponent<SiegeSceneBootstrapRunner>();
        warmUpRunner.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(warmUpRunner);
    }

    private static void DestroyWarmUpRunner()
    {
        if (warmUpRunner == null)
        {
            return;
        }

        Object.Destroy(warmUpRunner);
        warmUpRunner = null;
    }

    private sealed class SiegeSceneBootstrapRunner : MonoBehaviour
    {
        private Coroutine activeRoutine;

        public void Begin(IEnumerator routine)
        {
            if (activeRoutine != null)
            {
                StopCoroutine(activeRoutine);
            }

            activeRoutine = StartCoroutine(routine);
        }
    }
}
