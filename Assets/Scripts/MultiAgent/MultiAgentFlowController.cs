using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using ProfileResponse = MultiAgentBridge.ProfileResponse;
using DialogResponse = MultiAgentBridge.DialogResponse;
using EvaluateResponse = MultiAgentBridge.EvaluateResponse;
using PilihanJawaban = MultiAgentBridge.PilihanJawaban;

/// <summary>
/// MultiAgentFlowController — menggantikan CustomerManager untuk mode Multi-Agent.
/// 
/// Alur customer via LLM Multi-Agent:
///   1. Spawn customer visual dari CustomerProfile (sprite random)
///   2. Generate profile via Profile Agent (nama, background, OCEAN)
///   3. Generate order dialog via Dialogue Agent
///   4. Player serve → validate → wrong/success dialog via Dialogue Agent
///   5. Curhat multi-ronde via Dialogue Agent + mood system
///   6. Player jawab → evaluate via LLM → mood update + reaksi
/// 
/// Pasang di scene, disable CustomerManager lama, enable controller ini.
/// </summary>
public class MultiAgentFlowController : MonoBehaviour
{
    [Header("References (assign in Inspector)")]
    public RecipeValidator recipeValidator;
    public CupController cupController;
    public Transform spawnParent;
    public GameObject customerPrefab;
    public InkDialogController inkDialogController;

    [Header("Customer Profiles (for visuals only)")]
    public List<CustomerProfile> profiles = new List<CustomerProfile>();

    [Header("Multi-Agent Adapter")]
    public MultiAgentCustomerAdapter adapter;

    [Header("Daily Settings")]
    public int minCustomersPerDay = 2;
    public int maxCustomersPerDay = 4;
    public float delayBeforeNextDay = 1f;
    public float delayBetweenCustomers = 1f;
    public bool spawnAllPerDay = false;

    [Header("Visual")]
    public float customerFadeDuration = 0.25f;
    [Tooltip("Delay sebelum customer pergi setelah dialog terakhir (detik)")]
    public float customerLeaveDelay = 1.5f;

    [Header("Affinity")]
    public float affinityGainOnCurhatSatisfy = 10f;
    public float affinityPenaltyOnCurhatAngry = -10f;
    public float affinityPenaltyOnMaxFailLeave = -10f;

    [Header("Score Multipliers")]
    public float affinityScoreMultiplierHostile = 0.8f;
    public float affinityScoreMultiplierFriend = 1f;
    public float affinityScoreMultiplierBestFriend = 1.2f;
    public float affinityScoreMultiplierSoulmate = 1.5f;

    [Header("UI (optional)")]
    public CustomerAffinityWidget affinityWidget;
    public SlidingPanelController ingredientPanelController;
    [Tooltip("Curhat choice UI — auto-found if null")]
    public CurhatChoiceUI curhatChoiceUI;

    [Header("NPC Menu")]
    [Tooltip("Daftar minuman yang tersedia (dikirim ke Python)")]
    public string[] menuMinuman = { "Black Tea", "Mint Tea", "Green Tea" };

    [Header("Debug")]
    public bool verboseLogging = true;

    // Runtime state
    private List<CustomerProfile> todaysProfiles = new List<CustomerProfile>();
    private int todaysIndex = 0;
    private Customer currentCustomer = null;
    private CustomerProfile currentProfile = null;
    private string currentCustomerDisplayName = "";
    private string currentRequestedRecipeName = null;
    private bool serveLocked = false;
    private bool finishInProgress = false;

    private enum FlowState
    {
        Idle,
        GeneratingProfile,
        GeneratingOrder,
        WaitingForServe,
        GeneratingWrong,
        GeneratingSuccess,
        GeneratingAngry,
        GeneratingCurhat,
        WaitingCurhatInput,
        GeneratingReaction,
        WaitingBetweenCustomers,
        DayEnding
    }
    private FlowState state = FlowState.Idle;

