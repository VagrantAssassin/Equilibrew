// (Full file with only one small addition: public bool IsPanelOpen property)

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using Ink.Runtime;
using PilihanJawaban = MultiAgentBridge.PilihanJawaban;

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

    [Header("Affinity Widget (optional)")]
    [Tooltip("Customer affinity widget. Shown together with the dialog panel opening and hidden when the panel closes.")]
    public CustomerAffinityWidget affinityWidget;

    private Story inkStory;
    private Action<DialogueReaction, List<string>> onComplete;
    public event Action<int, string, List<string>> OnChoiceSelected;
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
        if (continueButton != null) continueButton.gameObject.SetActive(false);
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
        // Show affinity widget together with dialog panel
        affinityWidget?.Show();
        if (!(skipOpenAnimation || runtimeDialogInstance != null && runtimeKeptOpen))
        {
            panelRoot.transform.localScale = Vector3.one * panelStartScale;
            if (affinityWidget != null)
            {
                affinityWidget.transform.localScale = Vector3.one * panelStartScale;
                StartCoroutine(ScaleTransform(affinityWidget.transform, panelStartScale, 1f, panelScaleDuration));
            }
            yield return StartCoroutine(ScaleTransform(panelRoot.transform, panelStartScale, 1f, panelScaleDuration));
        }
        else
        {
            panelRoot.transform.localScale = Vector3.one;
            if (affinityWidget != null) affinityWidget.transform.localScale = Vector3.one;
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
                    OnChoiceSelected?.Invoke(idx, choices[idx].text.Trim(), new List<string>(lastChosenTags));
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
            if (affinityWidget != null) StartCoroutine(ScaleTransform(affinityWidget.transform, 1f, panelStartScale, panelScaleDuration));
            yield return StartCoroutine(ScaleTransform(t, 1f, panelStartScale, panelScaleDuration));

            // Hide affinity widget together with dialog panel
            affinityWidget?.Hide();

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

    // ══════════════════════════════════════════════════════════════════════════
    // DYNAMIC TEXT MODE — untuk Multi-Agent integration (tanpa Ink story)
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// PlayDynamicText — tampilkan text langsung (dari LLM) tanpa Ink story.
    /// Mendukung typewriter, panel animation, acknowledge button.
    /// 
    /// Digunakan oleh MultiAgentFlowController untuk menampilkan dialog
    /// yang di-generate oleh Python Multi-Agent system.
    /// </summary>
    public void PlayDynamicText(string text, Action onCompleteCallback, bool leavePanelOpen = false, bool requireAcknowledge = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            Debug.LogWarning("[InkDialogController] PlayDynamicText called with empty text.");
            onCompleteCallback?.Invoke();
            return;
        }
        if (isPlaying)
        {
            Debug.LogWarning("[InkDialogController] Already playing. Ignoring PlayDynamicText.");
            onCompleteCallback?.Invoke();
            return;
        }
        StartCoroutine(PlayDynamicTextCoroutine(text, onCompleteCallback, leavePanelOpen, requireAcknowledge));
    }

    private IEnumerator PlayDynamicTextCoroutine(string text, Action onCompleteCallback, bool leavePanelOpen, bool requireAcknowledge)
    {
        isPlaying = true;

        // Split text into sentences for per-sentence display
        var sentences = SplitIntoSentences(text);
        if (sentences.Count == 0)
        {
            isPlaying = false;
            onCompleteCallback?.Invoke();
            yield break;
        }

        // Get or create panel root
        GameObject panelRoot = dialogPanelRoot;

        if (runtimeDialogInstance != null)
        {
            panelRoot = runtimeDialogInstance;
        }
        else if (panelRoot == null)
        {
            if (dialogPrefab == null || dialogAnchor == null)
            {
                Debug.LogError("[InkDialogController] No dialogPanelRoot and no dialogPrefab/dialogAnchor.");
                isPlaying = false;
                onCompleteCallback?.Invoke();
                yield break;
            }
            runtimeDialogInstance = Instantiate(dialogPrefab, dialogAnchor, false);
            panelRoot = runtimeDialogInstance;

            // Apply cached speaker name
            if (!string.IsNullOrEmpty(cachedSpeakerName))
                SetSpeakerName(cachedSpeakerName);
        }

        // Show panel with animation (animate open only on first appearance)
        bool shouldAnimateOpen = !panelRoot.activeSelf || (runtimeDialogInstance != null && !runtimeKeptOpen);
        panelRoot.SetActive(true);
        affinityWidget?.Show();

        if (shouldAnimateOpen)
        {
            panelRoot.transform.localScale = Vector3.one * panelStartScale;
            if (affinityWidget != null)
            {
                affinityWidget.transform.localScale = Vector3.one * panelStartScale;
                StartCoroutine(ScaleTransform(affinityWidget.transform, panelStartScale, 1f, panelScaleDuration));
            }
            yield return StartCoroutine(ScaleTransform(panelRoot.transform, panelStartScale, 1f, panelScaleDuration));
        }

        // Find body text
        TextMeshProUGUI bodyText = panelRoot.GetComponentInChildren<TextMeshProUGUI>(true);

        // Find or create continue button
        Button contBtn = continueButton;
        if (contBtn == null)
        {
            var contT = panelRoot.transform.Find("ContinueButton");
            if (contT != null) contBtn = contT.GetComponent<Button>();
        }

        bool createdDynamicContinue = false;
        if (contBtn == null)
        {
            var panelBtn = panelRoot.GetComponent<Button>();
            if (panelBtn == null)
                panelBtn = panelRoot.AddComponent<Button>();
            contBtn = panelBtn;
            createdDynamicContinue = true;

            var panelColors = contBtn.colors;
            panelColors.normalColor = Color.clear;
            panelColors.highlightedColor = Color.clear;
            panelColors.pressedColor = new Color(1, 1, 1, 0.1f);
            contBtn.colors = panelColors;
            contBtn.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
        }

        // Hide choice container during sentence display
        if (choiceContainer != null) choiceContainer.SetActive(false);

        // ── Display sentences one by one ──
        for (int i = 0; i < sentences.Count; i++)
        {
            bool isLast = (i == sentences.Count - 1);

            // Show sentence with typewriter
            if (bodyText != null)
            {
                if (useTypewriter)
                    yield return StartCoroutine(TypewriterEffect(bodyText, sentences[i]));
                else
                    bodyText.text = sentences[i];
            }

            // Last sentence without acknowledge requirement → auto-advance after read delay
            if (isLast && !requireAcknowledge)
            {
                float autoReadDelay = Mathf.Max(1.5f, sentences[i].Length / 40f);
                yield return new WaitForSecondsRealtime(autoReadDelay);
            }
            // Otherwise wait for player click to continue
            else if (contBtn != null)
            {
                contBtn.gameObject.SetActive(true);
                contBtn.interactable = true;
                bool ack = false;
                UnityAction onPress = () => ack = true;
                contBtn.onClick.AddListener(onPress);
                while (!ack) yield return null;
                contBtn.onClick.RemoveListener(onPress);
                if (!createdDynamicContinue)
                    contBtn.gameObject.SetActive(false);
                else
                    contBtn.interactable = false;
            }
            else
            {
                float readDelay = Mathf.Max(1.5f, sentences[i].Length / 40f);
                yield return new WaitForSecondsRealtime(readDelay);
            }
        }

        // Close panel or leave open
        if (leavePanelOpen)
        {
            runtimeKeptOpen = true;
        }
        else
        {
            yield return StartCoroutine(AnimateClosePanel(panelRoot));
        }

        isPlaying = false;
        onCompleteCallback?.Invoke();
    }

    /// <summary>
    /// ShowChoicesOnPanel — tampilkan pilihan jawaban menggunakan choice buttons yang ada
    /// (Choice_Agree, Choice_Neutral, Choice_Disagree).
    /// 
    /// Dipanggil setelah dialog NPC selesai ditampilkan (per-sentence).
    /// Player memilih salah satu → callback(jawaban, nada).
    /// </summary>
    public void ShowChoicesOnPanel(PilihanJawaban[] pilihan, Action<string, string> onChoiceSelected)
    {
        if (pilihan == null || pilihan.Length == 0)
        {
            onChoiceSelected?.Invoke("", "neutral");
            return;
        }

        GameObject panelRoot = runtimeDialogInstance ?? dialogPanelRoot;
        if (panelRoot == null)
        {
            Debug.LogWarning("[InkDialogController] No panel for ShowChoicesOnPanel.");
            onChoiceSelected?.Invoke("", "neutral");
            return;
        }

        // Keep panel open
        panelRoot.SetActive(true);

        // Hide continue button while choices are displayed
        if (continueButton != null) continueButton.gameObject.SetActive(false);

        // Check if choiceContainer is actually visible in hierarchy
        // (may be inside an inactive parent like InkDialogPanel)
        bool containerUsable = choiceContainer != null && choiceContainer.activeInHierarchy;

        // Use existing choice buttons from InkDialogController
        List<Button> buttons = new List<Button>();
        if (containerUsable && choiceButtons != null && choiceButtons.Count > 0)
        {
            foreach (var b in choiceButtons)
                if (b != null) buttons.Add(b);
        }
        else if (containerUsable)
        {
            buttons.AddRange(choiceContainer.GetComponentsInChildren<Button>(true));
        }

        // Show choice container (only if parent hierarchy is active)
        if (containerUsable) choiceContainer.SetActive(true);

        // Configure each button
        int count = Mathf.Min(buttons.Count, pilihan.Length);
        for (int i = 0; i < buttons.Count; i++)
        {
            var btn = buttons[i];
            btn.onClick.RemoveAllListeners();
            btn.gameObject.SetActive(i < count);
            btn.interactable = i < count;
        }

        for (int i = 0; i < count; i++)
        {
            var btn = buttons[i];
            var pil = pilihan[i];

            var tmp = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null)
            {
                string icon = pil.nada == "satisfy" ? "💚" : pil.nada == "angry" ? "🔴" : "💛";
                tmp.text = icon + " " + pil.teks;
            }

            string capturedTeks = pil.teks;
            string capturedNada = pil.nada;
            btn.onClick.AddListener(() =>
            {
                // Hide choices
                if (containerUsable && choiceContainer != null) choiceContainer.SetActive(false);
                foreach (var b in buttons) { b.onClick.RemoveAllListeners(); b.gameObject.SetActive(false); }
                onChoiceSelected?.Invoke(capturedTeks, capturedNada);
            });
        }

        if (count == 0 || !containerUsable)
        {
            Debug.LogWarning($"[InkDialogController] No usable choice buttons (count={count}, containerUsable={containerUsable}). Using fallback.");
            // Fallback: create temporary choice buttons under runtimeDialogInstance
            StartFallbackChoiceCoroutine(pilihan, onChoiceSelected);
        }
    }

    /// <summary>
    /// Split text into sentences for per-sentence dialog display.
    /// Handles Indonesian punctuation (. ! ?) and edge cases.
    /// </summary>
    /// <summary>
    /// Fallback: create temporary choice buttons when no configured buttons exist.
    /// Prevents auto-select which causes NPC to respond without player input.
    /// </summary>
    private void StartFallbackChoiceCoroutine(PilihanJawaban[] pilihan, Action<string, string> onChoiceSelected)
    {
        // Find the Canvas to parent the choice panel
        Canvas canvas = null;
        var panelRoot = runtimeDialogInstance ?? dialogPanelRoot;
        if (panelRoot != null)
            canvas = panelRoot.GetComponentInParent<Canvas>();

        // Create a standalone choice panel under the Canvas (not under dialog)
        GameObject choicePanel = new GameObject("FallbackChoicePanel");
        CanvasGroup cg = choicePanel.AddComponent<CanvasGroup>();
        cg.alpha = 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;

        RectTransform panelRt = choicePanel.AddComponent<RectTransform>();
        if (canvas != null)
            panelRt.SetParent(canvas.transform, false);

        // Semi-transparent dark background
        Image panelBg = choicePanel.AddComponent<Image>();
        panelBg.color = new Color(0.05f, 0.05f, 0.1f, 0.85f);

        // Position: bottom center of screen
        panelRt.anchorMin = new Vector2(0.15f, 0.02f);
        panelRt.anchorMax = new Vector2(0.85f, 0.38f);
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;

        // Vertical layout for buttons
        VerticalLayoutGroup vlg = choicePanel.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8f;
        vlg.padding = new RectOffset(10, 10, 10, 10);
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        // Add title
        GameObject titleGo = new GameObject("Title");
        titleGo.transform.SetParent(choicePanel.transform, false);
        RectTransform titleRt = titleGo.AddComponent<RectTransform>();
        TextMeshProUGUI titleTmp = titleGo.AddComponent<TextMeshProUGUI>();
        titleTmp.text = "Pilih jawabanmu:";
        titleTmp.fontSize = 16;
        titleTmp.alignment = TextAlignmentOptions.Center;
        titleTmp.color = new Color(0.8f, 0.8f, 0.8f);
        LayoutElement titleLe = titleGo.AddComponent<LayoutElement>();
        titleLe.preferredHeight = 25f;
        titleLe.flexibleWidth = 1f;

        // Create a button for each choice
        for (int i = 0; i < pilihan.Length; i++)
        {
            var pil = pilihan[i];
            string icon = pil.nada == "satisfy" ? "💚" : pil.nada == "angry" ? "🔴" : "💛";
            string nadaLabel = pil.nada == "satisfy" ? "[Satisfy]" : pil.nada == "angry" ? "[Angry]" : "[Neutral]";

            GameObject btnGo = new GameObject($"Choice_{i}");
            btnGo.transform.SetParent(choicePanel.transform, false);

            RectTransform btnRt = btnGo.AddComponent<RectTransform>();

            // Button background
            Image btnImg = btnGo.AddComponent<Image>();
            btnImg.color = new Color(0.15f, 0.15f, 0.25f, 0.95f);

            Button btn = btnGo.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = new Color(0.15f, 0.15f, 0.25f, 0.95f);
            cb.highlightedColor = new Color(0.25f, 0.35f, 0.55f, 1f);
            cb.pressedColor = new Color(0.1f, 0.2f, 0.4f, 1f);
            btn.colors = cb;

            // Layout element for sizing
            LayoutElement le = btnGo.AddComponent<LayoutElement>();
            le.preferredHeight = 55f;
            le.flexibleWidth = 1f;

            // Text container (HorizontalLayoutGroup for icon + text)
            GameObject textContainer = new GameObject("TextContainer");
            textContainer.transform.SetParent(btnGo.transform, false);
            RectTransform tcRt = textContainer.AddComponent<RectTransform>();
            // Stretch to fill button
            tcRt.anchorMin = Vector2.zero;
            tcRt.anchorMax = Vector2.one;
            tcRt.offsetMin = new Vector2(10, 0);
            tcRt.offsetMax = new Vector2(-10, 0);
            HorizontalLayoutGroup hlg = textContainer.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            // Nada label
            GameObject nadaGo = new GameObject("Nada");
            nadaGo.transform.SetParent(textContainer.transform, false);
            TextMeshProUGUI nadaTmp = nadaGo.AddComponent<TextMeshProUGUI>();
            nadaTmp.text = $"{icon} {nadaLabel}";
            nadaTmp.fontSize = 14;
            nadaTmp.alignment = TextAlignmentOptions.Left;
            nadaTmp.color = pil.nada == "satisfy" ? new Color(0.3f, 0.9f, 0.4f) :
                           pil.nada == "angry" ? new Color(0.9f, 0.3f, 0.3f) :
                           new Color(0.9f, 0.9f, 0.4f);
            LayoutElement nadaLe = nadaGo.AddComponent<LayoutElement>();
            nadaLe.preferredWidth = 90f;

            // Choice text
            GameObject txtGo = new GameObject("Text");
            txtGo.transform.SetParent(textContainer.transform, false);
            TextMeshProUGUI tmp = txtGo.AddComponent<TextMeshProUGUI>();
            tmp.text = pil.teks;
            tmp.fontSize = 15;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.color = Color.white;
            tmp.enableWordWrapping = true;

            string capturedTeks = pil.teks;
            string capturedNada = pil.nada;
            btn.onClick.AddListener(() =>
            {
                // Cleanup: destroy entire choice panel
                Destroy(choicePanel);
                onChoiceSelected?.Invoke(capturedTeks, capturedNada);
            });
        }

        // Add a skip button at the bottom
        GameObject skipGo = new GameObject("SkipButton");
        skipGo.transform.SetParent(choicePanel.transform, false);
        RectTransform skipRt = skipGo.AddComponent<RectTransform>();
        Image skipImg = skipGo.AddComponent<Image>();
        skipImg.color = new Color(0.2f, 0.2f, 0.2f, 0.7f);
        Button skipBtn = skipGo.AddComponent<Button>();
        skipBtn.colors = new ColorBlock {
            normalColor = new Color(0.2f, 0.2f, 0.2f, 0.7f),
            highlightedColor = new Color(0.3f, 0.3f, 0.3f, 0.9f),
            pressedColor = new Color(0.15f, 0.15f, 0.15f, 0.8f)
        };
        LayoutElement skipLe = skipGo.AddComponent<LayoutElement>();
        skipLe.preferredHeight = 30f;
        skipLe.flexibleWidth = 1f;

        GameObject skipTxtGo = new GameObject("Text");
        skipTxtGo.transform.SetParent(skipGo.transform, false);
        TextMeshProUGUI skipTmp = skipTxtGo.AddComponent<TextMeshProUGUI>();
        skipTmp.text = "Lewati";
        skipTmp.fontSize = 13;
        skipTmp.alignment = TextAlignmentOptions.Center;
        skipTmp.color = new Color(0.6f, 0.6f, 0.6f);

        skipBtn.onClick.AddListener(() =>
        {
            Destroy(choicePanel);
            // Default to neutral if skipped
            onChoiceSelected?.Invoke("", "neutral");
        });

        // Fade in animation
        StartCoroutine(FadeInPanel(cg));
    }

    private IEnumerator FadeInPanel(CanvasGroup cg)
    {
        if (cg == null) yield break;
        cg.alpha = 0f;
        float duration = 0.2f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            cg.alpha = Mathf.Lerp(0f, 1f, elapsed / duration);
            yield return null;
        }
        cg.alpha = 1f;
    }

    private List<string> SplitIntoSentences(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        text = text.Trim();
        // Split on sentence-ending punctuation followed by whitespace or end
        var parts = System.Text.RegularExpressions.Regex.Split(text, @"(?<=[.!?])\s+");

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                result.Add(trimmed);
        }

        if (result.Count == 0)
            result.Add(text);

        return result;
    }

    /// <summary>
    /// Animate panel close (scale down, hide, cleanup).
    /// </summary>
    private IEnumerator AnimateClosePanel(GameObject panelRoot)
    {
        if (panelRoot == null) yield break;

        Transform t = panelRoot.transform;
        if (affinityWidget != null)
            StartCoroutine(ScaleTransform(affinityWidget.transform, 1f, panelStartScale, panelScaleDuration));
        yield return StartCoroutine(ScaleTransform(t, 1f, panelStartScale, panelScaleDuration));

        affinityWidget?.Hide();

        if (continueButton != null)
            continueButton.gameObject.SetActive(false);

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

    /// <summary>
    /// ForceClose — paksa tutup dialog panel.
    /// Digunakan saat customer pergi atau sesi berakhir.
    /// </summary>
    public void ForceClose()
    {
        StopAllCoroutines();
        isPlaying = false;
        runtimeKeptOpen = false;

        if (runtimeDialogInstance != null)
        {
            Destroy(runtimeDialogInstance);
            runtimeDialogInstance = null;
        }

        if (dialogPanelRoot != null)
            dialogPanelRoot.SetActive(false);

        affinityWidget?.Hide();

        if (choiceContainer != null)
            choiceContainer.SetActive(false);

        if (continueButton != null)
            continueButton.gameObject.SetActive(false);

        Debug.Log("[InkDialogController] ForceClose complete.");
    }
}
