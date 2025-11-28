using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class CupController : MonoBehaviour
{
    public TextMeshProUGUI ingredientListText;
    public Image cupImage; // UI Image to display cup sprite
    public RecipeValidator recipeValidator;

    public Sprite emptySprite;
    public Sprite waterSprite;

    private List<string> ingredients = new List<string>();
    private bool hasWater = false;

    // Keep existing string-based API (normalizes input)
    public void AddIngredient(string ingredientName)
    {
        if (string.IsNullOrWhiteSpace(ingredientName)) return;

        string normalized = NormalizeName(ingredientName);

        if (IsHotWaterLabel(normalized))
            hasWater = true;
        else
            ingredients.Add(normalized);

        UpdateUI();
        CheckRecipe();
    }

    // New helper: accept the dropped GameObject and prefer IngredientData if present
    public void AddIngredientFromObject(GameObject item)
    {
        if (item == null) return;

        var data = item.GetComponent<IngredientData>();
        string nameKey = data != null && !string.IsNullOrWhiteSpace(data.ingredientName)
            ? data.ingredientName
            : item.name;

        AddIngredient(nameKey);
    }

    private void UpdateUI()
    {
        if (cupImage == null)
        {
            Debug.LogWarning("[CupController] cupImage is not assigned");
        }
        else
        {
            // show water/empty by default; recipe sprite will overwrite in CheckRecipe if matched
            cupImage.sprite = hasWater ? waterSprite : emptySprite;
        }

        if (ingredientListText != null)
        {
            if (ingredients.Count == 0 && !hasWater)
                ingredientListText.text = "Cup: (empty)";
            else
            {
                string text = "";
                if (hasWater) text += "Hot Water\n";
                text += string.Join("\n", ingredients);
                ingredientListText.text = text;
            }
        }
    }

    private void CheckRecipe()
    {
        if (recipeValidator == null)
        {
            Debug.LogWarning("[CupController] recipeValidator is not assigned");
            return;
        }

        // Pass a normalized copy so validator receives canonical names
        Sprite resultSprite = recipeValidator.Validate(ingredients.ToList(), hasWater);

        if (resultSprite != null)
        {
            if (cupImage != null)
            {
                cupImage.sprite = resultSprite;
                cupImage.SetNativeSize();
            }
            if (ingredientListText != null)
            {
                ingredientListText.text = "✔ Recipe done!";
            }
        }
    }

    public void ClearCup()
    {
        ingredients.Clear();
        hasWater = false;
        UpdateUI();
    }

    // Helpers

    private static string NormalizeName(string raw)
    {
        return raw?.Trim() ?? string.Empty;
    }

    private static bool IsHotWaterLabel(string normalizedLowercaseCandidate)
    {
        if (string.IsNullOrWhiteSpace(normalizedLowercaseCandidate)) return false;
        string key = normalizedLowercaseCandidate.Trim().ToLowerInvariant();
        return key == "hot water" || key == "air panas" || key.Contains("hot water") || key.Contains("air panas");
    }
}
