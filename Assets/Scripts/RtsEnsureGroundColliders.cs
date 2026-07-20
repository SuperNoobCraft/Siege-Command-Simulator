using UnityEngine;

/// <summary>
/// Ensures imported battlefield meshes have MeshColliders so RTS ground projection / pathing can hit them.
/// Place on the battlefield root, or it will auto-run for common field object names.
/// </summary>
[DefaultExecutionOrder(-200)]
public class RtsEnsureGroundColliders : MonoBehaviour
{
    [SerializeField] private bool setLayerToRtsGround = true;
    [SerializeField] private bool includeInactiveChildren = true;
    [SerializeField] private bool runOnAwake = true;

    private static bool loggedAutoEnsure;

    private void Awake()
    {
        if (runOnAwake)
        {
            EnsureCollidersOn(transform, setLayerToRtsGround, includeInactiveChildren);
        }
    }

    /// <summary>
    /// Idempotent scene-wide ensure so units can snap even if this component was not placed yet.
    /// </summary>
    public static void EnsureSceneGroundColliders()
    {
        RtsEnsureGroundColliders[] marked = Object.FindObjectsOfType<RtsEnsureGroundColliders>(true);
        for (int i = 0; i < marked.Length; i++)
        {
            if (marked[i] != null)
            {
                EnsureCollidersOn(
                    marked[i].transform,
                    marked[i].setLayerToRtsGround,
                    marked[i].includeInactiveChildren);
            }
        }

        bool ensuredNamed =
            TryEnsureByName("Hyrule_Field")
            || TryEnsureByName("Hyrule Field")
            || TryEnsureByName("Battlefield")
            || TryEnsureByName("RTS_Ground");

        if (!ensuredNamed && marked.Length == 0)
        {
            // Last resort: any root object whose name contains "Hyrule".
            Transform[] roots = GetSceneRoots();
            for (int i = 0; i < roots.Length; i++)
            {
                Transform root = roots[i];
                if (root == null || root.name.IndexOf("Hyrule", System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                EnsureCollidersOn(root, setLayerToRtsGround: true, includeInactiveChildren: true);
                ensuredNamed = true;
            }
        }

        if (!loggedAutoEnsure && (ensuredNamed || marked.Length > 0))
        {
            loggedAutoEnsure = true;
        }
    }

    private static Transform[] GetSceneRoots()
    {
        UnityEngine.SceneManagement.Scene scene =
            UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return System.Array.Empty<Transform>();
        }

        GameObject[] roots = scene.GetRootGameObjects();
        Transform[] transforms = new Transform[roots.Length];
        for (int i = 0; i < roots.Length; i++)
        {
            transforms[i] = roots[i] != null ? roots[i].transform : null;
        }

        return transforms;
    }

    private static bool TryEnsureByName(string objectName)
    {
        GameObject found = GameObject.Find(objectName);
        if (found == null)
        {
            // Also search inactive / nested by scanning roots.
            UnityEngine.SceneManagement.Scene scene =
                UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                return false;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Transform match = FindChildRecursive(roots[i].transform, objectName);
                if (match != null)
                {
                    EnsureCollidersOn(match, setLayerToRtsGround: true, includeInactiveChildren: true);
                    return true;
                }
            }

            return false;
        }

        EnsureCollidersOn(found.transform, setLayerToRtsGround: true, includeInactiveChildren: true);
        return true;
    }

    private static Transform FindChildRecursive(Transform parent, string objectName)
    {
        if (parent == null)
        {
            return null;
        }

        if (parent.name == objectName)
        {
            return parent;
        }

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindChildRecursive(parent.GetChild(i), objectName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    public static void EnsureCollidersOn(
        Transform root,
        bool setLayerToRtsGround,
        bool includeInactiveChildren)
    {
        if (root == null)
        {
            return;
        }

        int groundLayer = LayerMask.NameToLayer("RTS_Ground");
        int solidLayer = LayerMask.NameToLayer("RTS_Solid");
        int unitLayer = LayerMask.NameToLayer("RTS_Unit");
        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(includeInactiveChildren);
        int added = 0;

        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            if (filter == null || filter.sharedMesh == null)
            {
                continue;
            }

            // Never steal authored solid/unit layers — cliffs/walls tagged RTS_Solid must stay solid.
            if (IsProtectedLayer(filter.gameObject.layer, solidLayer, unitLayer))
            {
                if (filter.GetComponent<Collider>() == null)
                {
                    MeshCollider solidCollider = filter.gameObject.AddComponent<MeshCollider>();
                    solidCollider.sharedMesh = filter.sharedMesh;
                    solidCollider.convex = false;
                    added++;
                }

                continue;
            }

            if (filter.GetComponent<Collider>() != null)
            {
                // Only promote unset/Default meshes to ground — do not overwrite custom layers.
                if (setLayerToRtsGround && groundLayer >= 0 && CanPromoteToGroundLayer(filter.gameObject.layer, groundLayer))
                {
                    filter.gameObject.layer = groundLayer;
                }

                continue;
            }

            MeshCollider meshCollider = filter.gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = filter.sharedMesh;
            meshCollider.convex = false;
            added++;

            if (setLayerToRtsGround && groundLayer >= 0 && CanPromoteToGroundLayer(filter.gameObject.layer, groundLayer))
            {
                filter.gameObject.layer = groundLayer;
            }
        }

        if (added > 0)
        {
            Physics.SyncTransforms();
            Debug.Log(
                "RtsEnsureGroundColliders added "
                + added
                + " MeshCollider(s) under '"
                + root.name
                + "' for RTS ground projection.",
                root);
        }
    }

    private static bool IsProtectedLayer(int layer, int solidLayer, int unitLayer)
    {
        if (solidLayer >= 0 && layer == solidLayer)
        {
            return true;
        }

        if (unitLayer >= 0 && layer == unitLayer)
        {
            return true;
        }

        int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
        return ignoreRaycast >= 0 && layer == ignoreRaycast;
    }

    private static bool CanPromoteToGroundLayer(int layer, int groundLayer)
    {
        // Leave intentionally authored layers alone (walls, props, UI, etc.).
        return layer == 0 || layer == groundLayer;
    }

#if UNITY_EDITOR
    [ContextMenu("Ensure Ground Colliders Now")]
    private void EnsureNowFromContextMenu()
    {
        EnsureCollidersOn(transform, setLayerToRtsGround, includeInactiveChildren);
    }
#endif
}
