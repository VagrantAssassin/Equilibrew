using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CustomerProfile:
/// - profileName: label
/// - portrait: sprite shown in customer UI prefab
/// - preferredRecipeNames: list nama resep (string) -> dicocokkan ke Recipe.recipeName di runtime
/// - personality: enum (placeholder)
/// - maxFails: berapa kali pelanggan akan menerima salah sebelum pergi
/// </summary>
[CreateAssetMenu(menuName = "Equilibrew/Customer Profile", fileName = "CustomerProfile")]
public class CustomerProfile : ScriptableObject
{
    public string profileName;
    public Sprite portrait;
    [Tooltip("List of recipe names (string) that this profile prefers. Matched against Recipe.recipeName at runtime.")]
    public List<string> preferredRecipeNames = new List<string>();

    public Personality personality = Personality.Neutral;

    [Header("Behaviour")]
    [Tooltip("How many times the customer tolerates a wrong serve before leaving (>=1).")]
    public int maxFails = 3;

    public enum Personality
    {
        Neutral,
        Cheerful,
        Grumpy,
        Picky,
        Shy
    }
}