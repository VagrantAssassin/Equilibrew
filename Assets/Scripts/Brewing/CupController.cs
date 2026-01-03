using System;
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

    [Header("UI Buttons (optional - assign in Inspector)")]
    public Button serveButton;
    public Button resetButton;

    // last matched recipe (before serving)
    [NonSerialized] public Recipe currentMatchedRecipe = null;
    // last served recipe (store for customer matching later)
    [NonSerialized] public Recipe lastServedRecipe = null;

    // Events other systems can subscribe to
    public event Action<Recipe> OnServe; // passes served Recipe
    public event Action OnReset;

    private List<string> ingredients = new List<string>();
    private bool hasWater = false;

    private void Start()
    {
        // wire up buttons if present
        if (serveButton != null)
        {
            serveButton.onClick.AddListener(Serve);
            serveButton.interactable = false; // initially disabled until valid recipe
        }
        if (resetButton != null)
        {
            resetButton.onClick.AddListener(ResetCupButton);
        }

        UpdateUI();
    }

    // Keep existing string-based API (normalizes input)
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

        // Play ingredient-in SFX (centralized via AudioManager)
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX_IngredientIn();
        }

        UpdateUI();
        CheckRecipe();
    }

    // For other systems to call (if they pass GameObject)
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

        // use new FindMatchingRecipe to get the Recipe object (so we can store it)
        var matched = recipeValidator.FindMatchingRecipe(normalizedList, hasWater);

        currentMatchedRecipe = matched;

        if (matched != null)
        {
            if (cupImage != null)
            {
                cupImage.sprite = matched.resultSprite;
                cupImage.SetNativeSize();
                if (enableDebugLogs) Debug.Log($"[CupController] Recipe matched -> sprite set: {matched.recipeName} ({(matched.resultSprite != null ? matched.resultSprite.name : "NULL SPRITE")})");
            }
            if (ingredientListText != null)
            {
                ingredientListText.text = "✔ " + matched.recipeName;
            }

            if (serveButton != null) serveButton.interactable = true;
        }
        else
        {
            if (enableDebugLogs) Debug.Log("[CupController] No recipe matched in CheckRecipe.");
            if (serveButton != null) serveButton.interactable = false;
        }
    }

    // Called when player presses Serve button (or programmatically)
    public void Serve()
    {
        if (currentMatchedRecipe == null)
        {
            Debug.Log("[CupController] Cannot serve: current recipe is invalid or incomplete.");
            return;
        }

        // store last served so other systems can compare later
        lastServedRecipe = currentMatchedRecipe;

        if (enableDebugLogs) Debug.Log($"[CupController] Served recipe: {lastServedRecipe.recipeName}");

        // Play serve SFX
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX_Serve();
        }

        // notify listeners (e.g., CustomerManager / DialogueManager)
        OnServe?.Invoke(lastServedRecipe);

        // After serving, clear cup for next order
        ClearCup();
    }

    // Called by UI Reset button (wires to ClearCup but also triggers event)
    public void ResetCupButton()
    {
        ClearCup();
        OnReset?.Invoke();
        if (enableDebugLogs) Debug.Log("[CupController] ResetCupButton pressed -> cup cleared.");
    }

    public void ClearCup()
    {
        ingredients.Clear();
        hasWater = false;
        currentMatchedRecipe = null;
        if (serveButton != null) serveButton.interactable = false;
        UpdateUI();
    }

    // Helpers

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