using System.Collections;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

/// <summary>
/// AudioManager (singleton)
/// - Menyimpan AudioClips utama (BGM & SFX)
/// - Per-clip volume multipliers dapat diatur di Inspector
/// - Satu AudioSource untuk BGM (loop)
/// - Pool AudioSources untuk SFX (PlayOneShot)
/// - Simple FadeCoroutine untuk crossfade/stop BGM
/// - Listens to sceneLoaded and automatically switches BGM based on scene name mapping
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("BGM Clips")]
    public AudioClip bgmMainMenu;
    [Range(0f, 1f)] public float bgmMainMenuVolume = 1f;
    public AudioClip bgmCafe;
    [Range(0f, 1f)] public float bgmCafeVolume = 1f;

    [Header("SFX Clips")]
    public AudioClip sfxIngredientIn;
    [Range(0f, 1f)] public float sfxIngredientInVolume = 1f;
    public AudioClip sfxNPCSpawn;
    [Range(0f, 1f)] public float sfxNPCSpawnVolume = 1f;
    public AudioClip sfxNPCDespawn;
    [Range(0f, 1f)] public float sfxNPCDespawnVolume = 1f;
    public AudioClip sfxPaper;
    [Range(0f, 1f)] public float sfxPaperVolume = 1f;
    public AudioClip sfxButtonClick;
    [Range(0f, 1f)] public float sfxButtonClickVolume = 1f;

    // NEW: cabinet (lemari) and serve SFX
    public AudioClip sfxCabinet;
    [Range(0f, 1f)] public float sfxCabinetVolume = 1f;
    public AudioClip sfxServe;
    [Range(0f, 1f)] public float sfxServeVolume = 1f;

    [Header("Audio Sources / Pool")]
    public AudioSource bgmSource; // if null, created at runtime
    public int sfxPoolSize = 6;
    private AudioSource[] sfxPool;
    private int sfxPoolIndex = 0;

    [Header("Global Volumes")]
    [Range(0f, 1f)] public float bgmVolume = 0.7f;
    [Range(0f, 1f)] public float sfxVolume = 1f;

    [Header("Pause behaviour")]
    [Tooltip("If true, BGM AudioSource will ignore AudioListener.pause and keep playing in pause state.")]
    public bool bgmIgnoreListenerPause = true;

    [Header("Scene -> BGM mapping (adjust scene names if needed)")]
    public string sceneNameMainMenu = "MainMenu";
    public string sceneNameCafe = "Cafe";

    [Header("Optional: AudioMixer (assign if using)")]
    public AudioMixer audioMixer;
    public string mixerGroupBgm = "BGM";
    public string mixerGroupSfx = "SFX";

    private Coroutine bgmFadeCoroutine = null;
    private AudioClip currentBgmClip = null;
    private float currentBgmTargetVolume = 0.7f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Ensure bgmSource exists
        if (bgmSource == null)
        {
            GameObject go = new GameObject("BGM_Source");
            go.transform.SetParent(transform);
            bgmSource = go.AddComponent<AudioSource>();
            bgmSource.loop = true;
            bgmSource.playOnAwake = false;
        }
        bgmSource.volume = bgmVolume * 1f;
        bgmSource.ignoreListenerPause = bgmIgnoreListenerPause;

        // create SFX pool
        sfxPool = new AudioSource[Mathf.Max(1, sfxPoolSize)];
        for (int i = 0; i < sfxPool.Length; i++)
        {
            var go = new GameObject($"SFX_Source_{i}");
            go.transform.SetParent(transform);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.volume = sfxVolume;
            src.ignoreListenerPause = false; // keep SFX affected by listener pause by default
            sfxPool[i] = src;
        }

        // Subscribe to scene change events to automatically switch bgm
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnValidate()
    {
        if (bgmSource != null)
        {
            bgmSource.volume = bgmVolume * (currentBgmClip == bgmMainMenu ? bgmMainMenuVolume : (currentBgmClip == bgmCafe ? bgmCafeVolume : 1f));
            bgmSource.ignoreListenerPause = bgmIgnoreListenerPause;
        }
        if (sfxPool != null)
        {
            foreach (var s in sfxPool) if (s != null) s.volume = sfxVolume;
        }
    }

    // Scene loaded callback -> switch BGM according to scene name
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Normalize name checks (you can change mapping via Inspector)
        if (string.Equals(scene.name, sceneNameMainMenu, System.StringComparison.OrdinalIgnoreCase))
        {
            PlayBGM_MainMenu(true, 0.6f);
        }
        else if (string.Equals(scene.name, sceneNameCafe, System.StringComparison.OrdinalIgnoreCase))
        {
            PlayBGM_Cafe(true, 0.6f);
        }
        else
        {
            // For other scenes, you can decide to stop BGM or keep current.
            // We'll not stop automatically; keep current BGM.
        }

        // Ensure listener is not globally paused when entering menu (optional safety)
        if (string.Equals(scene.name, sceneNameMainMenu, System.StringComparison.OrdinalIgnoreCase))
        {
            AudioListener.pause = false;
        }
    }

    // -----------------------
    // BGM control
    // -----------------------
    public void PlayBGM_MainMenu(bool fade = true, float fadeDuration = 0.5f)
    {
        PlayBGMClip(bgmMainMenu, bgmMainMenuVolume, fade, fadeDuration);
    }

    public void PlayBGM_Cafe(bool fade = true, float fadeDuration = 0.5f)
    {
        PlayBGMClip(bgmCafe, bgmCafeVolume, fade, fadeDuration);
    }

    public void StopBGM(bool fade = true, float fadeDuration = 0.5f)
    {
        if (bgmFadeCoroutine != null) StopCoroutine(bgmFadeCoroutine);
        if (fade)
            bgmFadeCoroutine = StartCoroutine(FadeBgmToVolume(0f, fadeDuration, true));
        else
        {
            bgmSource.Stop();
            currentBgmClip = null;
            currentBgmTargetVolume = 0f;
        }
    }

    public void PlayBGMClip(AudioClip clip, float clipVolume = 1f, bool fade = true, float fadeDuration = 0.5f)
    {
        if (clip == null)
        {
            Debug.LogWarning("[AudioManager] PlayBGMClip called with null clip.");
            return;
        }

        // compute target volume using global bgmVolume and per-clip multiplier
        currentBgmTargetVolume = Mathf.Clamp01(bgmVolume * clipVolume);

        if (currentBgmClip == clip && bgmSource.isPlaying)
        {
            // ensure correct volume
            bgmSource.volume = currentBgmTargetVolume;
            return;
        }

        if (bgmFadeCoroutine != null) StopCoroutine(bgmFadeCoroutine);
        bgmFadeCoroutine = StartCoroutine(CrossfadeBGM(clip, currentBgmTargetVolume, fadeDuration));
    }

    private IEnumerator CrossfadeBGM(AudioClip newClip, float targetVolume, float duration)
    {
        float startVol = bgmSource.isPlaying ? bgmSource.volume : 0f;
        float t = 0f;
        float half = Mathf.Max(0.0001f, duration * 0.5f);

        // fade out
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / half);
            bgmSource.volume = Mathf.Lerp(startVol, 0f, p);
            yield return null;
        }

        bgmSource.Stop();
        bgmSource.clip = newClip;
        bgmSource.loop = true;
        bgmSource.Play();
        currentBgmClip = newClip;

        // fade in to targetVolume
        t = 0f;
        float from = 0f;
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / half);
            bgmSource.volume = Mathf.Lerp(from, targetVolume, p);
            yield return null;
        }
        bgmSource.volume = targetVolume;
        bgmFadeCoroutine = null;
    }

    private IEnumerator FadeBgmToVolume(float target, float duration, bool stopAfter = false)
    {
        float start = bgmSource.volume;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            bgmSource.volume = Mathf.Lerp(start, target, Mathf.Clamp01(t / duration));
            yield return null;
        }
        bgmSource.volume = target;
        if (stopAfter && target <= 0.001f)
        {
            bgmSource.Stop();
            bgmSource.clip = null;
            currentBgmClip = null;
        }
        bgmFadeCoroutine = null;
    }

    // -----------------------
    // SFX play helpers (per-clip volume multipliers used)
    // -----------------------
    private AudioSource GetNextSfxSource()
    {
        if (sfxPool == null || sfxPool.Length == 0) return bgmSource;
        var src = sfxPool[sfxPoolIndex];
        sfxPoolIndex = (sfxPoolIndex + 1) % sfxPool.Length;
        return src;
    }

    // Convenience typed calls
    public void PlaySFX_IngredientIn(float vol = 1f) { PlaySFXClip(sfxIngredientIn, sfxIngredientInVolume, vol); }
    public void PlaySFX_NPCSpawn(float vol = 1f) { PlaySFXClip(sfxNPCSpawn, sfxNPCSpawnVolume, vol); }
    public void PlaySFX_NPCDespawn(float vol = 1f) { PlaySFXClip(sfxNPCDespawn, sfxNPCDespawnVolume, vol); }
    public void PlaySFX_Paper(float vol = 1f) { PlaySFXClip(sfxPaper, sfxPaperVolume, vol); }
    public void PlaySFX_Button(float vol = 1f) { PlaySFXClip(sfxButtonClick, sfxButtonClickVolume, vol); }

    // NEW helpers
    public void PlaySFX_Cabinet(float vol = 1f) { PlaySFXClip(sfxCabinet, sfxCabinetVolume, vol); }
    public void PlaySFX_Serve(float vol = 1f) { PlaySFXClip(sfxServe, sfxServeVolume, vol); }

    /// <summary>
    /// Play SFX clip using per-clip multiplier and global sfxVolume.
    /// playScale allows callers to further scale (default 1).
    /// </summary>
    public void PlaySFXClip(AudioClip clip, float clipVolumeMultiplier = 1f, float playScale = 1f)
    {
        if (clip == null)
        {
            // silent if clip not assigned (avoid warning spam)
            return;
        }
        var src = GetNextSfxSource();
        float finalVol = Mathf.Clamp01(sfxVolume * clipVolumeMultiplier * playScale);
        src.volume = finalVol; // set base; PlayOneShot's volume param is relative, but we'll use PlayOneShot for overlap safety
        src.PlayOneShot(clip, finalVol);
    }

    // Generic helpers (allow calling by name from scripts)
    public void PlayNamedSfx(string name, float vol = 1f)
    {
        switch (name?.ToLowerInvariant())
        {
            case "ingredient": PlaySFX_IngredientIn(vol); break;
            case "npcspawn": PlaySFX_NPCSpawn(vol); break;
            case "npcdespawn": PlaySFX_NPCDespawn(vol); break;
            case "paper": PlaySFX_Paper(vol); break;
            case "button": PlaySFX_Button(vol); break;
            case "cabinet": PlaySFX_Cabinet(vol); break;
            case "serve": PlaySFX_Serve(vol); break;
            default: Debug.LogWarning($"[AudioManager] Unknown sfx name '{name}'"); break;
        }
    }

    // -----------------------
    // Debug helpers
    // -----------------------
    public bool GetBgmIsPlaying()
    {
        return bgmSource != null && bgmSource.isPlaying;
    }

    public string GetCurrentBgmName()
    {
        return currentBgmClip != null ? currentBgmClip.name : "(none)";
    }
}