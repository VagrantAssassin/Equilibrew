using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CustomerProfile: ScriptableObject untuk data pelanggan.
/// - preferredRecipeNames: daftar nama resep yang jadi preferensi pelanggan.
/// - orderStories: daftar TextAsset (compiled Ink JSON) paralel dengan preferredRecipeNames.
/// - preferredRecipesJson: optional TextAsset berisi JSON array ["A","B"] untuk mengisi preferredRecipeNames.
/// </summary>
[CreateAssetMenu(menuName = "Equilibrew/Customer Profile", fileName = "CustomerProfile")]
public class CustomerProfile : ScriptableObject
{
    public string profileName;
    public Sprite portrait;

    [Header("Order preferences (parallel lists)")]
    [Tooltip("Daftar nama resep. Indeks harus sejajar dengan orderStories.")]
    public List<string> preferredRecipeNames = new List<string>();

    [Tooltip("Daftar compiled Ink JSON (TextAsset). Indeks harus sejajar dengan preferredRecipeNames.")]
    public List<TextAsset> orderStories = new List<TextAsset>();

    [Header("Optional: curhat stories (separate)")]
    [Tooltip("Jika ingin gunakan file curhat terpisah, masukkan disini; jika kosong, orderStories akan dipakai sebagai fallback for curhat.")]
    public List<TextAsset> curhatStories = new List<TextAsset>();

    [Header("Optional: import preferred recipes from JSON TextAsset")]
    [Tooltip("Masukkan TextAsset JSON berformat array string seperti [\"Jasmine Tea\",\"Mint Tea\"] untuk mengisi preferredRecipeNames secara otomatis.")]
    public TextAsset preferredRecipesJson;

    [Header("Behaviour")]
    [Tooltip("How many wrong serves the customer tolerates before leaving (>=1).")]
    public int maxFails = 3;

    public Personality personality = Personality.Neutral;

    public enum Personality
    {
        Neutral,
        Cheerful,
        Grumpy,
        Picky,
        Shy
    }

    private void OnValidate()
    {
        // Jika ada JSON assigned, coba parse
        if (preferredRecipesJson != null && !string.IsNullOrEmpty(preferredRecipesJson.text))
        {
            var parsed = ParseJsonArrayOfStrings(preferredRecipesJson.text);
            if (parsed != null && parsed.Count > 0)
            {
                preferredRecipeNames = parsed;
                // Ensure parallel orderStories list has at least same count (fill nulls)
                while (orderStories.Count < preferredRecipeNames.Count) orderStories.Add(null);
            }
        }

        // Safety: if lengths mismatch, ensure orderStories list is at least as long
        if (orderStories == null) orderStories = new List<TextAsset>();
        while (orderStories.Count < preferredRecipeNames.Count) orderStories.Add(null);
    }

    /// <summary>
    /// Simple parser for JSON array of strings like: ["Jasmine Tea","Mint Tea"]
    /// Not a full JSON parser — expects simple array without nested structures.
    /// </summary>
    private List<string> ParseJsonArrayOfStrings(string json)
    {
        try
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(json)) return result;
            string s = json.Trim();

            if (s.StartsWith("[") && s.EndsWith("]"))
            {
                s = s.Substring(1, s.Length - 2).Trim();
                if (string.IsNullOrEmpty(s)) return result;
                // split by commas not inside quotes — simple approach assuming no escaped quotes
                int idx = 0;
                bool inQuotes = false;
                var token = "";
                while (idx < s.Length)
                {
                    char c = s[idx];
                    if (c == '"' )
                    {
                        inQuotes = !inQuotes;
                        idx++;
                        continue;
                    }
                    if (c == ',' && !inQuotes)
                    {
                        if (!string.IsNullOrWhiteSpace(token)) result.Add(token.Trim());
                        token = "";
                        idx++;
                        continue;
                    }
                    token += c;
                    idx++;
                }
                if (!string.IsNullOrWhiteSpace(token)) result.Add(token.Trim());
            }
            return result;
        }
        catch
        {
            Debug.LogWarning("[CustomerProfile] Failed to parse preferredRecipesJson - expected simple JSON array of strings.");
            return null;
        }
    }
}