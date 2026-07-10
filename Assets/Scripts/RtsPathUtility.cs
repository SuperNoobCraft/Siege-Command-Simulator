using System.Collections.Generic;
using UnityEngine;

public static class RtsPathUtility
{
    public static List<Vector3> BuildCommandPath(
        IReadOnlyList<Vector3> rawPoints,
        float groundY,
        float minSampleDistance,
        float simplifyEpsilon,
        int smoothIterations,
        float maxWaypointSpacing)
    {
        if (rawPoints == null || rawPoints.Count == 0)
        {
            return new List<Vector3>();
        }

        List<Vector3> flattened = new List<Vector3>(rawPoints.Count);
        for (int i = 0; i < rawPoints.Count; i++)
        {
            flattened.Add(FlattenToGround(rawPoints[i], groundY));
        }

        List<Vector3> filtered = FilterByMinDistance(flattened, minSampleDistance);
        if (filtered.Count == 0)
        {
            return new List<Vector3>();
        }

        if (filtered.Count == 1)
        {
            return filtered;
        }

        List<Vector3> simplified = SimplifyPolyline(filtered, simplifyEpsilon);
        int effectiveSmoothIterations = simplified.Count <= 3
            ? Mathf.Min(smoothIterations, 1)
            : smoothIterations;
        List<Vector3> cornerSafe = BevelSharpCorners(simplified, 90f, 2f);
        List<Vector3> smoothed = SmoothPolylineSegmented(cornerSafe, effectiveSmoothIterations, 90f);
        return ResamplePolyline(smoothed, maxWaypointSpacing);
    }

    public static List<Vector3> BuildTrackedCommandPath(
        IReadOnlyList<Vector3> rawPoints,
        float groundY,
        float minSampleDistance,
        float simplifyEpsilon,
        int smoothIterations,
        float maxWaypointSpacing,
        int stabilizeWindow = 4)
    {
        if (rawPoints == null || rawPoints.Count == 0)
        {
            return new List<Vector3>();
        }

        List<Vector3> stabilized = StabilizeRawPoints(rawPoints, groundY, stabilizeWindow);
        List<Vector3> filtered = FilterByMinDistance(
            stabilized,
            minSampleDistance);
        if (filtered.Count <= 1)
        {
            return filtered.Count == 0 ? new List<Vector3>() : filtered;
        }

        List<Vector3> simplified = SimplifyPolyline(filtered, simplifyEpsilon);
        List<Vector3> cornerSafe = BevelSharpCorners(simplified, 90f, 2.5f);
        List<Vector3> smoothed = SmoothPolylineSegmented(cornerSafe, smoothIterations, 90f);
        return ResamplePolyline(smoothed, maxWaypointSpacing);
    }

    public static List<Vector3> StabilizeRawPoints(
        IReadOnlyList<Vector3> rawPoints,
        float groundY,
        int windowSize)
    {
        if (rawPoints == null || rawPoints.Count == 0)
        {
            return new List<Vector3>();
        }

        if (windowSize <= 1 || rawPoints.Count <= 2)
        {
            List<Vector3> flattenedOnly = new List<Vector3>(rawPoints.Count);
            for (int i = 0; i < rawPoints.Count; i++)
            {
                flattenedOnly.Add(FlattenToGround(rawPoints[i], groundY));
            }

            return flattenedOnly;
        }

        List<Vector3> flattened = new List<Vector3>(rawPoints.Count);
        for (int i = 0; i < rawPoints.Count; i++)
        {
            flattened.Add(FlattenToGround(rawPoints[i], groundY));
        }

        List<Vector3> stabilized = new List<Vector3>(flattened.Count);
        int radius = Mathf.Max(1, windowSize / 2);

        for (int i = 0; i < flattened.Count; i++)
        {
            Vector3 average = Vector3.zero;
            int count = 0;
            int start = Mathf.Max(0, i - radius);
            int end = Mathf.Min(flattened.Count - 1, i + radius);

            for (int j = start; j <= end; j++)
            {
                average += flattened[j];
                count++;
            }

            stabilized.Add(average / Mathf.Max(1, count));
        }

        return stabilized;
    }

    public static Vector3 SmoothTrackedAimPoint(Vector3 previous, Vector3 raw, float smoothingStrength)
    {
        float alpha = Mathf.Clamp01(smoothingStrength);
        if (previous.sqrMagnitude < 0.0001f)
        {
            return raw;
        }

        Vector3 blended = Vector3.Lerp(previous, raw, alpha);
        blended.y = raw.y;
        return blended;
    }

