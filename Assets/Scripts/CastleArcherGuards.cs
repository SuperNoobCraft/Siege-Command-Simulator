using System.Collections.Generic;
using UnityEngine;
using Votanic.vXR.vGear;

/// <summary>
/// Static castle-wall archers. Attach to a parent (e.g. castleArchers) whose children are archer visuals.
/// </summary>
public class CastleArcherGuards : MonoBehaviour
{
    [Header("Archer Slots")]
    [Tooltip("Leave empty to use all direct children as archer firing positions.")]
    [SerializeField] private Transform[] archerSlots;
    [SerializeField] private bool autoAssignChildArchers = true;

    [Header("Regiment Targeting")]
    [SerializeField, Min(1f)] private float attackRange = 42f;
    [SerializeField, Min(0.05f)] private float targetScanInterval = 0.35f;
    [SerializeField] private LayerMask targetLayers = ~0;
    [SerializeField] private bool ignoreFriendliesInCamp = true;

    [Header("Regiment Combat")]
    [SerializeField, Min(0f)] private float attackDamage = 7f;
    [SerializeField, Min(0.05f)] private float minAttackCooldown = 1.35f;
    [SerializeField, Min(0.05f)] private float maxAttackCooldown = 2.75f;
    [SerializeField] private bool faceTargetWhileAiming = true;
    [SerializeField, Min(0f)] private float regimentShotSpreadRadius = 1.75f;

    [Header("Command Tower Harassment")]
    [Tooltip("Optional override. Defaults to vGear head, then main camera.")]
    [SerializeField] private Transform playerTarget;
    [Tooltip("Minimum seconds before the next player-shot roll is allowed.")]
    [SerializeField, Min(0.5f)] private float playerHarassmentMinTimeGap = 18f;
    [Tooltip("Chance to fire at the command tower once the time gap has elapsed.")]
    [SerializeField, Range(0f, 1f)] private float playerHarassmentShotChance = 0.35f;
    [Tooltip("Shortest possible gap late in the match after scaling is applied.")]
    [SerializeField, Min(0.5f)] private float playerHarassmentMinScaledTimeGap = 8f;
    [Tooltip("Highest possible shot chance late in the match after scaling is applied.")]
    [SerializeField, Range(0f, 1f)] private float playerHarassmentMaxScaledChance = 0.75f;
    [Tooltip("Match time used as 100% progress for harassment scaling.")]
    [SerializeField, Min(1f)] private float matchDurationReferenceSeconds = 180f;
    [Tooltip("How strongly the gap shrinks from min to scaled minimum by match end.")]
    [SerializeField, Range(0f, 2f)] private float gapScaleStrength = 1f;
    [Tooltip("How strongly the shot chance rises from base to max by match end.")]
    [SerializeField, Range(0f, 2f)] private float chanceScaleStrength = 1f;
    [SerializeField, Min(0f)] private float playerShotSpreadRadius = 1.75f;
    [SerializeField] private LayerMask playerHitLayers = ~0;

    [Header("Projectile")]
    [SerializeField] private GameObject arrowPrefab;
    [SerializeField, Min(0.1f)] private float projectileSpeed = 22f;
    [SerializeField, Min(0f)] private float projectileArcHeight = 2.75f;
    [SerializeField, Min(0f)] private float projectileLaunchHeight = 1.15f;

    [Header("Debug")]
    [SerializeField] private bool drawDebugGizmos = true;
    [SerializeField] private Color rangeGizmoColor = new Color(0.95f, 0.35f, 0.2f, 0.35f);

    private readonly List<ArcherSlotState> slots = new List<ArcherSlotState>();
    private float nextScanTime;
    private float nextPlayerHarassmentRollTime;
    private float matchStartTime;
    private TroopCombat cachedRegimentTarget;

    private void Start()
    {
        matchStartTime = Time.time;
        CacheArcherSlots();
        ScheduleNextPlayerHarassmentRoll();
        RegisterPlayerHitRig();
    }

