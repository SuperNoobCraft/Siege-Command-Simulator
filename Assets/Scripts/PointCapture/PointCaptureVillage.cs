using UnityEngine;

/// <summary>
/// One of the five capture villages. Occupied by standing regiments inside the capture radius.
/// </summary>
[DisallowMultipleComponent]
public class PointCaptureVillage : MonoBehaviour
{
    private const float CombatPresenceCooldownSeconds = 1f;

    [Header("Identity")]
    [SerializeField] private string villageName = "Village";
    [Tooltip("Starting owner only. Every village, including homes, can be captured the same way.")]
    [SerializeField] private CaptureOwner startingOwner = CaptureOwner.Neutral;

    [Header("Capture")]
    [Tooltip("Used when the board is missing. Occupy uses the village control disc (the large ground circle).")]
    [SerializeField, Min(0.5f)] private float captureRadius = 12f;
    [SerializeField] private bool occupyUsingControlDisc = true;
    [SerializeField, Min(0.25f)] private float captureSeconds = 4f;
    [SerializeField, Min(0.05f)] private float occupancyScanInterval = 0.2f;

    [Header("Visuals")]
    [SerializeField] private Renderer buildingRenderer;
    [SerializeField] private Renderer flagRenderer;
    [SerializeField] private Renderer captureRingRenderer;
    [SerializeField] private Renderer captureRingStripeRenderer;
    [SerializeField] private TextMesh nameLabel;
    [SerializeField] private TextMesh progressLabel;
    [SerializeField, Range(0.1f, 1f)] private float discColorAlpha = 0.45f;
    [SerializeField, Range(0.1f, 0.5f)] private float presenceStripeCoverage = 0.3f;
    [SerializeField, Min(1f)] private float presenceStripeRepeat = 8f;

    private static Texture2D sharedStripeMask;
    private static Material sharedStripeMaterialTemplate;
    private Material stripeMaterialInstance;

    private CaptureOwner currentOwner = CaptureOwner.Neutral;
    private CaptureOwner capturingOwner = CaptureOwner.Neutral;
    private float captureProgress;
    private float nextOccupancyScanTime;
    private int redOccupants;
    private int yellowOccupants;
    private bool hasCombatInZone;
    private bool hasHostilePressure;
    private bool hasOwnerPresence;
    private PointCaptureBoard board;
    private PointCaptureMatch match;
    private bool deferCaptureSimulation;

    public string VillageName => villageName;
    public CaptureOwner StartingOwner => startingOwner;
    public CaptureOwner CurrentOwner => currentOwner;
    public float CaptureRadius => OccupyRadius;
    public float OccupyRadius
    {
        get
        {
            if (occupyUsingControlDisc && board != null)
            {
                return Mathf.Max(0.5f, board.ControlRadius);
            }

            return captureRadius;
        }
    }
    public float CaptureProgress => captureProgress;
    public CaptureOwner CapturingOwner => capturingOwner;
    public Vector3 Position => transform.position;
    public int RedOccupants => redOccupants;
    public int YellowOccupants => yellowOccupants;
    public bool IsContested => redOccupants > 0 && yellowOccupants > 0;
    public bool HasCombatInZone => hasCombatInZone || hasHostilePressure || HasEnemyOccupants(currentOwner);
    public bool HasHostilePressure => hasHostilePressure;

    public bool HasEnemyOccupants(CaptureOwner owner)
    {
        if (owner == CaptureOwner.Red)
        {
            return yellowOccupants > 0;
        }

        if (owner == CaptureOwner.Yellow)
        {
            return redOccupants > 0;
        }

        return redOccupants > 0 || yellowOccupants > 0;
    }

    public void Configure(string displayName, CaptureOwner owner)
    {
        villageName = displayName;
        startingOwner = owner;
        currentOwner = owner;
        gameObject.name = "Village_" + displayName.Replace(" ", string.Empty);
    }

    public void Bind(PointCaptureBoard captureBoard, PointCaptureMatch captureMatch)
    {
        board = captureBoard;
        match = captureMatch;
        SyncCaptureRingScale();
    }

    public void AssignVisuals(Renderer building, Renderer flag, Renderer captureRing, TextMesh name, TextMesh progress)
    {
        buildingRenderer = building;
        flagRenderer = flag;
        captureRingRenderer = captureRing;
        nameLabel = name;
        progressLabel = progress;
        EnsureStripeRenderer();
    }

    public void ResetToStart()
    {
        currentOwner = startingOwner;
        capturingOwner = CaptureOwner.Neutral;
        captureProgress = 0f;
        redOccupants = 0;
        yellowOccupants = 0;
        hasCombatInZone = false;
        hasHostilePressure = false;
        hasOwnerPresence = false;
        RefreshVisuals();
        SyncCaptureRingScale();
    }

