    using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ProfileResponse = MultiAgentBridge.ProfileResponse;
using DialogResponse = MultiAgentBridge.DialogResponse;
using EvaluateResponse = MultiAgentBridge.EvaluateResponse;
using SessionSummary = MultiAgentBridge.SessionSummary;
using PilihanJawaban = MultiAgentBridge.PilihanJawaban;

/// <summary>
/// MultiAgentCustomerAdapter — menghubungkan CustomerManager dengan MultiAgentBridge.
/// 
/// Menggantikan alur Ink stories dengan dialog yang di-generate LLM.
/// Dipasang di scene bersama CustomerManager.
/// 
/// Alur:
///   1. CustomerManager spawn customer → adapter.GenerateProfile() → Profile Agent
///   2. Adapter.GenerateOrderDialog() → Dialogue Agent (pesanan)
///   3. Player serve salah → adapter.GenerateWrongDialog() → Dialogue Agent (pesanan_salah)
///   4. Player serve benar → adapter.GenerateSuccessDialog() → Dialogue Agent (berhasil)
///   5. Sesi curhat → adapter.StartCurhat() → Dialogue Agent (curhat) multi-ronde
///   6. Player jawab → adapter.EvaluateAnswer() → mood update + reaksi
///   7. Max fail → adapter.GenerateAngryDialog() → Dialogue Agent (marah)
/// </summary>
public class MultiAgentCustomerAdapter : MonoBehaviour
{
    public static MultiAgentCustomerAdapter Instance { get; private set; }

    [Header("References")]
    [Tooltip("InkDialogController di scene untuk menampilkan dialog")]
    public InkDialogController inkDialogController;

    [Tooltip("CustomerManager di scene")]
    public CustomerManager customerManager;

    [Header("Curhat Settings")]
    [Tooltip("Jumlah ronde curhat. 0 = random dari Python (3-5)")]
    public int fixedCurhatRounds = 0;

    [Header("Debug")]
    public bool verboseLogging = true;

    // Runtime state
    private string currentSessionId = null;
    private ProfileResponse currentProfile = null;
    private DialogResponse currentDialog = null;
    private int currentCurhatRound = 0;
    private int totalCurhatRounds = 0;
    private bool isGenerating = false;

    // Callbacks
    private Action<ProfileResponse> onProfileReady;
    private Action<DialogResponse> onDialogReady;
    private Action<EvaluateResponse> onEvaluateReady;
    private Action<string> onError;

    public string SessionId => currentSessionId;
    public ProfileResponse CurrentProfile => currentProfile;
    public bool IsGenerating => isGenerating;
    public int CurrentCurhatRound => currentCurhatRound;
    public int TotalCurhatRounds => totalCurhatRounds;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Generate NPC profile baru dari Multi-Agent system.
    /// </summary>
    public void GenerateProfile(string usia, string gender, Action<ProfileResponse> onSuccess, Action<string> onErr = null)
    {
        if (isGenerating)
        {
            onErr?.Invoke("Already generating. Wait for current request.");
            return;
        }

        isGenerating = true;
        onProfileReady = onSuccess;
        onError = onErr;

        if (verboseLogging)
            Debug.Log($"[Adapter] Generating profile: usia={usia}, gender={gender}");

        MultiAgentBridge.Instance.GenerateProfile(usia, gender, (response, error) =>
        {
            isGenerating = false;
            if (error != null)
            {
                Debug.LogError($"[Adapter] Profile error: {error}");
                onError?.Invoke(error);
                return;
            }

            currentSessionId = response.session_id;
            currentProfile = response;
            currentCurhatRound = 0;

            if (verboseLogging)
                Debug.Log($"[Adapter] Profile ready: {response.nama} (session={response.session_id})");

            onProfileReady?.Invoke(response);
        });
    }

