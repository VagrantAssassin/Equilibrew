using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// GameManager (singleton) untuk endless mode: menyimpan defaults global.
/// Per-profile values harus diletakkan di CustomerProfile (preferred).
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Gameplay")]
    public int maxHP = 3;
    public int startingScore = 0;
    public int pointsPerCorrectServe = 10;

    [Header("Global defaults for dialog reaction (fallbacks)")]
    [Tooltip("Default points when curhat yields 'satisfy' and profile doesn't override.")]
    public int pointsPerSatisfyDefault = 5;
    [Tooltip("Default points when curhat yields 'neutral' and profile doesn't override.")]
    public int pointsPerNeutralDefault = 0;
    [Tooltip("Default HP loss when curhat yields 'angry' and profile doesn't override.")]
    public int hpLossOnAngryDefault = 1;

    [Header("UI (assign in Inspector)")]
    public TextMeshProUGUI scoreText;
    public Transform heartsParent;
    public GameObject gameOverPanel;
    public TextMeshProUGUI gameOverScoreText;
    public TextMeshProUGUI gameOverBestText;

    [Header("Highscore key")]
    public string highscoreKey = "EQ_HIGH_SCORE";

    [Header("Restart behavior")]
    public bool reloadSceneOnRestart = false;

    // runtime
    private int score = 0;
    private int hp = 0;

    // Game over state flag
    private bool isGameOver = false;

    // Events (only GameManager may invoke them)
    public event Action OnGameOverEvent;
    public event Action OnGameRestartEvent;
    public event Action<int, int> OnScoreChanged;
    public event Action<int, int> OnHPChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        InitGame();
        UpdateUI();
        HideGameOverPanel();
    }

    public void InitGame()
    {
        // restore time/audio and clear game-over flag
        Time.timeScale = 1f;
        AudioListener.pause = false;
        isGameOver = false;

        score = startingScore;
        hp = Mathf.Clamp(maxHP, 0, 999);
        UpdateHearts();
        UpdateScoreText();
    }

    // AddScore with optional reason (for logging)
    public void AddScore(int points, string reason = null)
    {
        if (points == 0 || isGameOver) return;
        int prev = score;
        score += points;
        UpdateScoreText();
        OnScoreChanged?.Invoke(score, points);
        Debug.Log($"[GameManager] AddScore: {points} (reason={reason ?? "none"}) -> {prev} -> {score}");
    }

    public int GetScore() => score;
    public int GetHP() => hp;

    public void DecreaseHP(int count = 1, string reason = "")
    {
        if (count <= 0 || isGameOver) return;
        int prev = hp;
        hp = Mathf.Max(0, hp - count);
        UpdateHearts();
        OnHPChanged?.Invoke(hp, -count);
        Debug.Log($"[GameManager] DecreaseHP by {count} reason={reason} -> {prev} -> {hp}");

        if (hp <= 0) TriggerGameOver();
    }

    public void IncreaseHP(int count = 1)
    {
        if (count <= 0 || isGameOver) return;
        int prev = hp;
        hp = Mathf.Min(maxHP, hp + count);
        UpdateHearts();
        OnHPChanged?.Invoke(hp, count);
    }

    private void UpdateScoreText()
    {
        if (scoreText != null) scoreText.text = $"Score: {score}";
    }

    private void UpdateHearts()
    {
        if (heartsParent == null) return;
        int n = heartsParent.childCount;
        for (int i = 0; i < n; i++)
        {
            var go = heartsParent.GetChild(i).gameObject;
            if (go != null) go.SetActive(i < hp);
        }
    }

    private void UpdateUI()
    {
        UpdateScoreText();
        UpdateHearts();
    }

    private void TriggerGameOver()
    {
        if (isGameOver) return;
        isGameOver = true;

        Debug.Log("[GameManager] GameOver triggered.");
        SaveHighscoreIfNeeded();
        ShowGameOverPanel();

        // freeze game time and pause audio when game over
        Time.timeScale = 0f;
        AudioListener.pause = true;

        OnGameOverEvent?.Invoke();
    }

    private void ShowGameOverPanel()
    {
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
            if (gameOverScoreText != null) gameOverScoreText.text = $"Score: {score}";
            int best = PlayerPrefs.GetInt(highscoreKey, 0);
            if (gameOverBestText != null) gameOverBestText.text = $"Best: {best}";
        }
    }

    private void HideGameOverPanel()
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    private void SaveHighscoreIfNeeded()
    {
        int prev = PlayerPrefs.GetInt(highscoreKey, 0);
        if (score > prev) { PlayerPrefs.SetInt(highscoreKey, score); PlayerPrefs.Save(); }
    }

    /// <summary>
    /// Restart game: either reload scene (full reset) or reset internal state and fire OnGameRestartEvent.
    /// </summary>
    public void RestartGame()
    {
        // restore time/audio and clear game over state
        Time.timeScale = 1f;
        AudioListener.pause = false;
        isGameOver = false;

        if (reloadSceneOnRestart)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            return;
        }

        HideGameOverPanel();
        InitGame();
        OnGameRestartEvent?.Invoke();
        Debug.Log("[GameManager] RestartGame: reset internal state and fired OnGameRestartEvent.");
    }

    /// <summary>
    /// Public helper so external code can request the restart event to be fired.
    /// Use this instead of trying to invoke the event from outside.
    /// </summary>
    public void FireOnGameRestart()
    {
        // keep behavior consistent: restore time/audio first
        Time.timeScale = 1f;
        AudioListener.pause = false;
        isGameOver = false;

        OnGameRestartEvent?.Invoke();
    }

    public void ClearSavedHighscore()
    {
        PlayerPrefs.DeleteKey(highscoreKey);
        PlayerPrefs.Save();
    }
}