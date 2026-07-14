using System;
using UnityEngine;

/// <summary>
/// Play environment override. Attach to the same object as <see cref="SiegeGameManager"/>.
/// </summary>
public enum SiegePlayEnvironmentMode
{
    Auto,
    DesktopPc,
    Cave,
    Hmd
}

/// <summary>
/// Resolves whether the match runs in desktop test mode or a tracked Votanic XR environment (CAVE/HMD).
/// Attach this component to your GameManager object alongside <see cref="SiegeGameManager"/>.
/// </summary>
[DefaultExecutionOrder(-150)]
public class SiegePlayEnvironment : MonoBehaviour
{
    public static SiegePlayEnvironment Instance { get; private set; }

    [Header("Play Environment")]
    [Tooltip("Auto reads the active Votanic config (ConfigCAVE.vxrc, ConfigPC.vxrc, ConfigHMD.vxrc, etc.). "
             + "Force Desktop PC for mouse testing in the editor. Force Cave/Hmd when testing tracked input.")]
    [SerializeField] private SiegePlayEnvironmentMode playEnvironment = SiegePlayEnvironmentMode.Auto;
    [Tooltip("When in CAVE/HMD, register the Votanic player transform with SiegeCommanderArrowHealth for arrow hits.")]
    [SerializeField] private bool autoBindCommanderToVotanicHead = true;
    [Tooltip("Fallback eye height when aiming from the CAVE user root because no head transform is available.")]
    [SerializeField, Min(0.5f)] private float caveFallbackEyeHeight = 1.6f;

    private SiegePlayEnvironmentMode resolvedMode = SiegePlayEnvironmentMode.DesktopPc;

    public static event Action EnvironmentChanged;

