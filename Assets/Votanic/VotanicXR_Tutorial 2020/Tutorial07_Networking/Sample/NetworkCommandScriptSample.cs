using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Votanic.vXR.vGear;
using Votanic.vXR.vGear.Networking;

public class NetworkCommandScriptSample : MonoBehaviour
{
    vGear_Networking NetworkingManager;
    GameObject RotationCube;
    bool isCubeRotating = false;
    void Start()
    {
        NetworkingManager = GameObject.Find("/XRNetworkManager").GetComponent<vGear_Networking>();
        NetworkingManager.ReceivedMessage = PrintReceivedCommand;

        RotationCube = GameObject.Find("/Rotation Cube");
    }
    void Update()
    {
        //  Send a command via network
        if (vGear.Cmd.Received("Grab"))
        {
            NetworkingManager.Send("Greeting");
        }

        //  Send "DoRotation" after receive "button"
        if (vGear.Cmd.Received("Button"))
        {
            NetworkingManager.Send("DoRotation");
        }
        if (isCubeRotating)
        {
            RotationCube.transform.Rotate(1f, 0f, 0f);
        }
    }

    void PrintReceivedCommand(string message)
    {
        Debug.Log("Command received: " + message);
        if (message.Contains("DoRotation"))
        {
            isCubeRotating = !isCubeRotating;
        }
    }
}
