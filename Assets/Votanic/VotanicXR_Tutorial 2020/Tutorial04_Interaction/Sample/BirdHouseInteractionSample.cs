using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BirdHouseInteractionSample : MonoBehaviour
{
    public void DisableBirdHouse()
    {
        GetComponent<Renderer>().enabled = false;
    }
}
