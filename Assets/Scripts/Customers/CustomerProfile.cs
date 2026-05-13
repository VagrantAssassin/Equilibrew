using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[Serializable]
public class CustomerOrderOption
{
    public string recipeName;
    public TextAsset orderStory;
}

[CreateAssetMenu(fileName = "CustomerProfile", menuName = "Equilibrew/CustomerProfile", order = 1)]
public class CustomerProfile : ScriptableObject
{
    [Header("Category Identity")]
    [FormerlySerializedAs("profileName")]
    public string categoryName;

    [Tooltip("Nama pelanggan yang mungkin muncul ketika kategori ini datang.")]
    public List<string> possibleNames = new List<string>();

    // Backward compatibility for existing scripts that still reference profileName.
    public string profileName => categoryName;

    [Header("Legacy fallback portrait (optional)")]
    public Sprite portrait;

    [Header("Body Part Sprites (acak per kedatangan)")]
    public List<Sprite> headSprites = new List<Sprite>();
    public List<Sprite> hairSprites = new List<Sprite>();
    public List<Sprite> shirtSprites = new List<Sprite>();

    [Header("Order setup (recommended)")]
    [Tooltip("Pasangan resep + dialog order yang diacak saat pelanggan kategori ini datang.")]
    public List<CustomerOrderOption> orderOptions = new List<CustomerOrderOption>();

    [Header("Legacy order fallback (opsional)")]
    public List<string> preferredRecipeNames = new List<string>();
    public List<TextAsset> orderStories = new List<TextAsset>();

    [Header("Order Stories per Affinity Tier (optional, index matches preferredRecipeNames)")]
    [Tooltip("Order dialog variants used when affinity tier is Hostile (<=35%). Falls back to orderStories if empty.")]
    public List<TextAsset> orderStoriesHostile = new List<TextAsset>();
    [Tooltip("Order dialog variants used when affinity tier is Friend (35%-75%). Falls back to orderStories if empty.")]
    public List<TextAsset> orderStoriesFriend = new List<TextAsset>();
    [Tooltip("Order dialog variants used when affinity tier is BestFriend (75%-100%). Falls back to orderStories if empty.")]
    public List<TextAsset> orderStoriesBestFriend = new List<TextAsset>();
    [Tooltip("Order dialog variants used when affinity tier is Soulmate (100%). Falls back to orderStories if empty.")]
    public List<TextAsset> orderStoriesSoulmate = new List<TextAsset>();

    [Header("Curhat (optional)")]
    public List<TextAsset> curhatStories = new List<TextAsset>();

    [Header("Curhat Stories per Affinity Tier (optional, picked randomly within tier)")]
    [Tooltip("Curhat dialog variants for Hostile tier. Falls back to curhatStories if empty.")]
    public List<TextAsset> curhatStoriesHostile = new List<TextAsset>();
    [Tooltip("Curhat dialog variants for Friend tier. Falls back to curhatStories if empty.")]
    public List<TextAsset> curhatStoriesFriend = new List<TextAsset>();
    [Tooltip("Curhat dialog variants for BestFriend tier. Falls back to curhatStories if empty.")]
    public List<TextAsset> curhatStoriesBestFriend = new List<TextAsset>();
    [Tooltip("Curhat dialog variants for Soulmate tier. Falls back to curhatStories if empty.")]
    public List<TextAsset> curhatStoriesSoulmate = new List<TextAsset>();

    [Header("Outcome dialogs (optional)")]
    public TextAsset successStory;
    public List<TextAsset> wrongStories = new List<TextAsset>();
    public TextAsset wrongStory;
    [Tooltip("Leave dialog variants when customer decides to leave. Falls back to leaveStory if empty.")]
    public List<TextAsset> leaveStories = new List<TextAsset>();
    public TextAsset leaveStory;

    [Header("Behavior")]
    [Tooltip("Range max gagal serve sebelum pelanggan pergi. Nilai akhir akan diacak per kedatangan.")]
    public int minMaxFails = 1;
    public int maxMaxFails = 3;

    [Header("Legacy fallback (opsional)")]
    public int maxFails = 2;

    [Header("Affinity")]
    [Tooltip("Range affinity awal (0-100). Akan diacak per kedatangan dengan kelipatan 5.")]
    [Range(0, 100)] public int minStartingAffinity = 30;
    [Range(0, 100)] public int maxStartingAffinity = 70;

    [Header("Legacy affinity fallback (opsional)")]
    public float startingAffinity = 50f;

    [Header("Reaction scoring (per-profile override)")]
    [Tooltip("Points awarded when curhat result is SATISFY (positive reaction).")]
    public int pointsOnSatisfy = 5;
    [Tooltip("Points awarded when curhat result is NEUTRAL.")]
    public int pointsOnNeutral = 0;
    [Tooltip("HP lost (positive integer) when curhat result is ANGRY. Set 0 to disable.")]
    public int hpLossOnAngry = 1;

    [Header("Background / Bio (editable)")]
    [Tooltip("Short background / biography text for the customer (e.g. 'yatim piatu, bercita-cita jadi ...').")]
    [TextArea(3, 6)]
    public string background = "";

