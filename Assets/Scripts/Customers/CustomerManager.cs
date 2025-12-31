using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// CustomerManager - full stable version (fade-out visible fix)
/// - Compatible with CustomerVisualController OR CustomerVisualFade (tries both)
/// - Picks index from preferredRecipeNames and plays orderStories[index] if present
/// - Plays curhat after correct serve, clears placeholder before playing Ink
/// - Non-blocking visual fade-out that runs while object is still active, then destroyed after duration
/// - Debug logs added to help trace flow
/// </summary>
public class CustomerManager : MonoBehaviour
{
    [Header("References (assign in Inspector)")]
    public RecipeValidator recipeValidator;
    public CupController cupController;
    public Transform spawnParent;
    public GameObject customerPrefab;

    [Header("Legacy small dialog prefab (optional)")]
    public RectTransform dialogAnchor;
    public GameObject dialogPrefab; // optional fallback: small text box with TMP inside

    [Header("Ink Dialog Controller (scene)")]
    public InkDialogController inkDialogController;

    [Header("Profiles (customers pool)")]
    public List<CustomerProfile> profiles = new List<CustomerProfile>();

    [Header("Daily & delays")]
    [Tooltip("Minimum customers per day when not using spawnAllPerDay")]
    public int minCustomersPerDay = 1;
    [Tooltip("Delay before next day starts (seconds)")]
    public float delayBeforeNextDay = 1f;
    [Tooltip("Delay between customers when advancing (seconds)")]
    public float delayBetweenCustomers = 1f;
    [Tooltip("If true, spawn all profiles for the day (ignores random count). Useful for testing.")]
    public bool spawnAllPerDay = false;
    [Tooltip("If true, when the day runs out StartNewDay is called immediately. Useful for testing.")]
    public bool autoRestartDay = true;

    [Header("Message durations")]
    public float failureMessageDuration = 1.5f;
    public float successMessageDuration = 1.0f;
    public float leaveMessageDuration = 1.5f;

    [Header("Visual")]
    [Tooltip("Default fade duration for customer visuals (seconds)")]
    public float customerFadeDuration = 0.25f;

    // runtime
    private List<CustomerProfile> todaysProfiles = new List<CustomerProfile>();
    private int todaysIndex = 0;
    private Customer currentCustomer = null;
    private CustomerProfile currentProfile = null;
    private GameObject activeDialogInstance = null;
    private Coroutine messageCoroutine = null;

    // current requested (chosen index into preferredRecipeNames & orderStories)
    private int currentRequestedIndex = -1;
    private string currentRequestedRecipeName = null;
    private TextAsset currentRequestedOrderStory = null;

    private enum ManagerState { Idle, Ordering, WaitingForServe, ShowingMessage, WaitingBetweenCustomers, DayEnding }
    private ManagerState state = ManagerState.Idle;

    // prevent race on serve
    private bool serveLocked = false;

    private void Start()
    {
        if (cupController != null)
        {
            cupController.OnServe -= OnServeReceived;
            cupController.OnServe += OnServeReceived;
        }
        else
        {
            Debug.LogWarning("[CustomerManager] cupController not assigned.");
        }

        StartNewDay();
    }

    private void OnDestroy()
    {
        if (cupController != null)
            cupController.OnServe -= OnServeReceived;
    }