    private void RegisterPlayerHitRig()
    {
        Transform player = ResolvePlayerTarget();
        if (player == null)
        {
            return;
        }

        SiegeCommanderArrowHealth health = player.GetComponentInParent<SiegeCommanderArrowHealth>();
        if (health == null)
        {
            health = SiegeCommanderArrowHealth.Instance;
        }

        if (health != null)
        {
            health.RegisterHitRoot(player);
            return;
        }

        Debug.LogWarning(
            "CastleArcherGuards could not find SiegeCommanderArrowHealth for player target '"
            + player.name
            + "'. Add SiegeCommanderArrowHealth to the commander rig.",
            this);
    }

    private void OnValidate()
    {
        attackRange = Mathf.Max(1f, attackRange);
        targetScanInterval = Mathf.Max(0.05f, targetScanInterval);
        attackDamage = Mathf.Max(0f, attackDamage);
        minAttackCooldown = Mathf.Max(0.05f, minAttackCooldown);
        maxAttackCooldown = Mathf.Max(minAttackCooldown, maxAttackCooldown);
        regimentShotSpreadRadius = Mathf.Max(0f, regimentShotSpreadRadius);
        playerHarassmentMinTimeGap = Mathf.Max(0.5f, playerHarassmentMinTimeGap);
        playerHarassmentMinScaledTimeGap = Mathf.Max(0.5f, playerHarassmentMinScaledTimeGap);
        playerHarassmentMinScaledTimeGap = Mathf.Min(playerHarassmentMinScaledTimeGap, playerHarassmentMinTimeGap);
        playerHarassmentMaxScaledChance = Mathf.Clamp01(playerHarassmentMaxScaledChance);
        matchDurationReferenceSeconds = Mathf.Max(1f, matchDurationReferenceSeconds);
        gapScaleStrength = Mathf.Clamp(gapScaleStrength, 0f, 2f);
        chanceScaleStrength = Mathf.Clamp(chanceScaleStrength, 0f, 2f);
        playerShotSpreadRadius = Mathf.Max(0f, playerShotSpreadRadius);
        projectileSpeed = Mathf.Max(0.1f, projectileSpeed);
        projectileArcHeight = Mathf.Max(0f, projectileArcHeight);
        projectileLaunchHeight = Mathf.Max(0f, projectileLaunchHeight);
    }

    private void Update()
    {
        if (slots.Count == 0)
        {
            return;
        }

        UpdatePlayerHarassment();

        if (Time.time >= nextScanTime)
        {
            nextScanTime = Time.time + targetScanInterval;
            cachedRegimentTarget = FindBestFriendlyRegimentTarget();
        }

        for (int i = 0; i < slots.Count; i++)
        {
            UpdateArcherSlot(slots[i]);
        }
    }

    private void UpdatePlayerHarassment()
    {
        if (SiegeCommanderArrowHealth.Instance != null && SiegeCommanderArrowHealth.Instance.IsDefeated)
        {
            return;
        }

        if (Time.time < nextPlayerHarassmentRollTime)
        {
            return;
        }

        float shotChance = GetCurrentPlayerHarassmentChance();
        bool shouldFire = shotChance > 0f && Random.value <= shotChance;
        ScheduleNextPlayerHarassmentRoll();

        if (!shouldFire)
        {
            return;
        }

        Transform player = ResolvePlayerTarget();
        if (player == null)
        {
            return;
        }

        ArcherSlotState archer = slots[Random.Range(0, slots.Count)];
        FirePlayerHazardShot(archer, player);
    }

    private void ScheduleNextPlayerHarassmentRoll()
    {
        nextPlayerHarassmentRollTime = Time.time + GetCurrentPlayerHarassmentGap();
    }

    private float GetMatchProgress()
    {
        float elapsed = GetMatchElapsedSeconds();
        return Mathf.Clamp01(elapsed / matchDurationReferenceSeconds);
    }

