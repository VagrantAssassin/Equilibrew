using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class CupController : MonoBehaviour
{
    public TextMeshProUGUI ingredientListText;
    public Image cupImage; // Cup sendiri Image component
    public RecipeValidator recipeValidator;

    public Sprite emptySprite;
    public Sprite waterSprite;

    private List<string> ingredients = new List<string>();
    private bool hasWater = false;

    public void AddIngredient(string ingredientName)
    {
        if (ingredientName == "Hot Water")
        {
            hasWater = true;
        }
        else
        {
            ingredients.Add(ingredientName);
        }

        UpdateUI();
        CheckRecipe();
    }

    private void UpdateUI()
    {
        // Jika belum ada air, tampilkan empty
        cupImage.sprite = hasWater ? waterSprite : emptySprite;

        // TMP text
        if (ingredients.Count == 0 && !hasWater)
            ingredientListText.text = "Cup: (empty)";
        else
        {
            string text = "";
            if (hasWater) text += "Air Panas\n";
            text += string.Join("\n", ingredients);
            ingredientListText.text = text;
        }
    }

    private void CheckRecipe()
    {
        Recipe result = recipeValidator.Validate(ingredients, hasWater);

        if (result != null)
        {
            cupImage.sprite = result.resultSprite;  // ambil sprite dari RecipeValidator
            ingredientListText.text = "✔ " + result.recipeName;
            Debug.Log("[CupController] Recipe matched: " + result.recipeName);
        }
    }

    public void ClearCup()
    {
        ingredients.Clear();
        hasWater = false;
        UpdateUI();
    }
}