    #region Daily flow
    private void StartNewDay()
    {
        if (messageCoroutine != null) { StopCoroutine(messageCoroutine); messageCoroutine = null; }

        state = ManagerState.Idle;
        Debug.Log($"[CustomerManager] StartNewDay: profiles={(profiles!=null?profiles.Count:0)} minCustomersPerDay={minCustomersPerDay} spawnAllPerDay={spawnAllPerDay} autoRestartDay={autoRestartDay}");

        if (profiles == null || profiles.Count == 0)
        {
            Debug.LogWarning("[CustomerManager] No customer profiles assigned.");
            todaysProfiles.Clear();
            todaysIndex = 0;
            return;
        }

        var pool = new List<CustomerProfile>(profiles);
        Shuffle(pool);

        int count;
        if (spawnAllPerDay)
        {
            count = pool.Count;
        }
        else
        {
            int minC = Mathf.Max(1, minCustomersPerDay);
            int maxC = Mathf.Max(minC, pool.Count);
            count = UnityEngine.Random.Range(minC, maxC + 1);
            count = Mathf.Clamp(count, 1, pool.Count);
        }

        todaysProfiles = pool.GetRange(0, count);
        todaysIndex = 0;

        Debug.Log($"[CustomerManager] Today will have {todaysProfiles.Count} customers.");
        SpawnNextFromToday();
    }

