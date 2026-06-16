using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PilihanJawaban = MultiAgentBridge.PilihanJawaban;

/// <summary>
/// CurhatChoiceUI — UI panel untuk player memilih jawaban curhat.
/// 
/// Fitur:
/// - 3 tombol pilihan jawaban (dari LLM)
/// - Input field untuk jawaban bebas
/// - Submit button untuk jawaban bebas
/// - Auto-show/hide saat curhat session aktif
/// 
/// Flow:
/// 1. MultiAgentFlowController.ShowCurhatRound() → calls ShowChoices()
/// 2. Player memilih salah satu pilihan → OnChoiceClicked(nada, teks)
///    atau player mengetik jawaban bebas → OnFreeTextSubmitted()
/// 3. MultiAgentFlowController.OnCurhatPlayerResponse() dipanggil
/// </summary>
public class CurhatChoiceUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Parent panel yang di-show/hide")]
    public GameObject panelRoot;
    
    [Tooltip("Container untuk choice buttons (Vertical/Horizontal Layout Group)")]
    public Transform choiceButtonContainer;
    
    [Tooltip("Prefab untuk choice button (harus ada TextMeshProUGUI child)")]
    public Button choiceButtonPrefab;
    
    [Tooltip("Input field untuk jawaban bebas")]
    public TMP_InputField freeTextInput;
    
    [Tooltip("Button submit untuk jawaban bebas")]
    public Button submitButton;
    
    [Tooltip("Button untuk skip curhat (opsional)")]
    public Button skipButton;
    
    [Header("Settings")]
    [Tooltip("Label untuk input field")]
    public string inputPlaceholder = "Ketik jawabanmu...";
    
    [Tooltip("Label submit button")]
    public string submitLabel = "Kirim";
    
    [Tooltip("Label skip button")]
    public string skipLabel = "Lewati Curhat";
    
    [Tooltip("Tampilkan input bebas?")]
    public bool showFreeTextInput = true;
    
    // State
    private MultiAgentFlowController flowController;
    private List<Button> activeButtons = new List<Button>();
    private bool isActive = false;
    
    // Event
    public event Action<string, string> OnPlayerResponded; // jawaban, nada
    
    void Awake()
    {
        // Cari flow controller di scene
        flowController = FindObjectOfType<MultiAgentFlowController>();
        
        // Setup button listeners
        if (submitButton != null)
        {
            submitButton.onClick.AddListener(OnSubmitFreeText);
            var tmp = submitButton.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null) tmp.text = submitLabel;
        }
        
        if (skipButton != null)
        {
            skipButton.onClick.AddListener(OnSkipCurhat);
            var tmp = skipButton.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null) tmp.text = skipLabel;
        }
        
        if (freeTextInput != null)
        {
            freeTextInput.placeholder.GetComponent<TextMeshProUGUI>().text = inputPlaceholder;
            freeTextInput.gameObject.SetActive(showFreeTextInput);
        }
        
        if (submitButton != null)
            submitButton.gameObject.SetActive(showFreeTextInput);
        
        // Hide on start
        Hide();
    }
    
    /// <summary>
    /// Tampilkan pilihan jawaban curhat.
    /// Dipanggil oleh MultiAgentFlowController.ShowCurhatRound().
    /// </summary>
    public void ShowChoices(PilihanJawaban[] pilihan)
    {
        if (panelRoot == null)
        {
            Debug.LogError("[CurhatChoiceUI] panelRoot is null!");
            return;
        }
        
        // Clear old buttons
        ClearButtons();
        
        // Show panel
        panelRoot.SetActive(true);
        isActive = true;
        
        // Create choice buttons
        if (pilihan != null && choiceButtonContainer != null && choiceButtonPrefab != null)
        {
            foreach (var p in pilihan)
            {
                if (p == null) continue;
                
                var btn = Instantiate(choiceButtonPrefab, choiceButtonContainer);
                btn.gameObject.SetActive(true);
                
                // Set button text
                var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null)
                {
                    string icon = p.nada == "satisfy" ? "💚" : p.nada == "angry" ? "🔴" : "💛";
                    tmp.text = $"{icon} {p.teks}";
                }
                
                // Capture for closure
                string capturedNada = p.nada;
                string capturedTeks = p.teks;
                btn.onClick.AddListener(() => OnChoiceClicked(capturedNada, capturedTeks));
                
                activeButtons.Add(btn);
            }
        }
        
        // Show/hide free text input
        if (freeTextInput != null)
        {
            freeTextInput.gameObject.SetActive(showFreeTextInput);
            freeTextInput.text = "";
        }
        if (submitButton != null)
            submitButton.gameObject.SetActive(showFreeTextInput);
        
        // Show skip button
        if (skipButton != null)
            skipButton.gameObject.SetActive(true);
        
        Debug.Log($"[CurhatChoiceUI] Showing {activeButtons.Count} choices.");
    }
    
    /// <summary>
    /// Hide panel dan clear semua buttons.
    /// </summary>
    public void Hide()
    {
        isActive = false;
        
        if (panelRoot != null)
            panelRoot.SetActive(false);
        
        ClearButtons();
        
        if (freeTextInput != null)
        {
            freeTextInput.text = "";
            freeTextInput.gameObject.SetActive(false);
        }
        if (submitButton != null)
            submitButton.gameObject.SetActive(false);
        if (skipButton != null)
            skipButton.gameObject.SetActive(false);
    }
    
    /// <summary>
    /// Apakah UI sedang aktif (menunggu input)?
    /// </summary>
    public bool IsActive => isActive;
    
    // ── Private Methods ───────────────────────────────────────────────────────
    
    private void OnChoiceClicked(string nada, string teks)
    {
        if (!isActive) return;
        isActive = false;
        
        Debug.Log($"[CurhatChoiceUI] Choice clicked: nada={nada}, teks={teks}");
        
        Hide();
        
        // Kirim ke flow controller
        if (flowController != null)
        {
            flowController.OnCurhatPlayerResponse(teks, nada);
        }
        
        // Fire event
        OnPlayerResponded?.Invoke(teks, nada);
    }
    
    private void OnSubmitFreeText()
    {
        if (!isActive) return;
        
        string text = freeTextInput != null ? freeTextInput.text.Trim() : "";
        
        if (string.IsNullOrEmpty(text))
        {
            Debug.LogWarning("[CurhatChoiceUI] Free text is empty.");
            return;
        }
        
        isActive = false;
        
        Debug.Log($"[CurhatChoiceUI] Free text submitted: {text}");
        
        Hide();
        
        // Kirim ke flow controller (nada null = auto-detect)
        if (flowController != null)
        {
            flowController.OnCurhatPlayerResponse(text, null);
        }
        
        // Fire event
        OnPlayerResponded?.Invoke(text, null);
    }
    
    private void OnSkipCurhat()
    {
        if (!isActive) return;
        isActive = false;
        
        Debug.Log("[CurhatChoiceUI] Skipped curhat.");
        
        Hide();
        
        // Kirim skip ke flow controller
        if (flowController != null)
        {
            flowController.OnCurhatPlayerResponse("...", "neutral");
        }
        
        // Fire event
        OnPlayerResponded?.Invoke("...", "neutral");
    }
    
    private void ClearButtons()
    {
        foreach (var btn in activeButtons)
        {
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                Destroy(btn.gameObject);
            }
        }
        activeButtons.Clear();
    }
    
    void OnDestroy()
    {
        ClearButtons();
    }
}
