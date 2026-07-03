using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Votanic.vXR.vGear;

public class Tutorial03ControllerSample : MonoBehaviour
{


    void Start()
    {
        //  Movement Modes
        //vGear.SetSoul(true);
        //vGear.SetTraveler(true);

        //  Advance Teleport - collision mode 
        vGear.controller.teleport.SetHeightOffset(0.2f);
    }

    void Update()
    {
        //  Getting Movement Command Input Value
        Debug.Log("Move : " + vGear.Cmd.Value("Move"));

        //  Invoke Teleportation Programmatically
        if (vGear.Cmd.Received("Grab"))
        {
            vGear.Cmd.Send("Teleport");
        }
    }
}
