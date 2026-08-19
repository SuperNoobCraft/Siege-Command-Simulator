using System;
using UnityEngine;
using UnityEngine.EventSystems;
using Votanic.vXR.vCast;
using Votanic.vXR.vGear;

/// <summary>
/// Local prototype controls: command one side with the existing wand/mouse path tool,
/// click a village disc you control to raise regiments, switch sides with Tab.
/// </summary>
[DefaultExecutionOrder(20)]
[DisallowMultipleComponent]
public class PointCaptureLocalInput : MonoBehaviour
{
    [SerializeField] private PointCaptureMatch match;
    [SerializeField] private PointCaptureBoard board;
    [SerializeField] private PointCaptureSpawner spawner;
    [SerializeField] private PointCaptureRaiseMenu raiseMenu;
    [SerializeField] private VotanicWandRtsCommander wandCommander;
    [SerializeField] private CaptureOwner commandFaction = CaptureOwner.Yellow;
    [SerializeField] private KeyCode switchFactionKey = KeyCode.Tab;
    [SerializeField] private KeyCode startKey = KeyCode.Space;
    [SerializeField] private KeyCode restartKey = KeyCode.R;

    [Header("Spawns")]
    [Tooltip("Yellow / player 1 start. Falls back to a scene object named YellowSpawnPoint or StartPoint.")]
    [SerializeField] private Transform yellowSpawnPoint;
    [Tooltip("Red / player 2 start. Falls back to a scene object named RedSpawnPoint.")]
    [SerializeField] private Transform redSpawnPoint;

    private float raiseFailUntil;
    private string raiseFailMessage = string.Empty;

    private bool originalWandCommanderEnabled = true;
    private float ignoreRaiseUntil;

    public void SuppressRaiseMenu(float seconds = 0.55f)
    {
        ignoreRaiseUntil = Time.unscaledTime + Mathf.Max(0.1f, seconds);
        pointerPressOnCommandableTroop = true;
    }
    private bool pointerWasHeld;
    private float pointerPressUnscaledTime;
    private bool pointerPressOnCommandableTroop;

    public CaptureOwner CommandFaction => commandFaction;
    public string LastFailMessage => Time.unscaledTime < raiseFailUntil ? raiseFailMessage : string.Empty;
    public static PointCaptureLocalInput Instance { get; private set; }

    public void Configure(
        PointCaptureMatch captureMatch,
        PointCaptureSpawner captureSpawner,
        PointCaptureRaiseMenu menu,
        VotanicWandRtsCommander commander)
    {
        match = captureMatch;
        spawner = captureSpawner;
        raiseMenu = menu;
        wandCommander = commander;
        BindRaiseMenu();
        ApplyCommandFaction();
    }

    public void SetCommandFaction(CaptureOwner owner)
    {
        commandFaction = owner;
        raiseMenu?.Hide();
        ApplyCommandFaction();
    }

    private void Awake()
    {
        Instance = this;
        if (match == null)
        {
            match = PointCaptureMatch.Instance;
        }

        if (board == null)
        {
            board = PointCaptureBoard.Instance;
        }

        if (spawner == null)
        {
            spawner = GetComponent<PointCaptureSpawner>();
        }

        if (wandCommander == null)
        {
            wandCommander = FindObjectOfType<VotanicWandRtsCommander>();
        }

        originalWandCommanderEnabled = wandCommander == null ? true : wandCommander.enabled;

        BindSpawnPoints();
        EnsureRaiseMenu();
        BindRaiseMenu();
        ApplyCommandFaction();
    }

