using UnityEngine;

/// <summary>
/// Quiet looping 3D flame crackle that rides with a flaming arrow so you hear it as it passes.
/// Settings live on SiegeSoundEffects. Before the arrow is destroyed the AudioSource is detached
/// (same playing instance) and faded out — no OnDestroy spawn, no hard cutoff from Stop/Play.
/// </summary>
[DisallowMultipleComponent]
public class FlamingArrowPassbyAudio : MonoBehaviour
{
    private const string ResourcesClipPath = "SoundEffects/fire";
    private const string AudioHostName = "FlamePassbyAudio";

    private AudioSource source;
    private float volume = 0.22f;
    private float minDistance = 0.6f;
    private float maxDistance = 12f;
    private float spatialBlend = 1f;
    private float dopplerLevel = 0.4f;
    private bool releasedForLinger;

    private void Awake()
    {
        EnsurePlaying();
    }

    private void OnEnable()
    {
        if (!releasedForLinger)
        {
            EnsurePlaying();
        }
    }

    public void EnsurePlaying()
    {
        if (releasedForLinger)
        {
            return;
        }

        AudioClip clip = ResolveClip();
        if (clip == null)
        {
            return;
        }

        source = EnsureChildAudioSource();
        if (source == null)
        {
            return;
        }

        // Prefab may already have a root AudioSource — silence it so only the child loop plays.
        AudioSource rootSource = GetComponent<AudioSource>();
        if (rootSource != null && rootSource != source)
        {
            rootSource.playOnAwake = false;
            rootSource.Stop();
            rootSource.enabled = false;
        }

        ApplySourceSettings(source, clip, volume);
        if (!source.isPlaying)
        {
            source.Play();
            if (clip.length > 0.05f)
            {
                source.time = Random.Range(0f, clip.length * 0.85f);
            }
        }
    }

    /// <summary>
    /// Call this before Destroy(arrow). Detaches the playing AudioSource so the crackle continues
    /// seamlessly, then hands it to SiegeSoundEffects for a fade-out.
    /// </summary>
    public void DetachAndFadeBeforeDestroy()
    {
        if (releasedForLinger)
        {
            return;
        }

        releasedForLinger = true;

        if (!Application.isPlaying || source == null)
        {
            return;
        }

        AudioSource detached = source;
        source = null;

        if (detached == null)
        {
            return;
        }

        Transform host = detached.transform;
        host.SetParent(null, worldPositionStays: true);

        SiegeSoundEffects soundEffects = SiegeSoundEffects.Instance;
        if (soundEffects != null && soundEffects.isActiveAndEnabled)
        {
            soundEffects.FadeOutDetachedFlamingArrow(detached);
            return;
        }

        // No manager — hard-destroy after a short delay; still avoid OnDestroy spawning.
        Object.Destroy(host.gameObject, 1.25f);
    }

    private void OnDisable()
    {
        // Never spawn linger objects here (scene close / Destroy cleanup warning).
        if (releasedForLinger || source == null)
        {
            return;
        }

        if (source.isPlaying)
        {
            source.Stop();
        }
    }

    private AudioSource EnsureChildAudioSource()
    {
        Transform host = transform.Find(AudioHostName);
        if (host == null)
        {
            GameObject hostObject = new GameObject(AudioHostName);
            hostObject.transform.SetParent(transform, worldPositionStays: false);
            hostObject.transform.localPosition = Vector3.zero;
            hostObject.transform.localRotation = Quaternion.identity;
            return hostObject.AddComponent<AudioSource>();
        }

        AudioSource existing = host.GetComponent<AudioSource>();
        return existing != null ? existing : host.gameObject.AddComponent<AudioSource>();
    }

    private void ApplySourceSettings(AudioSource target, AudioClip clip, float playVolume)
    {
        target.clip = clip;
        target.loop = true;
        target.playOnAwake = false;
        target.mute = false;
        target.spatialBlend = spatialBlend;
        target.minDistance = minDistance;
        target.maxDistance = maxDistance;
        target.rolloffMode = AudioRolloffMode.Linear;
        target.dopplerLevel = dopplerLevel;
        target.spread = 0f;
        target.volume = playVolume;
        target.priority = 160;
    }

    private AudioClip ResolveClip()
    {
        SiegeSoundEffects soundEffects = SiegeSoundEffects.Instance;
        if (soundEffects != null)
        {
            AudioClip fromManager = soundEffects.FlamingArrowPassbyClip;
            ApplyManagerSettings(soundEffects.FlamingArrowPassbySettings);
            if (fromManager != null)
            {
                return fromManager;
            }
        }

        return Resources.Load<AudioClip>(ResourcesClipPath);
    }

    private void ApplyManagerSettings(SiegeSpatialSoundSettings settings)
    {
        if (settings == null)
        {
            return;
        }

        volume = Mathf.Clamp(settings.volume, 0f, 3f);
        minDistance = Mathf.Max(0.1f, settings.minDistance);
        maxDistance = Mathf.Max(minDistance + 0.1f, settings.maxDistance);
        spatialBlend = Mathf.Clamp01(settings.spatialBlend);
    }

    public static bool LooksLikeFlamingArrow(GameObject instance)
    {
        if (instance == null)
        {
            return false;
        }

        if (instance.GetComponent<FlamingArrowPassbyAudio>() != null)
        {
            return true;
        }

        string objectName = instance.name;
        if (!string.IsNullOrEmpty(objectName)
            && objectName.IndexOf("Flaming", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        Transform fireVfx = instance.transform.Find("VFX_Fire");
        return fireVfx != null;
    }

    public static void EnsureOn(GameObject instance)
    {
        if (!LooksLikeFlamingArrow(instance))
        {
            return;
        }

        FlamingArrowPassbyAudio passby = instance.GetComponent<FlamingArrowPassbyAudio>();
        if (passby == null)
        {
            passby = instance.AddComponent<FlamingArrowPassbyAudio>();
        }

        passby.EnsurePlaying();
    }

    public static void DetachAndFadeAll(GameObject instance)
    {
        if (instance == null)
        {
            return;
        }

        FlamingArrowPassbyAudio passby = instance.GetComponent<FlamingArrowPassbyAudio>();
        if (passby != null)
        {
            passby.DetachAndFadeBeforeDestroy();
        }
    }
}
