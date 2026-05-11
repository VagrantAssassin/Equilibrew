using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// MainMenuController (simplified)
/// - Play BGM Main Menu on Start (AudioManager will also switch BGM automatically when scenes change)
/// - Buttons should use ButtonSfx component for SFX (so we removed startButton/exitButton fields)
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Tooltip("Nama scene game (Cafe). Pastikan scene ada di Build Settings.")]
    public string gameSceneName = "Cafe";

    private void Start()
    {
        // Play Menu BGM when main menu loads (AudioManager will also handle sceneLoaded).
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayBGM_MainMenu();
        }
    }

    // Public button handlers ------------------------------------------------
    public void PlayGame()
    {
        // Reset persistent GameManager first (if already exists), then load game scene.
        // This prevents day counter being reset after Day 1 already started in Cafe.
        if (GameManager.Instance != null)
        {
            GameManager.Instance.InitGame();
            GameManager.Instance.FireOnGameRestart();
        }

        // Start scene load; AudioManager will switch BGM on sceneLoaded.
        StartCoroutine(LoadGameCoroutine());
    }

    public void CloseGame()
    {
#if UNITY_EDITOR
        // Stop play mode in Editor
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // Coroutine: load scene only (GameManager reset is handled before loading when needed).
    private IEnumerator LoadGameCoroutine()
    {
        // Start loading the scene asynchronously.
        var ao = SceneManager.LoadSceneAsync(gameSceneName);
        ao.allowSceneActivation = true;

        // Wait until scene load finished.
        while (!ao.isDone)
            yield return null;
    }
}
