using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Equilibrew/Customer Profile", fileName = "CustomerProfile")]
public class CustomerProfile : ScriptableObject
{
    public string profileName;
    public Sprite portrait;
    [Tooltip("List of recipe names (string) that this profile prefers. Matched against Recipe.recipeName at runtime.")]
    public List<string> preferredRecipeNames = new List<string>();

    [Header("Curhat (compiled Ink JSON TextAsset)")]
    [Tooltip("Compiled Ink story (TextAsset JSON). Add 0..N stories; one will be picked randomly for 'curhat'.")]
    public List<TextAsset> curhatStories = new List<TextAsset>();

    [Header("Behaviour")]
    [Tooltip("How many wrong serves the customer tolerates before leaving (>=1).")]
    public int maxFails = 3;

    public Personality personality = Personality.Neutral;

    public enum Personality
    {
        Neutral,
        Cheerful,
        Grumpy,
        Picky,
        Shy
    }
}