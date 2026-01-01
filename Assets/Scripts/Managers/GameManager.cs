// (isi file — overwrite existing GameManager.cs)
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

    private int score = 0;
    private int hp = 0;

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
        score = startingScore;
        hp = Mathf.Clamp(maxHP, 0, 999);
        UpdateHearts();
        UpdateScoreText();
    }

    public void AddScore(int points)
    {
        if (points == 0) return;
        score += points;
        UpdateScoreText();
        OnScoreChanged?.Invoke(score, points);
    }

    public int GetScore() => score;
    public int GetHP() => hp;

    public void DecreaseHP(int count = 1, string reason = "")
    {
        if (count <= 0) return;
        hp = Mathf.Max(0, hp - count);
        UpdateHearts();
        OnHPChanged?.Invoke(hp, -count);
        Debug.Log($"[GameManager] DecreaseHP by {count} reason={reason} -> hp={hp}");
        if (hp <= 0) TriggerGameOver();
    }

    public void IncreaseHP(int count = 1)
    {
        if (count <= 0) return;
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
        SaveHighscoreIfNeeded();
        ShowGameOverPanel();
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

    public void RestartGame()
    {
        if (reloadSceneOnRestart)
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            return;
        }

        HideGameOverPanel();
        InitGame();
        OnGameRestartEvent?.Invoke();
    }

    public void ClearSavedHighscore()
    {
        PlayerPrefs.DeleteKey(highscoreKey);
        PlayerPrefs.Save();
    }
}