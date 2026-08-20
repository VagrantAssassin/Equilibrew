using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections.Generic;

/// <summary>
/// MultiAgentBridge — HTTP client Unity ↔ Python Multi-Agent server.
/// 
/// Komunikasi via REST API ke FastAPI server di http://localhost:8765.
/// Semua request async (coroutine-based) agar tidak block main thread.
/// 
/// Cara pakai:
///   MultiAgentBridge.Instance.GenerateProfile("dewasa", "wanita", OnProfileReady);
///   MultiAgentBridge.Instance.GeneratePesanan(sessionId, OnDialogReady);
/// </summary>
public class MultiAgentBridge : MonoBehaviour
{
    public static MultiAgentBridge Instance { get; private set; }

    [Header("Server Configuration")]
    [Tooltip("URL server Python Multi-Agent")]
    public string serverUrl = "https://unfibered-noninstructional-corrin.ngrok-free.dev";

    [Tooltip("Timeout per request (detik)")]
    public int timeoutSeconds = 60;

    [Header("Debug")]
    public bool verboseLogging = true;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // No DontDestroyOnLoad — scene-bound. Bridge is recreated each scene load.
        // GameManager handles persistent progress via PlayerPrefs.
    }

    // ── Data Models ──────────────────────────────────────────────────────────

    [Serializable]
    public class ProfileResponse
    {
        public string session_id;
        public string nama;
        public string background;
        public string masalah_hari_ini;
        public OceanData ocean;
        public int max_fails;
    }

    [Serializable]
    public class OceanData
    {
        public int openness;
        public int conscientiousness;
        public int extraversion;
        public int agreeableness;
        public int neuroticism;
    }

    [Serializable]
    public class DialogResponse
    {
        public string session_id;
        public string dialog_state;
        public string dialog_text;
        public string minuman_dipesan;
        public PilihanJawaban[] pilihan_jawaban;
        public float critic_skor;
        public bool critic_lulus;
        public CriticLogEntry[] critic_log;
        public int total_ronde;
        public int ronde_sekarang;
    }

    [Serializable]
    public class CriticLogEntry
    {
        public int attempt_ke;
        public float skor;
        public CriticDimensi skor_dimensi;
        public string saran;
        public string[] catatan;
        public bool lulus;
    }

    [Serializable]
    public class CriticDimensi
    {
        public float understanding;
        public float empathy;
        public float appropriateness;
        public float engagement;
        public float creativity;
        public float coherence;
        public float naturalness;
        public float emotional_depth;
    }

    [Serializable]
    public class PilihanJawaban
    {
        public int id;
        public string teks;
        public string nada;
    }

    [Serializable]
    public class EvaluateResponse
    {
        public string session_id;
        public string nada;
        public int mood_sebelum;
        public int mood_sesudah;
        public string reaksi_npc;
        public RiwayatEntry riwayat_entry;
        public int total_ronde;
        public int ronde_sekarang;
    }

    [Serializable]
    public class RiwayatEntry
    {
        public int ronde;
        public string dialog_npc;
        public string jawaban_pemain;
        public string nada;
        public int mood_sebelum;
        public int mood_sesudah;
    }

    [Serializable]
    public class SessionSummary
    {
        public string session_id;
        public string nama;
        public string usia;
        public string gender;
        public int mood_awal;
        public int mood_akhir;
        public RiwayatEntry[] riwayat;
    }

    // ── Request Bodies ────────────────────────────────────────────────────────

    [Serializable]
    private class ProfileRequestBody
    {
        public string usia;
        public string gender;
    }

    [Serializable]
    private class PesananRequestBody
    {
        public string session_id;
        public string minuman_dipesan;
    }

    [Serializable]
    private class SessionRequestBody
    {
        public string session_id;
    }

    [Serializable]
    private class CurhatStartRequestBody
    {
        public string session_id;
        public int total_ronde;
    }

    [Serializable]
    private class CurhatRoundRequestBody
    {
        public string session_id;
        public int ronde;
    }

    [Serializable]
    private class EvaluateRequestBody
    {
        public string session_id;
        public string jawaban_pemain;
        public string chosen_nada;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Generate NPC profile baru. Callback menerima ProfileResponse + error string.
    /// </summary>
    public void GenerateProfile(string usia, string gender, Action<ProfileResponse, string> callback)
    {
        var body = new ProfileRequestBody { usia = usia, gender = gender };
        StartCoroutine(PostRequest<ProfileResponse>("/generate_profile", body, callback));
    }

    /// <summary>
    /// Generate dialog pemesanan NPC.
    /// </summary>
    public void GeneratePesanan(string sessionId, string minumanDipesan, Action<DialogResponse, string> callback)
    {
        var body = new PesananRequestBody { session_id = sessionId, minuman_dipesan = minumanDipesan };
        StartCoroutine(PostRequest<DialogResponse>("/generate_pesanan", body, callback));
    }

    /// <summary>
    /// Generate dialog reaksi pesanan salah.
    /// </summary>
    public void GenerateSalah(string sessionId, Action<DialogResponse, string> callback)
    {
        var body = new SessionRequestBody { session_id = sessionId };
        StartCoroutine(PostRequest<DialogResponse>("/generate_salah", body, callback));
    }

    /// <summary>
    /// Generate dialog NPC marah dan pergi.
    /// </summary>
    public void GenerateMarah(string sessionId, Action<DialogResponse, string> callback)
    {
        var body = new SessionRequestBody { session_id = sessionId };
        StartCoroutine(PostRequest<DialogResponse>("/generate_marah", body, callback));
    }

    /// <summary>
    /// Generate dialog NPC puas (pesanan benar).
    /// </summary>
    public void GenerateBerhasil(string sessionId, Action<DialogResponse, string> callback)
    {
        var body = new SessionRequestBody { session_id = sessionId };
        StartCoroutine(PostRequest<DialogResponse>("/generate_berhasil", body, callback));
    }

    /// <summary>
    /// Mulai sesi curhat (ronde pertama).
    /// </summary>
    public void StartCurhat(string sessionId, Action<DialogResponse, string> callback, int totalRonde = 0)
    {
        var body = new CurhatStartRequestBody { session_id = sessionId, total_ronde = totalRonde };
        StartCoroutine(PostRequest<DialogResponse>("/start_curhat", body, callback));
    }

    /// <summary>
    /// Lanjut ke ronde curhat berikutnya.
    /// </summary>
    public void NextCurhatRound(string sessionId, int ronde, Action<DialogResponse, string> callback)
    {
        var body = new CurhatRoundRequestBody { session_id = sessionId, ronde = ronde };
        StartCoroutine(PostRequest<DialogResponse>("/next_curhat_round", body, callback));
    }

    /// <summary>
    /// Evaluasi jawaban pemain → update mood + reaksi NPC.
    /// </summary>
    public void EvaluateAnswer(string sessionId, string jawaban, Action<EvaluateResponse, string> callback, string chosenNada = null)
    {
        var body = new EvaluateRequestBody
        {
            session_id = sessionId,
            jawaban_pemain = jawaban,
            chosen_nada = chosenNada
        };
        StartCoroutine(PostRequest<EvaluateResponse>("/evaluate_answer", body, callback));
    }

    /// <summary>
    /// Generate dialog penutup sesi curhat setelah semua ronde selesai.
    /// </summary>
    public void GenerateClosing(string sessionId, Action<DialogResponse, string> callback)
    {
        var body = new SessionRequestBody { session_id = sessionId };
        StartCoroutine(PostRequest<DialogResponse>("/generate_closing", body, callback));
    }

    /// <summary>
    /// Ambil ringkasan sesi.
    /// </summary>
    public void GetSessionSummary(string sessionId, Action<SessionSummary, string> callback)
    {
        StartCoroutine(GetRequest<SessionSummary>($"/session/{sessionId}/summary", callback));
    }

    /// <summary>
    /// Hapus session dari server.
    /// </summary>
    public void DeleteSession(string sessionId, Action<string> callback)
    {
        StartCoroutine(DeleteRequest($"/session/{sessionId}", callback));
    }

    /// <summary>
    /// Health check — cek apakah server hidup.
    /// </summary>
    public void HealthCheck(Action<bool, string> callback)
    {
        StartCoroutine(GetRequestRaw("/health", (result, error) =>
        {
            callback?.Invoke(error == null, error);
        }));
    }

    // ── HTTP Helpers ──────────────────────────────────────────────────────────

    private IEnumerator PostRequest<T>(string endpoint, object body, Action<T, string> callback)
    {
        string url = serverUrl + endpoint;
        string jsonBody = JsonUtility.ToJson(body);

        if (verboseLogging)
            Debug.Log($"[MultiAgentBridge] POST {url}\n  Body: {jsonBody}");

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = timeoutSeconds;

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string responseJson = request.downloadHandler.text;
                if (verboseLogging)
                    Debug.Log($"[MultiAgentBridge] Response ({endpoint}): {responseJson}");

                try
                {
                    T response = JsonUtility.FromJson<T>(responseJson);
                    callback?.Invoke(response, null);
                }
                catch (Exception e)
                {
                    string error = $"JSON parse error: {e.Message}";
                    Debug.LogError($"[MultiAgentBridge] {error}");
                    callback?.Invoke(default, error);
                }
            }
            else
            {
                string error = $"HTTP {request.responseCode}: {request.error} — {request.downloadHandler.text}";
                Debug.LogError($"[MultiAgentBridge] Request failed: {error}");
                callback?.Invoke(default, error);
            }
        }
    }

    private IEnumerator GetRequest<T>(string endpoint, Action<T, string> callback)
    {
        string url = serverUrl + endpoint;

        if (verboseLogging)
            Debug.Log($"[MultiAgentBridge] GET {url}");

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = timeoutSeconds;

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string responseJson = request.downloadHandler.text;
                try
                {
                    T response = JsonUtility.FromJson<T>(responseJson);
                    callback?.Invoke(response, null);
                }
                catch (Exception e)
                {
                    callback?.Invoke(default, $"JSON parse error: {e.Message}");
                }
            }
            else
            {
                callback?.Invoke(default, $"HTTP {request.responseCode}: {request.error}");
            }
        }
    }

    private IEnumerator GetRequestRaw(string endpoint, Action<string, string> callback)
    {
        string url = serverUrl + endpoint;

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = 10;
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
                callback?.Invoke(request.downloadHandler.text, null);
            else
                callback?.Invoke(null, $"HTTP {request.responseCode}: {request.error}");
        }
    }

    private IEnumerator DeleteRequest(string endpoint, Action<string> callback)
    {
        string url = serverUrl + endpoint;

        using (UnityWebRequest request = UnityWebRequest.Delete(url))
        {
            request.timeout = 10;
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
                callback?.Invoke(null);
            else
                callback?.Invoke($"HTTP {request.responseCode}: {request.error}");
        }
    }
}
