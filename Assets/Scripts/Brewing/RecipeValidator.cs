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

    // Existing API: return Sprite jika cocok (dipertahankan untuk kompatibilitas)
    public Sprite Validate(List<string> addedIngredients, bool hasWater)
    {
        var recipe = FindMatchingRecipe(addedIngredients, hasWater);
        return recipe != null ? recipe.resultSprite : null;
    }

    // New: kembalikan Recipe yang cocok (lebih berguna untuk penyimpanan nama/objek)
    public Recipe FindMatchingRecipe(List<string> addedIngredients, bool hasWater)
    {
        if (!hasWater)
        {
            if (enableDebugLogs) Debug.Log("[RecipeValidator] No water -> cannot validate");
            return null;
        }

        var normalizedAdded = addedIngredients
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .ToList();

        if (enableDebugLogs) Debug.Log($"[RecipeValidator] Validating. Added ({normalizedAdded.Count}): {string.Join(", ", normalizedAdded)}");

        foreach (var recipe in recipes)
        {
            if (recipe == null) continue;

            var normalizedRecipe = recipe.ingredients
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToLowerInvariant())
                // ignore hot water entry in recipe lists (our system tracks water separately)
                .Where(s => !IsHotWaterLabel(s))
                .ToList();

            if (normalizedRecipe.Count != normalizedAdded.Count)
            {
                if (enableDebugLogs) Debug.Log($"[RecipeValidator] Skip '{recipe.recipeName}' (count mismatch {normalizedRecipe.Count} != {normalizedAdded.Count})");
                continue;
            }

            bool equal = AreMultisetsEqual(normalizedRecipe, normalizedAdded);

            if (enableDebugLogs)
            {
                Debug.Log($"[RecipeValidator] Comparing with '{recipe.recipeName}' -> {(equal ? "MATCH" : "NO MATCH")}");
            }

            if (equal)
            {
                if (recipe.resultSprite == null && enableDebugLogs)
                    Debug.LogWarning($"[RecipeValidator] Recipe '{recipe.recipeName}' matched but resultSprite is null!");

                return recipe;
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
        return key == "hot water" || key == "air panas" || key.Contains("hot water") || key.Contains("air panas");
    }
}