    private void Awake()
    {
        if (captureRingRenderer == null)
        {
            Transform ring = transform.Find("CaptureRing");
            if (ring != null)
            {
                captureRingRenderer = ring.GetComponent<Renderer>();
            }
        }

        EnsureStripeRenderer();
        currentOwner = startingOwner;
        RefreshVisuals();
    }

    private void OnDestroy()
    {
        if (stripeMaterialInstance != null)
        {
            if (Application.isPlaying)
            {
                Destroy(stripeMaterialInstance);
            }
            else
            {
                DestroyImmediate(stripeMaterialInstance);
            }
        }
    }

    private void Update()
    {
        if (match == null || !match.IsPlaying)
        {
            return;
        }

        if (Time.time >= nextOccupancyScanTime)
        {
            nextOccupancyScanTime = Time.time + occupancyScanInterval;
            ScanOccupants();
        }

        if (!deferCaptureSimulation)
        {
            TickCapture(Time.deltaTime);
        }

        RefreshDiscColor();
        RefreshProgressLabel();
    }

    public void SetDeferCaptureSimulation(bool defer)
    {
        deferCaptureSimulation = defer;
    }

    public void ApplyNetworkCaptureState(CaptureOwner owner, CaptureOwner capturing, float progress)
    {
        CaptureOwner previousOwner = currentOwner;
        currentOwner = owner;
        capturingOwner = capturing;
        captureProgress = Mathf.Clamp01(progress);
        RefreshVisuals();

        if (previousOwner != owner && board != null)
        {
            board.NotifyTerritoryChanged();
        }
    }

    public bool ContainsPoint(Vector3 worldPosition)
    {
        return GetHorizontalDistance(worldPosition, transform.position) <= OccupyRadius;
    }

    private void ScanOccupants()
    {
        redOccupants = 0;
        yellowOccupants = 0;
        hasCombatInZone = false;
        hasHostilePressure = false;
        hasOwnerPresence = false;

        TroopCombat[] troops = FindObjectsOfType<TroopCombat>();
        float rimThreatRadius = OccupyRadius + 6f;
        for (int i = 0; i < troops.Length; i++)
        {
            TroopCombat troop = troops[i];
            if (troop == null
                || !troop.isActiveAndEnabled
                || troop.IsPermanentlyEliminated
                || troop.CurrentState == TroopCombat.State.Dead
                )
            {
                continue;
            }

            bool inside = ContainsPoint(troop.transform.position);
            bool retreating = troop.CurrentState == TroopCombat.State.Retreat;
            CaptureOwner troopOwner = CaptureTeams.FromTroopFaction(troop.TroopFaction);

            if (!inside)
            {
                continue;
            }

            if (CaptureTeams.IsPlayerSide(currentOwner) && troopOwner == currentOwner)
            {
                hasOwnerPresence = true;
            }

            if (retreating)
            {
                continue;
            }

            if (troop.CurrentState == TroopCombat.State.Fight
                || troop.WasRecentlyInCombat(CombatPresenceCooldownSeconds))
            {
                hasCombatInZone = true;
            }

            if (troopOwner == CaptureOwner.Red)
            {
                redOccupants++;
            }
            else
            {
                yellowOccupants++;
            }
        }

        if (!hasCombatInZone && redOccupants > 0 && yellowOccupants > 0)
        {
            hasCombatInZone = true;
        }

        if (!hasOwnerPresence || !CaptureTeams.IsPlayerSide(currentOwner))
        {
            return;
        }

        for (int i = 0; i < troops.Length; i++)
        {
            TroopCombat troop = troops[i];
            if (troop == null
                || !troop.isActiveAndEnabled
                || troop.IsPermanentlyEliminated
                || troop.CurrentState == TroopCombat.State.Dead
                || troop.CurrentState == TroopCombat.State.Retreat
                || ContainsPoint(troop.transform.position)
                )
            {
                continue;
            }

            CaptureOwner troopOwner = CaptureTeams.FromTroopFaction(troop.TroopFaction);
            if (troopOwner == currentOwner)
            {
                continue;
            }

            bool targetingInside = troop.CurrentTarget != null && ContainsPoint(troop.CurrentTarget.transform.position);
            float distanceToVillage = GetHorizontalDistance(troop.transform.position, transform.position);
            if (targetingInside || distanceToVillage <= rimThreatRadius)
            {
                hasHostilePressure = true;
                break;
            }
        }
    }

