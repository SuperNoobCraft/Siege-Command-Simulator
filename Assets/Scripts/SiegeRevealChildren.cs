using UnityEngine;

/// <summary>
/// Hides child effect objects under assigned parents, then reveals them when cannons fire or are overrun.
/// Keep the parent objects active; disable their children in the hierarchy (or let this script hide them on start).
/// </summary>
[DefaultExecutionOrder(150)]
public class SiegeRevealChildren : MonoBehaviour
{
    [Header("Effect Parents")]
    [Tooltip("Parent empty object whose children appear when friendly cannons fire.")]
    [SerializeField] private Transform cannonFireEffectsParent;
    [Tooltip("Parent empty object whose children appear when enemy forces overrun a cannon.")]
    [SerializeField] private Transform cannonOverrunEffectsParent;

    [Header("Behavior")]
    [SerializeField] private bool hideChildrenOnStart = true;
    [SerializeField] private bool revealOnlyOnce = true;
    [SerializeField] private bool playEffectsWithUnscaledTime = true;

    private bool cannonFireRevealed;
    private bool overrunRevealed;
    private bool isSubscribed;

    private void Start()
    {
        if (hideChildrenOnStart)
        {
            SetChildrenActive(cannonFireEffectsParent, false);
            SetChildrenActive(cannonOverrunEffectsParent, false);
        }

        TrySubscribe();
    }

    private void OnEnable()
    {
        TrySubscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void TrySubscribe()
    {
        if (isSubscribed)
        {
            return;
        }

        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager == null)
        {
            return;
        }

        manager.CannonsFired += HandleCannonsFired;
        manager.CannonOverrun += HandleCannonOverrun;
        isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!isSubscribed)
        {
            return;
        }

        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager != null)
        {
            manager.CannonsFired -= HandleCannonsFired;
            manager.CannonOverrun -= HandleCannonOverrun;
        }

        isSubscribed = false;
    }

    public void RevealCannonFireEffects()
    {
        if (revealOnlyOnce && cannonFireRevealed)
        {
            return;
        }

        RevealParentChildren(cannonFireEffectsParent);
        cannonFireRevealed = true;
    }

    public void RevealCannonOverrunEffects()
    {
        if (revealOnlyOnce && overrunRevealed)
        {
            return;
        }

        RevealParentChildren(cannonOverrunEffectsParent);
        overrunRevealed = true;
    }

    public void HideCannonFireEffects()
    {
        SetChildrenActive(cannonFireEffectsParent, false);
        cannonFireRevealed = false;
    }

    public void HideCannonOverrunEffects()
    {
        SetChildrenActive(cannonOverrunEffectsParent, false);
        overrunRevealed = false;
    }

    private void HandleCannonsFired()
    {
        RevealCannonFireEffects();
    }

    private void HandleCannonOverrun()
    {
        RevealCannonOverrunEffects();
    }

    private void RevealParentChildren(Transform parent)
    {
        if (parent == null)
        {
            return;
        }

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            child.gameObject.SetActive(true);
            if (playEffectsWithUnscaledTime)
            {
                PlayEffectSystems(child);
            }
        }
    }

    private static void PlayEffectSystems(Transform root)
    {
        ParticleSystem[] particleSystems = root.GetComponentsInChildren<ParticleSystem>(includeInactive: true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem particleSystem = particleSystems[i];
            if (particleSystem == null)
            {
                continue;
            }

            ParticleSystem.MainModule main = particleSystem.main;
            main.useUnscaledTime = true;
            particleSystem.Play(true);
        }

        Animator[] animators = root.GetComponentsInChildren<Animator>(includeInactive: true);
        for (int i = 0; i < animators.Length; i++)
        {
            Animator animator = animators[i];
            if (animator == null)
            {
                continue;
            }

            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        }
    }

    private static void SetChildrenActive(Transform parent, bool active)
    {
        if (parent == null)
        {
            return;
        }

        for (int i = 0; i < parent.childCount; i++)
        {
            parent.GetChild(i).gameObject.SetActive(active);
        }
    }
}
