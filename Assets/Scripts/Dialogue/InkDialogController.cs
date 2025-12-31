using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using Ink.Runtime;

/// <summary>
/// InkDialogController (updated)
/// - Panel scale animation (appear from small to normal, disappear reverse)
/// - Per-line typewriter effect (configurable)
/// - Continue button support (hold per line)
/// - Choice handling with suppression of echoed choice text
/// - Prioritize inspector-assigned choiceContainer + choiceButtons; fallback auto-find
/// </summary>
public enum DialogueReaction { Agree, Neutral, Disagree }

public class InkDialogController : MonoBehaviour
{
    [Header("Panel / Prefab mode")]
    [Tooltip("Optional: assign a static dialog panel in scene. If empty, dialogPrefab+dialogAnchor will be instantiated.")]
    public GameObject dialogPanelRoot;
    [Tooltip("Prefab for dialog (must contain BodyText and choice buttons). Used if dialogPanelRoot is empty.")]
    public GameObject dialogPrefab;
    [Tooltip("Parent RectTransform to instantiate dialogPrefab under.")]
    public RectTransform dialogAnchor;

    [Header("Choice UI (assign prepared container + buttons)")]
    [Tooltip("Assign the GameObject that contains your choice buttons (container).")]
    public GameObject choiceContainer;
    [Tooltip("Optional: assign the Button objects used as choices (0..n). If empty, controller will auto-find children inside choiceContainer.")]
    public List<Button> choiceButtons = new List<Button>();

    [Header("Per-line hold and typewriter")]
    [Tooltip("If assigned, the story will pause at each line until this button is pressed.")]
    public Button continueButton;
    [Tooltip("Enable per-character typing effect.")]
    public bool useTypewriter = true;
    [Tooltip("Characters per second for typewriter effect.")]
    public float typewriterCharsPerSecond = 60f;

    [Header("Panel scale animation")]
    [Tooltip("Duration of panel appear/disappear scale animation.")]
    public float panelScaleDuration = 0.18f;
    [Tooltip("Start scale factor when appearing (0..1).")]
    [Range(0.1f, 1f)]
    public float panelStartScale = 0.6f;

    private Story inkStory;
    private Action<DialogueReaction, List<string>> onComplete;
    private bool isPlaying = false;
    public bool IsPlaying => isPlaying;

    private GameObject runtimeDialogInstance = null;

    private void Awake()
    {
        if (dialogPanelRoot != null) dialogPanelRoot.SetActive(false);
        if (choiceContainer != null) choiceContainer.SetActive(false);
    }

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

        // Prepare panel (static or runtime)
        GameObject panelRoot = dialogPanelRoot;
        bool usingRuntimeInstance = false;
        if (panelRoot == null)
        {
            if (dialogPrefab == null || dialogAnchor == null)
            {
                Debug.LogError("[InkDialogController] No dialogPanelRoot and no dialogPrefab/dialogAnchor assigned.");
                Finish(DialogueReaction.Neutral, new List<string>());
                yield break;
            }
            runtimeDialogInstance = Instantiate(dialogPrefab, dialogAnchor, false);
            panelRoot = runtimeDialogInstance;
            usingRuntimeInstance = true;
        }

        // Ensure start scale
        panelRoot.SetActive(true);
        panelRoot.transform.localScale = Vector3.one * panelStartScale;
        yield return StartCoroutine(ScaleTransform(panelRoot.transform, panelStartScale, 1f, panelScaleDuration));

        // UI refs
        TextMeshProUGUI bodyText = panelRoot.GetComponentInChildren<TextMeshProUGUI>(true);

        GameObject container = choiceContainer;
        if (container == null)
        {
            var anyBtn = panelRoot.GetComponentInChildren<Button>(true);
            if (anyBtn != null) container = anyBtn.transform.parent != null ? anyBtn.transform.parent.gameObject : anyBtn.gameObject;
        }

        List<Button> buttons = new List<Button>();
        if (choiceButtons != null && choiceButtons.Count > 0)
        {
            foreach (var b in choiceButtons) if (b != null) buttons.Add(b);
        }
        else if (container != null)
        {
            buttons.AddRange(container.GetComponentsInChildren<Button>(true));
        }

        Button contBtn = continueButton;
        if (contBtn == null)
        {
            var contT = panelRoot.transform.Find("ContinueButton");
            if (contT != null) contBtn = contT.GetComponent<Button>();
        }

        // Create story
        try { inkStory = new Story(inkJson.text); }
        catch (Exception ex)
        {
            Debug.LogError("[InkDialogController] Failed to create Story: " + ex.Message);
            if (usingRuntimeInstance && runtimeDialogInstance != null) Destroy(runtimeDialogInstance);
            Finish(DialogueReaction.Neutral, new List<string>());
            yield break;
        }

        // Hide choices initially
        if (container != null) container.SetActive(false);

        // Track last choice for suppression & tags
        List<string> lastChosenTags = null;
        string lastChosenText = null;
        int? lastChosenIndex = null;

