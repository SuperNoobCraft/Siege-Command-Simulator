using System.Collections.Generic;
using UnityEngine;

public enum SiegeAudioOverlapMode
{
    Override,
    IgnoreIfBusy,
    Simultaneous,
    Queue
}

/// <summary>
/// Plays siege voice-over and event clips. Assign clips from Assets/Audio in the Inspector.
/// </summary>
[DefaultExecutionOrder(120)]
public class SiegeAudioManager : MonoBehaviour
{
    public static SiegeAudioManager Instance { get; private set; }

    [Header("Game Ending (Always Override Everything)")]
    [SerializeField] private AudioClip cannonsFiredClip;
    [SerializeField] private AudioClip cannonsDestroyedClip;
    [SerializeField] private AudioClip commanderFallenClip;

    [Header("Wave Announcements")]
    [SerializeField] private AudioClip wave1Clip;
    [SerializeField] private AudioClip wave2Clip;
    [SerializeField] private AudioClip wave3Clip;
    [SerializeField] private SiegeAudioOverlapMode waveOverlapMode = SiegeAudioOverlapMode.Queue;

    [Header("Time Announcements")]
    [SerializeField] private AudioClip fifteenSecondsUntilCannonsClip;
    [SerializeField] private SiegeAudioOverlapMode timeAnnouncementOverlapMode = SiegeAudioOverlapMode.Override;
    [SerializeField, Min(0f)] private float cannonCountdownTriggerSeconds = 15f;

    [Header("Commander Hits (Alternate A / B)")]
    [SerializeField] private AudioClip commanderHitAClip;
    [SerializeField] private AudioClip commanderHitBClip;
    [SerializeField] private SiegeAudioOverlapMode commanderHitOverlapMode = SiegeAudioOverlapMode.Simultaneous;

    [Header("Friendly Regiment Events")]
    [SerializeField] private AudioClip regimentDefeatedClip;
    [SerializeField] private AudioClip regimentDefeatedSubsequentClip;
    [SerializeField] private AudioClip regimentWipedClip;
    [SerializeField] private AudioClip allRegimentsWipedClip;

    [Header("Regroup Success")]
    [SerializeField] private AudioClip regimentRegroupSuccessClip;
    [SerializeField] private SiegeAudioOverlapMode regroupSuccessOverlapMode = SiegeAudioOverlapMode.Queue;

    [Header("Enemy Regiment Events")]
    [SerializeField] private AudioClip enemyRetreatingFirstClip;
    [SerializeField] private AudioClip enemyRetreatingSubsequentClip;

    [Header("Event Overlap (Random / Combat Feedback)")]
    [SerializeField] private SiegeAudioOverlapMode eventOverlapMode = SiegeAudioOverlapMode.IgnoreIfBusy;

    [Header("Playback")]
    [SerializeField, Range(0f, 1f)] private float voiceVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float eventVolume = 1f;

