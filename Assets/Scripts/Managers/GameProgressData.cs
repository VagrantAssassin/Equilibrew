using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class PowerUpUpgradeState
{
    public string powerUpId;
    public int level;
}

[CreateAssetMenu(fileName = "GameProgressData", menuName = "Equilibrew/GameProgressData", order = 10)]
public class GameProgressData : ScriptableObject
{
    [SerializeField] private int currency;
    [SerializeField] private int highScore;
    [SerializeField] private int longestDay;
    [SerializeField] private List<PowerUpUpgradeState> powerUpUpgrades = new List<PowerUpUpgradeState>();

    private const string CurrencyKey = "EQ_PROGRESS_CURRENCY";
    private const string HighScoreKey = "EQ_PROGRESS_HIGHSCORE";
    private const string LongestDayKey = "EQ_PROGRESS_LONGEST_DAY";
    private const string PowerUpPrefix = "EQ_PROGRESS_POWERUP_";

    public int Currency => currency;
    public int HighScore => highScore;
    public int LongestDay => longestDay;
    public IReadOnlyList<PowerUpUpgradeState> PowerUpUpgrades => powerUpUpgrades;

    public void Load()
    {
        currency = PlayerPrefs.GetInt(CurrencyKey, 0);
        highScore = PlayerPrefs.GetInt(HighScoreKey, 0);
        longestDay = PlayerPrefs.GetInt(LongestDayKey, 0);

        if (powerUpUpgrades == null) powerUpUpgrades = new List<PowerUpUpgradeState>();
        foreach (var entry in powerUpUpgrades)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.powerUpId)) continue;
            entry.level = PlayerPrefs.GetInt(PowerUpPrefix + entry.powerUpId, entry.level);
        }
    }

    public void Save()
    {
        PlayerPrefs.SetInt(CurrencyKey, currency);
        PlayerPrefs.SetInt(HighScoreKey, highScore);
        PlayerPrefs.SetInt(LongestDayKey, longestDay);

        if (powerUpUpgrades != null)
        {
            foreach (var entry in powerUpUpgrades)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.powerUpId)) continue;
                PlayerPrefs.SetInt(PowerUpPrefix + entry.powerUpId, Mathf.Max(0, entry.level));
            }
        }

        PlayerPrefs.Save();
    }

    public void AddCurrency(int amount)
    {
        if (amount <= 0) return;
        currency += amount;
    }

    public bool TrySpendCurrency(int amount)
    {
        if (amount <= 0) return false;
        if (currency < amount) return false;
        currency -= amount;
        return true;
    }

    public void SetHighScoreIfHigher(int score)
    {
        if (score > highScore)
            highScore = score;
    }

    public void SetLongestDayIfHigher(int day)
    {
        if (day > longestDay)
            longestDay = day;
    }

    public int GetPowerUpLevel(string powerUpId)
    {
        if (string.IsNullOrWhiteSpace(powerUpId) || powerUpUpgrades == null) return 0;
        var found = powerUpUpgrades.Find(x => x != null && string.Equals(x.powerUpId, powerUpId, StringComparison.OrdinalIgnoreCase));
        return found != null ? Mathf.Max(0, found.level) : 0;
    }

    public void SetPowerUpLevel(string powerUpId, int level)
    {
        if (string.IsNullOrWhiteSpace(powerUpId)) return;
        if (powerUpUpgrades == null) powerUpUpgrades = new List<PowerUpUpgradeState>();

        var found = powerUpUpgrades.Find(x => x != null && string.Equals(x.powerUpId, powerUpId, StringComparison.OrdinalIgnoreCase));
        if (found == null)
        {
            found = new PowerUpUpgradeState { powerUpId = powerUpId, level = Mathf.Max(0, level) };
            powerUpUpgrades.Add(found);
        }
        else
        {
            found.level = Mathf.Max(0, level);
        }
    }
}
