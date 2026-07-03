using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Votanic.vXR.vGear;

public class CommandControllerSample : MonoBehaviour
{ 
    void Update()
    {        
        if (vGear.Cmd.Received("OnWandSelectOnBarrel"))
        {
            GameObject.Find("Barrel").GetComponent<Renderer>().material.color 
                = new Color(Random.value, Random.value, Random.value);
        }    
        if (vGear.Cmd.Received("OnWandDeselectOnBarrel"))
        {
            GameObject.Find("Barrel").GetComponent<Renderer>().material.color = Color.white;
        }
    }
}
