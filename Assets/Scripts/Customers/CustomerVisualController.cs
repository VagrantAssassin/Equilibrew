using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// CustomerVisualController
/// - Menyediakan FadeIn / FadeOut coroutine yang mendukung CanvasGroup, Image, dan SpriteRenderer.
/// - Pasang script ini pada prefab customer (atau parent root) dan atur fadeDuration jika perlu.
/// </summary>
[RequireComponent(typeof(Transform))]
public class CustomerVisualController : MonoBehaviour
{
    [Tooltip("Durasi fade in/out (seconds)")]
    public float fadeDuration = 0.25f;

    private CanvasGroup canvasGroup;
    private List<Image> images = new List<Image>();
    private List<SpriteRenderer> spriteRenderers = new List<SpriteRenderer>();

    private void Awake()
    {
        canvasGroup = GetComponentInChildren<CanvasGroup>(true);
        images.AddRange(GetComponentsInChildren<Image>(true));
        spriteRenderers.AddRange(GetComponentsInChildren<SpriteRenderer>(true));
    }

    public IEnumerator FadeInCoroutine(float durationOverride = -1f)
    {
        float dur = durationOverride > 0f ? durationOverride : fadeDuration;

        // If we have CanvasGroup, use it
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Clamp01(elapsed / dur);
                yield return null;
            }
            canvasGroup.alpha = 1f;
            yield break;
        }

        // Else animate Image and SpriteRenderer alpha
        float elapsed2 = 0f;
        // capture original alphas
        Dictionary<Graphic, float> imageAlphas = new Dictionary<Graphic, float>();
        foreach (var im in images) if (im != null) { var c = im.color; imageAlphas[im] = c.a; im.color = new Color(c.r, c.g, c.b, 0f); }
        Dictionary<SpriteRenderer, float> srAlphas = new Dictionary<SpriteRenderer, float>();
        foreach (var sr in spriteRenderers) if (sr != null) { var c = sr.color; srAlphas[sr] = c.a; sr.color = new Color(c.r, c.g, c.b, 0f); }

        while (elapsed2 < dur)
        {
            elapsed2 += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed2 / dur);
            foreach (var kv in imageAlphas)
            {
                var im = kv.Key as Image;
                if (im == null) continue;
                var orig = kv.Value;
                var c = im.color;
                im.color = new Color(c.r, c.g, c.b, orig * progress);
            }
            foreach (var kv in srAlphas)
            {
                var sr = kv.Key;
                if (sr == null) continue;
                var orig = kv.Value;
                var c = sr.color;
                sr.color = new Color(c.r, c.g, c.b, orig * progress);
            }
            yield return null;
        }

        // finalize
        foreach (var kv in imageAlphas) { var im = kv.Key as Image; if (im != null) { var c = im.color; im.color = new Color(c.r, c.g, c.b, kv.Value); } }
        foreach (var kv in srAlphas) { var sr = kv.Key; if (sr != null) { var c = sr.color; sr.color = new Color(c.r, c.g, c.b, kv.Value); } }
    }

    public IEnumerator FadeOutCoroutine(float durationOverride = -1f)
    {
        float dur = durationOverride > 0f ? durationOverride : fadeDuration;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.unscaledDeltaTime;
                canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / dur);
                yield return null;
            }
            canvasGroup.alpha = 0f;
            yield break;
        }

        float elapsed2 = 0f;
        Dictionary<Graphic, float> imageAlphas = new Dictionary<Graphic, float>();
        foreach (var im in images) if (im != null) { var c = im.color; imageAlphas[im] = c.a; }
        Dictionary<SpriteRenderer, float> srAlphas = new Dictionary<SpriteRenderer, float>();
        foreach (var sr in spriteRenderers) if (sr != null) { var c = sr.color; srAlphas[sr] = c.a; }

        while (elapsed2 < dur)
        {
            elapsed2 += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed2 / dur);
            float inv = 1f - progress;
            foreach (var kv in imageAlphas)
            {
                var im = kv.Key as Image;
                if (im == null) continue;
                var orig = kv.Value;
                var c = im.color;
                im.color = new Color(c.r, c.g, c.b, orig * inv);
            }
            foreach (var kv in srAlphas)
            {
                var sr = kv.Key;
                if (sr == null) continue;
                var orig = kv.Value;
                var c = sr.color;
                sr.color = new Color(c.r, c.g, c.b, orig * inv);
            }
            yield return null;
        }

        foreach (var kv in imageAlphas) { var im = kv.Key as Image; if (im != null) { var c = im.color; im.color = new Color(c.r, c.g, c.b, 0f); } }
        foreach (var kv in srAlphas) { var sr = kv.Key; if (sr != null) { var c = sr.color; sr.color = new Color(c.r, c.g, c.b, 0f); } }
    }
}