    private void Shuffle<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int j = UnityEngine.Random.Range(i, list.Count);
            T tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
    }

    private void SpawnNextFromToday()
    {
        Debug.Log($"[CustomerManager] SpawnNextFromToday called. state={state} todaysIndex={todaysIndex} todaysProfilesCount={(todaysProfiles!=null?todaysProfiles.Count:0)}");

        ClearDialogInstance();

        if (todaysProfiles == null || todaysIndex >= todaysProfiles.Count)
        {
            Debug.Log("[CustomerManager] No more customers today.");
            if (autoRestartDay)
            {
                Debug.Log("[CustomerManager] autoRestartDay=true -> Starting next day immediately.");
                StartNewDay();
            }
            else
            {
                Debug.Log("[CustomerManager] Scheduling NextDayDelayed.");
                StartCoroutine(NextDayDelayed());
            }
            return;
        }

        if (state != ManagerState.Idle)
        {
            Debug.Log("[CustomerManager] Spawn deferred because state != Idle. Scheduling spawn shortly.");
            StartCoroutine(SpawnNextWhenIdleCoroutine());
            return;
        }

        var profile = todaysProfiles[todaysIndex++];

        if (customerPrefab == null || spawnParent == null)
        {
            Debug.LogError("[CustomerManager] customerPrefab or spawnParent not assigned.");
            return;
        }

        GameObject go = Instantiate(customerPrefab, spawnParent);
        go.name = "Customer_" + profile.profileName;
        var cust = go.GetComponent<Customer>() ?? go.AddComponent<Customer>();

        // configure customer
        cust.maxFails = Mathf.Max(1, profile.maxFails);
        cust.failCount = 0;

        var img = go.GetComponentInChildren<UnityEngine.UI.Image>(true);
        if (img != null)
        {
            if (profile.portrait != null)
            {
                img.sprite = profile.portrait;
                img.color = Color.white;
            }
            else
            {
                img.sprite = null;
                img.color = new Color(1, 1, 1, 0f);
            }
        }

        // Visual fade-in if available (try both controller names)
        var visOld = go.GetComponent("CustomerVisualController");
        if (visOld != null)
        {
            var comp = go.GetComponent("CustomerVisualController");
            StartCoroutine(CallFadeInCoroutineDynamic(comp, customerFadeDuration));
        }
        else
        {
            var visNew = go.GetComponent("CustomerVisualFade");
            if (visNew != null)
            {
                var comp2 = go.GetComponent("CustomerVisualFade");
                StartCoroutine(CallFadeInCoroutineDynamic(comp2, customerFadeDuration));
            }
        }

        // Selection logic: pick index from preferredRecipeNames, then use orderStories[index]
        int prefCount = profile.preferredRecipeNames != null ? profile.preferredRecipeNames.Count : 0;
        int storyCount = profile.orderStories != null ? profile.orderStories.Count : 0;

        if (prefCount <= 0)
        {
            Debug.LogWarning("[CustomerManager] Profile has no preferredRecipeNames. Skipping profile: " + profile.profileName);
            Destroy(go);
            SpawnNextFromToday();
            return;
        }

        int idx = UnityEngine.Random.Range(0, prefCount);
        currentRequestedIndex = idx;
        currentRequestedRecipeName = profile.preferredRecipeNames[idx];

        if (idx < storyCount)
            currentRequestedOrderStory = profile.orderStories[idx];
        else
        {
            currentRequestedOrderStory = null;
            Debug.LogWarning($"[CustomerManager] Profile '{profile.profileName}' missing orderStories[{idx}]. Falling back to placeholder.");
        }

        currentProfile = profile;

        // Find recipe object by name to present placeholder and also for validation later
        Recipe requestedRecipe = null;
        if (!string.IsNullOrEmpty(currentRequestedRecipeName) && recipeValidator != null && recipeValidator.recipes != null)
        {
            requestedRecipe = recipeValidator.recipes.Find(r => string.Equals(r.recipeName, currentRequestedRecipeName, StringComparison.OrdinalIgnoreCase));
            if (requestedRecipe == null)
                Debug.LogWarning("[CustomerManager] Requested recipe '" + currentRequestedRecipeName + "' not found in recipeValidator.recipes.");
        }
        cust.SetRequest(requestedRecipe);
        currentCustomer = cust;

        Debug.Log($"[CustomerManager] Spawned '{profile.profileName}' idx={currentRequestedIndex} recipe='{currentRequestedRecipeName}' hasOrderStory={(currentRequestedOrderStory!=null)}");

        // Play ordering phase
        state = ManagerState.Ordering;
        if (inkDialogController != null && currentRequestedOrderStory != null)
        {
            // ensure placeholder cleared
            ClearDialogInstance();
            inkDialogController.PlayCurhat(currentRequestedOrderStory, (r, tags) =>
            {
                state = ManagerState.WaitingForServe;
                Debug.Log("[CustomerManager] Ordering story complete, now WaitingForServe.");
            });
        }
        else
        {
            // fallback placeholder dialog
            CreateDialogInstanceForRecipe(requestedRecipe);
            state = ManagerState.WaitingForServe;
            Debug.Log("[CustomerManager] Ordering fallback (placeholder) shown; WaitingForServe.");
        }
    }

    private IEnumerator SpawnNextWhenIdleCoroutine()
    {
        yield return new WaitForSecondsRealtime(0.05f);
        SpawnNextFromToday();
    }

    private IEnumerator NextDayDelayed()
    {
        Debug.Log($"[CustomerManager] NextDayDelayed: waiting {delayBeforeNextDay}s then StartNewDay.");
        yield return new WaitForSecondsRealtime(delayBeforeNextDay);
        StartNewDay();
    }
    #endregion

    #region Curhat handling (after successful serve)
    private IEnumerator RunCurhatIfAnyAndThenAdvance()
    {
        // small thank-you message via placeholder dialog if exists
        var txt = activeDialogInstance?.GetComponentInChildren<TextMeshProUGUI>(true);
        if (txt != null)
        {
            StartShowMessageAndThen(txt, "Terima kasih", successMessageDuration, null);
            yield return new WaitForSecondsRealtime(successMessageDuration);
        }

        // clear placeholder dialog before curhat
        ClearDialogInstance();

        // choose curhat story: prefer curhatStories, else fallback to orderStory
        TextAsset curhatToPlay = null;
        if (currentProfile != null && currentProfile.curhatStories != null && currentProfile.curhatStories.Count > 0)
            curhatToPlay = currentProfile.curhatStories[UnityEngine.Random.Range(0, currentProfile.curhatStories.Count)];
        else
            curhatToPlay = currentRequestedOrderStory;

        if (curhatToPlay != null && inkDialogController != null)
        {
            // wait if controller busy
            while (inkDialogController.IsPlaying)
                yield return null;

            bool done = false;
            DialogueReaction reaction = DialogueReaction.Neutral;
            List<string> tags = null;

            inkDialogController.PlayCurhat(curhatToPlay, (r, tgs) =>
            {
                reaction = r;
                tags = tgs;
                done = true;
            });

            yield return new WaitUntil(() => done);

            HandleCurhatReaction(currentCustomer, reaction, tags);
        }
        else
        {
            Debug.Log("[CustomerManager] No curhat story to play (null).");
        }

        yield return new WaitForSecondsRealtime(delayBetweenCustomers);
        AdvanceToNextCustomer();
    }

    private void HandleCurhatReaction(Customer cust, DialogueReaction reaction, List<string> tags)
    {
        if (cust == null) return;

        switch (reaction)
        {
            case DialogueReaction.Agree:
                Debug.Log("[CustomerManager] Curhat Reaction: AGREE for " + cust.name);
                break;
            case DialogueReaction.Neutral:
                Debug.Log("[CustomerManager] Curhat Reaction: NEUTRAL for " + cust.name);
                break;
            case DialogueReaction.Disagree:
                Debug.Log("[CustomerManager] Curhat Reaction: DISAGREE for " + cust.name);
                break;
        }

        if (tags != null && tags.Count > 0)
            Debug.Log("[CustomerManager] Curhat tags: " + string.Join(",", tags));
    }
    #endregion

    #region Dialog helper (legacy placeholder)
    private void CreateDialogInstanceForRecipe(Recipe recipe)
    {
        ClearDialogInstance();

        if (dialogPrefab == null)
        {
            Debug.LogWarning("[CustomerManager] dialogPrefab not assigned.");
            return;
        }

        if (dialogAnchor == null)
            activeDialogInstance = Instantiate(dialogPrefab);
        else
            activeDialogInstance = Instantiate(dialogPrefab, dialogAnchor, false);

        if (activeDialogInstance == null) return;

        var txt = activeDialogInstance.GetComponentInChildren<TextMeshProUGUI>(true);
        if (txt != null)
        {
            txt.text = recipe != null ? recipe.recipeName : (currentRequestedRecipeName ?? "Saya ingin sesuatu...");
            txt.gameObject.SetActive(!string.IsNullOrEmpty(txt.text));
        }
    }

    private void ClearDialogInstance()
    {
        if (activeDialogInstance != null)
        {
            Destroy(activeDialogInstance);
            activeDialogInstance = null;
        }

        if (messageCoroutine != null)
        {
            StopCoroutine(messageCoroutine);
            messageCoroutine = null;
        }

        if (state == ManagerState.ShowingMessage) state = ManagerState.Idle;
    }
    #endregion

    #region Serve handling
    private void OnServeReceived(Recipe served)
    {
        Debug.Log($"[CustomerManager] OnServeReceived. state={state} current={(currentCustomer!=null?currentCustomer.name:"null")} serveLocked={serveLocked}");

        if (serveLocked)
        {
            Debug.Log("[CustomerManager] Serve ignored - locked.");
            return;
        }

        if (state != ManagerState.WaitingForServe)
        {
            Debug.Log("[CustomerManager] Serve ignored - not ready for serve.");
            return;
        }
        if (currentCustomer == null)
        {
            Debug.Log("[CustomerManager] No active customer.");
            return;
        }

        serveLocked = true;
        StartCoroutine(ReleaseServeLockNextFrame());

        bool ok = (served != null && string.Equals(served.recipeName, currentRequestedRecipeName, StringComparison.OrdinalIgnoreCase));
        if (ok)
        {
            Debug.Log("[CustomerManager] Correct serve!");
            StartCoroutine(RunCurhatIfAnyAndThenAdvance());
            return;
        }

        // Wrong serve
        Debug.Log("[CustomerManager] Wrong serve. fail(before)=" + currentCustomer.failCount + ", max=" + currentCustomer.maxFails);
        bool reached = currentCustomer.RegisterFail();
        Debug.Log("[CustomerManager] fail(after)=" + currentCustomer.failCount + ", reachedMax=" + reached);

        var dialogText = activeDialogInstance?.GetComponentInChildren<TextMeshProUGUI>(true);
        if (dialogText != null)
        {
            if (reached)
            {
                StartShowMessageAndThen(dialogText, "Kamu gimana sih kerjanya, saya mau pergi saja", leaveMessageDuration, null);
                StartCoroutine(AdvanceAfterDelay(leaveMessageDuration));
            }
            else
            {
                StartShowMessageAndThen(dialogText, "Ini bukan pesanan saya", failureMessageDuration, () => RestoreWaitingForServeCoroutine());
            }
        }
        else
        {
            if (reached)
            {
                StartCoroutine(AdvanceAfterDelay(0f));
            }
            else
            {
                Debug.Log("[CustomerManager] Wrong serve recorded; customer remains (no dialog).");
                state = ManagerState.WaitingForServe;
            }
        }
    }

    private IEnumerator ReleaseServeLockNextFrame()
    {
        yield return null;
        serveLocked = false;
    }

    private IEnumerator AdvanceAfterDelay(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        AdvanceToNextCustomer();
    }
    #endregion

    #region Advance & messages
    public void AdvanceToNextCustomer()
    {
        if (messageCoroutine != null) { StopCoroutine(messageCoroutine); messageCoroutine = null; }
        messageCoroutine = StartCoroutine(AdvanceToNextCustomerCoroutine());
    }

    // Non-blocking fade-out: start fade coroutines but do NOT wait for them;
    // schedule destroy after fade duration so fade is visible.
    private IEnumerator AdvanceToNextCustomerCoroutine()
    {
        state = ManagerState.WaitingBetweenCustomers;
        Debug.Log("[CustomerManager] AdvanceToNextCustomerCoroutine started.");

        GameObject goToDestroy = null;

        if (currentCustomer != null)
        {
            goToDestroy = currentCustomer.gameObject;

            // Attempt fade-out on either visual controller name (start fading non-blocking)
            var visOld = currentCustomer.GetComponent("CustomerVisualController");
            if (visOld != null)
            {
                StartCoroutine(CallFadeOutCoroutineDynamic(visOld, customerFadeDuration));
                Debug.Log("[CustomerManager] Started non-blocking fadeOut via CustomerVisualController.");
            }
            else
            {
                var visNew = currentCustomer.GetComponent("CustomerVisualFade");
                if (visNew != null)
                {
                    StartCoroutine(CallFadeOutCoroutineDynamic(visNew, customerFadeDuration));
                    Debug.Log("[CustomerManager] Started non-blocking fadeOut via CustomerVisualFade.");
                }
            }

            // Optionally disable interaction components here (so player can't interact while fading)
            // e.g., disable colliders/buttons on the customer root if present
            var coll = goToDestroy.GetComponent<Collider>();
            if (coll != null) coll.enabled = false;
            var uic = goToDestroy.GetComponentInChildren<UnityEngine.UI.Button>(true);
            if (uic != null) uic.interactable = false;

            // schedule destroy after fade duration (plus small cushion)
            StartCoroutine(DestroyGameObjectAfterDelay(goToDestroy, customerFadeDuration + 0.05f));

            // clear references immediately (so gameplay can continue)
            currentCustomer = null;
            currentProfile = null;
            currentRequestedIndex = -1;
            currentRequestedRecipeName = null;
            currentRequestedOrderStory = null;
        }

        ClearDialogInstance();

        yield return new WaitForSecondsRealtime(delayBetweenCustomers);

        state = ManagerState.Idle;
        messageCoroutine = null;
        Debug.Log("[CustomerManager] AdvanceToNextCustomerCoroutine finished. Spawning next...");
        SpawnNextFromToday();
    }
    #endregion

    #region Helpers: ShowMessage and Restore
    private void StartShowMessageAndThen(TextMeshProUGUI txt, string message, float duration, Func<IEnumerator> followupFactory)
    {
        if (messageCoroutine != null) { StopCoroutine(messageCoroutine); messageCoroutine = null; }
        messageCoroutine = StartCoroutine(ShowMessageAndThenCoroutine(txt, message, duration, followupFactory));
    }

    private IEnumerator ShowMessageAndThenCoroutine(TextMeshProUGUI txt, string message, float duration, Func<IEnumerator> followupFactory)
    {
        state = ManagerState.ShowingMessage;
        string prev = txt.text;
        txt.text = message;

        yield return new WaitForSecondsRealtime(duration);

        if (followupFactory != null)
        {
            if (messageCoroutine != null)
            {
                try { StopCoroutine(messageCoroutine); } catch { }
                messageCoroutine = null;
            }
            messageCoroutine = StartCoroutine(followupFactory());
        }
        else
        {
            if (activeDialogInstance != null)
            {
                var existingTxt = activeDialogInstance.GetComponentInChildren<TextMeshProUGUI>(true);
                if (existingTxt != null)
                    existingTxt.text = currentRequestedRecipeName ?? prev;
            }

            state = ManagerState.Idle;
            messageCoroutine = null;
        }
    }

    private IEnumerator RestoreWaitingForServeCoroutine()
    {
        state = ManagerState.WaitingForServe;

        if (activeDialogInstance != null)
        {
            var txt = activeDialogInstance.GetComponentInChildren<TextMeshProUGUI>(true);
            if (txt != null)
            {
                txt.text = currentRequestedRecipeName ?? (currentCustomer != null && currentCustomer.requestedRecipe != null ? currentCustomer.requestedRecipe.recipeName : "");
            }
        }

        yield break;
    }
    #endregion

    #region Dynamic fade-in/out callers (reflection invocation)
    private IEnumerator CallFadeInCoroutineDynamic(object compObj, float duration)
    {
        if (compObj == null) yield break;
        var comp = compObj as MonoBehaviour;
        if (comp == null) yield break;

        var mi = comp.GetType().GetMethod("FadeInCoroutine", new Type[] { typeof(float) });
        if (mi != null)
        {
            var enumerator = mi.Invoke(comp, new object[] { duration }) as IEnumerator;
            if (enumerator != null)
            {
                yield return StartCoroutine(enumerator);
                yield break;
            }
        }

        mi = comp.GetType().GetMethod("FadeInCoroutine", Type.EmptyTypes);
        if (mi != null)
        {
            var enumerator = mi.Invoke(comp, null) as IEnumerator;
            if (enumerator != null)
            {
                yield return StartCoroutine(enumerator);
                yield break;
            }
        }

        yield break;
    }

    private IEnumerator CallFadeOutCoroutineDynamic(object compObj, float duration)
    {
        if (compObj == null) yield break;
        var comp = compObj as MonoBehaviour;
        if (comp == null) yield break;

        var mi = comp.GetType().GetMethod("FadeOutCoroutine", new Type[] { typeof(float) });
        if (mi != null)
        {
            var enumerator = mi.Invoke(comp, new object[] { duration }) as IEnumerator;
            if (enumerator != null)
            {
                yield return StartCoroutine(enumerator);
                yield break;
            }
        }

        mi = comp.GetType().GetMethod("FadeOutCoroutine", Type.EmptyTypes);
        if (mi != null)
        {
            var enumerator = mi.Invoke(comp, null) as IEnumerator;
            if (enumerator != null)
            {
                yield return StartCoroutine(enumerator);
                yield break;
            }
        }

        yield break;
    }
    #endregion

    #region Utility: delayed destroy
    private IEnumerator DestroyGameObjectAfterDelay(GameObject go, float delay)
    {
        if (go == null) yield break;
        yield return new WaitForSecondsRealtime(delay);
        if (go != null)
        {
            // Ensure it's safe to destroy (not already destroyed)
            Destroy(go);
            Debug.Log("[CustomerManager] Destroyed faded customer object after delay.");
        }
    }
    #endregion
}