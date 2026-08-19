using System;
using UnityEngine;

/// <summary>
/// Five-village layout, adjacency, and area-of-control queries.
/// Control is a disc around each owned village plus a corridor between adjacent same-owner villages.
/// </summary>
[DefaultExecutionOrder(-90)]
[DisallowMultipleComponent]
public class PointCaptureBoard : MonoBehaviour
{
    [Serializable]
    public struct Adjacency
    {
        public int villageA;
        public int villageB;
    }

    public static PointCaptureBoard Instance { get; private set; }

    [Header("Villages")]
    [SerializeField] private PointCaptureVillage[] villages = new PointCaptureVillage[5];

    [Header("Adjacency")]
    [SerializeField] private Adjacency[] adjacentPairs;

    [Header("Control Shape")]
    [SerializeField, Min(1f)] private float controlRadius = 12f;
    [SerializeField, Min(1f)] private float corridorWidth = 12f;

    public event Action TerritoryChanged;

    public PointCaptureVillage[] Villages => villages;
    public Adjacency[] AdjacentPairs => adjacentPairs;
    public float ControlRadius => controlRadius;
    public float CorridorWidth => corridorWidth;

    private void Awake()
    {
        Instance = this;
        BindVillages();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void Configure(PointCaptureVillage[] configuredVillages, Adjacency[] pairs, float radius, float corridor)
    {
        villages = configuredVillages;
        adjacentPairs = pairs;
        controlRadius = radius;
        corridorWidth = corridor;
        BindVillages();
    }

    public void BindVillages()
    {
        PointCaptureMatch match = PointCaptureMatch.Instance;
        if (villages == null)
        {
            return;
        }

        for (int i = 0; i < villages.Length; i++)
        {
            if (villages[i] != null)
            {
                villages[i].Bind(this, match);
            }
        }
    }

    public void NotifyTerritoryChanged()
    {
        TerritoryChanged?.Invoke();
        if (PointCaptureMatch.Instance != null)
        {
            PointCaptureMatch.Instance.HandleTerritoryChanged();
        }
    }

    public int CountOwned(CaptureOwner owner)
    {
        int count = 0;
        if (villages == null)
        {
            return 0;
        }

        for (int i = 0; i < villages.Length; i++)
        {
            if (villages[i] != null && villages[i].CurrentOwner == owner)
            {
                count++;
            }
        }

        return count;
    }

    public bool IsInControl(CaptureOwner owner, Vector3 worldPosition)
    {
        return GetControllingOwner(worldPosition) == owner;
    }

    public CaptureOwner GetControllingOwner(Vector3 worldPosition)
    {
        bool red = CoversOwner(CaptureOwner.Red, worldPosition);
        bool yellow = CoversOwner(CaptureOwner.Yellow, worldPosition);
        if (red && !yellow)
        {
            return CaptureOwner.Red;
        }

        if (yellow && !red)
        {
            return CaptureOwner.Yellow;
        }

        return CaptureOwner.Neutral;
    }

    public bool CoversOwner(CaptureOwner owner, Vector3 worldPosition)
    {
        return IsInsideOwnedVillageDisc(owner, worldPosition)
            || IsInsideOwnedCorridor(owner, worldPosition);
    }

    public bool IsInsideOwnedVillageDisc(CaptureOwner owner, Vector3 worldPosition)
    {
        if (!CaptureTeams.IsPlayerSide(owner) || villages == null)
        {
            return false;
        }

        for (int i = 0; i < villages.Length; i++)
        {
            PointCaptureVillage village = villages[i];
            if (village == null || village.CurrentOwner != owner)
            {
                continue;
            }

            if (PointCaptureVillage.GetHorizontalDistance(worldPosition, village.Position) <= controlRadius)
            {
                return true;
            }
        }

        return false;
    }

    public bool IsInsideOwnedCorridor(CaptureOwner owner, Vector3 worldPosition)
    {
        if (!CaptureTeams.IsPlayerSide(owner) || adjacentPairs == null)
        {
            return false;
        }

        for (int i = 0; i < adjacentPairs.Length; i++)
        {
            PointCaptureVillage a = GetVillage(adjacentPairs[i].villageA);
            PointCaptureVillage b = GetVillage(adjacentPairs[i].villageB);
            if (a == null || b == null)
            {
                continue;
            }

            if (a.CurrentOwner != owner || b.CurrentOwner != owner)
            {
                continue;
            }

            if (DistanceToSegmentXZ(worldPosition, a.Position, b.Position) <= corridorWidth)
            {
                return true;
            }
        }

        return false;
    }

    public bool CanRaiseAt(CaptureOwner owner, Vector3 worldPosition, out string failReason)
    {
        failReason = string.Empty;
        if (!IsInControl(owner, worldPosition))
        {
            failReason = "Can only raise regiments in your area of control.";
            return false;
        }

        PointCaptureVillage discVillage = GetOwnedVillageDiscAt(owner, worldPosition);
        if (discVillage == null)
        {
            failReason = "Cannot raise in connecting territory between villages.";
            return false;
        }

        if (discVillage.HasCombatInZone)
        {
            failReason = "Cannot raise while there is combat in this village zone.";
            return false;
        }

        return true;
    }

    public PointCaptureVillage GetOwnedVillageDiscAt(CaptureOwner owner, Vector3 worldPosition)
    {
        if (!CaptureTeams.IsPlayerSide(owner) || villages == null)
        {
            return null;
        }

        PointCaptureVillage closest = null;
        float closestDistance = float.MaxValue;
        for (int i = 0; i < villages.Length; i++)
        {
            PointCaptureVillage village = villages[i];
            if (village == null || village.CurrentOwner != owner)
            {
                continue;
            }

            float distance = PointCaptureVillage.GetHorizontalDistance(worldPosition, village.Position);
            if (distance <= controlRadius && distance < closestDistance)
            {
                closest = village;
                closestDistance = distance;
            }
        }

        return closest;
    }

    public PointCaptureVillage GetVillage(int index)
    {
        if (villages == null || index < 0 || index >= villages.Length)
        {
            return null;
        }

        return villages[index];
    }

    public PointCaptureVillage GetVillageContaining(Vector3 worldPosition)
    {
        if (villages == null)
        {
            return null;
        }

        PointCaptureVillage closest = null;
        float closestDistance = float.MaxValue;
        for (int i = 0; i < villages.Length; i++)
        {
            PointCaptureVillage village = villages[i];
            if (village == null || !village.ContainsPoint(worldPosition))
            {
                continue;
            }

            float distance = PointCaptureVillage.GetHorizontalDistance(worldPosition, village.Position);
            if (distance < closestDistance)
            {
                closest = village;
                closestDistance = distance;
            }
        }

        return closest;
    }

    public PointCaptureVillage GetHomeVillage(CaptureOwner owner)
    {
        if (villages == null)
        {
            return null;
        }

        for (int i = 0; i < villages.Length; i++)
        {
            PointCaptureVillage village = villages[i];
            if (village != null && village.StartingOwner == owner)
            {
                return village;
            }
        }

        return null;
    }

    /// <summary>
    /// Nearest owned village disc suitable for retreat recovery. Prefers villages without active combat.
    /// If the regiment just lost a fight inside a village, that village is skipped so they flee elsewhere.
    /// </summary>
    public PointCaptureVillage FindBestRecoveryVillage(CaptureOwner owner, Vector3 fromPosition)
    {
        if (!CaptureTeams.IsPlayerSide(owner) || villages == null)
        {
            return null;
        }

        PointCaptureVillage currentVillage = GetVillageContaining(fromPosition);
        bool currentFightIsLost = currentVillage != null
            && currentVillage.CurrentOwner == owner
            && currentVillage.HasEnemyOccupants(owner);

        PointCaptureVillage bestSafe = null;
        float bestSafeDistance = float.MaxValue;
        PointCaptureVillage bestOther = null;
        float bestOtherDistance = float.MaxValue;
        PointCaptureVillage bestFallback = null;
        float bestFallbackDistance = float.MaxValue;

        for (int i = 0; i < villages.Length; i++)
        {
            PointCaptureVillage village = villages[i];
            if (village == null || village.CurrentOwner != owner)
            {
                continue;
            }

            float distance = PointCaptureVillage.GetHorizontalDistance(fromPosition, village.Position);
            if (distance < bestFallbackDistance)
            {
                bestFallback = village;
                bestFallbackDistance = distance;
            }

            bool lostThisVillage = currentFightIsLost && village == currentVillage;
            if (lostThisVillage)
            {
                continue;
            }

            if (distance < bestOtherDistance)
            {
                bestOther = village;
                bestOtherDistance = distance;
            }

            if (!village.HasCombatInZone && distance < bestSafeDistance)
            {
                bestSafe = village;
                bestSafeDistance = distance;
            }
        }

        if (bestSafe != null)
        {
            return bestSafe;
        }

        if (bestOther != null)
        {
            return bestOther;
        }

        return bestFallback;
    }

    public bool TryGetRecoveryDestination(CaptureOwner owner, Vector3 fromPosition, out Vector3 destination)
    {
        PointCaptureVillage village = FindBestRecoveryVillage(owner, fromPosition);
        if (village == null)
        {
            destination = default;
            return false;
        }

        destination = GetRecoveryApproachPosition(village, fromPosition);
        return true;
    }

    public Vector3 GetRecoveryApproachPosition(PointCaptureVillage village, Vector3 fromPosition)
    {
        if (village == null)
        {
            return fromPosition;
        }

        Vector3 villagePosition = village.Position;
        Vector3 towardRegiment = fromPosition - villagePosition;
        towardRegiment.y = 0f;
        if (towardRegiment.sqrMagnitude < 0.01f)
        {
            return villagePosition;
        }

        float approachRadius = Mathf.Max(controlRadius * 0.35f, 2f);
        return villagePosition + towardRegiment.normalized * approachRadius;
    }

    public static float DistanceToSegmentXZ(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector2 p = new Vector2(point.x, point.z);
        Vector2 av = new Vector2(a.x, a.z);
        Vector2 bv = new Vector2(b.x, b.z);
        Vector2 ab = bv - av;
        float lengthSquared = ab.sqrMagnitude;
        if (lengthSquared < 0.0001f)
        {
            return Vector2.Distance(p, av);
        }

        float t = Mathf.Clamp01(Vector2.Dot(p - av, ab) / lengthSquared);
        Vector2 projected = av + ab * t;
        return Vector2.Distance(p, projected);
    }

    private void OnDrawGizmos()
    {
        if (villages == null || adjacentPairs == null)
        {
            return;
        }

        Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
        for (int i = 0; i < adjacentPairs.Length; i++)
        {
            PointCaptureVillage a = GetVillage(adjacentPairs[i].villageA);
            PointCaptureVillage b = GetVillage(adjacentPairs[i].villageB);
            if (a == null || b == null)
            {
                continue;
            }

            Gizmos.DrawLine(a.Position + Vector3.up * 0.5f, b.Position + Vector3.up * 0.5f);
        }
    }
}