    private float GetMatchElapsedSeconds()
    {
        EnemyWaveController waveController = EnemyWaveController.Instance;
        if (waveController != null)
        {
            return waveController.MatchElapsedSeconds;
        }

        return Time.time - matchStartTime;
    }

    private float GetCurrentPlayerHarassmentGap()
    {
        float scale = Mathf.Clamp01(GetMatchProgress() * gapScaleStrength);
        return Mathf.Lerp(playerHarassmentMinTimeGap, playerHarassmentMinScaledTimeGap, scale);
    }

    private float GetCurrentPlayerHarassmentChance()
    {
        float scale = Mathf.Clamp01(GetMatchProgress() * chanceScaleStrength);
        return Mathf.Lerp(playerHarassmentShotChance, playerHarassmentMaxScaledChance, scale);
    }

    private void FirePlayerHazardShot(ArcherSlotState archer, Transform player)
    {
        if (archer.Transform == null)
        {
            return;
        }

        Vector3 spread = Random.insideUnitSphere * playerShotSpreadRadius;
        spread.y *= 0.65f;
        Vector3 aimPoint = player.position + spread;

        if (faceTargetWhileAiming)
        {
            FaceArcherToward(archer.Transform, aimPoint);
        }

        if (arrowPrefab == null)
        {
            return;
        }

        Vector3 launchPoint = archer.Transform.position;
        launchPoint.y += projectileLaunchHeight;

        TroopRangedProjectile.LaunchPlayerHazard(
            arrowPrefab,
            launchPoint,
            aimPoint,
            projectileSpeed,
            projectileArcHeight,
            playerHitLayers);
    }

    private void CacheArcherSlots()
    {
        slots.Clear();

        Transform[] sources = archerSlots;
        if ((sources == null || sources.Length == 0) && autoAssignChildArchers)
        {
            int childCount = transform.childCount;
            sources = new Transform[childCount];
            for (int i = 0; i < childCount; i++)
            {
                sources[i] = transform.GetChild(i);
            }
        }

        if (sources == null)
        {
            return;
        }

        float maxCooldown = Mathf.Max(minAttackCooldown, maxAttackCooldown);
        for (int i = 0; i < sources.Length; i++)
        {
            Transform slot = sources[i];
            if (slot == null)
            {
                continue;
            }

            float initialDelay = Random.Range(0f, maxCooldown);
            slots.Add(new ArcherSlotState
            {
                Transform = slot,
                NextFireTime = Time.time + initialDelay
            });
        }
    }

    private void UpdateArcherSlot(ArcherSlotState slot)
    {
        if (slot.Transform == null || Time.time < slot.NextFireTime)
        {
            return;
        }

        if (!TryChooseRegimentShot(slot.Transform.position, out ShotTarget shot))
        {
            slot.NextFireTime = Time.time + Random.Range(minAttackCooldown, maxAttackCooldown) * 0.5f;
            return;
        }

        if (faceTargetWhileAiming)
        {
            FaceArcherToward(slot.Transform, shot.AimPoint);
        }

        FireRegimentArrow(slot.Transform, shot);
        slot.NextFireTime = Time.time + Random.Range(minAttackCooldown, maxAttackCooldown);
    }

    private bool TryChooseRegimentShot(Vector3 archerPosition, out ShotTarget shot)
    {
        shot = default;

        TroopCombat regiment = cachedRegimentTarget;
        if (regiment == null)
        {
            regiment = FindBestFriendlyRegimentTarget();
        }

        if (regiment == null || !IsRegimentInRange(archerPosition, regiment))
        {
            return false;
        }

        Vector3 regimentCenter = regiment.transform.position;
        Vector2 spread = Random.insideUnitCircle * regimentShotSpreadRadius;
        Vector3 aimPoint = regimentCenter + new Vector3(spread.x, 0f, spread.y);
        shot = new ShotTarget(aimPoint, regiment, attackDamage);
        return true;
    }

