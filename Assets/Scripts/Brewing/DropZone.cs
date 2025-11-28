using UnityEngine;
using UnityEngine.EventSystems;

public class DropZone : MonoBehaviour, IDropHandler
{
    public CupController cupController; // assign di inspector

    public void OnDrop(PointerEventData eventData)
    {
        Debug.Log("[DropZone] OnDrop called. pointerDrag=" + (eventData.pointerDrag ? eventData.pointerDrag.name : "NULL"));

        if (eventData.pointerDrag == null) return;

        var dragItem = eventData.pointerDrag.GetComponent<DragItem>();
        var ingredient = eventData.pointerDrag.GetComponent<IngredientData>();

        if (dragItem != null && ingredient != null)
        {
            Debug.Log("[DropZone] Dropped ingredient: " + ingredient.ingredientName);
            if (cupController != null)
                cupController.AddIngredient(ingredient.ingredientName);
            else
                Debug.LogWarning("[DropZone] cupController is null! Assign the CupController instance in the Inspector.");

            // reset posisi item ke panel (atau bisa destroy nanti)
            dragItem.ResetPosition();
        }
        else
        {
            Debug.LogWarning("[DropZone] Drop failed: DragItem or IngredientData missing on pointerDrag.");
        }
    }

    // Helper untuk debug: panggil method ini via Inspector (Add Component -> ... tidak perlu)
    [ContextMenu("DEBUG_SimulateDrop_Honey")]
    private void DEBUG_SimulateDrop_Honey()
    {
        Debug.Log("[DropZone] DEBUG simulate drop -> calling cupController.AddIngredient(\"Honey\")");
        if (cupController != null)
            cupController.AddIngredient("Honey");
        else
            Debug.LogWarning("[DropZone] cupController is null in DEBUG simulate.");
    }
}