    public SiegePlayEnvironmentMode ConfiguredMode => playEnvironment;
    public static SiegePlayEnvironmentMode ActiveMode =>
        Instance != null ? Instance.resolvedMode : ResolveMode(SiegePlayEnvironmentMode.Auto);
    public static bool IsDesktopInput => ActiveMode == SiegePlayEnvironmentMode.DesktopPc;
    public static bool IsTrackedXr =>
        ActiveMode == SiegePlayEnvironmentMode.Cave || ActiveMode == SiegePlayEnvironmentMode.Hmd;
    public static bool IsCaveMode => ActiveMode == SiegePlayEnvironmentMode.Cave;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple SiegePlayEnvironment instances found. Using the most recent one.", this);
        }

        Instance = this;
        ApplyResolvedMode(ResolveMode(playEnvironment));
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Transform used for player identity/tracking.
    /// In CAVE this prefers <c>vGear.user</c> (room walking root). Head can sit at the spawn origin.
    /// In HMD/desktop this prefers the tracked head / main camera.
    /// </summary>
    public static Transform ResolvePlayerTransform()
    {
        if (IsCaveMode)
        {
            Transform user = ResolveUserTransform();
            if (user != null)
            {
                return user;
            }
        }

        Transform head = ResolveHeadTransform();
        if (head != null)
        {
            return head;
        }

        return ResolveUserTransform();
    }

    /// <summary>
    /// World aim/hit position for arrows. In CAVE, uses walking-user XZ with tracked-head Y
    /// so projectiles lead the player across the room at eye height.
    /// </summary>
    public static Vector3 ResolvePlayerAimPosition()
    {
        Transform head = ResolveHeadTransform();
        Transform user = ResolveUserTransform();

        if (IsCaveMode && user != null)
        {
            float eyeY = head != null
                ? head.position.y
                : user.position.y + GetCaveFallbackEyeHeight();
            return new Vector3(user.position.x, eyeY, user.position.z);
        }

        if (head != null)
        {
            return head.position;
        }

        if (user != null)
        {
            return user.position;
        }

        UnityEngine.Camera mainCamera = UnityEngine.Camera.main;
        return mainCamera != null ? mainCamera.transform.position : Vector3.zero;
    }

    public static Transform ResolveHeadTransform()
    {
        if (Votanic.vXR.vGear.vGear.head != null)
        {
            return Votanic.vXR.vGear.vGear.head.transform;
        }

        if (Votanic.vXR.vCast.vCast.head != null)
        {
            return Votanic.vXR.vCast.vCast.head.transform;
        }

        UnityEngine.Camera mainCamera = UnityEngine.Camera.main;
        return mainCamera != null ? mainCamera.transform : null;
    }

    public static Transform ResolveUserTransform()
    {
        if (Votanic.vXR.vGear.vGear.user != null)
        {
            return Votanic.vXR.vGear.vGear.user.transform;
        }

        if (Votanic.vXR.vCast.vCast.user != null)
        {
            return Votanic.vXR.vCast.vCast.user.transform;
        }

        return null;
    }

    public static UnityEngine.Camera ResolveViewCamera()
    {
        Transform headTransform = ResolveHeadTransform();
        if (headTransform != null)
        {
            UnityEngine.Camera headCamera = headTransform.GetComponent<UnityEngine.Camera>();
            if (headCamera != null)
            {
                return headCamera;
            }

            UnityEngine.Camera childCamera = headTransform.GetComponentInChildren<UnityEngine.Camera>();
            if (childCamera != null)
            {
                return childCamera;
            }
        }

        return UnityEngine.Camera.main;
    }

    public static string GetRestartPrompt(string desktopPrompt, string trackedPrompt)
    {
        return IsDesktopInput ? desktopPrompt : trackedPrompt;
    }

    private static float GetCaveFallbackEyeHeight()
    {
        return Instance != null ? Mathf.Max(0.5f, Instance.caveFallbackEyeHeight) : 1.6f;
    }

    private static SiegePlayEnvironmentMode ResolveMode(SiegePlayEnvironmentMode mode)
    {
        if (mode != SiegePlayEnvironmentMode.Auto)
        {
            return mode;
        }

        try
        {
            switch (Votanic.vXR.vCast.vCast.environment)
            {
                case Votanic.vXR.vCast.Core.SystemType.CAVE:
                    return SiegePlayEnvironmentMode.Cave;
                case Votanic.vXR.vCast.Core.SystemType.HMD:
                    return SiegePlayEnvironmentMode.Hmd;
                case Votanic.vXR.vCast.Core.SystemType.PC:
                    return SiegePlayEnvironmentMode.DesktopPc;
                default:
                    return SiegePlayEnvironmentMode.DesktopPc;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "Could not read vCast.environment; defaulting to desktop PC mode. "
                + exception.Message);
            return SiegePlayEnvironmentMode.DesktopPc;
        }
    }

    private void ApplyResolvedMode(SiegePlayEnvironmentMode mode)
    {
        SiegePlayEnvironmentMode previousMode = resolvedMode;
        resolvedMode = mode;

        if (autoBindCommanderToVotanicHead && IsTrackedXr)
        {
            TryBindCommanderHealthToVotanicHead();
        }

        if (previousMode != resolvedMode)
        {
            EnvironmentChanged?.Invoke();
        }

        Debug.Log("Siege play environment: " + resolvedMode + " (configured as " + playEnvironment + ").", this);
    }

    private static void TryBindCommanderHealthToVotanicHead()
    {
        Transform playerTransform = ResolvePlayerTransform();
        if (playerTransform == null)
        {
            return;
        }

        SiegeCommanderArrowHealth commanderHealth = SiegeCommanderArrowHealth.Instance;
        if (commanderHealth == null)
        {
            commanderHealth = UnityEngine.Object.FindObjectOfType<SiegeCommanderArrowHealth>();
        }

        if (commanderHealth == null)
        {
            return;
        }

        commanderHealth.RegisterHitRoot(playerTransform, includeChildColliders: false);
    }
}
