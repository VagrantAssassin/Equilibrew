using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// GameManager
/// - Manages score and day-based cumulative target.
/// - Keeps persistent progression data (currency, highscore, longest day, power-up placeholders).
/// - No HP / max HP mechanic.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Gameplay")]
    public int startingScore = 0;
    public int pointsPerCorrectServe = 10;
    public int pointsPerSatisfyDefault = 5;
    public int pointsPerNeutralDefault = 0;

    [Header("Daily Target Formula")]
    [Tooltip("Base target points contributed by each customer.")]
    public int targetPerCustomer = 5;
    [Tooltip("Formula factor uses integer division: (1 + day/dayGrowthDivider).")]
    public int dayGrowthDivider = 10;
    [Tooltip("Currency gained for every this many score points.")]
    public int scoreToCurrencyDivider = 10;

    [Header("Persistent Progress")]
    public GameProgressData progressData;

    [Header("UI (assign in Inspector)")]
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI dayText;
    public TextMeshProUGUI targetScoreText;
    public TextMeshProUGUI currencyText;
    public GameObject gameOverPanel;
    public TextMeshProUGUI gameOverScoreText;
    public TextMeshProUGUI gameOverBestText;

    [Header("Day Transition UI (optional)")]
    public GameObject dayTransitionPanel;
    public TextMeshProUGUI dayTransitionDayText;
    public TextMeshProUGUI dayTransitionTargetText;
    public Button dayTransitionContinueButton;

    [Header("Restart behavior")]
    [Tooltip("If true, RestartGame will reload the active scene. If false, RestartGame will InitGame() and invoke restart event.")]
    public bool reloadSceneOnRestart = false;

    private int score = 0;
    private int currentDay = 0;
    private int cumulativeTargetScore = 0;
    private int scoreCurrencyConverted = 0;
    private bool isGameOver = false;
    private Coroutine dayTransitionCoroutine;
    private bool dayTransitionContinueRequested = false;

    public event Action OnGameOverEvent;
    public event Action OnGameRestartEvent;
    public event Action<int, int> OnScoreChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        EnsureProgressData();

        if (dayTransitionContinueButton != null)
        {
            dayTransitionContinueButton.onClick.RemoveListener(OnDayTransitionContinuePressed);
            dayTransitionContinueButton.onClick.AddListener(OnDayTransitionContinuePressed);
        }
    }

    private void OnDestroy()
    {
        if (dayTransitionContinueButton != null)
            dayTransitionContinueButton.onClick.RemoveListener(OnDayTransitionContinuePressed);
    }

    private void Start()
    {
        progressData.Load();
        InitGame();
        UpdateUI();
        HideGameOverPanel();
        HideDayTransitionPanel();
    }

    public void InitGame()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        isGameOver = false;

        score = startingScore;
        currentDay = 0;
        cumulativeTargetScore = 0;
        scoreCurrencyConverted = 0;

        UpdateUI();
    }

    public void AddScore(int points, string reason = null)
    {
        if (points == 0 || isGameOver) return;
        int prev = score;
        score += points;

        SaveHighscoreIfNeeded();
        ConvertScoreToCurrency();

        UpdateScoreText();
        UpdateCurrencyText();
        OnScoreChanged?.Invoke(score, points);
        Debug.Log($"[GameManager] AddScore: {points} (reason={reason ?? "none"}) -> {prev} -> {score}");
    }

    public int GetScore() => score;
    public int GetCurrentDay() => currentDay;
    public int GetTargetScore() => cumulativeTargetScore;
    public int GetCurrency() => progressData != null ? progressData.Currency : 0;
    public int GetHighScore() => progressData != null ? progressData.HighScore : 0;
    public int GetLongestDay() => progressData != null ? progressData.LongestDay : 0;

    /// <summary>
    /// Called by CustomerManager when a new day starts and customer count is known.
    /// Formula: customers * targetPerCustomer * (1 + day/dayGrowthDivider) using integer division.
    /// </summary>
    public void BeginNewDay(int customerCount)
    {
        if (isGameOver) return;
        if (EvaluateEndOfDayAndTriggerGameOver()) return;
        if (customerCount <= 0)
        {
            Debug.LogWarning("[GameManager] BeginNewDay called with customerCount <= 0. Day start ignored.");
            return;
        }

        currentDay += 1;
        int safeDivider = Mathf.Max(1, dayGrowthDivider);
        int multiplier = 1 + (currentDay / safeDivider);
        int dayTargetIncrease = Mathf.Max(0, customerCount * targetPerCustomer * multiplier);
        cumulativeTargetScore += dayTargetIncrease;

        if (progressData != null)
        {
            progressData.SetLongestDayIfHigher(currentDay);
            progressData.Save();
        }

        UpdateDayText();
        UpdateTargetText();
        Debug.Log($"[GameManager] Day {currentDay} started. Customers={customerCount} DailyTarget+={dayTargetIncrease} CumulativeTarget={cumulativeTargetScore}");
    }

    public void ShowDayTransition(float durationSeconds)
    {
        if (dayTransitionPanel == null) return;
        if (dayTransitionCoroutine != null) StopCoroutine(dayTransitionCoroutine);
        dayTransitionCoroutine = StartCoroutine(ShowDayTransitionCoroutine(durationSeconds));
    }

    private IEnumerator ShowDayTransitionCoroutine(float durationSeconds)
    {
        dayTransitionPanel.SetActive(true);
        if (dayTransitionDayText != null) dayTransitionDayText.text = $"Day {Mathf.Max(1, currentDay)}";
        if (dayTransitionTargetText != null) dayTransitionTargetText.text = $"Target: {cumulativeTargetScore}";
        dayTransitionContinueRequested = false;

        if (dayTransitionContinueButton != null)
        {
            dayTransitionContinueButton.gameObject.SetActive(true);
            dayTransitionContinueButton.interactable = false;
        }

        float wait = Mathf.Max(0f, durationSeconds);
        if (wait > 0f)
            yield return new WaitForSecondsRealtime(wait);

        if (dayTransitionContinueButton != null)
        {
            dayTransitionContinueButton.interactable = true;
            while (!dayTransitionContinueRequested)
            {
                if (dayTransitionContinueButton == null) break;
                yield return null;
            }
        }

        HideDayTransitionPanel();
        dayTransitionCoroutine = null;
    }

    public bool IsDayTransitionVisible()
    {
        return dayTransitionPanel != null && dayTransitionPanel.activeInHierarchy;
    }

    public void OnDayTransitionContinuePressed()
    {
        dayTransitionContinueRequested = true;
    }

    private void HideDayTransitionPanel()
    {
        if (dayTransitionPanel != null)
            dayTransitionPanel.SetActive(false);

        if (dayTransitionContinueButton != null)
        {
            dayTransitionContinueButton.interactable = false;
            dayTransitionContinueButton.gameObject.SetActive(false);
        }
    }

    private void ConvertScoreToCurrency()
    {
        if (progressData == null) return;

        int safeDivider = Mathf.Max(1, scoreToCurrencyDivider);
        int totalConvertible = Mathf.Max(0, score / safeDivider);
        int delta = totalConvertible - scoreCurrencyConverted;
        if (delta <= 0) return;

        progressData.AddCurrency(delta);
        progressData.Save();
        scoreCurrencyConverted = totalConvertible;
    }

    private void UpdateScoreText()
    {
        if (scoreText != null) scoreText.text = $"Score: {score}";
    }

    private void UpdateDayText()
    {
        if (dayText != null)
            dayText.text = currentDay <= 0 ? "Day: -" : $"Day: {currentDay}";
    }

    private void UpdateTargetText()
    {
        if (targetScoreText != null) targetScoreText.text = $"Target: {cumulativeTargetScore}";
    }

    private void UpdateCurrencyText()
    {
        if (currencyText != null && progressData != null)
            currencyText.text = $"Currency: {progressData.Currency}";
    }

    private void UpdateUI()
    {
        UpdateScoreText();
        UpdateDayText();
        UpdateTargetText();
        UpdateCurrencyText();
    }

    public bool EvaluateEndOfDayAndTriggerGameOver()
    {
        if (isGameOver) return true;
        if (currentDay <= 0) return false;
        if (score >= cumulativeTargetScore) return false;

        Debug.LogWarning($"[GameManager] End-of-day check failed. Score={score} Target={cumulativeTargetScore}. Triggering GameOver.");
        TriggerGameOver();
        return true;
    }

    private void TriggerGameOver()
    {
        if (isGameOver) return;
        isGameOver = true;

        Debug.Log("[GameManager] GameOver triggered.");
        SaveHighscoreIfNeeded();
        ShowGameOverPanel();

        Time.timeScale = 0f;
        AudioListener.pause = true;
        OnGameOverEvent?.Invoke();
    }

    private void ShowGameOverPanel()
    {
        if (gameOverPanel == null) return;

        gameOverPanel.SetActive(true);
        if (gameOverScoreText != null) gameOverScoreText.text = $"Score: {score}";
        if (gameOverBestText != null) gameOverBestText.text = $"Best: {GetHighScore()}";
    }

    private void HideGameOverPanel()
    {
        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);
    }

    private void SaveHighscoreIfNeeded()
    {
        if (progressData == null) return;
        int before = progressData.HighScore;
        progressData.SetHighScoreIfHigher(score);
        if (progressData.HighScore != before)
        {
            progressData.Save();
            Debug.Log($"[GameManager] New highscore saved: {progressData.HighScore}");
        }
    }

    private void EnsureProgressData()
    {
        if (progressData != null) return;
        progressData = ScriptableObject.CreateInstance<GameProgressData>();
        progressData.name = "RuntimeGameProgressData";
        Debug.LogWarning("[GameManager] progressData is not assigned in Inspector. Using runtime fallback instance.");
    }

    // Legacy compatibility API (no-op due to HP removal)
    public int GetHP() => 0;
    public void DecreaseHP(int count = 1, string reason = "")
    {
        Debug.Log($"[GameManager] DecreaseHP ignored (HP mechanic removed). count={count}, reason={reason}");
    }
    public void IncreaseHP(int count = 1)
    {
        Debug.Log($"[GameManager] IncreaseHP ignored (HP mechanic removed). count={count}");
    }

    public void FireOnGameRestart()
    {
        Debug.Log("[GameManager] FireOnGameRestart invoked.");
        OnGameRestartEvent?.Invoke();

        if (reloadSceneOnRestart)
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void RestartGame()
    {
        Debug.Log("[GameManager] RestartGame called by UI.");

        if (reloadSceneOnRestart)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            return;
        }

        HideGameOverPanel();
        HideDayTransitionPanel();
        InitGame();
        OnGameRestartEvent?.Invoke();
    }

    public void RestartGame_FromButton()
    {
        RestartGame();
    }

    public void FireOnGameRestart_Public()
    {
        FireOnGameRestart();
    }
}
