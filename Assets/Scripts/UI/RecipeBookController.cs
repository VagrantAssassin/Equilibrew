using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[Serializable]
public class RecipeBookPage
{
    public string pageTitle;
    [TextArea(3, 10)]
    public string pageBody;
    // multiple images per page (you can add 0..4 sprites here)
    public List<Sprite> pageImages = new List<Sprite>();
}

public class RecipeBookController : MonoBehaviour
{
    [Header("UI References")]
    public GameObject panelRecipeBook;    // root panel (enable/disable)
    public Button openButton;             // external open button (optional)
    public Button closeButton;            // inside-panel close
    public Image pageImagePreview;        // large preview image (optional)
    [Tooltip("Fixed image slots (expected up to 4). Assign the 4 Image UI slots here in order.")]
    public List<Image> imageSlots = new List<Image>(); // fixed slots (prefer exactly 4)
    public TextMeshProUGUI pageTitle;     // title text
    public TextMeshProUGUI pageBody;      // body text (multi-line)
    public TextMeshProUGUI pageCounter;   // optional "1 / N" counter
    public Button prevButton;             // previous page
    public Button nextButton;             // next page

    [Header("Pages (editable in Inspector)")]
    public List<RecipeBookPage> pages = new List<RecipeBookPage>();

    [Header("Options")]
    public bool startClosed = true;       // panel starts closed
    public bool loopPages = false;        // next after last -> first
    public bool pauseOnOpen = false;      // set Time.timeScale = 0 on open
    [Tooltip("If true, clicking an image slot will set it to the large preview image (pageImagePreview).")]
    public bool enablePreviewOnClick = true;
    [Tooltip("Optional placeholder sprite to show in empty slots. If null, empty slots will be hidden.")]
    public Sprite emptySlotPlaceholder;

    private int currentIndex = 0;
    private float prevTimeScale = 1f;

    private void Awake()
    {
        if (openButton != null) openButton.onClick.AddListener(Open);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (prevButton != null) prevButton.onClick.AddListener(PrevPage);
        if (nextButton != null) nextButton.onClick.AddListener(NextPage);

        if (panelRecipeBook != null) panelRecipeBook.SetActive(!startClosed);
    }

    private void Start()
    {
        if (pages == null || pages.Count == 0)
            UpdateEmptyView();
        else
        {
            currentIndex = Mathf.Clamp(currentIndex, 0, pages.Count - 1);
            ShowPage(currentIndex);
        }

        // Warn in editor if slots not set to expected count (optional)
#if UNITY_EDITOR
        if (imageSlots == null || imageSlots.Count == 0)
            Debug.LogWarning("[RecipeBookController] imageSlots not assigned. Add up to 4 Image UI slots in Inspector.");
#endif
    }

    private void Update()
    {
        if (panelRecipeBook != null && panelRecipeBook.activeInHierarchy)
        {
            if (Input.GetKeyDown(KeyCode.RightArrow)) NextPage();
            if (Input.GetKeyDown(KeyCode.LeftArrow)) PrevPage();
            if (Input.GetKeyDown(KeyCode.Escape)) Close();
        }
    }

    public void Open()
    {
        if (panelRecipeBook == null) return;
        panelRecipeBook.SetActive(true);

        if (pages != null && pages.Count > 0) ShowPage(currentIndex);
        else UpdateEmptyView();

        if (pauseOnOpen)
        {
            prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }
    }

    public void Close()
    {
        if (panelRecipeBook == null) return;
        panelRecipeBook.SetActive(false);
        if (pauseOnOpen) Time.timeScale = prevTimeScale;
    }

    public void ShowPage(int index)
    {
        if (pages == null || pages.Count == 0)
        {
            UpdateEmptyView();
            return;
        }

        if (index < 0 || index >= pages.Count)
        {
            Debug.LogWarning("[RecipeBookController] ShowPage index out of range: " + index);
            return;
        }

        currentIndex = index;
        var p = pages[currentIndex];

        if (pageTitle != null) pageTitle.text = string.IsNullOrEmpty(p.pageTitle) ? "(No title)" : p.pageTitle;
        if (pageBody != null) pageBody.text = string.IsNullOrEmpty(p.pageBody) ? "" : p.pageBody;
        if (pageCounter != null) pageCounter.text = $"{currentIndex + 1} / {Math.Max(1, pages.Count)}";

        if (prevButton != null) prevButton.interactable = loopPages ? true : (currentIndex > 0);
        if (nextButton != null) nextButton.interactable = loopPages ? true : (currentIndex < pages.Count - 1);

        PopulateFixedImageSlots(p.pageImages);
    }

