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

    [Header("Debug / Helpers")]
    public bool enableDebugLogs = true;
    public bool disableAnimatorOnCupForDebug = false;

    private List<string> ingredients = new List<string>();
    private bool hasWater = false;

    public void AddIngredient(string ingredientName)
    {
        if (string.IsNullOrWhiteSpace(ingredientName))
        {
            if (enableDebugLogs) Debug.Log("[CupController] AddIngredient called with null/empty name.");
            return;
        }

        string normalized = NormalizeName(ingredientName);

        if (IsHotWaterLabel(normalized))
        {
            hasWater = true;
            if (enableDebugLogs) Debug.Log("[CupController] Hot water added (hasWater=true)");
        }
        else
        {
            ingredients.Add(normalized);
            if (enableDebugLogs) Debug.Log("[CupController] Ingredient added: '" + normalized + "'");
        }

        UpdateUI();
        CheckRecipe();
    }

    // Context menu helper: panggil dari Inspector -> tiga titik pada komponen -> pilih DEBUG_AddSampleIngredients
    [ContextMenu("DEBUG_AddSample_GreenTea+HotWater")]
    private void DEBUG_AddSample_GreenTea()
    {
        Debug.Log("[CupController] DEBUG: Simulating adding Green Tea + Hot Water");
        ClearCup();
        AddIngredient("Green Tea Leaf");
        AddIngredient("Hot Water");
    }

    private void UpdateUI()
    {
        if (cupImage == null)
        {
            Debug.LogWarning("[CupController] cupImage is not assigned");
        }
        else
        {
            if (disableAnimatorOnCupForDebug)
            {
                var anim = cupImage.GetComponent<Animator>();
                if (anim != null && anim.enabled)
                {
                    anim.enabled = false;
                    if (enableDebugLogs) Debug.Log("[CupController] Disabled Animator on cupImage for debugging.");
                }
            }

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

        var normalizedList = ingredients.Select(s => s.Trim()).ToList();

        if (enableDebugLogs) Debug.Log($"[CupController] Running CheckRecipe: hasWater={hasWater} | ingredients=[{string.Join(", ", normalizedList)}]");

        Sprite resultSprite = recipeValidator.Validate(normalizedList, hasWater);

        if (resultSprite != null)
        {
            if (cupImage != null)
            {
                cupImage.sprite = resultSprite;
                cupImage.SetNativeSize();
                if (enableDebugLogs) Debug.Log($"[CupController] Recipe matched -> sprite set: {resultSprite.name}");
            }
            if (ingredientListText != null)
            {
                ingredientListText.text = "✔ Recipe done!";
            }
        }
        else
        {
            if (enableDebugLogs) Debug.Log("[CupController] No recipe matched in CheckRecipe.");
        }
    }

    public void ClearCup()
    {
        ingredients.Clear();
        hasWater = false;
        UpdateUI();
    }

    private static string NormalizeName(string raw)
    {
        return raw?.Trim() ?? string.Empty;
    }

    private static bool IsHotWaterLabel(string normalizedCandidate)
    {
        if (string.IsNullOrWhiteSpace(normalizedCandidate)) return false;
        string key = normalizedCandidate.Trim().ToLowerInvariant();
        return key == "hot water" || key == "air panas" || key.Contains("hot water") || key.Contains("air panas");
    }
}