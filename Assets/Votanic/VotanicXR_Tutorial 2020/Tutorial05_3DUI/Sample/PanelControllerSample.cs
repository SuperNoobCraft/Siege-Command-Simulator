using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Votanic.vXR.vGear;

public class PanelControllerSample: MonoBehaviour
{

    vGear_Panel panel;
    void Start()
    {
        panel = GameObject.Find("DemoPanel/Panel.Grabbables/Panel").GetComponent<vGear_Panel>();
    }

    void Update()
    {
        if ((panel != null) && (!panel.isTransiting) && (vGear.Cmd.Received("Grab")))
        {
            if (panel.isOpened)
            {
                panel.Close();
            }
            else
            {
                panel.Open();
            }
        }
    }
}