        // Main loop: proceed through story lines and choices until done
        while (true)
        {
            // Print lines while can continue
            while (inkStory.canContinue)
            {
                string line = inkStory.Continue().Trim();

                // suppress echoed choice text if equals lastChosenText
                if (!string.IsNullOrEmpty(lastChosenText) && line == lastChosenText)
                {
                    Debug.Log("[InkDialogController] Suppressed echoed choice: " + line);
                    lastChosenText = null; // only suppress once
                    continue;
                }

                if (bodyText != null)
                {
                    if (useTypewriter)
                        yield return StartCoroutine(TypewriterEffect(bodyText, line));
                    else
                        bodyText.text = line;
                }

                // wait for continue button if assigned
                if (contBtn != null)
                {
                    bool pressed = false;
                    UnityAction onPress = () => pressed = true;
                    contBtn.onClick.AddListener(onPress);
                    while (!pressed) yield return null;
                    contBtn.onClick.RemoveListener(onPress);
                }
                else
                {
                    // no continueButton: a single frame delay gives UI time to update (auto advance)
                    yield return null;
                }
            }

            // Check choices
            var choices = inkStory.currentChoices;
            if (choices == null || choices.Count == 0)
            {
                // story done
                break;
            }

            // Show choice container
            if (container != null) container.SetActive(true);

            int mapCount = Math.Min(buttons.Count, choices.Count);

            // reset buttons
            for (int i = 0; i < buttons.Count; i++)
            {
                var b = buttons[i];
                if (b == null) continue;
                b.onClick.RemoveAllListeners();
                b.gameObject.SetActive(false);
                b.interactable = false;
            }

            // populate
            for (int i = 0; i < mapCount; i++)
            {
                var btn = buttons[i];
                if (btn == null) continue;
                btn.gameObject.SetActive(true);
                btn.interactable = true;

                var tmpLabel = btn.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmpLabel != null) tmpLabel.text = choices[i].text.Trim();
                else
                {
                    var legacy = btn.GetComponentInChildren<UnityEngine.UI.Text>(true);
                    if (legacy != null) legacy.text = choices[i].text.Trim();
                }

                int idx = i; // capture
                UnityAction handler = () =>
                {
                    Debug.Log("[InkDialogController] Choice clicked idx=" + idx + " text='" + choices[idx].text + "'");
                    // hide choices immediately
                    if (container != null) container.SetActive(false);
                    foreach (var b in buttons) if (b != null) { b.onClick.RemoveAllListeners(); b.gameObject.SetActive(false); }

                    // choose choice
                    inkStory.ChooseChoiceIndex(idx);

                    // record for suppression and final tags
                    var raw = choices[idx].tags;
                    lastChosenTags = new List<string>();
                    if (raw != null) foreach (var t in raw) lastChosenTags.Add(t);
                    lastChosenIndex = idx;
                    lastChosenText = choices[idx].text.Trim();
                    // do not call onComplete here; we continue loop to print the consequences
                };

                btn.onClick.AddListener(handler);
            }

            // wait for a choice to be selected (detect via lastChosenIndex)
            while (!lastChosenIndex.HasValue)
                yield return null;

            // reset for potential next choice round
            lastChosenIndex = null;
        }

        // Story finished. Determine reaction (prefer tags)
        DialogueReaction finalReaction = DialogueReaction.Neutral;
        if (lastChosenTags != null && lastChosenTags.Count > 0)
        {
            var parsed = ParseReactionFromTags(lastChosenTags);
            if (parsed.HasValue) finalReaction = parsed.Value;
        }

        // Hide panel with scale animation
        yield return StartCoroutine(ScaleTransform(panelRoot.transform, 1f, panelStartScale, panelScaleDuration));
        if (usingRuntimeInstance && runtimeDialogInstance != null) Destroy(runtimeDialogInstance);

        Finish(finalReaction, lastChosenTags);
    }

    private IEnumerator TypewriterEffect(TextMeshProUGUI tmp, string fullText)
    {
        if (tmp == null)
            yield break;

        tmp.text = fullText;
        tmp.ForceMeshUpdate();
        int totalChars = tmp.textInfo.characterCount;
        if (totalChars == 0)
        {
            yield break;
        }

        tmp.maxVisibleCharacters = 0;
        float charsPerSec = Mathf.Max(1f, typewriterCharsPerSecond);
        float delay = 1f / charsPerSec;
        int shown = 0;
        while (shown < totalChars)
        {
            shown++;
            tmp.maxVisibleCharacters = shown;
            yield return new WaitForSeconds(delay);
        }
        // ensure full visible
        tmp.maxVisibleCharacters = totalChars;
        yield break;
    }

    private IEnumerator ScaleTransform(Transform t, float fromScale, float toScale, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(elapsed / duration);
            float s = Mathf.SmoothStep(fromScale, toScale, p);
            t.localScale = Vector3.one * s;
            yield return null;
        }
        t.localScale = Vector3.one * toScale;
    }

    private DialogueReaction? ParseReactionFromTags(List<string> tags)
    {
        if (tags == null) return null;
        foreach (var t in tags)
        {
            if (string.IsNullOrEmpty(t)) continue;
            var low = t.Trim().ToLowerInvariant();
            if (low.Contains("reaction:agree") || low == "agree") return DialogueReaction.Agree;
            if (low.Contains("reaction:neutral") || low == "neutral") return DialogueReaction.Neutral;
            if (low.Contains("reaction:disagree") || low == "disagree") return DialogueReaction.Disagree;
        }
        return null;
    }

    private void Finish(DialogueReaction reaction, List<string> tags)
    {
        if (dialogPanelRoot != null) dialogPanelRoot.SetActive(false);
        isPlaying = false;
        onComplete?.Invoke(reaction, tags);
        onComplete = null;
        inkStory = null;
    }
}