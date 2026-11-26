using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Time of day for the district while a test drive is on. The sun ("Exterior Daylight") rises in the east, crosses
/// the sky and sets in the west over <see cref="DayLengthMinutes"/> real minutes for a full 24 hours; its colour warms
/// towards the horizon, sky exposure, ambient light, reflections and fog darken with it, a cool moonlight takes over
/// at night, and the street lanterns switch on at dusk and off at dawn. The drive starts in late afternoon so the
/// first sunset comes after a few minutes. Back in the showroom, the original lighting is restored.
/// </summary>
public sealed class TestDriveDayNight : MonoBehaviour
{
    /// <summary>Real minutes for one full day (24 game hours). 24 = one game hour per real minute.</summary>
    public const float DayLengthMinutes = 24f;
    public const float StartHour = 16.5f;

    private Light sun;
    private Material sky;
    private Material originalSky;
    private readonly List<Light> lamps = new List<Light>();
    private readonly List<float> lampIntensity = new List<float>();
    private readonly List<float> lampRange = new List<float>();

    // Original lighting.
    private Quaternion sunRotation;
    private float sunIntensity, ambientIntensity, reflectionIntensity, skyExposure = 1f;
    private Color sunColor, fogColor, skyTint = Color.white;
    private float baseAzimuth;
    private bool running, captured;
    private UnityEngine.Rendering.AmbientMode ambientMode;
    private UnityEngine.Rendering.SphericalHarmonicsL2 ambientProbe;
    private Color daySky, dayEquator, dayGround;

    public float Hour { get; private set; } = StartHour;
    /// <summary>0 at night, 1 in full daylight.</summary>
    public float Daylight { get; private set; } = 1f;
    public bool IsNight => Daylight < 0.3f;

    public static TestDriveDayNight Create(Transform parent)
    {
        var go = new GameObject("Test Drive Time of Day");
        go.transform.SetParent(parent, false);
        TestDriveDayNight world = go.AddComponent<TestDriveDayNight>();
        world.enabled = false;
        return world;
    }

    public void SetRunning(bool on)
    {
        if (on == running) return;
        if (on)
        {
            Capture();
            if (sun == null) return;
            running = true;
            enabled = true;
            Apply();
        }
        else
        {
            running = false;
            enabled = false;
            Restore();
        }
    }

