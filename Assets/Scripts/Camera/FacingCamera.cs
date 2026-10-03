using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FacingCamera : MonoBehaviour
{
    Transform[] Childs;

    // Start is called before the first frame update
    void Start()
    {
        Childs = new Transform[gameObject.transform.childCount];
        for(int i =0;i< gameObject.transform.childCount; i++)
        {
            Childs[i] = gameObject.transform.GetChild(i);
        }
    }

    // Update is called once per frame
    void Update()
    {
        for (int i = 0; i < gameObject.transform.childCount; i++)
        {
            Childs[i].rotation=Camera.main.transform.rotation;
        }
    }
}
