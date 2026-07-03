using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class TroopCombat : MonoBehaviour
{
    public enum Faction
    {
        Friendly,
        Enemy
    }

    public enum State
    {
        Idle,
        Fight,
        Dead
    }

    [Header("Identity")]
    [SerializeField] private Faction faction = Faction.Friendly;

    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private bool destroyOnDeath = true;

    [Header("Combat")]
    [SerializeField] private float attackDamage = 10f;
    [SerializeField] private float attackRange = 2.5f;
    [SerializeField] private float attackCooldown = 1.25f;
    [SerializeField] private float targetScanInterval = 0.2f;
    [SerializeField] private LayerMask targetLayers = ~0;
    [SerializeField] private bool faceTargetWhileFighting = true;

    [Header("Regiment Visuals")]
    [SerializeField] private GameObject troopPrefab;
    [SerializeField, Min(0)] private int maxUnitCount = 12;
    [SerializeField, Range(0f, 1f)] private float defeatedUnitPercentage = 0.4f;
    [SerializeField] private Transform troopVisualRoot;
    [SerializeField] private bool keepTroopVisualScaleIndependent = true;
    [SerializeField, Min(0.01f)] private float formationCellSpacing = 0.5f;
    [SerializeField, Range(0f, 0.5f)] private float formationJitterFraction = 0.2f;
    [SerializeField] private bool randomizeSpawnOrder = true;

    [Header("Debug Gizmos")]
    [SerializeField] private bool drawDebugGizmos = true;
    [SerializeField] private Transform debugAnchor;
    [SerializeField] private float debugLabelHeight = 2.2f;
    [SerializeField] private float debugHealthBarWidth = 1.2f;
    [SerializeField] private float debugHealthBarHeight = 0.08f;
    [SerializeField] private Color friendlyGizmoColor = new Color(0.2f, 0.8f, 0.35f, 1f);
    [SerializeField] private Color enemyGizmoColor = new Color(0.9f, 0.25f, 0.2f, 1f);
    [SerializeField] private Color idleGizmoColor = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private Color fightGizmoColor = new Color(1f, 0.82f, 0.2f, 1f);
    [SerializeField] private Color deadGizmoColor = new Color(0.4f, 0.4f, 0.4f, 1f);

    private RtsUnitMotor motor;
    private TroopCombat currentTarget;
    private float currentHealth;
    private float nextAttackTime;
    private float nextScanTime;
    private Vector3 troopPrefabScale = Vector3.one;
    private readonly List<TroopVisualInstance> troopVisuals = new List<TroopVisualInstance>();
    private int activeTroopVisualCount;

    public Faction TroopFaction => faction;
    public State CurrentState { get; private set; } = State.Idle;
    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public float AttackRange => attackRange;
    public float HealthNormalized => Mathf.Clamp01(currentHealth / Mathf.Max(1f, maxHealth));
    public int MaxUnitCount => maxUnitCount;
    public int ActiveUnitCount => activeTroopVisualCount;
    public int MinimumUnitCountAtDefeat => Mathf.RoundToInt(maxUnitCount * defeatedUnitPercentage);

    private void Awake()
    {
        motor = GetComponent<RtsUnitMotor>();
        currentHealth = Mathf.Max(1f, maxHealth);
        CacheTroopPrefabScale();
        EnsureTroopVisuals();
        ApplyTroopVisualFormation();
    }

    private void OnValidate()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        attackDamage = Mathf.Max(0f, attackDamage);
        attackRange = Mathf.Max(0f, attackRange);
        attackCooldown = Mathf.Max(0.05f, attackCooldown);
        targetScanInterval = Mathf.Max(0.05f, targetScanInterval);
        maxUnitCount = Mathf.Max(0, maxUnitCount);
        defeatedUnitPercentage = Mathf.Clamp01(defeatedUnitPercentage);
        formationCellSpacing = Mathf.Max(0.01f, formationCellSpacing);
        formationJitterFraction = Mathf.Clamp(formationJitterFraction, 0f, 0.5f);
    }

    private void Update()
    {
        if (CurrentState == State.Dead)
        {
            return;
        }

        SyncTroopVisualScale();

        if (Time.time >= nextScanTime)
        {
            nextScanTime = Time.time + Mathf.Max(0.05f, targetScanInterval);
            currentTarget = FindTargetInRange();
        }

        if (currentTarget == null)
        {
            CurrentState = State.Idle;
            return;
        }

        float rangeSqr = attackRange * attackRange;
        Vector3 toTarget = currentTarget.transform.position - transform.position;
        toTarget.y = 0f;

        if (toTarget.sqrMagnitude > rangeSqr)
        {
            currentTarget = null;
            CurrentState = State.Idle;
            return;
        }

        CurrentState = State.Fight;

        if (faceTargetWhileFighting && toTarget.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 8f * Time.deltaTime);
        }

        if (Time.time < nextAttackTime)
        {
            return;
        }

        nextAttackTime = Time.time + Mathf.Max(0.05f, attackCooldown);
        currentTarget.TakeDamage(attackDamage, this);
    }

    public void TakeDamage(float amount, TroopCombat attacker = null)
    {
        if (CurrentState == State.Dead)
        {
            return;
        }

        currentHealth = Mathf.Max(0f, currentHealth - Mathf.Max(0f, amount));

        if (troopVisuals.Count > 0)
        {
            ApplyCasualtyLoss();
        }

        if (currentHealth <= 0f)
        {
            Die(attacker);
        }
    }

    public bool CanBeTargetedBy(TroopCombat other)
    {
        if (other == null || CurrentState == State.Dead)
        {
            return false;
        }

        return faction != other.faction;
    }

    private TroopCombat FindTargetInRange()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, attackRange, targetLayers, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
        {
            return null;
        }

        TroopCombat bestTarget = null;
        float bestDistanceSqr = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null)
            {
                continue;
            }

            TroopCombat candidate = hit.GetComponentInParent<TroopCombat>();
            if (candidate == null || candidate == this || !candidate.CanBeTargetedBy(this))
            {
                continue;
            }

            Vector3 offset = candidate.transform.position - transform.position;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;

            if (distanceSqr < bestDistanceSqr)
            {
                bestDistanceSqr = distanceSqr;
                bestTarget = candidate;
            }
        }

        return bestTarget;
    }

    private void Die(TroopCombat attacker)
    {
        CurrentState = State.Dead;
        currentTarget = null;

        if (motor != null)
        {
            motor.Stop();
        }

        // SyncTroopVisualsToHealth(forceRefresh: true);

        if (destroyOnDeath && troopPrefab == null)
        {
            Destroy(gameObject);
        }
        else
        {
            nextAttackTime = 0f;
        }
    }

    private void EnsureTroopVisuals()
    {
        if (troopPrefab == null || maxUnitCount <= 0)
        {
            return;
        }

        Transform parent = troopVisualRoot != null ? troopVisualRoot : transform;
        bool layoutChanged = false;

        while (troopVisuals.Count < maxUnitCount)
        {
            GameObject instance = Instantiate(troopPrefab);
            instance.transform.SetParent(parent, true);
            instance.name = troopPrefab.name + "_" + (troopVisuals.Count + 1);
            ApplyTroopVisualScale(instance.transform);

            troopVisuals.Add(new TroopVisualInstance
            {
                Instance = instance,
                SlotIndex = randomizeSpawnOrder ? troopVisuals.Count : troopVisuals.Count
            });

            layoutChanged = true;
        }

        while (troopVisuals.Count > maxUnitCount)
        {
            int lastIndex = troopVisuals.Count - 1;
            TroopVisualInstance troopVisual = troopVisuals[lastIndex];
            troopVisuals.RemoveAt(lastIndex);

            if (troopVisual != null && troopVisual.Instance != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(troopVisual.Instance);
                }
                else
                {
                    DestroyImmediate(troopVisual.Instance);
                }
            }

            layoutChanged = true;
        }

        if (randomizeSpawnOrder)
        {
            ShuffleTroopVisualSlots();
            layoutChanged = true;
        }

        if (layoutChanged)
        {
            ApplyTroopVisualFormation();
        }
    }

    private void ShuffleTroopVisualSlots()
    {
        for (int i = 0; i < troopVisuals.Count; i++)
        {
            int swapIndex = Random.Range(i, troopVisuals.Count);
            if (swapIndex == i)
            {
                continue;
            }

            TroopVisualInstance temp = troopVisuals[i];
            troopVisuals[i] = troopVisuals[swapIndex];
            troopVisuals[swapIndex] = temp;
        }

        for (int i = 0; i < troopVisuals.Count; i++)
        {
            troopVisuals[i].SlotIndex = i;
        }
    }

    private void ApplyCasualtyLoss()
    {
        int activeCount = 0;
        for (int i = 0; i < troopVisuals.Count; i++)
        {
            TroopVisualInstance troopVisual = troopVisuals[i];
            if (troopVisual.Instance != null && troopVisual.Instance.activeSelf)
            {
                activeCount++;
            }
        }

        int minimumUnitCount = Mathf.Clamp(Mathf.RoundToInt(maxUnitCount * defeatedUnitPercentage), 0, maxUnitCount);
        int targetActiveCount = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(minimumUnitCount, maxUnitCount, HealthNormalized)), minimumUnitCount, maxUnitCount);

        if (activeCount <= targetActiveCount)
        {
            activeTroopVisualCount = activeCount;
            return;
        }

        List<int> candidates = new List<int>();
        for (int i = 0; i < troopVisuals.Count; i++)
        {
            TroopVisualInstance troopVisual = troopVisuals[i];
            if (troopVisual.Instance != null && troopVisual.Instance.activeSelf)
            {
                candidates.Add(i);
            }
        }

        int lossesNeeded = Mathf.Min(activeCount - targetActiveCount, candidates.Count);
        for (int i = 0; i < lossesNeeded; i++)
        {
            int pickedIndex = Random.Range(i, candidates.Count);
            int troopIndex = candidates[pickedIndex];
            candidates[pickedIndex] = candidates[i];
            candidates[i] = troopIndex;

            TroopVisualInstance troopVisual = troopVisuals[troopIndex];
            if (troopVisual.Instance != null)
            {
                troopVisual.Instance.SetActive(false);
            }
        }

        activeTroopVisualCount = activeCount - lossesNeeded;
    }

    private void ApplyTroopVisualFormation()
    {
        if (troopVisuals.Count == 0)
        {
            return;
        }

        int gridColumns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(maxUnitCount)));
        int gridRows = Mathf.Max(1, Mathf.CeilToInt((float)maxUnitCount / gridColumns));
        Vector2 gridCenter = new Vector2((gridColumns - 1) * 0.5f, (gridRows - 1) * 0.5f);
        float jitterAmount = formationCellSpacing * formationJitterFraction;

        for (int i = 0; i < troopVisuals.Count; i++)
        {
            TroopVisualInstance troopVisual = troopVisuals[i];
            if (troopVisual.Instance == null)
            {
                continue;
            }

            if (!troopVisual.Instance.activeSelf)
            {
                continue;
            }

            Vector3 localPosition = GetFormationPosition(troopVisual.SlotIndex, gridColumns, gridRows, gridCenter, jitterAmount);
            ApplyTroopVisualLocalPosition(troopVisual.Instance.transform, localPosition);
            troopVisual.Instance.transform.localRotation = Quaternion.identity;
            ApplyTroopVisualScale(troopVisual.Instance.transform);
        }
    }

    private void SyncTroopVisualScale()
    {
        if (!keepTroopVisualScaleIndependent || troopVisuals.Count == 0)
        {
            return;
        }

        for (int i = 0; i < troopVisuals.Count; i++)
        {
            TroopVisualInstance troopVisual = troopVisuals[i];
            if (troopVisual.Instance == null)
            {
                continue;
            }

            ApplyTroopVisualScale(troopVisual.Instance.transform);
        }
    }

    private void ApplyTroopVisualScale(Transform visualTransform)
    {
        if (visualTransform == null || !keepTroopVisualScaleIndependent)
        {
            return;
        }

        Transform parent = visualTransform.parent;
        if (parent == null)
        {
            visualTransform.localScale = troopPrefabScale;
            return;
        }

        Vector3 parentScale = parent.lossyScale;
        visualTransform.localScale = new Vector3(
            SafeDivide(troopPrefabScale.x, parentScale.x),
            SafeDivide(troopPrefabScale.y, parentScale.y),
            SafeDivide(troopPrefabScale.z, parentScale.z));
    }

    private void ApplyTroopVisualLocalPosition(Transform visualTransform, Vector3 desiredWorldOffset)
    {
        if (visualTransform == null)
        {
            return;
        }

        Transform parent = visualTransform.parent;
        if (parent == null)
        {
            visualTransform.localPosition = desiredWorldOffset;
            return;
        }

        Vector3 parentScale = parent.lossyScale;
        visualTransform.localPosition = new Vector3(
            SafeDivide(desiredWorldOffset.x, parentScale.x),
            SafeDivide(desiredWorldOffset.y, parentScale.y),
            SafeDivide(desiredWorldOffset.z, parentScale.z));
    }

    private void CacheTroopPrefabScale()
    {
        if (troopPrefab == null)
        {
            troopPrefabScale = Vector3.one;
            return;
        }

        troopPrefabScale = troopPrefab.transform.localScale;
    }

    private static float SafeDivide(float numerator, float denominator)
    {
        if (Mathf.Abs(denominator) < 0.0001f)
        {
            return numerator;
        }

        return numerator / denominator;
    }

    private Vector3 GetFormationPosition(int slotIndex, int gridColumns, int gridRows, Vector2 gridCenter, float jitterAmount)
    {
        int column = Mathf.Clamp(slotIndex % gridColumns, 0, gridColumns - 1);
        int row = Mathf.Clamp(slotIndex / gridColumns, 0, gridRows - 1);

        float x = (column - gridCenter.x) * formationCellSpacing;
        float z = (gridCenter.y - row) * formationCellSpacing;

        Vector2 jitter = GetDeterministicJitter(slotIndex) * jitterAmount;
        Vector3 localPosition = new Vector3(x + jitter.x, 0f, z + jitter.y);

        float halfWidth = (gridColumns - 1) * 0.5f * formationCellSpacing;
        float halfDepth = (gridRows - 1) * 0.5f * formationCellSpacing;
        localPosition.x = Mathf.Clamp(localPosition.x, -halfWidth, halfWidth);
        localPosition.z = Mathf.Clamp(localPosition.z, -halfDepth, halfDepth);

        return localPosition;
    }

    private Vector2 GetDeterministicJitter(int seed)
    {
        float xNoise = Mathf.Sin((seed + 1) * 12.9898f) * 43758.5453f;
        float yNoise = Mathf.Sin((seed + 1) * 78.233f + 19.19f) * 43758.5453f;

        return new Vector2(RepeatSigned01(xNoise), RepeatSigned01(yNoise));
    }

    private static float RepeatSigned01(float value)
    {
        float repeat = value - Mathf.Floor(value);
        return repeat * 2f - 1f;
    }

    private void OnDrawGizmos()
    {
        if (!drawDebugGizmos)
        {
            return;
        }

        Vector3 anchor = GetDebugAnchorPosition();
        Color teamColor = GetTeamColor();
        Color stateColor = GetStateColor();
        Color combinedColor = Color.Lerp(teamColor, stateColor, 0.45f);

        Gizmos.color = combinedColor;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        DrawHealthGizmo(anchor, combinedColor);

#if UNITY_EDITOR
        string debugText = BuildDebugLabel();
        if (!string.IsNullOrEmpty(debugText))
        {
            GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = combinedColor }
            };
            Handles.Label(anchor + Vector3.up * debugLabelHeight, debugText, style);
        }