    // ── Disable old CustomerManager as early as possible ──
    private void Awake()
    {
        // FindObjectsOfType to handle DDOL duplicates: disable only the CM in OUR scene
        var allCMs = FindObjectsOfType<CustomerManager>();
        foreach (var cm in allCMs)
        {
            if (cm.gameObject.scene == gameObject.scene)
            {
                Debug.Log($"[MultiAgentFlow] Disabling legacy CustomerManager in scene '{cm.gameObject.scene.name}'.");
                cm.enabled = false;
                break;
            }
        }
    }

    private void Start()
    {
        // Auto-find adapter if not assigned
        if (adapter == null)
            adapter = MultiAgentCustomerAdapter.Instance;
        if (adapter == null)
            adapter = FindObjectOfType<MultiAgentCustomerAdapter>();
        
        // Auto-find curhat choice UI
        if (curhatChoiceUI == null)
            curhatChoiceUI = FindObjectOfType<CurhatChoiceUI>();

        if (adapter == null)
        {
            Debug.LogError("[MultiAgentFlow] MultiAgentCustomerAdapter not found! Add it to the scene.");
            return;
        }

        // Wire up InkDialogController to adapter
        if (inkDialogController != null)
            adapter.inkDialogController = inkDialogController;

        if (cupController != null)
        {
            cupController.OnServe -= OnServeReceived;
            cupController.OnServe += OnServeReceived;
        }

        if (GameManager.Instance != null)
            GameManager.Instance.OnGameRestartEvent += OnGameRestart;

        ResetAllAffinities();

        // Check server health before starting (with retries)
        StartCoroutine(CheckServerAndStartDayWithRetry());
    }

    private void OnDestroy()
    {
        if (cupController != null)
            cupController.OnServe -= OnServeReceived;
        if (GameManager.Instance != null)
            GameManager.Instance.OnGameRestartEvent -= OnGameRestart;
    }

    // ── Day Flow ──────────────────────────────────────────────────────────────

    private void CheckServerAndStartDay()
    {
        StartCoroutine(CheckServerAndStartDayWithRetry());
    }

    [Tooltip("Jumlah percobaan koneksi ke server")]
    private const int healthCheckRetries = 3;
    [Tooltip("Delay antar percobaan (detik)")]
    private const float healthCheckRetryDelay = 1.5f;

    private IEnumerator CheckServerAndStartDayWithRetry()
    {
        for (int attempt = 1; attempt <= healthCheckRetries; attempt++)
        {
            Debug.Log($"[MultiAgentFlow] Health check attempt {attempt}/{healthCheckRetries}...");
            bool ready = false;
            bool ok = false;
            adapter.CheckServerHealth((success) =>
            {
                ok = success;
                ready = true;
                if (!success)
                    Debug.LogWarning($"[MultiAgentFlow] Health check failed on attempt {attempt}");
            });
            while (!ready) yield return null;

            if (ok)
            {
                Debug.Log("[MultiAgentFlow] Server is alive. Starting day.");
                StartNewDay();
                yield break;
            }

            if (attempt < healthCheckRetries)
            {
                Debug.Log($"[MultiAgentFlow] Retrying in {healthCheckRetryDelay}s...");
                yield return new WaitForSecondsRealtime(healthCheckRetryDelay);
            }
        }

        Debug.LogError("[MultiAgentFlow] Python server not reachable after retries! Start server.py first.");
    }

