using UnityEngine;
using UnityEngine.EventSystems;

public class DropZone : MonoBehaviour, IDropHandler
{
    public CupController cupController; // assign di inspector

    public void OnDrop(PointerEventData eventData)
    {
        if (eventData.pointerDrag == null) return;

        var dragItem = eventData.pointerDrag.GetComponent<DragItem>();
        var ingredient = eventData.pointerDrag.GetComponent<IngredientData>();

        if (dragItem != null && ingredient != null)
        {
            Debug.Log("[DropZone] Dropped ingredient: " + ingredient.ingredientName);
            if (cupController != null)
                cupController.AddIngredient(ingredient.ingredientName);

            // reset posisi item ke panel (atau bisa destroy nanti)
            dragItem.ResetPosition();
        }
    }
}