    public static List<Vector3> TrimLeadingStartJitter(
        IReadOnlyList<Vector3> points,
        Vector3 anchor,
        float jitterRadius)
    {
        if (points == null || points.Count < 3 || jitterRadius <= 0f)
        {
            return points == null ? new List<Vector3>() : new List<Vector3>(points);
        }

        Vector3 intent = points[points.Count - 1] - anchor;
        intent.y = 0f;
        if (intent.sqrMagnitude < 0.01f)
        {
            return new List<Vector3>(points);
        }

        intent.Normalize();
        float jitterRadiusSqr = jitterRadius * jitterRadius;
        List<Vector3> trimmed = new List<Vector3>(points);

        while (trimmed.Count > 2)
        {
            Vector3 candidate = trimmed[1];
            Vector3 fromAnchor = candidate - anchor;
            fromAnchor.y = 0f;
            if (fromAnchor.sqrMagnitude > jitterRadiusSqr)
            {
                break;
            }

            if (Vector3.Dot(fromAnchor, intent) >= 0f)
            {
                break;
            }

            trimmed.RemoveAt(1);
        }

        return trimmed;
    }

    public static float GetPathLength(IReadOnlyList<Vector3> points)
    {
        if (points == null || points.Count < 2)
        {
            return 0f;
        }

        float length = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            length += HorizontalDistance(points[i - 1], points[i]);
        }

