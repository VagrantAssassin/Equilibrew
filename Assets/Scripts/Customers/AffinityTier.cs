/// <summary>
/// AffinityTier - Kategori affinity NPC berdasarkan nilai affinity saat ini.
/// Hostile:    affinity &lt;= 35%
/// Friend:     35% &lt; affinity &lt;= 75%
/// BestFriend: 75% &lt; affinity &lt; 100%
/// Soulmate:   affinity == 100%
/// </summary>
public enum AffinityTier
{
    Hostile,
    Friend,
    BestFriend,
    Soulmate
}