    private void Update()
    {
        if (match == null)
        {
            match = PointCaptureMatch.Instance;
        }

        if (board == null)
        {
            board = PointCaptureBoard.Instance;
        }

        if (spawner == null)
        {
            spawner = FindObjectOfType<PointCaptureSpawner>();
        }

        EnsureRaiseMenu();
        SyncWandCommanderForRaiseMenu();

        if (match == null)
        {
            return;
        }

        TryLockNetworkFaction();

        if (match.CurrentState == PointCaptureMatch.MatchState.Waiting && WasReadyButtonPressedThisFrame())
        {
            match.NotifySideReady(commandFaction);
        }

        if (match.CurrentState == PointCaptureMatch.MatchState.Ended && WasReadyButtonPressedThisFrame())
        {
            match.NotifySideReset(commandFaction);
        }

        if (Input.GetKeyDown(switchFactionKey))
        {
            if (match.HasConnectedPeer)
            {
                ShowFail("Each cave commands its own side. Host is Yellow, client is Red.");
            }
            else
            {
                SwitchTestingSide();
            }

            return;
        }

        if (match.IsPlaying)
        {
            UpdateRaiseClick();
        }
    }

    private void UpdateRaiseClick()
    {
        bool pointerHeld = SiegeVrInput.IsPointerHeld();
        if (pointerHeld && !pointerWasHeld)
        {
            pointerPressUnscaledTime = Time.unscaledTime;
            bool menuBlocking = raiseMenu != null && (raiseMenu.IsOpen || raiseMenu.ClosedThisFrame);
            pointerPressOnCommandableTroop = menuBlocking
                || IsHoveringCommandableTroop()
                || (wandCommander != null && wandCommander.IsRecordingPath);
        }

        bool pointerReleased = !pointerHeld && pointerWasHeld;
        pointerWasHeld = pointerHeld;

        if (!pointerReleased)
        {
            return;
        }

        if (raiseMenu != null && raiseMenu.IsOpen)
        {
            return;
        }

        if (Time.unscaledTime < ignoreRaiseUntil)
        {
            return;
        }

        if (pointerPressOnCommandableTroop || (wandCommander != null && wandCommander.IsRecordingPath))
        {
            return;
        }

        const float maxClickDuration = 0.35f;
        if (Time.unscaledTime - pointerPressUnscaledTime > maxClickDuration)
        {
            return;
        }

        if (IsPointerOverUi() || (raiseMenu != null && raiseMenu.IsPointerOverMenu()))
        {
            return;
        }

        TryOpenRaiseMenu();
    }

    private void SwitchTestingSide()
    {
        commandFaction = CaptureTeams.Opposite(commandFaction);
        raiseMenu?.Hide();
        ApplyCommandFaction();
        TeleportToCommandSpawn();
    }

    private void TryLockNetworkFaction()
    {
        if (match == null || !match.HasConnectedPeer)
        {
            return;
        }

        CaptureOwner networked = match.NetworkLocalFaction;
        if (commandFaction == networked)
        {
            return;
        }

        commandFaction = networked;
        raiseMenu?.Hide();
        ApplyCommandFaction();
        TeleportToCommandSpawn();
    }

