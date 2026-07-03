using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Votanic.vXR.vGear;

public class Tutorial02ControllerSample : MonoBehaviour
{
    void Start()
    {
        vGear.Cmd.Send("Custom");
    }

    private void CheckCustomCommand()
    {
        //  CheckCustomCommand function in Update
        if (vGear.Cmd.Received("Custom"))
        {
            Debug.Log("Custom command received!");
        }
    }

    private void CheckAllCommand()
    {
        //  CheckAllCommand function in Update
        foreach (string command in vGear.Cmd.AllReceived())
        {
            Debug.Log("The value of " + command + " command is " + vGear.Cmd.Value(command));
            switch (command)
            {
                case "FunctionA":
                    FunctionA();
                    break;
            }
        }
    }

    private void CheckVirtualControllerInput()
    {
        //  CheckVirtualControllerInput function in Update
        if (vGear.Ctrl.ButtonDown(0))
        {
            Debug.Log("Button 0 is down.");
        }
        if (vGear.Ctrl.ButtonHold(0))
        {
            Debug.Log("Button 0 is held for some time.");
        }
        if (vGear.Ctrl.ButtonPress(0))
        {
            Debug.Log("Button 0 is pressing.");
        }

        if (vGear.Ctrl.AxisDown(0))
        {
            Debug.Log("Axis 0 is down.");
        }
        if (vGear.Ctrl.AxisHold(0))
        {
            Debug.Log("Axis 0 is held for some time.");
        }
        if (vGear.Ctrl.AxisPress(0))
        {
            Debug.Log("Axis 0 is pressing with value " +
                vGear.Ctrl.AxisValue(0).ToString("F2") + ".");
        }
    }

    private void FunctionA()
    {
        // Not Implemented
    }

    void Update()
    {
        CheckCustomCommand();
        CheckAllCommand();
        CheckVirtualControllerInput();
    }

}
