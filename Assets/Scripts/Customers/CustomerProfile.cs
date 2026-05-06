using System;
using System.Collections.Generic;
using UnityEngine;

// Jika sudah punya CustomerProfile, tambahkan field-field ini
[CreateAssetMenu(fileName = "CustomerProfile", menuName = "Equilibrew/CustomerProfile", order = 1)]
public class CustomerProfile : ScriptableObject
{
    public string profileName;
    public Sprite portrait;

    [Header("Recipe / Order")]
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
    public TextAsset wrongStory;
    public TextAsset leaveStory;

    [Header("Behavior")]
    public int maxFails = 2;

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
    /// Reset ke 50 setiap kali game dimulai atau di-restart via ResetAffinity().
    /// </summary>
    [System.NonSerialized]
    public float affinity = 50f;

    /// <summary>
    /// Reset affinity ke nilai awal (50%).
    /// Dipanggil saat game mulai atau di-restart.
    /// </summary>
    public void ResetAffinity()
    {
        affinity = 50f;
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
}