using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Votanic.vXR.vGear;

public class TeleportationSample : MonoBehaviour
{
    void Update()
    {
        //  Invoke Transform API to Teleport to a Given Position
        if (vGear.Cmd.Received("Trigger"))
        {
            vGear.user.Transform(GameObject.Find("Location1").transform);
        }
    }
}
