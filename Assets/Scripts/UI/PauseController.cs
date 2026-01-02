using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// PauseController
/// - Toggle pause/unpause (Time.timeScale)
/// - Shows/hides pausePanel
/// - Resume dan BackToMainMenu handlers for pause menu buttons
/// - ESC toggles pause
/// - Pauses audio using AudioListener.pause (simple global pause)
/// </summary>
public class PauseController : MonoBehaviour
{
    [Tooltip("Panel UI yang berisi tombol Resume / Back to Menu. Diset inactive di awal.")]
    public GameObject pausePanel;

    [Tooltip("Nama scene Main Menu (digunakan saat Back to Main Menu).")]
    public string mainMenuSceneName = "MainMenu";

    // apakah game saat ini dalam keadaan pause
    public bool IsPaused { get; private set; } = false;

    private void Start()
    {
        if (pausePanel != null) pausePanel.SetActive(false);
        IsPaused = false;
        AudioListener.pause = false;
    }

    private void Update()
    {
        // Toggle paus dengan ESC (atau Back button di Android)
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            TogglePause();
        }
    }

    // Toggle pause state
    public void TogglePause()
    {
        if (IsPaused) Resume();
        else Pause();
    }

    // Set pause: timeScale = 0, show panel, pause audio
    public void Pause()
    {
        if (IsPaused) return;
        IsPaused = true;

        // stop gameplay time
        Time.timeScale = 0f;

        // show UI
        if (pausePanel != null) pausePanel.SetActive(true);

        // simple audio pause (pauses all audio)
        AudioListener.pause = true;

        // optionally lock cursor, disable gameplay input etc - implement as needed
        Debug.Log("[PauseController] Game paused.");
    }

    // Resume: restore timeScale = 1, hide panel, resume audio
    public void Resume()
    {
        if (!IsPaused) return;
        IsPaused = false;

        // resume time
        Time.timeScale = 1f;

        // hide UI
        if (pausePanel != null) pausePanel.SetActive(false);

        // resume audio
        AudioListener.pause = false;

        Debug.Log("[PauseController] Game resumed.");
    }

    // Back to main menu from pause menu
    public void BackToMainMenu()
    {
        // ensure we resume time & audio before leaving scene
        Time.timeScale = 1f;
        AudioListener.pause = false;

        // load main menu
        SceneManager.LoadScene(mainMenuSceneName);
    }
}