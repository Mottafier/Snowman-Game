using UnityEngine;
using UnityEngine.UI;

// Flashes red around the edges of the screen when the player takes damage, then fades out.
// Creates its own overlay canvas the first time it's needed.
public class DamageFlash : MonoBehaviour
{
    private const float MaxAlpha = 0.6f;
    private const float FadeSeconds = 0.6f;

    private static DamageFlash instance;

    private static readonly Color DamageRed = new Color(0.85f, 0f, 0f);

    private Image overlay;
    private float alpha;
    private Color tint = DamageRed;

    // strength 0-1: bigger hits flash harder
    public static void Play(float strength = 1f)
    {
        Play(strength, DamageRed);
    }

    // Flash in another color, e.g. icy blue when the player is slowed
    public static void Play(float strength, Color color)
    {
        if (instance == null)
            instance = Create();
        instance.alpha = Mathf.Max(instance.alpha, MaxAlpha * Mathf.Clamp01(strength));
        instance.tint = color;
    }

    private static DamageFlash Create()
    {
        var go = new GameObject(nameof(DamageFlash));
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;

        var image = new GameObject("Overlay").AddComponent<Image>();
        image.transform.SetParent(go.transform, false);
        image.rectTransform.anchorMin = Vector2.zero;
        image.rectTransform.anchorMax = Vector2.one;
        image.rectTransform.offsetMin = Vector2.zero;
        image.rectTransform.offsetMax = Vector2.zero;
        image.sprite = CreateVignetteSprite();
        image.raycastTarget = false;
        image.enabled = false;

        var flash = go.AddComponent<DamageFlash>();
        flash.overlay = image;
        return flash;
    }

    void Update()
    {
        if (alpha <= 0f)
        {
            overlay.enabled = false;
            return;
        }

        overlay.enabled = true;
        overlay.color = new Color(tint.r, tint.g, tint.b, alpha);
        alpha = Mathf.MoveTowards(alpha, 0f, MaxAlpha / FadeSeconds * Time.unscaledDeltaTime);
    }

    // White texture that's faint in the middle and solid at the edges; the Image tints it red
    private static Sprite CreateVignetteSprite()
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1.3f, Mathf.Sqrt(dx * dx + dy * dy)));
                float a = Mathf.Lerp(0.25f, 1f, edge);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}
