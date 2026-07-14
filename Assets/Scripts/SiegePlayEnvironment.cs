using System;
using UnityEngine;
using Votanic.vXR.vCast;
using Votanic.vXR.vGear;

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
    [Tooltip("When in CAVE/HMD, register the glasses/vision transform with SiegeCommanderArrowHealth for arrow hits.")]
    [SerializeField] private bool autoBindCommanderToVotanicHead = true;
    [Tooltip("Fallback eye height when no vision/sensor/head transform is available.")]
    [SerializeField, Min(0.5f)] private float caveFallbackEyeHeight = 1.6f;
    [SerializeField] private bool logResolvedTrackingTransform = true;

    private SiegePlayEnvironmentMode resolvedMode = SiegePlayEnvironmentMode.DesktopPc;
    private static string lastLoggedTrackingSource;

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
    /// Transform used for commander body / hitbox tracking (physical glasses in CAVE).
    /// Prefer <c>sensor.vision</c> — <c>vCast.head</c> is the synchronizer and often follows the wand.
    /// </summary>
    public static Transform ResolvePlayerTransform()
    {
        Transform vision = ResolveVisionTransform();
        if (vision != null)
        {
            LogTrackingSourceOnce("vision/glasses", vision);
            return vision;
        }

        Transform sensor = ResolveSensorTransform();
        if (sensor != null)
        {
            LogTrackingSourceOnce("sensor", sensor);
            return sensor;
        }

        // HMD/desktop fallbacks. Skip synchronizer head if it is the controller.
        Transform head = ResolveHeadTransform(allowSynchronizerFallback: !IsCaveMode);
        if (head != null)
        {
            LogTrackingSourceOnce("head", head);
            return head;
        }

        Transform user = ResolveUserTransform();
        if (user != null)
        {
            LogTrackingSourceOnce("user", user);
            return user;
        }

        LogTrackingSourceOnce("none", null);
        return null;
    }

    /// <summary>
    /// World aim/hit position for arrows — glasses / vision pose when available.
    /// </summary>
    public static Vector3 ResolvePlayerAimPosition()
    {
        Transform tracking = ResolvePlayerTransform();
        if (tracking != null)
        {
            return tracking.position;
        }

        Transform user = ResolveUserTransform();
        if (user != null)
        {
            return user.position + Vector3.up * GetCaveFallbackEyeHeight();
        }

        UnityEngine.Camera mainCamera = UnityEngine.Camera.main;
        return mainCamera != null ? mainCamera.transform.position : Vector3.zero;
    }

    /// <summary>
    /// Glasses / eye tracking part of the Votanic sensor. This is what should drive the commander hurtbox in CAVE.
    /// </summary>
    public static Transform ResolveVisionTransform()
    {
        try
        {
            if (vGear.sensor != null && vGear.sensor.vision != null)
            {
                Transform vision = vGear.sensor.vision.transform;
                if (IsUsableTrackingTransform(vision))
                {
                    return vision;
                }
            }
        }
        catch (Exception)
        {
            // Sensor may not be ready during bootstrap.
        }

        try
        {
            if (vCast.sensor != null && vCast.sensor.vision != null)
            {
                Transform vision = vCast.sensor.vision.transform;
                if (IsUsableTrackingTransform(vision))
                {
                    return vision;
                }
            }
        }
        catch (Exception)
        {
            // Sensor may not be ready during bootstrap.
        }

        return null;
    }

    public static Transform ResolveSensorTransform()
    {
        try
        {
            if (vGear.sensor != null)
            {
                Transform sensor = vGear.sensor.transform;
                if (IsUsableTrackingTransform(sensor))
                {
                    return sensor;
                }
            }
        }
        catch (Exception)
        {
        }

        try
        {
            if (vCast.sensor != null)
            {
                Transform sensor = vCast.sensor.transform;
                if (IsUsableTrackingTransform(sensor))
                {
                    return sensor;
                }
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    /// <summary>
    /// Legacy head / synchronizer. In CAVE this often tracks the controller wand — prefer vision instead.
    /// </summary>
    public static Transform ResolveHeadTransform()
    {
        return ResolveHeadTransform(allowSynchronizerFallback: true);
    }

    private static Transform ResolveHeadTransform(bool allowSynchronizerFallback)
    {
        Transform vision = ResolveVisionTransform();
        if (vision != null)
        {
            return vision;
        }

        Transform sensor = ResolveSensorTransform();
        if (sensor != null)
        {
            return sensor;
        }

        if (!allowSynchronizerFallback)
        {
            UnityEngine.Camera mainCamera = UnityEngine.Camera.main;
            return mainCamera != null ? mainCamera.transform : null;
        }

        try
        {
            if (vGear.head != null)
            {
                Transform head = vGear.head.transform;
                if (IsUsableTrackingTransform(head) && !IsControllerTransform(head))
                {
                    return head;
                }
            }
        }
        catch (Exception)
        {
        }

        try
        {
            if (vCast.head != null)
            {
                Transform head = vCast.head.transform;
                if (IsUsableTrackingTransform(head) && !IsControllerTransform(head))
                {
                    return head;
                }
            }
        }
        catch (Exception)
        {
        }

        UnityEngine.Camera fallbackCamera = UnityEngine.Camera.main;
        return fallbackCamera != null ? fallbackCamera.transform : null;
    }

    public static Transform ResolveUserTransform()
    {
        try
        {
            if (vGear.user != null)
            {
                return vGear.user.transform;
            }
        }
        catch (Exception)
        {
        }

        try
        {
            if (vCast.user != null)
            {
                return vCast.user.transform;
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    public static Transform ResolveControllerTransform()
    {
        try
        {
            if (vGear.controller != null)
            {
                return vGear.controller.transform;
            }
        }
        catch (Exception)
        {
        }

        try
        {
            if (vCast.controller != null)
            {
                return vCast.controller.transform;
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    public static UnityEngine.Camera ResolveViewCamera()
    {
        Transform vision = ResolveVisionTransform();
        if (vision != null)
        {
            UnityEngine.Camera visionCamera = vision.GetComponent<UnityEngine.Camera>();
            if (visionCamera != null)
            {
                return visionCamera;
            }

            UnityEngine.Camera childCamera = vision.GetComponentInChildren<UnityEngine.Camera>();
            if (childCamera != null)
            {
                return childCamera;
            }
        }

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

    private static bool IsControllerTransform(Transform candidate)
    {
        if (candidate == null)
        {
            return false;
        }

        Transform controller = ResolveControllerTransform();
        if (controller == null)
        {
            return false;
        }

        return candidate == controller
            || candidate.IsChildOf(controller)
            || controller.IsChildOf(candidate);
    }

    private static bool IsUsableTrackingTransform(Transform candidate)
    {
        return candidate != null && candidate.gameObject.activeInHierarchy;
    }

    private static void LogTrackingSourceOnce(string source, Transform transform)
    {
        if (Instance == null || !Instance.logResolvedTrackingTransform)
        {
            return;
        }

        string key = source + ":" + (transform != null ? transform.name : "null");
        if (lastLoggedTrackingSource == key)
        {
            return;
        }

        lastLoggedTrackingSource = key;
        Debug.Log(
            "Siege tracking source: " + source
            + (transform != null ? " ('" + GetHierarchyPath(transform) + "')" : " (null)"),
            Instance);
    }

    private static string GetHierarchyPath(Transform transform)
    {
        if (transform == null)
        {
            return string.Empty;
        }

        string path = transform.name;
        Transform parent = transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
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
            switch (vCast.environment)
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
