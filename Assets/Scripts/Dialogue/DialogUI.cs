using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI controller for a dialog bubble / panel.
/// - SetLine(text) shows the line (optionally typewriter).
/// - ShowChoices(list, callback) populates choice buttons and forwards choice index when clicked.
/// - Expectation: prefab root has a TextMeshProUGUI for line and a container for buttons.
/// </summary>
public class DialogUI : MonoBehaviour
{
    [Header("Parts")]
    public TextMeshProUGUI lineText;
    public Transform choicesContainer;
    public Button choiceButtonPrefab;

    [Header("Typewriter")]
    public bool useTypewriter = true;
    public float typeSpeed = 0.01f; // seconds per character

    private List<Button> activeButtons = new List<Button>();
    private Coroutine typeCoroutine;

    public void SetLine(string text)
    {
        StopTypewriter();
        if (!useTypewriter || string.IsNullOrEmpty(text))
        {
            if (lineText != null) lineText.text = text ?? "";
            return;
        }

        if (lineText != null)
        {
            typeCoroutine = StartCoroutine(TypeRoutine(text));
        }
    }

    private System.Collections.IEnumerator TypeRoutine(string text)
    {
        lineText.text = "";
        for (int i = 0; i < text.Length; i++)
        {
            lineText.text += text[i];
            yield return new WaitForSecondsRealtime(typeSpeed);
        }
        typeCoroutine = null;
    }

    public void SkipTypewriter()
    {
        if (typeCoroutine != null)
        {
            StopCoroutine(typeCoroutine);
            typeCoroutine = null;
        }
    }

    private void StopTypewriter()
    {
        if (typeCoroutine != null)
        {
            StopCoroutine(typeCoroutine);
            typeCoroutine = null;
        }
    }

    public void ShowChoices(IList<string> choices, Action<int> onChoiceSelected)
    {
        ClearChoices();
        if (choicesContainer == null || choiceButtonPrefab == null) return;

        for (int i = 0; i < choices.Count; i++)
        {
            var btnObj = Instantiate(choiceButtonPrefab, choicesContainer);
            btnObj.gameObject.SetActive(true);
            var tmp = btnObj.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null) tmp.text = choices[i];
            int captured = i;
            btnObj.onClick.RemoveAllListeners();
            btnObj.onClick.AddListener(() => onChoiceSelected?.Invoke(captured));
            activeButtons.Add(btnObj);
        }
    }

    public void ClearChoices()
    {
        foreach (var b in activeButtons)
        {
            if (b != null) Destroy(b.gameObject);
        }
        activeButtons.Clear();
    }

    private void OnDestroy()
    {
        StopTypewriter();
        ClearChoices();
    }
}