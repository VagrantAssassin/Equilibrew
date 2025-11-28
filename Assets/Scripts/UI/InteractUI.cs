using TMPro;
using UnityEngine;

public class InteractUI : MonoBehaviour
{
    public TextMeshProUGUI promptText;

    private void Awake()
    {
        if (promptText == null)
            promptText = GetComponent<TextMeshProUGUI>();

        promptText.enabled = false; // hide awal
    }

    public void Show(string message)
    {
        if (promptText != null)
        {
            promptText.text = message;
            promptText.enabled = true;
        }
    }

    public void Hide()
    {
        if (promptText != null)
            promptText.enabled = false;
    }
}