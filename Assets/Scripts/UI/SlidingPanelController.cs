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
        // compute positions in anchored space
        ComputePositions();

        // initialize state
        isOpen = !startClosed;
        if (startClosed)
            SetAnchoredPosition(hiddenAnchoredPos);
        else
            SetAnchoredPosition(shownAnchoredPos);
    }

    // Call this if panel size / canvas changes at runtime (e.g. resolution change)
    public void ComputePositions()
    {
        // Ensure panelRect set
        if (panelRect == null) panelRect = GetComponent<RectTransform>();

        // Get width in pixels relative to canvas scale
        float panelWidth = panelRect.rect.width;
        float scale = 1f;
        if (parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            // approximate scale for camera/overlay variants
            scale = parentCanvas.scaleFactor;
        }
        else if (parentCanvas != null)
        {
            scale = parentCanvas.scaleFactor;
        }

        // hiddenX shifts panel left so that only visibleWidthWhenClosed remains visible
        // anchoredPosition.x is local to anchor; we use negative X to move left
        float hiddenOffset = -(panelWidth - visibleWidthWhenClosed);

        // Keep the same Y as current anchoredPosition
        shownAnchoredPos = panelRect.anchoredPosition;
        hiddenAnchoredPos = new Vector2(hiddenOffset, shownAnchoredPos.y);
    }

    // Toggle API
    public void Toggle()
    {
        if (panelRect == null) return;
        if (animCoroutine != null) return; // prevent re-entrance by default

        if (isOpen) Hide();
        else Show();
    }

    public void Show()
    {
        if (animCoroutine != null) StopCoroutine(animCoroutine);
        animCoroutine = StartCoroutine(AnimateTo(shownAnchoredPos));
        isOpen = true;
        if (toggleButton != null) toggleButton.interactable = false; // optional: disable during animation
    }

    public void Hide()
    {
        if (animCoroutine != null) StopCoroutine(animCoroutine);
        animCoroutine = StartCoroutine(AnimateTo(hiddenAnchoredPos));
        isOpen = false;
        if (toggleButton != null) toggleButton.interactable = false;
    }

    private IEnumerator AnimateTo(Vector2 target)
    {
        Vector2 start = panelRect.anchoredPosition;
        float t = 0f;

        // optionally disable raycast graphics while animating
        if (!interactableWhileAnimating)
            SetGraphicsRaycast(false);

        while (t < animationDuration)
        {
            t += Time.unscaledDeltaTime; // use unscaled so works when Time.timeScale == 0 (pause)
            float k = Mathf.SmoothStep(0f, 1f, t / animationDuration);
            panelRect.anchoredPosition = Vector2.Lerp(start, target, k);
            yield return null;
        }

        panelRect.anchoredPosition = target;

        if (!interactableWhileAnimating)
            SetGraphicsRaycast(true);

        // re-enable toggleButton after animation (so player cannot spam)
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
        // enable/disable raycastTarget on all Graphic children so UI interactions stop while animating
        if (panelGraphics == null) panelGraphics = GetComponentsInChildren<Graphic>(true);
        foreach (var g in panelGraphics)
        {
            if (g == null) continue;
            // do not touch the toggleButton itself (it must remain interactable)
            if (toggleButton != null && g.gameObject == toggleButton.gameObject) continue;
            g.raycastTarget = enabled;
        }
    }

    // Optional helper for editor / external calls
    public bool IsOpen() => isOpen;
}