using System.Collections;
using UnityEngine;

/// <summary>
/// Hides child effect objects under assigned parents, then reveals them when cannons fire or are overrun.
/// Keep the parent objects active; disable their children in the hierarchy (or let this script hide them on start).
/// </summary>
[DefaultExecutionOrder(150)]
public class SiegeRevealChildren : MonoBehaviour
{
    [Header("Effect Parents")]
    [Tooltip("Intact enemy walls visible before cannons fire. This parent is disabled when the walls are destroyed.")]
    [SerializeField] private Transform cannonFireIntactWallsParent;
    [Tooltip("Broken enemy walls and explosion effects. Children appear when friendly cannons fire.")]
    [SerializeField] private Transform cannonFireEffectsParent;
    [Tooltip("Parent empty object whose children appear when enemy forces overrun a cannon.")]
    [SerializeField] private Transform cannonOverrunEffectsParent;

    [Header("Cannon Fire Sequence")]
    [Tooltip("3D position(s) for the cannon shot sound. Uses this transform if it has no children.")]
    [SerializeField] private Transform cannonShotAudioOrigin;
    [Tooltip("Seconds after the cannon shot sound before explosion visuals and sound play.")]
    [SerializeField, Min(0f)] private float cannonFireExplosionDelaySeconds = 0.5f;

    [Header("Behavior")]
    [SerializeField] private bool hideChildrenOnStart = true;
    [SerializeField] private bool revealOnlyOnce = true;
    [SerializeField] private bool playEffectsWithUnscaledTime = true;

    private bool cannonFireRevealed;
    private bool overrunRevealed;
    private bool isSubscribed;
    private Coroutine cannonFireSequenceCoroutine;

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
        StopCannonFireSequence();
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
        SetParentActive(cannonFireIntactWallsParent, false);
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
        StopCannonFireSequence();
        StopEffectSystems(cannonFireEffectsParent);
        SetChildrenActive(cannonFireEffectsParent, false);
        SetParentActive(cannonFireIntactWallsParent, true);
        cannonFireRevealed = false;
    }

    public void HideCannonOverrunEffects()
    {
        StopEffectSystems(cannonOverrunEffectsParent);
        SetChildrenActive(cannonOverrunEffectsParent, false);
        overrunRevealed = false;
    }

    public void ResetForMatchStart()
    {
        StopCannonFireSequence();
        HideCannonFireEffects();
        HideCannonOverrunEffects();
    }

    private void HandleCannonsFired()
    {
        StopCannonFireSequence();
        cannonFireSequenceCoroutine = StartCoroutine(PlayCannonFireSequence());
    }

    private void HandleCannonOverrun()
    {
        RevealCannonOverrunEffects();
        PlayOverrunExplosionSound();
    }

    private IEnumerator PlayCannonFireSequence()
    {
        PlayCannonShotSound();

        if (cannonFireExplosionDelaySeconds > 0f)
        {
            if (playEffectsWithUnscaledTime)
            {
                yield return new WaitForSecondsRealtime(cannonFireExplosionDelaySeconds);
            }
            else
            {
                yield return new WaitForSeconds(cannonFireExplosionDelaySeconds);
            }
        }

        RevealCannonFireEffects();
        PlayCannonFireExplosionSound();
        PlayCannonsFiredVoiceline();
        cannonFireSequenceCoroutine = null;
    }

    private static void PlayCannonsFiredVoiceline()
    {
        SiegeAudioManager audioManager = SiegeAudioManager.Instance;
        if (audioManager == null)
        {
            return;
        }

        audioManager.PlayCannonsFiredVoiceline();
    }

    private void PlayCannonShotSound()
    {
        SiegeSoundEffects soundEffects = SiegeSoundEffects.Instance;
        if (soundEffects == null)
        {
            return;
        }

        Transform shotOrigin = cannonShotAudioOrigin != null ? cannonShotAudioOrigin : cannonFireEffectsParent;
        soundEffects.PlayCannonShotAtOrigins(shotOrigin);
    }

    private void PlayCannonFireExplosionSound()
    {
        SiegeSoundEffects soundEffects = SiegeSoundEffects.Instance;
        if (soundEffects == null)
        {
            return;
        }

        soundEffects.PlayExplosionAtOrigins(cannonFireEffectsParent);
    }

    private void PlayOverrunExplosionSound()
    {
        SiegeSoundEffects soundEffects = SiegeSoundEffects.Instance;
        if (soundEffects == null)
        {
            return;
        }

        soundEffects.PlayExplosionAtOrigins(cannonOverrunEffectsParent);
    }

    private void StopCannonFireSequence()
    {
        if (cannonFireSequenceCoroutine == null)
        {
            return;
        }

        StopCoroutine(cannonFireSequenceCoroutine);
        cannonFireSequenceCoroutine = null;
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

    private static void StopEffectSystems(Transform parent)
    {
        if (parent == null)
        {
            return;
        }

        ParticleSystem[] particleSystems = parent.GetComponentsInChildren<ParticleSystem>(includeInactive: true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            if (particleSystems[i] != null)
            {
                particleSystems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
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

    private static void SetParentActive(Transform parent, bool active)
    {
        if (parent == null)
        {
            return;
        }

        parent.gameObject.SetActive(active);
    }
}
