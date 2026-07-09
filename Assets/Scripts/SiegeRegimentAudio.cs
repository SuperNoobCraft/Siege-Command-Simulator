using UnityEngine;

/// <summary>
/// Loops marching audio on a regiment while it is moving and not in melee combat.
/// Added automatically by SiegeSoundEffects when the match starts.
/// </summary>
[DefaultExecutionOrder(130)]
public class SiegeRegimentAudio : MonoBehaviour
{
    private RtsUnitMotor motor;
    private TroopCombat combat;
    private AudioSource marchingSource;
    private bool isMarchingPlaying;

    private void Awake()
    {
        motor = GetComponent<RtsUnitMotor>();
        combat = GetComponent<TroopCombat>();
    }

    private void OnDisable()
    {
        StopMarching();
    }

    private void Update()
    {
        bool shouldMarch = ShouldPlayMarching();
        if (shouldMarch)
        {
            if (!isMarchingPlaying)
            {
                StartMarching();
            }
        }
        else if (isMarchingPlaying)
        {
            StopMarching();
        }
    }

    private bool ShouldPlayMarching()
    {
        if (motor == null || combat == null || !motor.HasDestination)
        {
            return false;
        }

        TroopCombat.State state = combat.CurrentState;
        return state != TroopCombat.State.Fight
            && state != TroopCombat.State.Dead
            && state != TroopCombat.State.Regroup;
    }

    private void StartMarching()
    {
        SiegeSoundEffects soundEffects = SiegeSoundEffects.Instance;
        if (soundEffects == null)
        {
            return;
        }

        if (marchingSource == null)
        {
            marchingSource = soundEffects.CreateMarchingSource(transform);
            if (marchingSource == null)
            {
                return;
            }
        }

        if (!marchingSource.isPlaying)
        {
            marchingSource.Play();
        }

        isMarchingPlaying = true;
    }

    private void StopMarching()
    {
        if (marchingSource != null && marchingSource.isPlaying)
        {
            marchingSource.Stop();
        }

        isMarchingPlaying = false;
    }
}
