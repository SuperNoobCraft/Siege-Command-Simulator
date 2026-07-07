using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(RtsUnitMotor))]
public class RtsMovementPathDisplay : MonoBehaviour
{
    [Header("Active Path")]
    [SerializeField] private Color activePathColor = Color.red;
    [SerializeField, Min(0.01f)] private float lineWidth = 0.12f;
    [SerializeField, Min(0.1f)] private float arrowHeadLength = 0.75f;
    [SerializeField, Min(5f)] private float arrowHeadAngle = 28f;
    [SerializeField, Min(0.05f)] private float groundOffset = 0.08f;

    private RtsUnitMotor motor;
    private LineRenderer pathLine;
    private readonly List<Vector3> pathPoints = new List<Vector3>();
    private bool showOnHover;

    public void SetHoverVisible(bool visible)
    {
        showOnHover = visible;
    }

    private void Awake()
    {
        motor = GetComponent<RtsUnitMotor>();
        EnsurePathLine();
    }

    private void LateUpdate()
    {
        if (motor == null)
        {
            return;
        }

        if (!motor.HasActivePath || !isActiveAndEnabled || !showOnHover)
        {
            SetLineVisible(false);
            return;
        }

        motor.GetActivePathPoints(pathPoints);
        if (pathPoints.Count < 2)
        {
            SetLineVisible(false);
            return;
        }

        UpdatePathLine(pathPoints, activePathColor);
    }

    private void EnsurePathLine()
    {
        if (pathLine != null)
        {
            return;
        }

        GameObject lineObject = new GameObject("ActiveMovementPath");
        lineObject.transform.SetParent(transform, false);
        int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        lineObject.layer = ignoreRaycastLayer >= 0 ? ignoreRaycastLayer : gameObject.layer;

        pathLine = lineObject.AddComponent<LineRenderer>();
        pathLine.useWorldSpace = true;
        pathLine.loop = false;
        pathLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pathLine.receiveShadows = false;
        pathLine.allowOcclusionWhenDynamic = false;
        pathLine.textureMode = LineTextureMode.Stretch;
        pathLine.alignment = LineAlignment.View;
        pathLine.numCornerVertices = 4;
        pathLine.numCapVertices = 4;
        pathLine.widthMultiplier = lineWidth;
        pathLine.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        pathLine.enabled = false;
    }

    private void UpdatePathLine(IReadOnlyList<Vector3> points, Color color)
    {
        EnsurePathLine();
        List<Vector3> renderedPoints = new List<Vector3>(points.Count + 4);

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 point = points[i];
            point.y += groundOffset;
            renderedPoints.Add(point);
        }

        AppendArrowHead(renderedPoints);
        pathLine.positionCount = renderedPoints.Count;
        pathLine.widthMultiplier = lineWidth;
        pathLine.startColor = color;
        pathLine.endColor = color;
        pathLine.SetPositions(renderedPoints.ToArray());
        pathLine.enabled = true;
    }

    private void AppendArrowHead(List<Vector3> points)
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

    private void SetLineVisible(bool visible)
    {
        if (pathLine != null)
        {
            pathLine.enabled = visible;
        }
    }
}