#endif
    }

    private void DrawHealthGizmo(Vector3 anchor, Color tint)
    {
        float clampedMaxHealth = Mathf.Max(1f, maxHealth);
        float healthRatio = Mathf.Clamp01(currentHealth / clampedMaxHealth);

        Vector3 left = anchor + Vector3.left * (debugHealthBarWidth * 0.5f);
        Vector3 right = anchor + Vector3.right * (debugHealthBarWidth * 0.5f);
        Vector3 barTop = Vector3.up * debugLabelHeight;
        Vector3 barBottom = barTop + Vector3.up * debugHealthBarHeight;

        Gizmos.color = new Color(0f, 0f, 0f, 0.6f);
        Gizmos.DrawLine(left + barTop, right + barTop);
        Gizmos.DrawLine(left + barBottom, right + barBottom);
        Gizmos.DrawLine(left + barTop, left + barBottom);
        Gizmos.DrawLine(right + barTop, right + barBottom);

        Vector3 fillRight = Vector3.Lerp(left + barTop, right + barTop, healthRatio);
        Gizmos.color = Color.Lerp(deadGizmoColor, tint, healthRatio);
        Gizmos.DrawLine(left + barTop, fillRight);
    }

    private Vector3 GetDebugAnchorPosition()
    {
        if (debugAnchor != null)
        {
            return debugAnchor.position;
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers != null && renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                Renderer rendererRef = renderers[i];
                if (rendererRef != null)
                {
                    bounds.Encapsulate(rendererRef.bounds);
                }
            }

            return bounds.center + Vector3.up * bounds.extents.y;
        }

        Collider[] colliders = GetComponentsInChildren<Collider>();
        if (colliders != null && colliders.Length > 0)
        {
            Bounds bounds = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++)
            {
                Collider colliderRef = colliders[i];
                if (colliderRef != null)
                {
                    bounds.Encapsulate(colliderRef.bounds);
                }
            }

            return bounds.center + Vector3.up * bounds.extents.y;
        }

        return transform.position + Vector3.up * debugLabelHeight;
    }

    private Color GetTeamColor()
    {
        return faction == Faction.Friendly ? friendlyGizmoColor : enemyGizmoColor;
    }

    private Color GetStateColor()
    {
        switch (CurrentState)
        {
            case State.Fight:
                return fightGizmoColor;
            case State.Dead:
                return deadGizmoColor;
            default:
                return idleGizmoColor;
        }
    }

    private string BuildDebugLabel()
    {
        float clampedMaxHealth = Mathf.Max(1f, maxHealth);
        float healthPercent = Mathf.Clamp01(currentHealth / clampedMaxHealth) * 100f;
        string targetName = currentTarget != null ? currentTarget.name : "none";

        return faction + " | " + CurrentState + " | HP " + currentHealth.ToString("F0") + "/" + clampedMaxHealth.ToString("F0") + " (" + healthPercent.ToString("F0") + "%)" + " | Units " + activeTroopVisualCount + "/" + maxUnitCount + " (min " + MinimumUnitCountAtDefeat + ")" + " | ATK " + attackDamage.ToString("F0") + " | RNG " + attackRange.ToString("F1") + " | Target " + targetName;
    }

    [System.Serializable]
    private class TroopVisualInstance
    {
        public GameObject Instance;
        public int SlotIndex;
    }
}