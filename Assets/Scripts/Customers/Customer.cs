using UnityEngine;

/// <summary>
/// Simple Customer data container and checker.
/// Now includes fail tracking (failCount & maxFails) so each customer can leave after N wrong serves.
/// </summary>
public class Customer : MonoBehaviour
{
    [HideInInspector] public Recipe requestedRecipe;

    // fail tracking for this instance
    [HideInInspector] public int failCount = 0;
    [HideInInspector] public int maxFails = 3; // default, overwritten by CustomerManager when spawned from profile

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

    /// <summary>
    /// Register a failed serve attempt. Returns true if customer reached or exceeded maxFails and should leave.
    /// </summary>
    public bool RegisterFail()
    {
        failCount++;
        Debug.Log($"[Customer] Fail registered. Count={failCount}/{maxFails}");
        return failCount >= Mathf.Max(1, maxFails);
    }
}