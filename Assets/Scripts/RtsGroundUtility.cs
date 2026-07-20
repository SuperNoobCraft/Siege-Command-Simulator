using UnityEngine;

/// <summary>
/// Shared RTS ground probes. Casts down onto walkable ground under an XZ point.
/// </summary>
public static class RtsGroundUtility
{
    private const string GroundLayerName = "RTS_Ground";

    public static LayerMask DefaultGroundMask
    {
        get
        {
            int layer = LayerMask.NameToLayer(GroundLayerName);
            return layer >= 0 ? (LayerMask)(1 << layer) : (LayerMask)~0;
        }
    }

    public static Vector3 ProjectPointOntoGround(
        Vector3 point,
        float yOffset = 0.08f,
        float preferredY = float.NaN)
    {
        float prefer = float.IsNaN(preferredY) ? point.y : preferredY;
        if (TrySampleGroundY(
                point.x,
                point.z,
                DefaultGroundMask,
                Mathf.Max(256f, prefer + 64f),
                yOffset,
                out float groundY,
                preferredY: prefer,
                maxVerticalSnap: 64f))
        {
            return new Vector3(point.x, groundY, point.z);
        }

        point.y += yOffset;
        return point;
    }

    public static LayerMask DefaultSolidMask
    {
        get
        {
            int layer = LayerMask.NameToLayer("RTS_Solid");
            return layer >= 0 ? (LayerMask)(1 << layer) : (LayerMask)0;
        }
    }

    public static int UnitLayer
    {
        get { return LayerMask.NameToLayer("RTS_Unit"); }
    }

    /// <summary>
    /// Casts straight down and returns a walkable ground hit under XZ.
    /// When <paramref name="preferredY"/> is finite, prefers the surface nearest that height
    /// (stops flicker between overlapping ground meshes). From a high drop, uses topmost hit.
    /// </summary>
    public static bool TrySampleGroundY(
        float worldX,
        float worldZ,
        LayerMask groundLayers,
        float rayStartHeight,
        float yOffset,
        out float groundY,
        float preferredY = float.NaN,
        float maxVerticalSnap = 8f)
    {
        return TrySampleGround(
            worldX,
            worldZ,
            groundLayers,
            rayStartHeight,
            yOffset,
            out groundY,
            out _,
            preferredY,
            maxVerticalSnap);
    }

