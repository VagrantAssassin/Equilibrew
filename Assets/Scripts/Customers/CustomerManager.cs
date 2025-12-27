using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// CustomerManager (final) - flow:
/// - spawn customer
/// - show request small dialog
/// - on correct serve -> show thankyou then run curhat (InkDialogController)
/// - on curhat done -> apply reaction -> advance
/// - on wrong serve -> increment fail; if max -> customer leaves
/// </summary>
public class CustomerManager : MonoBehaviour
{
    [Header("References (assign in Inspector)")]
    public RecipeValidator recipeValidator;
    public CupController cupController;
    public Transform spawnParent;
    public GameObject customerPrefab;

    [Header("Dialog (request small prefab)")]
    public RectTransform dialogAnchor;
    public GameObject dialogPrefab; // small request box

    [Header("Ink Dialog (controller)")]
    public InkDialogController inkDialogController;

    [Header("Profiles (customers pool)")]
    public List<CustomerProfile> profiles = new List<CustomerProfile>();

    [Header("Daily & delays")]
    public int minCustomersPerDay = 1;
    public float delayBeforeNextDay = 1.0f;
    public float delayBetweenCustomers = 1.0f;

    [Header("Message durations")]
    public float failureMessageDuration = 1.5f;
    public float successMessageDuration = 1.0f;
    public float leaveMessageDuration = 1.5f;

    // runtime
    private List<CustomerProfile> todaysProfiles = new List<CustomerProfile>();
    private int todaysIndex = 0;
    private Customer currentCustomer = null;
    private CustomerProfile currentProfile = null;
    private GameObject activeDialogInstance = null;
    private Coroutine messageCoroutine = null;

    private int dayNumber = 0;

    private enum ManagerState { Idle, ShowingMessage, WaitingBetweenCustomers, DayEnding }
    private ManagerState state = ManagerState.Idle;

    // prevent double-serve race
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
        dayNumber++;
        Debug.Log("[CustomerManager] Starting day " + dayNumber);

        if (profiles == null || profiles.Count == 0)
        {
            Debug.LogWarning("[CustomerManager] No customer profiles assigned.");
            todaysProfiles.Clear();
            todaysIndex = 0;
            return;
        }

        int maxAvailable = profiles.Count;
        int count = Mathf.Clamp(UnityEngine.Random.Range(minCustomersPerDay, maxAvailable + 1), 1, maxAvailable);

        var pool = new List<CustomerProfile>(profiles);
        Shuffle(pool);
        todaysProfiles = pool.GetRange(0, count);
        todaysIndex = 0;

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
        ClearDialogInstance();

        if (todaysProfiles == null || todaysIndex >= todaysProfiles.Count)
        {
            StartCoroutine(NextDayDelayed());
            return;
        }

        if (state != ManagerState.Idle)
        {
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

        // set profile-related fields
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

        // Choose requested recipe
        Recipe requested = null;
        if (profile.preferredRecipeNames != null && recipeValidator != null && recipeValidator.recipes != null)
        {
            var names = new List<string>(profile.preferredRecipeNames);
            Shuffle(names);
            foreach (var n in names)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                requested = recipeValidator.recipes.Find(r => string.Equals(r.recipeName, n.Trim(), StringComparison.OrdinalIgnoreCase));
                if (requested != null) break;
            }
        }
        if (requested == null && recipeValidator != null && recipeValidator.recipes != null && recipeValidator.recipes.Count > 0)
            requested = recipeValidator.recipes[UnityEngine.Random.Range(0, recipeValidator.recipes.Count)];

        // set current
        cust.SetRequest(requested);
        currentCustomer = cust;
        currentProfile = profile;

        Debug.Log("[CustomerManager] Spawned '" + profile.profileName + "' requesting '" + (requested != null ? requested.recipeName : "NONE") + "' (maxFails=" + cust.maxFails + ")");

