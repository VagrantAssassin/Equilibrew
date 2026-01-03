using UnityEngine;

/// <summary>
/// Simple test helper: press keys in Play Mode to trigger AudioManager playback.
/// Attach to any GameObject (AudioManager recommended).
/// </summary>
public class TestAudioTrigger : MonoBehaviour
{
    void Update()
    {
        if (AudioManager.Instance == null) return;

        // BGM
        if (Input.GetKeyDown(KeyCode.F1))
        {
            Debug.Log("[TestAudioTrigger] PlayBGM_MainMenu");
            AudioManager.Instance.PlayBGM_MainMenu();
        }
        if (Input.GetKeyDown(KeyCode.F2))
        {
            Debug.Log("[TestAudioTrigger] PlayBGM_Cafe");
            AudioManager.Instance.PlayBGM_Cafe();
        }

        // SFX
        if (Input.GetKeyDown(KeyCode.F3))
        {
            Debug.Log("[TestAudioTrigger] PlaySFX_IngredientIn");
            AudioManager.Instance.PlaySFX_IngredientIn();
        }
        if (Input.GetKeyDown(KeyCode.F4))
        {
            Debug.Log("[TestAudioTrigger] PlaySFX_NPCSpawn");
            AudioManager.Instance.PlaySFX_NPCSpawn();
        }
        if (Input.GetKeyDown(KeyCode.F5))
        {
            Debug.Log("[TestAudioTrigger] PlaySFX_NPCDespawn");
            AudioManager.Instance.PlaySFX_NPCDespawn();
        }
        if (Input.GetKeyDown(KeyCode.F6))
        {
            Debug.Log("[TestAudioTrigger] PlaySFX_Paper");
            AudioManager.Instance.PlaySFX_Paper();
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            Debug.Log("[TestAudioTrigger] PlaySFX_Button");
            AudioManager.Instance.PlaySFX_Button();
        }

        // Stop BGM
        if (Input.GetKeyDown(KeyCode.Slash))
        {
            Debug.Log("[TestAudioTrigger] StopBGM");
            AudioManager.Instance.StopBGM();
        }
    }
}