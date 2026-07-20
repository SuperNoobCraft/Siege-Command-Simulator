using System.Collections;
using UnityEngine;

/// <summary>
/// Fatal fall when the commander walks off the command tower platform during play.
/// Assign the tower floor/trigger collider (or leave empty to use <see cref="SiegePlayerBoundary"/>).
/// In CAVE the Votanic user root is never moved or rotated (walls must stay aligned).
/// </summary>
[DefaultExecutionOrder(210)]
public class SiegeCommandTowerFallDeath : MonoBehaviour
{
    [Header("Tower Bounds")]
    [Tooltip("Trigger or solid collider for the walkable command tower. If empty, uses a SiegePlayerBoundary on this object or in the scene.")]
    [SerializeField] private Collider towerFloorCollider;
    [SerializeField] private SiegePlayerBoundary playerBoundary;
    [Tooltip("Shrinks the safe area slightly so the fall triggers before floating off a sharp edge.")]
    [SerializeField, Min(0f)] private float edgePadding = 0.2f;
    [Tooltip("Only treat leaving on XZ as a fall (ignore mild vertical wobble).")]
    [SerializeField] private bool horizontalOnly = true;

    [Header("Detection")]
    [Tooltip("Use the commander hurtbox / tracked head (physical CAVE walking), not the joystick-driven user root.")]
    [SerializeField] private bool trackHurtboxNotJoystickUser = true;
    [SerializeField] private bool onlyWhileMatchPlaying = true;
    [SerializeField, Min(0f)] private float startDelaySeconds = 1.5f;
    [SerializeField, Min(0f)] private float leaveGraceSeconds = 0.12f;

    [Header("Fall Presentation")]
    [SerializeField, Min(0.5f)] private float fallDurationSeconds = 2.4f;
    [SerializeField, Min(0.1f)] private float fallGravity = 18f;
    [SerializeField, Min(0f)] private float tumbleDegreesPerSecond = 220f;
    [SerializeField] private Vector3 tumbleAxisBias = new Vector3(1f, 0.35f, 0.2f);
    [Tooltip("On restart: restore exact cached rotation and standing Y. Keep current XZ (no horizontal teleport).")]
    [SerializeField] private bool restoreCachedUserPoseOnRestart = true;

    [Header("Debug")]
    [SerializeField] private bool logFallEvents = true;

    private bool fallTriggered;
    private float matchArmedAt = -1f;
    private float outsideSince = -1f;
    private Coroutine fallRoutine;
    private Vector3 tumbleAxis = Vector3.right;
    private Vector3 cachedUserPosition;
    private Quaternion cachedUserRotation;
    private bool hasCachedUserPose;
    private SiegeGameManager boundManager;

    private void Awake()
    {
        ResolveTowerCollider();
    }

    private void OnEnable()
    {
        TryBindMatchManager();
    }

    private void OnDisable()
    {
        UnbindMatchManager();
        StopFallRoutine();
    }

    private void Update()
    {
        TryBindMatchManager();

        if (fallTriggered || towerFloorCollider == null)
        {
            return;
        }

        if (!IsArmed())
        {
            return;
        }

        if (!TryResolveTrackedBodyPosition(out Vector3 trackedBodyPosition))
        {
            return;
        }

        bool inside = IsInsideTower(trackedBodyPosition);
        if (inside)
        {
            outsideSince = -1f;
            return;
        }

        if (outsideSince < 0f)
        {
            outsideSince = Time.unscaledTime;
        }

        if (Time.unscaledTime - outsideSince < leaveGraceSeconds)
        {
            return;
        }

        BeginFall();
    }

    /// <summary>
    /// Physical commander position in the room — hurtbox first, then tracked head.
    /// Avoids the joystick user root which moves without real walking.
    /// </summary>
    private bool TryResolveTrackedBodyPosition(out Vector3 position)
    {
        if (trackHurtboxNotJoystickUser)
        {
            SiegeCommanderArrowHealth health = SiegeCommanderArrowHealth.Instance;
            if (health != null && health.TryGetHurtboxAimPoint(out position))
            {
                return true;
            }

            Transform hurtbox = health != null ? health.GetHurtboxTransform() : null;
            if (hurtbox != null)
            {
                position = hurtbox.position;
                return true;
            }

            Transform head = SiegePlayEnvironment.ResolveVisionTransform()
                ?? SiegePlayEnvironment.ResolveSensorTransform()
                ?? SiegePlayEnvironment.ResolveHeadTransform();
            if (head != null)
            {
                position = head.position;
                return true;
            }
        }

        Transform user = ResolveUserRoot();
        if (user != null)
        {
            position = user.position;
            return true;
        }

        position = Vector3.zero;
        return false;
    }