    private AudioSource voiceSource;
    private AudioSource eventSource;
    private readonly Queue<AudioClip> voiceQueue = new Queue<AudioClip>();
    private bool useCommanderHitA = true;
    private bool friendlyRegimentDefeatFirstPlayed;
    private bool enemyRetreatFirstPlayed;
    private bool cannonCountdownAnnounced;
    private int resetPlaySessionId = -1;
    private bool isGameManagerBound;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple SiegeAudioManager instances found. Using the most recent one.", this);
        }

        Instance = this;
        EnsureAudioSources();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnEnable()
    {
        EnemyWaveController.WaveDeployed += HandleWaveDeployed;
        TroopCombat.RegimentEnteredRetreat += HandleRegimentEnteredRetreat;
        TroopCombat.RegimentRegroupCompleted += HandleRegimentRegroupCompleted;
        TroopCombat.RegimentPermanentlyDestroyed += HandleRegimentPermanentlyDestroyed;
        SiegeCommanderArrowHealth.CommanderHitRegistered += HandleCommanderHit;

        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager != null)
        {
            BindGameManager(manager);
            isGameManagerBound = true;
        }
    }

    private void OnDisable()
    {
        EnemyWaveController.WaveDeployed -= HandleWaveDeployed;
        TroopCombat.RegimentEnteredRetreat -= HandleRegimentEnteredRetreat;
        TroopCombat.RegimentRegroupCompleted -= HandleRegimentRegroupCompleted;
        TroopCombat.RegimentPermanentlyDestroyed -= HandleRegimentPermanentlyDestroyed;
        SiegeCommanderArrowHealth.CommanderHitRegistered -= HandleCommanderHit;

        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager != null)
        {
            UnbindGameManager(manager);
            isGameManagerBound = false;
        }
    }

    private void Update()
    {
        TryBindGameManager();
        TryResetForPlaySession();
        ProcessVoiceQueue();
    }

    private void TryBindGameManager()
    {
        if (isGameManagerBound)
        {
            return;
        }

        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager == null)
        {
            return;
        }

        BindGameManager(manager);
        isGameManagerBound = true;
    }

    private void BindGameManager(SiegeGameManager manager)
    {
        manager.CannonOverrun += HandleCannonsDestroyed;
        manager.CannonFireCountdownUpdated += HandleCannonCountdownUpdated;
        manager.MatchStateChanged += HandleMatchStateChanged;
    }

    private void UnbindGameManager(SiegeGameManager manager)
    {
        manager.CannonOverrun -= HandleCannonsDestroyed;
        manager.CannonFireCountdownUpdated -= HandleCannonCountdownUpdated;
        manager.MatchStateChanged -= HandleMatchStateChanged;
    }

    private void TryResetForPlaySession()
    {
        int sessionId = SiegeGameManager.PlaySessionId;
        if (resetPlaySessionId == sessionId)
        {
            return;
        }

        resetPlaySessionId = sessionId;
        ResetMatchAudioState();
    }

    private void ResetMatchAudioState()
    {
        useCommanderHitA = true;
        friendlyRegimentDefeatFirstPlayed = false;
        enemyRetreatFirstPlayed = false;
        cannonCountdownAnnounced = false;
        voiceQueue.Clear();
        StopAllPlayback();
    }

    private void HandleMatchStateChanged(SiegeGameManager.MatchState state)
    {
        if (state == SiegeGameManager.MatchState.Playing
            || state == SiegeGameManager.MatchState.SelectingDifficulty)
        {
            ResetMatchAudioState();
        }
    }

    private void HandleWaveDeployed(int waveNumber)
    {
        if (!ShouldPlaySiegeVoicelines())
        {
            return;
        }

        PlayVoiceClip(ResolveWaveClip(waveNumber), waveOverlapMode);
    }

    private AudioClip ResolveWaveClip(int waveNumber)
    {
        int finalWave = EnemyWaveController.Instance != null
            ? EnemyWaveController.Instance.FinalWaveNumber
            : SiegeMatchSettings.MaxWaves;

        if (waveNumber == finalWave)
        {
            return wave3Clip;
        }

        return waveNumber switch
        {
            1 => wave1Clip,
            2 => wave2Clip,
            3 => wave3Clip,
            _ => null
        };
    }

    private void HandleCannonCountdownUpdated(float secondsRemaining)
    {
        if (!ShouldPlaySiegeVoicelines())
        {
            return;
        }

        if (cannonCountdownAnnounced || secondsRemaining > cannonCountdownTriggerSeconds)
        {
            return;
        }

        if (secondsRemaining <= 0f)
        {
            return;
        }

        cannonCountdownAnnounced = true;
        PlayVoiceClip(fifteenSecondsUntilCannonsClip, timeAnnouncementOverlapMode);
    }

    public void PlayCannonsFiredVoiceline()
    {
        PlayGameEndingClip(cannonsFiredClip);
    }

    private void HandleCannonsDestroyed()
    {
        PlayGameEndingClip(cannonsDestroyedClip);
    }

    private void HandleCommanderHit(int hitCount, Vector3 hitPoint)
    {
        if (!ShouldPlaySiegeVoicelines())
        {
            return;
        }

        SiegeGameManager manager = SiegeGameManager.Instance;
        if (manager != null && !manager.IsPlaying)
        {
            return;
        }

        SiegeCommanderArrowHealth commander = SiegeCommanderArrowHealth.Instance;
        if (commander != null && hitCount >= commander.MaxHits)
        {
            return;
        }

        AudioClip clip = useCommanderHitA ? commanderHitAClip : commanderHitBClip;
        useCommanderHitA = !useCommanderHitA;
        PlayEventClip(clip, commanderHitOverlapMode);
    }

    public void PlayCommanderFallen()
    {
        PlayGameEndingClip(commanderFallenClip);
    }

    private void HandleRegimentEnteredRetreat(TroopCombat regiment)
    {
        if (regiment == null || !ShouldPlaySiegeVoicelines())
        {
            return;
        }

        if (regiment.TroopFaction == TroopCombat.Faction.Enemy)
        {
            AudioClip enemyClip = enemyRetreatFirstPlayed ? enemyRetreatingSubsequentClip : enemyRetreatingFirstClip;
            enemyRetreatFirstPlayed = true;
            PlayEventClip(enemyClip, eventOverlapMode);
            return;
        }

        AudioClip friendlyClip = friendlyRegimentDefeatFirstPlayed
            ? regimentDefeatedSubsequentClip
            : regimentDefeatedClip;
        friendlyRegimentDefeatFirstPlayed = true;
        PlayEventClip(friendlyClip, eventOverlapMode);
    }

    private void HandleRegimentRegroupCompleted(TroopCombat regiment)
    {
        if (regiment == null || regiment.TroopFaction != TroopCombat.Faction.Friendly || !ShouldPlaySiegeVoicelines())
        {
            return;
        }

        PlayVoiceClip(regimentRegroupSuccessClip, regroupSuccessOverlapMode);
    }

    private void HandleRegimentPermanentlyDestroyed(TroopCombat regiment)
    {
        if (regiment == null || regiment.TroopFaction != TroopCombat.Faction.Friendly || !ShouldPlaySiegeVoicelines())
        {
            return;
        }

        PlayEventClip(regimentWipedClip, eventOverlapMode);

        if (!AnyFriendlyRegimentStillAlive())
        {
            PlayEventClip(allRegimentsWipedClip, eventOverlapMode);
        }
    }

    private static bool AnyFriendlyRegimentStillAlive()
    {
        TroopCombat[] regiments = FindObjectsOfType<TroopCombat>(includeInactive: true);
        for (int i = 0; i < regiments.Length; i++)
        {
            TroopCombat regiment = regiments[i];
            if (regiment == null
                || regiment.TroopFaction != TroopCombat.Faction.Friendly
                || regiment.CurrentState == TroopCombat.State.Dead)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool ShouldPlaySiegeVoicelines()
    {
        return !SiegeMatchSettings.IsSiegePvpMode;
    }

    private void PlayGameEndingClip(AudioClip clip)
    {
        if (clip == null || !ShouldPlaySiegeVoicelines())
        {
            return;
        }

        StopAllPlayback();
        voiceSource.clip = clip;
        voiceSource.volume = voiceVolume;
        voiceSource.Play();
    }

    private void PlayVoiceClip(AudioClip clip, SiegeAudioOverlapMode overlapMode)
    {
        if (clip == null || !ShouldPlaySiegeVoicelines())
        {
            return;
        }

        PlayOnSource(voiceSource, voiceQueue, clip, overlapMode, voiceVolume);
    }

    private void PlayEventClip(AudioClip clip, SiegeAudioOverlapMode overlapMode)
    {
        if (clip == null || !ShouldPlaySiegeVoicelines())
        {
            return;
        }

        if (overlapMode == SiegeAudioOverlapMode.Simultaneous)
        {
            eventSource.PlayOneShot(clip, eventVolume);
            return;
        }

        if (overlapMode == SiegeAudioOverlapMode.IgnoreIfBusy && voiceSource.isPlaying)
        {
            return;
        }

        PlayOnSource(voiceSource, voiceQueue, clip, overlapMode, voiceVolume);
    }

    private static void PlayOnSource(
        AudioSource source,
        Queue<AudioClip> queue,
        AudioClip clip,
        SiegeAudioOverlapMode overlapMode,
        float volume)
    {
        switch (overlapMode)
        {
            case SiegeAudioOverlapMode.Override:
                queue.Clear();
                source.Stop();
                source.clip = clip;
                source.volume = volume;
                source.Play();
                break;

            case SiegeAudioOverlapMode.IgnoreIfBusy:
                if (source.isPlaying)
                {
                    return;
                }

                source.clip = clip;
                source.volume = volume;
                source.Play();
                break;

            case SiegeAudioOverlapMode.Queue:
                if (!source.isPlaying)
                {
                    source.clip = clip;
                    source.volume = volume;
                    source.Play();
                }
                else
                {
                    queue.Enqueue(clip);
                }

                break;

            case SiegeAudioOverlapMode.Simultaneous:
                source.PlayOneShot(clip, volume);
                break;
        }
    }

    private void ProcessVoiceQueue()
    {
        if (voiceSource.isPlaying || voiceQueue.Count == 0)
        {
            return;
        }

        AudioClip nextClip = voiceQueue.Dequeue();
        if (nextClip == null)
        {
            return;
        }

        voiceSource.clip = nextClip;
        voiceSource.volume = voiceVolume;
        voiceSource.Play();
    }

    private void StopAllPlayback()
    {
        voiceQueue.Clear();
        voiceSource.Stop();
        eventSource.Stop();
    }

    private void EnsureAudioSources()
    {
        if (voiceSource == null)
        {
            voiceSource = gameObject.AddComponent<AudioSource>();
            voiceSource.playOnAwake = false;
            voiceSource.spatialBlend = 0f;
        }

        if (eventSource == null)
        {
            eventSource = gameObject.AddComponent<AudioSource>();
            eventSource.playOnAwake = false;
            eventSource.spatialBlend = 0f;
        }
    }
}