    /// <summary>
    /// Generate dialog pemesanan NPC.
    /// </summary>
    public void GenerateOrderDialog(string minumanDipesan, Action<DialogResponse> onSuccess, Action<string> onErr = null)
    {
        if (!CheckSession(onErr)) return;

        isGenerating = true;
        onDialogReady = onSuccess;
        onError = onErr;

        if (verboseLogging)
            Debug.Log($"[Adapter] Generating order dialog for session {currentSessionId}, minuman={minumanDipesan}");

        MultiAgentBridge.Instance.GeneratePesanan(currentSessionId, minumanDipesan, (response, error) =>
        {
            isGenerating = false;
            HandleDialogResponse(response, error, onSuccess, onErr);
        });
    }

    /// <summary>
    /// Generate dialog reaksi pesanan salah.
    /// </summary>
    public void GenerateWrongDialog(Action<DialogResponse> onSuccess, Action<string> onErr = null)
    {
        if (!CheckSession(onErr)) return;

        isGenerating = true;
        onDialogReady = onSuccess;
        onError = onErr;

        MultiAgentBridge.Instance.GenerateSalah(currentSessionId, (response, error) =>
        {
            isGenerating = false;
            HandleDialogResponse(response, error, onSuccess, onErr);
        });
    }

    /// <summary>
    /// Generate dialog NPC marah dan pergi (max fail reached).
    /// </summary>
    public void GenerateAngryDialog(Action<DialogResponse> onSuccess, Action<string> onErr = null)
    {
        if (!CheckSession(onErr)) return;

        isGenerating = true;
        onDialogReady = onSuccess;
        onError = onErr;

        MultiAgentBridge.Instance.GenerateMarah(currentSessionId, (response, error) =>
        {
            isGenerating = false;
            HandleDialogResponse(response, error, onSuccess, onErr);
        });
    }

    /// <summary>
    /// Generate dialog NPC puas (pesanan benar).
    /// </summary>
    public void GenerateSuccessDialog(Action<DialogResponse> onSuccess, Action<string> onErr = null)
    {
        if (!CheckSession(onErr)) return;

        isGenerating = true;
        onDialogReady = onSuccess;
        onError = onErr;

        MultiAgentBridge.Instance.GenerateBerhasil(currentSessionId, (response, error) =>
        {
            isGenerating = false;
            HandleDialogResponse(response, error, onSuccess, onErr);
        });
    }

    /// <summary>
    /// Mulai sesi curhat (ronde pertama).
    /// </summary>
    public void StartCurhat(Action<DialogResponse> onSuccess, Action<string> onErr = null)
    {
        if (!CheckSession(onErr)) return;

        isGenerating = true;
        onDialogReady = onSuccess;
        onError = onErr;

        currentCurhatRound = 1;

        MultiAgentBridge.Instance.StartCurhat(currentSessionId, (response, error) =>
        {
            isGenerating = false;
            if (error == null && response != null)
            {
                totalCurhatRounds = response.total_ronde > 0 ? response.total_ronde : 3;
                if (verboseLogging)
                    Debug.Log($"[Adapter] Curhat started: total_ronde={totalCurhatRounds}");
            }
            HandleDialogResponse(response, error, onSuccess, onErr);
        }, fixedCurhatRounds);
    }

    /// <summary>
    /// Lanjut ke ronde curhat berikutnya.
    /// </summary>
    public void NextCurhatRound(Action<DialogResponse> onSuccess, Action<string> onErr = null)
    {
        if (!CheckSession(onErr)) return;

        isGenerating = true;
        onDialogReady = onSuccess;
        onError = onErr;

        currentCurhatRound++;

        MultiAgentBridge.Instance.NextCurhatRound(currentSessionId, currentCurhatRound, (response, error) =>
        {
            isGenerating = false;
            if (error == null && response != null && response.total_ronde > 0)
                totalCurhatRounds = response.total_ronde;
            HandleDialogResponse(response, error, onSuccess, onErr);
        });
    }

