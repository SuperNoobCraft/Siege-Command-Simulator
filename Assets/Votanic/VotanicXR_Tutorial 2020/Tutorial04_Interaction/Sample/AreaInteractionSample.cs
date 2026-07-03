using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AreaInteractionSample : MonoBehaviour
{
    public void AreaChangeColor()
    {
        GetComponent<Renderer>().material.color = new Color(Random.value, Random.value, Random.value, .5f);
    }
    public void AreaRestoreColor()
    {
        GetComponent<Renderer>().material.color = new Color( 1f, 1f, 1f, .16f);
    }

}