        return length;
    }

    public static Vector3 FlattenToGround(Vector3 point, float groundY)
    {
        return new Vector3(point.x, groundY, point.z);
    }

    private static List<Vector3> FilterByMinDistance(IReadOnlyList<Vector3> points, float minDistance)
    {
        List<Vector3> filtered = new List<Vector3>(points.Count);
        float minDistanceSqr = minDistance * minDistance;

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 point = points[i];
            if (filtered.Count == 0)
            {
                filtered.Add(point);
                continue;
            }

            if (HorizontalDistanceSqr(filtered[filtered.Count - 1], point) >= minDistanceSqr)
            {
                filtered.Add(point);
            }
        }

        return filtered;
    }

    private static List<Vector3> SimplifyPolyline(IReadOnlyList<Vector3> points, float epsilon)
    {
        if (points.Count <= 2 || epsilon <= 0f)
        {
            return new List<Vector3>(points);
        }

        bool[] keep = new bool[points.Count];
        keep[0] = true;
        keep[points.Count - 1] = true;
        DouglasPeucker(points, 0, points.Count - 1, epsilon, keep);

        List<Vector3> simplified = new List<Vector3>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            if (keep[i])
            {
                simplified.Add(points[i]);
            }
        }

        return simplified;
    }

    private static void DouglasPeucker(IReadOnlyList<Vector3> points, int startIndex, int endIndex, float epsilon, bool[] keep)
    {
        if (endIndex <= startIndex + 1)
        {
            return;
        }

        float maxDistance = 0f;
        int farthestIndex = startIndex;
        Vector3 start = points[startIndex];
        Vector3 end = points[endIndex];

        for (int i = startIndex + 1; i < endIndex; i++)
        {
            float distance = PerpendicularDistance(points[i], start, end);
            if (distance > maxDistance)
            {
                maxDistance = distance;
                farthestIndex = i;
            }
        }

        if (maxDistance > epsilon)
        {
            keep[farthestIndex] = true;
            DouglasPeucker(points, startIndex, farthestIndex, epsilon, keep);
            DouglasPeucker(points, farthestIndex, endIndex, epsilon, keep);
        }
    }

    private static float PerpendicularDistance(Vector3 point, Vector3 lineStart, Vector3 lineEnd)
    {
        Vector3 axis = lineEnd - lineStart;
        axis.y = 0f;
        float axisLengthSqr = axis.sqrMagnitude;
        if (axisLengthSqr < 0.0001f)
        {
            return Mathf.Sqrt(HorizontalDistanceSqr(point, lineStart));
        }

        float t = Mathf.Clamp01(Vector3.Dot(point - lineStart, axis) / axisLengthSqr);
        Vector3 projection = lineStart + axis * t;
        return Mathf.Sqrt(HorizontalDistanceSqr(point, projection));
    }

    private static List<Vector3> SmoothPolyline(IReadOnlyList<Vector3> points, int iterations)
    {
        if (points.Count < 3 || iterations <= 0)
        {
            return new List<Vector3>(points);
        }

        List<Vector3> current = new List<Vector3>(points);
        List<Vector3> next = new List<Vector3>(points.Count * 2);

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            next.Clear();
            next.Add(current[0]);

            for (int i = 0; i < current.Count - 1; i++)
            {
                Vector3 a = current[i];
                Vector3 b = current[i + 1];
                next.Add(Vector3.Lerp(a, b, 0.25f));
                next.Add(Vector3.Lerp(a, b, 0.75f));
            }

            next.Add(current[current.Count - 1]);
            List<Vector3> swap = current;
            current = next;
            next = swap;
        }

        return current;
    }

    private static List<Vector3> ResamplePolyline(IReadOnlyList<Vector3> points, float maxSpacing)
    {
        if (points.Count <= 1 || maxSpacing <= 0f)
        {
            return new List<Vector3>(points);
        }

        float totalLength = GetPathLength(points);
        if (totalLength <= maxSpacing)
        {
            return new List<Vector3>(points);
        }

        int sampleCount = Mathf.Max(2, Mathf.CeilToInt(totalLength / maxSpacing) + 1);
        List<Vector3> resampled = new List<Vector3>(sampleCount);
        for (int i = 0; i < sampleCount; i++)
        {
            float distanceAlongPath = (totalLength * i) / (sampleCount - 1);
            resampled.Add(GetPointAlongPath(points, distanceAlongPath));
        }

        return resampled;
    }

    private static Vector3 GetPointAlongPath(IReadOnlyList<Vector3> points, float targetDistance)
    {
        if (points.Count == 0)
        {
            return Vector3.zero;
        }

        if (targetDistance <= 0f)
        {
            return points[0];
        }

        float traveled = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            float segmentLength = HorizontalDistance(points[i - 1], points[i]);
            if (segmentLength < 0.0001f)
            {
                continue;
            }

            if (traveled + segmentLength >= targetDistance)
            {
                float t = (targetDistance - traveled) / segmentLength;
                return Vector3.Lerp(points[i - 1], points[i], t);
            }

            traveled += segmentLength;
        }

        return points[points.Count - 1];
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        return Mathf.Sqrt(HorizontalDistanceSqr(a, b));
    }

    private static float HorizontalDistanceSqr(Vector3 a, Vector3 b)
    {
        Vector3 offset = a - b;
        offset.y = 0f;
        return offset.sqrMagnitude;
    }

    private static List<Vector3> SmoothPolylineSegmented(
        IReadOnlyList<Vector3> points,
        int iterations,
        float splitAngleDegrees)
    {
        if (points.Count < 3 || iterations <= 0)
        {
            return new List<Vector3>(points);
        }

        List<int> cornerIndices = new List<int>();
        for (int i = 1; i < points.Count - 1; i++)
        {
            if (GetTurnAngleDegrees(points[i - 1], points[i], points[i + 1]) > splitAngleDegrees)
            {
                cornerIndices.Add(i);
            }
        }

        if (cornerIndices.Count == 0)
        {
            return SmoothPolyline(points, iterations);
        }

        List<Vector3> combined = new List<Vector3>(points.Count * 2);
        int segmentStart = 0;

        for (int i = 0; i <= cornerIndices.Count; i++)
        {
            int segmentEnd = i < cornerIndices.Count ? cornerIndices[i] : points.Count - 1;
            int segmentLength = segmentEnd - segmentStart + 1;
            if (segmentLength >= 2)
            {
                List<Vector3> segment = new List<Vector3>(segmentLength);
                for (int j = segmentStart; j <= segmentEnd; j++)
                {
                    segment.Add(points[j]);
                }

                List<Vector3> smoothedSegment = SmoothPolyline(segment, iterations);
                if (combined.Count > 0 && smoothedSegment.Count > 0)
                {
                    smoothedSegment.RemoveAt(0);
                }

                combined.AddRange(smoothedSegment);
            }

            segmentStart = segmentEnd;
        }

        return combined;
    }

    private static List<Vector3> BevelSharpCorners(
        IReadOnlyList<Vector3> points,
        float sharpAngleDegrees,
        float bevelDistance)
    {
        if (points.Count < 3 || sharpAngleDegrees <= 0f || bevelDistance <= 0f)
        {
            return new List<Vector3>(points);
        }

        List<Vector3> beveled = new List<Vector3>(points.Count + 8);
        beveled.Add(points[0]);

        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector3 previous = points[i - 1];
            Vector3 corner = points[i];
            Vector3 next = points[i + 1];
            float turnAngle = GetTurnAngleDegrees(previous, corner, next);
            if (turnAngle <= sharpAngleDegrees)
            {
                beveled.Add(corner);
                continue;
            }

            Vector3 incoming = corner - previous;
            Vector3 outgoing = next - corner;
            incoming.y = 0f;
            outgoing.y = 0f;
            float incomingLength = incoming.magnitude;
            float outgoingLength = outgoing.magnitude;
            if (incomingLength < 0.05f || outgoingLength < 0.05f)
            {
                beveled.Add(corner);
                continue;
            }

            float inset = Mathf.Min(bevelDistance, incomingLength * 0.45f, outgoingLength * 0.45f);
            Vector3 beforeCorner = corner - incoming.normalized * inset;
            Vector3 afterCorner = corner + outgoing.normalized * inset;
            beveled.Add(beforeCorner);
            beveled.Add(corner);
            beveled.Add(afterCorner);
        }

        beveled.Add(points[points.Count - 1]);
        return beveled;
    }

    private static float GetTurnAngleDegrees(Vector3 previous, Vector3 corner, Vector3 next)
    {
        Vector3 incoming = corner - previous;
        Vector3 outgoing = next - corner;
        incoming.y = 0f;
        outgoing.y = 0f;
        if (incoming.sqrMagnitude < 0.0001f || outgoing.sqrMagnitude < 0.0001f)
        {
            return 0f;
        }

        return Vector3.Angle(incoming.normalized, outgoing.normalized);
    }
}
