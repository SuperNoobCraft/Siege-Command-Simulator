using UnityEngine;

/// <summary>
/// Regiment manpower upkeep and village recovery for Point Capture.
/// </summary>
[DisallowMultipleComponent]
public class PointCaptureArmyEconomy : MonoBehaviour
{
    private const float RecoveryCombatCooldownSeconds = 1f;

    [SerializeField] private PointCaptureMatch match;
    [SerializeField] private PointCaptureBoard board;
    [SerializeField, Min(0f)] private float idleUpkeepPerTenSeconds = 1f;
    [SerializeField, Min(0f)] private float recoveryUpkeepPerSecond = 3f;
    [SerializeField, Min(0f)] private float villageRecoveryHealthPerSecond = 20f;

    private void Awake()
    {
        if (match == null)
        {
            match = GetComponent<PointCaptureMatch>();
        }

        if (board == null)
        {
            board = GetComponent<PointCaptureBoard>();
        }
    }

    public void TickIdleUpkeep(int intervalCount)
    {
        if (intervalCount <= 0)
        {
            return;
        }

        if (!TryGetEconomyContext(out PointCaptureMatch activeMatch, out PointCaptureBoard activeBoard))
        {
            return;
        }

        TroopCombat[] troops = FindObjectsOfType<TroopCombat>();
        for (int i = 0; i < troops.Length; i++)
        {
            TroopCombat troop = troops[i];
            if (!IsActiveRegiment(troop) || IsRecoveringInVillage(troop, activeBoard))
            {
                continue;
            }

            CaptureOwner owner = CaptureTeams.FromTroopFaction(troop.TroopFaction);
            activeMatch.DrainManpower(owner, idleUpkeepPerTenSeconds * intervalCount);
        }
    }

    public void TickRecovery(float deltaTime)
    {
        if (deltaTime <= 0f || !TryGetEconomyContext(out PointCaptureMatch activeMatch, out PointCaptureBoard activeBoard))
        {
            return;
        }

        TroopCombat[] troops = FindObjectsOfType<TroopCombat>();
        for (int i = 0; i < troops.Length; i++)
        {
            TroopCombat troop = troops[i];
            if (!IsActiveRegiment(troop))
            {
                continue;
            }

            CaptureOwner owner = CaptureTeams.FromTroopFaction(troop.TroopFaction);
            if (!IsRecoveringInVillage(troop, owner, activeBoard))
            {
                continue;
            }

            float recoveryCost = recoveryUpkeepPerSecond * deltaTime;
            if (activeMatch.GetManpower(owner) < recoveryCost)
            {
                continue;
            }

            activeMatch.DrainManpower(owner, recoveryCost);
            troop.ApplyPointCaptureVillageRecovery(villageRecoveryHealthPerSecond, deltaTime);
        }
    }

    private bool TryGetEconomyContext(out PointCaptureMatch activeMatch, out PointCaptureBoard activeBoard)
    {
        activeMatch = match != null ? match : PointCaptureMatch.Instance;
        activeBoard = board != null ? board : PointCaptureBoard.Instance;
        return activeMatch != null && activeMatch.IsPlaying && activeBoard != null;
    }

    private static bool IsActiveRegiment(TroopCombat troop)
    {
        return troop != null
            && troop.isActiveAndEnabled
            && !troop.IsPermanentlyEliminated
            && troop.CurrentState != TroopCombat.State.Dead
            && !troop.IsPointCaptureRaising;
    }

    private bool IsRecoveringInVillage(TroopCombat troop, PointCaptureBoard activeBoard)
    {
        CaptureOwner owner = CaptureTeams.FromTroopFaction(troop.TroopFaction);
        return IsRecoveringInVillage(troop, owner, activeBoard);
    }

    private static bool IsRecoveringInVillage(TroopCombat troop, CaptureOwner owner, PointCaptureBoard activeBoard)
    {
        if (troop.IsInCombat
            || troop.WasRecentlyInCombat(RecoveryCombatCooldownSeconds)
            || troop.CurrentState == TroopCombat.State.Retreat)
        {
            return false;
        }

        if (troop.CurrentHealth >= troop.MaxHealth)
        {
            return false;
        }

        PointCaptureVillage village = activeBoard.GetOwnedVillageDiscAt(owner, troop.transform.position);
        if (village == null)
        {
            return false;
        }

        // If the village is under enemy presence, the whole zone blocks recovery for both sides.
        // (i.e. don't allow one side to heal through the other being in the same village.)
        return !village.HasCombatInZone;
    }
}
