using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Votanic.vXR.vGear;

public class VirtualControllerScriptSample : MonoBehaviour
{
    float time = 0f;
    void Update()
    {
        time += Time.deltaTime;
        if (time >= 5f)
        {
            vGear.Ctrl.Vibrate(1f);
            time = 0f;
        }
    }
}
