using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Votanic.vNet.Networking;
using Votanic.vXR.vGear.Networking;

/// <summary>
/// Keeps remote XR opponents visible. Votanic defaults <see cref="EntityDisplay"/> to None, so
/// head/hand prefabs flash for a frame on spawn then get destroyed/hidden while nametags remain.
/// Optionally places a static body that follows each remote head's position and Yaw only
/// (no pitch/roll — looking down does not tip the body). Assign host + client models —
/// the remote peer gets the opposite of this machine's network-config role (host/client).
/// Put on the same object as <see cref="vGear_Networking"/> (XRNetworkManager), or let
/// <see cref="SiegePvpSession"/> / <see cref="VGearNetworkConfigLoader"/> add it.
/// </summary>
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public class SiegeRemoteOpponentVisuals : MonoBehaviour
{
    [SerializeField] private vGear_Networking networking;

    [Header("Votanic entity / nametag visibility")]
    [Tooltip("Show remote head + hands. Applied locally only (sync off) so peers cannot fight over None.")]
    [SerializeField] private EntityDisplay entityDisplay = EntityDisplay.OthersEntities;
    [SerializeField] private IdentityDisplay identityDisplay = IdentityDisplay.OthersName;
    [SerializeField, Min(0.1f)] private float reapplyDisplayIntervalSeconds = 0.5f;

    [Header("Optional full-body stand-in")]
    [Tooltip("Body for the Host / Attacker. Used when the remote peer is host (your network-config role is client).")]
    [SerializeField] private GameObject hostBodyPrefab;
    [Tooltip("Body for the Client / Defender. Used when the remote peer is client (your network-config role is host).")]
    [SerializeField] private GameObject clientBodyPrefab;
    [SerializeField] private Vector3 bodyLocalPosition = new Vector3(0f, -1.55f, 0f);
    [Tooltip("Extra model orientation baked on top of head yaw (usually keep at 0).")]
    [SerializeField] private Vector3 bodyLocalEulerAngles = Vector3.zero;
    [SerializeField] private Vector3 bodyLocalScale = Vector3.one;
    [Tooltip("Hide floating head/hand models when a body prefab is attached (nametag stays).")]
    [SerializeField] private bool hideFloatingPartsWhenBodyAttached = true;

    private static FieldInfo networkUserEntityDisplayField;
    private static FieldInfo networkUserIdentityDisplayField;
    private static FieldInfo networkUserModelsField;
    private static MethodInfo networkUserShowEntityMethod;
    private static MethodInfo networkUserShowIdentityMethod;
    private static bool networkUserApiResolved;

    private readonly Dictionary<int, GameObject> bodyByUserId = new Dictionary<int, GameObject>();
    private readonly Dictionary<int, GameObject> bodyPrefabByUserId = new Dictionary<int, GameObject>();
    private float nextDisplayRefreshTime;

    private void Reset()
    {
        networking = GetComponent<vGear_Networking>();
    }

    private void Awake()
    {
        if (networking == null)
        {
            networking = GetComponent<vGear_Networking>();
        }

        if (networking == null)
        {
            networking = FindObjectOfType<vGear_Networking>();
        }

        ResolveNetworkUserApi();
        ApplyEntityDisplay(force: true);
    }

    private void OnEnable()
    {
        nextDisplayRefreshTime = 0f;
        ApplyEntityDisplay(force: true);
    }

    private void OnDisable()
    {
        ClearAllBodies();
    }

    /// <summary>
    /// Pushed from <see cref="SiegePvpSession"/> so host/client body models can be assigned in the
    /// scene without hunting the XRNetworkManager prefab instance.
    /// Remote body is chosen from <see cref="vGear_Networking.type"/> (network-config role):
    /// local Host → show clientBodyPrefab on the peer; local Client → show hostBodyPrefab on the peer.
    /// </summary>
    public void ConfigureFromSession(
        GameObject hostBody,
        GameObject clientBody,
        Vector3 localPosition,
        Vector3 localEuler,
        Vector3 localScale,
        bool hideFloatingParts)
    {
        if (hostBody != null)
        {
            hostBodyPrefab = hostBody;
        }

        if (clientBody != null)
        {
            clientBodyPrefab = clientBody;
        }

        bodyLocalPosition = localPosition;
        bodyLocalEulerAngles = localEuler;
        bodyLocalScale = localScale;
        hideFloatingPartsWhenBodyAttached = hideFloatingParts;
        nextDisplayRefreshTime = 0f;
    }

    /// <summary>
    /// Prefab for the remote peer: opposite of this machine's host/client role from network config.
    /// </summary>
    private GameObject ResolveRemoteBodyPrefab()
    {
        if (networking == null)
        {
            return hostBodyPrefab != null ? hostBodyPrefab : clientBodyPrefab;
        }

        // Local host is looking at the client peer; local client is looking at the host peer.
        return networking.type == UserType.Host ? clientBodyPrefab : hostBodyPrefab;
    }

    private void LateUpdate()
    {
        if (networking == null)
        {
            return;
        }

        if (Time.unscaledTime >= nextDisplayRefreshTime)
        {
            ApplyEntityDisplay(force: false);
            nextDisplayRefreshTime = Time.unscaledTime + Mathf.Max(0.1f, reapplyDisplayIntervalSeconds);
        }

        RefreshRemoteUsers();
    }

    private void ApplyEntityDisplay(bool force)
    {
        if (networking == null)
        {
            return;
        }

        try
        {
            // Always write the field — SetEntityDisplay with sync can be overwritten by peer None.
            networking.entityDisplay = entityDisplay;
            networking.identityDisplay = identityDisplay;

            // sync:false — each machine controls what it sees; avoids host/client fighting.
            networking.SetEntityDisplay(entityDisplay, false);
            networking.SetIdentityDisplay(identityDisplay, false);
        }
        catch (Exception ex)
        {
            if (force)
            {
                Debug.LogWarning("SiegeRemoteOpponentVisuals: failed to set entity display — " + ex.Message, this);
            }
        }
    }

    private void RefreshRemoteUsers()
    {
        HashSet<int> seen = new HashSet<int>();
        IEnumerable<vGear_NetworkUser> users;
        try
        {
            users = networking.GetAllNetworkUsers();
        }
        catch
        {
            return;
        }

        if (users == null)
        {
            return;
        }

        foreach (vGear_NetworkUser user in users)
        {
            if (user == null)
            {
                continue;
            }

            int userId = (int)user.userID;
            bool isLocal = userId == networking.networkID;
            if (isLocal)
            {
                continue;
            }

            seen.Add(userId);
            KeepRemoteEntityVisible(user);
            RefreshBodyForUser(user, userId);
        }

        RemoveStaleBodies(seen);
    }

    private void KeepRemoteEntityVisible(vGear_NetworkUser user)
    {
        ResolveNetworkUserApi();

        bool wantFloatingParts = !(hideFloatingPartsWhenBodyAttached && ResolveRemoteBodyPrefab() != null);
        bool needsShow = wantFloatingParts && !HasActiveRenderer(user.head);

        // ShowEntity / entityDisplay are internal on Votanic — drive them via reflection.
        try
        {
            if (networkUserEntityDisplayField != null)
            {
                networkUserEntityDisplayField.SetValue(user, entityDisplay);
            }

            if (networkUserIdentityDisplayField != null)
            {
                networkUserIdentityDisplayField.SetValue(user, identityDisplay);
            }

            if (needsShow && networkUserShowEntityMethod != null)
            {
                networkUserShowEntityMethod.Invoke(user, new object[] { entityDisplay });
            }

            if (networkUserShowIdentityMethod != null)
            {
                networkUserShowIdentityMethod.Invoke(user, new object[] { identityDisplay });
            }
        }
        catch (Exception)
        {
        }

        if (wantFloatingParts)
        {
            SetFloatingPartModelsActive(user, true);
            ForceRenderersEnabled(user.head);
            ForceRenderersEnabled(user.rightHand);
            ForceRenderersEnabled(user.leftHand);
        }
        else
        {
            SetFloatingPartModelsActive(user, false);
        }

        GameObject[] models = null;
        try
        {
            if (networkUserModelsField != null)
            {
                models = networkUserModelsField.GetValue(user) as GameObject[];
            }
        }
        catch (Exception)
        {
        }

        if (models == null)
        {
            return;
        }

        for (int i = 0; i < models.Length; i++)
        {
            GameObject model = models[i];
            if (model == null)
            {
                continue;
            }

            if (model.activeSelf != wantFloatingParts)
            {
                model.SetActive(wantFloatingParts);
            }
        }
    }

    private static bool HasActiveRenderer(Transform root)
    {
        if (root == null)
        {
            return false;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].enabled && renderers[i].gameObject.activeInHierarchy)
            {
                return true;
            }
        }

        return false;
    }

    private void RefreshBodyForUser(vGear_NetworkUser user, int userId)
    {
        GameObject preferredPrefab = ResolveRemoteBodyPrefab();
        if (preferredPrefab == null)
        {
            if (bodyByUserId.TryGetValue(userId, out GameObject existing) && existing != null)
            {
                Destroy(existing);
                bodyByUserId.Remove(userId);
                bodyPrefabByUserId.Remove(userId);
            }

            return;
        }

        Transform head = user.head;
        if (head == null)
        {
            return;
        }

        bool hasBody = bodyByUserId.TryGetValue(userId, out GameObject body) && body != null;
        bool wrongPrefab = hasBody
            && bodyPrefabByUserId.TryGetValue(userId, out GameObject usedPrefab)
            && usedPrefab != preferredPrefab;

        if (wrongPrefab)
        {
            Destroy(body);
            bodyByUserId.Remove(userId);
            bodyPrefabByUserId.Remove(userId);
            hasBody = false;
            body = null;
        }

        if (!hasBody)
        {
            // Unparented so head pitch/roll never tips the body — we follow pose in SyncBodyToHeadYaw.
            body = Instantiate(preferredPrefab);
            body.name = preferredPrefab.name + "_Remote_" + userId;
            StripGameplayComponents(body);
            bodyByUserId[userId] = body;
            bodyPrefabByUserId[userId] = preferredPrefab;

            if (hideFloatingPartsWhenBodyAttached)
            {
                SetFloatingPartModelsActive(user, false);
            }
        }

        SyncBodyToHeadYaw(body.transform, head);
    }

    /// <summary>
    /// Track head world position with the configured offset, but only copy yaw (Y) rotation.
    /// Prefab facing is corrected by a fixed 180° yaw — editor root rotation is overwritten here.
    /// </summary>
    private void SyncBodyToHeadYaw(Transform body, Transform head)
    {
        float yaw = head.eulerAngles.y + 180f;
        Quaternion yawOnly = Quaternion.Euler(0f, yaw, 0f);
        body.SetPositionAndRotation(
            head.position + yawOnly * bodyLocalPosition,
            yawOnly * Quaternion.Euler(bodyLocalEulerAngles));
        body.localScale = bodyLocalScale;
    }

    private static void StripGameplayComponents(GameObject body)
    {
        // Body is cosmetic only — disable motors/combat/colliders that may ship on a troop prefab.
        Collider[] colliders = body.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
            {
                colliders[i].enabled = false;
            }
        }

        Rigidbody[] bodies = body.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i] != null)
            {
                bodies[i].isKinematic = true;
                bodies[i].detectCollisions = false;
            }
        }

        MonoBehaviour[] behaviours = body.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
            {
                continue;
            }

            string typeName = behaviour.GetType().Name;
            if (typeName == "TroopCombat"
                || typeName == "RtsUnitMotor"
                || typeName == "EnemyRegimentAI"
                || typeName == "RtsUnitHighlight")
            {
                behaviour.enabled = false;
            }
        }

        Animator[] animators = body.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            if (animators[i] != null)
            {
                animators[i].enabled = false;
            }
        }
    }

    private void RemoveStaleBodies(HashSet<int> seen)
    {
        List<int> stale = null;
        foreach (KeyValuePair<int, GameObject> pair in bodyByUserId)
        {
            if (seen.Contains(pair.Key))
            {
                continue;
            }

            if (stale == null)
            {
                stale = new List<int>();
            }

            stale.Add(pair.Key);
            if (pair.Value != null)
            {
                Destroy(pair.Value);
            }
        }

        if (stale == null)
        {
            return;
        }

        for (int i = 0; i < stale.Count; i++)
        {
            bodyByUserId.Remove(stale[i]);
            bodyPrefabByUserId.Remove(stale[i]);
        }
    }

    private static void SetFloatingPartModelsActive(vGear_NetworkUser user, bool active)
    {
        SetPartChildrenActive(user.head, active);
        SetPartChildrenActive(user.rightHand, active);
        SetPartChildrenActive(user.leftHand, active);
    }

    private static void SetPartChildrenActive(Transform partRoot, bool active)
    {
        if (partRoot == null || partRoot.childCount <= 0)
        {
            return;
        }

        // Activate mesh roots; skip nametag UI and our attached body stand-in.
        for (int i = 0; i < partRoot.childCount; i++)
        {
            Transform child = partRoot.GetChild(i);
            if (child == null)
            {
                continue;
            }

            if (child.GetComponentInChildren<UnityEngine.UI.Text>(true) != null)
            {
                continue;
            }

            if (child.name.IndexOf("Remote_", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            if (child.gameObject.activeSelf != active)
            {
                child.gameObject.SetActive(active);
            }
        }
    }

    private static void ForceRenderersEnabled(Transform root)
    {
        if (root == null)
        {
            return;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            if (!renderer.enabled)
            {
                renderer.enabled = true;
            }

            if (!renderer.gameObject.activeSelf)
            {
                renderer.gameObject.SetActive(true);
            }
        }
    }

    private static void ResolveNetworkUserApi()
    {
        if (networkUserApiResolved)
        {
            return;
        }

        networkUserApiResolved = true;
        Type userType = typeof(vGear_NetworkUser);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        networkUserEntityDisplayField = userType.GetField("entityDisplay", flags);
        networkUserIdentityDisplayField = userType.GetField("identityDisplay", flags);
        networkUserModelsField = userType.GetField("models", flags);
        networkUserShowEntityMethod = userType.GetMethod(
            "ShowEntity",
            flags,
            null,
            new[] { typeof(EntityDisplay) },
            null);
        networkUserShowIdentityMethod = userType.GetMethod(
            "ShowIdentity",
            flags,
            null,
            new[] { typeof(IdentityDisplay) },
            null);
    }

    private void ClearAllBodies()
    {
        foreach (KeyValuePair<int, GameObject> pair in bodyByUserId)
        {
            if (pair.Value != null)
            {
                Destroy(pair.Value);
            }
        }

        bodyByUserId.Clear();
        bodyPrefabByUserId.Clear();
    }
}
