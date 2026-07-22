using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SiegeSpatialSoundSettings
{
    public AudioClip clip;
    [Tooltip("Per-clip gain. Raise for distant sounds (e.g. explosions) without making nearby ones painfully loud.")]
    [Range(0f, 3f)] public float volume = 1f;
    [Min(0.1f)] public float minDistance = 1f;
    [Min(1f)] public float maxDistance = 50f;
    [Range(0f, 1f)] public float spatialBlend = 1f;
}

/// <summary>
/// Plays 3D positional sound effects at world locations. Assign clips from Assets/Audio/SoundEffects.
/// </summary>
[DefaultExecutionOrder(125)]
public class SiegeSoundEffects : MonoBehaviour
{
    public static SiegeSoundEffects Instance { get; private set; }

    [Header("Sound Effects")]
    [SerializeField] private SiegeSpatialSoundSettings arrowImpact = new SiegeSpatialSoundSettings
    {
        volume = 1f,
        minDistance = 0.5f,
        maxDistance = 18f
    };

    [Tooltip("Enemy castle archers firing at the commander. Keep loud/long-range so it carries across the battlefield.")]
    [SerializeField] private SiegeSpatialSoundSettings arrowShoot = new SiegeSpatialSoundSettings
    {
        volume = 2.2f,
        minDistance = 8f,
        maxDistance = 140f,
        spatialBlend = 0.75f
    };

    [SerializeField] private SiegeSpatialSoundSettings cannonShot = new SiegeSpatialSoundSettings
    {
        volume = 1.1f,
        minDistance = 2f,
        maxDistance = 80f
    };

    [SerializeField] private SiegeSpatialSoundSettings explosion = new SiegeSpatialSoundSettings
    {
        volume = 1.6f,
        minDistance = 4f,
        maxDistance = 120f
    };

    [SerializeField] private SiegeSpatialSoundSettings marching = new SiegeSpatialSoundSettings
    {
        volume = 0.55f,
        minDistance = 2f,
        maxDistance = 28f
    };

    [SerializeField] private SiegeSpatialSoundSettings meleeCombat = new SiegeSpatialSoundSettings
    {
        volume = 0.85f,
        minDistance = 1f,
        maxDistance = 22f
    };

    [Tooltip("Looping crackle that rides flaming commander arrows. Keep quiet and short-range for a pass-by whoosh.")]
    [SerializeField] private SiegeSpatialSoundSettings flamingArrowPassby = new SiegeSpatialSoundSettings
    {
        volume = 0.22f,
        minDistance = 0.6f,
        maxDistance = 12f,
        spatialBlend = 1f
    };

    [Tooltip("How long the flame sound keeps fading after a flaming arrow despawns / misses.")]
    [SerializeField, Min(0.05f)] private float flamingArrowDespawnFadeSeconds = 1.5f;

    [Header("Pooling")]
    [SerializeField, Min(4)] private int pooledSourceCount = 20;

    private readonly List<PooledSpatialSource> pooledSources = new List<PooledSpatialSource>();
    private Transform poolRoot;

    public SiegeSpatialSoundSettings MarchingSettings => marching;

    public AudioClip FlamingArrowPassbyClip => flamingArrowPassby != null ? flamingArrowPassby.clip : null;

    public SiegeSpatialSoundSettings FlamingArrowPassbySettings => flamingArrowPassby;

    public float FlamingArrowDespawnFadeSeconds => Mathf.Max(0.05f, flamingArrowDespawnFadeSeconds);

    /// <summary>
    /// Continues an already-playing flaming-arrow AudioSource after the arrow is destroyed.
    /// Pass a detached host (not created from OnDestroy).
    /// </summary>
    public void FadeOutDetachedFlamingArrow(AudioSource detachedSource)
    {
        if (detachedSource == null)
        {
            return;
        }

        if (!isActiveAndEnabled || !Application.isPlaying)
        {
            Destroy(detachedSource.gameObject);
            return;
        }

        EnsurePool();
        Transform parent = poolRoot != null ? poolRoot : transform;
        detachedSource.transform.SetParent(parent, worldPositionStays: true);
        detachedSource.name = "FlamingArrowPassbyLinger";

        float startVolume = detachedSource.volume;
        if (startVolume <= 0.001f && flamingArrowPassby != null)
        {
            startVolume = Mathf.Max(0.05f, flamingArrowPassby.volume);
            detachedSource.volume = startVolume;
        }

        StartCoroutine(FadeOutAndDestroy(
            detachedSource.gameObject,
            detachedSource,
            startVolume,
            FlamingArrowDespawnFadeSeconds));
    }

