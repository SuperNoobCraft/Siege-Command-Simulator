using System.Collections;
using UnityEngine;

/// <summary>
/// Optional Lite / Demo Day mode-select helper. Active when GameManager Lite Mode or Demo Day is on.
/// Timed (+ Endless in Lite) discs on mode select; Main Menu disc only after a match ends.
/// Demo Day shows Timed Challenge only.
/// </summary>
[DefaultExecutionOrder(50)]
[DisallowMultipleComponent]
public class SiegeLiteModeSelect : MonoBehaviour
{
    public static SiegeLiteModeSelect Instance { get; private set; }

    [Header("Stand Zones")]
    [Tooltip("Assign discs here, or use Generate Stand Circles in the Inspector.")]
    [SerializeField] private SiegeStandZone timedChallengeZone;
    [SerializeField] private SiegeStandZone endlessSurvivalZone;
    [SerializeField] private SiegeStandZone returnToMainMenuZone;
    [Tooltip("If a zone slot is empty at runtime, spawn a default disc. Prefer generating and placing them in the editor.")]
    [SerializeField] private bool autoCreateMissingZonesAtRuntime = false;

    private bool zonesListening;
    private bool zonesReady;
    private bool lastAwaitingRestart;
    private SiegeGameManager boundManager;
    private Coroutine bootRoutine;

