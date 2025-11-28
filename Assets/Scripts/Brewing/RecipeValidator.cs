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

    // Mengembalikan Recipe jika cocok, null jika tidak
    public Recipe Validate(List<string> addedIngredients, bool hasWater)
    {
        if (!hasWater) return null;

        foreach (var recipe in recipes)
        {
            if (recipe.ingredients.Count != addedIngredients.Count) continue;

            bool match = true;
            foreach (var ing in recipe.ingredients)
            {
                if (!addedIngredients.Contains(ing))
                {
                    match = false;
                    break;
                }
            }

            if (match) return recipe;
        }

        return null;
    }
}