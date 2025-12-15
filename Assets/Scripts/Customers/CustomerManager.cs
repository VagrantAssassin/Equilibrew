using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// CustomerManager (reverted): 
/// - spawn one customer at a time (random recipe from RecipeValidator)
/// - instantiate dialogPrefab as child of dialogAnchor (empty RectTransform in Canvas)
/// - dialog prefab is authoritative for visual dialog (Image + TMP inside prefab)
/// - on serve success: destroy dialog prefab and customer, then spawn next
/// - on serve failure: show temporary failure text inside the instantiated dialog prefab (if TMP exists)
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
    [Tooltip("Dialog UI prefab (RectTransform root). Must contain Image/TextMeshProUGUI if you want sprite+text set.")]
    public GameObject dialogPrefab;

    [Header("Options")]
    public int maxCustomersPerDay = 10;     // reserved for future use
    public float failureMessageDuration = 1.5f; // seconds to show failure message (optional)

    // internal
    private Customer currentCustomer;
    private GameObject activeDialogInstance;
    private Coroutine failureCoroutine;

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

        // spawn dialog prefab under dialogAnchor
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

        // attempt to find Image and TMP inside the instantiated prefab and set them
        if (activeDialogInstance != null)
        {
            var img = activeDialogInstance.GetComponentInChildren<Image>();
            var txt = activeDialogInstance.GetComponentInChildren<TextMeshProUGUI>();

            if (img != null)
            {
                if (recipe != null && recipe.resultSprite != null)
                {
                    img.sprite = recipe.resultSprite;
                    img.color = Color.white;
                    img.gameObject.SetActive(true);
                }
                else
                {
                    img.gameObject.SetActive(false);
                }
            }

            if (txt != null)
            {
                txt.text = recipe != null ? recipe.recipeName : "";
                txt.gameObject.SetActive(!string.IsNullOrEmpty(txt.text));
            }
        }
    }

    private void ClearDialogInstance()
    {
        if (activeDialogInstance != null)
        {
            Destroy(activeDialogInstance);
            activeDialogInstance = null;
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
            // cleanup dialog and customer, then spawn next
            ClearDialogInstance();
            Destroy(currentCustomer.gameObject, 0.05f);
            currentCustomer = null;
            if (failureCoroutine != null) { StopCoroutine(failureCoroutine); failureCoroutine = null; }
            SpawnNextCustomer();
        }
        else
        {
            Debug.Log("[CustomerManager] Customer not satisfied -> gagal.");
            // show temporary failure message if prefab has TMP; otherwise just log
            if (activeDialogInstance != null)
            {
                var txt = activeDialogInstance.GetComponentInChildren<TextMeshProUGUI>();
                if (txt != null)
                {
                    if (failureCoroutine != null) StopCoroutine(failureCoroutine);
                    failureCoroutine = StartCoroutine(ShowTemporaryFailureMessage(txt));
                }
            }
        }
    }

    private IEnumerator ShowTemporaryFailureMessage(TextMeshProUGUI txt)
    {
        string prev = txt.text;
        txt.text = "That is not what I ordered!";
        yield return new WaitForSeconds(failureMessageDuration);
        // restore original if still have customer
        if (currentCustomer != null && currentCustomer.requestedRecipe != null)
            txt.text = currentCustomer.requestedRecipe.recipeName;
        else
            txt.text = prev;
        failureCoroutine = null;
    }
}