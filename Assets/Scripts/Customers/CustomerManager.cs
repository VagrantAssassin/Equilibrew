using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// CustomerManager - complete final version with speaker-name helpers included
/// - Uses profile-provided Ink TextAssets for order/success/wrong/leave/curhat where available.
/// - Ensures wrong story will replay reliably by clearing previous panel and waiting for controller to be idle.
/// - Non-blocking visible fade-in/out, scheduling destroy after fade so fade is visible.
/// - Ordering: leavePanelOpen = true (panel reused)
/// - successStory / leaveStory: require user acknowledge at end (requireUserToAcknowledgeEnd = true)
/// - wrongStory / ordering: no acknowledgment required at end
/// - curhat: requires user acknowledgement at end so player can read it
/// - Automatically sets speaker/name text on dialog prefab instances by searching for child "Name"/"SpeakerName" or a child with "name" in its transform name.
/// - Reaction interpretation: Satisfy / Neutral / Angry (determined from Ink tags or fallback DialogueReaction)
/// 
/// Change in this version:
/// - allowServeWhilePanelOpen logic refined so wrongStory (non-ack) allows re-serving when finished,
///   while still blocking serve while story is actively playing.
/// - Plays NPC spawn/despawn SFX via AudioManager when customer spawns or is scheduled for despawn.
/// </summary>
public class CustomerManager : MonoBehaviour
{
    private static readonly string[] CurhatOutcomeReactionTags = { "angry", "disagree", "satisfy", "agree", "neutral" };

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
    public int minCustomersPerDay = 2;
    [Tooltip("Maximum customers per day when not using spawnAllPerDay")]
    public int maxCustomersPerDay = 4;
    [Tooltip("Delay before next day starts (seconds)")]
    public float delayBeforeNextDay = 1f;
    [Tooltip("Delay between customers when advancing (seconds)")]
    public float delayBetweenCustomers = 1f;
    [Tooltip("If true, spawn all profiles for the day (ignores random count). Useful for testing.")]
    public bool spawnAllPerDay = false;

    [Header("Message durations")]
    public float failureMessageDuration = 1.5f;
    public float successMessageDuration = 1.0f;
    public float leaveMessageDuration = 1.5f;

    [Header("Visual")]
    [Tooltip("Default fade duration for customer visuals (seconds)")]
    public float customerFadeDuration = 0.25f;

    [Header("Affinity tuning")]
    [Tooltip("Affinity change saat curhat hasil SATISFY.")]
    public float affinityGainOnCurhatSatisfy = 10f;
    [Tooltip("Affinity change saat curhat hasil ANGRY.")]
    public float affinityPenaltyOnCurhatAngry = -10f;
    [Tooltip("Affinity change saat pelanggan marah lalu pergi karena mencapai max fail.")]
    public float affinityPenaltyOnMaxFailLeave = -10f;

    [Header("Affinity score multiplier (correct serve)")]
    [Tooltip("Multiplier skor saat tier affinity Hostile.")]
    public float affinityScoreMultiplierHostile = 0.8f;
    [Tooltip("Multiplier skor saat tier affinity Friend.")]
    public float affinityScoreMultiplierFriend = 1f;
    [Tooltip("Multiplier skor saat tier affinity BestFriend.")]
    public float affinityScoreMultiplierBestFriend = 1.2f;
    [Tooltip("Multiplier skor saat tier affinity Soulmate.")]
    public float affinityScoreMultiplierSoulmate = 1.5f;

    [Header("Affinity UI (optional)")]
    [Tooltip("Widget UI yang menampilkan hati affinity pelanggan aktif. Assign di Inspector.")]
    public CustomerAffinityWidget affinityWidget;

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

    // allow serve while panel open temporarily (used for ordering flows that don't require user ack,
    // and used after wrongStory (non-ack) finishes so player can attempt again).
    private bool allowServeWhilePanelOpen = false;
    private Coroutine startDayCoroutine = null;
    private CustomerProfile lastSpawnedProfile = null;

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

        // Subscribe to game restart event so affinities reset on game restart
        if (GameManager.Instance != null)
            GameManager.Instance.OnGameRestartEvent += OnGameRestart;

        // Reset all affinities to 50% at game start
        ResetAllAffinities();

