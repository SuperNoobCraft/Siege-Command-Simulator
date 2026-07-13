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
    [Tooltip("When in CAVE/HMD, register the Votanic head transform with SiegeCommanderArrowHealth for arrow hits.")]
    [SerializeField] private bool autoBindCommanderToVotanicHead = true;

    private SiegePlayEnvironmentMode resolvedMode = SiegePlayEnvironmentMode.DesktopPc;

    public static event Action EnvironmentChanged;

    public SiegePlayEnvironmentMode ConfiguredMode => playEnvironment;
    public static SiegePlayEnvironmentMode ActiveMode =>
        Instance != null ? Instance.resolvedMode : ResolveMode(SiegePlayEnvironmentMode.Auto);
    public static bool IsDesktopInput => ActiveMode == SiegePlayEnvironmentMode.DesktopPc;
    public static bool IsTrackedXr =>
        ActiveMode == SiegePlayEnvironmentMode.Cave || ActiveMode == SiegePlayEnvironmentMode.Hmd;

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

    public static Transform ResolvePlayerTransform()
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

    public static UnityEngine.Camera ResolveViewCamera()
    {
        Transform playerTransform = ResolvePlayerTransform();
        if (playerTransform != null)
        {
            UnityEngine.Camera playerCamera = playerTransform.GetComponent<UnityEngine.Camera>();
            if (playerCamera != null)
            {
                return playerCamera;
            }
        }

        return UnityEngine.Camera.main;
    }

    public static string GetRestartPrompt(string desktopPrompt, string trackedPrompt)
    {
        return IsDesktopInput ? desktopPrompt : trackedPrompt;
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
        Transform headTransform = ResolvePlayerTransform();
        if (headTransform == null)
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

        commanderHealth.RegisterHitRoot(headTransform, includeChildColliders: false);
    }
}