    private void StartNewDay()
    {
        state = FlowState.Idle;
        Debug.Log($"[MultiAgentFlow] StartNewDay: profiles={profiles.Count}");

        if (profiles == null || profiles.Count == 0)
        {
            Debug.LogWarning("[MultiAgentFlow] No customer profiles assigned.");
            return;
        }

        var pool = new List<CustomerProfile>();
        foreach (var p in profiles)
            if (p != null) pool.Add(p);

        int count;
        if (spawnAllPerDay)
        {
            count = pool.Count;
            Shuffle(pool);
            todaysProfiles = new List<CustomerProfile>(pool);
        }
        else
        {
            int minC = Mathf.Max(1, minCustomersPerDay);
            int maxC = Mathf.Max(minC, maxCustomersPerDay);
            count = UnityEngine.Random.Range(minC, maxC + 1);

            todaysProfiles = new List<CustomerProfile>();
            for (int i = 0; i < count; i++)
                todaysProfiles.Add(pool[UnityEngine.Random.Range(0, pool.Count)]);
        }

        todaysIndex = 0;

        if (GameManager.Instance != null)
            GameManager.Instance.BeginNewDay(todaysProfiles.Count);

        Debug.Log($"[MultiAgentFlow] Today: {todaysProfiles.Count} customers.");
        StartCoroutine(SpawnFirstCustomerAfterDelay());
    }

    private IEnumerator SpawnFirstCustomerAfterDelay()
    {
        var gm = GameManager.Instance;
        if (gm != null)
        {
            gm.ShowDayTransition(delayBeforeNextDay);
            while (gm != null && gm.IsDayTransitionVisible())
                yield return null;
        }
        else
        {
            yield return new WaitForSecondsRealtime(delayBeforeNextDay);
        }

        SpawnNextCustomer();
    }

