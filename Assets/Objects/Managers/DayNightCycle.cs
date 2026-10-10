using UnityEngine;
using UnityEngine.Rendering;

// Spins the sun around the world so day turns to night and back. At night a dim blue moon lights the way,
// and the ambient light and sky follow along (the sky is Unity's procedural one, which follows the sun).
// Created automatically, so it doesn't need to be added to the scene. Change cycleSeconds in the Inspector during Play.
public class DayNightCycle : MonoBehaviour
{
    public float cycleSeconds = 10f;  // one full day and night
    [Range(0f, 1f)] public float timeOfDay = 0.3f; // 0 = midnight, 0.25 = sunrise, 0.5 = noon, 0.75 = sunset

    private const float SunYaw = -30f; // which way the sun crosses the sky

    private static readonly Color NoonLight = new Color(1f, 0.97f, 0.9f);
    private static readonly Color HorizonLight = new Color(1f, 0.55f, 0.3f);
    private static readonly Color MoonLight = new Color(0.55f, 0.65f, 1f);
    private static readonly Color DaySky = new Color(0.55f, 0.62f, 0.75f);
    private static readonly Color DayEquator = new Color(0.45f, 0.48f, 0.55f);
    private static readonly Color DayGround = new Color(0.3f, 0.3f, 0.32f);
    private static readonly Color NightSky = new Color(0.06f, 0.08f, 0.18f);
    private static readonly Color NightEquator = new Color(0.04f, 0.05f, 0.12f);
    private static readonly Color NightGround = new Color(0.02f, 0.02f, 0.05f);

    private Light sun;
    private Light moon;
    private float sunIntensity = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateInstance()
    {
        if (FindAnyObjectByType<DayNightCycle>() != null)
            return;
        new GameObject(nameof(DayNightCycle)).AddComponent<DayNightCycle>();
    }

    void Start()
    {
        // The scene's brightest directional light becomes the sun
        foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type == LightType.Directional && (sun == null || light.intensity > sun.intensity))
                sun = light;
        }
        if (sun == null)
        {
            sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
        }
        sunIntensity = sun.intensity;
        RenderSettings.sun = sun;

        moon = new GameObject("Moon").AddComponent<Light>();
        moon.transform.SetParent(transform, false);
        moon.type = LightType.Directional;
        moon.color = MoonLight;
        moon.shadows = LightShadows.None;

        RenderSettings.ambientMode = AmbientMode.Trilight;
    }

    void Update()
    {
        if (sun == null)
            return;

        timeOfDay = Mathf.Repeat(timeOfDay + Time.deltaTime / Mathf.Max(cycleSeconds, 0.1f), 1f);

        // Pitch -90 at midnight (straight up from below), 0 at sunrise, 90 at noon, 180 at sunset
        float pitch = timeOfDay * 360f - 90f;
        sun.transform.rotation = Quaternion.Euler(pitch, SunYaw, 0f);
        moon.transform.rotation = Quaternion.Euler(pitch + 180f, SunYaw, 0f);

        // How high the sun is: 1 at noon, 0 on the horizon, negative at night
        float height = Mathf.Sin(pitch * Mathf.Deg2Rad);
        float day = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.1f, 0.25f, height));

        sun.intensity = sunIntensity * Mathf.Clamp01(height * 4f);
        sun.color = Color.Lerp(HorizonLight, NoonLight, Mathf.Clamp01(height * 2.5f));
        sun.enabled = height > 0f;
        moon.intensity = 0.35f * (1f - day);
        moon.enabled = moon.intensity > 0.01f;

        RenderSettings.ambientSkyColor = Color.Lerp(NightSky, DaySky, day);
        RenderSettings.ambientEquatorColor = Color.Lerp(NightEquator, DayEquator, day);
        RenderSettings.ambientGroundColor = Color.Lerp(NightGround, DayGround, day);
    }
}
