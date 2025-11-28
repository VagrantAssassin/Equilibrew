using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[System.Serializable]
public class Recipe
{
    public string recipeName;
    public List<string> ingredients; // bahan (bisa berisi "Hot Water" atau tidak)
    public Sprite resultSprite;      // sprite unik per resep
}

public class RecipeValidator : MonoBehaviour
{
    public List<Recipe> recipes;
    [Header("Debug")]
    public bool enableDebugLogs = true;

    // Mengembalikan Sprite hasil jika cocok, null jika tidak
    public Sprite Validate(List<string> addedIngredients, bool hasWater)
    {
        if (!hasWater)
        {
            if (enableDebugLogs) Debug.Log("[RecipeValidator] No water -> cannot validate");
            return null;
        }

        // Normalize incoming ingredients (case-insensitive, trimmed)
        var normalizedAdded = addedIngredients
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .ToList();

        if (enableDebugLogs) Debug.Log($"[RecipeValidator] Validating. Added ({normalizedAdded.Count}): {string.Join(", ", normalizedAdded)}");

        foreach (var recipe in recipes)
        {
            if (recipe == null) continue;

            // Normalize recipe ingredients and ignore any "hot water" entries in the recipe
            var normalizedRecipe = recipe.ingredients
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToLowerInvariant())
                .Where(s => !IsHotWaterLabel(s))
                .ToList();

            // Quick count check (recipe without hot water vs addedIngredients)
            if (normalizedRecipe.Count != normalizedAdded.Count)
            {
                if (enableDebugLogs) Debug.Log($"[RecipeValidator] Skip '{recipe.recipeName}' (count mismatch {normalizedRecipe.Count} != {normalizedAdded.Count})");
                continue;
            }

            // Multiset comparison using grouping (handles duplicates and order-independence)
            bool equal = AreMultisetsEqual(normalizedRecipe, normalizedAdded);

            if (enableDebugLogs)
            {
                Debug.Log($"[RecipeValidator] Comparing with '{recipe.recipeName}' -> {(equal ? "MATCH" : "NO MATCH")}");
            }

            if (equal)
            {
                if (recipe.resultSprite == null && enableDebugLogs)
                    Debug.LogWarning($"[RecipeValidator] Recipe '{recipe.recipeName}' matched but resultSprite is null!");

                return recipe.resultSprite;
            }
        }

        if (enableDebugLogs) Debug.Log("[RecipeValidator] No recipe matched.");
        return null;
    }

    private bool AreMultisetsEqual(List<string> a, List<string> b)
    {
        var ga = a.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var gb = b.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        if (ga.Count != gb.Count) return false;

        foreach (var kv in ga)
        {
            int countB;
            if (!gb.TryGetValue(kv.Key, out countB)) return false;
            if (countB != kv.Value) return false;
        }

        return true;
    }

    private static bool IsHotWaterLabel(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        // cek kata kunci hot water / air panas (case-insensitive karena sudah lower)
        return key == "hot water" || key == "air panas" || key.Contains("hot water") || key.Contains("air panas");
    }
}