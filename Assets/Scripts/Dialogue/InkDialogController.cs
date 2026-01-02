// (Full file with only one small addition: public bool IsPanelOpen property)

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using Ink.Runtime;

/// <summary>
/// InkDialogController (fixed)
/// - Menggabungkan tags pilihan terakhir dan story.currentTags pada akhir cerita,
///   sehingga semua tag yang di-author pada .ink dikirim ke callback.
/// - Menyediakan SetSpeakerName dan behavior runtime instance seperti sebelumnya.
/// </summary>
public enum DialogueReaction { Agree, Neutral, Disagree }

public class InkDialogController : MonoBehaviour
{
    [Header("Panel / Prefab mode")]
    public GameObject dialogPanelRoot;
    public GameObject dialogPrefab;
    public RectTransform dialogAnchor;

    [Header("Choice UI (assign if preferred)")]
    public GameObject choiceContainer;
    public List<Button> choiceButtons = new List<Button>();

    [Header("Speaker name (optional)")]
    [Tooltip("Optional TextMeshProUGUI inside dialog prefab to show speaker/customer name. Child object name typically 'SpeakerName' or 'Name'.")]
    public TextMeshProUGUI speakerNameText;

    [Header("Hold control & typewriter")]
    public Button continueButton;
    public bool useTypewriter = true;
    public float typewriterCharsPerSecond = 60f;

    [Header("Panel animation")]
    public float panelScaleDuration = 0.18f;
    [Range(0.1f, 1f)]
    public float panelStartScale = 0.6f;

    private Story inkStory;
    private Action<DialogueReaction, List<string>> onComplete;
    private bool isPlaying = false;
    public bool IsPlaying => isPlaying;

    private GameObject runtimeDialogInstance = null;
    private bool runtimeKeptOpen = false;

    // cached speaker name so SetSpeakerName before PlayCurhat will be applied later
    private string cachedSpeakerName = "";

    private void Awake()
    {
        if (dialogPanelRoot != null) dialogPanelRoot.SetActive(false);
        if (choiceContainer != null) choiceContainer.SetActive(false);
    }

    // NEW: expose whether dialog panel is currently visible/open
    public bool IsPanelOpen
    {
        get
        {
            if (runtimeDialogInstance != null) return runtimeDialogInstance.activeInHierarchy;
            if (dialogPanelRoot != null) return dialogPanelRoot.activeInHierarchy;
            return false;
        }
    }

    // --- speaker name helper (unchanged except using GetComponentInChildren) ---
    public void SetSpeakerName(string name)
    {
        if (string.IsNullOrEmpty(name)) name = "";
        cachedSpeakerName = name ?? "";

        // 1) runtime instance
        if (runtimeDialogInstance != null)
        {
            var t = FindChildByNamesRecursive(runtimeDialogInstance.transform, new string[] { "SpeakerName", "Name" });
            if (t != null)
            {
                var tmp = t.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmp != null) { tmp.text = name; return; }
            }
            var tmpFallback = FindTMPByNameHint(runtimeDialogInstance.transform, "name");
            if (tmpFallback != null) { tmpFallback.text = name; return; }
        }

        // 2) inspector-assigned
        if (speakerNameText != null)
        {
            speakerNameText.text = name;
            return;
        }

