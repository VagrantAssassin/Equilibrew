using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Simple Main Menu controller:
/// - PlayGame(): load scene dengan nama yang diberikan (default "Cafe") dan setelah scene siap, reset GameManager (InitGame + request restart event)
/// - CloseGame(): quit application (works in build; stops play mode in Editor)
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Tooltip("Nama scene game (Cafe). Pastikan scene ada di Build Settings.")]
    public string gameSceneName = "Cafe";

    // Public button handlers ------------------------------------------------
    public void PlayGame()
    {
        StartCoroutine(LoadGameAndResetCoroutine());
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

    // Coroutine: load scene, wait until GameManager is present, reset it and fire restart event
    private IEnumerator LoadGameAndResetCoroutine()
    {
        // Start loading the scene asynchronously
        var ao = SceneManager.LoadSceneAsync(gameSceneName);
        ao.allowSceneActivation = true;

        // wait until scene load finished
        while (!ao.isDone)
            yield return null;

        // Wait one frame for Awake/Start to run in the newly loaded scene
        yield return null;

        // If GameManager exists, call InitGame and request it to fire restart event
        if (GameManager.Instance != null)
        {
            GameManager.Instance.InitGame();
            GameManager.Instance.FireOnGameRestart(); // call helper method
        }
        else
        {
            // If GameManager is instantiated by scene and not yet available, wait a bit for it
            float timeout = 2f;
            float t = 0f;
            while (GameManager.Instance == null && t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (GameManager.Instance != null)
            {
                GameManager.Instance.InitGame();
                GameManager.Instance.FireOnGameRestart();
            }
            else
            {
                Debug.LogWarning("[MainMenuController] GameManager not found after loading scene '" + gameSceneName + "'. Make sure GameManager exists in the scene or is a persistent prefab.");
            }
        }
    }
}