using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// CustomerManager (cleaned) with Ink curhat integration and reaction handling.
/// Replaced risky interpolations with safe concatenation for stability.
/// </summary>
public class CustomerManager : MonoBehaviour
{
    [Header("References (assign in Inspector)")]
    public RecipeValidator recipeValidator;
    public CupController cupController;
    public Transform spawnParent;
    public GameObject customerPrefab;

    [Header("Dialog (UI prefabs)")]
    public RectTransform dialogAnchor;
    public GameObject dialogPrefab; // recipe request

    [Header("Ink Dialog (scene controller)")]
    public InkDialogController inkDialogController;

    [Header("Profiles (customers pool)")]
    public List<CustomerProfile> profiles = new List<CustomerProfile>();

    [Header("Daily options")]
    public int minCustomersPerDay = 1;
    public float delayBeforeNextDay = 1.0f;

    [Header("Delays")]
    public float delayBetweenCustomers = 1.0f;

    [Header("Message durations")]
    public float failureMessageDuration = 1.5f;
    public float successMessageDuration = 1.0f;
    public float leaveMessageDuration = 1.5f;

    [Header("Effects (curhat reactions)")]
    public float tipMultiplierOnAgree = 1.2f;
    public float satisfactionPenaltyOnDisagree = 0.2f;

    // runtime
    private List<CustomerProfile> todaysProfiles = new List<CustomerProfile>();
    private int todaysIndex = 0;
    private Customer currentCustomer = null;
    private GameObject activeDialogInstance = null;
    private Coroutine messageCoroutine = null;

    private int dayNumber = 0;

    private enum ManagerState { Idle, ShowingMessage, WaitingBetweenCustomers, DayEnding }
    private ManagerState state = ManagerState.Idle;

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
        if (messageCoroutine != null)
        {
            StopCoroutine(messageCoroutine);
            messageCoroutine = null;
        }
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

        Debug.Log("[CustomerManager] Day " + dayNumber + " will have " + todaysProfiles.Count + " customers.");
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

        cust.SetRequest(requested);
        currentCustomer = cust;

        string requestedName = (requested != null) ? requested.recipeName : "NONE";
        Debug.Log("[CustomerManager] Spawned '" + profile.profileName + "' requesting '" + requestedName + "' (maxFails=" + cust.maxFails + ")");

        CreateDialogInstanceForRecipe(requested);

        if (profile.curhatStories != null && profile.curhatStories.Count > 0 && inkDialogController != null)
        {
            StartCoroutine(RunCurhatForProfile(profile));
        }
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

    #region Curhat handling
    private IEnumerator RunCurhatForProfile(CustomerProfile profile)
    {
        if (profile.curhatStories == null || profile.curhatStories.Count == 0 || inkDialogController == null)
            yield break;

        var t = profile.curhatStories[UnityEngine.Random.Range(0, profile.curhatStories.Count)];
        bool done = false;
        DialogueReaction reaction = DialogueReaction.Neutral;
        List<string> tags = null;

        inkDialogController.PlayCurhat(t, (r, choiceTags) =>
        {
            reaction = r;
            tags = choiceTags;
            done = true;
        });

        yield return new WaitUntil(() => done);

        HandleCurhatReaction(currentCustomer, reaction, tags);
    }

    private void HandleCurhatReaction(Customer cust, DialogueReaction reaction, List<string> tags)
    {
        if (cust == null) return;

        if (reaction == DialogueReaction.Agree)
        {
            Debug.Log("[CustomerManager] Curhat: AGREE for " + cust.name + " => increase tip chance by x" + tipMultiplierOnAgree);
        }
        else if (reaction == DialogueReaction.Neutral)
        {
            Debug.Log("[CustomerManager] Curhat: NEUTRAL for " + cust.name);
        }
        else if (reaction == DialogueReaction.Disagree)
        {
            Debug.Log("[CustomerManager] Curhat: DISAGREE for " + cust.name + " => satisfaction penalty " + satisfactionPenaltyOnDisagree);
        }

        if (tags != null && tags.Count > 0)
        {
            string joined = string.Join(",", tags);
            Debug.Log("[CustomerManager] Curhat tags: " + joined);
        }
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

    #region Serve handling (unchanged)
    private void OnServeReceived(Recipe served)
    {
        Debug.Log("[CustomerManager] OnServeReceived. state=" + state + " current=" + (currentCustomer != null ? currentCustomer.name : "null"));
        if (state != ManagerState.Idle) { Debug.Log("[CustomerManager] Serve ignored - not idle."); return; }
        if (currentCustomer == null) { Debug.Log("[CustomerManager] No active customer."); return; }

        bool ok = currentCustomer.CheckServed(served);
        if (ok)
        {
            var txt = activeDialogInstance?.GetComponentInChildren<TextMeshProUGUI>(true);
            if (txt != null)
            {
                StartShowMessageAndThen(txt, "Terima kasih", successMessageDuration, AdvanceToNextCustomerCoroutine);
                return;
            }
            AdvanceToNextCustomer();
            return;
        }

        Debug.Log("[CustomerManager] Wrong serve. fail(before)=" + currentCustomer.failCount + ", max=" + currentCustomer.maxFails);
        bool reached = currentCustomer.RegisterFail();
        Debug.Log("[CustomerManager] fail(after)=" + currentCustomer.failCount + ", reachedMax=" + reached);

        var dialogText = activeDialogInstance?.GetComponentInChildren<TextMeshProUGUI>(true);
        if (dialogText != null)
        {
            if (reached)
            {
                StartShowMessageAndThen(dialogText, "Kamu gimana sih kerjanya, saya mau pergi saja", leaveMessageDuration, AdvanceToNextCustomerCoroutine);
            }
            else
            {
                StartShowMessageAndThen(dialogText, "Ini bukan pesanan saya", failureMessageDuration, null);
            }
        }
        else
        {
            if (reached) AdvanceToNextCustomer();
            else Debug.Log("[CustomerManager] Wrong serve recorded; customer remains (no dialog).");
        }
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