using UnityEngine;
using Votanic.vXR.vCast;

/// <summary>
/// Parents / follows a Votanic entity that only exists at play (e.g. Hand2).
/// Attach to LeftHandCube under vGear/Frame.
/// </summary>
public class LeftHandChild : MonoBehaviour
{
    [SerializeField] private string entityName = "Hand2";
    [SerializeField] private Vector3 localPosition;
    [SerializeField] private Vector3 localEulerAngles;
    [Tooltip("If true, keep world pose when first parenting. Otherwise snap to localPosition/localEulerAngles.")]
    [SerializeField] private bool worldPositionStays;
    [Tooltip("If parenting is blocked, still copy Hand2 pose every frame.")]
    [SerializeField] private bool forceFollowIfNotChild = true;
    [SerializeField, Min(0.05f)] private float retryInterval = 0.25f;
    [SerializeField] private bool logAttach = true;

    private Transform boundParent;
    private float nextRetryTime;
    private bool loggedMissing;

    private void LateUpdate()
    {
        if (boundParent == null)
        {
            TryResolveParent();
            if (boundParent == null)
            {
                return;
            }
        }

        if (boundParent == null)
        {
            return;
        }

        if (transform.parent != boundParent)
        {
            Attach(boundParent);
        }

        // Fallback: if something keeps us off Hand2, still track its pose.
        if (forceFollowIfNotChild && transform.parent != boundParent)
        {
            transform.SetPositionAndRotation(
                boundParent.TransformPoint(localPosition),
                boundParent.rotation * Quaternion.Euler(localEulerAngles));
        }
    }

    private void TryResolveParent()
    {
        if (Time.unscaledTime < nextRetryTime)
        {
            return;
        }

        nextRetryTime = Time.unscaledTime + retryInterval;

        Transform found = FindEntityTransform();
        if (found == null)
        {
            if (logAttach && !loggedMissing)
            {
                loggedMissing = true;
                Debug.LogWarning(
                    $"LeftHandChild on '{name}': waiting for '{entityName}' (not found yet).",
                    this);
            }

            return;
        }

        boundParent = found;
        Attach(boundParent);

        if (logAttach)
        {
            Debug.Log(
                $"LeftHandChild on '{name}': bound to '{boundParent.name}' "
                + $"(parent now = {(transform.parent != null ? transform.parent.name : "null")}).",
                this);
        }
    }

    private Transform FindEntityTransform()
    {
        // 1) Prefer the live hierarchy object you move in the editor.
        Transform byName = FindTransformByName(entityName);
        if (byName != null)
        {
            return byName;
        }

        // 2) Votanic entity API (may be unavailable early / on some configs).
        try
        {
            var entity = vCast.GetEntity(entityName);
            if (entity != null)
            {
                if (entity.transform != null)
                {
                    return entity.transform;
                }

                if (entity.gameObject != null)
                {
                    return entity.gameObject.transform;
                }
            }
        }
        catch (System.Exception exception)
        {
            if (logAttach)
            {
                Debug.LogWarning(
                    $"LeftHandChild: vCast.GetEntity(\"{entityName}\") failed: {exception.Message}",
                    this);
            }
        }

        return null;
    }

    private static Transform FindTransformByName(string targetName)
    {
        // Include inactive objects; Hand2 is created at play under User.
#if UNITY_2023_1_OR_NEWER
        Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
#endif
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null || candidate.name != targetName)
            {
                continue;
            }

            // Skip assets / prefabs not in a loaded scene.
            if (!candidate.gameObject.scene.IsValid() || !candidate.gameObject.scene.isLoaded)
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    private void Attach(Transform parent)
    {
        transform.SetParent(parent, worldPositionStays);
        if (!worldPositionStays)
        {
            transform.localPosition = localPosition;
            transform.localRotation = Quaternion.Euler(localEulerAngles);
        }
    }
}
