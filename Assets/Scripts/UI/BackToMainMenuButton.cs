using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Simple handler for in-game "Back to Main Menu" button.
/// Loads MainMenu scene. If you want to reset GameManager state when returning,
/// you can toggle reloadSceneOnRestart in GameManager or rely on MainMenu Play to reset when re-entering Cafe.
/// </summary>
public class BackToMainMenuButton : MonoBehaviour
{
    [Tooltip("Nama main menu scene")]
    public string mainMenuSceneName = "MainMenu";

    public void BackToMainMenu()
    {
        // Optionally you can call GameManager.Instance.InitGame() here or perform cleanup
        SceneManager.LoadScene(mainMenuSceneName);
    }
}