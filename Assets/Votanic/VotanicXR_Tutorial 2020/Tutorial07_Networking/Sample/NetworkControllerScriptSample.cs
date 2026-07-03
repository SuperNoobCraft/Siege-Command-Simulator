using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Votanic.vXR.vGear;
using Votanic.vXR.vGear.Networking;

public class NetworkControllerScriptSample : MonoBehaviour
{
    vGear_Networking NetworkingManager;
    GameObject cube;
    void Start()
    {
        NetworkingManager = GameObject.Find("/XRNetworkManager").GetComponent<vGear_Networking>();
    }
    void Update()
    {
        if (vGear.Cmd.Received("Trigger") && cube == null)
        {
            cube = Instantiate(NetworkingManager.spawnPrefabs[0]);
            NetworkServer.Spawn(cube);
        }
    }
}
