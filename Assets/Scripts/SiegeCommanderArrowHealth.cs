using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Tracks enemy arrow hits on the commander. Three hits ends the match.
/// Attach to the VR head / player rig with a trigger collider the player can dodge into or out of.
/// </summary>
public class SiegeCommanderArrowHealth : MonoBehaviour
{
    public static SiegeCommanderArrowHealth Instance { get; private set; }
    public static event Action<int, Vector3> CommanderHitRegistered;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
        CommanderHitRegistered = null;
    }

    [Header("Hits")]
    [SerializeField, Min(1)] private int maxHits = 3;
    [SerializeField, Min(0f)] private float hitInvulnerabilityDuration = 0.35f;
    [SerializeField] private bool autoCreateHeadHitVolume = false;
    [SerializeField] private bool autoRegisterChildColliders = true;
    [SerializeField, Min(0.05f)] private float headHitVolumeRadius = 0.22f;
    [Tooltip("Create and follow a sphere trigger on the tracked head/camera for arrow hits.")]
    [SerializeField] private bool useTrackedHeadHitVolume = true;

    [Header("Debug Gizmo")]
    [Tooltip("Draw the active commander hurtbox in the Scene view. Toggle on SiegeCommanderArrowHealth.")]
    [SerializeField] private bool drawHitVolumeGizmo = true;
    [SerializeField] private Color hitVolumeGizmoColor = new Color(1f, 0.2f, 0.2f, 0.85f);
    [SerializeField] private Color hitVolumeGizmoFillColor = new Color(1f, 0.2f, 0.2f, 0.12f);

    [Header("Screen Tint")]
    [Tooltip("Leave empty to create a standalone overlay canvas (recommended). Do not assign vGear head or user.")]
    [SerializeField] private Transform damageOverlayParent;
    [SerializeField] private Image damageOverlayImage;
    [SerializeField, Range(0f, 1f)] private float firstHitTintAlpha = 0.18f;
    [SerializeField, Range(0f, 1f)] private float secondHitTintAlpha = 0.42f;
    [SerializeField, Range(0f, 1f)] private float fatalHitTintAlpha = 0.65f;
    [SerializeField, Min(0f)] private float hitTintFadeInDuration = 0.08f;
    [SerializeField, Min(0f)] private float hitTintHoldDuration = 0.18f;
    [SerializeField, Min(0f)] private float hitTintFadeOutDuration = 0.5f;
    [SerializeField] private Color damageTintColor = new Color(0.85f, 0.05f, 0.05f, 1f);

    [Header("Game Over")]
    [SerializeField] private UnityEvent onArrowHit;
    [SerializeField] private UnityEvent onGameOver;

    private float invulnerableUntil;
    private bool isDefeated;
    private Coroutine tintCoroutine;
    private readonly HashSet<Collider> registeredHitColliders = new HashSet<Collider>();
    private readonly HashSet<Transform> registeredHitRoots = new HashSet<Transform>();
    private Transform trackedHeadTransform;
    private SphereCollider trackedHeadHitCollider;

    public int HitCount { get; private set; }
    public int MaxHits => maxHits;
    public int HitsRemaining => Mathf.Max(0, maxHits - HitCount);
    public bool IsDefeated => isDefeated;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple SiegeCommanderArrowHealth instances found. Using the most recent one.", this);
        }

        Instance = this;
        RegisterHitRoot(transform);
        EnsureCommanderHitVolumes();
        EnsureHeadHitVolume();
        EnsureDamageOverlay();
        ClearDamageTint();
        RefreshCommanderHpUi();
    }

    private void Start()
    {
        RefreshCommanderHpUi();
    }

    private void LateUpdate()
    {
        SyncTrackedHeadHitVolume();
    }

    private void SyncTrackedHeadHitVolume()
    {
        if (!Application.isPlaying || !useTrackedHeadHitVolume)
        {
            return;
        }

        Transform headTransform = ResolveTrackedHeadTransform();
        if (headTransform == null)
        {
            return;
        }

        if (trackedHeadTransform != headTransform)
        {
            trackedHeadTransform = headTransform;
            trackedHeadHitCollider = null;
            RegisterHitRoot(headTransform);
        }

        EnsureTrackedHeadHitCollider(headTransform);
    }

    private Transform ResolveTrackedHeadTransform()
    {
        Transform headTransform = SiegePlayEnvironment.ResolvePlayerTransform();
        if (headTransform != null)
        {
            return headTransform;
        }

        foreach (Transform root in registeredHitRoots)
        {
            if (root != null)
            {
                return root;
            }
        }

        return transform;
    }

    private void EnsureTrackedHeadHitCollider(Transform headTransform)
    {
        if (headTransform == null)
        {
            return;
        }

        if (trackedHeadHitCollider == null)
        {
            trackedHeadHitCollider = FindTrackedHeadHitCollider(headTransform);
        }

        if (trackedHeadHitCollider == null)
        {
            GameObject hitVolumeObject = new GameObject("CommanderTrackedHeadHitVolume");
            hitVolumeObject.transform.SetParent(headTransform, false);
            hitVolumeObject.transform.localPosition = Vector3.zero;
            hitVolumeObject.transform.localRotation = Quaternion.identity;
            hitVolumeObject.layer = gameObject.layer;

            trackedHeadHitCollider = hitVolumeObject.AddComponent<SphereCollider>();
            trackedHeadHitCollider.isTrigger = true;
            hitVolumeObject.AddComponent<SiegeCommanderHitVolume>();
        }

        trackedHeadHitCollider.radius = headHitVolumeRadius;
        RegisterHitCollider(trackedHeadHitCollider);
    }

    private static SphereCollider FindTrackedHeadHitCollider(Transform headTransform)
    {
        SiegeCommanderHitVolume[] hitVolumes = headTransform.GetComponentsInChildren<SiegeCommanderHitVolume>(true);
        for (int i = 0; i < hitVolumes.Length; i++)
        {
            SphereCollider sphere = hitVolumes[i].GetComponent<SphereCollider>();
            if (sphere != null)
            {
                return sphere;
            }
        }

        return headTransform.GetComponentInChildren<SphereCollider>(true);
    }

    public bool TryGetActiveHitVolume(out Vector3 center, out float radius)
    {
        if (trackedHeadHitCollider != null)
        {
            center = trackedHeadHitCollider.bounds.center;
            radius = trackedHeadHitCollider.radius * GetMaxAxis(trackedHeadHitCollider.transform.lossyScale);
            return true;
        }

        Collider fallback = GetPrimaryHitCollider();
        if (fallback != null)
        {
            center = fallback.bounds.center;
            radius = fallback.bounds.extents.magnitude;
            return true;
        }

        center = transform.position;
        radius = headHitVolumeRadius;
        return false;
    }

    private Collider GetPrimaryHitCollider()
    {
        if (trackedHeadHitCollider != null)
        {
            return trackedHeadHitCollider;
        }

        foreach (Collider collider in registeredHitColliders)
        {
            if (collider != null)
            {
                return collider;
            }
        }

        return GetComponentInChildren<Collider>(true);
    }

    private static float GetMaxAxis(Vector3 scale)
    {
        return Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
    }

    private void OnDrawGizmos()
    {
        if (!drawHitVolumeGizmo)
        {
            return;
        }

        if (!TryGetActiveHitVolume(out Vector3 center, out float radius))
        {
            center = transform.position;
            radius = headHitVolumeRadius;
        }

        if (Application.isPlaying)
        {
            Transform headTransform = ResolveTrackedHeadTransform();
            if (headTransform != null)
            {
                center = headTransform.position;
            }
        }

        Gizmos.color = hitVolumeGizmoFillColor;
        Gizmos.DrawSphere(center, radius);
        Gizmos.color = hitVolumeGizmoColor;
        Gizmos.DrawWireSphere(center, radius);
    }

    private void RefreshCommanderHpUi()
    {
        if (SiegeMatchUi.Instance != null)
        {
            SiegeMatchUi.Instance.UpdateCommanderHp(HitsRemaining, maxHits);
            return;
        }

        SiegeMatchUi matchUi = FindObjectOfType<SiegeMatchUi>();
        if (matchUi != null)
        {
            matchUi.UpdateCommanderHp(HitsRemaining, maxHits);
        }
    }

    public void RegisterHitRoot(Transform root)
    {
        if (root == null)
        {
            return;
        }

        registeredHitRoots.Add(root);
        if (!autoRegisterChildColliders)
        {
            return;
        }

        Collider[] colliders = root.GetComponentsInChildren<Collider>(includeInactive: true);
        for (int i = 0; i < colliders.Length; i++)
        {
            RegisterHitCollider(colliders[i]);
        }
    }

    public void RegisterHitCollider(Collider collider)
    {
        if (collider != null)
        {
            registeredHitColliders.Add(collider);
        }
    }

    public bool IsRegisteredHitCollider(Collider collider)
    {
        if (collider == null)
        {
            return false;
        }

        if (registeredHitColliders.Contains(collider))
        {
            return true;
        }

        foreach (Transform root in registeredHitRoots)
        {
            if (root != null && collider.transform.IsChildOf(root))
            {
                return true;
            }
        }

        return collider.transform == transform || collider.transform.IsChildOf(transform);
    }

    public static bool TryResolveHitCollider(Collider collider, out SiegeCommanderArrowHealth health)
    {
        health = null;
        if (collider == null)
        {
            return false;
        }

        health = collider.GetComponent<SiegeCommanderArrowHealth>();
        if (health != null)
        {
            return true;
        }

        health = collider.GetComponentInParent<SiegeCommanderArrowHealth>();
        if (health != null)
        {
            return true;
        }

        SiegeCommanderHitVolume hitVolume = collider.GetComponent<SiegeCommanderHitVolume>();
        if (hitVolume == null)
        {
            hitVolume = collider.GetComponentInParent<SiegeCommanderHitVolume>();
        }

        if (hitVolume != null && hitVolume.Health != null)
        {
            health = hitVolume.Health;
            return true;
        }

        if (Instance != null && Instance.IsRegisteredHitCollider(collider))
        {
            health = Instance;
            return true;
        }

        return false;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void RegisterArrowHit(Vector3 hitPoint)
    {
        if (isDefeated || Time.time < invulnerableUntil)
        {
            return;
        }

        HitCount = Mathf.Min(maxHits, HitCount + 1);
        invulnerableUntil = Time.time + hitInvulnerabilityDuration;
        onArrowHit?.Invoke();
        CommanderHitRegistered?.Invoke(HitCount, hitPoint);

        if (HitCount >= maxHits)
        {
            PlayHitTintFlash(HitCount, stayVisible: true);
            TriggerGameOver();
            return;
        }

        PlayHitTintFlash(HitCount, stayVisible: false);

        SiegeMatchUi matchUi = FindObjectOfType<SiegeMatchUi>();
        if (matchUi != null)
        {
            matchUi.UpdateCommanderHp(HitsRemaining, maxHits);
        }

        Debug.Log(
            "Commander hit by enemy arrow (" + HitCount + "/" + maxHits + ", "
            + HitsRemaining + " remaining) at "
            + hitPoint.ToString("F1"),
            this);
    }

    private void TriggerGameOver()
    {
        if (isDefeated)
        {
            return;
        }

        isDefeated = true;

        Debug.Log("Commander defeated after " + maxHits + " arrow hits.", this);
        onGameOver?.Invoke();

        SiegeAudioManager audioManager = SiegeAudioManager.Instance;
        if (audioManager != null)
        {
            audioManager.PlayCommanderFallen();
        }

        SiegeGameManager gameManager = SiegeGameManager.Instance;
        if (gameManager != null)
        {
            gameManager.NotifyCommanderDefeated();
        }
    }

    private void PlayHitTintFlash(int hitNumber, bool stayVisible)
    {
        if (damageOverlayImage == null)
        {
            return;
        }

        float peakAlpha = GetPeakTintAlpha(hitNumber);
        if (tintCoroutine != null)
        {
            StopCoroutine(tintCoroutine);
        }

        tintCoroutine = StartCoroutine(HitTintFlashRoutine(peakAlpha, stayVisible));
    }

    private float GetPeakTintAlpha(int hitNumber)
    {
        if (hitNumber <= 1)
        {
            return firstHitTintAlpha;
        }

        if (hitNumber == 2)
        {
            return secondHitTintAlpha;
        }

        return fatalHitTintAlpha;
    }

    private IEnumerator HitTintFlashRoutine(float peakAlpha, bool stayVisible)
    {
        damageOverlayImage.enabled = true;

        yield return FadeTintAlpha(0f, peakAlpha, hitTintFadeInDuration);

        if (hitTintHoldDuration > 0f)
        {
            yield return WaitUnscaled(hitTintHoldDuration);
        }

        if (stayVisible)
        {
            SetTintAlpha(peakAlpha);
            tintCoroutine = null;
            yield break;
        }

        yield return FadeTintAlpha(peakAlpha, 0f, hitTintFadeOutDuration);
        ClearDamageTint();
        tintCoroutine = null;
    }

    private IEnumerator FadeTintAlpha(float fromAlpha, float toAlpha, float duration)
    {
        if (duration <= 0f)
        {
            SetTintAlpha(toAlpha);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            SetTintAlpha(Mathf.Lerp(fromAlpha, toAlpha, t));
            yield return null;
        }

        SetTintAlpha(toAlpha);
    }

    private static IEnumerator WaitUnscaled(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private void SetTintAlpha(float alpha)
    {
        if (damageOverlayImage == null)
        {
            return;
        }

        Color tint = damageTintColor;
        tint.a = alpha;
        damageOverlayImage.color = tint;
        damageOverlayImage.enabled = alpha > 0.001f;
    }

    private void ClearDamageTint()
    {
        SetTintAlpha(0f);
    }

    private void EnsureCommanderHitVolumes()
    {
        Collider[] colliders = GetComponentsInChildren<Collider>(includeInactive: true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null)
            {
                continue;
            }

            RegisterHitCollider(collider);
            if (collider.GetComponent<SiegeCommanderHitVolume>() == null)
            {
                collider.gameObject.AddComponent<SiegeCommanderHitVolume>();
            }
        }
    }

    private void EnsureHeadHitVolume()
    {
        if (!autoCreateHeadHitVolume)
        {
            return;
        }

        Collider[] existingColliders = GetComponentsInChildren<Collider>(includeInactive: true);
        for (int i = 0; i < existingColliders.Length; i++)
        {
            if (existingColliders[i] != null)
            {
                return;
            }
        }

        GameObject hitVolume = new GameObject("CommanderHeadHitVolume");
        hitVolume.transform.SetParent(transform, false);
        hitVolume.layer = gameObject.layer;

        SphereCollider collider = hitVolume.AddComponent<SphereCollider>();
        collider.isTrigger = true;
        collider.radius = headHitVolumeRadius;
        hitVolume.AddComponent<SiegeCommanderHitVolume>();
        RegisterHitCollider(collider);
    }

    private void EnsureDamageOverlay()
    {
        if (damageOverlayImage != null)
        {
            return;
        }

        GameObject existingOverlay = GameObject.Find("ArrowDamageOverlay");
        if (existingOverlay != null)
        {
            damageOverlayImage = existingOverlay.GetComponentInChildren<Image>();
            if (damageOverlayImage != null)
            {
                return;
            }
        }

        GameObject canvasObject = new GameObject("ArrowDamageOverlay");
        if (damageOverlayParent != null)
        {
            canvasObject.transform.SetParent(damageOverlayParent, false);
        }

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject imageObject = new GameObject("Tint");
        imageObject.transform.SetParent(canvasObject.transform, false);

        RectTransform rectTransform = imageObject.AddComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        damageOverlayImage = imageObject.AddComponent<Image>();
        damageOverlayImage.raycastTarget = false;
        damageOverlayImage.enabled = false;
    }
}
