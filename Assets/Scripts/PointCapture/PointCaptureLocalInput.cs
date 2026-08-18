using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Local prototype controls: command one side with the existing wand/mouse path tool,
/// right-click a village disc you control to raise regiments, switch sides with Tab.
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

    private float raiseFailUntil;
    private string raiseFailMessage = string.Empty;

    private bool originalWandCommanderEnabled = true;

    public CaptureOwner CommandFaction => commandFaction;
    public string LastFailMessage => Time.unscaledTime < raiseFailUntil ? raiseFailMessage : string.Empty;

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

        if (match == null)
        {
            return;
        }

        if (Input.GetKeyDown(startKey)
            && (match.CurrentState == PointCaptureMatch.MatchState.Waiting
                || match.CurrentState == PointCaptureMatch.MatchState.Ended))
        {
            raiseMenu?.Hide();
            if (match.CurrentState == PointCaptureMatch.MatchState.Ended)
            {
                match.Restart();
            }
            else
            {
                match.BeginCountdown();
            }
        }

        if (match.CurrentState == PointCaptureMatch.MatchState.Ended && Input.GetKeyDown(restartKey))
        {
            raiseMenu?.Hide();
            match.Restart();
            return;
        }

        if (Input.GetKeyDown(switchFactionKey))
        {
            commandFaction = CaptureTeams.Opposite(commandFaction);
            raiseMenu?.Hide();
            ApplyCommandFaction();
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (!IsHoveringCommandableTroop() && !IsPointerOverUi())
            {
                TryOpenRaiseMenu();
            }
        }
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

        raiseMenu.Show(aimPoint, commandFaction, Input.mousePosition);
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

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
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
        Camera viewCamera = Camera.main;
        if (viewCamera == null)
        {
            viewCamera = SiegePlayEnvironment.ResolveViewCamera();
        }

        if (viewCamera != null)
        {
            return viewCamera.ScreenPointToRay(Input.mousePosition);
        }

        if (wandCommander != null)
        {
            return wandCommander.BuildGameplayRay();
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
        if (wandCommander != null)
        {
            wandCommander.SetControllableFaction(CaptureTeams.ToTroopFaction(commandFaction));
        }
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
        if (wandCommander != null)
        {
            wandCommander.enabled = originalWandCommanderEnabled;
        }
    }

    private void OnDisable()
    {
        if (wandCommander != null)
        {
            wandCommander.enabled = originalWandCommanderEnabled;
        }
    }
}