    private void Shuffle<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int j = UnityEngine.Random.Range(i, list.Count);
            T tmp = list[i]; list[i] = list[j]; list[j] = tmp;
        }
    }

    // ── Customer Spawn ────────────────────────────────────────────────────────

    private void SpawnNextCustomer()
    {
        finishInProgress = false;

        if (todaysProfiles == null || todaysIndex >= todaysProfiles.Count)
        {
            Debug.Log("[MultiAgentFlow] No more customers today.");
            StartCoroutine(NextDayDelayed());
            return;
        }

        if (state != FlowState.Idle)
        {
            Debug.Log("[MultiAgentFlow] Not idle, deferring spawn...");
            StartCoroutine(SpawnWhenIdle());
            return;
        }

        currentProfile = todaysProfiles[todaysIndex++];
        currentProfile.ResetAffinity();
        currentCustomerDisplayName = currentProfile.GetRandomDisplayName();

        AutoOpenIngredientPanel();
        UpdateAffinityWidget(currentProfile);

        // Generate profile via LLM — spawn visual AFTER response
        state = FlowState.GeneratingProfile;
        Debug.Log($"[MultiAgentFlow] Generating profile for category={currentProfile.categoryName}");

        string usia = GuessUsiaFromProfile(currentProfile);
        string gender = GuessGenderFromProfile(currentProfile);

        adapter.GenerateProfile(usia, gender, (profileResp) =>
        {
            Debug.Log($"[MultiAgentFlow] Profile ready: {profileResp.nama}");
            currentCustomerDisplayName = profileResp.nama;

            // ── Spawn visual AFTER LLM responds ──
            GameObject go = Instantiate(customerPrefab, spawnParent);
            go.name = $"Customer_{currentProfile.categoryName}_{currentCustomerDisplayName}";
            var cust = go.GetComponent<Customer>() ?? go.AddComponent<Customer>();
            cust.maxFails = Mathf.Clamp(profileResp.max_fails, 1, 3);
            cust.failCount = 0;
            cust.profile = currentProfile;
            currentCustomer = cust;

            ApplyRandomCustomerVisuals(go, currentProfile);

            // Ensure all Image alphas are reset before fade-in (CanvasGroup handles overall visibility)
            foreach (var img in go.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (img != null && img.sprite != null)
                    img.color = Color.white;
            }

            // Fade in
            var vis = go.GetComponent<CustomerVisualController>();
            if (vis != null) StartCoroutine(vis.FadeInCoroutine(customerFadeDuration));

            if (AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX_NPCSpawn();

            // Set speaker name
            inkDialogController?.SetSpeakerName(currentCustomerDisplayName);

            // Generate order dialog
            state = FlowState.GeneratingOrder;
            adapter.GenerateOrderDialog((dialogResp) =>
            {
                Debug.Log($"[MultiAgentFlow] Order dialog ready. Critic: {dialogResp.critic_skor}/100");

                // Find recipe
                currentRequestedRecipeName = dialogResp.minuman_dipesan;
                Recipe requestedRecipe = null;
                if (!string.IsNullOrEmpty(currentRequestedRecipeName) && recipeValidator?.recipes != null)
                {
                    requestedRecipe = recipeValidator.recipes.Find(r =>
                        string.Equals(r.recipeName, currentRequestedRecipeName, StringComparison.OrdinalIgnoreCase));
                }
                cust.SetRequest(requestedRecipe);

                // Play order dialog (per-sentence, panel stays open)
                PlayDynamicDialog(dialogResp.dialog_text, true, false, () =>
                {
                    state = FlowState.WaitingForServe;
                    Debug.Log("[MultiAgentFlow] Order complete. Waiting for serve.");
                });

            }, (error) => HandleError("Order", error));

        }, (error) => HandleError("Profile", error), menuMinuman);
    }

    private IEnumerator SpawnWhenIdle()
    {
        yield return new WaitForSecondsRealtime(0.1f);
        SpawnNextCustomer();
    }

    private IEnumerator NextDayDelayed()
    {
        var gm = GameManager.Instance;
        if (gm != null && gm.EvaluateEndOfDayAndTriggerGameOver())
        {
            state = FlowState.DayEnding;
            yield break;
        }
        yield return null;
        StartNewDay();
    }

    // ── Serve Handling ────────────────────────────────────────────────────────

    private void OnServeReceived(Recipe served)
    {
        if (serveLocked || state != FlowState.WaitingForServe)
        {
            if (verboseLogging)
                Debug.Log($"[MultiAgentFlow] Serve ignored: locked={serveLocked} state={state}");
            return;
        }

        if (inkDialogController != null && inkDialogController.IsPlaying)
        {
            Debug.Log("[MultiAgentFlow] Serve ignored: dialog still playing.");
            return;
        }

        serveLocked = true;
        StartCoroutine(ReleaseServeLockNextFrame());

        bool correct = served != null &&
            string.Equals(served.recipeName, currentRequestedRecipeName, StringComparison.OrdinalIgnoreCase);

        if (correct)
        {
            Debug.Log("[MultiAgentFlow] Correct serve!");
            AutoCloseIngredientPanel();
            StartCoroutine(HandleCorrectServe());
        }
        else
        {
            Debug.Log("[MultiAgentFlow] Wrong serve.");
            currentCustomer.RegisterFail();

            if (currentCustomer.failCount >= currentCustomer.maxFails)
            {
                Debug.Log("[MultiAgentFlow] Max fails reached. Customer leaving angry.");
                StartCoroutine(HandleMaxFailLeave());
            }
            else
            {
                StartCoroutine(HandleWrongServe());
            }
        }
    }

    private IEnumerator ReleaseServeLockNextFrame()
    {
        yield return null;
        serveLocked = false;
    }

    private IEnumerator HandleWrongServe()
    {
        state = FlowState.GeneratingWrong;

        adapter.GenerateWrongDialog((dialogResp) =>
        {
            Debug.Log("[MultiAgentFlow] Wrong dialog ready.");
            PlayDynamicDialog(dialogResp.dialog_text, true, false, () =>
            {
                state = FlowState.WaitingForServe;
                Debug.Log("[MultiAgentFlow] Wrong dialog done. Waiting for serve again.");
            });
        }, (error) =>
        {
            Debug.LogError($"[MultiAgentFlow] Wrong dialog error: {error}");
            state = FlowState.WaitingForServe;
        });

        yield break;
    }

    private IEnumerator HandleMaxFailLeave()
    {
        state = FlowState.GeneratingAngry;

        // Apply affinity penalty
        if (currentProfile != null)
        {
            currentProfile.ChangeAffinity(affinityPenaltyOnMaxFailLeave);
            UpdateAffinityWidget(currentProfile);
        }

        if (GameManager.Instance != null)
            GameManager.Instance.AddScore(GameManager.Instance.pointsPenaltyOnMaxFail, "max_fail_penalty");

        adapter.GenerateAngryDialog((dialogResp) =>
        {
            Debug.Log("[MultiAgentFlow] Angry dialog ready.");
            PlayDynamicDialog(dialogResp.dialog_text, false, true, () =>
            {
                AdvanceToNextCustomer();
            });
        }, (error) =>
        {
            Debug.LogError($"[MultiAgentFlow] Angry dialog error: {error}");
            AdvanceToNextCustomer();
        });

        yield break;
    }

    private IEnumerator HandleCorrectServe()
    {
        state = FlowState.GeneratingSuccess;

        // Generate success dialog
        adapter.GenerateSuccessDialog((successResp) =>
        {
            Debug.Log("[MultiAgentFlow] Success dialog ready.");
            PlayDynamicDialog(successResp.dialog_text, true, true, () =>
            {
                // After success dialog acknowledged, start curhat
                StartCoroutine(RunCurhatSession());
            });
        }, (error) =>
        {
            Debug.LogError($"[MultiAgentFlow] Success dialog error: {error}");
            StartCoroutine(RunCurhatSession());
        });

        yield break;
    }

    // ── Curhat Session ────────────────────────────────────────────────────────

    private IEnumerator RunCurhatSession()
    {
        state = FlowState.GeneratingCurhat;
        Debug.Log("[MultiAgentFlow] Starting curhat session.");

        // Small thank-you
        yield return new WaitForSecondsRealtime(0.5f);

        adapter.StartCurhat((curhatResp) =>
        {
            Debug.Log("[MultiAgentFlow] Curhat round 1 ready.");
            ShowCurhatRound(curhatResp);
        }, (error) =>
        {
            Debug.LogError($"[MultiAgentFlow] Curhat start error: {error}");
            FinishCustomer();
        });
    }

    private void ShowCurhatRound(DialogResponse curhatResp)
    {
        // Show NPC dialog per-sentence, then show choices using existing UI
        bool hasChoices = curhatResp.pilihan_jawaban != null && curhatResp.pilihan_jawaban.Length > 0;

        // Ronde terakhir tanpa pilihan = penutup → set state berbeda
        if (!hasChoices)
        {
            state = FlowState.GeneratingCurhat; // sementara, bukan WaitingCurhatInput
        }
        else
        {
            state = FlowState.WaitingCurhatInput;
        }

        PlayDynamicDialog(curhatResp.dialog_text, true, false, () =>
        {
            if (hasChoices && inkDialogController != null)
            {
                // Show choice buttons
                inkDialogController.ShowChoicesOnPanel(curhatResp.pilihan_jawaban, (jawaban, nada) =>
                {
                    Debug.Log($"[MultiAgentFlow] Choice selected: {jawaban} (nada={nada})");
                    OnCurhatPlayerResponse(jawaban, nada);
                });
                Debug.Log("[MultiAgentFlow] Curhat dialog shown. Waiting for player response.");
            }
            else
            {
                // Penutup - tidak ada pilihan, selesai setelah dialog
                Debug.Log("[MultiAgentFlow] Penutup shown. Finishing customer.");
                FinishCustomer();
            }
        });
    }

    /// <summary>
    /// Dipanggil ketika player memilih jawaban curhat (dari UI choice button atau input field).
    /// </summary>
    public void OnCurhatPlayerResponse(string jawaban, string chosenNada = null)
    {
        if (state != FlowState.WaitingCurhatInput)
        {
            Debug.LogWarning("[MultiAgentFlow] Not waiting for curhat input.");
            return;
        }

        state = FlowState.GeneratingReaction;
        Debug.Log($"[MultiAgentFlow] Player answered: {jawaban} (nada={chosenNada ?? "auto"})");

        float moodSebelum = currentProfile != null ? currentProfile.affinity : 50f;

        adapter.EvaluateAnswer(jawaban, (evalResp) =>
        {
            Debug.Log($"[MultiAgentFlow] Evaluate: nada={evalResp.nada}, mood={evalResp.mood_sebelum}→{evalResp.mood_sesudah}");

            // Update affinity based on outcome
            ApplyCurhatAffinity(evalResp.nada);

            // Skip reaction dialog if empty (reaksi removed - player choice becomes context for next round)
            string reactionText = evalResp.reaksi_npc;
            if (!string.IsNullOrEmpty(reactionText))
            {
                PlayDynamicDialog(reactionText, true, true, () =>
                {
                    CheckNextCurhatRound();
                });
            }
            else
            {
                CheckNextCurhatRound();
            }
        }, (error) =>
        {
            Debug.LogError($"[MultiAgentFlow] Evaluate error: {error}");
            CheckNextCurhatRound();
        }, chosenNada);
    }

    private void CheckNextCurhatRound()
    {
        // Check round limit BEFORE requesting next round
        int nextRound = adapter.CurrentCurhatRound + 1;
        int maxRounds = adapter.TotalCurhatRounds;

        if (maxRounds > 0 && nextRound > maxRounds)
        {
            Debug.Log($"[MultiAgentFlow] Curhat selesai: sudah mencapai {maxRounds} ronde.");
            FinishCustomer();
            return;
        }

        Debug.Log($"[MultiAgentFlow] Requesting curhat round {nextRound}/{maxRounds}");
        adapter.NextCurhatRound((nextResp) =>
        {
            Debug.Log($"[MultiAgentFlow] Next curhat round {nextResp.ronde_sekarang}/{nextResp.total_ronde} ready.");
            ShowCurhatRound(nextResp);
        }, (error) =>
        {
            // Server returned error (e.g. round limit exceeded) — finish
            Debug.Log($"[MultiAgentFlow] Curhat session ended: {error}");
            FinishCustomer();
        });
    }

    private void ApplyCurhatAffinity(string nada)
    {
        if (currentProfile == null) return;

        switch (nada)
        {
            case "satisfy":
                currentProfile.ChangeAffinity(affinityGainOnCurhatSatisfy);
                Debug.Log($"[MultiAgentFlow] Curhat SATISFY: affinity +{affinityGainOnCurhatSatisfy} → {currentProfile.affinity}%");
                break;
            case "angry":
                currentProfile.ChangeAffinity(affinityPenaltyOnCurhatAngry);
                Debug.Log($"[MultiAgentFlow] Curhat ANGRY: affinity {affinityPenaltyOnCurhatAngry} → {currentProfile.affinity}%");
                break;
            default:
                Debug.Log($"[MultiAgentFlow] Curhat NEUTRAL: affinity unchanged at {currentProfile.affinity}%");
                break;
        }
        UpdateAffinityWidget(currentProfile);
    }

    // ── Finish & Advance ──────────────────────────────────────────────────────

    private void FinishCustomer()
    {
        if (finishInProgress) return;
        finishInProgress = true;

        // Hide curhat UI if visible
        curhatChoiceUI?.Hide();

        // Calculate score
        if (GameManager.Instance != null)
        {
            int basePoints = GameManager.Instance.pointsPerCorrectServe;
            float multiplier = GetAffinityMultiplier();
            int finalPoints = Mathf.FloorToInt(basePoints * multiplier);
            GameManager.Instance.AddScore(finalPoints, $"curhat_complete_x{multiplier:0.##}");
            Debug.Log($"[MultiAgentFlow] Score: base={basePoints} × {multiplier:0.##} = {finalPoints}");
        }

        StartCoroutine(FinishCustomerDelayed());
    }

    private IEnumerator FinishCustomerDelayed()
    {
        // Biarkan NPC terlihat sebentar setelah dialog terakhir
        yield return new WaitForSecondsRealtime(customerLeaveDelay);

        // Close dialog panel
        if (inkDialogController != null) inkDialogController.ForceClose();

        AdvanceToNextCustomer();
    }

    private void AdvanceToNextCustomer()
    {
        StartCoroutine(AdvanceCoroutine());
    }

    private IEnumerator AdvanceCoroutine()
    {
        state = FlowState.WaitingBetweenCustomers;

        // Clear session on server
        adapter.ClearSession();

        // Fade out and destroy customer
        if (currentCustomer != null)
        {
            if (AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX_NPCDespawn();

            var vis = currentCustomer.GetComponent<CustomerVisualController>();
            if (vis != null) StartCoroutine(vis.FadeOutCoroutine(customerFadeDuration));

            var go = currentCustomer.gameObject;
            var coll = go.GetComponent<Collider>();
            if (coll != null) coll.enabled = false;
            var btn = go.GetComponentInChildren<UnityEngine.UI.Button>(true);
            if (btn != null) btn.interactable = false;

            yield return new WaitForSecondsRealtime(customerFadeDuration + 0.05f);
            Destroy(go);

            currentCustomer = null;
            currentProfile = null;
            currentCustomerDisplayName = "";
            currentRequestedRecipeName = null;

            ResolveAffinityWidget()?.Hide();
        }

        yield return new WaitForSecondsRealtime(delayBetweenCustomers);

        state = FlowState.Idle;
        Debug.Log("[MultiAgentFlow] Advancing to next customer.");
        SpawnNextCustomer();
    }

    // ── Dynamic Dialog Display ────────────────────────────────────────────────

    /// <summary>
    /// Tampilkan dialog text via InkDialogController (mode dynamic, bukan Ink story).
    /// </summary>
    private void PlayDynamicDialog(string text, bool leavePanelOpen, bool requireAck, Action onComplete)
    {
        if (inkDialogController == null)
        {
            Debug.LogWarning("[MultiAgentFlow] InkDialogController not assigned.");
            onComplete?.Invoke();
            return;
        }

        inkDialogController.SetSpeakerName(currentCustomerDisplayName);
        inkDialogController.PlayDynamicText(text, onComplete, leavePanelOpen, requireAck);
    }

    private void CloseDialogPanel()
    {
        if (inkDialogController != null)
            inkDialogController.ForceClose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string GuessUsiaFromProfile(CustomerProfile profile)
    {
        string cat = (profile.categoryName ?? "").ToLower();
        // English category names from CustomerProfile
        if (cat.Contains("kid") || cat.Contains("child") || cat.Contains("anak"))
            return "anak-anak";
        if (cat.Contains("teen") || cat.Contains("remaja"))
            return "remaja";
        if (cat.Contains("adult") || cat.Contains("dewasa"))
            return "dewasa";
        if (cat.Contains("old") || cat.Contains("elder") || cat.Contains("lansia") || cat.Contains("tua"))
            return "orang tua";
        return "dewasa";
    }

    private string GuessGenderFromProfile(CustomerProfile profile)
    {
        string cat = (profile.categoryName ?? "").ToLower();
        // English category names from CustomerProfile
        if (cat.Contains("female") || cat.Contains("wanita") || cat.Contains("girl") || cat.Contains("ibu"))
            return "wanita";
        if (cat.Contains("male") || cat.Contains("pria") || cat.Contains("boy") || cat.Contains("laki"))
            return "pria";
        // Check names as fallback
        string names = string.Join(" ", profile.possibleNames ?? new List<string>()).ToLower();
        if (names.Contains("ani") || names.Contains("siti") || names.Contains("dewi") ||
            names.Contains("ratna") || names.Contains("putri") || names.Contains("clara") || names.Contains("diana"))
            return "wanita";
        return "pria";
    }

    private float GetAffinityMultiplier()
    {
        if (currentProfile == null) return 1f;
        switch (currentProfile.GetCurrentTier())
        {
            case AffinityTier.Soulmate: return affinityScoreMultiplierSoulmate;
            case AffinityTier.BestFriend: return affinityScoreMultiplierBestFriend;
            case AffinityTier.Friend: return affinityScoreMultiplierFriend;
            default: return affinityScoreMultiplierHostile;
        }
    }

    private void HandleError(string context, string error)
    {
        Debug.LogError($"[MultiAgentFlow] {context} error: {error}");
        // Fallback: advance to next customer
        AdvanceToNextCustomer();
    }

    private void ResetAllAffinities()
    {
        if (profiles == null) return;
        foreach (var p in profiles)
            if (p != null) p.ResetAffinity();
    }

    private void OnGameRestart() => ResetAllAffinities();

    private CustomerAffinityWidget ResolveAffinityWidget()
    {
        if (affinityWidget != null) return affinityWidget;
        if (inkDialogController?.affinityWidget != null) return inkDialogController.affinityWidget;
        return null;
    }

    private void UpdateAffinityWidget(CustomerProfile profile)
    {
        if (profile == null) return;
        ResolveAffinityWidget()?.UpdateDisplay(profile.affinity, profile.GetCurrentTier());
    }

    private void AutoOpenIngredientPanel()
    {
        if (ingredientPanelController == null)
            ingredientPanelController = FindObjectOfType<SlidingPanelController>();
        ingredientPanelController?.Show();
    }

    private void AutoCloseIngredientPanel()
    {
        if (ingredientPanelController == null)
            ingredientPanelController = FindObjectOfType<SlidingPanelController>();
        ingredientPanelController?.Hide();
    }

    // ── Visual helpers (reused from CustomerManager) ──────────────────────────

    private void ApplyRandomCustomerVisuals(GameObject customerObject, CustomerProfile profile)
    {
        if (customerObject == null || profile == null) return;

        var allImages = customerObject.GetComponentsInChildren<UnityEngine.UI.Image>(true);
        if (allImages == null || allImages.Length == 0) return;

        var headSprite = profile.GetRandomHeadSprite();
        var hairSprite = profile.GetRandomHairSprite();
        var shirtSprite = profile.GetRandomShirtSprite();

        // Try to find slots by name
        UnityEngine.UI.Image headSlot = null, shirtSlot = null, hairSlot = null;
        foreach (var img in allImages)
        {
            if (img == null) continue;
            string lower = img.gameObject.name.ToLowerInvariant();
            if (headSlot == null && (lower.Contains("head") || lower.Contains("kepala"))) headSlot = img;
            if (shirtSlot == null && (lower.Contains("shirt") || lower.Contains("baju") || lower.Contains("body") || lower.Contains("cloth"))) shirtSlot = img;
            if (hairSlot == null && (lower.Contains("hair") || lower.Contains("rambut"))) hairSlot = img;
        }

        bool usedAnyPart = false;
        usedAnyPart |= SetSlotSprite(headSlot, headSprite);
        usedAnyPart |= SetSlotSprite(shirtSlot, shirtSprite);
        usedAnyPart |= SetSlotSprite(hairSlot, hairSprite);

        // Fallback: if no part sprites were assigned, use the profile portrait
        if (!usedAnyPart && profile.portrait != null)
        {
            var fallbackSlot = shirtSlot ?? hairSlot ?? headSlot;
            if (fallbackSlot != null)
                SetSlotSprite(fallbackSlot, profile.portrait);
        }
    }

    private bool SetSlotSprite(UnityEngine.UI.Image img, Sprite sprite)
    {
        if (img == null) return false;
        if (sprite == null)
        {
            img.sprite = null;
            img.color = new Color(1f, 1f, 1f, 0f);
            return false;
        }
        img.sprite = sprite;
        img.color = Color.white;
        return true;
    }
}
