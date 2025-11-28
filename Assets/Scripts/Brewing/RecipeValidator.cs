using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Recipe
{
    public string recipeName;
    public List<string> ingredients; // bahan kecuali Hot Water
    public Sprite resultSprite;      // sprite unik per resep
}

public class RecipeValidator : MonoBehaviour
{
    public List<Recipe> recipes;

    // Validasi resep dan return sprite jika cocok, null jika tidak
    public Sprite Validate(List<string> addedIngredients, bool hasWater)
    {
        if (!hasWater) return null; // Air panas wajib

        foreach (var recipe in recipes)
        {
            if (recipe.ingredients.Count != addedIngredients.Count) continue;

            bool match = true;

            foreach (var ing in recipe.ingredients)
            {
                // exact match case-insensitive
                bool found = false;
                foreach (var added in addedIngredients)
                {
                    if (string.Equals(ing.Trim(), added.Trim(), System.StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                Debug.Log("[RecipeValidator] Recipe matched: " + recipe.recipeName);
                return recipe.resultSprite; // return sprite unik resep
            }
        }

        return null;
    }
}