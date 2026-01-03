using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(CanvasGroup))]
public class DragItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private Canvas canvas;
    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;

    // posisi asli di panel, tetap sepanjang hidup object
    // gunakan anchoredPosition (UI-local) untuk stabilitas lintas device / Canvas scalers
    private Vector2 originalAnchoredPosition;
    [Tooltip("Jika true akan menampilkan debug log untuk drag/reset (matikan di build)")]
    public bool enableDebugLogs = false;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        canvas = GetComponentInParent<Canvas>();
    }

    private void Start()
    {
        // Simpan posisi asli setelah satu frame agar layout/group dan CanvasScaler sempat menyesuaikan.
        // Ini menghindari masalah posisi yang diambil terlalu dini pada device/layar berbeda (WebGL/responsive).
        StartCoroutine(StoreOriginalPositionNextFrame());
    }

    private IEnumerator StoreOriginalPositionNextFrame()
    {
        // tunggu satu frame agar layout dan canvas scaler selesai mengatur posisi
        yield return null;
        if (rectTransform != null)
        {
            originalAnchoredPosition = rectTransform.anchoredPosition;
            if (enableDebugLogs) Debug.Log($"[DragItem] Stored originalAnchoredPosition = {originalAnchoredPosition} for {gameObject.name}");
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        // Letakkan item di atas sibling lain supaya terlihat saat drag
        rectTransform.SetAsLastSibling();

        // Jangan ubah originalAnchoredPosition di sini — kita ingin kembali ke posisi panel awal.
        canvasGroup.blocksRaycasts = false;
        canvasGroup.alpha = 0.8f;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (canvas == null) return;
        // Gunakan anchoredPosition agar seragam dengan ResetPosition
        rectTransform.anchoredPosition += eventData.delta / canvas.scaleFactor;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        canvasGroup.blocksRaycasts = true;
        canvasGroup.alpha = 1f;

        // Check apakah drop di DropZone berhasil
        if (!eventData.pointerEnter || eventData.pointerEnter.GetComponentInParent<DropZone>() == null)
        {
            // kalau drop tidak di gelas, kembalikan ke posisi asli (anchored)
            ResetPosition();
        }
    }

    public void ResetPosition()
    {
        if (rectTransform == null) return;
        rectTransform.anchoredPosition = originalAnchoredPosition;
        if (enableDebugLogs) Debug.Log($"[DragItem] ResetPosition -> anchoredPosition set to {originalAnchoredPosition} for {gameObject.name}");
    }
}