    private bool WasReadyButtonPressedThisFrame()
    {
        if (Input.GetKeyDown(switchFactionKey))
        {
            return false;
        }

        if (SiegeVrInput.WasPointerPressedThisFrame())
        {
            return true;
        }

        if (Input.GetKeyDown(startKey) || Input.GetKeyDown(restartKey))
        {
            return true;
        }

        return Input.anyKeyDown && !Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1);
    }

    private void TryOpenRaiseMenu()
    {
        EnsureRaiseMenu();
        if (raiseMenu == null || raiseMenu.IsOpen)
        {
            return;
        }

        if (match == null || !match.IsPlaying)
        {
            ShowFail("Wait for the match to start before raising regiments.");
            return;
        }

        if (spawner == null || board == null)
        {
            ShowFail("Raise setup is missing from this scene.");
            return;
        }

        if (!TryGetAimPoint(out Vector3 aimPoint))
        {
            ShowFail("Aim at the ground to raise a regiment.");
            return;
        }

        if (!board.CanRaiseAt(commandFaction, aimPoint, out string failReason))
        {
            ShowFail(failReason);
            return;
        }

        raiseMenu.Show(aimPoint, commandFaction);
    }

    private bool IsHoveringCommandableTroop()
    {
        Ray ray = BuildPointerRay();
        RaycastHit[] hits = Physics.SphereCastAll(
            ray,
            0.85f,
            1000f,
            ~0,
            QueryTriggerInteraction.Collide);

        if (hits == null || hits.Length == 0)
        {
            hits = Physics.RaycastAll(ray, 1000f, ~0, QueryTriggerInteraction.Collide);
        }

        if (hits == null || hits.Length == 0)
        {
            return false;
        }

        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null)
            {
                continue;
            }

            RtsUnitMotor unit = hitCollider.GetComponentInParent<RtsUnitMotor>();
            if (unit == null || !unit.IsCommandUnit || !unit.CanReceiveCommands)
            {
                continue;
            }

            TroopCombat combat = unit.GetComponent<TroopCombat>();
            if (combat == null)
            {
                combat = unit.GetComponentInParent<TroopCombat>();
            }

            if (combat == null)
            {
                combat = unit.GetComponentInChildren<TroopCombat>();
            }

            if (combat == null)
            {
                continue;
            }

            return combat.TroopFaction == CaptureTeams.ToTroopFaction(commandFaction);
        }

        return false;
    }

    private static bool IsPointerOverUi()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private bool TryGetAimPoint(out Vector3 point)
    {
        point = Vector3.zero;
        Ray ray = BuildPointerRay();
        int groundMask = RtsGroundUtility.DefaultGroundMask.value;
        if (groundMask != 0
            && Physics.Raycast(ray, out RaycastHit groundHit, 2000f, groundMask, QueryTriggerInteraction.Ignore))
        {
            point = groundHit.point;
            return true;
        }

        if (Physics.Raycast(ray, out RaycastHit anyHit, 2000f, ~0, QueryTriggerInteraction.Ignore))
        {
            point = anyHit.point;
            return true;
        }

        Plane ground = new Plane(Vector3.up, Vector3.zero);
        if (ground.Raycast(ray, out float enter))
        {
            point = ray.GetPoint(enter);
            return true;
        }

        return false;
    }

    private Ray BuildPointerRay()
    {
        if (wandCommander == null)
        {
            wandCommander = FindObjectOfType<VotanicWandRtsCommander>();
        }

        if (wandCommander != null)
        {
            return wandCommander.BuildGameplayRay();
        }

        Camera viewCamera = SiegePlayEnvironment.ResolveViewCamera();
        if (viewCamera != null)
        {
            return viewCamera.ScreenPointToRay(Input.mousePosition);
        }

        return new Ray(transform.position, transform.forward);
    }

    private void EnsureRaiseMenu()
    {
        if (raiseMenu == null)
        {
            raiseMenu = GetComponent<PointCaptureRaiseMenu>();
        }

        if (raiseMenu == null)
        {
            raiseMenu = FindObjectOfType<PointCaptureRaiseMenu>();
        }

        if (raiseMenu == null)
        {
            raiseMenu = gameObject.AddComponent<PointCaptureRaiseMenu>();
            raiseMenu.Configure(match, spawner);
            BindRaiseMenu();
        }

        EnsureEventSystem();
    }

    private void BindRaiseMenu()
    {
        if (raiseMenu != null)
        {
            raiseMenu.SetFailureHandler(ShowFail);
            if (match != null && spawner != null)
            {
                raiseMenu.Configure(match, spawner);
            }
        }
    }

    private void SyncWandCommanderForRaiseMenu()
    {
        bool menuOpen = raiseMenu != null && raiseMenu.IsOpen;
        VotanicWandRtsCommander[] commanders = FindObjectsOfType<VotanicWandRtsCommander>(true);
        for (int i = 0; i < commanders.Length; i++)
        {
            if (commanders[i] != null)
            {
                commanders[i].enabled = originalWandCommanderEnabled && !menuOpen;
            }
        }
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<StandaloneInputModule>();
    }

    private void ApplyCommandFaction()
    {
        TroopCombat.Faction faction = CaptureTeams.ToTroopFaction(commandFaction);
        VotanicWandRtsCommander[] commanders = FindObjectsOfType<VotanicWandRtsCommander>(true);
        for (int i = 0; i < commanders.Length; i++)
        {
            if (commanders[i] != null)
            {
                commanders[i].SetControllableFaction(faction);
            }
        }

        if (wandCommander != null)
        {
            wandCommander.SetControllableFaction(faction);
        }
    }

    private void BindSpawnPoints()
    {
        if (yellowSpawnPoint == null)
        {
            yellowSpawnPoint = FindSpawnByName("YellowSpawnPoint") ?? FindSpawnByName("StartPoint");
        }

        if (redSpawnPoint == null)
        {
            redSpawnPoint = FindSpawnByName("RedSpawnPoint");
        }
    }

    private static Transform FindSpawnByName(string objectName)
    {
        GameObject found = GameObject.Find(objectName);
        return found != null ? found.transform : null;
    }

    public Transform GetSpawnPoint(CaptureOwner owner)
    {
        BindSpawnPoints();
        return owner == CaptureOwner.Red ? redSpawnPoint : yellowSpawnPoint;
    }

    private void TeleportToCommandSpawn()
    {
        Transform destination = GetSpawnPoint(commandFaction);
        if (destination == null)
        {
            ShowFail("No " + CaptureTeams.GetDisplayName(commandFaction) + " spawn point in the scene.");
            return;
        }

        if (!TryTeleportUser(destination))
        {
            ShowFail("Could not teleport to " + CaptureTeams.GetDisplayName(commandFaction) + " spawn.");
        }
    }

    private bool TryTeleportUser(Transform destination)
    {
        if (destination == null)
        {
            return false;
        }

        try
        {
            if (vGear.user != null)
            {
                vGear.user.Transform(destination.position, destination.eulerAngles);
                return true;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Point Capture vGear.user.Transform failed: " + exception.Message, this);
        }

        try
        {
            if (vCast.user != null)
            {
                vCast.user.Transform(destination);
                return true;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Point Capture vCast.user.Transform failed: " + exception.Message, this);
        }

        Transform user = SiegePlayEnvironment.ResolveUserTransform();
        if (user != null)
        {
            user.SetPositionAndRotation(destination.position, destination.rotation);
            return true;
        }

        return false;
    }

    private void ShowFail(string message)
    {
        raiseFailMessage = message;
        raiseFailUntil = Time.unscaledTime + 2.5f;
        Debug.Log("[PointCapture] " + message, this);
    }

    private void OnGUI()
    {
        if (Time.unscaledTime >= raiseFailUntil || string.IsNullOrEmpty(raiseFailMessage))
        {
            return;
        }

        const int width = 560;
        Rect rect = new Rect((Screen.width - width) * 0.5f, 18f, width, 36f);
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, 24f), raiseFailMessage);
    }

    private void OnEnable()
    {
        BindPathCommandListeners(true);
        if (wandCommander != null)
        {
            wandCommander.enabled = originalWandCommanderEnabled;
        }
    }

    private void OnDisable()
    {
        BindPathCommandListeners(false);
        if (wandCommander != null)
        {
            wandCommander.enabled = originalWandCommanderEnabled;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void BindPathCommandListeners(bool subscribe)
    {
        VotanicWandRtsCommander[] commanders = FindObjectsOfType<VotanicWandRtsCommander>(true);
        for (int i = 0; i < commanders.Length; i++)
        {
            VotanicWandRtsCommander commander = commanders[i];
            if (commander == null)
            {
                continue;
            }

            commander.PathCommandIssued -= HandlePathCommandIssued;
            if (subscribe)
            {
                commander.PathCommandIssued += HandlePathCommandIssued;
            }
        }
    }

    private void HandlePathCommandIssued(RtsUnitMotor motor, System.Collections.Generic.IReadOnlyList<Vector3> path)
    {
        ignoreRaiseUntil = Time.unscaledTime + 0.45f;
        raiseMenu?.Hide();
    }
}
