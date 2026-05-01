using UnityEngine;

public class AspectRatioHandler : MonoBehaviour
{
    void Start()
    {
        // Target aspect ratio (16:9)
        float targetAspect = 16.0f / 9.0f;

        // Mendapatkan aspect ratio layar HP saat ini
        float windowAspect = (float)Screen.width / Screen.height;

        // Menghitung skala tinggi yang dibutuhkan
        float scaleHeight = windowAspect / targetAspect;

        Camera camera = GetComponent<Camera>();

        // Jika layar lebih lebar dari 16:9 (seperti 21:9 atau layar HP modern)
        if (scaleHeight < 1.0f)
        {
            Rect rect = camera.rect;
            rect.width = 1.0f;
            rect.height = scaleHeight;
            rect.x = 0;
            rect.y = (1.0f - scaleHeight) / 2.0f;
            camera.rect = rect;
        }
        else // Jika layar lebih tinggi dari 16:9
        {
            float scaleWidth = 1.0f / scaleHeight;
            Rect rect = camera.rect;
            rect.width = scaleWidth;
            rect.height = 1.0f;
            rect.x = (1.0f - scaleWidth) / 2.0f;
            rect.y = 0;
            camera.rect = rect;
        }
    }
}