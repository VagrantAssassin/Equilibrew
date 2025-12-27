using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using Ink.Runtime;

/// <summary>
/// InkDialogController - improved:
/// - Jika bodyText tidak diassign, akan GetComponentInChildren dari dialogPanelRoot
/// - Jika choiceButtons tidak diassign, akan mengisi dari children of choiceContainer
/// - Memastikan listeners di-add/remove dengan UnityAction dan dibersihkan setelah pilihan
/// - Setelah pemilihan: tombol hilang / dialog ditutup
/// </summary>
public enum DialogueReaction
{
    Agree,
    Neutral,
    Disagree
}

public class InkDialogController : MonoBehaviour
{
    [Header("UI References (assign prefab root, controller bisa auto-find child TMP / buttons)")]
    [Tooltip("Panel dialog besar (kanan). Harus memiliki TextMeshProUGUI child yang menampilkan body dialog.")]
    public GameObject dialogPanelRoot;

    [Tooltip("Optional: assign direct reference ke bodyText. Jika kosong, akan dicari di dialogPanelRoot.")]
    public TextMeshProUGUI bodyText;

    [Tooltip("Container untuk choice buttons (tengah-bawah). Buttons harus berada sebagai children.")]
    public GameObject choiceContainer;

    [Tooltip("Optional: assign 3 buttons in inspector (0=Agree,1=Neutral,2=Disagree). Jika kosong, controller akan auto-find Buttons in choiceContainer.")]
    public List<Button> choiceButtons = new List<Button>(3);

    [Tooltip("Optional: assign TMP labels for each button (will override child TMP if present).")]
    public TextMeshProUGUI[] choiceLabels;

    [Tooltip("Optional Continue button if you want to step lines.")]
    public Button continueButton;

    private Story inkStory;
    private Action<DialogueReaction, List<string>> onComplete;
    private bool isPlaying = false;
    public bool IsPlaying => isPlaying;

