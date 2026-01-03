using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// ButtonSfx (simple)
/// - Attach to UI Button to play one of AudioManager's SFX on click or pointer-down.
/// - Routes calls through AudioManager to keep single source of truth.
/// </summary>
[RequireComponent(typeof(Button))]
public class ButtonSfx : MonoBehaviour, IPointerDownHandler
{
    public enum SfxType
    {
        ButtonClick,
        Paper,
        Ingredient,
        NPCSpawn,
        NPCDespawn,
        Cabinet, // new
        Serve    // new
    }

    [Header("Configuration")]
    public SfxType sfxType = SfxType.ButtonClick;
    [Range(0f, 1f)] public float volume = 1f;
    [Tooltip("Play on Button.onClick (true) or only on pointer down (false)")]
    public bool playOnClick = true;
    [Tooltip("Also play when pointer down (useful for earlier feedback)")]
    public bool playOnPointerDown = false;

    Button btn;

    void Awake()
    {
        btn = GetComponent<Button>();
        if (btn == null) return;

        // Ensure we don't register duplicate listeners
        if (playOnClick)
        {
            btn.onClick.RemoveListener(OnPlayRequested);
            btn.onClick.AddListener(OnPlayRequested);
        }
    }

    void OnDestroy()
    {
        if (btn != null && playOnClick)
            btn.onClick.RemoveListener(OnPlayRequested);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (playOnPointerDown)
            OnPlayRequested();
    }

    private void OnPlayRequested()
    {
        if (AudioManager.Instance == null) return;

        switch (sfxType)
        {
            case SfxType.ButtonClick: AudioManager.Instance.PlaySFX_Button(volume); break;
            case SfxType.Paper: AudioManager.Instance.PlaySFX_Paper(volume); break;
            case SfxType.Ingredient: AudioManager.Instance.PlaySFX_IngredientIn(volume); break;
            case SfxType.NPCSpawn: AudioManager.Instance.PlaySFX_NPCSpawn(volume); break;
            case SfxType.NPCDespawn: AudioManager.Instance.PlaySFX_NPCDespawn(volume); break;
            case SfxType.Cabinet: AudioManager.Instance.PlaySFX_Cabinet(volume); break;
            case SfxType.Serve: AudioManager.Instance.PlaySFX_Serve(volume); break;
        }
    }
}