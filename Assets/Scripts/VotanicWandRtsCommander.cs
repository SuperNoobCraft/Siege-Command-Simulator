using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Votanic.vXR.vCast;
using Votanic.vXR.vGear;

public class VotanicWandRtsCommander : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private string issueCommandName = "Grab";
    [Tooltip("When enabled, left mouse issues commands while testing on desktop. Ignored in CAVE/HMD modes.")]
    [SerializeField] private bool enableDesktopFallback = true;
    [Tooltip("Use Camera.main mouse ray on desktop instead of a transform forward ray.")]
    [SerializeField] private bool useDesktopMouseRay = true;
    [SerializeField] private KeyCode desktopKeyA = KeyCode.Mouse0;
    [SerializeField] private KeyCode desktopKeyB = KeyCode.None;
    [SerializeField] private Transform wandOrigin;
    [SerializeField] private float maxRayDistance = 1000f;

    [Header("Wand Pointer (CAVE/HMD)")]
    [SerializeField] private bool showWandPointerInTrackedXr = true;
    [SerializeField] private bool enableVotanicSdkWandRay = true;
    [Tooltip("Custom LineRenderer beam length. Uses Max Ray Distance when zero.")]
    [SerializeField, Min(0f)] private float wandPointerLength = 0f;
    [Tooltip("Pushed to Votanic SDK each refresh. Config defaults are ~2m in Setting.vxrs and override SetMaxLength.")]
    [SerializeField, Min(1f)] private float votanicSdkWandRayLength = 120f;
    [SerializeField, Min(0.05f)] private float votanicWandRayRefreshInterval = 0.25f;
    [SerializeField, Min(0.5f)] private float votanicWandRayInitRetryDuration = 4f;
    [SerializeField, Min(0.01f)] private float wandPointerWidth = 0.035f;
    [SerializeField] private Color wandPointerColor = new Color(0.2f, 0.85f, 1f, 0.95f);
    [SerializeField] private Color wandPointerHitColor = new Color(0.2f, 1f, 0.35f, 0.95f);
    [Tooltip("Keep the visible beam at full length. Hits only change color, they do not shorten the beam.")]
    [SerializeField] private bool wandPointerAlwaysFullLength = true;
    [SerializeField] private LayerMask wandPointerIgnoreLayers;

    [Header("Raycast Layers")]
    [SerializeField] private LayerMask selectableLayers = ~0;
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private Collider groundCollider;

    [Header("Path Drawing")]
    [SerializeField, Min(0.05f)] private float pathSampleMinDistance = 0.4f;
    [SerializeField, Min(0.05f)] private float pathInitialSampleMinDistance = 0.75f;
    [SerializeField, Min(0f)] private float pathRecordingGraceDuration = 0.12f;
    [SerializeField, Min(0f)] private float pathStartJitterRadius = 1.25f;
    [SerializeField, Min(0f)] private float pathSimplifyEpsilon = 0.75f;
    [SerializeField, Range(0, 4)] private int pathSmoothIterations = 1;
    [SerializeField, Min(0.25f)] private float pathMaxWaypointSpacing = 2f;
    [SerializeField, Min(0.1f)] private float pathMinIssueLength = 0.75f;

    [Header("Tracked XR Path Tuning")]
    [Tooltip("Applied automatically in CAVE/HMD. Higher values ignore controller noise better.")]
    [SerializeField, Min(0.05f)] private float trackedPathSampleMinDistance = 0.85f;
    [SerializeField, Min(0.05f)] private float trackedPathInitialSampleMinDistance = 1.1f;
    [SerializeField, Min(0f)] private float trackedPathSimplifyEpsilon = 1.1f;
    [SerializeField, Range(0, 4)] private int trackedPathSmoothIterations = 2;
    [SerializeField, Min(0.25f)] private float trackedPathMaxWaypointSpacing = 2.75f;
    [SerializeField, Range(1, 8)] private int trackedPathStabilizeWindow = 5;
    [SerializeField, Range(0.02f, 1f)] private float trackedAimSmoothingStrength = 0.18f;
    [SerializeField, Min(0f)] private float trackedPathStartJitterRadius = 0.6f;

    [Header("Path Preview")]
    [SerializeField] private Color previewPathColor = Color.green;
    [SerializeField, Min(0.01f)] private float previewLineWidth = 0.12f;
    [SerializeField, Min(0.1f)] private float previewArrowHeadLength = 0.75f;
    [SerializeField, Min(5f)] private float previewArrowHeadAngle = 28f;
    [SerializeField, Min(0.05f)] private float previewGroundOffset = 0.08f;

    [Header("Debug")]
    [SerializeField] private bool drawDebugRay;
    [SerializeField] private bool verboseDebugLogs;
    [SerializeField] private bool showDebugOverlay;

    private RtsUnitMotor hoveredUnit;
    private RtsUnitMotor commandingUnit;
    private RtsUnitMotor lastHoverHighlightedUnit;
    private RtsUnitMotor lastCommandHighlightedUnit;
    private RtsMovementPathDisplay hoveredPathDisplay;
    private readonly List<Vector3> recordedPathPoints = new List<Vector3>();
    private readonly List<Vector3> previewPathPoints = new List<Vector3>();
    private readonly List<Vector3> smoothedPathScratch = new List<Vector3>();
    private LineRenderer previewPathLine;
    private LineRenderer wandPointerLine;
    private bool isRecordingPath;
    private bool wasCommandHeld;
    private bool vrCommandLatched;
    private float pathRecordingStartTime;
    private string debugStatusLine = "Ready";
    private string debugHoverLine = "Hover: none";
    private string debugPathLine = "Path: none";
    private Vector3 smoothedAimPoint;
    private bool hasSmoothedAimPoint;
    private float nextVotanicWandRayRefreshTime;
    private Coroutine votanicWandRayInitCoroutine;
    private bool votanicWandRayConfigured;

    private void Awake()
    {
        EnsurePreviewPathLine();
        EnsureWandPointerLine();
    }

    private void OnEnable()
    {
        SiegePlayEnvironment.EnvironmentChanged += HandlePlayEnvironmentChanged;
        BeginVotanicWandRayInitialization();
    }

    private void OnDisable()
    {
        SiegePlayEnvironment.EnvironmentChanged -= HandlePlayEnvironmentChanged;
        if (votanicWandRayInitCoroutine != null)
        {
            StopCoroutine(votanicWandRayInitCoroutine);
            votanicWandRayInitCoroutine = null;
        }
    }

    private void Start()
    {
        BeginVotanicWandRayInitialization();
    }

    private void HandlePlayEnvironmentChanged()
    {
        votanicWandRayConfigured = false;
        if (votanicWandRayInitCoroutine != null)
        {
            StopCoroutine(votanicWandRayInitCoroutine);
            votanicWandRayInitCoroutine = null;
        }

        BeginVotanicWandRayInitialization();
    }

    private void BeginVotanicWandRayInitialization()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        if (votanicWandRayInitCoroutine == null)
        {
            votanicWandRayInitCoroutine = StartCoroutine(InitializeVotanicWandRayWhenReady());
        }
    }

    private void Update()
    {
        if (commandingUnit != null && !commandingUnit.CanReceiveCommands)
        {
            CancelCommandMode();
        }

        Ray ray = BuildWandRay();
        UpdateHoveredUnit(ray);
        UpdateWandPointer(ray);

        if (drawDebugRay)
        {
            Debug.DrawRay(ray.origin, ray.direction * maxRayDistance, Color.cyan, 1.2f);
        }

        bool commandHeld = IsCommandHeld();
        bool commandPressedThisFrame = commandHeld && !wasCommandHeld;
        bool commandReleasedThisFrame = !commandHeld && wasCommandHeld;

        if (commandPressedThisFrame && !isRecordingPath && hoveredUnit != null)
        {
            BeginCommandMode(hoveredUnit);
        }

        if (isRecordingPath)
        {
            if (!commandHeld || commandingUnit == null || !commandingUnit.CanReceiveCommands)
            {
                if (commandReleasedThisFrame)
                {
                    CompleteCommandMode();
                }
                else
                {
                    CancelCommandMode();
                }
            }
            else
            {
                TryRecordPathPoint(ray);
                UpdatePreviewPath();
            }
        }

        UpdateHighlights();
        wasCommandHeld = commandHeld;

        if (SiegePlayEnvironment.IsTrackedXr && Time.time >= nextVotanicWandRayRefreshTime)
        {
            ConfigureTrackedWandPointer();
            nextVotanicWandRayRefreshTime = Time.time + votanicWandRayRefreshInterval;
        }
    }

    private void BeginCommandMode(RtsUnitMotor unit)
    {
        if (unit == null || !unit.CanReceiveCommands)
        {
            return;
        }

        if (unit.HasActivePath || unit.IsBlockedBySolidObstacle || unit.IsStuck)
        {
            unit.Stop();
        }

        commandingUnit = unit;
        isRecordingPath = true;
        pathRecordingStartTime = Time.time;
        recordedPathPoints.Clear();
        smoothedAimPoint = Vector3.zero;
        hasSmoothedAimPoint = false;
        recordedPathPoints.Add(GetGroundedUnitPosition(unit));

        SetPathHoverVisible(hoveredPathDisplay, false);
        debugStatusLine = "Commanding: " + unit.name;
        debugPathLine = "Path: recording";
    }

    private void CompleteCommandMode()
    {
        RtsUnitMotor unit = commandingUnit;
        List<Vector3> issuedPath = BuildSmoothedCommandPath();

        isRecordingPath = false;
        commandingUnit = null;
        recordedPathPoints.Clear();
        SetPreviewVisible(false);

        if (unit != null && issuedPath.Count >= 2 && RtsPathUtility.GetPathLength(issuedPath) >= pathMinIssueLength)
        {
            unit.FollowPath(issuedPath);
            debugStatusLine = "Issued path to " + unit.name;
            debugPathLine = "Path: issued (" + issuedPath.Count + " waypoints)";
        }
        else
        {
            debugStatusLine = "Command cancelled";
            debugPathLine = "Path: too short";
        }
    }

    private void CancelCommandMode()
    {
        isRecordingPath = false;
        commandingUnit = null;
        recordedPathPoints.Clear();
        SetPreviewVisible(false);
        debugPathLine = "Path: cancelled";
    }

    private List<Vector3> BuildSmoothedCommandPath()
    {
        if (recordedPathPoints.Count == 0)
        {
            return smoothedPathScratch;
        }

        float groundY = recordedPathPoints[0].y;
        smoothedPathScratch.Clear();
        if (UsesTrackedPathTuning())
        {
            smoothedPathScratch.AddRange(RtsPathUtility.BuildTrackedCommandPath(
                recordedPathPoints,
                groundY,
                trackedPathSampleMinDistance,
                trackedPathSimplifyEpsilon,
                trackedPathSmoothIterations,
                trackedPathMaxWaypointSpacing,
                trackedPathStabilizeWindow));
        }
        else
        {
            smoothedPathScratch.AddRange(RtsPathUtility.BuildCommandPath(
                recordedPathPoints,
                groundY,
                pathSampleMinDistance,
                pathSimplifyEpsilon,
                pathSmoothIterations,
                pathMaxWaypointSpacing));
        }

        float jitterRadius = UsesTrackedPathTuning() ? trackedPathStartJitterRadius : pathStartJitterRadius;
        if (jitterRadius > 0f && smoothedPathScratch.Count >= 2)
        {
            List<Vector3> trimmed = RtsPathUtility.TrimLeadingStartJitter(
                smoothedPathScratch,
                recordedPathPoints[0],
                jitterRadius);
            smoothedPathScratch.Clear();
            smoothedPathScratch.AddRange(trimmed);
        }

        return smoothedPathScratch;
    }

    private void TryRecordPathPoint(Ray ray)
    {
        if (Time.time - pathRecordingStartTime < pathRecordingGraceDuration)
        {
            return;
        }

        if (!TryGetSmoothedBattlefieldPoint(ray, out Vector3 battlefieldPoint))
        {
            return;
        }

        if (recordedPathPoints.Count == 0)
        {
            recordedPathPoints.Add(battlefieldPoint);
            return;
        }

        Vector3 lastPoint = recordedPathPoints[recordedPathPoints.Count - 1];
        Vector3 offset = battlefieldPoint - lastPoint;
        offset.y = 0f;

        float minDistance = recordedPathPoints.Count == 1
            ? GetEffectiveInitialSampleDistance()
            : GetEffectiveSampleDistance();
        if (offset.sqrMagnitude < minDistance * minDistance)
        {
            return;
        }

        recordedPathPoints.Add(battlefieldPoint);
    }

    private void UpdatePreviewPath()
    {
        if (commandingUnit == null)
        {
            SetPreviewVisible(false);
            return;
        }

        previewPathPoints.Clear();
        previewPathPoints.AddRange(recordedPathPoints);
        if (previewPathPoints.Count == 0)
        {
            previewPathPoints.Add(GetGroundedUnitPosition(commandingUnit));
        }

        if (TryGetSmoothedBattlefieldPoint(BuildWandRay(), out Vector3 livePoint))
        {
            Vector3 lastPoint = previewPathPoints[previewPathPoints.Count - 1];
            Vector3 offset = livePoint - lastPoint;
            offset.y = 0f;
            if (offset.sqrMagnitude >= GetEffectiveSampleDistance() * GetEffectiveSampleDistance() * 0.25f)
            {
                previewPathPoints.Add(livePoint);
            }
        }

        if (previewPathPoints.Count < 2)
        {
            SetPreviewVisible(false);
            return;
        }

        List<Vector3> smoothedPreview = UsesTrackedPathTuning()
            ? RtsPathUtility.BuildTrackedCommandPath(
                previewPathPoints,
                previewPathPoints[0].y,
                trackedPathSampleMinDistance,
                trackedPathSimplifyEpsilon,
                trackedPathSmoothIterations,
                trackedPathMaxWaypointSpacing,
                trackedPathStabilizeWindow)
            : RtsPathUtility.BuildCommandPath(
                previewPathPoints,
                previewPathPoints[0].y,
                pathSampleMinDistance,
                pathSimplifyEpsilon,
                pathSmoothIterations,
                pathMaxWaypointSpacing);

        UpdatePreviewLine(smoothedPreview);
    }

    private void UpdateHoveredUnit(Ray ray)
    {
        RtsMovementPathDisplay previousDisplay = hoveredPathDisplay;
        if (previousDisplay != null)
        {
            previousDisplay.SetHoverVisible(false);
        }

        if (!TryRaycastGameplayHit(ray, maxRayDistance, selectableLayers, out RaycastHit hit))
        {
            hoveredUnit = null;
            hoveredPathDisplay = null;
            debugHoverLine = "Hover: none";
            return;
        }

        RtsUnitMotor unit = hit.collider.GetComponentInParent<RtsUnitMotor>();
        bool isCommandUnit = unit != null && unit.IsCommandUnit && unit.CanReceiveCommands;
        debugHoverLine = "Hover hit: " + hit.collider.name + " | unit=" + (unit != null ? unit.name : "none") + " | commandable=" + isCommandUnit;

        if (verboseDebugLogs)
        {
            Debug.Log(debugHoverLine);
        }

        hoveredUnit = isCommandUnit ? unit : null;
        hoveredPathDisplay = hoveredUnit != null ? GetPathDisplay(hoveredUnit) : null;
        if (hoveredPathDisplay != null && !isRecordingPath)
        {
            hoveredPathDisplay.SetHoverVisible(true);
        }
    }

    private void UpdateHighlights()
    {
        if (lastHoverHighlightedUnit != null && lastHoverHighlightedUnit != hoveredUnit)
        {
            SetHighlightState(lastHoverHighlightedUnit, isHovered: false, isSelected: false);
        }

        if (lastCommandHighlightedUnit != null && lastCommandHighlightedUnit != commandingUnit)
        {
            SetHighlightState(lastCommandHighlightedUnit, isHovered: false, isSelected: false);
        }

        bool showHover = hoveredUnit != null && hoveredUnit != commandingUnit && !isRecordingPath;
        SetHighlightState(hoveredUnit, isHovered: showHover, isSelected: false);
        SetHighlightState(commandingUnit, isHovered: false, isSelected: isRecordingPath);

        lastHoverHighlightedUnit = showHover ? hoveredUnit : null;
        lastCommandHighlightedUnit = isRecordingPath ? commandingUnit : null;
    }

    private bool IsCommandHeld()
    {
        if (ShouldUseDesktopFallback())
        {
            if (desktopKeyA != KeyCode.None && Input.GetKey(desktopKeyA))
            {
                vrCommandLatched = false;
                return true;
            }

            if (desktopKeyB != KeyCode.None && Input.GetKey(desktopKeyB))
            {
                vrCommandLatched = false;
                return true;
            }
        }

        float commandValue = vGear.Cmd.Value(issueCommandName);
        if (vGear.Cmd.Received(issueCommandName) || commandValue > 0.5f)
        {
            vrCommandLatched = true;
        }
        else if (vrCommandLatched && commandValue <= 0.01f)
        {
            vrCommandLatched = false;
        }

        return vrCommandLatched;
    }

    private Ray BuildWandRay()
    {
        if (ShouldUseDesktopFallback() && useDesktopMouseRay)
        {
            UnityEngine.Camera viewCamera = SiegePlayEnvironment.ResolveViewCamera();
            if (viewCamera != null)
            {
                return viewCamera.ScreenPointToRay(Input.mousePosition);
            }
        }

        Transform source = wandOrigin;

        if (source == null && vGear.controller != null)
        {
            source = vGear.controller.transform;
        }

        if (source == null && vGear.head != null)
        {
            source = vGear.head.transform;
        }

        if (source == null)
        {
            source = transform;
        }

        return new Ray(source.position, source.forward);
    }

    private bool ShouldUseDesktopFallback()
    {
        return enableDesktopFallback && SiegePlayEnvironment.IsDesktopInput;
    }

    private bool TryGetBattlefieldPoint(Ray ray, out Vector3 point)
    {
        point = default;

        if (groundCollider != null)
        {
            if (!groundCollider.Raycast(ray, out RaycastHit hit, maxRayDistance))
            {
                debugPathLine = "Path: no ground hit";
                return false;
            }

            if (ShouldIgnoreRaycastCollider(hit.collider))
            {
                if (!TryRaycastGameplayHit(ray, maxRayDistance, groundLayers, out RaycastHit gameplayHit))
                {
                    debugPathLine = "Path: no ground hit";
                    return false;
                }

                point = gameplayHit.point;
                return true;
            }

            point = hit.point;
            return true;
        }

        if (!TryRaycastGameplayHit(ray, maxRayDistance, groundLayers, out RaycastHit groundHit))
        {
            debugPathLine = "Path: no ground hit";
            return false;
        }

        point = groundHit.point;
        return true;
    }

    private bool TryRaycastGameplayHit(Ray ray, float maxDistance, LayerMask layers, out RaycastHit hit)
    {
        hit = default;
        RaycastHit[] hits = Physics.RaycastAll(ray, maxDistance, layers, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
        {
            return false;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            if (ShouldIgnoreRaycastCollider(hits[i].collider))
            {
                continue;
            }

            hit = hits[i];
            return true;
        }

        return false;
    }

    private bool ShouldIgnoreRaycastCollider(Collider collider)
    {
        if (collider == null)
        {
            return true;
        }

        if (SiegePlayerBoundary.IsBoundaryCollider(collider))
        {
            return true;
        }

        return wandPointerIgnoreLayers != 0
            && ((wandPointerIgnoreLayers.value & (1 << collider.gameObject.layer)) != 0);
    }

    private static Vector3 GetGroundedUnitPosition(RtsUnitMotor unit)
    {
        Vector3 position = unit.transform.position;
        return new Vector3(position.x, position.y, position.z);
    }

    private static RtsMovementPathDisplay GetPathDisplay(RtsUnitMotor unit)
    {
        if (unit == null)
        {
            return null;
        }

        RtsMovementPathDisplay display = unit.GetComponent<RtsMovementPathDisplay>();
        if (display == null)
        {
            display = unit.gameObject.AddComponent<RtsMovementPathDisplay>();
        }

        return display;
    }

    private static void SetPathHoverVisible(RtsMovementPathDisplay display, bool visible)
    {
        if (display != null)
        {
            display.SetHoverVisible(visible);
        }
    }

    private static void SetHighlightState(RtsUnitMotor unit, bool isHovered, bool isSelected)
    {
        if (unit == null)
        {
            return;
        }

        RtsUnitHighlight highlight = unit.GetComponent<RtsUnitHighlight>();
        if (highlight == null)
        {
            return;
        }

        highlight.SetHovered(isHovered);
        highlight.SetSelected(isSelected);
    }

    private void EnsurePreviewPathLine()
    {
        if (previewPathLine != null)
        {
            return;
        }

        GameObject lineObject = new GameObject("CommandPathPreview");
        lineObject.transform.SetParent(transform, false);
        int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        lineObject.layer = ignoreRaycastLayer >= 0 ? ignoreRaycastLayer : gameObject.layer;

        previewPathLine = lineObject.AddComponent<LineRenderer>();
        previewPathLine.useWorldSpace = true;
        previewPathLine.loop = false;
        previewPathLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        previewPathLine.receiveShadows = false;
        previewPathLine.allowOcclusionWhenDynamic = false;
        previewPathLine.textureMode = LineTextureMode.Stretch;
        previewPathLine.alignment = LineAlignment.View;
        previewPathLine.numCornerVertices = 4;
        previewPathLine.numCapVertices = 4;
        previewPathLine.widthMultiplier = previewLineWidth;
        previewPathLine.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        previewPathLine.enabled = false;
    }

    private void UpdatePreviewLine(IReadOnlyList<Vector3> points)
    {
        EnsurePreviewPathLine();
        List<Vector3> renderedPoints = new List<Vector3>(points.Count + 4);

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 point = points[i];
            point.y += previewGroundOffset;
            renderedPoints.Add(point);
        }

        AppendArrowHead(renderedPoints, previewArrowHeadLength, previewArrowHeadAngle);
        previewPathLine.positionCount = renderedPoints.Count;
        previewPathLine.widthMultiplier = previewLineWidth;
        previewPathLine.startColor = previewPathColor;
        previewPathLine.endColor = previewPathColor;
        previewPathLine.SetPositions(renderedPoints.ToArray());
        previewPathLine.enabled = true;
    }

    private void SetPreviewVisible(bool visible)
    {
        if (previewPathLine != null)
        {
            previewPathLine.enabled = visible;
        }
    }

    private IEnumerator InitializeVotanicWandRayWhenReady()
    {
        float deadline = Time.time + votanicWandRayInitRetryDuration;
        while (Time.time < deadline)
        {
            if (!SiegePlayEnvironment.IsTrackedXr)
            {
                SetWandPointerVisible(false);
                yield return null;
                continue;
            }

            if (ConfigureTrackedWandPointer())
            {
                break;
            }

            yield return null;
        }

        votanicWandRayInitCoroutine = null;
    }

    private bool ConfigureTrackedWandPointer()
    {
        if (!SiegePlayEnvironment.IsTrackedXr)
        {
            SetWandPointerVisible(false);
            return false;
        }

        if (!enableVotanicSdkWandRay)
        {
            return true;
        }

        try
        {
            if (vCast.controller != null)
            {
                vCast.controller.SetTool("Wand");
                vCast.controller.EnableWandRay(true);
                vCast.controller.DisplayWandRay(true);
                vCast.controller.SetMaxLength(GetVotanicSdkWandRayLength());
                ApplyVotanicSdkIgnoreLayers();
                votanicWandRayConfigured = true;
                return true;
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("Could not enable Votanic SDK wand ray: " + exception.Message, this);
        }

        return false;
    }

    private float GetEffectiveWandPointerLength()
    {
        return wandPointerLength > 0f ? wandPointerLength : maxRayDistance;
    }

    private void ApplyVotanicSdkIgnoreLayers()
    {
        System.Collections.Generic.List<int> ignoreLayers = new System.Collections.Generic.List<int>();
        if (wandPointerIgnoreLayers.value != 0)
        {
            for (int layer = 0; layer < 32; layer++)
            {
                if ((wandPointerIgnoreLayers.value & (1 << layer)) != 0)
                {
                    ignoreLayers.Add(layer);
                }
            }
        }

        SiegePlayerBoundary[] boundaries = FindObjectsOfType<SiegePlayerBoundary>();
        for (int i = 0; i < boundaries.Length; i++)
        {
            SiegePlayerBoundary boundary = boundaries[i];
            if (boundary == null)
            {
                continue;
            }

            Collider boundaryCollider = boundary.GetComponent<Collider>();
            if (boundaryCollider != null)
            {
                int layer = boundaryCollider.gameObject.layer;
                if (!ignoreLayers.Contains(layer))
                {
                    ignoreLayers.Add(layer);
                }
            }
        }

        if (ignoreLayers.Count > 0)
        {
            vCast.controller.SetIgnoreLayers(ignoreLayers.ToArray());
        }
    }

    private float GetVotanicSdkWandRayLength()
    {
        return Mathf.Max(votanicSdkWandRayLength, GetEffectiveWandPointerLength());
    }

    private void EnsureWandPointerLine()
    {
        if (wandPointerLine != null)
        {
            return;
        }

        GameObject lineObject = new GameObject("WandPointer");
        lineObject.transform.SetParent(transform, false);
        int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        lineObject.layer = ignoreRaycastLayer >= 0 ? ignoreRaycastLayer : gameObject.layer;

        wandPointerLine = lineObject.AddComponent<LineRenderer>();
        wandPointerLine.useWorldSpace = true;
        wandPointerLine.loop = false;
        wandPointerLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        wandPointerLine.receiveShadows = false;
        wandPointerLine.allowOcclusionWhenDynamic = false;
        wandPointerLine.textureMode = LineTextureMode.Stretch;
        wandPointerLine.alignment = LineAlignment.View;
        wandPointerLine.numCornerVertices = 2;
        wandPointerLine.numCapVertices = 2;
        wandPointerLine.widthMultiplier = wandPointerWidth;
        wandPointerLine.positionCount = 2;
        wandPointerLine.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        wandPointerLine.enabled = false;
    }

    private void UpdateWandPointer(Ray ray)
    {
        if (!showWandPointerInTrackedXr || SiegePlayEnvironment.IsDesktopInput)
        {
            SetWandPointerVisible(false);
            return;
        }

        EnsureWandPointerLine();
        float pointerLength = GetEffectiveWandPointerLength();
        Vector3 fullEndPoint = ray.origin + ray.direction * pointerLength;
        bool hasHit = TryRaycastForPointer(ray, pointerLength, out RaycastHit hit);
        Vector3 endPoint = wandPointerAlwaysFullLength || !hasHit ? fullEndPoint : hit.point;

        wandPointerLine.positionCount = hasHit && wandPointerAlwaysFullLength ? 3 : 2;
        wandPointerLine.SetPosition(0, ray.origin);
        if (hasHit && wandPointerAlwaysFullLength)
        {
            wandPointerLine.SetPosition(1, hit.point);
            wandPointerLine.SetPosition(2, fullEndPoint);
            wandPointerLine.startColor = wandPointerColor;
            wandPointerLine.endColor = wandPointerColor;
            wandPointerLine.colorGradient = BuildPointerGradient(wandPointerHitColor, wandPointerColor, hit.distance / pointerLength);
        }
        else
        {
            wandPointerLine.SetPosition(1, endPoint);
            wandPointerLine.startColor = hasHit ? wandPointerHitColor : wandPointerColor;
            wandPointerLine.endColor = wandPointerLine.startColor;
            wandPointerLine.colorGradient = DefaultPointerGradient(hasHit ? wandPointerHitColor : wandPointerColor);
        }

        wandPointerLine.widthMultiplier = wandPointerWidth;
        wandPointerLine.enabled = true;
    }

    private static Gradient DefaultPointerGradient(Color color)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(color, 0f),
                new GradientColorKey(color, 1f)
            },
            new[]
            {
                new GradientAlphaKey(color.a, 0f),
                new GradientAlphaKey(color.a, 1f)
            });
        return gradient;
    }

    private static Gradient BuildPointerGradient(Color hitColor, Color tailColor, float hitFraction)
    {
        float clampedHit = Mathf.Clamp01(hitFraction);
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(hitColor, 0f),
                new GradientColorKey(hitColor, clampedHit),
                new GradientColorKey(tailColor, clampedHit),
                new GradientColorKey(tailColor, 1f)
            },
            new[]
            {
                new GradientAlphaKey(hitColor.a, 0f),
                new GradientAlphaKey(hitColor.a, clampedHit),
                new GradientAlphaKey(tailColor.a, clampedHit),
                new GradientAlphaKey(tailColor.a, 1f)
            });
        return gradient;
    }

    private void SetWandPointerVisible(bool visible)
    {
        if (wandPointerLine != null)
        {
            wandPointerLine.enabled = visible;
        }
    }

    private bool TryRaycastForPointer(Ray ray, float pointerLength, out RaycastHit hit)
    {
        LayerMask pointerLayers = selectableLayers | groundLayers;
        return TryRaycastGameplayHit(ray, pointerLength, pointerLayers, out hit);
    }

    private bool UsesTrackedPathTuning()
    {
        return SiegePlayEnvironment.IsTrackedXr;
    }

    private float GetEffectiveSampleDistance()
    {
        return UsesTrackedPathTuning() ? trackedPathSampleMinDistance : pathSampleMinDistance;
    }

    private float GetEffectiveInitialSampleDistance()
    {
        return UsesTrackedPathTuning() ? trackedPathInitialSampleMinDistance : pathInitialSampleMinDistance;
    }

    private bool TryGetSmoothedBattlefieldPoint(Ray ray, out Vector3 point)
    {
        if (!TryGetBattlefieldPoint(ray, out Vector3 rawPoint))
        {
            point = default;
            return false;
        }

        if (!UsesTrackedPathTuning())
        {
            point = rawPoint;
            return true;
        }

        if (!hasSmoothedAimPoint)
        {
            smoothedAimPoint = rawPoint;
            hasSmoothedAimPoint = true;
            point = rawPoint;
            return true;
        }

        smoothedAimPoint = RtsPathUtility.SmoothTrackedAimPoint(
            smoothedAimPoint,
            rawPoint,
            trackedAimSmoothingStrength);
        point = smoothedAimPoint;
        return true;
    }

    private static void AppendArrowHead(List<Vector3> points, float arrowHeadLength, float arrowHeadAngle)
    {
        if (points.Count < 2 || arrowHeadLength <= 0f)
        {
            return;
        }

        Vector3 end = points[points.Count - 1];
        Vector3 previous = points[points.Count - 2];
        Vector3 direction = end - previous;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        direction.Normalize();
        Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
        Vector3 side = rotation * Quaternion.Euler(0f, 180f + arrowHeadAngle, 0f) * Vector3.forward;
        points.Add(end + side * arrowHeadLength);
        points.Add(end);
        side = rotation * Quaternion.Euler(0f, 180f - arrowHeadAngle, 0f) * Vector3.forward;
        points.Add(end + side * arrowHeadLength);
    }

    private void OnGUI()
    {
        if (!showDebugOverlay)
        {
            return;
        }

        GUI.Box(new Rect(10f, 10f, 780f, 150f), "Votanic RTS Debug");
        GUI.Label(new Rect(20f, 35f, 500f, 20f), debugStatusLine);
        GUI.Label(new Rect(20f, 55f, 500f, 20f), debugHoverLine);
        GUI.Label(new Rect(20f, 75f, 500f, 20f), debugPathLine);
        GUI.Label(new Rect(20f, 95f, 720f, 20f), "Hovered: " + (hoveredUnit != null ? hoveredUnit.name : "none") + " | Commanding: " + (commandingUnit != null ? commandingUnit.name : "none"));
        GUI.Label(new Rect(20f, 115f, 720f, 20f), "Recording: " + isRecordingPath + " | Raw points: " + recordedPathPoints.Count + " | Env: " + SiegePlayEnvironment.ActiveMode);
    }
}