    private void TickCapture(float deltaTime)
    {
        CaptureOwner presentOwner = CaptureOwner.Neutral;
        if (redOccupants > 0 && yellowOccupants == 0)
        {
            presentOwner = CaptureOwner.Red;
        }
        else if (yellowOccupants > 0 && redOccupants == 0)
        {
            presentOwner = CaptureOwner.Yellow;
        }

        if (presentOwner == CaptureOwner.Neutral)
        {
            if (!IsContested)
            {
                DecayProgress(deltaTime);
            }

            return;
        }

        if (presentOwner == currentOwner)
        {
            captureProgress = 0f;
            capturingOwner = CaptureOwner.Neutral;
            return;
        }

        if (capturingOwner != presentOwner)
        {
            capturingOwner = presentOwner;
            captureProgress = 0f;
        }

        float phaseDuration = Mathf.Max(0.25f, captureSeconds * 0.5f);
        captureProgress = Mathf.Min(1f, captureProgress + deltaTime / phaseDuration);
        if (captureProgress < 1f)
        {
            return;
        }

        if (currentOwner != CaptureOwner.Neutral)
        {
            Neutralize();
            return;
        }

        SetOwner(presentOwner);
    }

    private void DecayProgress(float deltaTime)
    {
        if (captureProgress <= 0f)
        {
            capturingOwner = CaptureOwner.Neutral;
            return;
        }

        float duration = Mathf.Max(0.25f, captureSeconds * 0.5f);
        captureProgress = Mathf.Max(0f, captureProgress - deltaTime / duration);
        if (captureProgress <= 0f)
        {
            capturingOwner = CaptureOwner.Neutral;
        }
    }

    public void SetOwner(CaptureOwner owner)
    {
        if (currentOwner == owner)
        {
            captureProgress = 0f;
            capturingOwner = CaptureOwner.Neutral;
            RefreshVisuals();
            return;
        }

        currentOwner = owner;
        captureProgress = 0f;
        capturingOwner = CaptureOwner.Neutral;
        RefreshVisuals();
        if (board != null)
        {
            board.NotifyTerritoryChanged();
        }
    }

    private void Neutralize()
    {
        if (currentOwner == CaptureOwner.Neutral)
        {
            captureProgress = 0f;
            RefreshVisuals();
            return;
        }

        currentOwner = CaptureOwner.Neutral;
        captureProgress = 0f;
        RefreshVisuals();
        if (board != null)
        {
            board.NotifyTerritoryChanged();
        }
    }

    public void RefreshVisuals()
    {
        Color color = CaptureTeams.GetColor(currentOwner);
        ApplyRendererColor(buildingRenderer, color);
        ApplyRendererColor(flagRenderer, color);
        RefreshDiscColor();

        if (nameLabel != null)
        {
            nameLabel.text = villageName + "\n" + CaptureTeams.GetDisplayName(currentOwner);
            nameLabel.color = Color.white;
        }

        SyncCaptureRingScale();
        RefreshProgressLabel();
    }

    private void RefreshDiscColor()
    {
        if (captureRingRenderer == null)
        {
            return;
        }

        if (TryGetOwnerPresenceIntruder(out CaptureOwner intruder) || (hasHostilePressure && hasOwnerPresence))
        {
            // Fight in the disc, or the owner is still here while enemies press the rim.
            if (intruder == CaptureOwner.Neutral)
            {
                intruder = CaptureTeams.Opposite(currentOwner);
            }

            Color orange = new Color(1f, 0.55f, 0.1f, discColorAlpha);
            ApplyRendererColor(captureRingRenderer, orange);
            RefreshStripeOverlay(intruder, visible: false);
            return;
        }

        RefreshStripeOverlay(CaptureOwner.Neutral, visible: false);
        Color discColor = CaptureTeams.GetSiegeDiscColor(
            currentOwner,
            capturingOwner,
            captureProgress,
            discColorAlpha);
        ApplyRendererColor(captureRingRenderer, discColor);
    }

    private bool TryGetOwnerPresenceIntruder(out CaptureOwner intruder)
    {
        intruder = CaptureOwner.Neutral;
        if (!CaptureTeams.IsPlayerSide(currentOwner))
        {
            return false;
        }

        int ownerCount = currentOwner == CaptureOwner.Red ? redOccupants : yellowOccupants;
        int intruderCount = currentOwner == CaptureOwner.Red ? yellowOccupants : redOccupants;
        if (ownerCount <= 0 || intruderCount <= 0)
        {
            return false;
        }

        intruder = CaptureTeams.Opposite(currentOwner);
        return true;
    }

    private void RefreshStripeOverlay(CaptureOwner intruder, bool visible)
    {
        EnsureStripeRenderer();
        if (captureRingStripeRenderer == null)
        {
            return;
        }

        captureRingStripeRenderer.enabled = visible;
        if (!visible)
        {
            return;
        }

        Material stripeMaterial = GetStripeMaterialInstance();
        captureRingStripeRenderer.sharedMaterial = stripeMaterial;

        Color stripeColor = CaptureTeams.GetColor(intruder, discColorAlpha);
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        captureRingStripeRenderer.GetPropertyBlock(block);
        block.SetColor("_Color", stripeColor);
        captureRingStripeRenderer.SetPropertyBlock(block);
    }

