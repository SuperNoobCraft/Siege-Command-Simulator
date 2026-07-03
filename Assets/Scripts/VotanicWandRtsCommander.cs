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

    [Header("Debug")]
    [SerializeField] private bool drawDebugRay;
    [SerializeField] private bool verboseDebugLogs;
    [SerializeField] private bool showDebugOverlay;

    [Header("Selection")]
    [SerializeField] private bool autoDeselectAfterMove = true;
    [SerializeField] private bool clearSelectionOnMissClick = true;

    private RtsUnitMotor selectedUnit;
    private RtsUnitMotor hoveredUnit;
    private string debugStatusLine = "Ready";
    private string debugHoverLine = "Hover: none";
    private string debugMoveLine = "Move: none";
    private string debugGroundHitsLine = "Ground hits: none";
    private string debugWallHitsLine = "Wall hits: none";

    private void Update()
    {
        if (selectedUnit != null && !selectedUnit.CanReceiveCommands)
        {
            SetSelectedUnit(null);
        }

        Ray ray = BuildWandRay();
        UpdateHoveredUnit(ray);

        if (drawDebugRay)
        {
            Debug.DrawRay(ray.origin, ray.direction * maxRayDistance, Color.cyan, 1.2f);
        }

        if (!IsCommandPressed())
        {
            return;
        }

        if (verboseDebugLogs)
        {
            Debug.Log("RTS command pressed");
        }

        if (selectedUnit == null)
        {
            if (hoveredUnit != null)
            {
                SetSelectedUnit(hoveredUnit);
            }
            return;
        }

        if (TryIssueMoveOrder(ray))
        {
            if (autoDeselectAfterMove)
            {
                SetSelectedUnit(null);
            }
            return;
        }

        if (hoveredUnit != null)
        {
            SetSelectedUnit(hoveredUnit);
            return;
        }

        if (clearSelectionOnMissClick)
        {
            SetSelectedUnit(null);
        }
    }

    private bool IsCommandPressed()
    {
        if (vGear.Cmd.Received(issueCommandName))
        {
            return true;
        }

        if (!enableDesktopFallback)
        {
            return false;
        }

        bool keyA = desktopKeyA != KeyCode.None && Input.GetKeyDown(desktopKeyA);
        bool keyB = desktopKeyB != KeyCode.None && Input.GetKeyDown(desktopKeyB);
        return keyA || keyB;
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

    private void UpdateHoveredUnit(Ray ray)
    {
        if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, selectableLayers, QueryTriggerInteraction.Ignore))
        {
            SetHoveredUnit(null);
            debugHoverLine = "Hover: none";
            return;
        }

        RtsUnitMotor unit = hit.collider.GetComponentInParent<RtsUnitMotor>();
        bool isCommandUnit = unit != null && unit.IsCommandUnit && unit.CanReceiveCommands;
        debugHoverLine = "Hover hit: " + hit.collider.name + " | dist=" + hit.distance.ToString("F2") + " | unit=" + (unit != null ? unit.name : "none") + " | commandable=" + isCommandUnit;

        if (verboseDebugLogs)
        {
            Debug.Log(debugHoverLine);
        }

        SetHoveredUnit(isCommandUnit ? unit : null);
    }

    private bool TryIssueMoveOrder(Ray ray)
    {
        if (selectedUnit == null)
        {
            return false;
        }

        if (!selectedUnit.CanReceiveCommands)
        {
            debugMoveLine = "Move: selected unit cannot receive commands";
            return false;
        }

        if (groundCollider != null)
        {
            if (!groundCollider.Raycast(ray, out RaycastHit hit, maxRayDistance))
            {
                debugMoveLine = "Move: no ground hit";
                debugGroundHitsLine = "Ground collider: " + groundCollider.name + " | no hit";
                debugWallHitsLine = BuildHitList(ray, ~groundLayers, "Other hits");

                if (verboseDebugLogs)
                {
                    Debug.Log(debugMoveLine);
                    Debug.Log(debugGroundHitsLine);
                    Debug.Log(debugWallHitsLine);
                }

                return false;
            }

            debugMoveLine = "Move hit: " + hit.collider.name + " | dist=" + hit.distance.ToString("F2") + " | point=" + hit.point;
            debugGroundHitsLine = "Ground collider: " + groundCollider.name + " | hit dist=" + hit.distance.ToString("F2") + " | point=" + hit.point;
            debugWallHitsLine = BuildHitList(ray, ~groundLayers, "Other hits");

            if (verboseDebugLogs)
            {
                Debug.Log(debugMoveLine);
                Debug.Log(debugGroundHitsLine);
                Debug.Log(debugWallHitsLine);
            }

            selectedUnit.MoveTo(hit.point);
            debugStatusLine = "Selected: " + selectedUnit.name + " -> moving";
            return true;
        }

        if (!Physics.Raycast(ray, out RaycastHit groundHit, maxRayDistance, groundLayers, QueryTriggerInteraction.Ignore))
        {
            debugMoveLine = "Move: no ground hit";
            debugGroundHitsLine = BuildHitList(ray, groundLayers, "Ground hits");
            debugWallHitsLine = BuildHitList(ray, ~groundLayers, "Other hits");

            if (verboseDebugLogs)
            {
                Debug.Log(debugMoveLine);
                Debug.Log(debugGroundHitsLine);
                Debug.Log(debugWallHitsLine);
            }

            return false;
        }

        debugMoveLine = "Move hit: " + groundHit.collider.name + " | dist=" + groundHit.distance.ToString("F2") + " | point=" + groundHit.point;
        debugGroundHitsLine = BuildHitList(ray, groundLayers, "Ground hits");
        debugWallHitsLine = BuildHitList(ray, ~groundLayers, "Other hits");

        if (verboseDebugLogs)
        {
            Debug.Log(debugMoveLine);
            Debug.Log(debugGroundHitsLine);
            Debug.Log(debugWallHitsLine);
        }

        selectedUnit.MoveTo(groundHit.point);
        debugStatusLine = "Selected: " + selectedUnit.name + " -> moving";
        return true;
    }

    private void SetHoveredUnit(RtsUnitMotor newHoveredUnit)
    {
        if (hoveredUnit == newHoveredUnit)
        {
            return;
        }

        SetHighlightState(hoveredUnit, isHovered: false, isSelected: hoveredUnit == selectedUnit);
        hoveredUnit = newHoveredUnit;
        SetHighlightState(hoveredUnit, isHovered: hoveredUnit != selectedUnit, isSelected: hoveredUnit == selectedUnit);
        debugStatusLine = "Selected: " + (selectedUnit != null ? selectedUnit.name : "none") + " | Hover: " + (hoveredUnit != null ? hoveredUnit.name : "none");
    }

    private void SetSelectedUnit(RtsUnitMotor newSelectedUnit)
    {
        if (selectedUnit == newSelectedUnit)
        {
            return;
        }

        SetHighlightState(selectedUnit, isHovered: selectedUnit == hoveredUnit, isSelected: false);
        selectedUnit = newSelectedUnit;
        SetHighlightState(selectedUnit, isHovered: false, isSelected: true);

        if (selectedUnit == null && hoveredUnit != null)
        {
            SetHighlightState(hoveredUnit, isHovered: true, isSelected: false);
        }

        debugStatusLine = "Selected: " + (selectedUnit != null ? selectedUnit.name : "none") + " | Hover: " + (hoveredUnit != null ? hoveredUnit.name : "none");
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

    private void OnGUI()
    {
        if (!showDebugOverlay)
        {
            return;
        }

        GUI.Box(new Rect(10f, 10f, 780f, 170f), "Votanic RTS Debug");
        GUI.Label(new Rect(20f, 35f, 500f, 20f), debugStatusLine);
        GUI.Label(new Rect(20f, 55f, 500f, 20f), debugHoverLine);
        GUI.Label(new Rect(20f, 75f, 500f, 20f), debugMoveLine);
        GUI.Label(new Rect(20f, 95f, 720f, 20f), debugGroundHitsLine);
        GUI.Label(new Rect(20f, 115f, 720f, 20f), debugWallHitsLine);
        GUI.Label(new Rect(20f, 135f, 720f, 20f), "Selected: " + (selectedUnit != null ? selectedUnit.name : "none") + " | Hovered: " + (hoveredUnit != null ? hoveredUnit.name : "none"));
    }

    private string BuildHitList(Ray ray, LayerMask mask, string label)
    {
        RaycastHit[] hits = Physics.RaycastAll(ray, maxRayDistance, mask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
        {
            return label + ": none";
        }

        System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        int count = Mathf.Min(hits.Length, 6);
        string result = label + ": ";
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            string layerName = LayerMask.LayerToName(hit.collider.gameObject.layer);
            if (string.IsNullOrEmpty(layerName))
            {
                layerName = hit.collider.gameObject.layer.ToString();
            }

            result += "[" + hit.collider.name + " | layer=" + layerName + " | dist=" + hit.distance.ToString("F2") + "]";
            if (i < count - 1)
            {
                result += " ";
            }
        }

        return result;
    }
}
