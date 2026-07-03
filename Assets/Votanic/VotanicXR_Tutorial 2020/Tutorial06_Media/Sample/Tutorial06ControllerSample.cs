using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Votanic.vMedia.MediaPlayer;

public class Tutorial06ControllerSample : MonoBehaviour
{
    public MediaPlayer360 mediaPlayer360;
    IEnumerator Start()
    {
        yield return new WaitForSeconds(1f);
        if (mediaPlayer360 != null)
        {
            mediaPlayer360.SetStereoType(StereoType.DownUp);
        }
    }
}