    /// <summary>
    /// Evaluasi jawaban pemain → update mood + reaksi NPC.
    /// </summary>
    public void EvaluateAnswer(string jawaban, Action<EvaluateResponse> onSuccess, Action<string> onErr = null, string chosenNada = null)
    {
        if (!CheckSession(onErr)) return;

        isGenerating = true;
        onEvaluateReady = onSuccess;
        onError = onErr;

        MultiAgentBridge.Instance.EvaluateAnswer(currentSessionId, jawaban, (response, error) =>
        {
            isGenerating = false;
            if (error != null)
            {
                Debug.LogError($"[Adapter] Evaluate error: {error}");
                onErr?.Invoke(error);
                return;
            }

            if (verboseLogging)
                Debug.Log($"[Adapter] Evaluate: nada={response.nada}, mood={response.mood_sebelum}→{response.mood_sesudah}");

            onEvaluateReady?.Invoke(response);
        }, chosenNada);
    }

    /// <summary>
    /// Generate dialog penutup sesi curhat setelah semua ronde selesai.
    /// </summary>
    public void GenerateClosing(Action<DialogResponse> onSuccess, Action<string> onErr = null)
    {
        if (!CheckSession(onErr)) return;

        isGenerating = true;
        onDialogReady = onSuccess;
        onError = onErr;

        MultiAgentBridge.Instance.GenerateClosing(currentSessionId, (response, error) =>
        {
            isGenerating = false;
            if (error != null)
            {
                Debug.LogError($"[Adapter] Closing error: {error}");
                onErr?.Invoke(error);
                return;
            }

            if (verboseLogging)
                Debug.Log($"[Adapter] Closing: {response.dialog_text?.Substring(0, System.Math.Min(50, response.dialog_text?.Length ?? 0))}...");

            onDialogReady?.Invoke(response);
        });
    }

    /// <summary>
    /// Hapus session dari server.
    /// </summary>
    public void ClearSession()
    {
        if (currentSessionId != null)
        {
            MultiAgentBridge.Instance.DeleteSession(currentSessionId, (error) =>
            {
                if (error != null)
                    Debug.LogWarning($"[Adapter] Delete session error: {error}");
            });
        }
        currentSessionId = null;
        currentProfile = null;
        currentDialog = null;
        currentCurhatRound = 0;
        totalCurhatRounds = 0;
    }

    /// <summary>
    /// Cek apakah server hidup.
    /// </summary>
    public void CheckServerHealth(Action<bool> callback)
    {
        MultiAgentBridge.Instance.HealthCheck((ok, error) =>
        {
            if (!ok)
                Debug.LogWarning($"[Adapter] Server health check failed: {error}");
            callback?.Invoke(ok);
        });
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private bool CheckSession(Action<string> onErr)
    {
        if (currentSessionId == null)
        {
            onErr?.Invoke("No active session. Generate profile first.");
            return false;
        }
        return true;
    }

    private void HandleDialogResponse(DialogResponse response, string error, Action<DialogResponse> onSuccess, Action<string> onErr)
    {
        if (error != null)
        {
            Debug.LogError($"[Adapter] Dialog error: {error}");
            onErr?.Invoke(error);
            return;
        }

        currentDialog = response;

        if (verboseLogging)
            Debug.Log($"[Adapter] Dialog ready: state={response.dialog_state}, critic={response.critic_skor:F1}/5.0, lulus={response.critic_lulus}");

        // Log detail critic per-attempt
        LogCriticHistory(response);

        onSuccess?.Invoke(response);
    }

    /// <summary>
    /// Log detail critic per attempt ke Unity console.
    /// </summary>
    private void LogCriticHistory(DialogResponse response)
    {
        if (response.critic_log == null || response.critic_log.Length == 0)
        {
            if (verboseLogging)
                Debug.Log($"[Adapter] Critic: no log (closing atau non-critic state)");
            return;
        }

        for (int i = 0; i < response.critic_log.Length; i++)
        {
            var entry = response.critic_log[i];
            string status = entry.lulus ? "✅" : "❌";
            Debug.Log($"[Adapter] Critic attempt {entry.attempt_ke}: {status} skor={entry.skor:F1}/5.0");

            if (entry.catatan != null && entry.catatan.Length > 0)
            {
                foreach (var catatan in entry.catatan)
                    Debug.Log($"[Adapter]   📝 {catatan}");
            }

            if (!string.IsNullOrEmpty(entry.saran))
                Debug.Log($"[Adapter]   💡 Saran: {entry.saran}");
        }
    }
}
