using System.Collections.Generic;
using UnityEngine;
using Votanic.vXR.vGear;

public class VotanicWandRtsCommander : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private string issueCommandName = "Grab";
    [SerializeField] private bool enableDesktopFallback = true;
    [SerializeField] private KeyCode desktopKeyA = KeyCode.Mouse0;
    [SerializeField] private KeyCode desktopKeyB = KeyCode.None;
    [SerializeField] private Transform wandOrigin;
    [SerializeField] private float maxRayDistance = 1000f;

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
    private bool isRecordingPath;
    private bool wasCommandHeld;
    private bool vrCommandLatched;
    private float pathRecordingStartTime;
    private string debugStatusLine = "Ready";
    private string debugHoverLine = "Hover: none";
    private string debugPathLine = "Path: none";

    private void Awake()
    {
        EnsurePreviewPathLine();
    }

    private void Update()
    {
        if (commandingUnit != null && !commandingUnit.CanReceiveCommands)
        {
            CancelCommandMode();
        }

        Ray ray = BuildWandRay();
        UpdateHoveredUnit(ray);

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
        smoothedPathScratch.AddRange(RtsPathUtility.BuildCommandPath(
            recordedPathPoints,
            groundY,
            pathSampleMinDistance,
            pathSimplifyEpsilon,
            pathSmoothIterations,
            pathMaxWaypointSpacing));

        if (pathStartJitterRadius > 0f && smoothedPathScratch.Count >= 2)
        {
            List<Vector3> trimmed = RtsPathUtility.TrimLeadingStartJitter(
                smoothedPathScratch,
                recordedPathPoints[0],
                pathStartJitterRadius);
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

        if (!TryGetBattlefieldPoint(ray, out Vector3 battlefieldPoint))
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
            ? pathInitialSampleMinDistance
            : pathSampleMinDistance;
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

        if (TryGetBattlefieldPoint(BuildWandRay(), out Vector3 livePoint))
        {
            Vector3 lastPoint = previewPathPoints[previewPathPoints.Count - 1];
            Vector3 offset = livePoint - lastPoint;
            offset.y = 0f;
            if (offset.sqrMagnitude >= 0.05f)
            {
                previewPathPoints.Add(livePoint);
            }
        }

        if (previewPathPoints.Count < 2)
        {
            SetPreviewVisible(false);
            return;
        }

        List<Vector3> smoothedPreview = RtsPathUtility.BuildCommandPath(
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

        if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, selectableLayers, QueryTriggerInteraction.Ignore))
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
        if (enableDesktopFallback)
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

            point = hit.point;
            return true;
        }

        if (!Physics.Raycast(ray, out RaycastHit groundHit, maxRayDistance, groundLayers, QueryTriggerInteraction.Ignore))
        {
            debugPathLine = "Path: no ground hit";
            return false;
        }

        point = groundHit.point;
        return true;
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
        GUI.Label(new Rect(20f, 115f, 720f, 20f), "Recording: " + isRecordingPath + " | Raw points: " + recordedPathPoints.Count);
    }
}
