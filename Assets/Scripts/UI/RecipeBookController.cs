using System;
using System.Collections.Generic;
using System.Linq;
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
    public enum ViewMode { Recipes, Profiles }

    [Header("UI References (shared controller)")]
    public GameObject panelRecipeBook;    // root panel (enable/disable)
    public Button openButton;             // external open button (optional)
    public Button closeButton;            // inside-panel close
    public Image pageImagePreview;        // large preview image (optional)
    [Tooltip("Fixed image slots (expected up to 4). Assign the 4 Image UI slots here in order. (USED FOR RECIPES)")]
    public List<Image> imageSlots = new List<Image>(); // fixed slots for recipes
    public TextMeshProUGUI pageTitle;     // title text (RECIPE title)
    public TextMeshProUGUI pageBody;      // body text (RECIPE body)
    public TextMeshProUGUI pageCounter;   // optional "1 / N" counter
    public Button prevButton;             // previous page
    public Button nextButton;             // next page

    [Header("Pages (editable in Inspector)")]
    public List<RecipeBookPage> pages = new List<RecipeBookPage>();

    [Header("Profiles (customer guide)")]
    [Tooltip("Assign CustomerProfile assets here (optional).")]
    public List<CustomerProfile> customerProfiles = new List<CustomerProfile>();

    [Header("Tab Buttons (optional)")]
    public Button recipeTabButton;
    public Button profileTabButton;

    [Header("Profile -> UI Mapping (assign targets in Inspector)")]
    [Tooltip("Image target for portrait. If left null portrait won't be set/shown.")]
    public Image profilePortraitTarget;
    [Tooltip("Large background image target (optional).")]
    public Image profileBackgroundImageTarget;
    [Tooltip("Text target for title (profile name). If null the recipe title will be kept unchanged.")]
    public TextMeshProUGUI profileTitleTarget;
    [Tooltip("Text target for background/bio narrative. If null the recipe body will be kept unchanged.")]
    public TextMeshProUGUI profileBackgroundTextTarget;
    [Tooltip("Text target to show preferred recipes list (comma separated). If null this won't be shown.")]
    public TextMeshProUGUI profilePreferredRecipesTarget;

    [Header("Profile image slots (ONLY used for Profiles if assigned)")]
    [Tooltip("If you want profiles to populate dedicated image slots, assign them here. Leave empty to avoid modifying recipe image slots.")]
    public List<Image> profileImageSlots = new List<Image>();

    [Header("Canvas group roots (assign your 2 panels here)")]
    [Tooltip("Either assign a CanvasGroup directly OR assign the GameObject root (recipePanelRootObj) below. If you assign root GameObject, a CanvasGroup will be created automatically.")]
    public CanvasGroup recipeCanvasGroup;
    [Tooltip("Either assign a CanvasGroup directly OR assign the GameObject root (profilePanelRootObj) below. If you assign root GameObject, a CanvasGroup will be created automatically.")]
    public CanvasGroup profileCanvasGroup;

    [Header("Optional: assign GameObject roots (if you prefer to assign GameObject instead of CanvasGroup)")]
    [Tooltip("If you assign this GameObject and recipeCanvasGroup is empty, a CanvasGroup will be created/used on this GameObject.")]
    public GameObject recipePanelRootObj;
    [Tooltip("If you assign this GameObject and profileCanvasGroup is empty, a CanvasGroup will be created/used on this GameObject.")]
    public GameObject profilePanelRootObj;

    [Header("Options")]
    public bool startClosed = true;       // panel starts closed
    public bool loopPages = false;        // next after last -> first
    public bool pauseOnOpen = false;      // set Time.timeScale = 0 on open
    public bool enablePreviewOnClick = true;
    public Sprite emptySlotPlaceholder;

    [Header("Pause: Components to disable while recipe book open")]
    public List<Behaviour> behavioursToDisableOnOpen = new List<Behaviour>();

    // runtime indices per mode
    private int recipeIndex = 0;
    private int profileIndex = 0;

    // runtime
    private int currentIndex = 0; // mirrors whichever mode is active (kept for compatibility)
    private float prevTimeScale = 1f;
    private Dictionary<Behaviour, bool> behaviourPrevState = new Dictionary<Behaviour, bool>();
    private ViewMode currentViewMode = ViewMode.Recipes;

    private void Awake()
    {
        // If user assigned GameObject roots instead of CanvasGroup, ensure we have CanvasGroup components.
        EnsureCanvasGroupFromRoot(ref recipeCanvasGroup, recipePanelRootObj, "RecipePanelRoot");
        EnsureCanvasGroupFromRoot(ref profileCanvasGroup, profilePanelRootObj, "ProfilePanelRoot");

        if (openButton != null) openButton.onClick.AddListener(Open);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (prevButton != null) prevButton.onClick.AddListener(PrevPage);
        if (nextButton != null) nextButton.onClick.AddListener(NextPage);

        if (recipeTabButton != null) recipeTabButton.onClick.AddListener(() => SetViewMode(ViewMode.Recipes));
        if (profileTabButton != null) profileTabButton.onClick.AddListener(() => SetViewMode(ViewMode.Profiles));

        if (panelRecipeBook != null) panelRecipeBook.SetActive(!startClosed);

        if (openButton != null && panelRecipeBook != null)
        {
            bool panelIsOpen = panelRecipeBook.activeInHierarchy;
            if (openButton != closeButton)
                openButton.interactable = !panelIsOpen;
        }
    }

    // Helper: if cgRef null but rootObj provided, get/add a CanvasGroup on the rootObj
    private void EnsureCanvasGroupFromRoot(ref CanvasGroup cgRef, GameObject rootObj, string debugName)
    {
        if (cgRef != null) return;
        if (rootObj == null) return;

        cgRef = rootObj.GetComponent<CanvasGroup>();
        if (cgRef == null)
        {
            cgRef = rootObj.AddComponent<CanvasGroup>();
#if UNITY_EDITOR
            UnityEditor.Undo.RegisterCreatedObjectUndo(cgRef, "Add CanvasGroup for " + debugName);
#endif
        }
    }

    private void Start()
    {
        // initialize indices (clamped)
        recipeIndex = Mathf.Clamp(recipeIndex, 0, Math.Max(0, pages != null ? pages.Count - 1 : 0));
        profileIndex = Mathf.Clamp(profileIndex, 0, Math.Max(0, customerProfiles != null ? customerProfiles.Count - 1 : 0));

        // Default: try show recipes if we have pages, otherwise profiles if available
        if (pages == null || pages.Count == 0)
        {
            if (customerProfiles != null && customerProfiles.Count > 0)
            {
                currentViewMode = ViewMode.Profiles;
                SetCanvasActive(recipeCanvasGroup, false);
                SetCanvasActive(profileCanvasGroup, true);
                profileIndex = Mathf.Clamp(profileIndex, 0, customerProfiles.Count - 1);
                ShowProfilePage(profileIndex);
            }
            else
            {
                // nothing assigned
                SetCanvasActive(recipeCanvasGroup, true);
                SetCanvasActive(profileCanvasGroup, false);
                UpdateEmptyView();
            }
        }
        else
        {
            currentViewMode = ViewMode.Recipes;
            SetCanvasActive(recipeCanvasGroup, true);
            SetCanvasActive(profileCanvasGroup, false);
            recipeIndex = Mathf.Clamp(recipeIndex, 0, pages.Count - 1);
            ShowPage(recipeIndex);
        }
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

    // CanvasGroup helper: show/hide panel without triggering OnEnable/Start logic
    private void SetCanvasActive(CanvasGroup cg, bool visible)
    {
        if (cg == null)
        {
            // If no CanvasGroup available, try toggle root GameObject instead (best-effort fallback)
            if (cg == recipeCanvasGroup && recipePanelRootObj != null) recipePanelRootObj.SetActive(visible);
            else if (cg == profileCanvasGroup && profilePanelRootObj != null) profilePanelRootObj.SetActive(visible);
            return;
        }
        cg.alpha = visible ? 1f : 0f;
        cg.interactable = visible;
        cg.blocksRaycasts = visible;
    }

    public void Open()
    {
        if (panelRecipeBook == null) return;
        if (panelRecipeBook.activeInHierarchy)
        {
            if (openButton != null && openButton.interactable) openButton.interactable = false;
            return;
        }

        panelRecipeBook.SetActive(true);

        // Ensure current canvas shown
        if (currentViewMode == ViewMode.Recipes)
        {
            SetCanvasActive(recipeCanvasGroup, true);
            SetCanvasActive(profileCanvasGroup, false);
            ShowPage(recipeIndex);
        }
        else
        {
            SetCanvasActive(recipeCanvasGroup, false);
            SetCanvasActive(profileCanvasGroup, true);
            ClearFixedImageSlots(imageSlots);
            if (pageImagePreview != null) pageImagePreview.sprite = null;
            ShowProfilePage(profileIndex);
        }

        if (openButton != null && openButton != closeButton) openButton.interactable = false;

        if (pauseOnOpen)
        {
            prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            DisableBehavioursForPause();
        }
    }

    public void Close()
    {
        if (panelRecipeBook == null) return;
        if (!panelRecipeBook.activeInHierarchy) return;

        panelRecipeBook.SetActive(false);

        if (openButton != null && openButton != closeButton) openButton.interactable = true;

        if (pauseOnOpen)
        {
            RestoreBehavioursAfterPause();
            Time.timeScale = prevTimeScale;
        }
    }

    // Switch mode: saves current mode index, restores other mode index, toggles canvas groups
    public void SetViewMode(ViewMode mode)
    {
        if (currentViewMode == mode) return;

        // save current index into correct slot
        if (currentViewMode == ViewMode.Recipes)
            recipeIndex = Mathf.Clamp(currentIndex, 0, Math.Max(0, pages != null ? pages.Count - 1 : 0));
        else
            profileIndex = Mathf.Clamp(currentIndex, 0, Math.Max(0, customerProfiles != null ? customerProfiles.Count - 1 : 0));

        currentViewMode = mode;

        if (currentViewMode == ViewMode.Recipes)
        {
            // switch to recipes
            SetCanvasActive(recipeCanvasGroup, true);
            SetCanvasActive(profileCanvasGroup, false);

            // clear profile-only images
            ClearFixedImageSlots(profileImageSlots);

            // restore recipe index & show
            recipeIndex = Mathf.Clamp(recipeIndex, 0, Math.Max(0, pages != null ? pages.Count - 1 : 0));
            ShowPage(recipeIndex);
        }
        else
        {
            // switch to profiles
            SetCanvasActive(recipeCanvasGroup, false);
            SetCanvasActive(profileCanvasGroup, true);

            // clear recipe images so no leftover
            ClearFixedImageSlots(imageSlots);
            if (pageImagePreview != null) pageImagePreview.sprite = null;

            profileIndex = Mathf.Clamp(profileIndex, 0, Math.Max(0, customerProfiles != null ? customerProfiles.Count - 1 : 0));
            ShowProfilePage(profileIndex);
        }

        if (recipeTabButton != null) recipeTabButton.interactable = (currentViewMode != ViewMode.Recipes);
        if (profileTabButton != null) profileTabButton.interactable = (currentViewMode != ViewMode.Profiles);
    }

    // ----- Recipes -----
    public void ShowPage(int index)
    {
        if (currentViewMode != ViewMode.Recipes)
        {
            ShowProfilePage(index);
            return;
        }

        if (pages == null || pages.Count == 0) { UpdateEmptyView(); return; }
        if (index < 0 || index >= pages.Count) { Debug.LogWarning("[RecipeBookController] ShowPage index out of range: " + index); return; }

        // clear any profile images to avoid overlap
        ClearFixedImageSlots(profileImageSlots);

        currentIndex = index;
        recipeIndex = index;
        var p = pages[currentIndex];

        if (pageTitle != null) pageTitle.text = string.IsNullOrEmpty(p.pageTitle) ? "(No title)" : p.pageTitle;
        if (pageBody != null) pageBody.text = string.IsNullOrEmpty(p.pageBody) ? "" : p.pageBody;
        if (pageCounter != null) pageCounter.text = $"{currentIndex + 1} / {Math.Max(1, pages.Count)}";

        if (prevButton != null) prevButton.interactable = loopPages ? true : (currentIndex > 0);
        if (nextButton != null) nextButton.interactable = loopPages ? true : (currentIndex < pages.Count - 1);

        PopulateFixedImageSlots(p.pageImages, imageSlots);
    }

    public void NextPage()
    {
        if (currentViewMode == ViewMode.Recipes)
        {
            if (pages == null || pages.Count == 0) return;
            int next = recipeIndex + 1;
            if (next >= pages.Count) next = loopPages ? 0 : pages.Count - 1;
            ShowPage(next);
        }
        else
        {
            if (customerProfiles == null || customerProfiles.Count == 0) return;
            int next = profileIndex + 1;
            if (next >= customerProfiles.Count) next = loopPages ? 0 : customerProfiles.Count - 1;
            ShowProfilePage(next);
        }
    }

    public void PrevPage()
    {
        if (currentViewMode == ViewMode.Recipes)
        {
            if (pages == null || pages.Count == 0) return;
            int prev = recipeIndex - 1;
            if (prev < 0) prev = loopPages ? pages.Count - 1 : 0;
            ShowPage(prev);
        }
        else
        {
            if (customerProfiles == null || customerProfiles.Count == 0) return;
            int prev = profileIndex - 1;
            if (prev < 0) prev = loopPages ? customerProfiles.Count - 1 : 0;
            ShowProfilePage(prev);
        }
    }

    private void UpdateEmptyView()
    {
        if (currentViewMode == ViewMode.Recipes)
        {
            if (pageTitle != null) pageTitle.text = "(No pages)";
            if (pageBody != null) pageBody.text = "Add pages in the Inspector to populate this book.";
            if (pageCounter != null) pageCounter.text = $"0 / 0";
            ClearFixedImageSlots(imageSlots);
            if (pageImagePreview != null) pageImagePreview.sprite = null;
        }
        else
        {
            // Do not overwrite recipe title/body unless profile targets are explicitly assigned.
            if (profileTitleTarget != null) profileTitleTarget.text = "(No profiles)";
            if (profileBackgroundTextTarget != null) profileBackgroundTextTarget.text = "No customer profiles assigned.";
            if (pageCounter != null) pageCounter.text = $"0 / 0";

            ClearFixedImageSlots(profileImageSlots);
            if (pageImagePreview != null) pageImagePreview.sprite = null;
        }

        if (prevButton != null) prevButton.interactable = false;
        if (nextButton != null) nextButton.interactable = false;
    }

    private void PopulateFixedImageSlots(List<Sprite> sprites, List<Image> targetSlots)
    {
        if (targetSlots == null || targetSlots.Count == 0)
        {
            if (currentViewMode == ViewMode.Recipes)
            {
                if (pageImagePreview != null && sprites != null && sprites.Count > 0)
                    pageImagePreview.sprite = sprites[0];
            }
            return;
        }

        ClearFixedImageSlots(targetSlots);

        int slotCount = targetSlots.Count;
        for (int i = 0; i < slotCount; i++)
        {
            var slot = targetSlots[i];
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
                slot.sprite = null;
                slot.color = Color.clear;
                slot.gameObject.SetActive(true);
            }

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

        Sprite firstPreview = null;
        if (sprites != null && sprites.Count > 0) firstPreview = sprites[0];
        else if (emptySlotPlaceholder != null) firstPreview = emptySlotPlaceholder;

        if (pageImagePreview != null && currentViewMode == ViewMode.Recipes) pageImagePreview.sprite = firstPreview;
    }

    private void ClearFixedImageSlots(List<Image> targetSlots)
    {
        if (targetSlots == null) return;
        foreach (var slot in targetSlots)
        {
            if (slot == null) continue;
            slot.sprite = null;
            slot.color = Color.clear;
            var btn = slot.GetComponent<Button>();
            if (btn != null) btn.onClick.RemoveAllListeners();
        }

        if (targetSlots == imageSlots && pageImagePreview != null) pageImagePreview.sprite = null;
    }

    private void ShowProfilePage(int index)
    {
        currentViewMode = ViewMode.Profiles;

        if (customerProfiles == null || customerProfiles.Count == 0)
        {
            UpdateEmptyView();
            return;
        }

        if (index < 0 || index >= customerProfiles.Count)
        {
            Debug.LogWarning("[RecipeBookController] ShowProfilePage index out of range: " + index);
            return;
        }

        // Clear recipe images so they won't remain visible when showing profiles
        ClearFixedImageSlots(imageSlots);
        if (pageImagePreview != null) pageImagePreview.sprite = null;

        currentIndex = index;
        profileIndex = index;
        var profile = customerProfiles[currentIndex];

        // Only set profileTitleTarget if explicitly assigned
        if (profileTitleTarget != null)
        {
            if (!string.IsNullOrEmpty(profile.profileName))
            {
                profileTitleTarget.gameObject.SetActive(true);
                profileTitleTarget.text = profile.profileName;
            }
            else profileTitleTarget.gameObject.SetActive(false);
        }

        // Only set profileBackgroundTextTarget if explicitly assigned
        if (profileBackgroundTextTarget != null)
        {
            if (!string.IsNullOrEmpty(profile.background))
            {
                profileBackgroundTextTarget.gameObject.SetActive(true);
                profileBackgroundTextTarget.text = profile.background;
            }
            else profileBackgroundTextTarget.gameObject.SetActive(false);
        }

        // Only set preferred recipes text if explicitly assigned
        if (profilePreferredRecipesTarget != null)
        {
            if (profile.preferredRecipeNames != null && profile.preferredRecipeNames.Count > 0)
            {
                profilePreferredRecipesTarget.gameObject.SetActive(true);
                profilePreferredRecipesTarget.text = "Likes: " + string.Join(", ", profile.preferredRecipeNames);
            }
            else profilePreferredRecipesTarget.gameObject.SetActive(false);
        }

        // Portrait mapping (image) — only change if portrait target(s) assigned
        bool portraitHandled = false;
        if (profilePortraitTarget != null)
        {
            if (profile.portrait != null)
            {
                profilePortraitTarget.gameObject.SetActive(true);
                profilePortraitTarget.sprite = profile.portrait;
                profilePortraitTarget.color = Color.white;
                portraitHandled = true;
            }
            else profilePortraitTarget.gameObject.SetActive(false);
        }

        if (!portraitHandled)
        {
            if (profileImageSlots != null && profileImageSlots.Count > 0)
            {
                if (profile.portrait != null)
                    PopulateFixedImageSlots(new List<Sprite> { profile.portrait }, profileImageSlots);
                else
                    ClearFixedImageSlots(profileImageSlots);
            }
            // if no profile image targets assigned, do NOT touch recipe image slots
        }

        if (profileBackgroundImageTarget != null)
        {
            profileBackgroundImageTarget.gameObject.SetActive(false);
        }

        if (pageCounter != null) pageCounter.text = $"{currentIndex + 1} / {Math.Max(1, customerProfiles.Count)}";

        if (prevButton != null) prevButton.interactable = loopPages ? true : (currentIndex > 0);
        if (nextButton != null) nextButton.interactable = loopPages ? true : (currentIndex < customerProfiles.Count - 1);
    }

    private string BuildProfileSummary(CustomerProfile profile)
    {
        string body = "";
        if (!string.IsNullOrEmpty(profile.background))
        {
            body += profile.background + "\n\n";
        }

        if (profile.preferredRecipeNames != null && profile.preferredRecipeNames.Count > 0)
        {
            body += "Preferred recipes: " + string.Join(", ", profile.preferredRecipeNames) + "\n";
        }
        if (profile.curhatStories != null && profile.curhatStories.Count > 0)
        {
            body += $"Curhat stories: {profile.curhatStories.Count}\n";
        }
        if (profile.orderStories != null && profile.orderStories.Count > 0)
        {
            body += $"Order variants: {profile.orderStories.Count}\n";
        }
        body += $"\nMax fails: {profile.maxFails}\n";
        body += $"Satisfy: {profile.pointsOnSatisfy}  Neutral: {profile.pointsOnNeutral}  Angry HP loss: {profile.hpLossOnAngry}\n";

        return body.Trim();
    }

    private void DisableBehavioursForPause()
    {
        behaviourPrevState.Clear();

        if (behavioursToDisableOnOpen == null || behavioursToDisableOnOpen.Count == 0)
        {
            var found = FindObjectsOfType<Behaviour>();
            foreach (var b in found)
            {
                if (b == null) continue;
                if (b.GetType().Name == "DragItem")
                {
                    behaviourPrevState[b] = b.enabled;
                    b.enabled = false;
                }
            }
        }
        else
        {
            foreach (var b in behavioursToDisableOnOpen)
            {
                if (b == null) continue;
                behaviourPrevState[b] = b.enabled;
                b.enabled = false;
            }
        }
    }

    private void RestoreBehavioursAfterPause()
    {
        foreach (var kv in behaviourPrevState)
        {
            var b = kv.Key;
            if (b == null) continue;
            try { b.enabled = kv.Value; } catch { }
        }
        behaviourPrevState.Clear();
    }

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
    private void DEBUG_REMOVE_CURRENT()
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