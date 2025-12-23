using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Ink.Runtime;
using TMPro;

/// <summary>
/// DialogManager: minimal Ink runtime integration + UI hooking.
/// - Requires Ink Unity Integration package (for Story class).
/// - Assign dialogPrefab (with DialogUI) and dialogAnchor (Canvas RectTransform) in Inspector.
/// Usage:
///   DialogManager.Instance.ShowStory(compiledInkJSON, followTarget: someTransform);
///   DialogManager.Instance.ShowText("Simple line", autoCloseSeconds: 2f);
/// </summary>
public class DialogManager : MonoBehaviour
{
    public static DialogManager Instance { get; private set; }

    [Header("UI")]
    public GameObject dialogPrefab; // prefab root with DialogUI component (Text + choice container)
    public RectTransform dialogAnchor; // parent for dialogPrefab instances (Canvas)
    public float autoAdvanceDefault = 0f; // 0=wait for input; >0 auto advance seconds

    // runtime
    private Story currentStory;
    private GameObject currentDialogGO;
    private DialogUI currentDialogUI;
    private Action onCompleteCallback;
    private bool waitingForChoice = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Update()
    {
        // player input to continue when waiting for a non-choice line
        if (currentStory != null && currentDialogUI != null && !waitingForChoice)
        {
            // advance on click/space/enter
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                ContinueStory();
            }
        }
    }

    // Convenience: show a simple text-only dialog (no Ink)
    public void ShowText(string text, float autoCloseSeconds = 0f, Action onComplete = null)
    {
        ClearCurrentDialog();
        if (dialogPrefab == null)
        {
            Debug.LogWarning("[DialogManager] dialogPrefab not assigned.");
            onComplete?.Invoke();
            return;
        }

        currentDialogGO = Instantiate(dialogPrefab, dialogAnchor ? dialogAnchor : null);
        currentDialogUI = currentDialogGO.GetComponent<DialogUI>();
        if (currentDialogUI != null)
        {
            currentDialogUI.SetLine(text);
            currentDialogUI.ClearChoices();
        }

        if (autoCloseSeconds > 0f)
        {
            StartCoroutine(AutoCloseCoroutine(autoCloseSeconds, onComplete));
        }
        else
        {
            onCompleteCallback = onComplete;
        }
    }

    private IEnumerator AutoCloseCoroutine(float seconds, Action callback)
    {
        yield return new WaitForSecondsRealtime(seconds);
        CloseCurrentDialog();
        callback?.Invoke();
    }

    // Start an Ink story from compiled TextAsset (ink json)
    public void ShowStory(TextAsset inkJSON, Transform followTarget = null, Action onComplete = null)
    {
        if (inkJSON == null)
        {
            Debug.LogWarning("[DialogManager] inkJSON is null.");
            onComplete?.Invoke();
            return;
        }

        ClearCurrentDialog();
        onCompleteCallback = onComplete;

        try
        {
            currentStory = new Story(inkJSON.text);
        }
        catch (Exception ex)
        {
            Debug.LogError("[DialogManager] Failed to create Story: " + ex);
            onComplete?.Invoke();
            return;
        }

        if (dialogPrefab == null)
        {
            Debug.LogWarning("[DialogManager] dialogPrefab not assigned.");
            onComplete?.Invoke();
            return;
        }

        currentDialogGO = Instantiate(dialogPrefab, dialogAnchor ? dialogAnchor : null);
        currentDialogUI = currentDialogGO.GetComponent<DialogUI>();

        // optional: if followTarget provided and you want bubble over character, compute screen pos and move UI (not implemented here)
        // (You can convert world->screen and set anchoredPosition of currentDialogGO RectTransform)

        ContinueStory();
    }

    private void ContinueStory()
    {
        if (currentStory == null)
        {
            CloseCurrentDialog();
            return;
        }

        // if there are choices queued and we are waiting for a choice, ignore
        if (currentStory.currentChoices != null && currentStory.currentChoices.Count > 0)
        {
            ShowChoices();
            return;
        }

        if (!currentStory.canContinue)
        {
            // story finished
            CloseCurrentDialog();
            onCompleteCallback?.Invoke();
            return;
        }

        // get next line
        string line = currentStory.Continue().Trim();
        var tags = currentStory.currentTags; // not used here, but available

        // show line
        if (currentDialogUI != null)
        {
            currentDialogUI.SetLine(line);
            // by default, wait for player input; if you want auto-advance check tags for duration
        }

        // if choices are available immediately after this line, will be shown on next ContinueStory call (Update loop)
        // But Ink often yields text lines and then choices; check currentChoices now:
        if (currentStory.currentChoices != null && currentStory.currentChoices.Count > 0)
        {
            ShowChoices();
        }
    }

    private void ShowChoices()
    {
        if (currentDialogUI == null || currentStory == null) return;

        var choices = currentStory.currentChoices;
        var labels = new List<string>();
        foreach (var c in choices) labels.Add(c.text);

        waitingForChoice = true;
        currentDialogUI.ShowChoices(labels, (choiceIndex) =>
        {
            // select choice in story
            currentStory.ChooseChoiceIndex(choiceIndex);
            waitingForChoice = false;
            currentDialogUI.ClearChoices();
            // ContinueStory to show next lines after choice
            ContinueStory();
        });
    }

    public void CloseCurrentDialog()
    {
        ClearCurrentDialog();
        onCompleteCallback?.Invoke();
    }

    private void ClearCurrentDialog()
    {
        if (currentDialogUI != null)
        {
            currentDialogUI.ClearChoices();
            currentDialogUI = null;
        }
        if (currentDialogGO != null)
        {
            Destroy(currentDialogGO);
            currentDialogGO = null;
        }
        currentStory = null;
        waitingForChoice = false;
    }
}