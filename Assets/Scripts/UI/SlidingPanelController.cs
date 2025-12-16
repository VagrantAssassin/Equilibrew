using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(RectTransform))]
public class SlidingPanelController : MonoBehaviour
{
    [Header("References")]
    public RectTransform panelRect;      // biasanya this.GetComponent<RectTransform>()
    public Button toggleButton;          // tombol yang memicu show/hide (child atau sibling)
    public Canvas parentCanvas;          // optional, untuk canvas scale reference

    [Header("Toggle Button Label (optional)")]
    public TextMeshProUGUI toggleButtonTMPText; // assign jika label menggunakan TextMeshPro
    public Text toggleButtonUIText;              // fallback jika label pakai legacy UI Text
    [Tooltip("Symbol to show when panel is open (example: \"<\")")]
    public string openSymbol = "<";
    [Tooltip("Symbol to show when panel is closed (example: \">\")")]
    public string closedSymbol = ">";

    [Header("Behaviour")]
    [Tooltip("Lebar area yang tetap terlihat ketika panel 'tertutup' (px). Biasanya lebar tombol.")]
    public float visibleWidthWhenClosed = 64f;
    public float animationDuration = 0.32f;
    public bool startClosed = false;
    public bool interactableWhileAnimating = false;

    // state
    private Vector2 shownAnchoredPos;
    private Vector2 hiddenAnchoredPos;
    private bool isOpen = true;
    private Coroutine animCoroutine;
    private Graphic[] panelGraphics;

    void Reset()
    {
        panelRect = GetComponent<RectTransform>();
    }

    void Awake()
    {
        if (panelRect == null) panelRect = GetComponent<RectTransform>();
        if (toggleButton != null)
        {
            toggleButton.onClick.RemoveListener(Toggle);
            toggleButton.onClick.AddListener(Toggle);
        }
        panelGraphics = GetComponentsInChildren<Graphic>(true);
    }

    void Start()
    {
        // compute positions and initial state
        ComputePositions();

        isOpen = !startClosed;
        if (startClosed)
            SetAnchoredPosition(hiddenAnchoredPos);
        else
            SetAnchoredPosition(shownAnchoredPos);

        UpdateToggleLabel(); // set initial symbol
    }

    // Call this if panel size / canvas changes at runtime (e.g. resolution change)
    public void ComputePositions()
    {
        if (panelRect == null) panelRect = GetComponent<RectTransform>();

        float panelWidth = panelRect.rect.width;
        float scale = 1f;
        if (parentCanvas != null)
            scale = parentCanvas.scaleFactor;

        float hiddenOffset = -(panelWidth - visibleWidthWhenClosed);

        shownAnchoredPos = panelRect.anchoredPosition;
        hiddenAnchoredPos = new Vector2(hiddenOffset, shownAnchoredPos.y);
    }

    // Toggle API
    public void Toggle()
    {
        if (panelRect == null) return;
        if (animCoroutine != null) return; // prevent re-entrance

        if (isOpen) Hide();
        else Show();
    }

    public void Show()
    {
        if (animCoroutine != null) StopCoroutine(animCoroutine);
        animCoroutine = StartCoroutine(AnimateTo(shownAnchoredPos));
        isOpen = true;
        UpdateToggleLabel();
        if (toggleButton != null) toggleButton.interactable = false;
    }

    public void Hide()
    {
        if (animCoroutine != null) StopCoroutine(animCoroutine);
        animCoroutine = StartCoroutine(AnimateTo(hiddenAnchoredPos));
        isOpen = false;
        UpdateToggleLabel();
        if (toggleButton != null) toggleButton.interactable = false;
    }

    private IEnumerator AnimateTo(Vector2 target)
    {
        Vector2 start = panelRect.anchoredPosition;
        float t = 0f;

        if (!interactableWhileAnimating)
            SetGraphicsRaycast(false);

        while (t < animationDuration)
        {
            t += Time.unscaledDeltaTime; // so animation works even when game paused
            float k = Mathf.SmoothStep(0f, 1f, t / animationDuration);
            panelRect.anchoredPosition = Vector2.Lerp(start, target, k);
            yield return null;
        }

        panelRect.anchoredPosition = target;

        if (!interactableWhileAnimating)
            SetGraphicsRaycast(true);

        if (toggleButton != null)
            toggleButton.interactable = true;

        animCoroutine = null;
    }

    private void SetAnchoredPosition(Vector2 pos)
    {
        if (panelRect != null) panelRect.anchoredPosition = pos;
    }

    private void SetGraphicsRaycast(bool enabled)
    {
        if (panelGraphics == null) panelGraphics = GetComponentsInChildren<Graphic>(true);
        foreach (var g in panelGraphics)
        {
            if (g == null) continue;
            if (toggleButton != null && g.gameObject == toggleButton.gameObject) continue;
            g.raycastTarget = enabled;
        }
    }

    // Update the toggle button label based on isOpen
    private void UpdateToggleLabel()
    {
        string symbol = isOpen ? openSymbol : closedSymbol;

        if (toggleButtonTMPText != null)
        {
            toggleButtonTMPText.text = symbol;
            return;
        }

        if (toggleButtonUIText != null)
        {
            toggleButtonUIText.text = symbol;
            return;
        }

        // Try to auto-find text component under the toggleButton if fields not assigned
        if (toggleButton != null)
        {
            var tmp = toggleButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null)
            {
                tmp.text = symbol;
                return;
            }

            var txt = toggleButton.GetComponentInChildren<Text>(true);
            if (txt != null)
            {
                txt.text = symbol;
                return;
            }
        }
    }

    public bool IsOpen() => isOpen;
}