    public void NextPage()
    {
        if (pages == null || pages.Count == 0) return;
        int next = currentIndex + 1;
        if (next >= pages.Count) next = loopPages ? 0 : pages.Count - 1;
        ShowPage(next);
    }

    public void PrevPage()
    {
        if (pages == null || pages.Count == 0) return;
        int prev = currentIndex - 1;
        if (prev < 0) prev = loopPages ? pages.Count - 1 : 0;
        ShowPage(prev);
    }

    private void UpdateEmptyView()
    {
        if (pageTitle != null) pageTitle.text = "(No pages)";
        if (pageBody != null) pageBody.text = "Add pages in the Inspector to populate this book.";
        if (pageCounter != null) pageCounter.text = $"0 / 0";
        ClearFixedImageSlots();
        if (prevButton != null) prevButton.interactable = false;
        if (nextButton != null) nextButton.interactable = false;
        if (pageImagePreview != null) pageImagePreview.sprite = null;
    }

    // Map up to N sprites into the fixed imageSlots list.
    // If sprites.Count < imageSlots.Count, remaining slots will show emptySlotPlaceholder (if set) or be hidden.
    private void PopulateFixedImageSlots(List<Sprite> sprites)
    {
        // safety
        if (imageSlots == null || imageSlots.Count == 0)
        {
            // fallback: set preview to first sprite if any
            if (pageImagePreview != null && sprites != null && sprites.Count > 0)
                pageImagePreview.sprite = sprites[0];
            return;
        }

        ClearFixedImageSlots();

        int slotCount = imageSlots.Count;
        for (int i = 0; i < slotCount; i++)
        {
            var slot = imageSlots[i];
            if (slot == null) continue;

            Sprite sp = (sprites != null && i < sprites.Count) ? sprites[i] : null;

            if (sp != null)
            {
                slot.sprite = sp;
                slot.color = Color.white;
                slot.gameObject.SetActive(true);
            }
            else if (emptySlotPlaceholder != null)
            {
                slot.sprite = emptySlotPlaceholder;
                slot.color = Color.white;
                slot.gameObject.SetActive(true);
            }
            else
            {
                // hide empty slot if no placeholder
                slot.sprite = null;
                slot.color = Color.clear;
                slot.gameObject.SetActive(true); // keep active so layout stays consistent; transparent image
            }

            // wire click to preview if Button exists and preview enabled
            var btn = slot.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                if (enablePreviewOnClick)
                {
                    var captured = sp ?? emptySlotPlaceholder;
                    btn.onClick.AddListener(() =>
                    {
                        if (pageImagePreview != null) pageImagePreview.sprite = captured;
                    });
                }
            }
        }

        // set preview to first available sprite (prefer real sprite over placeholder)
        Sprite firstPreview = null;
        if (sprites != null && sprites.Count > 0) firstPreview = sprites[0];
        else if (emptySlotPlaceholder != null) firstPreview = emptySlotPlaceholder;

        if (pageImagePreview != null) pageImagePreview.sprite = firstPreview;
    }

    private void ClearFixedImageSlots()
    {
        if (imageSlots == null) return;
        foreach (var slot in imageSlots)
        {
            if (slot == null) continue;
            slot.sprite = null;
            slot.color = Color.clear;
            // preserve active state so layout doesn't jump; using transparent image keeps layout stable
            var btn = slot.GetComponent<Button>();
            if (btn != null) btn.onClick.RemoveAllListeners();
        }

        if (pageImagePreview != null) pageImagePreview.sprite = null;
    }

    // Inspector helpers
    [ContextMenu("DEBUG_AddEmptyPage")]
    private void DEBUG_AddEmptyPage()
    {
        pages.Add(new RecipeBookPage { pageTitle = "New Page", pageBody = "Edit this page in Inspector." });
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
        ShowPage(pages.Count - 1);
    }

    [ContextMenu("DEBUG_RemoveCurrentPage")]
    private void DEBUG_RemoveCurrentPage()
    {
        if (pages == null || pages.Count == 0) return;
        pages.RemoveAt(currentIndex);
        currentIndex = Mathf.Clamp(currentIndex, 0, Math.Max(0, pages.Count - 1));
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
        if (pages.Count == 0) UpdateEmptyView();
        else ShowPage(currentIndex);
    }
}