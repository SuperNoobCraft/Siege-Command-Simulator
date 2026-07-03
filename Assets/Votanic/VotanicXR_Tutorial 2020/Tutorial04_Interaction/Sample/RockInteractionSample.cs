using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Votanic.vXR.vGear;

public class RockInteractionSample: MonoBehaviour
{
    public void OnWandSelectOnRock()
    {
        GetComponent<Renderer>().material.color = new Color(Random.value, Random.value, Random.value);
    }

    public void OnWandDeselectOnRock()
    {
        GetComponent<Renderer>().material.color = Color.white;
    }

}
