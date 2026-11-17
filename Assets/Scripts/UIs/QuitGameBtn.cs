using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class QuitGameBtn : MonoBehaviour
{
    private Button QuitGameButton;
    // Start is called before the first frame update
    void Start()
    {
        QuitGameButton = gameObject.GetComponent<Button>();
        QuitGameButton.onClick.AddListener(()=> Application.Quit());  
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
