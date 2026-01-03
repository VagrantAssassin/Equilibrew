using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

/// <summary>
/// GameManager
/// - Manages score, HP and game over state.
/// - Exposes events for score/hp changes and game lifecycle.
/// - Updated: heart UI now supports sprite-swap mode (keep hearts visible and swap sprite instead of enabling/disabling).
/// - Added: public RestartGame() to be used by UI Retry button.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Gameplay")]
    public int startingScore = 0;
    public int maxHP = 3;
    public int pointsPerCorrectServe = 5;

    public int pointsPerSatisfyDefault = 5;
    public int pointsPerNeutralDefault = 0;
    [Tooltip("Default HP loss when curhat yields 'angry' and profile doesn't override.")]
    public int hpLossOnAngryDefault = 1;

    [Header("UI (assign in Inspector)")]
    public TextMeshProUGUI scoreText;
    public Transform heartsParent;
    public GameObject gameOverPanel;
    public TextMeshProUGUI gameOverScoreText;
    public TextMeshProUGUI gameOverBestText;

    [Header("Hearts - sprite swap (optional)")]
    [Tooltip("Sprite to show when a heart slot is full (HP present).")]
    public Sprite heartFullSprite;
    [Tooltip("Sprite to show when a heart slot is lost/damaged (HP absent).")]
    public Sprite heartLostSprite;
    [Tooltip("If true, GameManager will swap sprites on Image components under heartsParent instead of enabling/disabling GameObjects.")]
    public bool useHeartSpriteSwap = true;
    [Tooltip("If true and using sprite swap, call SetNativeSize() on Image after sprite assignment.")]
    public bool setHeartImageNativeSize = false;

    [Header("Highscore key")]
    public string highscoreKey = "EQ_HIGH_SCORE";

    [Header("Restart behavior")]
    [Tooltip("If true, RestartGame will reload the active scene. If false, RestartGame will InitGame() and invoke restart event.")]
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

    /// <summary>
    /// UpdateHearts:
    /// - If useHeartSpriteSwap==true and child has Image, swap sprite to full/lost accordingly (keeps the image visible).
    /// - Otherwise fallback to legacy behaviour (enable/disable child GameObject).
    /// </summary>
    private void UpdateHearts()
    {
        if (heartsParent == null) return;
        int n = heartsParent.childCount;
        for (int i = 0; i < n; i++)
        {
            var child = heartsParent.GetChild(i).gameObject;
            if (child == null) continue;

            if (useHeartSpriteSwap)
            {
                var img = child.GetComponent<Image>();
                if (img != null)
                {
                    // swap sprite according to hp
                    if (i < hp)
                    {
                        if (heartFullSprite != null) img.sprite = heartFullSprite;
                    }
                    else
                    {
                        if (heartLostSprite != null) img.sprite = heartLostSprite;
                    }
                    img.enabled = true;
                    if (setHeartImageNativeSize && img.sprite != null) img.SetNativeSize();
                }
                else
                {
                    // fallback: if there's no Image component, keep legacy behaviour
                    child.SetActive(i < hp);
                }
            }
            else
            {
                // legacy behaviour: enable/disable whole GameObject
                child.SetActive(i < hp);
            }
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
            if (gameOverBestText != null)
            {
                int best = PlayerPrefs.GetInt(highscoreKey, 0);
                gameOverBestText.text = $"Best: {best}";
            }
        }
    }

    private void HideGameOverPanel()
    {
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(false);
        }
    }

    private void SaveHighscoreIfNeeded()
    {
        int best = PlayerPrefs.GetInt(highscoreKey, 0);
        if (score > best)
        {
            PlayerPrefs.SetInt(highscoreKey, score);
            PlayerPrefs.Save();
            Debug.Log($"[GameManager] New highscore saved: {score}");
        }
    }

    /// <summary>
    /// Fire game restart event so other systems (UI, managers) can re-init or reset.
    /// Note: FireOnGameRestart will also reload scene if reloadSceneOnRestart = true.
    /// </summary>
    public void FireOnGameRestart()
    {
        Debug.Log("[GameManager] FireOnGameRestart invoked.");
        OnGameRestartEvent?.Invoke();

        if (reloadSceneOnRestart)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }

    /// <summary>
    /// Public restart method for UI Retry button.
    /// - If reloadSceneOnRestart==true -> reload active scene.
    /// - Else -> hide game over panel, call InitGame() and invoke OnGameRestartEvent.
    /// Attach this method to Retry button's OnClick in the Inspector.
    /// </summary>
    public void RestartGame()
    {
        Debug.Log("[GameManager] RestartGame called by UI.");

        if (reloadSceneOnRestart)
        {
            // will reload the scene (and all managers will re-awake)
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            return;
        }

        // Otherwise perform in-place re-init
        HideGameOverPanel();
        InitGame();
        OnGameRestartEvent?.Invoke();
    }

    /// <summary>
    /// Convenience alias for UI, if you prefer more explicit name in Inspector.
    /// </summary>
    public void RestartGame_FromButton()
    {
        RestartGame();
    }

    /// <summary>
    /// Fire game restart programmatically (alias kept for compatibility).
    /// </summary>
    public void FireOnGameRestart_Public()
    {
        FireOnGameRestart();
    }
}