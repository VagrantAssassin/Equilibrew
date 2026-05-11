using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// CustomerAffinityWidget
/// 
/// Widget UI yang menampilkan affinity pelanggan aktif:
///   - Gambar hati (Image) yang berubah warna sesuai AffinityTier
///   - Teks angka affinity (TextMeshProUGUI) di tengah hati
/// 
/// Cara pakai:
///   1. Buat Canvas/Panel UI dengan GameObject bertipe Image (hati) dan child TMP.
///   2. Pasang script ini pada root GameObject widget tersebut.
///   3. Assign heartImage dan affinityText di Inspector.
///   4. Atur warna tiap tier di Inspector.
///   5. Assign widget ini ke CustomerManager.affinityWidget di Inspector.
/// </summary>
public class CustomerAffinityWidget : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Image komponen hati yang warnanya akan berubah sesuai tier.")]
    public Image heartImage;

    [Tooltip("TMP text di tengah hati yang menampilkan angka affinity (0-100).")]
    public TextMeshProUGUI affinityText;

    [Header("Tier Colors (atur di Inspector)")]
    [Tooltip("Warna hati saat tier Hostile (affinity ≤ 35%).")]
    public Color colorHostile = Color.white;

    [Tooltip("Warna hati saat tier Friend (35% < affinity ≤ 75%).")]
    public Color colorFriend = new Color(0.4f, 0.85f, 0.4f, 1f); // hijau

    [Tooltip("Warna hati saat tier BestFriend (75% < affinity < 100%).")]
    public Color colorBestFriend = new Color(1f, 0.65f, 0f, 1f); // oranye

    [Tooltip("Warna hati saat tier Soulmate (affinity = 100%).")]
    public Color colorSoulmate = new Color(1f, 0.2f, 0.4f, 1f); // merah muda

    [Header("Text format")]
    [Tooltip("Format string untuk angka affinity. {0} akan diganti nilai affinity (dibulatkan).")]
    public string textFormat = "{0}";

    private void Awake()
    {
        // Sembunyikan widget saat awal; CustomerManager akan menampilkannya saat customer spawn.
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Perbarui tampilan widget berdasarkan nilai dan tier affinity saat ini.
    /// Dipanggil oleh CustomerManager setiap kali affinity berubah.
    /// </summary>
    public void UpdateDisplay(float affinity, AffinityTier tier)
    {
        // Perbarui warna hati
        if (heartImage != null)
            heartImage.color = GetColorForTier(tier);

        // Perbarui teks angka
        if (affinityText != null)
            affinityText.text = string.Format(textFormat, Mathf.RoundToInt(affinity));
    }

    /// <summary>
    /// Tampilkan widget menggunakan nilai yang sudah di-set sebelumnya (tanpa memperbarui nilai).
    /// </summary>
    public void Show()
    {
        gameObject.SetActive(true);
    }

    /// <summary>
    /// Tampilkan widget dan langsung perbarui nilainya.
    /// </summary>
    public void Show(float affinity, AffinityTier tier)
    {
        gameObject.SetActive(true);
        UpdateDisplay(affinity, tier);
    }

    /// <summary>
    /// Sembunyikan widget (misal saat tidak ada customer aktif).
    /// </summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private Color GetColorForTier(AffinityTier tier)
    {
        switch (tier)
        {
            case AffinityTier.Soulmate:   return colorSoulmate;
            case AffinityTier.BestFriend: return colorBestFriend;
            case AffinityTier.Friend:     return colorFriend;
            default:                      return colorHostile;
        }
    }
}
