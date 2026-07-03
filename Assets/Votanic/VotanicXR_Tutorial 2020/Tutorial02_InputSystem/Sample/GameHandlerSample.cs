using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Votanic.vXR.vGear;

public class GameHandlerSample : MonoBehaviour
{
    
    void Update()
    {
        if (vGear.Cmd.Received("ShootOverloaded"))
        {
            Debug.Log("You press shoot button too much!");
        }

    }
}