    private static Transform ResolveUserRoot()
    {
        Transform user = SiegePlayEnvironment.ResolveUserTransform();
        if (user != null)
        {
            return user;
        }

        return SiegePlayEnvironment.ResolvePlayerTransform();
    }

    private void TryBindMatchManager()
    {
        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager == boundManager)
        {
            return;
        }

        UnbindMatchManager();
        if (manager == null)
        {
            return;
        }

        boundManager = manager;
        boundManager.MatchStateChanged += HandleMatchStateChanged;
        HandleMatchStateChanged(boundManager.CurrentState);
    }

    private void UnbindMatchManager()
    {
        if (boundManager == null)
        {
            return;
        }

        boundManager.MatchStateChanged -= HandleMatchStateChanged;
        boundManager = null;
    }

    private void HandleMatchStateChanged(SiegeGameManager.MatchState state)
    {
        if (state == SiegeGameManager.MatchState.Playing)
        {
            fallTriggered = false;
            outsideSince = -1f;
            matchArmedAt = Time.unscaledTime + startDelaySeconds;
            StopFallRoutine();
            CacheUserPose();
            return;
        }

        matchArmedAt = -1f;
        outsideSince = -1f;
        StopFallRoutine();

        if (state == SiegeGameManager.MatchState.SelectingDifficulty)
        {
            fallTriggered = false;
            // Restart click → restore exact pre-fall rotation + standing Y; keep current XZ.
            RestoreCachedUserPose();
        }
    }

    private void CacheUserPose()
    {
        Transform user = ResolveUserRoot();
        if (user == null)
        {
            hasCachedUserPose = false;
            return;
        }

        cachedUserPosition = user.position;
        cachedUserRotation = user.rotation;
        hasCachedUserPose = true;
    }

    private void RestoreCachedUserPose()
    {
        if (!restoreCachedUserPoseOnRestart || !hasCachedUserPose)
        {
            return;
        }

        Transform user = ResolveUserRoot();
        if (user == null)
        {
            return;
        }

        // Keep current XZ (physical cave place). Restore standing height + exact pre-fall rotation.
        Vector3 current = user.position;
        user.SetPositionAndRotation(
            new Vector3(current.x, cachedUserPosition.y, current.z),
            cachedUserRotation);

        if (logFallEvents)
        {
            Debug.Log(
                "Restored user rotation/Y on restart; kept XZ ("
                + current.x.ToString("F2") + ", " + current.z.ToString("F2")
                + "), rot " + cachedUserRotation.eulerAngles.ToString("F1") + ".",
                this);
        }
    }

    private bool IsArmed()
    {
        if (matchArmedAt < 0f || Time.unscaledTime < matchArmedAt)
        {
            return false;
        }

        // City defender stands off the command tower in Siege PVP — fall death is attacker-only.
        if (SiegeMatchSettings.IsSiegePvpMode)
        {
            SiegePvpSession pvp = SiegePvpSession.Instance;
            if (pvp != null && pvp.IsDefender)
            {
                return false;
            }
        }

        if (!onlyWhileMatchPlaying)
        {
            return true;
        }

        SiegeGameManager manager = SiegeGameManager.Instance;
        return manager != null && manager.IsPlaying;
    }

    private void BeginFall()
    {
        if (fallTriggered)
        {
            return;
        }

        fallTriggered = true;

        // Cache pose before tumble so restart can restore exact CAVE alignment rotation.
        CacheUserPose();

        tumbleAxis = tumbleAxisBias.sqrMagnitude > 0.0001f
            ? tumbleAxisBias.normalized
            : Vector3.right;
        tumbleAxis += new Vector3(Random.Range(-0.25f, 0.25f), Random.Range(-0.15f, 0.15f), Random.Range(-0.25f, 0.25f));
        if (tumbleAxis.sqrMagnitude < 0.0001f)
        {
            tumbleAxis = Vector3.right;
        }

        tumbleAxis.Normalize();

        if (logFallEvents)
        {
            Debug.Log("Commander left the command tower — starting fatal fall cinematic.", this);
        }

        SiegeCommanderArrowHealth health = SiegeCommanderArrowHealth.Instance;
        if (health != null)
        {
            health.BeginFatalFallPresentation();
        }

        StopFallRoutine();
        fallRoutine = StartCoroutine(PlayFallSequence());
    }

    private IEnumerator PlayFallSequence()
    {
        Transform user = ResolveUserRoot();
        float elapsed = 0f;
        float fallVelocity = 0f;

        while (elapsed < fallDurationSeconds)
        {
            float dt = Time.unscaledDeltaTime;
            elapsed += dt;

            if (user != null)
            {
                fallVelocity += fallGravity * dt;
                Vector3 position = user.position;
                // Drop on Y only during cinematic; leave XZ alone so room tracking stays put.
                position.y -= fallVelocity * dt;
                user.position = position;
                user.Rotate(tumbleAxis, tumbleDegreesPerSecond * dt, Space.World);
            }

            yield return null;
        }

        fallRoutine = null;

        SiegeCommanderArrowHealth health = SiegeCommanderArrowHealth.Instance;
        if (health != null)
        {
            health.CompleteFatalFallDefeat();
        }
        else if (SiegeGameManager.Instance != null)
        {
            SiegeGameManager.Instance.NotifyCommanderFellFromTower();
        }
    }

    private void StopFallRoutine()
    {
        if (fallRoutine == null)
        {
            return;
        }

        StopCoroutine(fallRoutine);
        fallRoutine = null;
    }

    private bool IsInsideTower(Vector3 worldPosition)
    {
        if (towerFloorCollider == null)
        {
            return true;
        }

        // Collider.bounds is a world AABB and ignores rotation — a yawed tower floor
        // becomes a larger axis-aligned rectangle, so fall only triggers at AABB corners.
        if (towerFloorCollider is BoxCollider box)
        {
            return IsInsideOrientedBox(box, worldPosition);
        }

        return IsInsideViaClosestPoint(towerFloorCollider, worldPosition);
    }

    private bool IsInsideOrientedBox(BoxCollider box, Vector3 worldPosition)
    {
        Vector3 local = box.transform.InverseTransformPoint(worldPosition) - box.center;
        Vector3 halfExtents = box.size * 0.5f;
        halfExtents.x = Mathf.Max(0f, halfExtents.x - edgePadding);
        halfExtents.z = Mathf.Max(0f, halfExtents.z - edgePadding);

        if (horizontalOnly)
        {
            return Mathf.Abs(local.x) <= halfExtents.x && Mathf.Abs(local.z) <= halfExtents.z;
        }

        halfExtents.y = Mathf.Max(0f, halfExtents.y - edgePadding);
        return Mathf.Abs(local.x) <= halfExtents.x
            && Mathf.Abs(local.y) <= halfExtents.y
            && Mathf.Abs(local.z) <= halfExtents.z;
    }

    private bool IsInsideViaClosestPoint(Collider collider, Vector3 worldPosition)
    {
        Vector3 probe = worldPosition;
        if (horizontalOnly)
        {
            // Compare on the collider's horizontal plane so mild vertical tracking noise
            // does not push ClosestPoint onto the top/bottom faces.
            Bounds bounds = collider.bounds;
            probe.y = bounds.center.y;
        }

        Vector3 closest = collider.ClosestPoint(probe);
        Vector3 delta = closest - probe;
        if (horizontalOnly)
        {
            delta.y = 0f;
        }

        // ClosestPoint returns the probe itself when inside. Inward edge padding is only
        // applied for BoxCollider (oriented local extents) above.
        const float epsilonSq = 0.0001f;
        return delta.sqrMagnitude <= epsilonSq;
    }

    private void ResolveTowerCollider()
    {
        if (towerFloorCollider != null)
        {
            return;
        }

        if (playerBoundary == null)
        {
            playerBoundary = GetComponent<SiegePlayerBoundary>();
            if (playerBoundary == null)
            {
                playerBoundary = FindObjectOfType<SiegePlayerBoundary>();
            }
        }

        if (playerBoundary != null)
        {
            towerFloorCollider = playerBoundary.GetComponent<Collider>();
            Collider[] childColliders = playerBoundary.GetComponentsInChildren<Collider>();
            for (int i = 0; i < childColliders.Length; i++)
            {
                if (childColliders[i] != null && childColliders[i].enabled)
                {
                    towerFloorCollider = childColliders[i];
                    break;
                }
            }
        }

        if (towerFloorCollider == null)
        {
            towerFloorCollider = GetComponent<Collider>();
        }
    }

    /// <summary>Random ground point across the full walkable tower floor (fallback when no SiegePlayerBoundary is available).</summary>
    public bool TrySampleRandomTowerFloorPoint(out Vector3 point)
    {
        point = Vector3.zero;
        ResolveTowerCollider();
        if (towerFloorCollider == null)
        {
            return false;
        }

        if (towerFloorCollider is BoxCollider box)
        {
            Vector3 half = box.size * 0.5f;
            if (half.x <= 0.025f || half.z <= 0.025f)
            {
                return false;
            }

            Vector3 local = box.center + new Vector3(
                Random.Range(-half.x, half.x),
                -half.y + 0.05f,
                Random.Range(-half.z, half.z));
            point = box.transform.TransformPoint(local);
            return float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z);
        }

        Bounds bounds = towerFloorCollider.bounds;
        float x = Random.Range(bounds.min.x, bounds.max.x);
        float z = Random.Range(bounds.min.z, bounds.max.z);
        float y = bounds.min.y + 0.05f;
        point = new Vector3(x, y, z);
        return float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z);
    }
}