    /// <summary>
    /// Same as <see cref="TrySampleGroundY"/> but also returns the hit normal for slope/cliff checks.
    /// </summary>
    public static bool TrySampleGround(
        float worldX,
        float worldZ,
        LayerMask groundLayers,
        float rayStartHeight,
        float yOffset,
        out float groundY,
        out Vector3 groundNormal,
        float preferredY = float.NaN,
        float maxVerticalSnap = 8f)
    {
        groundY = 0f;
        groundNormal = Vector3.up;
        float startY = Mathf.Max(rayStartHeight, float.IsNaN(preferredY) ? 512f : preferredY + 64f);
        Vector3 origin = new Vector3(worldX, startY, worldZ);
        float maxDistance = startY + 2048f;

        LayerMask anyWalkable = ~0;
        int solidLayer = LayerMask.NameToLayer("RTS_Solid");
        if (solidLayer >= 0)
        {
            anyWalkable &= ~(1 << solidLayer);
        }

        int unitLayer = UnitLayer;
        if (unitLayer >= 0)
        {
            anyWalkable &= ~(1 << unitLayer);
        }

        if (TryFindGroundHit(origin, maxDistance, groundLayers, preferredY, maxVerticalSnap, out RaycastHit hit)
            || TryFindGroundHit(origin, maxDistance, DefaultGroundMask, preferredY, maxVerticalSnap, out hit)
            || TryFindGroundHit(origin, maxDistance, LayerMask.GetMask("Default"), preferredY, maxVerticalSnap, out hit)
            || TryFindGroundHit(origin, maxDistance, anyWalkable, preferredY, maxVerticalSnap, out hit)
            || TrySampleTerrainHeight(worldX, worldZ, out hit))
        {
            groundY = hit.point.y + yOffset;
            groundNormal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : Vector3.up;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Legacy overload kept for callers that pass a preferred Y as ray-start bias.
    /// </summary>
    public static bool TrySampleGroundY(
        float worldX,
        float worldZ,
        LayerMask groundLayers,
        float rayStartHeight,
        float preferredY,
        float yOffset,
        out float groundY)
    {
        float startY = Mathf.Max(rayStartHeight, preferredY + 64f);
        return TrySampleGroundY(
            worldX,
            worldZ,
            groundLayers,
            startY,
            yOffset,
            out groundY,
            preferredY,
            maxVerticalSnap: 8f);
    }

    private static bool TrySampleTerrainHeight(float worldX, float worldZ, out RaycastHit hit)
    {
        hit = default;
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null || terrain.terrainData == null)
        {
            return false;
        }

        Vector3 terrainPos = terrain.transform.position;
        float height = terrain.SampleHeight(new Vector3(worldX, 0f, worldZ)) + terrainPos.y;
        hit.point = new Vector3(worldX, height, worldZ);
        hit.distance = 0f;
        hit.normal = Vector3.up;
        return true;
    }

    private static bool TryFindGroundHit(
        Vector3 origin,
        float maxDistance,
        LayerMask groundLayers,
        float preferredY,
        float maxVerticalSnap,
        out RaycastHit bestHit)
    {
        bestHit = default;
        if (groundLayers == 0)
        {
            return false;
        }

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            maxDistance,
            groundLayers,
            QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
        {
            return false;
        }

        bool hasPreferred = !float.IsNaN(preferredY);
        // Force/init snaps use a large maxVerticalSnap (e.g. 512). Prefer the topmost surface
        // under the ray so authored y≈100 drops onto the field, not a nearby cliff ledge score.
        bool preferTopmostDrop = hasPreferred && maxVerticalSnap >= 256f;

        float bestScore = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null || ShouldIgnoreGroundCollider(hitCollider))
            {
                continue;
            }

            float hitY = hits[i].point.y;
            float score;
            if (preferTopmostDrop)
            {
                // Smallest ray distance = topmost surface under the drop.
                score = hits[i].distance;
            }
            else if (hasPreferred)
            {
                float verticalDelta = hitY - preferredY;
                float absDelta = Mathf.Abs(verticalDelta);
                // Still grounded: stick to the surface near current height.
                if (absDelta <= maxVerticalSnap)
                {
                    // Prefer the walkable surface at/below the regiment — not a ledge above.
                    score = absDelta + (verticalDelta > 0.05f ? 2f : 0f);
                }
                else if (preferredY > hitY + maxVerticalSnap)
                {
                    score = 1000f + hits[i].distance;
                }
                else
                {
                    // Preferred is below this hit (ceiling/overhang) — skip unless nothing else.
                    score = 5000f + absDelta;
                }
            }
            else
            {
                score = hits[i].distance;
            }

            if (score < bestScore)
            {
                bestScore = score;
                bestHit = hits[i];
                found = true;
            }
        }

        return found;
    }

    private static bool ShouldIgnoreGroundCollider(Collider collider)
    {
        if (!collider.enabled || !collider.gameObject.activeInHierarchy)
        {
            return true;
        }

        int solidLayer = LayerMask.NameToLayer("RTS_Solid");
        if (solidLayer >= 0 && collider.gameObject.layer == solidLayer)
        {
            return true;
        }

        if (collider.GetComponentInParent<RtsUnitMotor>() != null)
        {
            return true;
        }

        if (collider.GetComponentInParent<TroopCombat>() != null)
        {
            return true;
        }

        if (collider.GetComponentInParent<SiegeCommanderArrowHealth>() != null)
        {
            return true;
        }

        if (SiegePlayerBoundary.IsBoundaryCollider(collider))
        {
            return true;
        }

        return false;
    }
}
