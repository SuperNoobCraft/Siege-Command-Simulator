using System.Collections.Generic;
using UnityEngine;

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
    [Tooltip("Optional override. Defaults to the tracked Votanic head, then main camera.")]
    [SerializeField] private Transform playerTarget;
    [Tooltip("Minimum seconds before the next player-shot roll is allowed.")]
    [SerializeField, Min(0.5f)] private float playerHarassmentMinTimeGap = 18f;
    [Tooltip("Chance to fire at the command tower once the time gap has elapsed.")]
    [SerializeField, Range(0f, 1f)] private float playerHarassmentShotChance = 0.35f;
    [Tooltip("Shortest possible gap late in the match after scaling is applied.")]
    [SerializeField, Min(0.5f)] private float playerHarassmentMinScaledTimeGap = 8f;
    [Tooltip("Highest possible shot chance late in the match after scaling is applied.")]
    [SerializeField, Range(0f, 1f)] private float playerHarassmentMaxScaledChance = 0.75f;
    [Tooltip("Fallback only if SiegeGameManager is missing. Normally driven by total seconds until cannons fire.")]
    [SerializeField, Min(1f)] private float matchDurationReferenceSeconds = 180f;
    [Tooltip("How strongly the gap shrinks from min to scaled minimum by match end.")]
    [SerializeField, Range(0f, 2f)] private float gapScaleStrength = 1f;
    [Tooltip("How strongly the shot chance rises from base to max by match end.")]
    [SerializeField, Range(0f, 2f)] private float chanceScaleStrength = 1f;
    [SerializeField, Min(0f)] private float playerShotSpreadRadius = 1.75f;
    [SerializeField] private LayerMask playerHitLayers = ~0;
    [SerializeField] private bool enablePlayerShotOutline = true;
    [SerializeField] private Color playerShotOutlineColor = new Color(1f, 0.12f, 0.12f, 1f);
    [SerializeField, Min(1f)] private float playerShotOutlineScale = 1.14f;
    [Tooltip("Radius of the runtime sphere used to detect commander hits. Arrows do not need prefab colliders.")]
    [SerializeField, Min(0.01f)] private float playerHazardHitRadius = 0.1f;

    [Header("Dodge Arrows Mode")]
    [Tooltip("Regiment targeting is disabled. Min Time Gap is the global interval between volleys (not per archer). Archers Per Volley is the maximum archers that may fire each interval (actual count is random from 1 to that max).")]
    [SerializeField] private PlayerHarassmentProfile dodgeArrowsHarassment = new PlayerHarassmentProfile
    {
        minTimeGap = 2f,
        shotChance = 1f,
        minScaledTimeGap = 1.25f,
        maxScaledChance = 1f,
        matchDurationReferenceSeconds = 30f,
        gapScaleStrength = 1f,
        chanceScaleStrength = 0.5f,
        shotSpreadRadius = 1.25f,
        archersPerVolley = 2,
        firstShotDelay = 0.75f
    };
    [Tooltip("Optional projectile overrides for Dodge Arrows. Leave speeds at zero to reuse the main projectile settings.")]
    [SerializeField] private DodgeArrowsProjectileProfile dodgeArrowsProjectile = new DodgeArrowsProjectileProfile
    {
        speed = 24f,
        arcHeight = 2.5f,
        launchHeight = 1.15f
    };

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
    private float nextPlayerTargetBindTime;
    private float matchStartTime;
    private TroopCombat cachedRegimentTarget;

    private void Start()
    {
        matchStartTime = Time.time;
        CacheArcherSlots();
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
            health.RegisterHitRoot(player, includeChildColliders: false);
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
        playerShotOutlineScale = Mathf.Max(1f, playerShotOutlineScale);
        playerHazardHitRadius = Mathf.Max(0.01f, playerHazardHitRadius);
        projectileSpeed = Mathf.Max(0.1f, projectileSpeed);
        projectileArcHeight = Mathf.Max(0f, projectileArcHeight);
        projectileLaunchHeight = Mathf.Max(0f, projectileLaunchHeight);
        ValidateHarassmentProfile(dodgeArrowsHarassment);
        dodgeArrowsProjectile.Validate();
    }

    private void Update()
    {
        if (slots.Count == 0)
        {
            return;
        }

        TryRefreshPlayerHitBinding();

        if (IsDodgeArrowsMode())
        {
            UpdateDodgeArcherSlots();
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
        if (!ShouldHarassCommander())
        {
            return;
        }

        if (SiegeCommanderArrowHealth.Instance != null && SiegeCommanderArrowHealth.Instance.IsDefeated)
        {
            return;
        }

        if (Time.time < nextPlayerHarassmentRollTime)
        {
            return;
        }

        float shotChance = GetActiveHarassmentProfile().GetScaledShotChance(GetMatchProgress());
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

        int volleyCount = Mathf.Clamp(GetActiveHarassmentProfile().archersPerVolley, 1, slots.Count);
        FirePlayerHarassmentVolley(player, volleyCount);
    }

    private void UpdateDodgeArcherSlots()
    {
        if (!ShouldHarassCommander())
        {
            return;
        }

        if (SiegeCommanderArrowHealth.Instance != null && SiegeCommanderArrowHealth.Instance.IsDefeated)
        {
            return;
        }

        if (Time.time < nextPlayerHarassmentRollTime)
        {
            return;
        }

        ScheduleNextDodgeVolley();

        Transform player = ResolvePlayerTarget();
        if (player == null)
        {
            return;
        }

        int maxVolley = Mathf.Clamp(dodgeArrowsHarassment.archersPerVolley, 1, slots.Count);
        FirePlayerHarassmentVolley(player, maxVolley, randomizeVolleySize: true);
    }

    private void ScheduleNextDodgeVolley()
    {
        nextPlayerHarassmentRollTime = Time.time + dodgeArrowsHarassment.GetScaledTimeGap(GetMatchProgress());
    }

    private void FirePlayerHarassmentVolley(Transform player, int maxArcherCount, bool randomizeVolleySize = false)
    {
        if (player == null || slots.Count == 0 || maxArcherCount <= 0)
        {
            return;
        }

        List<int> available = new List<int>(slots.Count);
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].Transform != null)
            {
                available.Add(i);
            }
        }

        if (available.Count == 0)
        {
            return;
        }

        int cappedMax = Mathf.Min(maxArcherCount, available.Count);
        int shots = randomizeVolleySize ? Random.Range(1, cappedMax + 1) : cappedMax;
        for (int shotIndex = 0; shotIndex < shots; shotIndex++)
        {
            int pick = Random.Range(0, available.Count);
            int slotIndex = available[pick];
            available.RemoveAt(pick);
            FirePlayerHazardShot(slots[slotIndex], player);
        }
    }

    private bool ShouldHarassCommander()
    {
        SiegeGameManager manager = SiegeGameManager.Instance;
        return manager != null && manager.IsPlaying;
    }

    private void TryRefreshPlayerHitBinding()
    {
        if (Time.time < nextPlayerTargetBindTime)
        {
            return;
        }

        nextPlayerTargetBindTime = Time.time + 1f;
        RegisterPlayerHitRig();
    }

    private void ScheduleNextPlayerHarassmentRoll()
    {
        nextPlayerHarassmentRollTime = Time.time + GetActiveHarassmentProfile().GetScaledTimeGap(GetMatchProgress());
    }

    private PlayerHarassmentProfile GetActiveHarassmentProfile()
    {
        return IsDodgeArrowsMode() ? dodgeArrowsHarassment : regimentHarassmentProfile;
    }

    private bool IsDodgeArrowsMode()
    {
        return SiegeMatchSettings.IsDodgeArrowsMode;
    }

    private float GetProjectileSpeed()
    {
        if (IsDodgeArrowsMode() && dodgeArrowsProjectile.speed > 0f)
        {
            return dodgeArrowsProjectile.speed;
        }

        return projectileSpeed;
    }

    private float GetProjectileArcHeight()
    {
        if (IsDodgeArrowsMode() && dodgeArrowsProjectile.arcHeight >= 0f && dodgeArrowsProjectile.useOverrides)
        {
            return dodgeArrowsProjectile.arcHeight;
        }

        return projectileArcHeight;
    }

    private float GetProjectileLaunchHeight()
    {
        if (IsDodgeArrowsMode() && dodgeArrowsProjectile.launchHeight > 0f)
        {
            return dodgeArrowsProjectile.launchHeight;
        }

        return projectileLaunchHeight;
    }

    private float GetActivePlayerShotSpreadRadius()
    {
        if (IsDodgeArrowsMode())
        {
            return dodgeArrowsHarassment.shotSpreadRadius;
        }

        return playerShotSpreadRadius;
    }

    private PlayerHarassmentProfile regimentHarassmentProfile => new PlayerHarassmentProfile
    {
        minTimeGap = playerHarassmentMinTimeGap,
        shotChance = playerHarassmentShotChance,
        minScaledTimeGap = playerHarassmentMinScaledTimeGap,
        maxScaledChance = playerHarassmentMaxScaledChance,
        matchDurationReferenceSeconds = matchDurationReferenceSeconds,
        gapScaleStrength = gapScaleStrength,
        chanceScaleStrength = chanceScaleStrength,
        shotSpreadRadius = playerShotSpreadRadius
    };

    private float GetMatchProgress()
    {
        float elapsed = GetMatchElapsedSeconds();
        float reference = GetMatchDurationReferenceSeconds();
        return Mathf.Clamp01(elapsed / reference);
    }

    private float GetMatchDurationReferenceSeconds()
    {
        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager != null)
        {
            return Mathf.Max(1f, manager.TotalSecondsUntilCannonsFire);
        }

        PlayerHarassmentProfile profile = GetActiveHarassmentProfile();
        return Mathf.Max(1f, profile.matchDurationReferenceSeconds);
    }

    public void ResetForMatchStart()
    {
        matchStartTime = Time.time;
        nextPlayerTargetBindTime = 0f;
        nextScanTime = 0f;
        cachedRegimentTarget = null;
        CacheArcherSlots();

        if (IsDodgeArrowsMode())
        {
            nextPlayerHarassmentRollTime = Time.time + Mathf.Max(0f, dodgeArrowsHarassment.firstShotDelay);
            return;
        }

        nextPlayerHarassmentRollTime = 0f;
        ScheduleNextPlayerHarassmentRoll();
        ScheduleRegimentArcherCooldowns();
    }

    private void ScheduleRegimentArcherCooldowns()
    {
        float maxCooldown = Mathf.Max(minAttackCooldown, maxAttackCooldown);
        for (int i = 0; i < slots.Count; i++)
        {
            slots[i].NextFireTime = Time.time + Random.Range(0f, maxCooldown);
        }
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

    private static void ValidateHarassmentProfile(PlayerHarassmentProfile profile)
    {
        profile.minTimeGap = Mathf.Max(0.5f, profile.minTimeGap);
        profile.minScaledTimeGap = Mathf.Max(0.5f, profile.minScaledTimeGap);
        profile.minScaledTimeGap = Mathf.Min(profile.minScaledTimeGap, profile.minTimeGap);
        profile.maxScaledChance = Mathf.Clamp01(profile.maxScaledChance);
        profile.shotChance = Mathf.Clamp01(profile.shotChance);
        profile.matchDurationReferenceSeconds = Mathf.Max(1f, profile.matchDurationReferenceSeconds);
        profile.gapScaleStrength = Mathf.Clamp(profile.gapScaleStrength, 0f, 2f);
        profile.chanceScaleStrength = Mathf.Clamp(profile.chanceScaleStrength, 0f, 2f);
        profile.shotSpreadRadius = Mathf.Max(0f, profile.shotSpreadRadius);
        profile.archersPerVolley = Mathf.Max(1, profile.archersPerVolley);
        profile.firstShotDelay = Mathf.Max(0f, profile.firstShotDelay);
    }

    private void FirePlayerHazardShot(ArcherSlotState archer, Transform player)
    {
        if (archer.Transform == null)
        {
            return;
        }

        Vector2 spreadXZ = Random.insideUnitCircle * GetActivePlayerShotSpreadRadius();
        Vector3 aimPoint = player.position + new Vector3(spreadXZ.x, 0f, spreadXZ.y);

        Vector3 launchPoint = archer.Transform.position;
        launchPoint.y += GetProjectileLaunchHeight();
        if (!IsValidPosition(launchPoint) || !IsValidPosition(aimPoint))
        {
            return;
        }

        if (faceTargetWhileAiming)
        {
            FaceArcherToward(archer.Transform, aimPoint);
        }

        if (arrowPrefab == null)
        {
            return;
        }

        TroopRangedProjectile.LaunchPlayerHazard(
            arrowPrefab,
            launchPoint,
            aimPoint,
            GetProjectileSpeed(),
            GetProjectileArcHeight(),
            playerHitLayers,
            playerHazardHitRadius,
            enablePlayerShotOutline,
            playerShotOutlineColor,
            playerShotOutlineScale);
    }

    private void CacheArcherSlots()
    {
        slots.Clear();

        List<Transform> resolvedSlots = new List<Transform>();
        if (archerSlots != null)
        {
            for (int i = 0; i < archerSlots.Length; i++)
            {
                Transform slot = archerSlots[i];
                if (slot != null && !resolvedSlots.Contains(slot))
                {
                    resolvedSlots.Add(slot);
                }
            }
        }

        if (autoAssignChildArchers)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child != null && !resolvedSlots.Contains(child))
                {
                    resolvedSlots.Add(child);
                }
            }
        }

        if (resolvedSlots.Count == 0)
        {
            return;
        }

        for (int i = 0; i < resolvedSlots.Count; i++)
        {
            slots.Add(new ArcherSlotState
            {
                Transform = resolvedSlots[i],
                NextFireTime = float.MaxValue
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

        return SiegePlayEnvironment.ResolvePlayerTransform();
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

    private static bool IsValidPosition(Vector3 position)
    {
        return float.IsFinite(position.x)
            && float.IsFinite(position.y)
            && float.IsFinite(position.z);
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

    [System.Serializable]
    private class PlayerHarassmentProfile
    {
        [Min(0.5f)] public float minTimeGap = 18f;
        [Range(0f, 1f)] public float shotChance = 0.35f;
        [Min(0.5f)] public float minScaledTimeGap = 8f;
        [Range(0f, 1f)] public float maxScaledChance = 0.75f;
        [Min(1f)] public float matchDurationReferenceSeconds = 180f;
        [Range(0f, 2f)] public float gapScaleStrength = 1f;
        [Range(0f, 2f)] public float chanceScaleStrength = 1f;
        [Min(0f)] public float shotSpreadRadius = 1.75f;
        [Min(1)] public int archersPerVolley = 1;
        [Min(0f)] public float firstShotDelay = 0.75f;

        public float GetScaledTimeGap(float matchProgress)
        {
            float scale = Mathf.Clamp01(matchProgress * gapScaleStrength);
            return Mathf.Lerp(minTimeGap, minScaledTimeGap, scale);
        }

        public float GetScaledShotChance(float matchProgress)
        {
            float scale = Mathf.Clamp01(matchProgress * chanceScaleStrength);
            return Mathf.Lerp(shotChance, maxScaledChance, scale);
        }
    }

    [System.Serializable]
    private class DodgeArrowsProjectileProfile
    {
        public bool useOverrides = true;
        [Min(0f)] public float speed;
        [Min(0f)] public float arcHeight = 2.5f;
        [Min(0f)] public float launchHeight = 1.15f;

        public void Validate()
        {
            speed = Mathf.Max(0f, speed);
            arcHeight = Mathf.Max(0f, arcHeight);
            launchHeight = Mathf.Max(0f, launchHeight);
        }
    }
}
