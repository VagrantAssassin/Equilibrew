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

    [Header("Curhat (optional)")]
    public List<TextAsset> curhatStories = new List<TextAsset>();

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
}