using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Votanic.vXR.vGear;

public class CommandHandlerSample : MonoBehaviour
{
    int shootCount = 0;

    void Update()
    {
        if (vGear.Cmd.Received("Shoot") || vGear.Input.KeyboardDown(KeyCode.H))
        {
            GameObject bullet = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            bullet.transform.localScale = new Vector3(.05f, .05f, .05f);
            bullet.transform.LookAt(vGear.controller.transform);
            bullet.AddComponent<Rigidbody>().AddForce(vGear.head.transform.forward * 1000f);
            Destroy(bullet, 5f);

            if (++shootCount > 5)
            {
                vGear.Cmd.Send("ShootOverloaded");
            }
        }

        if (vGear.Cmd.Received("Move"))
        {
            Debug.Log("Move value: " + vGear.Cmd.Value("Move"));
        }
        
    }
}
