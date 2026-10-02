using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ChangeCameraState : MonoBehaviour
{
    private Camera SceneCamera;
    private CameraMove cameraMove;
    private Toggle ChangeCameraStateToggle;
    // Start is called before the first frame update
    void Start()
    {
        SceneCamera = Camera.main;
        cameraMove = SceneCamera.GetComponent<CameraMove>();
        ChangeCameraStateToggle = gameObject.GetComponent<Toggle>();
        ChangeCameraStateToggle.onValueChanged.AddListener(ToggleValueChanged); 
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void ToggleValueChanged(bool isOn)
    {
        cameraMove.ChangeCameraState();
    }
}
