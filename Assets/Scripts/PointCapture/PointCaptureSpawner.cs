using System;
using UnityEngine;

/// <summary>
/// Instantiates existing regiment prefabs inside a side's area of control.
/// </summary>
[DisallowMultipleComponent]
public class PointCaptureSpawner : MonoBehaviour
{
    public enum RegimentType
    {
        Infantry = 0,
        Archer = 1
    }

    [Header("Prefabs (yellow = own/friendly, red = foe)")]
    [SerializeField] private GameObject redInfantryPrefab;
    [SerializeField] private GameObject redArcherPrefab;
    [SerializeField] private GameObject yellowInfantryPrefab;
    [SerializeField] private GameObject yellowArcherPrefab;

    [Header("Placement")]
    [SerializeField] private Transform unitsRoot;
    [SerializeField] private float spawnSurfaceOffset = 0.05f;
    [SerializeField, Min(1f)] private float startingSpawnOffset = 4f;
    [SerializeField, Min(0.1f)] private float raiseAppearSeconds = 1f;

    public GameObject RedInfantryPrefab => redInfantryPrefab;
    public GameObject RedArcherPrefab => redArcherPrefab;
    public GameObject YellowInfantryPrefab => yellowInfantryPrefab;
    public GameObject YellowArcherPrefab => yellowArcherPrefab;

    private int nextRegimentSpawnId = 100;

    private void Awake()
    {
        AlignPrefabsToFactionColors();
    }

    private void OnEnable()
    {
        TroopCombat.RegimentRegroupCompleted += HandleRegroupCompleted;
    }

    private void OnDisable()
    {
        TroopCombat.RegimentRegroupCompleted -= HandleRegroupCompleted;
    }

    public void Configure(
        GameObject redInfantry,
        GameObject redArcher,
        GameObject yellowInfantry,
        GameObject yellowArcher,
        Transform spawnedRoot)
    {
        redInfantryPrefab = redInfantry;
        redArcherPrefab = redArcher;
        yellowInfantryPrefab = yellowInfantry;
        yellowArcherPrefab = yellowArcher;
        unitsRoot = spawnedRoot;
    }