        // show request text in small dialog (Jasmine Tea)
        CreateDialogInstanceForRecipe(requested);
    }

    private IEnumerator SpawnNextWhenIdleCoroutine()
    {
        yield return new WaitForSecondsRealtime(0.05f);
        SpawnNextFromToday();
    }

    private IEnumerator NextDayDelayed()
    {
        yield return new WaitForSecondsRealtime(delayBeforeNextDay);
        StartNewDay();
    }
    #endregion

    #region Curhat handling (after successful serve)
    private IEnumerator RunCurhatIfAnyAndThenAdvance()
    {
        // show a small thank-you first (update request dialog text)
        var txt = activeDialogInstance?.GetComponentInChildren<TextMeshProUGUI>(true);
        if (txt != null)
        {
            StartShowMessageAndThen(txt, "Terima kasih", successMessageDuration, null);
            yield return new WaitForSecondsRealtime(successMessageDuration);
        }

        // run curhat if profile has any stories
        if (currentProfile != null && currentProfile.curhatStories != null && currentProfile.curhatStories.Count > 0 && inkDialogController != null)
        {
            // safety: jika controller sedang bermain, tunggu sampai selesai
            while (inkDialogController.IsPlaying)
                yield return null;

            bool done = false;
            DialogueReaction reaction = DialogueReaction.Neutral;
            List<string> tags = null;

            // play story (dialog panel right)
            inkDialogController.PlayCurhat(currentProfile.curhatStories[UnityEngine.Random.Range(0, currentProfile.curhatStories.Count)], (r, tgs) =>
            {
                reaction = r;
                tags = tgs;
                done = true;
            });

            // wait until done
            yield return new WaitUntil(() => done);

            // apply reaction effects
            HandleCurhatReaction(currentCustomer, reaction, tags);
        }

        // proceed to next customer after a short delay
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

    #region Dialog helper (request placeholder)
    private void CreateDialogInstanceForRecipe(Recipe recipe)
    {
        ClearDialogInstance();

        if (dialogPrefab == null)
        {
            Debug.LogWarning("[CustomerManager] dialogPrefab not assigned. No visual dialog will be shown.");
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
            txt.text = recipe != null ? recipe.recipeName : "Saya ingin sesuatu...";
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
        Debug.Log("[CustomerManager] OnServeReceived. state=" + state + " current=" + (currentCustomer != null ? currentCustomer.name : "null"));

        if (serveLocked)
        {
            Debug.Log("[CustomerManager] Serve ignored - locked to prevent double processing.");
            return;
        }

        if (state != ManagerState.Idle) { Debug.Log("[CustomerManager] Serve ignored - not idle."); return; }
        if (currentCustomer == null) { Debug.Log("[CustomerManager] No active customer."); return; }

        // lock briefly to avoid re-entrancy
        serveLocked = true;
        StartCoroutine(ReleaseServeLockNextFrame());

        bool ok = currentCustomer.CheckServed(served);
        if (ok)
        {
            StartCoroutine(RunCurhatIfAnyAndThenAdvance());
            return;
        }

        // WRONG serve handling
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
                StartShowMessageAndThen(dialogText, "Ini bukan pesanan saya", failureMessageDuration, null);
            }
        }
        else
        {
            if (reached) StartCoroutine(AdvanceAfterDelay(0f));
            else Debug.Log("[CustomerManager] Wrong serve recorded; customer remains (no dialog).");
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

    #region Actions (Advance)
    public void AdvanceToNextCustomer()
    {
        if (messageCoroutine != null) { StopCoroutine(messageCoroutine); messageCoroutine = null; }
        messageCoroutine = StartCoroutine(AdvanceToNextCustomerCoroutine());
    }

    private IEnumerator AdvanceToNextCustomerCoroutine()
    {
        state = ManagerState.WaitingBetweenCustomers;

        if (currentCustomer != null)
        {
            currentCustomer.gameObject.SetActive(false);
            Destroy(currentCustomer.gameObject, 0.05f);
            currentCustomer = null;
            currentProfile = null;
        }

        ClearDialogInstance();

        yield return new WaitForSecondsRealtime(delayBetweenCustomers);

        state = ManagerState.Idle;
        messageCoroutine = null;
        SpawnNextFromToday();
    }
    #endregion

    #region Message helper (robust)
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
            if (currentCustomer != null && currentCustomer.requestedRecipe != null)
                txt.text = currentCustomer.requestedRecipe.recipeName;
            else
                txt.text = prev;

            state = ManagerState.Idle;
            messageCoroutine = null;
        }
    }
    #endregion
}