    private IEnumerator FadeOutAndDestroy(GameObject lingerObject, AudioSource lingerSource, float startVolume, float fadeSeconds)
    {
        float elapsed = 0f;
        float duration = Mathf.Max(0.05f, fadeSeconds);
        while (elapsed < duration && lingerSource != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - ((1f - t) * (1f - t));
            lingerSource.volume = startVolume * (1f - eased);
            yield return null;
        }

        if (lingerSource != null)
        {
            lingerSource.Stop();
        }

        if (lingerObject != null)
        {
            Destroy(lingerObject);
        }
    }

    private void CleanupFlamingArrowLingers()
    {
        if (poolRoot == null)
        {
            return;
        }

        for (int i = poolRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = poolRoot.GetChild(i);
            if (child == null || !child.name.StartsWith("FlamingArrowPassbyLinger", System.StringComparison.Ordinal))
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple SiegeSoundEffects instances found. Using the most recent one.", this);
        }

        Instance = this;
        EnsurePool();
    }

    private void OnDestroy()
    {
        CleanupFlamingArrowLingers();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnDisable()
    {
        SiegeCommanderArrowHealth.CommanderHitRegistered -= HandleCommanderHit;
        TroopCombat.MeleeAttackPerformed -= HandleMeleeAttack;
        CleanupFlamingArrowLingers();
    }

    private void OnEnable()
    {
        SiegeCommanderArrowHealth.CommanderHitRegistered += HandleCommanderHit;
        TroopCombat.MeleeAttackPerformed += HandleMeleeAttack;
        EnsureRegimentAudioComponents();
    }

    public void PlayArrowImpact(Vector3 position)
    {
        PlayAtPosition(arrowImpact, position);
    }

    public void PlayArrowShoot(Vector3 position)
    {
        SiegeSpatialSoundSettings settings = arrowShoot;
        if (settings.clip == null && arrowImpact.clip != null)
        {
            // Fallback until a dedicated shoot clip is assigned in the Inspector.
            settings = new SiegeSpatialSoundSettings
            {
                clip = arrowImpact.clip,
                volume = Mathf.Max(1.6f, arrowShoot.volume),
                minDistance = arrowShoot.minDistance,
                maxDistance = arrowShoot.maxDistance,
                spatialBlend = arrowShoot.spatialBlend
            };
        }

        PlayAtPosition(settings, position);
    }

    public void PlayCannonShot(Vector3 position)
    {
        PlayAtPosition(ResolveOrResources(cannonShot, "SoundEffects/cannonShot"), position);
    }

    public void PlayCannonShotAtOrigins(Transform originParent)
    {
        PlayAtOrigins(ResolveOrResources(cannonShot, "SoundEffects/cannonShot"), originParent);
    }

    public void PlayCannonLand(Vector3 position)
    {
        SiegeSpatialSoundSettings settings = ResolveOrResources(explosion, "SoundEffects/explosion");
        if (settings.clip == null)
        {
            settings = ResolveOrResources(cannonShot, "SoundEffects/cannonShot");
        }

        PlayAtPosition(settings, position);
    }

    public void PlayExplosion(Vector3 position)
    {
        PlayAtPosition(ResolveOrResources(explosion, "SoundEffects/explosion"), position);
    }

    public void PlayExplosionAtOrigins(Transform originParent)
    {
        PlayAtOrigins(ResolveOrResources(explosion, "SoundEffects/explosion"), originParent);
    }

    public void PlayMeleeCombat(Vector3 position)
    {
        PlayAtPosition(meleeCombat, position);
    }

    public AudioSource CreateMarchingSource(Transform followTarget)
    {
        if (marching.clip == null || followTarget == null)
        {
            return null;
        }

        GameObject marchingObject = new GameObject("MarchingAudio");
        marchingObject.transform.SetParent(followTarget, worldPositionStays: false);
        marchingObject.transform.localPosition = Vector3.zero;

        AudioSource source = marchingObject.AddComponent<AudioSource>();
        ApplySpatialSettings(source, marching);
        source.loop = true;
        source.clip = marching.clip;
        source.volume = marching.volume;
        source.playOnAwake = false;
        return source;
    }

    private void HandleCommanderHit(int hitCount, Vector3 hitPoint)
    {
        PlayArrowImpact(hitPoint);
    }

    private void HandleMeleeAttack(TroopCombat regiment, Vector3 position)
    {
        if (regiment == null || regiment.CurrentState == TroopCombat.State.Dead)
        {
            return;
        }

        PlayMeleeCombat(position);
    }

    private void PlayAtOrigins(SiegeSpatialSoundSettings settings, Transform originParent)
    {
        if (settings.clip == null || originParent == null)
        {
            return;
        }

        if (originParent.childCount == 0)
        {
            PlayAtPosition(settings, originParent.position);
            return;
        }

        // Cinematic parents can have dozens of VFX children — playing at every child
        // exhausts the spatial pool and is needlessly loud. Cap to a few origins.
        const int maxOrigins = 4;
        int played = 0;
        for (int i = 0; i < originParent.childCount && played < maxOrigins; i++)
        {
            Transform child = originParent.GetChild(i);
            if (child == null || !child.gameObject.activeInHierarchy)
            {
                continue;
            }

            PlayAtPosition(settings, child.position);
            played++;
        }

        if (played == 0)
        {
            PlayAtPosition(settings, originParent.position);
        }
    }

    private void PlayAtPosition(SiegeSpatialSoundSettings settings, Vector3 position)
    {
        if (settings.clip == null)
        {
            return;
        }

        PooledSpatialSource pooled = GetAvailablePooledSource();
        if (pooled == null)
        {
            // Pool busy — still play a one-shot so cannon/explosion never go fully silent.
            AudioSource.PlayClipAtPoint(
                settings.clip,
                position,
                Mathf.Clamp(settings.volume, 0f, 1f));
            return;
        }

        pooled.IsActive = true;
        pooled.Source.transform.position = position;
        ApplySpatialSettings(pooled.Source, settings);
        pooled.Source.PlayOneShot(settings.clip, settings.volume);
        StartCoroutine(ReturnSourceWhenFinished(pooled, settings.clip.length));
    }

    private static SiegeSpatialSoundSettings ResolveOrResources(SiegeSpatialSoundSettings settings, string resourcesPath)
    {
        if (settings == null)
        {
            settings = new SiegeSpatialSoundSettings();
        }

        if (settings.clip != null)
        {
            return settings;
        }

        AudioClip fallback = Resources.Load<AudioClip>(resourcesPath);
        if (fallback == null)
        {
            return settings;
        }

        return new SiegeSpatialSoundSettings
        {
            clip = fallback,
            volume = settings.volume > 0.01f ? settings.volume : 1f,
            minDistance = Mathf.Max(0.1f, settings.minDistance),
            maxDistance = Mathf.Max(1f, settings.maxDistance),
            spatialBlend = settings.spatialBlend
        };
    }

    private static void ApplySpatialSettings(AudioSource source, SiegeSpatialSoundSettings settings)
    {
        source.spatialBlend = settings.spatialBlend;
        source.minDistance = settings.minDistance;
        source.maxDistance = settings.maxDistance;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.dopplerLevel = 0f;
        source.spread = 0f;
    }

    private IEnumerator ReturnSourceWhenFinished(PooledSpatialSource pooled, float clipLength)
    {
        float waitDuration = Mathf.Max(0.05f, clipLength);
        yield return new WaitForSeconds(waitDuration);

        if (pooled != null && pooled.Source != null)
        {
            pooled.Source.Stop();
            pooled.IsActive = false;
        }
    }

    private void EnsurePool()
    {
        if (poolRoot == null)
        {
            poolRoot = new GameObject("SpatialAudioPool").transform;
            poolRoot.SetParent(transform, worldPositionStays: false);
        }

        while (pooledSources.Count < pooledSourceCount)
        {
            GameObject sourceObject = new GameObject("SpatialOneShot_" + pooledSources.Count);
            sourceObject.transform.SetParent(poolRoot, worldPositionStays: false);
            AudioSource source = sourceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            pooledSources.Add(new PooledSpatialSource(source));
        }
    }

    private PooledSpatialSource GetAvailablePooledSource()
    {
        for (int i = 0; i < pooledSources.Count; i++)
        {
            PooledSpatialSource pooled = pooledSources[i];
            if (!pooled.IsActive)
            {
                return pooled;
            }
        }

        return null;
    }

    private void EnsureRegimentAudioComponents()
    {
        TroopCombat[] regiments = FindObjectsOfType<TroopCombat>(includeInactive: true);
        for (int i = 0; i < regiments.Length; i++)
        {
            TroopCombat regiment = regiments[i];
            if (regiment == null || regiment.GetComponent<SiegeRegimentAudio>() != null)
            {
                continue;
            }

            regiment.gameObject.AddComponent<SiegeRegimentAudio>();
        }
    }

    private sealed class PooledSpatialSource
    {
        public readonly AudioSource Source;
        public bool IsActive;

        public PooledSpatialSource(AudioSource source)
        {
            Source = source;
        }
    }
}