    // -----------------------------------------------------------------------
    // Affinity System (runtime only, not serialized to disk)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Nilai affinity runtime NPC ini (0-100). Tidak di-serialize ke asset disk.
    /// Reset ke startingAffinity setiap kali game dimulai atau di-restart via ResetAffinity().
    /// </summary>
    [System.NonSerialized]
    public float affinity = 50f;

    /// <summary>
    /// Reset affinity ke nilai random dalam range affinity awal dengan step 5.
    /// </summary>
    public void ResetAffinity()
    {
        affinity = GetRandomStartingAffinityStep5();
    }

    /// <summary>
    /// Ubah affinity, jaga tetap dalam rentang 0-100.
    /// </summary>
    public void ChangeAffinity(float delta)
    {
        affinity = Mathf.Clamp(affinity + delta, 0f, 100f);
    }

    /// <summary>
    /// Kembalikan tier affinity saat ini berdasarkan nilai affinity.
    /// Hostile:    affinity &lt;= 35
    /// Friend:     35 &lt; affinity &lt;= 75
    /// BestFriend: 75 &lt; affinity &lt; 100
    /// Soulmate:   affinity == 100
    /// </summary>
    public AffinityTier GetCurrentTier()
    {
        if (affinity >= 100f) return AffinityTier.Soulmate;
        if (affinity > 75f)   return AffinityTier.BestFriend;
        if (affinity > 35f)   return AffinityTier.Friend;
        return AffinityTier.Hostile;
    }

    public string GetRandomDisplayName()
    {
        if (possibleNames != null && possibleNames.Count > 0)
        {
            var filtered = possibleNames.FindAll(n => !string.IsNullOrWhiteSpace(n));
            if (filtered.Count > 0)
                return filtered[UnityEngine.Random.Range(0, filtered.Count)];
        }

        return string.IsNullOrWhiteSpace(categoryName) ? name : categoryName;
    }

    public int GetRandomMaxFails()
    {
        int min = Mathf.Max(1, minMaxFails);
        int max = Mathf.Max(min, maxMaxFails);
        return UnityEngine.Random.Range(min, max + 1);
    }

    public TextAsset GetRandomWrongStory()
    {
        if (wrongStories != null && wrongStories.Count > 0)
        {
            var valid = wrongStories.FindAll(w => w != null);
            if (valid.Count > 0)
                return valid[UnityEngine.Random.Range(0, valid.Count)];
        }

        return wrongStory;
    }

    public TextAsset GetRandomLeaveStory()
    {
        if (leaveStories != null && leaveStories.Count > 0)
        {
            var valid = leaveStories.FindAll(story => story != null);
            if (valid.Count > 0)
                return valid[UnityEngine.Random.Range(0, valid.Count)];
        }

        return leaveStory;
    }

    public Sprite GetRandomHeadSprite() => GetRandomSpriteFrom(headSprites);
    public Sprite GetRandomHairSprite() => GetRandomSpriteFrom(hairSprites);
    public Sprite GetRandomShirtSprite() => GetRandomSpriteFrom(shirtSprites);

    public bool TryGetRandomOrder(out string recipeName, out TextAsset orderStory)
    {
        recipeName = null;
        orderStory = null;

        if (orderOptions != null && orderOptions.Count > 0)
        {
            var valid = orderOptions.FindAll(o => o != null && !string.IsNullOrWhiteSpace(o.recipeName));
            if (valid.Count > 0)
            {
                var pick = valid[UnityEngine.Random.Range(0, valid.Count)];
                recipeName = pick.recipeName;
                orderStory = pick.orderStory;
                return true;
            }
        }

        if (preferredRecipeNames == null || preferredRecipeNames.Count == 0)
            return false;

        int idx = UnityEngine.Random.Range(0, preferredRecipeNames.Count);
        recipeName = preferredRecipeNames[idx];
        if (orderStories != null && idx < orderStories.Count)
            orderStory = orderStories[idx];

        return !string.IsNullOrWhiteSpace(recipeName);
    }

    private float GetRandomStartingAffinityStep5()
    {
        int min = Mathf.Clamp(minStartingAffinity, 0, 100);
        int max = Mathf.Clamp(maxStartingAffinity, 0, 100);

        if (max < min)
        {
            int swap = min;
            min = max;
            max = swap;
        }

        int minStep = Mathf.CeilToInt(min / 5f);
        int maxStep = Mathf.FloorToInt(max / 5f);
        if (maxStep < minStep)
        {
            int legacy = Mathf.RoundToInt(Mathf.Clamp(startingAffinity, 0f, 100f));
            return Mathf.Clamp(Mathf.RoundToInt(legacy / 5f) * 5, 0, 100);
        }

        return UnityEngine.Random.Range(minStep, maxStep + 1) * 5;
    }

    private Sprite GetRandomSpriteFrom(List<Sprite> list)
    {
        if (list == null || list.Count == 0)
            return null;

        var valid = list.FindAll(s => s != null);
        if (valid.Count == 0)
            return null;

        return valid[UnityEngine.Random.Range(0, valid.Count)];
    }
}
