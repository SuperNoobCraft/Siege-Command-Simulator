using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Votanic.vXR;
using Votanic.vXR.vCast;
using Votanic.vXR.vGear;

public class vCastTemp : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        vCast.Cmd.AddAction(OnReceivedCommand);
        vCast.Cmd.Send("Custom");
    }

    // Update is called once per frame
    void Update()
    {
        //Get Command
        if (vCast.Cmd.Received("Custom"))
        {
            CustomMethod();
        }
        //Get Controller Input
        if (vCast.Ctrl.ButtonUp(0))
        {
            ControllerMethod();
        }

        //Get Device Input
        if (vCast.Input.KeyboardUp(KeyCode.Space))
        {

        }

        //vCast.Frame.Transform(targetPos, targetRot);
    }
    void OnDestroy()
    {
        vCast.Cmd.RemoveAction(OnReceivedCommand);
    }

    void OnReceivedCommand(string command, float value = 1, int target = -1, int interactor = -1, InteractorType type = InteractorType.None)
    {
        Debug.Log(command);
    }

    void CustomMethod()
    {

    }

    void ControllerMethod()
    {

    }
}
