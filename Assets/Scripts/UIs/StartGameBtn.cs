using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class StartGameBtn : MonoBehaviour
{
    [SerializeField]
    private Button StartGameButton;
    // Start is called before the first frame update
    void Start()
    {
        StartGameButton = gameObject.GetComponent<Button>();
        StartGameButton.onClick.RemoveAllListeners();
        StartGameButton.onClick.AddListener(GameStart);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void GameStart()
    {
        SceneManager.LoadScene("Taki's Test");
    }
}
