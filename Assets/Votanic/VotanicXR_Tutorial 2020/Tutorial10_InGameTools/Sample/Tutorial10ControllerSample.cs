using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Votanic.vXR.vGear;

public class Tutorial10ControllerSample : MonoBehaviour
{

    void Update()
    {
        //	Set the tool into Brush when Grab is received
        if (vGear.Cmd.Received("Grab"))
        {
            vGear.controller.SetTool("Brush");
        }
    }
}
