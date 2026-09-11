using UnityEngine;

/// <summary>
/// Ground disc the player can stand in (head/vision tracked) to trigger a Lite menu action.
/// Used only when <see cref="SiegeGameManager.LiteModeActive"/> is true.
/// </summary>
[DisallowMultipleComponent]
public class SiegeStandZone : MonoBehaviour
{
    public enum Action
    {
        TimedChallenge = 0,
        EndlessSurvival = 1,
        ReturnToMainMenu = 2
    }

    [SerializeField] private Action zoneAction = Action.TimedChallenge;
    [SerializeField, Min(0.1f)] private float radius = 0.55f;
    [SerializeField, Min(0.1f)] private float dwellSeconds = 2f;
    [SerializeField] private Transform playerOverride;
    [SerializeField] private bool projectPlayerToZoneHeight = true;

    [Header("Progress Visual")]
    [SerializeField] private Renderer fillRenderer;
    [SerializeField] private Color idleColor = new Color(0.15f, 0.35f, 0.55f, 0.55f);
    [SerializeField] private Color fillingColor = new Color(0.2f, 0.75f, 0.95f, 0.75f);
    [SerializeField] private Color completeColor = new Color(0.25f, 0.95f, 0.45f, 0.85f);
    [SerializeField] private Transform progressScaleTarget;

    private float dwellTimer;
    private bool completed;
    private MaterialPropertyBlock propertyBlock;
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    public Action ZoneAction => zoneAction;
    public float Progress01 => dwellSeconds <= 0f ? 0f : Mathf.Clamp01(dwellTimer / dwellSeconds);

    public event System.Action<SiegeStandZone> Completed;

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
        if (fillRenderer == null)
        {
            fillRenderer = GetComponentInChildren<Renderer>();
        }

        ApplyVisual(0f, false);
    }

    private void OnDisable()
    {
        ResetDwell();
    }

    private void Update()
    {
        if (!isActiveAndEnabled || completed || !SiegeGameManager.LiteModeActive)
        {
            return;
        }

        Vector3? playerPos = TryGetPlayerGroundPosition();
        bool occupied = playerPos.HasValue && IsInsideRadius(playerPos.Value);

        if (occupied)
        {
            dwellTimer += Time.unscaledDeltaTime;
            ApplyVisual(Progress01, false);

            if (dwellTimer >= dwellSeconds)
            {
                completed = true;
                ApplyVisual(1f, true);
                Completed?.Invoke(this);
            }
        }
        else if (dwellTimer > 0f)
        {
            ResetDwell();
        }
    }

    public void ResetDwell()
    {
        dwellTimer = 0f;
        completed = false;
        ApplyVisual(0f, false);
    }

    public void SetZoneActive(bool active)
    {
        if (gameObject.activeSelf != active)
        {
            gameObject.SetActive(active);
        }

        if (!active)
        {
            ResetDwell();
        }
    }

    public void Configure(Action action, float standRadius, float requiredDwellSeconds, Color? idle = null)
    {
        zoneAction = action;
        radius = Mathf.Max(0.1f, standRadius);
        dwellSeconds = Mathf.Max(0.1f, requiredDwellSeconds);
        if (idle.HasValue)
        {
            idleColor = idle.Value;
        }

        ResetDwell();
    }

    private Vector3? TryGetPlayerGroundPosition()
    {
        Transform tracking = playerOverride != null
            ? playerOverride
            : SiegePlayEnvironment.ResolvePlayerTransform();

        if (tracking == null)
        {
            Camera main = Camera.main;
            if (main == null)
            {
                return null;
            }

            tracking = main.transform;
        }

        Vector3 pos = tracking.position;
        if (projectPlayerToZoneHeight)
        {
            pos.y = transform.position.y;
        }

        return pos;
    }

    private bool IsInsideRadius(Vector3 playerGroundPos)
    {
        Vector3 delta = playerGroundPos - transform.position;
        delta.y = 0f;
        return delta.sqrMagnitude <= radius * radius;
    }

    private void ApplyVisual(float progress, bool complete)
    {
        Color color = complete
            ? completeColor
            : Color.Lerp(idleColor, fillingColor, progress);

        if (fillRenderer != null)
        {
            fillRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(ColorId, color);
            propertyBlock.SetColor(BaseColorId, color);
            fillRenderer.SetPropertyBlock(propertyBlock);
        }

        if (progressScaleTarget != null)
        {
            float scale = Mathf.Max(0.05f, progress);
            Vector3 local = progressScaleTarget.localScale;
            progressScaleTarget.localScale = new Vector3(scale, local.y, scale);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
#endif
}