        StartNewDay();
    }

    private void OnDestroy()
    {
        if (cupController != null)
            cupController.OnServe -= OnServeReceived;

        if (GameManager.Instance != null)
            GameManager.Instance.OnGameRestartEvent -= OnGameRestart;
    }

    #region Daily flow
    private void StartNewDay()
    {
        if (messageCoroutine != null) { StopCoroutine(messageCoroutine); messageCoroutine = null; }
        if (startDayCoroutine != null) { StopCoroutine(startDayCoroutine); startDayCoroutine = null; }

        state = ManagerState.Idle;
        Debug.Log($"[CustomerManager] StartNewDay: profiles={(profiles!=null?profiles.Count:0)} minCustomersPerDay={minCustomersPerDay} maxCustomersPerDay={maxCustomersPerDay} spawnAllPerDay={spawnAllPerDay}");

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
            if (maxCustomersPerDay < minCustomersPerDay)
                Debug.LogWarning($"[CustomerManager] maxCustomersPerDay ({maxCustomersPerDay}) is smaller than minCustomersPerDay ({minCustomersPerDay}); effective range will be [{minCustomersPerDay}, {minCustomersPerDay}] before pool clamping.");

            int minC = Mathf.Clamp(minCustomersPerDay, 1, pool.Count);
            int maxC = Mathf.Clamp(maxCustomersPerDay, minC, pool.Count);
            count = UnityEngine.Random.Range(minC, maxC + 1);
        }

        EnsureFirstCustomerOfDayIsNotImmediateRepeat(pool, count);
        todaysProfiles = pool.GetRange(0, count);
        todaysIndex = 0;

        if (GameManager.Instance != null)
            GameManager.Instance.BeginNewDay(todaysProfiles.Count);

        Debug.Log($"[CustomerManager] Today will have {todaysProfiles.Count} customers.");
        float dayIntroDelay = Mathf.Max(0f, delayBeforeNextDay);
        startDayCoroutine = StartCoroutine(SpawnFirstCustomerAfterDayIntro(dayIntroDelay));
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

    private void EnsureFirstCustomerOfDayIsNotImmediateRepeat(List<CustomerProfile> pool, int todaysCount)
    {
        if (pool == null || pool.Count <= 1 || todaysCount <= 0 || lastSpawnedProfile == null)
            return;

        if (pool[0] != lastSpawnedProfile)
            return;

        for (int i = 1; i < pool.Count; i++)
        {
            if (pool[i] == null || pool[i] == lastSpawnedProfile)
                continue;

            var originalFirst = pool[0];
            pool[0] = pool[i];
            pool[i] = originalFirst;
            Debug.Log($"[CustomerManager] Reordered start-of-day queue to avoid cross-day immediate repeat: '{lastSpawnedProfile.profileName}'.");
            return;
        }

        Debug.LogWarning($"[CustomerManager] Cross-day immediate repeat unavoidable for profile '{lastSpawnedProfile.profileName}'.");
    }

    private void SpawnNextFromToday()
    {
        Debug.Log($"[CustomerManager] SpawnNextFromToday called. state={state} todaysIndex={todaysIndex} todaysProfilesCount={(todaysProfiles!=null?todaysProfiles.Count:0)}");

        ClearDialogInstance();

        if (todaysProfiles == null || todaysIndex >= todaysProfiles.Count)
        {
            Debug.Log("[CustomerManager] No more customers today.");
            Debug.Log("[CustomerManager] Scheduling NextDayDelayed.");
            StartCoroutine(NextDayDelayed());
            return;
        }

        if (state != ManagerState.Idle)
        {
            Debug.Log("[CustomerManager] Spawn deferred because state != Idle. Scheduling spawn shortly.");
            StartCoroutine(SpawnNextWhenIdleCoroutine());
            return;
        }

        var profile = TakeNextProfileAvoidingRepeat();
        if (profile == null)
        {
            Debug.LogWarning("[CustomerManager] Failed to select next profile.");
            StartCoroutine(NextDayDelayed());
            return;
        }

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
        cust.profile = profile;

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

        // Play NPC spawn SFX
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX_NPCSpawn();
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

        // Override with tier-specific order story if available (affinity system)
        List<TextAsset> tierOrderStories = GetOrderStoriesForTier(profile);
        if (tierOrderStories != null && idx < tierOrderStories.Count && tierOrderStories[idx] != null)
        {
            currentRequestedOrderStory = tierOrderStories[idx];
            Debug.Log($"[CustomerManager] Using tier-specific order story for tier={profile.GetCurrentTier()} affinity={profile.affinity}%");
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

        // Prepare affinity widget display values (will be shown by InkDialogController when dialog opens)
        UpdateAffinityWidgetDisplay(profile);

        // Play ordering phase. Keep panel open if using Ink orderStory (so we can reuse for result/curhat)
        state = ManagerState.Ordering;
        allowServeWhilePanelOpen = false; // default false; set true only if ordering flow explicitly allows serve while panel open

        if (inkDialogController != null && currentRequestedOrderStory != null)
        {
            ClearDialogInstance();
            // set speaker name for dialog
            inkDialogController.SetSpeakerName(currentProfile.profileName);

            // For ordering we intentionally set leavePanelOpen = true so we can reuse the panel.
            // Ordering normally does not require ack (requireUserToAcknowledgeEnd = false),
            // so we allow serve while panel is left open in that case (preserve original UX).
            bool requireAck = false;
            bool leavePanelOpen = true;
            if (!requireAck && leavePanelOpen)
                allowServeWhilePanelOpen = true;

            inkDialogController.PlayCurhat(currentRequestedOrderStory, (r, tags) =>
            {
                // When ordering story completes, we enter WaitingForServe.
                // If ordering was non-ack and leavePanelOpen=true, allowServeWhilePanelOpen remains true
                state = ManagerState.WaitingForServe;
                Debug.Log("[CustomerManager] Ordering story complete, now WaitingForServe.");
            }, skipOpenAnimation: false, leavePanelOpen: leavePanelOpen, requireUserToAcknowledgeEnd: requireAck);
        }
        else
        {
            CreateDialogInstanceForRecipe(requestedRecipe);
            state = ManagerState.WaitingForServe;
            // placeholder flow: allow serve while placeholder visible (original UX)
            allowServeWhilePanelOpen = true;
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
        Debug.Log("[CustomerManager] NextDayDelayed: starting next day.");

        var gm = GameManager.Instance;
        if (gm != null && gm.EvaluateEndOfDayAndTriggerGameOver())
        {
            state = ManagerState.DayEnding;
            yield break;
        }

        yield return null;
        StartNewDay();
    }

    private IEnumerator SpawnFirstCustomerAfterDayIntro(float delay)
    {
        bool waitedViaTransitionPanel = false;
        var gm = GameManager.Instance;
        if (gm != null)
        {
            gm.ShowDayTransition(delay);
            if (gm.IsDayTransitionVisible())
            {
                waitedViaTransitionPanel = true;
                while (gm != null && gm.IsDayTransitionVisible())
                    yield return null;
            }
        }

        if (!waitedViaTransitionPanel && delay > 0f)
        {
            yield return new WaitForSecondsRealtime(delay);
        }

        startDayCoroutine = null;
        SpawnNextFromToday();
    }

    private CustomerProfile TakeNextProfileAvoidingRepeat()
    {
        if (todaysProfiles == null || todaysIndex >= todaysProfiles.Count)
            return null;

        while (todaysIndex < todaysProfiles.Count && todaysProfiles[todaysIndex] == null)
            todaysIndex++;

        if (todaysIndex >= todaysProfiles.Count)
            return null;

        if (lastSpawnedProfile != null && todaysProfiles[todaysIndex] == lastSpawnedProfile)
        {
            for (int i = todaysIndex + 1; i < todaysProfiles.Count; i++)
            {
                if (todaysProfiles[i] == null || todaysProfiles[i] == lastSpawnedProfile)
                    continue;

                var swap = todaysProfiles[todaysIndex];
                todaysProfiles[todaysIndex] = todaysProfiles[i];
                todaysProfiles[i] = swap;
                Debug.Log($"[CustomerManager] Reordered today's queue to avoid immediate repeat: '{lastSpawnedProfile.profileName}' moved away from index {todaysIndex}.");
                break;
            }
        }

        var selected = todaysProfiles[todaysIndex++];
        if (selected == lastSpawnedProfile)
            Debug.LogWarning($"[CustomerManager] Immediate repeat unavoidable for profile '{selected?.profileName ?? "null"}'.");

        lastSpawnedProfile = selected;
        return selected;
    }

    private float GetAffinityScoreMultiplier(CustomerProfile profile)
    {
        if (profile == null) return 1f;

        switch (profile.GetCurrentTier())
        {
            case AffinityTier.Soulmate: return affinityScoreMultiplierSoulmate;
            case AffinityTier.BestFriend: return affinityScoreMultiplierBestFriend;
            case AffinityTier.Friend: return affinityScoreMultiplierFriend;
            default: return affinityScoreMultiplierHostile;
        }
    }
    #endregion

    #region Serve handling
    private void OnServeReceived(Recipe served)
    {
        Debug.Log($"[CustomerManager] OnServeReceived. state={state} current={(currentCustomer!=null?currentCustomer.name:"null")} serveLocked={serveLocked} allowServeWhilePanelOpen={allowServeWhilePanelOpen}");

        // New guards: block serve when dialog is active or ink controller is playing or legacy dialog instance is visible,
        // except when allowServeWhilePanelOpen == true (ordering flow that allowed serve even if panel was left open).
        if (serveLocked)
        {
            Debug.Log("[CustomerManager] Serve ignored - locked.");
            return;
        }

        if (inkDialogController != null)
        {
            if (inkDialogController.IsPlaying)
            {
                Debug.Log("[CustomerManager] Serve ignored - Ink dialog still playing.");
                return;
            }

            if (inkDialogController.IsPanelOpen && !allowServeWhilePanelOpen)
            {
                Debug.Log("[CustomerManager] Serve ignored - dialog panel still open.");
                return;
            }
        }

        if (activeDialogInstance != null && activeDialogInstance.activeInHierarchy && !allowServeWhilePanelOpen)
        {
            Debug.Log("[CustomerManager] Serve ignored - activeDialogInstance is visible.");
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

        // Passed guards — lock and process
        serveLocked = true;
        StartCoroutine(ReleaseServeLockNextFrame());

        // As soon as we start processing a serve, disallow serve-while-panel-open for safety
        allowServeWhilePanelOpen = false;

        bool ok = (served != null && string.Equals(served.recipeName, currentRequestedRecipeName, StringComparison.OrdinalIgnoreCase));
        if (ok)
        {
            Debug.Log("[CustomerManager] Correct serve!");
            // Correct serve: base score multiplied by affinity-tier multiplier.
            if (GameManager.Instance != null)
            {
                int basePoints = GameManager.Instance.pointsPerCorrectServe;
                float multiplier = GetAffinityScoreMultiplier(currentProfile);
                int finalPoints = Mathf.FloorToInt(basePoints * multiplier);
                GameManager.Instance.AddScore(finalPoints, $"correct_serve_x{multiplier:0.##}");
                Debug.Log($"[CustomerManager] Correct serve score: base={basePoints} multiplier={multiplier:0.##} final={finalPoints}");
            }

            // start coroutine that will play success story (if any) then curhat
            StartCoroutine(CorrectServeSequence());
            return;
        }

        // Wrong serve
        Debug.Log("[CustomerManager] Wrong serve. fail(before)=" + currentCustomer.failCount + ", max=" + currentCustomer.maxFails);
        bool reached = currentCustomer.RegisterFail();
        Debug.Log("[CustomerManager] fail(after)=" + currentCustomer.failCount + ", reachedMax=" + reached);

        if (!reached)
        {
            // Not yet leaving: try to play profile.wrongStory if exists
            if (currentProfile != null && currentProfile.wrongStory != null && inkDialogController != null)
            {
                // Ensure serve is blocked while the wrongStory is actively playing
                allowServeWhilePanelOpen = false;

                // set speaker name
                inkDialogController.SetSpeakerName(currentProfile.profileName);
                StartCoroutine(PlayWrongStoryThenRestore(currentProfile.wrongStory));
            }
            else
            {
                // fallback to previous text message behavior
                var dialogText = activeDialogInstance?.GetComponentInChildren<TextMeshProUGUI>(true);
                if (dialogText != null)
                {
                    // After this failure message we want player to be able to serve again,
                    // so allowServeWhilePanelOpen = true so serve will be accepted even if placeholder remains visible.
                    allowServeWhilePanelOpen = true;
                    StartShowMessageAndThen(dialogText, "Ini bukan pesanan saya", failureMessageDuration, () => RestoreWaitingForServeCoroutine());
                }
                else
                {
                    Debug.Log("[CustomerManager] Wrong serve recorded; customer remains (no dialog).");
                    // allow re-serve immediately in this fallback case
                    allowServeWhilePanelOpen = true;
                    state = ManagerState.WaitingForServe;
                }
            }
            return;
        }

        // ensure panel-open-serve flag is cleared
        allowServeWhilePanelOpen = false;

        if (currentProfile != null)
        {
            currentProfile.ChangeAffinity(affinityPenaltyOnMaxFailLeave);
            Debug.Log($"[CustomerManager] Max fail reached: affinity {affinityPenaltyOnMaxFailLeave} -> {currentProfile.affinity}% ({currentProfile.GetCurrentTier()})");
            UpdateAffinityWidgetDisplay(currentProfile);
        }

        if (currentProfile != null && currentProfile.leaveStory != null && inkDialogController != null)
        {
            // set speaker name
            inkDialogController.SetSpeakerName(currentProfile.profileName);
            StartCoroutine(PlayLeaveStoryThenAdvance(currentProfile.leaveStory));
        }
        else
        {
            // fallback
            var dialogText = activeDialogInstance?.GetComponentInChildren<TextMeshProUGUI>(true);
            if (dialogText != null)
            {
                StartShowMessageAndThen(dialogText, "Kamu gimana sih kerjanya, saya mau pergi saja", leaveMessageDuration, null);
                StartCoroutine(AdvanceAfterDelay(leaveMessageDuration));
            }
            else
            {
                StartCoroutine(AdvanceAfterDelay(0f));
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

    #region Correct / Wrong / Leave sequences
    private void SetSpeakerAndPlay(TextAsset story, bool skipOpenAnim, bool leaveOpen, bool requireAck, Action<DialogueReaction, List<string>> callback)
    {
        if (inkDialogController != null)
        {
            // any story that requires ack or is not an ordering flow should disable allowServeWhilePanelOpen
            allowServeWhilePanelOpen = false;
            inkDialogController.SetSpeakerName(currentProfile != null ? currentProfile.profileName : "");
            inkDialogController.PlayCurhat(story, callback, skipOpenAnim, leaveOpen, requireAck);
        }
    }

    private IEnumerator CorrectServeSequence()
    {
        // If profile has successStory play it first (require acknowledgement at end), else proceed directly
        if (currentProfile != null && currentProfile.successStory != null && inkDialogController != null)
        {
            bool done = false;
            DialogueReaction ignored = DialogueReaction.Neutral;
            // set speaker name
            inkDialogController.SetSpeakerName(currentProfile.profileName);
            // successStory requires acknowledgement from player at end
            // success story should block serve while showing/ack required
            allowServeWhilePanelOpen = false;
            inkDialogController.PlayCurhat(currentProfile.successStory, (r, tgs) =>
            {
                ignored = r;
                done = true;
            }, skipOpenAnimation: true, leavePanelOpen: true, requireUserToAcknowledgeEnd: true);
            yield return new WaitUntil(() => done);

            // After success story (acknowledged), play curhat (if any). Curhat will require ack as well below.
            yield return StartCoroutine(RunCurhatIfAnyAndThenAdvance());
        }
        else
        {
            // No success story: directly run curhat
            yield return StartCoroutine(RunCurhatIfAnyAndThenAdvance());
        }
    }

    // Ensure previous panel is cleared and controller idle before replaying wrong story.
    private IEnumerator PlayWrongStoryThenRestore(TextAsset wrongStory)
    {
        if (inkDialogController == null)
        {
            // fallback: show simple message then restore waiting state
            var dialogTextFallback = activeDialogInstance?.GetComponentInChildren<TextMeshProUGUI>(true);
            if (dialogTextFallback != null)
            {
                // allow re-serve after message
                allowServeWhilePanelOpen = true;
                StartShowMessageAndThen(dialogTextFallback, "Ini bukan pesanan saya", failureMessageDuration, () => RestoreWaitingForServeCoroutine());
            }
            yield break;
        }

        // Wait until any existing Ink play finishes to avoid overlapping plays
        while (inkDialogController.IsPlaying)
            yield return null;

        // Clear any leftover placeholder or previous panel so we start fresh
        ClearDialogInstance();

        bool done = false;

        // Play the wrongStory; do NOT skip opening animation so replay is obvious. No ack required.
        // wrongStory should block serve while showing (we cleared allowServeWhilePanelOpen before calling)
        inkDialogController.PlayCurhat(wrongStory, (r, tgs) =>
        {
            done = true;
        }, skipOpenAnimation: false, leavePanelOpen: true, requireUserToAcknowledgeEnd: false);

        // Wait until the wrong dialog finishes playing
        yield return new WaitUntil(() => done);

        // After showing wrong dialog, allow re-serving even if panel remains (wrong story is non-ack)
        allowServeWhilePanelOpen = true;

        // After showing wrong dialog, restore waiting UI so player can try again
        StartCoroutine(RestoreWaitingForServeCoroutine());
    }

    private IEnumerator PlayLeaveStoryThenAdvance(TextAsset leaveStory)
    {
        if (inkDialogController == null)
        {
            StartCoroutine(AdvanceAfterDelay(0f));
            yield break;
        }

        bool done = false;
        // Leave story requires user acknowledge at end before advancing
        allowServeWhilePanelOpen = false;
        inkDialogController.PlayCurhat(leaveStory, (r, tgs) =>
        {
            done = true;
        }, skipOpenAnimation: true, leavePanelOpen: false, requireUserToAcknowledgeEnd: true);

        yield return new WaitUntil(() => done);
        // After leave story (acknowledged), advance to next customer
        AdvanceToNextCustomer();
    }
    #endregion

    #region Curhat handling (REQUIRES ACKNOWLEDGEMENT AT END)
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

        // choose curhat story: prefer tier-specific list, then generic curhatStories, then fallback to orderStory
        TextAsset curhatToPlay = null;
        if (currentProfile != null)
        {
            // Try tier-specific curhat stories first (affinity system)
            List<TextAsset> tierCurhats = GetCurhatStoriesForTier(currentProfile);
            if (tierCurhats != null && tierCurhats.Count > 0)
            {
                curhatToPlay = tierCurhats[UnityEngine.Random.Range(0, tierCurhats.Count)];
                Debug.Log($"[CustomerManager] Using tier-specific curhat for tier={currentProfile.GetCurrentTier()} affinity={currentProfile.affinity}%");
            }
            // Fall back to generic curhatStories
            else if (currentProfile.curhatStories != null && currentProfile.curhatStories.Count > 0)
                curhatToPlay = currentProfile.curhatStories[UnityEngine.Random.Range(0, currentProfile.curhatStories.Count)];
        }
        if (curhatToPlay == null)
            curhatToPlay = currentRequestedOrderStory;

        if (curhatToPlay != null && inkDialogController != null)
        {
            // set speaker name
            inkDialogController.SetSpeakerName(currentProfile != null ? currentProfile.profileName : "");

            // wait if controller busy
            while (inkDialogController.IsPlaying)
                yield return null;

            bool done = false;
            DialogueReaction reaction = DialogueReaction.Neutral;
            List<string> tags = null;
            bool affinityAppliedAtChoice = false;

            Action<DialogueReaction, List<string>> onChoiceSelected = null;
            onChoiceSelected = (choiceReaction, choiceTags) =>
            {
                if (affinityAppliedAtChoice) return;
                if (!HasExplicitCurhatOutcomeTag(choiceTags)) return;

                CurhatOutcome choiceOutcome = DetermineOutcomeFromTags(choiceTags, choiceReaction);
                ApplyCurhatAffinityOutcome(choiceOutcome);
                affinityAppliedAtChoice = true;
                if (inkDialogController != null)
                    inkDialogController.OnChoiceSelected -= onChoiceSelected;
            };
            inkDialogController.OnChoiceSelected += onChoiceSelected;

            try
            {
                // Curhat requires user acknowledgement at end, so block serve while showing
                allowServeWhilePanelOpen = false;

                // Play curhat reusing panel if one was left open (skipOpenAnimation true).
                // requireUserToAcknowledgeEnd = true so curhat won't auto-close before player reads it.
                inkDialogController.PlayCurhat(curhatToPlay, (r, tgs) =>
                {
                    reaction = r;
                    tags = tgs;
                    done = true;
                }, skipOpenAnimation: true, leavePanelOpen: false, requireUserToAcknowledgeEnd: true);

                yield return new WaitUntil(() => done);
            }
            finally
            {
                inkDialogController.OnChoiceSelected -= onChoiceSelected;
            }

            HandleCurhatReaction(currentCustomer, reaction, tags, affinityAppliedAtChoice);
        }
        else
        {
            Debug.Log("[CustomerManager] No curhat story to play (null).");
        }

        yield return new WaitForSecondsRealtime(delayBetweenCustomers);
        AdvanceToNextCustomer();
    }
    #endregion

    #region HandleCurhatReaction (extend for game effects)
    private enum CurhatOutcome { Satisfy, Neutral, Angry }

    private bool HasReactionTag(string normalizedTag, string reactionName)
    {
        if (string.IsNullOrEmpty(normalizedTag) || string.IsNullOrEmpty(reactionName))
            return false;

        return normalizedTag.Contains($"reaction:{reactionName}") || normalizedTag == reactionName;
    }

    private bool HasExplicitCurhatOutcomeTag(List<string> tags)
    {
        if (tags == null || tags.Count == 0)
            return false;

        foreach (var tag in tags)
        {
            if (string.IsNullOrEmpty(tag)) continue;
            var low = tag.Trim().ToLowerInvariant();
            foreach (var reactionTag in CurhatOutcomeReactionTags)
            {
                if (HasReactionTag(low, reactionTag))
                    return true;
            }
        }

        return false;
    }

    private CurhatOutcome DetermineOutcomeFromTags(List<string> tags, DialogueReaction reactionFromInk)
    {
        // Prefer explicit tag if present; fallback to reaction enum if no tag found.
        if (tags != null)
        {
            bool hasAngry = false;
            bool hasSatisfy = false;
            bool hasNeutral = false;

            foreach (var t in tags)
            {
                if (string.IsNullOrEmpty(t)) continue;
                var low = t.Trim().ToLowerInvariant();
                if (HasReactionTag(low, "angry") || HasReactionTag(low, "disagree")) return CurhatOutcome.Angry;
                if (HasReactionTag(low, "satisfy") || HasReactionTag(low, "agree")) return CurhatOutcome.Satisfy;
                if (HasReactionTag(low, "neutral")) return CurhatOutcome.Neutral;

                if (low == "angry" || low == "disagree") hasAngry = true;
                else if (low == "satisfy" || low == "agree") hasSatisfy = true;
                else if (low == "neutral") hasNeutral = true;
            }

            if (hasAngry && !hasSatisfy) return CurhatOutcome.Angry;
            if (hasSatisfy && !hasAngry) return CurhatOutcome.Satisfy;
            // If both angry and satisfy are present, treat as ambiguous and fall back to reaction enum below.
            if (hasNeutral) return CurhatOutcome.Neutral;
        }

        // Fallback: use DialogueReaction (legacy)
        switch (reactionFromInk)
        {
            case DialogueReaction.Agree: return CurhatOutcome.Satisfy;
            case DialogueReaction.Disagree: return CurhatOutcome.Angry;
            default: return CurhatOutcome.Neutral;
        }
    }

    private void ApplyCurhatAffinityOutcome(CurhatOutcome outcome)
    {
        if (currentProfile == null) return;

        switch (outcome)
        {
            case CurhatOutcome.Satisfy:
                currentProfile.ChangeAffinity(affinityGainOnCurhatSatisfy);
                Debug.Log($"[CustomerManager] Curhat SATISFY: affinity {affinityGainOnCurhatSatisfy} -> {currentProfile.affinity}% ({currentProfile.GetCurrentTier()})");
                break;
            case CurhatOutcome.Angry:
                currentProfile.ChangeAffinity(affinityPenaltyOnCurhatAngry);
                Debug.Log($"[CustomerManager] Curhat ANGRY: affinity {affinityPenaltyOnCurhatAngry} -> {currentProfile.affinity}% ({currentProfile.GetCurrentTier()})");
                break;
            default:
                Debug.Log($"[CustomerManager] Curhat NEUTRAL: affinity unchanged at {currentProfile.affinity}% ({currentProfile.GetCurrentTier()})");
                break;
        }
        UpdateAffinityWidgetDisplay(currentProfile);
    }

    private void HandleCurhatReaction(Customer cust, DialogueReaction reaction, List<string> tags, bool affinityAlreadyApplied)
    {
        if (cust == null) return;

        // Determine outcome based on tags or fallback reaction
        CurhatOutcome outcome = DetermineOutcomeFromTags(tags, reaction);
        if (!affinityAlreadyApplied)
            ApplyCurhatAffinityOutcome(outcome);

        switch (outcome)
        {
            case CurhatOutcome.Satisfy:
                Debug.Log($"[CustomerManager] Curhat outcome: SATISFY for {cust.name}");
                break;

            case CurhatOutcome.Neutral:
                Debug.Log($"[CustomerManager] Curhat outcome: NEUTRAL for {cust.name}");
                break;

            case CurhatOutcome.Angry:
                Debug.Log($"[CustomerManager] Curhat outcome: ANGRY for {cust.name}");
                break;
        }

        if (tags != null && tags.Count > 0)
            Debug.Log("[CustomerManager] Curhat tags: " + string.Join(",", tags));
    }
    #endregion

    #region Affinity helpers
    /// <summary>
    /// Reset affinity semua profil ke 50%. Dipanggil saat game mulai atau di-restart.
    /// </summary>
    private void ResetAllAffinities()
    {
        if (profiles == null) return;
        foreach (var p in profiles)
            if (p != null) p.ResetAffinity();
        Debug.Log("[CustomerManager] All affinities reset to 50%.");
    }

    /// <summary>Handler untuk GameManager.OnGameRestartEvent.</summary>
    private void OnGameRestart()
    {
        ResetAllAffinities();
        lastSpawnedProfile = null;
    }

    /// <summary>
    /// Kembalikan list order stories yang sesuai tier affinity profil saat ini.
    /// Returns null/empty jika tier list kosong sehingga caller dapat fallback ke orderStories.
    /// </summary>
    private List<TextAsset> GetOrderStoriesForTier(CustomerProfile profile)
    {
        if (profile == null) return null;
        switch (profile.GetCurrentTier())
        {
            case AffinityTier.Soulmate:   return profile.orderStoriesSoulmate;
            case AffinityTier.BestFriend: return profile.orderStoriesBestFriend;
            case AffinityTier.Friend:     return profile.orderStoriesFriend;
            default:                      return profile.orderStoriesHostile;
        }
    }

    /// <summary>
    /// Kembalikan list curhat stories yang sesuai tier affinity profil saat ini.
    /// Returns null/empty jika tier list kosong sehingga caller dapat fallback ke curhatStories.
    /// </summary>
    private List<TextAsset> GetCurhatStoriesForTier(CustomerProfile profile)
    {
        if (profile == null) return null;
        switch (profile.GetCurrentTier())
        {
            case AffinityTier.Soulmate:   return profile.curhatStoriesSoulmate;
            case AffinityTier.BestFriend: return profile.curhatStoriesBestFriend;
            case AffinityTier.Friend:     return profile.curhatStoriesFriend;
            default:                      return profile.curhatStoriesHostile;
        }
    }

    private CustomerAffinityWidget ResolveAffinityWidget()
    {
        if (affinityWidget != null)
            return affinityWidget;
        if (inkDialogController != null && inkDialogController.affinityWidget != null)
            return inkDialogController.affinityWidget;
        return null;
    }

    private void UpdateAffinityWidgetDisplay(CustomerProfile profile)
    {
        if (profile == null) return;
        ResolveAffinityWidget()?.UpdateDisplay(profile.affinity, profile.GetCurrentTier());
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

        // Auto set speaker/name on legacy dialog instance if it has a child named "Name" or "SpeakerName"
        SetNameOnDialogInstance(activeDialogInstance, currentProfile != null ? currentProfile.profileName : "");
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

    #region Advance & messages
    public void AdvanceToNextCustomer()
    {
        if (messageCoroutine != null) { StopCoroutine(messageCoroutine); messageCoroutine = null; }
        messageCoroutine = StartCoroutine(AdvanceToNextCustomerCoroutine());
    }

    private IEnumerator AdvanceToNextCustomerCoroutine()
    {
        state = ManagerState.WaitingBetweenCustomers;
        Debug.Log("[CustomerManager] AdvanceToNextCustomerCoroutine started.");

        GameObject goToDestroy = null;

        if (currentCustomer != null)
        {
            goToDestroy = currentCustomer.gameObject;

            // Play NPC despawn SFX when customer starts leaving
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySFX_NPCDespawn();
            }

            // Attempt fade-out on either visual controller name (start fading non-blocking, visible)
            var visOld = currentCustomer.GetComponent("CustomerVisualController");
            if (visOld != null)
            {
                StartCoroutine(CallFadeOutCoroutineDynamic(visOld, customerFadeDuration));
            }
            else
            {
                var visNew = currentCustomer.GetComponent("CustomerVisualFade");
                if (visNew != null)
                {
                    StartCoroutine(CallFadeOutCoroutineDynamic(visNew, customerFadeDuration));
                }
            }

            // disable immediate interaction components so player can't interact while fading
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

            // Hide affinity widget — no active customer
            ResolveAffinityWidget()?.Hide();
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

    #region Dynamic fade-in/out callers & destroy helper
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

    private IEnumerator DestroyGameObjectAfterDelay(GameObject go, float delay)
    {
        if (go == null) yield break;
        yield return new WaitForSecondsRealtime(delay);
        if (go != null)
        {
            Destroy(go);
            Debug.Log("[CustomerManager] Destroyed faded customer object after delay.");
        }
    }
    #endregion

    #region Utility: set name on legacy dialog instance & helpers
    private void SetNameOnDialogInstance(GameObject dialogInstance, string name)
    {
        if (dialogInstance == null) return;
        var t = FindChildByNamesRecursive(dialogInstance.transform, new string[] { "Name", "SpeakerName" });
        if (t != null)
        {
            // grab TMP from the subtree (handles Name -> Text structure)
            var tmp = t.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null) { tmp.text = name ?? ""; return; }
        }

        // fallback: find first TMP with 'name' in transform name
        var tmpFallback = FindTMPByNameHint(dialogInstance.transform, "name");
        if (tmpFallback != null) tmpFallback.text = name ?? "";
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
    #endregion
}
