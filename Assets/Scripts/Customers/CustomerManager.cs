using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// CustomerManager (text-only dialog):
/// - spawn one customer at a time (random recipe from RecipeValidator)
/// - instantiate dialogPrefab as child of dialogAnchor (empty RectTransform in Canvas)
/// - dialog prefab is used only for text (no icon handling)
/// - on serve success: dialog text -> "Terima kasih", then next customer spawns
/// - on serve failure: dialog text -> "Apa ini, tidak enak" for a short duration, then restore
/// </summary>
public class CustomerManager : MonoBehaviour
{
    [Header("References (assign in Inspector)")]
    public RecipeValidator recipeValidator; // source of recipes
    public CupController cupController;     // subscribe OnServe
    public Transform spawnParent;           // parent transform for customer GameObject (optional)
    public GameObject customerPrefab;       // optional visual prefab for customers (can be null)

    [Header("Dialog (UI prefab)")]
    [Tooltip("Empty RectTransform under Canvas where dialog prefab will be instantiated.")]
    public RectTransform dialogAnchor;
    [Tooltip("Dialog UI prefab (RectTransform root). Should contain a TextMeshProUGUI for dialog text.")]
    public GameObject dialogPrefab;

    [Header("Options")]
    public int maxCustomersPerDay = 10;         // reserved for future use
    public float failureMessageDuration = 1.5f; // seconds to show failure message
    public float successMessageDuration = 1.0f; // seconds to show success message before next spawn

    // internal
    private Customer currentCustomer;
    private GameObject activeDialogInstance;
    private Coroutine messageCoroutine;

    private void Start()
    {
        if (cupController != null)
            cupController.OnServe += OnServeReceived;
        else
            Debug.LogWarning("[CustomerManager] cupController not assigned.");

        ClearDialogInstance();
        SpawnNextCustomer();
    }

    private void OnDestroy()
    {
        if (cupController != null)
            cupController.OnServe -= OnServeReceived;
    }

    /// <summary>
    /// Pick a random recipe, spawn a Customer GameObject, and instantiate dialogPrefab as child of dialogAnchor.
    /// Dialog prefab is used only for text (we don't touch any Image).
    /// </summary>
    public void SpawnNextCustomer()
    {
        if (recipeValidator == null || recipeValidator.recipes == null || recipeValidator.recipes.Count == 0)
        {
            Debug.LogWarning("[CustomerManager] No recipes available in RecipeValidator.");
            ClearDialogInstance();
            return;
        }

        int idx = Random.Range(0, recipeValidator.recipes.Count);
        Recipe chosen = recipeValidator.recipes[idx];

        // instantiate customer GameObject
        GameObject go;
        if (customerPrefab != null)
        {
            go = Instantiate(customerPrefab, spawnParent ? spawnParent : transform);
        }
        else
        {
            go = new GameObject("Customer");
            if (spawnParent != null) go.transform.SetParent(spawnParent, false);
            else go.transform.SetParent(transform, false);
        }

        currentCustomer = go.GetComponent<Customer>() ?? go.AddComponent<Customer>();
        currentCustomer.SetRequest(chosen);

        Debug.Log($"[CustomerManager] Spawned customer requesting '{chosen.recipeName}'");

        // spawn dialog prefab under dialogAnchor and set its text only
        CreateDialogInstanceForRecipe(chosen);
    }

    private void CreateDialogInstanceForRecipe(Recipe recipe)
    {
        ClearDialogInstance();

        if (dialogPrefab == null)
        {
            Debug.LogWarning("[CustomerManager] dialogPrefab not assigned. No visual dialog will be shown.");
            return;
        }

        if (dialogAnchor == null)
        {
            Debug.LogWarning("[CustomerManager] dialogAnchor not assigned. Instantiating dialog prefab at root instead.");
            activeDialogInstance = Instantiate(dialogPrefab);
        }
        else
        {
            activeDialogInstance = Instantiate(dialogPrefab, dialogAnchor, false);
        }

        if (activeDialogInstance == null) return;

        // Only set text (do NOT touch any Image in prefab)
        var txt = activeDialogInstance.GetComponentInChildren<TextMeshProUGUI>(true);
        if (txt != null)
        {
            txt.text = recipe != null ? recipe.recipeName : "";
            txt.gameObject.SetActive(!string.IsNullOrEmpty(txt.text));
        }
    }

    private void ClearDialogInstance()
    {
        if (activeDialogInstance != null)
        {
            Destroy(activeDialogInstance);
            activeDialogInstance = null;
        }

        if (messageCoroutine != null)
        {
            StopCoroutine(messageCoroutine);
            messageCoroutine = null;
        }
    }

    private void OnServeReceived(Recipe served)
    {
        if (currentCustomer == null)
        {
            Debug.Log("[CustomerManager] No active customer to serve.");
            return;
        }

        bool ok = currentCustomer.CheckServed(served);
        if (ok)
        {
            Debug.Log("[CustomerManager] Customer satisfied -> berhasil!");
            // show success message then spawn next
            if (activeDialogInstance != null)
            {
                var txt = activeDialogInstance.GetComponentInChildren<TextMeshProUGUI>(true);
                if (txt != null)
                {
                    if (messageCoroutine != null) StopCoroutine(messageCoroutine);
                    messageCoroutine = StartCoroutine(ShowSuccessThenNext(txt));
                    return;
                }
            }

            // fallback if no TMP present: cleanup immediately and spawn next
            CleanupAndSpawnNext();
        }
        else
        {
            Debug.Log("[CustomerManager] Customer not satisfied -> gagal.");
            // show failure message temporarily
            if (activeDialogInstance != null)
            {
                var txt = activeDialogInstance.GetComponentInChildren<TextMeshProUGUI>(true);
                if (txt != null)
                {
                    if (messageCoroutine != null) StopCoroutine(messageCoroutine);
                    messageCoroutine = StartCoroutine(ShowFailureMessage(txt));
                }
            }
        }
    }

    private IEnumerator ShowFailureMessage(TextMeshProUGUI txt)
    {
        string prev = txt.text;
        txt.text = "Apa ini, tidak enak";
        yield return new WaitForSeconds(failureMessageDuration);
        // restore requested name if customer still exists
        if (currentCustomer != null && currentCustomer.requestedRecipe != null)
            txt.text = currentCustomer.requestedRecipe.recipeName;
        else
            txt.text = prev;
        messageCoroutine = null;
    }

    private IEnumerator ShowSuccessThenNext(TextMeshProUGUI txt)
    {
        string prev = txt.text;
        txt.text = "Terima kasih";
        yield return new WaitForSeconds(successMessageDuration);

        // cleanup and spawn next
        CleanupAndSpawnNext();
        messageCoroutine = null;
    }

    private void CleanupAndSpawnNext()
    {
        ClearDialogInstance();
        if (currentCustomer != null)
            Destroy(currentCustomer.gameObject, 0.05f);
        currentCustomer = null;
        SpawnNextCustomer();
    }
}