    private void FireRegimentArrow(Transform archerSlot, ShotTarget shot)
    {
        if (arrowPrefab == null)
        {
            ApplyRegimentDamage(shot);
            return;
        }

        Vector3 launchPoint = archerSlot.position;
        launchPoint.y += projectileLaunchHeight;

        TroopRangedProjectile.Launch(
            arrowPrefab,
            launchPoint,
            shot.AimPoint,
            projectileSpeed,
            projectileArcHeight);

        ApplyRegimentDamage(shot);
    }

    private static void ApplyRegimentDamage(ShotTarget shot)
    {
        if (shot.RegimentTarget == null || shot.RegimentTarget.CurrentState == TroopCombat.State.Dead)
        {
            return;
        }

        if (shot.DamageAmount <= 0f)
        {
            return;
        }

        shot.RegimentTarget.TakeDamage(shot.DamageAmount, null, true);
    }

    private TroopCombat FindBestFriendlyRegimentTarget()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, attackRange, targetLayers, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
        {
            return null;
        }

        TroopCombat bestTarget = null;
        float bestDistanceSqr = float.MaxValue;
        Vector3 origin = transform.position;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null)
            {
                continue;
            }

            TroopCombat regiment = hit.GetComponentInParent<TroopCombat>();
            if (!IsValidRegimentTarget(regiment))
            {
                continue;
            }

            float distanceSqr = GetHorizontalDistanceSqr(origin, regiment.transform.position);
            if (distanceSqr < bestDistanceSqr)
            {
                bestDistanceSqr = distanceSqr;
                bestTarget = regiment;
            }
        }

        return bestTarget;
    }

    private bool IsValidRegimentTarget(TroopCombat regiment)
    {
        if (regiment == null || regiment.TroopFaction != TroopCombat.Faction.Friendly)
        {
            return false;
        }

        if (regiment.CurrentState == TroopCombat.State.Dead || regiment.IsRegrouping)
        {
            return false;
        }

        if (ignoreFriendliesInCamp)
        {
            RtsCampManager campManager = RtsCampManager.Instance;
            if (campManager != null
                && campManager.IsInCampZone(regiment.transform.position, TroopCombat.Faction.Friendly))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsRegimentInRange(Vector3 archerPosition, TroopCombat regiment)
    {
        if (regiment == null)
        {
            return false;
        }

        return GetHorizontalDistance(archerPosition, regiment.transform.position) <= attackRange + 0.01f;
    }

    private Transform ResolvePlayerTarget()
    {
        if (playerTarget != null)
        {
            return playerTarget;
        }

        if (vGear.head != null)
        {
            return vGear.head.transform;
        }

        Camera mainCamera = Camera.main;
        return mainCamera != null ? mainCamera.transform : null;
    }

    private static void FaceArcherToward(Transform archerSlot, Vector3 targetPosition)
    {
        Vector3 direction = targetPosition - archerSlot.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        archerSlot.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private static float GetHorizontalDistance(Vector3 a, Vector3 b)
    {
        return Mathf.Sqrt(GetHorizontalDistanceSqr(a, b));
    }

    private static float GetHorizontalDistanceSqr(Vector3 a, Vector3 b)
    {
        Vector3 offset = a - b;
        offset.y = 0f;
        return offset.sqrMagnitude;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos)
        {
            return;
        }

        Gizmos.color = rangeGizmoColor;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }

    private readonly struct ShotTarget
    {
        public ShotTarget(Vector3 aimPoint, TroopCombat regimentTarget, float damageAmount)
        {
            AimPoint = aimPoint;
            RegimentTarget = regimentTarget;
            DamageAmount = damageAmount;
        }

        public Vector3 AimPoint { get; }
        public TroopCombat RegimentTarget { get; }
        public float DamageAmount { get; }
    }

    private class ArcherSlotState
    {
        public Transform Transform;
        public float NextFireTime;
    }
}