        // 3) dialogPanelRoot
        if (dialogPanelRoot != null)
        {
            var t2 = FindChildByNamesRecursive(dialogPanelRoot.transform, new string[] { "SpeakerName", "Name" });
            if (t2 != null)
            {
                var tmp2 = t2.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmp2 != null) { tmp2.text = name; return; }
            }
            var tmpFallback2 = FindTMPByNameHint(dialogPanelRoot.transform, "name");
            if (tmpFallback2 != null) { tmpFallback2.text = name; return; }
        }
    }

    /// <summary>
    /// PlayCurhat(TextAsset inkJson, callback, skipOpenAnimation=false, leavePanelOpen=false, requireUserToAcknowledgeEnd=false)
    /// </summary>
    public void PlayCurhat(TextAsset inkJson, Action<DialogueReaction, List<string>> onCompleteCallback, bool skipOpenAnimation = false, bool leavePanelOpen = false, bool requireUserToAcknowledgeEnd = false)
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
        StartCoroutine(PlayCoroutine(inkJson, onCompleteCallback, skipOpenAnimation, leavePanelOpen, requireUserToAcknowledgeEnd));
    }

    private IEnumerator PlayCoroutine(TextAsset inkJson, Action<DialogueReaction, List<string>> onCompleteCallback, bool skipOpenAnimation, bool leavePanelOpen, bool requireUserToAcknowledgeEnd)
    {
        isPlaying = true;
        onComplete = onCompleteCallback;

        GameObject panelRoot = dialogPanelRoot;
        bool usingRuntimeInstThisCall = false;
        if (runtimeDialogInstance != null)
        {
            panelRoot = runtimeDialogInstance;
            skipOpenAnimation = skipOpenAnimation || true;
        }
        else if (panelRoot == null)
        {
            if (dialogPrefab == null || dialogAnchor == null)
            {
                Debug.LogError("[InkDialogController] No dialogPanelRoot and no dialogPrefab/dialogAnchor assigned.");
                Finish(DialogueReaction.Neutral, new List<string>(), leavePanelOpen);
                yield break;
            }
            runtimeDialogInstance = Instantiate(dialogPrefab, dialogAnchor, false);
            panelRoot = runtimeDialogInstance;
            usingRuntimeInstThisCall = true;

            // Apply cached speaker name if present
            if (!string.IsNullOrEmpty(cachedSpeakerName))
            {
                var speak = FindChildByNamesRecursive(runtimeDialogInstance.transform, new string[] { "SpeakerName", "Name" });
                if (speak != null)
                {
                    var tmp = speak.GetComponentInChildren<TextMeshProUGUI>(true);
                    if (tmp != null) tmp.text = cachedSpeakerName;
                }
                else
                {
                    var tmpFallback = FindTMPByNameHint(runtimeDialogInstance.transform, "name");
                    if (tmpFallback != null) tmpFallback.text = cachedSpeakerName;
                }
            }
        }

        if (runtimeDialogInstance != null && speakerNameText == null)
        {
            var speak = FindChildByNamesRecursive(runtimeDialogInstance.transform, new string[] { "SpeakerName", "Name" });
            if (speak != null)
            {
                var tmp = speak.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmp != null) speakerNameText = tmp;
            }
        }

        panelRoot.SetActive(true);
        if (!(skipOpenAnimation || runtimeDialogInstance != null && runtimeKeptOpen))
        {
            panelRoot.transform.localScale = Vector3.one * panelStartScale;
            yield return StartCoroutine(ScaleTransform(panelRoot.transform, panelStartScale, 1f, panelScaleDuration));
        }
        else
        {
            panelRoot.transform.localScale = Vector3.one;
        }

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

        try { inkStory = new Story(inkJson.text); }
        catch (Exception ex)
        {
            Debug.LogError("[InkDialogController] Failed to create Story: " + ex.Message);
            Finish(DialogueReaction.Neutral, new List<string>(), leavePanelOpen);
            yield break;
        }

        if (container != null) container.SetActive(false);
        if (contBtn != null) { contBtn.gameObject.SetActive(false); contBtn.interactable = false; }

        List<string> lastChosenTags = null;
        string lastChosenText = null;
        int? lastChosenIndex = null;

        while (true)
        {
            while (inkStory.canContinue)
            {
                string line = inkStory.Continue().Trim();

                if (!string.IsNullOrEmpty(lastChosenText) && line == lastChosenText)
                {
                    Debug.Log("[InkDialogController] Suppressed echoed choice line: " + line);
                    lastChosenText = null;
                    continue;
                }

                if (bodyText != null)
                {
                    if (useTypewriter)
                        yield return StartCoroutine(TypewriterEffect(bodyText, line));
                    else
                        bodyText.text = line;
                }

                if (contBtn != null && inkStory.canContinue)
                {
                    contBtn.gameObject.SetActive(true);
                    contBtn.interactable = true;
                    bool pressed = false;
                    UnityAction onPress = () => pressed = true;
                    contBtn.onClick.AddListener(onPress);
                    while (!pressed) yield return null;
                    contBtn.onClick.RemoveListener(onPress);
                    contBtn.gameObject.SetActive(false);
                }
                else
                {
                    yield return null;
                }
            }

            var choices = inkStory.currentChoices;
            if (choices == null || choices.Count == 0)
            {
                break;
            }

            if (container != null) container.SetActive(true);

            int mapCount = Math.Min(buttons.Count, choices.Count);
            for (int i = 0; i < buttons.Count; i++)
            {
                var b = buttons[i];
                if (b == null) continue;
                b.onClick.RemoveAllListeners();
                b.gameObject.SetActive(false);
                b.interactable = false;
            }

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

                int idx = i;
                UnityAction handler = () =>
                {
                    Debug.Log("[InkDialogController] Choice clicked idx=" + idx + " text='" + choices[idx].text + "'");
                    if (container != null) container.SetActive(false);
                    foreach (var b2 in buttons) if (b2 != null) { b2.onClick.RemoveAllListeners(); b2.gameObject.SetActive(false); }

                    inkStory.ChooseChoiceIndex(idx);

                    // collect tags from the chosen choice
                    var raw = choices[idx].tags;
                    lastChosenTags = new List<string>();
                    if (raw != null) foreach (var t in raw) lastChosenTags.Add(t);
                    lastChosenIndex = idx;
                    lastChosenText = choices[idx].text.Trim();
                };

                btn.onClick.AddListener(handler);
            }

            while (!lastChosenIndex.HasValue) yield return null;
            lastChosenIndex = null;
        }

        // At this point, story ended (no more choices)
        // Build final tag list: combine lastChosenTags (choice tags) and inkStory.currentTags (tags on final line)
        List<string> finalTags = new List<string>();
        if (lastChosenTags != null)
        {
            foreach (var t in lastChosenTags) if (!string.IsNullOrEmpty(t) && !finalTags.Contains(t)) finalTags.Add(t);
        }

        // inkStory.currentTags contains tags on the last continued line(s)
        var curr = inkStory.currentTags;
        if (curr != null && curr.Count > 0)
        {
            foreach (var t in curr)
            {
                if (string.IsNullOrEmpty(t)) continue;
                if (!finalTags.Contains(t)) finalTags.Add(t);
            }
        }

        // Determine final reaction from lastChosenTags (choice tags) if available, else neutral
        DialogueReaction finalReaction = DialogueReaction.Neutral;
        if (lastChosenTags != null && lastChosenTags.Count > 0)
        {
            var parsed = ParseReactionFromTags(lastChosenTags);
            if (parsed.HasValue) finalReaction = parsed.Value;
        }
        else if (curr != null && curr.Count > 0)
        {
            var parsed2 = ParseReactionFromTags(curr);
            if (parsed2.HasValue) finalReaction = parsed2.Value;
        }

        if (contBtn != null) { contBtn.gameObject.SetActive(false); contBtn.interactable = false; }
        if (container != null) container.SetActive(false);

        if (requireUserToAcknowledgeEnd && contBtn != null)
        {
            contBtn.gameObject.SetActive(true);
            contBtn.interactable = true;
            bool ack = false;
            UnityAction onPressEnd = () => ack = true;
            contBtn.onClick.AddListener(onPressEnd);
            while (!ack) yield return null;
            contBtn.onClick.RemoveListener(onPressEnd);
            contBtn.gameObject.SetActive(false);
        }

        if (runtimeDialogInstance != null && leavePanelOpen)
        {
            runtimeKeptOpen = true;
        }
        else
        {
            Transform t = panelRoot.transform;
            yield return StartCoroutine(ScaleTransform(t, 1f, panelStartScale, panelScaleDuration));

            if (runtimeDialogInstance != null)
            {
                Destroy(runtimeDialogInstance);
                runtimeDialogInstance = null;
                runtimeKeptOpen = false;
            }
            else
            {
                if (dialogPanelRoot != null) dialogPanelRoot.SetActive(false);
            }
        }

        // Pass finalReaction and finalTags to callback (ensures tags on final line are included)
        Finish(finalReaction, finalTags, leavePanelOpen);
    }

    private void Finish(DialogueReaction reaction, List<string> tags, bool leavePanelOpen)
    {
        isPlaying = false;
        onComplete?.Invoke(reaction, tags ?? new List<string>());
        onComplete = null;
        inkStory = null;
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

    private IEnumerator TypewriterEffect(TextMeshProUGUI tmp, string fullText)
    {
        if (tmp == null) yield break;
        tmp.text = fullText;
        tmp.ForceMeshUpdate();
        int totalChars = tmp.textInfo.characterCount;
        if (totalChars == 0) yield break;

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
        tmp.maxVisibleCharacters = totalChars;
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

    // Helper: find child recursively by any of the given names (case-insensitive)
    private Transform FindChildByNamesRecursive(Transform root, string[] names)
    {
        if (root == null || names == null) return null;
        foreach (var n in names)
        {
            if (string.Equals(root.name, n, StringComparison.OrdinalIgnoreCase)) return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            var found = FindChildByNamesRecursive(c, names);
            if (found != null) return found;
        }
        return null;
    }

    // Helper: find first TextMeshProUGUI child whose transform.name contains hint (case-insensitive)
    private TextMeshProUGUI FindTMPByNameHint(Transform root, string hint)
    {
        if (root == null || string.IsNullOrEmpty(hint)) return null;
        if (root.name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var tmpHere = root.GetComponent<TextMeshProUGUI>();
            if (tmpHere != null) return tmpHere;
        }
        for (int i = 0; i < root.childCount; i++)
        {
            var t = FindTMPByNameHint(root.GetChild(i), hint);
            if (t != null) return t;
        }
        return null;
    }
}