    private void Capture()
    {
        GameObject found = GameObject.Find("Exterior Daylight");
        sun = found != null ? found.GetComponent<Light>() : RenderSettings.sun;
        if (sun == null) return;
        sunRotation = sun.transform.rotation;
        sunIntensity = sun.intensity;
        sunColor = sun.color;
        baseAzimuth = sun.transform.eulerAngles.y;
        ambientIntensity = RenderSettings.ambientIntensity;
        // The scene's ambient light as three colours (sky, horizon, ground), read off its ambient probe, so the time
        // of day can drive it directly: changing the intensity of a skybox ambient does nothing until the probe is
        // re-baked, which is why the first night stayed lit like day in the shade and black in the open.
        ambientMode = RenderSettings.ambientMode;
        ambientProbe = RenderSettings.ambientProbe;
        var directions = new[] { Vector3.up, Vector3.forward, Vector3.down };
        var colours = new Color[3];
        ambientProbe.Evaluate(directions, colours);
        daySky = colours[0]; dayEquator = colours[1]; dayGround = colours[2];
        if (daySky.maxColorComponent < 0.05f) { daySky = new Color(0.55f, 0.62f, 0.72f); dayEquator = new Color(0.48f, 0.5f, 0.52f); dayGround = new Color(0.25f, 0.24f, 0.22f); }
        reflectionIntensity = RenderSettings.reflectionIntensity;
        fogColor = RenderSettings.fogColor;
        originalSky = RenderSettings.skybox;
        if (originalSky != null && sky == null)
        {
            sky = new Material(originalSky) { name = originalSky.name + " (time of day)" };
            if (sky.HasProperty("_Exposure")) skyExposure = sky.GetFloat("_Exposure");
            if (sky.HasProperty("_Tint")) skyTint = sky.GetColor("_Tint");
        }
        if (sky != null) RenderSettings.skybox = sky;
        if (lamps.Count == 0)
        {
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (l.name != "Warm Road LED") continue;
                lamps.Add(l);
                lampIntensity.Add(l.intensity);
                lampRange.Add(l.range);
            }
        }
        captured = true;
    }

    private void Restore()
    {
        if (!captured || sun == null) return;
        sun.transform.rotation = sunRotation;
        sun.intensity = sunIntensity;
        sun.color = sunColor;
        sun.shadowStrength = Mathf.Max(sun.shadowStrength, 0.72f);
        RenderSettings.ambientIntensity = ambientIntensity;
        RenderSettings.ambientMode = ambientMode;
        RenderSettings.ambientProbe = ambientProbe;
        RenderSettings.reflectionIntensity = reflectionIntensity;
        RenderSettings.fogColor = fogColor;
        if (originalSky != null) RenderSettings.skybox = originalSky;
        for (int i = 0; i < lamps.Count; i++)
        {
            if (lamps[i] == null) continue;
            lamps[i].intensity = lampIntensity[i];
            lamps[i].range = lampRange[i];
            lamps[i].enabled = true;
        }
    }

    private void Update()
    {
        if (!running || sun == null) return;
        Hour = Mathf.Repeat(Hour + Time.deltaTime * 24f / (DayLengthMinutes * 60f), 24f);
        Apply();
    }

    private void Apply()
    {
        // Sun path: up at 6:00, highest (62°) at 13:00, down at 20:00; azimuth sweeps east to west.
        float dayAngle = (Hour - 6f) / 14f * Mathf.PI;
        float elevation = Mathf.Sin(dayAngle) * 62f;
        if (Hour < 6f || Hour > 20f) elevation = -Mathf.Abs(Mathf.Sin((Hour < 6f ? Hour + 4f : Hour - 20f) / 10f * Mathf.PI)) * 30f - 2f;
        float azimuth = baseAzimuth + (Hour - 13f) * 15f;

        float day = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-5f, 12f, elevation));
        Daylight = day;
        float golden = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f, 28f, elevation));

        if (elevation > -1.5f)
        {
            sun.transform.rotation = Quaternion.Euler(Mathf.Max(elevation, 1f), azimuth, 0f);
            Color warm = Color.Lerp(sunColor, new Color(1f, 0.52f, 0.26f), golden * 0.85f);
            sun.color = warm;
            sun.intensity = sunIntensity * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-1.5f, 10f, elevation));
            sun.shadowStrength = 0.72f;
        }
        else
        {
            // Moonlight from the opposite side of the sky.
            float moon = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-1.5f, -8f, elevation));
            sun.transform.rotation = Quaternion.Euler(38f, azimuth + 180f, 0f);
            sun.color = new Color(0.58f, 0.68f, 1f);
            sun.intensity = 0.2f * moon;
            sun.shadowStrength = 0.45f;
        }

        // Night is dark but readable (moonlit sky, city glow), not black.
        // Night is dark but readable: a moonlit sky and the city's glow, not black.
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        Color tint = new Color(1f, 0.82f, 0.7f);
        RenderSettings.ambientSkyColor = Color.Lerp(new Color(0.11f, 0.13f, 0.2f), Color.Lerp(daySky, daySky * tint, golden * 0.5f), day);
        RenderSettings.ambientEquatorColor = Color.Lerp(new Color(0.1f, 0.095f, 0.11f), Color.Lerp(dayEquator, dayEquator * tint, golden * 0.6f), day);
        RenderSettings.ambientGroundColor = Color.Lerp(new Color(0.06f, 0.05f, 0.045f), dayGround, day);
        // For the sea and the shop glass (their shaders read these): sky, sun (or moon) and the shops' own light.
        Shader.SetGlobalColor("_TestDriveSkyTop", Color.Lerp(new Color(0.03f, 0.045f, 0.09f), RenderSettings.ambientSkyColor * 1.5f, day));
        Shader.SetGlobalColor("_TestDriveSkyHorizon", RenderSettings.fogColor * 1.1f);
        Shader.SetGlobalVector("_TestDriveSunDir", -sun.transform.forward);
        Shader.SetGlobalColor("_TestDriveSunColor", sun.color * sun.intensity);
        Shader.SetGlobalFloat("_TestDriveShopLight", Mathf.Lerp(1.0f, 0.75f, day));
        RenderSettings.reflectionIntensity = Mathf.Lerp(0.2f, reflectionIntensity, day);
        Color nightFog = new Color(0.035f, 0.045f, 0.08f);
        Color duskFog = new Color(0.78f, 0.55f, 0.42f);
        RenderSettings.fogColor = Color.Lerp(nightFog, Color.Lerp(fogColor, duskFog, golden * 0.6f), day);
        if (sky != null)
        {
            if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", Mathf.Lerp(0.09f, skyExposure, day));
            if (sky.HasProperty("_Tint"))
                sky.SetColor("_Tint", Color.Lerp(new Color(0.3f, 0.36f, 0.6f), Color.Lerp(skyTint, new Color(1f, 0.72f, 0.55f), golden * 0.5f), day));
            if (sky.HasProperty("_Rotation")) sky.SetFloat("_Rotation", Mathf.Repeat(-(Hour - StartHour) * 3f, 360f));
        }

        // Street lanterns: on at dusk, fading in as the light goes.
        float lampLevel = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.15f, day));
        for (int i = 0; i < lamps.Count; i++)
        {
            if (lamps[i] == null) continue;
            // The lanterns light the carriageway 9 m below them: five times their daytime setting, wider reach.
            lamps[i].enabled = lampLevel > 0.01f;
            lamps[i].intensity = lampIntensity[i] * 5f * lampLevel;
            lamps[i].range = lampRange[i] * 1.6f;
        }
    }
}