    private void Awake()
    {
        // ensure dialog root inactive initially
        if (dialogPanelRoot != null) dialogPanelRoot.SetActive(false);
        if (choiceContainer != null) choiceContainer.SetActive(false);

        // auto-find bodyText from dialogPanelRoot if not assigned
        if (bodyText == null && dialogPanelRoot != null)
        {
            bodyText = dialogPanelRoot.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        // if choiceButtons not assigned in inspector, try to auto-find from choiceContainer
        if ((choiceButtons == null || choiceButtons.Count == 0) && choiceContainer != null)
        {
            choiceButtons = new List<Button>();
            var found = choiceContainer.GetComponentsInChildren<Button>(true);
            foreach (var b in found)
                choiceButtons.Add(b);
        }

        // make sure we have three slots (if fewer, we still work with available buttons)
        if (choiceButtons == null) choiceButtons = new List<Button>();

        // bind continueButton to a named method (safe)
        if (continueButton != null)
            continueButton.onClick.AddListener(OnContinuePressed);
    }

    /// <summary>
    /// Play a compiled Ink JSON TextAsset — use prefab's bodyText and buttons.
    /// </summary>
    public void PlayCurhat(TextAsset inkJson, Action<DialogueReaction, List<string>> onCompleteCallback)
    {
        if (inkJson == null)
        {
            Debug.LogWarning("[InkDialogController] PlayCurhat called with null inkJson.");
            onCompleteCallback?.Invoke(DialogueReaction.Neutral, new List<string>());
            return;
        }

        if (isPlaying)
        {
            Debug.LogWarning("[InkDialogController] Already playing a story.");
            return;
        }

        StartCoroutine(PlayCoroutine(inkJson, onCompleteCallback));
    }

    private IEnumerator PlayCoroutine(TextAsset inkJson, Action<DialogueReaction, List<string>> onCompleteCallback)
    {
        isPlaying = true;
        onComplete = onCompleteCallback;

        if (dialogPanelRoot != null) dialogPanelRoot.SetActive(true);
        if (choiceContainer != null) choiceContainer.SetActive(false);

        // ensure bodyText available now (in case it was added later)
        if (bodyText == null && dialogPanelRoot != null)
            bodyText = dialogPanelRoot.GetComponentInChildren<TextMeshProUGUI>(true);

        // ensure choiceButtons available (re-scan in case not in Awake)
        if ((choiceButtons == null || choiceButtons.Count == 0) && choiceContainer != null)
        {
            choiceButtons = new List<Button>();
            var found = choiceContainer.GetComponentsInChildren<Button>(true);
            foreach (var b in found)
                choiceButtons.Add(b);
        }

        // Create story from compiled JSON (Ink runtime must be installed)
        try
        {
            inkStory = new Story(inkJson.text);
        }
        catch (Exception ex)
        {
            Debug.LogError("[InkDialogController] Failed to create Story from JSON: " + ex.Message);
            Finish(DialogueReaction.Neutral, new List<string>());
            yield break;
        }

        // display lines until we reach choices
        while (inkStory.canContinue)
        {
            string line = inkStory.Continue().Trim();
            if (bodyText != null) bodyText.text = line;

            if (continueButton != null)
            {
                bool cont = false;
                UnityAction handler = () => cont = true;
                continueButton.onClick.AddListener(handler);
                while (!cont) yield return null;
                continueButton.onClick.RemoveListener(handler);
            }
            else
            {
                yield return null; // let UI update
            }
        }

        var choices = inkStory.currentChoices;
        if (choices == null || choices.Count == 0)
        {
            // no choices, finish
            Finish(DialogueReaction.Neutral, new List<string>());
            yield break;
        }

        // Activate choice container and populate buttons
        int mapCount = Math.Min(3, choices.Count);
        if (choiceContainer != null) choiceContainer.SetActive(true);

        // Clean up any previous listeners on buttons and set labels
        for (int i = 0; i < choiceButtons.Count; i++)
        {
            var btn = choiceButtons[i];
            if (btn == null) continue;

            btn.onClick.RemoveAllListeners(); // safe cleanup

            if (i < mapCount)
            {
                btn.gameObject.SetActive(true);

                string label = choices[i].text.Trim();
                if (choiceLabels != null && i < choiceLabels.Length && choiceLabels[i] != null)
                {
                    choiceLabels[i].text = label;
                }
                else
                {
                    var lbl = btn.GetComponentInChildren<TextMeshProUGUI>(true);
                    if (lbl != null) lbl.text = label;
                }

                // Add listener with UnityAction capturing index
                int idx = i;
                UnityAction choiceHandler = () => OnChoiceSelected(idx);
                btn.onClick.AddListener(choiceHandler);
            }
            else
            {
                // hide excess buttons
                btn.gameObject.SetActive(false);
            }
        }

        // Wait for selection: the OnChoiceSelected will call onComplete
        bool waiting = true;
        DialogueReaction selectedReaction = DialogueReaction.Neutral;
        List<string> selectedTags = new List<string>();

        Action<DialogueReaction, List<string>> localComplete = (r, tags) =>
        {
            selectedReaction = r;
            selectedTags = tags ?? new List<string>();
            waiting = false;
        };

        var prevOnComplete = onComplete;
        onComplete = localComplete;

        while (waiting) yield return null;

        onComplete = prevOnComplete;
        Finish(selectedReaction, selectedTags);
    }

    /// <summary>
    /// Handle button press — cleans up UI/listeners immediately, tells inkStory to choose, then invokes callback.
    /// </summary>
    private void OnChoiceSelected(int choiceIndex)
    {
        if (inkStory == null || inkStory.currentChoices == null || choiceIndex >= inkStory.currentChoices.Count)
            return;

        // Normalize tags into List<string>
        var tags = new List<string>();
        if (inkStory.currentChoices[choiceIndex].tags != null)
        {
            foreach (var t in inkStory.currentChoices[choiceIndex].tags)
                tags.Add(t);
        }

        // Immediately cleanup UI and listeners so buttons disappear
        if (choiceContainer != null) choiceContainer.SetActive(false);
        foreach (var btn in choiceButtons)
        {
            if (btn == null) continue;
            btn.onClick.RemoveAllListeners();
            // optionally hide the button GameObject to avoid visual leftover
            btn.gameObject.SetActive(false);
        }

        // Make the choice in the Ink story runtime
        inkStory.ChooseChoiceIndex(choiceIndex);

        DialogueReaction reaction = DialogueReaction.Neutral;
        if (choiceIndex == 0) reaction = DialogueReaction.Agree;
        else if (choiceIndex == 1) reaction = DialogueReaction.Neutral;
        else if (choiceIndex == 2) reaction = DialogueReaction.Disagree;

        // invoke callback (this will be captured by coroutine waiting)
        onComplete?.Invoke(reaction, tags);
    }

    private void OnContinuePressed()
    {
        // no-op, handled by dynamic UnityAction in coroutine
    }

    /// <summary>
    /// Finish: hide dialog, reset isPlaying, and call onComplete if not already called.
    /// </summary>
    private void Finish(DialogueReaction reaction, List<string> tags)
    {
        if (choiceContainer != null) choiceContainer.SetActive(false);
        if (dialogPanelRoot != null) dialogPanelRoot.SetActive(false);

        isPlaying = false;

        // invoke game-level callback (if any)
        onComplete?.Invoke(reaction, tags);
        onComplete = null;
        inkStory = null;
    }
}