    public static bool IsActive =>
        Instance != null
        && Instance.isActiveAndEnabled
        && SiegeGameManager.LiteModeActive;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple SiegeLiteModeSelect instances found. Using the most recent one.", this);
        }

        Instance = this;
        AutoBindZonesIfNeeded();
    }

    private void OnEnable()
    {
        if (bootRoutine != null)
        {
            StopCoroutine(bootRoutine);
        }

        bootRoutine = StartCoroutine(BootWhenReady());
    }

    private void OnDisable()
    {
        if (bootRoutine != null)
        {
            StopCoroutine(bootRoutine);
            bootRoutine = null;
        }

        UnbindManager();
        SubscribeZones(false);
        SetModeSelectZonesVisible(false);
        SetReturnToMenuZoneVisible(false);
        // Do not call MatchUi here — scene reload / destroy can crash if we touch UI while tearing down.
    }

    private void OnDestroy()
    {
        SubscribeZones(false);
        UnbindManager();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private IEnumerator BootWhenReady()
    {
        yield return null;
        yield return null;

        while (Application.isPlaying && !SiegeSceneBootstrap.IsWarmUpComplete)
        {
            yield return null;
        }

        if (!Application.isPlaying || !isActiveAndEnabled)
        {
            yield break;
        }

        TryBindManager();
        if (!SiegeGameManager.LiteModeActive)
        {
            yield break;
        }

        if (autoCreateMissingZonesAtRuntime)
        {
            EnsureDefaultZonesExist();
        }
        else
        {
            AutoBindZonesIfNeeded();
        }

        if (timedChallengeZone == null || returnToMainMenuZone == null
            || (!SiegeGameManager.DemoDayActive && endlessSurvivalZone == null))
        {
            Debug.LogWarning(
                SiegeGameManager.DemoDayActive
                    ? "SiegeLiteModeSelect: assign Timed / Main Menu stand zones, or Generate Stand Circles from the Inspector."
                    : "SiegeLiteModeSelect: assign Timed / Endless / Main Menu stand zones, "
                      + "or Generate Stand Circles from the Inspector.",
                this);
        }

        zonesReady = true;
        lastAwaitingRestart = false;
        SubscribeZones(true);
        RefreshForCurrentState();
        bootRoutine = null;
    }

    private void Update()
    {
        if (!SiegeGameManager.LiteModeActive || !zonesReady)
        {
            return;
        }

        TryBindManager();

        // Main Menu disc appears only once MatchUi is ready for press-to-return.
        bool awaiting = SiegeMatchUi.Instance != null && SiegeMatchUi.Instance.IsAwaitingRestart;
        if (awaiting != lastAwaitingRestart)
        {
            lastAwaitingRestart = awaiting;
            RefreshForCurrentState();
        }
    }

    private void TryBindManager()
    {
        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager == boundManager)
        {
            return;
        }

        UnbindManager();
        boundManager = manager;
        if (boundManager != null)
        {
            boundManager.MatchStateChanged += HandleMatchStateChanged;
        }
    }

    private void UnbindManager()
    {
        if (boundManager != null)
        {
            boundManager.MatchStateChanged -= HandleMatchStateChanged;
            boundManager = null;
        }
    }

    private void HandleMatchStateChanged(SiegeGameManager.MatchState state)
    {
        if (!zonesReady || !SiegeGameManager.LiteModeActive)
        {
            return;
        }

        RefreshForCurrentState();
    }

    private void RefreshForCurrentState()
    {
        if (!SiegeGameManager.LiteModeActive || !zonesReady)
        {
            SetModeSelectZonesVisible(false);
            SetReturnToMenuZoneVisible(false);
            return;
        }

        SiegeGameManager manager = SiegeGameManager.Instance;
        bool selecting = manager != null
            && manager.CurrentState == SiegeGameManager.MatchState.SelectingDifficulty;
        bool awaitingReturn = SiegeMatchUi.Instance != null && SiegeMatchUi.Instance.IsAwaitingRestart;

        SetModeSelectZonesVisible(selecting);
        SetReturnToMenuZoneVisible(awaitingReturn);

        if (selecting)
        {
            HideLegacyOptions();
            if (SiegeMatchUi.Instance != null)
            {
                SiegeMatchUi.Instance.ShowLiteModeSelectPrompt();
            }
        }
    }

    private void SetModeSelectZonesVisible(bool visible)
    {
        AutoBindZonesIfNeeded();
        ApplyZoneVisibility(timedChallengeZone, visible);
        // Demo Day: Timed Challenge only.
        bool showEndless = visible && !SiegeGameManager.DemoDayActive;
        ApplyZoneVisibility(endlessSurvivalZone, showEndless);
    }

    private void SetReturnToMenuZoneVisible(bool visible)
    {
        AutoBindZonesIfNeeded();
        ApplyZoneVisibility(returnToMainMenuZone, visible);
    }

    private static void ApplyZoneVisibility(SiegeStandZone zone, bool visible)
    {
        if (zone == null)
        {
            return;
        }

        zone.SetZoneActive(visible);
        if (visible)
        {
            zone.ResetDwell();
        }
    }

    private void HideLegacyOptions()
    {
        SiegeDifficultyOption[] options = FindObjectsOfType<SiegeDifficultyOption>(true);
        for (int i = 0; i < options.Length; i++)
        {
            if (options[i] != null)
            {
                options[i].SetOptionVisible(false);
            }
        }
    }

    private void SubscribeZones(bool subscribe)
    {
        if (subscribe == zonesListening)
        {
            return;
        }

        zonesListening = subscribe;
        BindZone(timedChallengeZone, subscribe);
        BindZone(endlessSurvivalZone, subscribe);
        BindZone(returnToMainMenuZone, subscribe);
    }

    private void BindZone(SiegeStandZone zone, bool subscribe)
    {
        if (zone == null)
        {
            return;
        }

        if (subscribe)
        {
            zone.Completed -= HandleZoneCompleted;
            zone.Completed += HandleZoneCompleted;
        }
        else
        {
            zone.Completed -= HandleZoneCompleted;
        }
    }

    private void HandleZoneCompleted(SiegeStandZone zone)
    {
        if (!IsActive || zone == null)
        {
            return;
        }

        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager == null)
        {
            return;
        }

        if (!SiegeSceneBootstrap.IsGameplayInputAllowed)
        {
            zone.ResetDwell();
            return;
        }

        switch (zone.ZoneAction)
        {
            case SiegeStandZone.Action.TimedChallenge:
                if (manager.CurrentState != SiegeGameManager.MatchState.SelectingDifficulty)
                {
                    zone.ResetDwell();
                    return;
                }

                manager.ConfirmPlayMode(SiegeGameMode.DodgeArrows);
                SetModeSelectZonesVisible(false);
                break;

            case SiegeStandZone.Action.EndlessSurvival:
                if (SiegeGameManager.DemoDayActive)
                {
                    zone.ResetDwell();
                    return;
                }

                if (manager.CurrentState != SiegeGameManager.MatchState.SelectingDifficulty)
                {
                    zone.ResetDwell();
                    return;
                }

                manager.ConfirmPlayMode(SiegeGameMode.DodgeArrowsEndless);
                SetModeSelectZonesVisible(false);
                break;

            case SiegeStandZone.Action.ReturnToMainMenu:
                // Same-scene title / mode select — identical to press-anything-to-return.
                if (SiegeMatchUi.Instance == null || !SiegeMatchUi.Instance.IsAwaitingRestart)
                {
                    zone.ResetDwell();
                    return;
                }

                SetReturnToMenuZoneVisible(false);
                manager.RestartMatch();
                break;
        }
    }

    private void AutoBindZonesIfNeeded()
    {
        SiegeStandZone[] zones = GetComponentsInChildren<SiegeStandZone>(true);
        for (int i = 0; i < zones.Length; i++)
        {
            SiegeStandZone zone = zones[i];
            if (zone == null)
            {
                continue;
            }

            switch (zone.ZoneAction)
            {
                case SiegeStandZone.Action.TimedChallenge:
                    if (timedChallengeZone == null)
                    {
                        timedChallengeZone = zone;
                    }

                    break;
                case SiegeStandZone.Action.EndlessSurvival:
                    if (endlessSurvivalZone == null)
                    {
                        endlessSurvivalZone = zone;
                    }

                    break;
                case SiegeStandZone.Action.ReturnToMainMenu:
                    if (returnToMainMenuZone == null)
                    {
                        returnToMainMenuZone = zone;
                    }

                    break;
            }
        }
    }

    private void EnsureDefaultZonesExist()
    {
        AutoBindZonesIfNeeded();
        if (timedChallengeZone != null && endlessSurvivalZone != null && returnToMainMenuZone != null)
        {
            return;
        }

        Vector3 origin = transform.position;
        Transform player = null;
        try
        {
            player = SiegePlayEnvironment.ResolvePlayerTransform();
        }
        catch
        {
            player = null;
        }

        if (player != null)
        {
            origin = new Vector3(player.position.x, transform.position.y, player.position.z);
        }

        if (timedChallengeZone == null)
        {
            timedChallengeZone = CreateDefaultZone(
                "LiteStand_TimedChallenge",
                SiegeStandZone.Action.TimedChallenge,
                origin + new Vector3(-1.1f, 0.02f, 1.2f),
                new Color(0.2f, 0.55f, 0.95f, 0.7f));
        }

        if (endlessSurvivalZone == null)
        {
            endlessSurvivalZone = CreateDefaultZone(
                "LiteStand_EndlessSurvival",
                SiegeStandZone.Action.EndlessSurvival,
                origin + new Vector3(1.1f, 0.02f, 1.2f),
                new Color(0.95f, 0.4f, 0.15f, 0.7f));
        }

        if (returnToMainMenuZone == null)
        {
            returnToMainMenuZone = CreateDefaultZone(
                "LiteStand_MainMenu",
                SiegeStandZone.Action.ReturnToMainMenu,
                origin + new Vector3(0f, 0.02f, -0.4f),
                new Color(0.45f, 0.45f, 0.45f, 0.7f));
        }
    }

    private SiegeStandZone CreateDefaultZone(
        string objectName,
        SiegeStandZone.Action action,
        Vector3 worldPosition,
        Color idle)
    {
        // Avoid CreatePrimitive + renderer.material (can hard-crash with some CAVE/render setups).
        GameObject disc = new GameObject(objectName);
        disc.transform.SetParent(transform, false);
        disc.transform.position = worldPosition;
        disc.transform.localScale = new Vector3(1.1f, 0.02f, 1.1f);

        MeshFilter filter = disc.AddComponent<MeshFilter>();
        filter.sharedMesh = BuildUnitCylinderMesh();

        MeshRenderer rend = disc.AddComponent<MeshRenderer>();
        rend.sharedMaterial = CreateUnlitColorMaterial(idle);

        SiegeStandZone zone = disc.AddComponent<SiegeStandZone>();
        zone.Configure(action, 0.55f, 2f, idle);
        return zone;
    }

    private static Mesh s_cylinderMesh;
    private static Material s_unlitTemplate;

    private static Mesh BuildUnitCylinderMesh()
    {
        if (s_cylinderMesh != null)
        {
            return s_cylinderMesh;
        }

        // Low-poly disc (flat cylinder).
        const int sides = 24;
        Vector3[] verts = new Vector3[sides + 1];
        int[] tris = new int[sides * 3];
        verts[0] = Vector3.zero;
        for (int i = 0; i < sides; i++)
        {
            float a = (i / (float)sides) * Mathf.PI * 2f;
            verts[i + 1] = new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f);
            tris[i * 3] = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = i + 2 <= sides ? i + 2 : 1;
        }

        s_cylinderMesh = new Mesh { name = "LiteStandDisc" };
        s_cylinderMesh.vertices = verts;
        s_cylinderMesh.triangles = tris;
        s_cylinderMesh.RecalculateNormals();
        s_cylinderMesh.RecalculateBounds();
        return s_cylinderMesh;
    }

    private static Material CreateUnlitColorMaterial(Color color)
    {
        if (s_unlitTemplate == null)
        {
            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            s_unlitTemplate = shader != null ? new Material(shader) : new Material(Shader.Find("Hidden/InternalErrorShader"));
        }

        Material mat = new Material(s_unlitTemplate);
        if (mat.HasProperty("_Color"))
        {
            mat.color = color;
        }

        return mat;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Inspector context menu (component ⋮ / right-click header): spawn three placeable stand discs.
    /// </summary>
    [ContextMenu("Generate Stand Circles")]
    public void GenerateStandCirclesContextMenu()
    {
        GenerateStandCircles(replaceExisting: false);
    }

    [ContextMenu("Generate Stand Circles (Replace Existing)")]
    public void GenerateStandCirclesReplaceContextMenu()
    {
        GenerateStandCircles(replaceExisting: true);
    }

    public void GenerateStandCircles(bool replaceExisting)
    {
        UnityEditor.Undo.IncrementCurrentGroup();
        int group = UnityEditor.Undo.GetCurrentGroup();
        UnityEditor.Undo.SetCurrentGroupName("Generate Lite Stand Circles");

        Vector3 origin = transform.position;

        if (replaceExisting)
        {
            DestroyGeneratedZone(ref timedChallengeZone);
            DestroyGeneratedZone(ref endlessSurvivalZone);
            DestroyGeneratedZone(ref returnToMainMenuZone);
        }

        if (timedChallengeZone == null)
        {
            timedChallengeZone = CreateEditorStandCircle(
                "LiteStand_TimedChallenge",
                SiegeStandZone.Action.TimedChallenge,
                origin + new Vector3(-1.1f, 0.02f, 1.2f),
                new Color(0.2f, 0.55f, 0.95f, 0.7f));
        }

        if (endlessSurvivalZone == null)
        {
            endlessSurvivalZone = CreateEditorStandCircle(
                "LiteStand_EndlessSurvival",
                SiegeStandZone.Action.EndlessSurvival,
                origin + new Vector3(1.1f, 0.02f, 1.2f),
                new Color(0.95f, 0.4f, 0.15f, 0.7f));
        }

        if (returnToMainMenuZone == null)
        {
            returnToMainMenuZone = CreateEditorStandCircle(
                "LiteStand_MainMenu",
                SiegeStandZone.Action.ReturnToMainMenu,
                origin + new Vector3(0f, 0.02f, -0.4f),
                new Color(0.45f, 0.45f, 0.45f, 0.7f));
        }

        UnityEditor.EditorUtility.SetDirty(this);
        if (!Application.isPlaying)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }

        UnityEditor.Undo.CollapseUndoOperations(group);
        Debug.Log("Lite stand circles ready — move Timed / Endless / Main Menu discs in the Scene view.", this);
    }

    private void DestroyGeneratedZone(ref SiegeStandZone zone)
    {
        if (zone == null)
        {
            return;
        }

        GameObject go = zone.gameObject;
        zone = null;
        UnityEditor.Undo.DestroyObjectImmediate(go);
    }

    private SiegeStandZone CreateEditorStandCircle(
        string objectName,
        SiegeStandZone.Action action,
        Vector3 worldPosition,
        Color idle)
    {
        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = objectName;
        UnityEditor.Undo.RegisterCreatedObjectUndo(disc, "Create " + objectName);
        disc.transform.SetParent(transform, true);
        disc.transform.position = worldPosition;
        disc.transform.localScale = new Vector3(1.2f, 0.025f, 1.2f);

        Collider col = disc.GetComponent<Collider>();
        if (col != null)
        {
            // Detection is XZ radius on SiegeStandZone; collider is only for picking in the editor.
            col.isTrigger = true;
        }

        Renderer rend = disc.GetComponent<Renderer>();
        if (rend != null)
        {
            Material mat = CreateUnlitColorMaterial(idle);
            UnityEditor.Undo.RegisterCreatedObjectUndo(mat, "Lite stand material");
            rend.sharedMaterial = mat;
        }

        SiegeStandZone zone = UnityEditor.Undo.AddComponent<SiegeStandZone>(disc);
        zone.Configure(action, 0.55f, 2f, idle);
        UnityEditor.EditorUtility.SetDirty(zone);
        return zone;
    }
#endif
}