    private Material GetStripeMaterialInstance()
    {
        if (stripeMaterialInstance == null)
        {
            stripeMaterialInstance = new Material(GetSharedStripeMaterialTemplate());
            stripeMaterialInstance.mainTexture = GetSharedStripeMask(presenceStripeCoverage);
            stripeMaterialInstance.mainTextureScale = new Vector2(presenceStripeRepeat, presenceStripeRepeat);
        }

        return stripeMaterialInstance;
    }

    private void EnsureStripeRenderer()
    {
        if (captureRingStripeRenderer != null)
        {
            return;
        }

        Transform ring = transform.Find("CaptureRing");
        if (ring == null)
        {
            return;
        }

        Transform existing = ring.Find("CaptureRingStripes");
        if (existing != null)
        {
            captureRingStripeRenderer = existing.GetComponent<Renderer>();
            return;
        }

        GameObject stripeObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        stripeObject.name = "CaptureRingStripes";
        stripeObject.transform.SetParent(ring, false);
        stripeObject.transform.localPosition = new Vector3(0f, 0.12f, 0f);
        stripeObject.transform.localScale = new Vector3(0.985f, 1.05f, 0.985f);
        Collider collider = stripeObject.GetComponent<Collider>();
        if (collider != null)
        {
            if (Application.isPlaying)
            {
                Destroy(collider);
            }
            else
            {
                DestroyImmediate(collider);
            }
        }

        captureRingStripeRenderer = stripeObject.GetComponent<Renderer>();
        captureRingStripeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        captureRingStripeRenderer.receiveShadows = false;
        captureRingStripeRenderer.enabled = false;
    }

    private static Texture2D GetSharedStripeMask(float coverage)
    {
        if (sharedStripeMask == null)
        {
            const int size = 64;
            sharedStripeMask = new Texture2D(size, size, TextureFormat.Alpha8, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            float clampedCoverage = Mathf.Clamp(coverage, 0.1f, 0.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float diagonal = (x + y) / (float)(size * 2f);
                    float phase = diagonal - Mathf.Floor(diagonal);
                    float alpha = phase < clampedCoverage ? 1f : 0f;
                    sharedStripeMask.SetPixel(x, y, new Color(alpha, alpha, alpha, alpha));
                }
            }

            sharedStripeMask.Apply();
        }

        return sharedStripeMask;
    }

    private static Material GetSharedStripeMaterialTemplate()
    {
        if (sharedStripeMaterialTemplate != null)
        {
            return sharedStripeMaterialTemplate;
        }

        Shader shader = Shader.Find("Unlit/Transparent");
        if (shader == null)
        {
            shader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
        }

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        sharedStripeMaterialTemplate = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        return sharedStripeMaterialTemplate;
    }

    private void RefreshProgressLabel()
    {
        if (progressLabel == null)
        {
            return;
        }

        if (capturingOwner != CaptureOwner.Neutral && captureProgress > 0.01f)
        {
            string phase = currentOwner == CaptureOwner.Neutral ? "Capturing" : "Neutralizing";
            progressLabel.text = phase + " " + CaptureTeams.GetDisplayName(capturingOwner)
                + " " + Mathf.RoundToInt(captureProgress * 100f) + "%";
            progressLabel.color = CaptureTeams.GetColor(capturingOwner);
            progressLabel.gameObject.SetActive(true);
            return;
        }

        if (IsContested)
        {
            progressLabel.text = "Contested";
            progressLabel.color = Color.white;
            progressLabel.gameObject.SetActive(true);
            return;
        }

        progressLabel.text = string.Empty;
        progressLabel.gameObject.SetActive(false);
    }

    private static void ApplyRendererColor(Renderer renderer, Color color)
    {
        if (renderer == null)
        {
            return;
        }

        MaterialPropertyBlock block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        block.SetColor("_Color", color);
        renderer.SetPropertyBlock(block);
    }

    public static float GetHorizontalDistance(Vector3 a, Vector3 b)
    {
        Vector3 offset = a - b;
        offset.y = 0f;
        return offset.magnitude;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = CaptureTeams.GetColor(currentOwner == CaptureOwner.Neutral ? startingOwner : currentOwner, 0.35f);
        Gizmos.DrawWireSphere(transform.position, OccupyRadius);
    }

    private void SyncCaptureRingScale()
    {
        Transform ring = transform.Find("CaptureRing");
        if (ring == null)
        {
            return;
        }

        float diameter = OccupyRadius * 2f;
        ring.localScale = new Vector3(diameter, Mathf.Max(0.02f, ring.localScale.y), diameter);
    }
}
