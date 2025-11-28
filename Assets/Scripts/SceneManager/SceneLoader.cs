using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class SceneLoader : MonoBehaviour
{
    public TextMeshProUGUI interactPrompt; // drag dari inspector
    private SceneTrigger currentTrigger = null;
    public SceneFadeController fadeController; // drag FadePanel prefab

    private void Awake()
    {
        if (interactPrompt != null)
            interactPrompt.enabled = false; // hide awal
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        SceneTrigger trigger = other.GetComponent<SceneTrigger>();
        if (trigger != null)
        {
            currentTrigger = trigger;

            if (interactPrompt != null)
            {
                interactPrompt.text = "Press E to Enter " + trigger.sceneName;
                interactPrompt.enabled = true;
            }
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        SceneTrigger trigger = other.GetComponent<SceneTrigger>();
        if (trigger != null && trigger == currentTrigger)
        {
            currentTrigger = null;

            if (interactPrompt != null)
                interactPrompt.enabled = false;
        }
    }

    private void Update()
    {
        if (currentTrigger != null && Input.GetKeyDown(KeyCode.E))
        {
            if (interactPrompt != null)
                interactPrompt.enabled = false;

            if (fadeController != null)
            {
                fadeController.FadeToScene(currentTrigger.sceneName);
            }
            else
            {
                SceneManager.LoadScene(currentTrigger.sceneName);
            }
        }
    }
}