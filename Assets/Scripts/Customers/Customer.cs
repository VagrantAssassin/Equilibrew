using UnityEngine;

/// <summary>
/// Simple Customer data container and checker.
/// Customer no longer manages UI directly; CustomerManager spawns dialog prefab under dialogAnchor.
/// </summary>
public class Customer : MonoBehaviour
{
    [HideInInspector] public Recipe requestedRecipe;

    public void SetRequest(Recipe r)
    {
        requestedRecipe = r;
    }

    /// <summary>
    /// Validate served recipe matches requested recipe (by recipeName).
    /// </summary>
    public bool CheckServed(Recipe served)
    {
        bool matched = false;
        if (served == null || requestedRecipe == null)
            matched = false;
        else
            matched = string.Equals(served.recipeName, requestedRecipe.recipeName);

        Debug.Log($"[Customer] Served='{served?.recipeName ?? "null"}' Expected='{requestedRecipe?.recipeName ?? "null"}' => {(matched ? "SUCCESS" : "FAIL")}");
        return matched;
    }
}