    public void ClearSpawnedUnits()
    {
        nextRegimentSpawnId = 100;

        if (unitsRoot == null)
        {
            return;
        }

        for (int i = unitsRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = unitsRoot.GetChild(i);
            if (child == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }
    }

    public void SpawnStartingForces()
    {
        PointCaptureBoard board = PointCaptureBoard.Instance;
        if (board == null)
        {
            return;
        }

        SpawnAtHome(CaptureOwner.Red, board, Vector3.right);
        SpawnAtHome(CaptureOwner.Yellow, board, Vector3.left);
    }

    public bool TryRaise(
        CaptureOwner owner,
        RegimentType regimentType,
        Vector3 worldPosition,
        out string failReason)
    {
        failReason = string.Empty;
        PointCaptureMatch match = PointCaptureMatch.Instance;
        PointCaptureBoard board = PointCaptureBoard.Instance;
        if (match == null || board == null)
        {
            failReason = "Match is not ready.";
            return false;
        }

        if (!match.IsPlaying)
        {
            failReason = "Match has not started.";
            return false;
        }

        if (!board.CanRaiseAt(owner, worldPosition, out failReason))
        {
            return false;
        }

        float cost = match.GetRaiseCost(regimentType);
        if (!match.TrySpendManpower(owner, cost))
        {
            failReason = "Not enough manpower (" + cost.ToString("0") + " required).";
            return false;
        }

        GameObject spawned = SpawnRegiment(owner, regimentType, worldPosition, allocateSpawnId: true);
        if (spawned == null)
        {
            failReason = "Missing regiment prefab.";
            return false;
        }

        PointCaptureNetworkSession networkSession = PointCaptureNetworkSession.Instance;
        if (networkSession != null)
        {
            networkSession.NotifyLocalRegimentSpawned(owner, regimentType, spawned.transform.position, GetSpawnId(spawned));
        }

        return true;
    }

    public GameObject SpawnRegimentFromNetwork(
        CaptureOwner owner,
        RegimentType regimentType,
        Vector3 worldPosition,
        int regimentSpawnId)
    {
        return SpawnRegiment(owner, regimentType, worldPosition, allocateSpawnId: false, regimentSpawnId);
    }

    public GameObject SpawnRegiment(CaptureOwner owner, RegimentType regimentType, Vector3 worldPosition)
    {
        return SpawnRegiment(owner, regimentType, worldPosition, allocateSpawnId: true);
    }

    private GameObject SpawnRegiment(
        CaptureOwner owner,
        RegimentType regimentType,
        Vector3 worldPosition,
        bool allocateSpawnId,
        int regimentSpawnId = -1)
    {
        GameObject prefab = GetPrefab(owner, regimentType);
        if (prefab == null)
        {
            Debug.LogWarning("No prefab assigned for " + owner + " " + regimentType + ".", this);
            return null;
        }

        Vector3 spawnPosition = RtsGroundUtility.ProjectPointOntoGround(worldPosition, spawnSurfaceOffset);
        Vector3 lookTarget = Vector3.zero;
        PointCaptureBoard board = PointCaptureBoard.Instance;
        if (board != null && board.GetVillage(2) != null)
        {
            lookTarget = board.GetVillage(2).Position;
        }

        Vector3 look = lookTarget - spawnPosition;
        look.y = 0f;
        Quaternion rotation = look.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(look.normalized, Vector3.up)
            : Quaternion.identity;

        GameObject instance = Instantiate(prefab, spawnPosition, rotation, unitsRoot);
        int spawnId = allocateSpawnId ? AllocateRegimentSpawnId() : regimentSpawnId;
        instance.name = BuildRegimentName(owner, regimentType, spawnId);
        PrepareSpawnedRegiment(instance, playRaiseReveal: true);
        return instance;
    }

    private void SpawnAtHome(CaptureOwner owner, PointCaptureBoard board, Vector3 sideOffset)
    {
        PointCaptureVillage home = board.GetHomeVillage(owner);
        if (home == null)
        {
            return;
        }

        Vector3 towardCenter = -home.Position;
        towardCenter.y = 0f;
        if (towardCenter.sqrMagnitude < 0.01f)
        {
            towardCenter = Vector3.forward;
        }

        towardCenter.Normalize();
        Vector3 spawnPoint = home.Position + towardCenter * startingSpawnOffset + sideOffset.normalized * 0.5f;
        SpawnRegimentInstant(owner, RegimentType.Infantry, spawnPoint, GetStartingSpawnId(owner));
    }

    private static int GetStartingSpawnId(CaptureOwner owner)
    {
        return owner == CaptureOwner.Yellow ? 1 : 2;
    }

    private GameObject SpawnRegimentInstant(CaptureOwner owner, RegimentType regimentType, Vector3 worldPosition, int regimentSpawnId)
    {
        GameObject prefab = GetPrefab(owner, regimentType);
        if (prefab == null)
        {
            return null;
        }

        Vector3 spawnPosition = RtsGroundUtility.ProjectPointOntoGround(worldPosition, spawnSurfaceOffset);
        GameObject instance = Instantiate(prefab, spawnPosition, Quaternion.identity, unitsRoot);
        instance.name = BuildRegimentName(owner, regimentType, regimentSpawnId);
        PrepareSpawnedRegiment(instance, playRaiseReveal: false);
        return instance;
    }

    private int AllocateRegimentSpawnId()
    {
        nextRegimentSpawnId = Mathf.Max(nextRegimentSpawnId + 1, 101);
        return nextRegimentSpawnId;
    }

    private static string BuildRegimentName(CaptureOwner owner, RegimentType regimentType, int regimentSpawnId)
    {
        return CaptureTeams.GetDisplayName(owner) + "_" + regimentType + "_s" + regimentSpawnId;
    }

    private static int GetSpawnId(GameObject regiment)
    {
        if (regiment == null || string.IsNullOrEmpty(regiment.name))
        {
            return -1;
        }

        int marker = regiment.name.LastIndexOf("_s", StringComparison.Ordinal);
        if (marker < 0)
        {
            return -1;
        }

        string suffix = regiment.name.Substring(marker + 2);
        return int.TryParse(suffix, out int spawnId) ? spawnId : -1;
    }

    private GameObject GetPrefab(CaptureOwner owner, RegimentType regimentType)
    {
        if (owner == CaptureOwner.Yellow)
        {
            return regimentType == RegimentType.Archer ? yellowArcherPrefab : yellowInfantryPrefab;
        }

        return regimentType == RegimentType.Archer ? redArcherPrefab : redInfantryPrefab;
    }

    private void PrepareSpawnedRegiment(GameObject instance, bool playRaiseReveal)
    {
        if (instance == null)
        {
            return;
        }

        EnemyRegimentAI ai = instance.GetComponent<EnemyRegimentAI>();
        if (ai == null)
        {
            ai = instance.GetComponentInChildren<EnemyRegimentAI>();
        }

        if (ai != null)
        {
            ai.enabled = false;
            Destroy(ai);
        }

        RtsUnitMotor motor = instance.GetComponent<RtsUnitMotor>();
        if (motor == null)
        {
            motor = instance.GetComponentInChildren<RtsUnitMotor>();
        }

        RestoreCommandable(motor);

        TroopCombat combat = instance.GetComponent<TroopCombat>();
        if (combat == null)
        {
            combat = instance.GetComponentInChildren<TroopCombat>();
        }

        if (playRaiseReveal && combat != null)
        {
            combat.BeginPointCaptureRaiseReveal(raiseAppearSeconds);
        }
    }

    private static void HandleRegroupCompleted(TroopCombat troop)
    {
        if (troop == null || PointCaptureMatch.Instance == null)
        {
            return;
        }

        troop.SetHoldInCampUntilNextWave(false);
        RtsUnitMotor motor = troop.GetComponent<RtsUnitMotor>();
        if (motor == null)
        {
            motor = troop.GetComponentInChildren<RtsUnitMotor>();
        }

        RestoreCommandable(motor);
    }

    private static void RestoreCommandable(RtsUnitMotor motor)
    {
        if (motor == null)
        {
            return;
        }

        motor.SetIsCommandUnit(true);
        motor.CanReceiveCommands = true;
    }

    private void AlignPrefabsToFactionColors()
    {
        if (IsEnemyPrefab(yellowInfantryPrefab) && IsFriendlyPrefab(redInfantryPrefab))
        {
            GameObject swap = redInfantryPrefab;
            redInfantryPrefab = yellowInfantryPrefab;
            yellowInfantryPrefab = swap;
        }

        if (IsEnemyPrefab(yellowArcherPrefab) && IsFriendlyPrefab(redArcherPrefab))
        {
            GameObject swap = redArcherPrefab;
            redArcherPrefab = yellowArcherPrefab;
            yellowArcherPrefab = swap;
        }
    }

    private static bool IsFriendlyPrefab(GameObject prefab)
    {
        return GetPrefabFaction(prefab) == TroopCombat.Faction.Friendly;
    }

    private static bool IsEnemyPrefab(GameObject prefab)
    {
        return GetPrefabFaction(prefab) == TroopCombat.Faction.Enemy;
    }

    private static TroopCombat.Faction? GetPrefabFaction(GameObject prefab)
    {
        if (prefab == null)
        {
            return null;
        }

        TroopCombat combat = prefab.GetComponent<TroopCombat>();
        if (combat == null)
        {
            combat = prefab.GetComponentInChildren<TroopCombat>();
        }

        return combat != null ? combat.TroopFaction : null;
    }
}
