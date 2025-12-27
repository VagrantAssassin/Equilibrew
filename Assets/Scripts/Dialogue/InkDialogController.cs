using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
#if USE_INK
using Ink.Runtime;
#endif

public enum DialogueReaction
{
    Agree,
    Neutral,
    Disagree
}

/// <summary>
/// InkDialogController: menampilkan story compiled (TextAsset JSON) dan memetakan pilihan
/// - Pilihan pertama -> Agree, kedua -> Neutral, ketiga -> Disagree.
/// - Mengembalikan (reaction, tags) lewat callback.
/// </summary>
public class InkDialogController : MonoBehaviour
{
    [Header("UI")]
    public GameObject panelRoot;
    public TextMeshProUGUI bodyText;
    public Button continueButton; // optional
    public List<Button> choiceButtons = new List<Button>(3);
    public TextMeshProUGUI[] choiceLabels; // optional explicit labels inside buttons

#if USE_INK
    private Story inkStory;
#endif

    private Action<DialogueReaction, List<string>> onComplete;
    private bool isPlaying = false;

    private void Awake()
    {
        if (panelRoot != null) panelRoot.SetActive(false);

        if (continueButton != null)
            continueButton.onClick.AddListener(OnContinuePressed);

        for (int i = 0; i < choiceButtons.Count; i++)
        {
            int idx = i;
            if (choiceButtons[i] != null)
                choiceButtons[i].onClick.AddListener(() => OnChoiceSelected(idx));
        }
    }

    public void PlayCurhat(TextAsset inkJson, Action<DialogueReaction, List<string>> onCompleteCallback)
    {
        if (isPlaying)
        {
            Debug.LogWarning("[InkDialogController] Already playing a story. Ignoring PlayCurhat call.");
            return;
        }

        StartCoroutine(PlayCoroutine(inkJson, onCompleteCallback));
    }

    private IEnumerator PlayCoroutine(TextAsset inkJson, Action<DialogueReaction, List<string>> onCompleteCallback)
    {
        isPlaying = true;
        onComplete = onCompleteCallback;

        if (panelRoot != null) panelRoot.SetActive(true);

#if USE_INK
        if (inkJson == null)
        {
            Debug.LogWarning("[InkDialogController] inkJson is null - nothing to play.");
            Finish(DialogueReaction.Neutral, null);
            yield break;
        }

        inkStory = new Story(inkJson.text);

        // show initial lines until choices
        while (inkStory.canContinue)
        {
            string line = inkStory.Continue().Trim();
            if (!string.IsNullOrEmpty(line) && bodyText != null)
                bodyText.text = line;

            if (continueButton != null)
            {
                bool cont = false;
                System.Action handler = () => cont = true;
                continueButton.onClick.AddListener(handler);
                while (!cont)
                    yield return null;
                continueButton.onClick.RemoveListener(handler);
            }
            else
            {
                // wait a frame so UI updates
                yield return null;
            }
        }

        var choices = inkStory.currentChoices;
        if (choices == null || choices.Count == 0)
        {
            // story finished with no choices
            Finish(DialogueReaction.Neutral, new List<string>());
            yield break;
        }

        // map up to 3 choices
        int maxMap = Math.Min(3, choices.Count);
        for (int i = 0; i < choiceButtons.Count; i++)
        {
            var btn = choiceButtons[i];
            if (btn == null) continue;

            if (i < maxMap)
            {
                btn.gameObject.SetActive(true);
                string label = choices[i].text.Trim();
                if (choiceLabels != null && i < choiceLabels.Length && choiceLabels[i] != null)
                    choiceLabels[i].text = label;
                else
                {
                    var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
                    if (lbl != null) lbl.text = label;
                }
            }
            else
            {
                btn.gameObject.SetActive(false);
            }
        }

        // wait for selection; selection handled in OnChoiceSelected which picks from inkStory
        bool waiting = true;
        DialogueReaction selected = DialogueReaction.Neutral;
        List<string> selectedTags = new List<string>();

        Action<DialogueReaction, List<string>> localComplete = (r, tags) =>
        {
            selected = r;
            selectedTags = tags ?? new List<string>();
            waiting = false;
        };

        var prevOnComplete = onComplete;
        onComplete = localComplete;

        while (waiting) yield return null;

        onComplete = prevOnComplete;
        Finish(selected, selectedTags);
#else
        // Fallback path (no Ink runtime): show static bodyText (if any) and the three buttons, map by index
        if (inkJson != null && bodyText != null) bodyText.text = inkJson.text;

        int chosen = -1;
        for (int i = 0; i < choiceButtons.Count; i++)
        {
            var btn = choiceButtons[i];
            if (btn == null) continue;
            btn.gameObject.SetActive(true);
            int idx = i;
            btn.onClick.AddListener(() => chosen = idx);
        }

        while (chosen < 0) yield return null;

        // cleanup listeners
        for (int i = 0; i < choiceButtons.Count; i++)
            if (choiceButtons[i] != null) choiceButtons[i].onClick.RemoveAllListeners();

        DialogueReaction reaction = DialogueReaction.Neutral;
        if (chosen == 0) reaction = DialogueReaction.Agree;
        else if (chosen == 1) reaction = DialogueReaction.Neutral;
        else if (chosen == 2) reaction = DialogueReaction.Disagree;

        Finish(reaction, new List<string>());
#endif
    }

    private void OnChoiceSelected(int choiceIndex)
    {
#if USE_INK
        if (inkStory == null || inkStory.currentChoices == null || choiceIndex >= inkStory.currentChoices.Count) return;

        var choice = inkStory.currentChoices[choiceIndex];
        var tags = new List<string>(choice.tags);
        inkStory.ChooseChoiceIndex(choiceIndex);

        DialogueReaction reaction = DialogueReaction.Neutral;
        if (choiceIndex == 0) reaction = DialogueReaction.Agree;
        else if (choiceIndex == 1) reaction = DialogueReaction.Neutral;
        else if (choiceIndex == 2) reaction = DialogueReaction.Disagree;

        onComplete?.Invoke(reaction, tags);
#else
        DialogueReaction reaction = DialogueReaction.Neutral;
        if (choiceIndex == 0) reaction = DialogueReaction.Agree;
        else if (choiceIndex == 1) reaction = DialogueReaction.Neutral;
        else if (choiceIndex == 2) reaction = DialogueReaction.Disagree;

        onComplete?.Invoke(reaction, new List<string>());
#endif
    }

    private void OnContinuePressed() { /* handled inline in coroutine */ }

    private void Finish(DialogueReaction reaction, List<string> tags)
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        isPlaying = false;
        onComplete?.Invoke(reaction, tags);
        onComplete = null;
    }
}