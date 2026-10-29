#if UNITY_EDITOR
// Aurelle realism: rendering and lighting foundation.
//
// PC  (quality level 5 "Ultra", Quality URP Config + Standalone Preset): HDR, ACES post stack through a global volume,
//     SMAA on the desktop camera, Forward+, SSAO, 4096 main-light shadows, real-time soft shadow from the hero key light.
// Quest (quality level 1 "Low", Performance URP Config + Android Preset): unchanged pipeline asset; the Android renderer
//     has no PostProcessData, so post-processing and SSAO can never run there, whatever a camera asks for.
// Scene: baked indirect GI for the showroom only (exterior stays realtime), baked reflection probes, light probes,
//     re-tuned showroom lights. Everything is found by name at run time and the tool is idempotent.
// Hero studio (Showroom Lighting/Aurelle Hero Studio): a flush ceiling frame of diffuse light boxes over the hero car
//     (long highlight lines on the paint, captured by the bake and the 512 px hero probe), the generated contact
//     shadow under the car (AurelleHeroContactShadow, every quality level) and the podium's planar reflection
//     (AurellePlanarReflection, Ultra on the desktop only, drawn through the SSAO-free AurelleMirrorRenderer).
//
// Entry points
//   Menu  Aurelle/Realism/Rendering/Apply Render Setup and Bake Lighting   (normal use)
//   Menu  Aurelle/Realism/Rendering/Apply Render Setup (no bake)           (preparation only; bake afterwards)
//   Batch -executeMethod AurelleRenderSetup.BatchBake                      (apply + bake + save + exit)
//   Batch -executeMethod AurelleRenderSetup.Batch                          (apply + save + exit, no bake)
// Run it LAST, after every other tool that adds or replaces showroom geometry, because the GI/static flags, probe
// placement and the bake depend on what is in the scene.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class AurelleRenderSetup
{
    // ------------------------------------------------------------------ paths

    public const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const string PcPipelinePath = "Assets/Settings/Project Configuration/Quality URP Config.asset";
    const string PcRendererPath = "Assets/Settings/Project Configuration/Standalone Preset.asset";
    const string QuestPipelinePath = "Assets/Settings/Project Configuration/Performance URP Config.asset";
    const string QuestRendererPath = "Assets/Settings/Project Configuration/Android Preset.asset";
    const string RenderingFolder = "Assets/Settings/Rendering";
    const string PostProfilePath = RenderingFolder + "/AurellePostProfile.asset";
    const string LightingSettingsPath = RenderingFolder + "/AurelleLighting.lighting";
    const string MaterialFolder = "Assets/Materials/Realism/Rendering";
    const string LensMaterialPath = MaterialFolder + "/AurelleDownlightLens.mat";
    const string OccluderMaterialPath = MaterialFolder + "/AurelleBakeOccluder.mat";
    const string SoftboxMaterialPath = MaterialFolder + "/AurelleStudioSoftbox.mat";
    const string SoftboxHousingMaterialPath = MaterialFolder + "/AurelleStudioSoftboxHousing.mat";
    const string ContactShadowTexturePath = MaterialFolder + "/AurelleHeroContactShadow.png";
    const string ContactShadowMaterialPath = MaterialFolder + "/AurelleHeroContactShadow.mat";
    const string MirrorRendererPath = RenderingFolder + "/AurelleMirrorRenderer.asset";
    const int PcQualityLevel = 5;
    const string Tag = "[AurelleRender] ";

    // ------------------------------------------------------------------ scene names (naming contracts)

    const string LightingRootName = "Showroom Lighting";
    const string EnvironmentRootName = "Showroom Environment";
    const string InteriorRootName = "Showroom Interior Zones";
    const string PremiumRootName = "Showroom Premium Collection";
    const string HeroCarName = "CarConcept";
    const string V6Name = "V6_Engine_Prototype(Clone)";
    const string PostVolumeName = "Aurelle Post Volume";
    const string DownlightGridName = "Aurelle Downlights";
    const string BakeOccluderRootName = "Aurelle Bake Occluders";
    const string MainProbeName = "Showroom Reflection Probe";
    const string HeroProbeName = "Hero Car Reflection Probe";
    const string PremiumProbePrefix = "Premium Car Reflection Probe - ";
    const string ShowroomProbeGroupName = "Light Probe Group";
    const string ExteriorProbeGroupName = "Exterior Light Probe Grid";
    const string HeroStudioName = "Aurelle Hero Studio";
    const string PodiumDeckPath = EnvironmentRootName + "/Circular Display/Inset Display Surface";

    // ------------------------------------------------------------------ tuning

    // Downlights and wall washers are baked: they cost nothing at run time on Quest, light every static surface
    // through the lightmaps (no per-object light limit) and cast soft baked shadows. Dynamic objects get them
    // through the light probes. The hero car keeps two mixed (real-time direct) lights; the key casts a real-time
    // soft shadow on PC (Quest has no additional-light shadows).
    const float DownlightIntensity = 55f, DownlightKelvin = 4700f, DownlightOuter = 96f, DownlightInner = 44f, DownlightRange = 14f, RowSpacing = 6f;
    const float DownlightShadowRadius = 0.3f;
    const float CarDownlightIntensity = 60f, CarDownlightOuter = 72f, CarDownlightInner = 34f;
    const float WasherIntensity = 110f, WasherKelvin = 4300f, WasherOuter = 90f, WasherInner = 50f, WasherRange = 10f, WasherSetback = 2.2f;
    // Studio look: the softbox canopy carries the top light (bake, probes, paint reflections); the key is a softer
    // front-quarter modelling light with a softer (1024 tier) shadow, the rim a cool edge from behind. Lower than the
    // first pass so that clear-coat and polished-stone highlights stay out of the clipped range.
    const float KeyIntensity = 58f, KeyKelvin = 5000f, KeyOuter = 64f, KeyInner = 22f, KeyRange = 11f; // range keeps the premium cars out of its shadow map
    const float KeyShadowStrength = 0.8f;
    const float RimIntensity = 28f, RimKelvin = 6500f, RimOuter = 62f, RimInner = 30f, RimRange = 13f;
    // Softbox canopy: two long strips along the car and two short cross strips, flush under the ceiling, centred on
    // the hero car and kept inside the inner LED ring's suspension rods. Baked emissive (lights the bake), captured
    // by the probes, excluded from nothing.
    const float CanopyHalfWidth = 1.05f, CanopyHalfLength = 2.4f, CanopyStrip = 0.4f, CanopyDepth = 0.08f, CanopyLip = 0.025f;
    const float CanopyEmission = 7.5f, CanopyKelvin = 5200f;
    // Hero probe: 512 px on PC (twice the hall probes; the softbox lines stay crisp on the paint), 256 px on Android.
    // Not 1024: Forward+ copies every visible probe into an octahedral atlas at four times its face size, so a 1024 px
    // probe takes a 4096 px region and grows the atlas to 8192 px (256 MB at B10G11R11), and its first upload showed
    // a one-frame garbage reflection on the glazing in the studio lab.
    const int HeroProbeResolution = 512, HeroProbeAndroidMax = 256;
    const float HeroProbeBlend = 0.6f;
    // Planar reflection: layer the car moves to at run time (named by this tool), lowest quality level that draws it.
    const int MirrorLayer = 30;
    const string MirrorLayerName = "Aurelle Planar Mirror";
    // Sky light: the builder's 0.62 / 0.55 made shaded facades read black once the fake fill light went away.
    const float AmbientIntensity = 1.0f, SkyReflectionIntensity = 0.8f;
    const float StreetLampIntensity = 12f;
    const bool UseHdrLensMaterial = true;
    const float LensEmission = 8f;
    // Premium display cars stay dynamic (probe-lit, no GI flags). Bake-only box occluders, active only while the
    // bake runs, give the floor soft contact shadows under them. Set false to bake without them.
    const bool PremiumCarsOccludeInBake = true;

    // Lightmaps: about 12 texels per metre, one 2048 atlas, non-directional (Quest-friendly).
    const float LightmapTexelsPerUnit = 16f;
    const int LightmapMaxSize = 2048;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly StringBuilder report = new StringBuilder();

    // ------------------------------------------------------------------ entry points

    [MenuItem("Aurelle/Realism/Rendering/Apply Render Setup and Bake Lighting", false, 300)]
    public static void MenuApplyAndBake() { RunInteractive(true); }

    [MenuItem("Aurelle/Realism/Rendering/Apply Render Setup (no bake)", false, 301)]
    public static void MenuApply() { RunInteractive(false); }

    /// <summary>Batch: open SampleScene, apply the render setup (no bake), save, exit in batchmode.</summary>
    public static void Batch() { RunBatch(false); }

    /// <summary>Batch: open SampleScene, apply the render setup, bake lighting synchronously, save, exit in batchmode.</summary>
    public static void BatchBake() { RunBatch(true); }

    static void RunInteractive(bool bake)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning(Tag + "Exit Play Mode first."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        bool ok = Apply(scene, bake);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        FlushReport(ok);
    }

    static void RunBatch(bool bake)
    {
        int code = 0;
        try
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!Apply(scene, bake)) code = 3;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            code = 2;
        }
        FlushReport(code == 0);
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    /// <summary>Applies project settings and the scene setup to an open SampleScene; optionally bakes.</summary>
    public static bool Apply(Scene scene, bool bake)
    {
        report.Length = 0;
        uvCache.Clear();
        Note("apply start, bake=" + bake + ", unity " + Application.unityVersion);
        EnsureFolder(RenderingFolder);
        EnsureFolder(MaterialFolder);
        ApplyPipelineAssets();
        ApplyQualitySettings();
        ApplyScene(scene);
        if (!bake) return true;
        return BakeLighting(scene);
    }

    // ------------------------------------------------------------------ project settings

    static void ApplyPipelineAssets()
    {
        var pc = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcPipelinePath);
        if (pc == null) { Warn("PC pipeline asset missing: " + PcPipelinePath); }
        else
        {
            var so = new SerializedObject(pc);
            SetProp(so, "m_SupportsHDR", true);
            SetProp(so, "m_HDRColorBufferPrecision", 0);           // 32-bit R11G11B10
            SetProp(so, "m_MSAA", 4);
            SetProp(so, "m_ColorGradingMode", 1);                  // HDR grading (ACES in HDR space)
            SetProp(so, "m_ColorGradingLutSize", 32);
            SetProp(so, "m_MainLightShadowsSupported", true);
            SetProp(so, "m_MainLightShadowmapResolution", 4096);
            SetProp(so, "m_ShadowDistance", 90f);
            SetProp(so, "m_ShadowCascadeCount", 4);
            SetProp(so, "m_Cascade4Split", new Vector3(0.05f, 0.16f, 0.42f)); // 4.5 m, 14 m, 38 m, 90 m
            SetProp(so, "m_CascadeBorder", 0.15f);
            SetProp(so, "m_SoftShadowsSupported", true);
            SetProp(so, "m_SoftShadowQuality", (int)SoftShadowQuality.High);
            SetProp(so, "m_AdditionalLightsRenderingMode", 1);     // per pixel
            SetProp(so, "m_AdditionalLightsPerObjectLimit", 8);
            SetProp(so, "m_AdditionalLightShadowsSupported", true);
            SetProp(so, "m_AdditionalLightsShadowmapResolution", 4096);
            SetProp(so, "m_AdditionalLightsShadowResolutionTierLow", 512);
            SetProp(so, "m_AdditionalLightsShadowResolutionTierMedium", 1024);
            SetProp(so, "m_AdditionalLightsShadowResolutionTierHigh", 2048);
            SetProp(so, "m_ReflectionProbeBlending", true);
            SetProp(so, "m_ReflectionProbeBoxProjection", true);
            SetProp(so, "m_MixedLightingSupported", true);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pc);
            Note("PC pipeline: HDR, HDR grading, MSAA 4x, shadows 4096/4 cascades/90 m/soft high, additional-light shadows 4096 atlas, probe blending + box projection");
        }

        var pcRenderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(PcRendererPath);
        if (pcRenderer == null) { Warn("PC renderer missing: " + PcRendererPath); }
        else
        {
            var so = new SerializedObject(pcRenderer);
            SetProp(so, "m_RenderingMode", (int)RenderingMode.ForwardPlus); // no per-object light limit on PC
            so.ApplyModifiedPropertiesWithoutUndo();
            if (pcRenderer.postProcessData == null) Warn("Standalone renderer has no PostProcessData: post-processing cannot run on PC");
            EnsureSsao(pcRenderer);
            EditorUtility.SetDirty(pcRenderer);
            Note("PC renderer: Forward+, SSAO feature");
        }

        // Quest: verify only. Post-processing is disabled at renderer level (postProcessData == null), which is
        // the safest switch in URP 17.3: UniversalRenderer never creates the post passes, so a camera with
        // renderPostProcessing on still renders without post. No SSAO feature on this renderer.
        var questRenderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(QuestRendererPath);
        if (questRenderer == null) Warn("Quest renderer missing: " + QuestRendererPath);
        else
        {
            if (questRenderer.postProcessData != null) Warn("Android Preset has PostProcessData assigned: post-processing would run on Quest");
            if (questRenderer.rendererFeatures.Any(f => f is ScreenSpaceAmbientOcclusion)) Warn("Android Preset has an SSAO feature: remove it for Quest");
            Note("Quest renderer checked: postProcessData=" + (questRenderer.postProcessData ? "SET (!)" : "none") + ", features=" + questRenderer.rendererFeatures.Count);
        }
        var questPipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(QuestPipelinePath);
        if (questPipeline != null) Note("Quest pipeline left unchanged: HDR=" + questPipeline.supportsHDR + ", MSAA=" + questPipeline.msaaSampleCount + ", shadows=" + questPipeline.mainLightShadowmapResolution);
    }

    static void EnsureSsao(UniversalRendererData data)
    {
        var feature = data.rendererFeatures.FirstOrDefault(f => f is ScreenSpaceAmbientOcclusion) as ScreenSpaceAmbientOcclusion;
        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            feature.name = "ScreenSpaceAmbientOcclusion";
            AssetDatabase.AddObjectToAsset(feature, data);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);
            var so = new SerializedObject(data);
            var list = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        feature.SetActive(true);
        var fso = new SerializedObject(feature);
        var s = fso.FindProperty("m_Settings");
        SetRel(s, "AOMethod", 0);              // blue noise
        SetRel(s, "Downsample", false);
        SetRel(s, "AfterOpaque", false);       // modulate indirect light, not a post blend
        SetRel(s, "Source", 1);                // depth normals
        SetRel(s, "NormalSamples", 1);         // medium
        // Soft and short: a stronger, wider SSAO paints dark halos on walls behind thin dark mounts and bars.
        SetRel(s, "Intensity", 0.6f);
        SetRel(s, "DirectLightingStrength", 0.22f);
        SetRel(s, "Radius", 0.12f);
        SetRel(s, "Samples", 1);               // medium (8): bilateral blur hides the difference, ~half the cost of high
        SetRel(s, "BlurQuality", 0);           // bilateral
        SetRel(s, "Falloff", 60f);
        fso.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(feature);
    }

    static void ApplyQualitySettings()
    {
        // The editor's current level is what the Game view and Play Mode use: make it the PC look.
        QualitySettings.SetQualityLevel(PcQualityLevel, true);
        Object qs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset").FirstOrDefault();
        if (qs != null)
        {
            var so = new SerializedObject(qs);
            SetProp(so, "m_CurrentQuality", PcQualityLevel);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(qs);
        }
        Note("quality: editor current level " + QualitySettings.GetQualityLevel() + " (" + QualitySettings.names[QualitySettings.GetQualityLevel()] + "); per-platform defaults untouched");
    }

    // ------------------------------------------------------------------ scene

    struct Hall { public float minX, maxX, minZ, maxZ, floorY, ceilY, backZ; }

    static void ApplyScene(Scene scene)
    {
        Hall hall = MeasureHall(scene);
        Note("hall x " + F(hall.minX) + ".." + F(hall.maxX) + " z " + F(hall.minZ) + ".." + F(hall.maxZ) + " floor " + F(hall.floorY) + " ceiling " + F(hall.ceilY));
        Transform lighting = RootOrCreate(scene, LightingRootName);
        ApplyEnvironment(scene);
        ApplyLights(scene, lighting, hall);
        var dynamicBounds = ApplyStaticFlags(scene);
        ApplyReflectionProbes(scene, lighting, hall);
        ApplyLightProbes(scene, lighting, hall, dynamicBounds);
        ApplyPostVolume(scene, lighting);
        ApplyHeroStudio(scene, lighting);
        ApplyCameras(scene);
        ApplyLightingSettings();
        EditorSceneManager.MarkSceneDirty(scene);
    }

    static Hall MeasureHall(Scene scene)
    {
        var h = new Hall { minX = -17.44f, maxX = 19.44f, minZ = -9.68f, maxZ = 18f, floorY = -0.05f, ceilY = 5.3f, backZ = -17.72f };
        Renderer r;
        string arch = EnvironmentRootName + "/Showroom Architecture/";
        if (TryRenderer(scene, arch + "Ceiling Canopy", out r)) h.ceilY = r.bounds.min.y;
        if (TryRenderer(scene, EnvironmentRootName + "/Showroom Floor", out r)) h.floorY = r.bounds.max.y;
        if (TryRenderer(scene, arch + "Rear Feature Wall", out r)) h.minZ = r.bounds.max.z;
        if (TryRenderer(scene, arch + "Smoked Glass Wall Section 0 0", out r)) h.minX = r.bounds.max.x;
        if (TryRenderer(scene, arch + "Smoked Glass Wall Section 1 0", out r)) h.maxX = r.bounds.min.x;
        if (TryRenderer(scene, arch + "Ceiling Front Beam", out r)) h.maxZ = r.bounds.center.z;
        if (TryRenderer(scene, arch + "Rear Graphite Feature Wall", out r)) h.backZ = r.bounds.max.z;
        return h;
    }

    static void ApplyEnvironment(Scene scene)
    {
        // Ambient and reflections stay sky-driven (the builder's Kloppenheim HDRI); the bake turns them into probes
        // and lightmaps. Two reflection bounces so glossy surfaces seen in a probe show reflections themselves.
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = AmbientIntensity;
        RenderSettings.reflectionIntensity = SkyReflectionIntensity;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.defaultReflectionResolution = 256;
        RenderSettings.reflectionBounces = 2;
        Light sun = FindSun(scene);
        if (sun != null) RenderSettings.sun = sun;
        Note("environment: ambient " + RenderSettings.ambientMode + " x" + F(RenderSettings.ambientIntensity) + ", sky reflection x" + F(RenderSettings.reflectionIntensity) + ", sky " + (RenderSettings.skybox ? RenderSettings.skybox.name : "none"));
    }

    static Light FindSun(Scene scene)
    {
        Transform t = FindPath(scene, "Exterior Environment/Exterior Daylight");
        Light sun = t ? t.GetComponent<Light>() : null;
        return sun != null ? sun : RenderSettings.sun;
    }

    // ------------------------------------------------------------------ lights

    static void ApplyLights(Scene scene, Transform lighting, Hall hall)
    {
        float lightY = hall.ceilY - 0.12f;

        // Sun: mixed, so its bounce into the glass pavilion is baked while its direct light and shadows stay real time.
        Light sun = FindSun(scene);
        if (sun != null)
        {
            sun.lightmapBakeType = LightmapBakeType.Mixed;
            sun.shadows = LightShadows.Soft;
            sun.bounceIntensity = 1f;
            AdditionalData(sun).softShadowQuality = SoftShadowQuality.UsePipelineSettings;
            Note("sun '" + sun.name + "': Mixed, I=" + F(sun.intensity) + ", soft shadows");
        }

        // Fill: a fake bounce light that baked indirect light replaces. Kept in the scene, disabled.
        Light fill = ChildLight(lighting, "Showroom Fill Light");
        if (fill != null)
        {
            fill.lightmapBakeType = LightmapBakeType.Realtime;
            fill.enabled = false;
            Note("fill light disabled (baked indirect light replaces it)");
        }

        Material lens = UseHdrLensMaterial ? EnsureLensMaterial() : null;

        // Downlight grid. The existing recessed row (Showroom Architecture) fixes the columns; rows are added in
        // front of it (entrance side) and over the premium row, where each display car gets its own downlight.
        var recessed = AllLights(scene).Where(l => l.name.StartsWith("Recessed Ceiling Spotlight", StringComparison.Ordinal)).OrderBy(l => l.transform.position.x).ToList();
        var columns = new List<float>();
        foreach (Light l in recessed) if (!columns.Any(c => Mathf.Abs(c - l.transform.position.x) < 1f)) columns.Add(l.transform.position.x);
        if (columns.Count == 0) for (float x = hall.minX + 6.4f; x < hall.maxX - 2f; x += 4.8f) columns.Add(x);
        float baseRow = recessed.Count > 0 ? recessed.Average(l => l.transform.position.z) : 1.3f;
        foreach (Light l in recessed)
        {
            Vector3 p = l.transform.position; p.y = lightY;
            ConfigureSpot(l, p, p + Vector3.down, LightmapBakeType.Baked, DownlightIntensity, DownlightKelvin, DownlightOuter, DownlightInner, DownlightRange, LightShadows.Soft, 1f, DownlightShadowRadius);
        }
        if (lens != null)
            foreach (var r in AllRenderers(scene).Where(x => x.name.StartsWith("Recessed Spotlight Lens", StringComparison.Ordinal)))
            {
                // The interior restyles these into multi-part downlight cans whose LAST slot is the lens disc:
                // only that slot gets the HDR lens, never the housing in slot 0.
                Material[] slots = r.sharedMaterials;
                int lensSlot = slots.Length - 1;
                if (lensSlot >= 0 && slots[lensSlot] != lens) { slots[lensSlot] = lens; r.sharedMaterials = slots; }
                r.shadowCastingMode = ShadowCastingMode.Off;
            }

        var cars = new List<KeyValuePair<string, Bounds>>();
        GameObject premium = Root(scene, PremiumRootName);
        if (premium != null)
            foreach (Transform display in premium.transform)
            {
                Bounds b;
                if (TryRendererBounds(display, out b)) cars.Add(new KeyValuePair<string, Bounds>(display.name, b));
            }
        Transform grid = ChildTransform(lighting, DownlightGridName);
        var wanted = new HashSet<string>();
        int added = 0;
        foreach (var car in cars)
        {
            string name = "Downlight - " + car.Key;
            Vector3 p = new Vector3(car.Value.center.x, lightY, car.Value.center.z);
            AddDownlight(grid, name, p, CarDownlightIntensity, CarDownlightOuter, CarDownlightInner, lens, hall);
            wanted.Add(name); added++;
        }
        // Reception desk and the presenter's idle post get their own downlight (Remy is lit through the probes).
        Bounds desk;
        Transform reception = FindPath(scene, InteriorRootName + "/Reception Zone");
        if (reception != null && TryRendererBounds(reception, out desk))
        {
            AddDownlight(grid, "Downlight - Reception", new Vector3(desk.center.x, lightY, desk.max.z - 0.2f), DownlightIntensity, CarDownlightOuter, CarDownlightInner, lens, hall);
            wanted.Add("Downlight - Reception"); added++;
        }
        Transform remy = FindPath(scene, EnvironmentRootName + "/Showroom Presenter/Remy Presenter");
        if (remy != null)
        {
            Vector3 post = remy.position + Vector3.ProjectOnPlane(remy.forward, Vector3.up).normalized * 0.6f;
            AddDownlight(grid, "Downlight - Presenter Post", new Vector3(post.x, lightY, post.z), DownlightIntensity, CarDownlightOuter, CarDownlightInner, lens, hall);
            wanted.Add("Downlight - Presenter Post"); added++;
        }
        var rows = new List<float>();
        if (cars.Count > 0) rows.Add(cars.Average(c => c.Value.center.z));
        for (float z = baseRow + RowSpacing; z < hall.maxZ - 2.5f; z += RowSpacing) rows.Add(z);
        // Ceiling fixtures added by the interior (pendants and the like) take precedence over grid downlights.
        var fixtures = new List<Bounds>();
        GameObject interiorRoot = Root(scene, InteriorRootName);
        if (interiorRoot != null)
            foreach (Renderer r in interiorRoot.GetComponentsInChildren<Renderer>(true))
            {
                Bounds b = r.bounds;
                if (b.max.y > hall.ceilY - 1.5f && b.min.y > hall.floorY + 1.6f && b.size.y < 3f) fixtures.Add(b);
            }
        int skippedForFixtures = 0, skippedForCanopy = 0;
        Bounds heroForCanopy;
        bool haveCanopy = TryHeroBounds(scene, out heroForCanopy);
        Rect canopy = haveCanopy ? CanopyRect(heroForCanopy) : new Rect();
        foreach (float z in rows)
            foreach (float x in columns)
            {
                if (cars.Any(c => Mathf.Abs(c.Value.center.x - x) < 2.2f && Mathf.Abs(c.Value.center.z - z) < 3f)) continue;
                // The studio canopy is the hero car's top light; a downlight there would sit inside its housing.
                if (haveCanopy && x > canopy.xMin - 0.4f && x < canopy.xMax + 0.4f && z > canopy.yMin - 0.4f && z < canopy.yMax + 0.4f) { skippedForCanopy++; continue; }
                if (fixtures.Any(b => x > b.min.x - 0.6f && x < b.max.x + 0.6f && z > b.min.z - 0.6f && z < b.max.z + 0.6f)) { skippedForFixtures++; continue; }
                string name = "Downlight " + F(x) + " " + F(z);
                AddDownlight(grid, name, new Vector3(x, lightY, z), DownlightIntensity, DownlightOuter, DownlightInner, lens, hall);
                wanted.Add(name); added++;
            }
        foreach (Transform c in grid.Cast<Transform>().ToList()) if (!wanted.Contains(c.name)) Object.DestroyImmediate(c.gameObject);
        Note("downlights: " + recessed.Count + " recessed + " + added + " added (" + rows.Count + " rows x " + columns.Count + " columns, one over each display car, reception, presenter post; " + skippedForFixtures + " skipped under interior ceiling fixtures, " + skippedForCanopy + " under the hero canopy), all baked, " + DownlightKelvin + "K, I=" + F(DownlightIntensity) + "/" + F(CarDownlightIntensity));
        if (haveCanopy)
            foreach (Light l in recessed)
                if (canopy.Contains(new Vector2(l.transform.position.x, l.transform.position.z))) Warn("recessed downlight '" + l.name + "' lies under the hero canopy");

        // Wall washers: just below the ceiling, set back from the feature wall, aimed at its middle. One is centred on
        // each wall display; the rest fill the gaps. Existing 'Rear Wall Wash Spotlight N' lights are reused.
        Renderer wall;
        float wallMinX = -13.5f, wallMaxX = 15.5f;
        if (TryRenderer(scene, EnvironmentRootName + "/Showroom Architecture/Rear Feature Wall", out wall)) { wallMinX = wall.bounds.min.x; wallMaxX = wall.bounds.max.x; }
        var washX = new List<float>();
        Transform displays = FindPath(scene, InteriorRootName + "/Rear Wall Advertising Displays");
        if (displays != null)
            foreach (Renderer r in displays.GetComponentsInChildren<Renderer>(true))
                if (r.name.Contains("bezel") && !washX.Any(x => Mathf.Abs(x - r.bounds.center.x) < 1f)) washX.Add(r.bounds.center.x);
        for (float x = wallMinX + 1.5f; x <= wallMaxX - 1.5f + 0.01f; x += 0.25f)
            if (washX.All(w => Mathf.Abs(w - x) >= 3.6f)) washX.Add(x);
        washX.Sort();
        int wi = 0;
        foreach (float x in washX)
        {
            wi++;
            string name = "Rear Wall Wash Spotlight " + wi;
            Light l = ChildLight(lighting, name);
            if (l == null) { l = new GameObject(name).AddComponent<Light>(); l.transform.SetParent(lighting, false); }
            Vector3 p = new Vector3(x, lightY, hall.minZ + WasherSetback);
            Vector3 aim = new Vector3(x, hall.floorY + 2.1f, hall.minZ);
            ConfigureSpot(l, p, aim, LightmapBakeType.Baked, WasherIntensity, WasherKelvin, WasherOuter, WasherInner, WasherRange, LightShadows.Soft, 1f, 0.1f);
        }
        for (int i = wi + 1; i <= 64; i++) { Light extra = ChildLight(lighting, "Rear Wall Wash Spotlight " + i); if (extra != null) Object.DestroyImmediate(extra.gameObject); }
        Note("wall washers: " + wi + " baked, " + F(WasherSetback) + " m from the feature wall, aimed at its middle, I=" + F(WasherIntensity));

        // Hero car key and rim: mixed (real-time direct, baked bounce), real-time soft shadows on PC.
        Bounds hero;
        bool haveHero = TryHeroBounds(scene, out hero);
        Vector3 heroCenter = haveHero ? hero.center : new Vector3(1.07f, 0.55f, 2.24f);
        Light key = ChildLight(lighting, "Showroom Key Spot");
        if (key != null)
        {
            Vector3 p = new Vector3(heroCenter.x + 4.2f, lightY, heroCenter.z + 4.3f);
            ConfigureSpot(key, p, heroCenter + Vector3.up * 0.1f, LightmapBakeType.Mixed, KeyIntensity, KeyKelvin, KeyOuter, KeyInner, KeyRange, LightShadows.Soft, KeyShadowStrength, 0.1f);
            SetShadowTier(key, 1);   // medium tier: a softer real-time penumbra under the car
        }
        Light rim = ChildLight(lighting, "Showroom Rim Spot");
        if (rim != null)
        {
            Vector3 p = new Vector3(heroCenter.x - 0.2f, lightY, heroCenter.z - 5.2f);
            // No real-time shadow: a rim light's shadow is hardly visible and costs a second pass over the hero car.
            ConfigureSpot(rim, p, heroCenter + new Vector3(0f, 0.35f, 0.4f), LightmapBakeType.Mixed, RimIntensity, RimKelvin, RimOuter, RimInner, RimRange, LightShadows.None, 0.8f, 0.1f);
        }
        Note("hero key/rim: mixed, I=" + F(KeyIntensity) + "/" + F(RimIntensity) + ", key casts real-time soft shadows (PC only, medium tier, strength " + F(KeyShadowStrength) + ")");
        if (haveHero) BuildHeroCanopy(lighting, hall, hero);

        // Street lamps stay real time and out of the bake.
        int lamps = 0;
        foreach (Light l in AllLights(scene).Where(l => l.name == "Warm Road LED"))
        {
            l.lightmapBakeType = LightmapBakeType.Realtime;
            l.intensity = StreetLampIntensity;
            lamps++;
        }
        Note("street lamps: " + lamps + " real time, I=" + F(StreetLampIntensity) + " (daylight scene: subtle pools instead of 30)");

        BuildBakeOccluders(scene, lighting, hall);
    }

    static void AddDownlight(Transform grid, string name, Vector3 position, float intensity, float outer, float inner, Material lens, Hall hall)
    {
        Transform t = grid.Find(name);
        GameObject go = t != null ? t.gameObject : new GameObject(name);
        if (t == null) go.transform.SetParent(grid, false);
        Light l = go.GetComponent<Light>();
        if (l == null) l = go.AddComponent<Light>();
        ConfigureSpot(l, position, position + Vector3.down, LightmapBakeType.Baked, intensity, DownlightKelvin, outer, inner, DownlightRange, LightShadows.Soft, 1f, DownlightShadowRadius);
        // Visible fixture: a flush lens disc in the ceiling, captured by reflection probes (not a GI emitter).
        Transform lt = go.transform.Find("Lens");
        GameObject lensGo;
        if (lt == null)
        {
            lensGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lensGo.name = "Lens";
            Object.DestroyImmediate(lensGo.GetComponent<Collider>());
            lensGo.transform.SetParent(go.transform, false);
        }
        else lensGo = lt.gameObject;
        lensGo.transform.position = new Vector3(position.x, hall.ceilY - 0.015f, position.z);
        lensGo.transform.rotation = Quaternion.identity;
        lensGo.transform.localScale = Vector3.one;
        lensGo.transform.localScale = new Vector3(0.3f / lensGo.transform.lossyScale.x, 0.02f / lensGo.transform.lossyScale.y, 0.3f / lensGo.transform.lossyScale.z);
        var mr = lensGo.GetComponent<MeshRenderer>();
        if (lens != null) mr.sharedMaterial = lens;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        GameObjectUtility.SetStaticEditorFlags(lensGo, StaticEditorFlags.ReflectionProbeStatic);
        GameObjectUtility.SetStaticEditorFlags(go, 0);
    }

    // Bake-only occluders for the premium display cars: two boxes per car (body, cabin) in the display's own frame.
    // They are enabled only while BakeLighting runs, so the floor gets soft contact shadows and darker probes under
    // and around each car while the cars themselves stay fully dynamic (no lightmap UVs or GI flags on the GLBs).
    static void BuildBakeOccluders(Scene scene, Transform lighting, Hall hall)
    {
        Transform root = ChildTransform(lighting, BakeOccluderRootName);
        var wanted = new HashSet<string>();
        int n = 0;
        GameObject premium = Root(scene, PremiumRootName);
        if (PremiumCarsOccludeInBake && premium != null)
        {
            Material mat = EnsureOccluderMaterial();
            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            foreach (Transform display in premium.transform)
            {
                Bounds local;
                if (!TryLocalBounds(display, out local)) continue;
                // Body starts 16 cm above the showroom floor (world units, whatever the display's scale or offset).
                float sy = Mathf.Max(Mathf.Abs(display.lossyScale.y), 1e-4f);
                float bottom = display.InverseTransformPoint(new Vector3(display.position.x, hall.floorY + 0.16f, display.position.z)).y;
                float height = local.max.y - bottom;
                if (height * sy <= 0.2f) continue;
                bool longZ = local.size.z >= local.size.x;
                // Smaller than the bounds: bumpers and sills curve in, so the dark core stays under the body.
                Vector3 bodySize = new Vector3(local.size.x * (longZ ? 0.8f : 0.86f), height * 0.5f, local.size.z * (longZ ? 0.86f : 0.8f));
                Vector3 cabinSize = new Vector3(local.size.x * (longZ ? 0.66f : 0.46f), height * 0.4f, local.size.z * (longZ ? 0.46f : 0.66f));
                string body = "Occluder - " + display.name + " body", cabin = "Occluder - " + display.name + " cabin";
                AddOccluderBox(root, body, display, new Vector3(local.center.x, bottom + bodySize.y * 0.5f, local.center.z), bodySize, cube, mat);
                AddOccluderBox(root, cabin, display, new Vector3(local.center.x, bottom + bodySize.y + cabinSize.y * 0.5f, local.center.z), cabinSize, cube, mat);
                wanted.Add(body); wanted.Add(cabin);
                n++;
            }
        }
        foreach (Transform c in root.Cast<Transform>().ToList()) if (!wanted.Contains(c.name)) Object.DestroyImmediate(c.gameObject);
        root.gameObject.SetActive(false);
        Note("bake occluders: " + n + " premium cars (inactive outside the bake)");
    }

    static void AddOccluderBox(Transform root, string name, Transform frame, Vector3 localCenter, Vector3 localSize, Mesh cube, Material mat)
    {
        Transform t = root.Find(name);
        GameObject go = t != null ? t.gameObject : new GameObject(name);
        if (t == null) go.transform.SetParent(root, false);
        go.transform.position = frame.TransformPoint(localCenter);
        go.transform.rotation = frame.rotation;
        Vector3 s = frame.lossyScale;
        go.transform.localScale = Vector3.Scale(localSize, s);
        var mf = go.GetComponent<MeshFilter>();
        if (mf == null) mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = cube;
        var mr = go.GetComponent<MeshRenderer>();
        if (mr == null) mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.On;
        mr.receiveGI = ReceiveGI.LightProbes;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI);
    }

    static bool TryLocalBounds(Transform frame, out Bounds local)
    {
        local = new Bounds();
        bool any = false;
        foreach (MeshFilter mf in frame.GetComponentsInChildren<MeshFilter>(false))
        {
            var r = mf.GetComponent<MeshRenderer>();
            if (mf.sharedMesh == null || r == null || !r.enabled) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = frame.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (any) local.Encapsulate(p); else { local = new Bounds(p, Vector3.zero); any = true; }
            }
        }
        return any;
    }

    static Material EnsureOccluderMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(OccluderMaterialPath);
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (mat == null)
        {
            mat = new Material(lit) { name = "AurelleBakeOccluder" };
            AssetDatabase.CreateAsset(mat, OccluderMaterialPath);
        }
        mat.SetColor("_BaseColor", new Color(0.06f, 0.06f, 0.065f));
        mat.SetFloat("_Smoothness", 0.5f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void ConfigureSpot(Light l, Vector3 position, Vector3 aim, LightmapBakeType mode, float intensity, float kelvin,
        float outer, float inner, float range, LightShadows shadows, float shadowStrength, float bakedShadowRadius)
    {
        l.type = LightType.Spot;
        l.transform.position = position;
        Vector3 dir = (aim - position).normalized;
        l.transform.rotation = Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.98f ? Vector3.forward : Vector3.up);
        l.lightmapBakeType = mode;
        l.intensity = intensity;
        l.color = Color.white;
        l.useColorTemperature = true;
        l.colorTemperature = kelvin;
        l.spotAngle = outer;
        l.innerSpotAngle = inner;
        l.range = range;
        l.shadows = shadows;
        l.shadowStrength = shadowStrength;
        l.shadowRadius = bakedShadowRadius;
        l.bounceIntensity = 1f;
        l.renderMode = LightRenderMode.Auto;
        l.cullingMask = ~0;
        l.enabled = true;
        var ad = AdditionalData(l);
        ad.softShadowQuality = SoftShadowQuality.UsePipelineSettings;
        EditorUtility.SetDirty(l);
    }

    static UniversalAdditionalLightData AdditionalData(Light l)
    {
        var ad = l.GetComponent<UniversalAdditionalLightData>();
        if (ad == null) ad = l.gameObject.AddComponent<UniversalAdditionalLightData>();
        return ad;
    }

    static void SetShadowTier(Light l, int tier)
    {
        var so = new SerializedObject(AdditionalData(l));
        SetProp(so, "m_AdditionalLightsShadowResolutionTier", tier);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static Material EnsureLensMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(LensMaterialPath);
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (mat == null)
        {
            mat = new Material(lit) { name = "AurelleDownlightLens" };
            AssetDatabase.CreateAsset(mat, LensMaterialPath);
        }
        if (mat.shader != lit) mat.shader = lit;
        mat.SetColor("_BaseColor", new Color(0.95f, 0.93f, 0.9f));
        mat.SetFloat("_Smoothness", 0.7f);
        mat.SetFloat("_Metallic", 0f);
        Color emission = Mathf.CorrelatedColorTemperatureToRGB(DownlightKelvin) * LensEmission;
        mat.SetColor("_EmissionColor", emission);
        mat.EnableKeyword("_EMISSION");
        // The spot lights do the lighting; the lens is only a visible source (camera, probes, bloom), not a baked
        // emitter. URP 17.3 keeps _EMISSION only for Realtime/Baked emissive flags (and a bake re-validates the
        // material), so use RealtimeEmissive: with realtime GI off it never feeds a bake.
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ------------------------------------------------------------------ static / GI flags

    const StaticEditorFlags GiFlags = StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic;

    class RuleCounts { public int lightmapped, probeLit, excluded, cleared; public Dictionary<string, int> reasons = new Dictionary<string, int>(); }

    // Returns the bounds of dynamic and probe-lit cars (used to keep light probes out of them).
    static List<Bounds> ApplyStaticFlags(Scene scene)
    {
        var counts = new RuleCounts();
        var carBounds = new List<Bounds>();

        // 1) Dynamic content: never static for GI or probes.
        GameObject heroRoot = Root(scene, HeroCarName);
        if (heroRoot != null)
        {
            foreach (Transform t in heroRoot.GetComponentsInChildren<Transform>(true)) counts.cleared += ClearGiFlags(t.gameObject);
            Bounds hb; if (TryHeroBounds(scene, out hb)) carBounds.Add(hb);
        }
        Transform presenter = FindPath(scene, EnvironmentRootName + "/Showroom Presenter");
        if (presenter != null) foreach (Transform t in presenter.GetComponentsInChildren<Transform>(true)) counts.cleared += ClearGiFlags(t.gameObject);

        // 2) Static showroom: architecture, floor, podium and interior furniture, by rule.
        var staticRoots = new List<Transform>();
        Transform env = FindPath(scene, EnvironmentRootName);
        if (env != null) foreach (Transform c in env) if (c.name != "Showroom Presenter") staticRoots.Add(c);
        GameObject interior = Root(scene, InteriorRootName);
        if (interior != null) staticRoots.Add(interior.transform);
        foreach (Transform root in staticRoots)
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) ClassifyStatic(r, counts);

        // 3) Premium display cars stay fully dynamic (lit by probes); their baked contact shadows come from the
        //    bake-only occluder boxes (see BuildBakeOccluders).
        GameObject premium = Root(scene, PremiumRootName);
        if (premium != null)
            foreach (Transform display in premium.transform)
            {
                Bounds b; if (TryRendererBounds(display, out b)) carBounds.Add(b);
                foreach (Renderer r in display.GetComponentsInChildren<Renderer>(true)) counts.cleared += ClearGiFlags(r.gameObject);
            }
        Note("GI flags: lightmapped " + counts.lightmapped + ", contribute+probe-lit " + counts.probeLit + ", excluded " + counts.excluded + " (" + string.Join(", ", counts.reasons.Select(kv => kv.Key + " " + kv.Value)) + "), GI flags removed from dynamic objects " + counts.cleared);
        return carBounds;
    }

    static void ClassifyStatic(Renderer r, RuleCounts counts)
    {
        string reason = ExcludeReason(r);
        if (reason != null)
        {
            ClearGiFlags(r.gameObject);
            counts.excluded++;
            counts.reasons[reason] = counts.reasons.TryGetValue(reason, out int n) ? n + 1 : 1;
            // Transparent surfaces still belong in reflection captures (glass), text does not.
            if (reason == "transparent" || reason == "light fixture") SetGiFlags(r.gameObject, StaticEditorFlags.ReflectionProbeStatic, StaticEditorFlags.ContributeGI);
            return;
        }
        var mr = (MeshRenderer)r;
        SetGiFlags(r.gameObject, GiFlags, 0);
        mr.lightProbeUsage = LightProbeUsage.BlendProbes;
        if (WantsLightmap(mr))
        {
            mr.receiveGI = ReceiveGI.Lightmaps;
            counts.lightmapped++;
        }
        else
        {
            mr.receiveGI = ReceiveGI.LightProbes;
            counts.probeLit++;
        }
    }

    static string ExcludeReason(Renderer r)
    {
        if (!(r is MeshRenderer)) return "not a mesh renderer";
        if (r.GetComponent<TextMesh>() != null) return "text";
        var mf = r.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return "no mesh";
        if (r.sharedMaterials.Length == 0 || r.sharedMaterials.Any(m => m == null)) return "missing material";
        if (r.sharedMaterials.Any(m => m.shader != null && (m.shader.name.StartsWith("GUI/", StringComparison.Ordinal) || m.shader.name.StartsWith("UI/", StringComparison.Ordinal) || m.shader.name.Contains("Text")))) return "text";
        // Downlight lenses are visible light sources only: as GI contributors their HDR emission would be baked
        // (Unity bakes realtime-emissive materials when realtime GI is off) and paint halos on the ceiling.
        if (r.sharedMaterials.Any(m => AssetDatabase.GetAssetPath(m) == LensMaterialPath)) return "light fixture";
        string dyn = DynamicReason(r.transform);
        if (dyn != null) return dyn;
        if (IsTransparent(r)) return "transparent";
        LODGroup lod = r.GetComponentInParent<LODGroup>();
        if (lod != null)
        {
            var lods = lod.GetLODs();
            for (int i = 1; i < lods.Length; i++) if (lods[i].renderers.Contains(r)) return "LOD>0";
        }
        return null;
    }

    // Anything that can move or is driven at run time stays dynamic.
    static string DynamicReason(Transform t)
    {
        // Walk up to, but not including, the scene root: a controller on a zone root must not make every
        // piece of furniture dynamic.
        for (Transform p = t; p != null && p.parent != null; p = p.parent)
        {
            if (p.GetComponent<Animator>() != null || p.GetComponent<Animation>() != null) return "animated";
            if (p.GetComponent<Rigidbody>() != null) return "rigidbody";
            if (p.GetComponent<Cloth>() != null || p.GetComponent<ParticleSystem>() != null) return "simulated";
            foreach (var mb in p.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                string asm = mb.GetType().Assembly.GetName().Name;
                string n = mb.GetType().Name;
                if (asm == "Assembly-CSharp" || n.Contains("Interactable") || n.Contains("Grab")) return "scripted";
            }
        }
        return null;
    }

    static bool IsTransparent(Renderer r)
    {
        foreach (Material m in r.sharedMaterials)
        {
            if (m == null) continue;
            if (m.renderQueue >= 2600) return true;
            if (m.GetTag("RenderType", false) == "Transparent") return true;
            if (m.HasProperty("_Surface") && m.GetFloat("_Surface") > 0.5f) return true;
        }
        return false;
    }

    static readonly Dictionary<Mesh, bool> uvCache = new Dictionary<Mesh, bool>();

    // Lightmap only surfaces that have usable lightmap UVs and enough area to deserve texels.
    static bool WantsLightmap(MeshRenderer r)
    {
        Mesh mesh = r.GetComponent<MeshFilter>().sharedMesh;
        Vector3 s = r.bounds.size;
        float largest = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
        float second = s.x + s.y + s.z - largest - Mathf.Min(s.x, Mathf.Min(s.y, s.z));
        if (largest < 0.25f || largest * second < 0.04f) return false;   // tiny or thread-like: probes are better
        if (!uvCache.TryGetValue(mesh, out bool ok))
        {
            ok = mesh.HasVertexAttribute(VertexAttribute.TexCoord1) ? UvLayoutUsable(mesh, 1, true) : UvLayoutUsable(mesh, 0, false);
            uvCache[mesh] = ok;
        }
        return ok;
    }

    // Unity bakes with UV2 when present, else UV0. Accept a set only when it lies in [0,1] and barely overlaps.
    static bool UvLayoutUsable(Mesh mesh, int channel, bool trustIfUnreadable)
    {
        var uvs = new List<Vector2>();
        int[] tris;
        try
        {
            mesh.GetUVs(channel, uvs);
            tris = mesh.triangles;
        }
        catch (Exception)
        {
            return trustIfUnreadable;
        }
        if (uvs.Count == 0 || tris.Length == 0) return trustIfUnreadable && uvs.Count > 0;
        foreach (Vector2 uv in uvs) if (uv.x < -0.002f || uv.x > 1.002f || uv.y < -0.002f || uv.y > 1.002f) return false;
        const int N = 128;
        var cover = new byte[N * N];
        int covered = 0, overlapped = 0;
        for (int i = 0; i + 2 < tris.Length; i += 3)
        {
            Vector2 a = uvs[tris[i]] * N, b = uvs[tris[i + 1]] * N, c = uvs[tris[i + 2]] * N;
            float area = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
            if (Mathf.Abs(area) < 1e-7f) continue;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            int x1 = Mathf.Min(N - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            int y1 = Mathf.Min(N - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float w0 = Edge(b, c, p), w1 = Edge(c, a, p), w2 = Edge(a, b, p);
                    bool inside = area > 0 ? (w0 >= 0 && w1 >= 0 && w2 >= 0) : (w0 <= 0 && w1 <= 0 && w2 <= 0);
                    if (!inside) continue;
                    int idx = y * N + x;
                    if (cover[idx] == 0) covered++;
                    else if (cover[idx] == 1) overlapped++;
                    if (cover[idx] < 2) cover[idx]++;
                }
        }
        if (covered == 0) return false;
        return overlapped <= covered * 0.03f;
    }

    static float Edge(Vector2 a, Vector2 b, Vector2 p) { return (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x); }

    static int ClearGiFlags(GameObject go)
    {
        var f = GameObjectUtility.GetStaticEditorFlags(go);
        if ((f & GiFlags) == 0) return 0;
        GameObjectUtility.SetStaticEditorFlags(go, f & ~GiFlags);
        return 1;
    }

    static void SetGiFlags(GameObject go, StaticEditorFlags add, StaticEditorFlags remove)
    {
        var f = GameObjectUtility.GetStaticEditorFlags(go);
        var n = (f | add) & ~remove;
        if (n != f) GameObjectUtility.SetStaticEditorFlags(go, n);
    }

    // ------------------------------------------------------------------ reflection probes

    static void ApplyReflectionProbes(Scene scene, Transform lighting, Hall hall)
    {
        // Main hall: the old real-time OnAwake probe becomes a baked, box-projected probe that matches the room
        // (floor, ceiling, glazing, feature wall), so floor and paint reflections line up with the room.
        Bounds room = new Bounds();
        room.SetMinMax(new Vector3(hall.minX, hall.floorY, hall.minZ), new Vector3(hall.maxX, hall.ceilY, hall.maxZ));
        ReflectionProbe main = ChildComponent<ReflectionProbe>(lighting, MainProbeName);
        // Captured in the open aisle in front of the hero podium, at eye level.
        Vector3 mainCapture = new Vector3((hall.minX + hall.maxX) * 0.5f, hall.floorY + 1.45f, hall.minZ + (hall.maxZ - hall.minZ) * 0.66f);
        ConfigureProbe(main, mainCapture, room, true, 1, 256, 1f);

        // Hero car: captured from the car's centre (the car itself is dynamic and not captured), direction-only
        // lookup, which is the accurate choice for a convex object in the middle of a large room. The influence box
        // starts above the floor so floor pixels keep the box-projected main probe (Forward+ evaluates per pixel).
        // 512 px keeps the studio canopy's strips crisp in the clear coat; the box blends into the hall probe over
        // 0.6 m so the car's lower body, wheels and exploded parts do not show a seam.
        Bounds hero;
        int count = 1;
        if (TryHeroBounds(scene, out hero))
        {
            ReflectionProbe hp = ChildComponent<ReflectionProbe>(lighting, HeroProbeName);
            Bounds box = new Bounds();
            box.SetMinMax(new Vector3(hero.min.x - 0.7f, hall.floorY + 0.14f, hero.min.z - 0.7f), new Vector3(hero.max.x + 0.7f, hero.max.y + 0.8f, hero.max.z + 0.7f));
            ConfigureProbe(hp, new Vector3(hero.center.x, hall.floorY + 0.8f, hero.center.z), box, false, 3, HeroProbeResolution, HeroProbeBlend);
            count++;
        }

        // One probe per premium display, same idea; removed probes for displays that no longer exist.
        GameObject premium = Root(scene, PremiumRootName);
        var wanted = new HashSet<string>();
        if (premium != null)
        {
            foreach (Transform display in premium.transform)
            {
                Bounds b;
                if (!TryRendererBounds(display, out b)) continue;
                string name = PremiumProbePrefix + display.name;
                wanted.Add(name);
                ReflectionProbe p = ChildComponent<ReflectionProbe>(lighting, name);
                Bounds box = new Bounds();
                box.SetMinMax(new Vector3(b.min.x - 0.5f, hall.floorY + 0.14f, b.min.z - 0.5f), new Vector3(b.max.x + 0.5f, b.max.y + 0.7f, b.max.z + 0.5f));
                ConfigureProbe(p, new Vector3(b.center.x, hall.floorY + 0.75f, b.center.z), box, false, 2, 256, 0.3f);
                count++;
            }
        }
        foreach (Transform c in lighting.Cast<Transform>().ToList())
            if (c.name.StartsWith(PremiumProbePrefix, StringComparison.Ordinal) && !wanted.Contains(c.name)) Object.DestroyImmediate(c.gameObject);
        Note("reflection probes: " + count + " baked (main box-projected 256, hero " + HeroProbeResolution + " (Android " + HeroProbeAndroidMax + "), per premium car 256)");
    }

    static void ConfigureProbe(ReflectionProbe p, Vector3 capture, Bounds box, bool boxProjection, int importance, int resolution, float blend)
    {
        p.transform.position = capture;
        p.transform.rotation = Quaternion.identity;
        p.transform.localScale = Vector3.one;
        p.mode = ReflectionProbeMode.Baked;
        p.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        p.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        p.center = box.center - capture;
        p.size = box.size;
        p.boxProjection = boxProjection;
        p.importance = importance;
        p.resolution = resolution;
        p.hdr = true;
        p.intensity = 1f;
        p.blendDistance = blend;
        p.clearFlags = ReflectionProbeClearFlags.Skybox;
        p.cullingMask = ~0;
        p.shadowDistance = 80f;
        p.nearClipPlane = 0.05f;
        p.farClipPlane = 1000f;
        p.enabled = true;
        EditorUtility.SetDirty(p);
    }

    // ------------------------------------------------------------------ light probes

    static void ApplyLightProbes(Scene scene, Transform lighting, Hall hall, List<Bounds> carBounds)
    {
        // Solid static volumes (lightmapped geometry) that probes must not sit inside.
        var solids = new List<Bounds>();
        foreach (var r in AllRenderers(scene))
        {
            if (!(r is MeshRenderer) || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if ((GameObjectUtility.GetStaticEditorFlags(r.gameObject) & StaticEditorFlags.ContributeGI) == 0) continue;
            Bounds b = r.bounds;
            if (Mathf.Min(b.size.x, Mathf.Min(b.size.y, b.size.z)) < 0.12f) continue;
            if (b.size.x > 30f && b.size.z > 30f) continue; // floor / ceiling slabs: probes are placed between them
            b.Expand(-0.02f);
            solids.Add(b);
        }
        foreach (var cb in carBounds) { Bounds b = cb; b.Expand(0.05f); solids.Add(b); }

        // Showroom: 2.4 x 2.25 m grid at four heights inside the hall, a denser layer around the cars and a sparser
        // grid behind the feature wall. Probes inside solid static geometry or inside a car are dropped.
        var inside = new List<Vector3>();
        float[] heights = { 0.3f, 1.2f, 2.5f, hall.ceilY - hall.floorY - 0.6f };
        for (float x = hall.minX + 0.5f; x <= hall.maxX - 0.4f; x += 2.4f)
            for (float z = hall.minZ + 0.45f; z <= hall.maxZ - 0.3f; z += 2.25f)
                foreach (float h in heights) inside.Add(new Vector3(x, hall.floorY + h, z));
        // Denser layer around the hero podium and the premium row for smoother car lighting.
        foreach (var cb in carBounds)
            for (float x = cb.min.x - 1.2f; x <= cb.max.x + 1.25f; x += 1.2f)
                for (float z = cb.min.z - 1.2f; z <= cb.max.z + 1.25f; z += 1.2f)
                    foreach (float h in new[] { 0.25f, 0.9f, 1.7f }) inside.Add(new Vector3(x, hall.floorY + h, z));
        for (float x = hall.minX + 1f; x <= hall.maxX - 1f; x += 4f)
            for (float z = hall.backZ + 1.2f; z <= hall.minZ - 0.8f; z += 3f)
                foreach (float h in new[] { 0.4f, 2.4f, hall.ceilY - hall.floorY - 0.6f }) inside.Add(new Vector3(x, hall.floorY + h, z));
        var showroom = Filter(inside, solids, 0.35f);

        // Exterior: the district is not baked, but every non-lightmapped renderer samples the probe hull, so a coarse
        // sky-lit grid (20 m, two heights, plus a ring 4 m outside the pavilion) keeps exterior objects from
        // extrapolating the showroom's interior probes.
        var outside = new List<Vector3>();
        Bounds district = new Bounds(new Vector3(0f, 0f, 40f), new Vector3(280f, 10f, 280f));
        Transform exterior = FindPath(scene, "Exterior Environment");
        if (exterior != null)
        {
            bool any = false;
            foreach (Transform part in exterior)
            {
                if (part.name == "Land and Arrival") continue; // the ground slab reaches far beyond the streets
                Bounds b;
                if (!TryRendererBounds(part, out b)) continue;
                if (any) district.Encapsulate(b); else { district = b; any = true; }
            }
        }
        float x0 = Mathf.Floor((district.min.x - 10f) / 20f) * 20f, x1 = district.max.x + 10f;
        float z0 = Mathf.Floor((district.min.z - 10f) / 20f) * 20f, z1 = district.max.z + 10f;
        for (float x = x0; x <= x1 + 0.1f; x += 20f)
            for (float z = z0; z <= z1 + 0.1f; z += 20f)
            {
                if (x > hall.minX - 6f && x < hall.maxX + 6f && z > hall.backZ - 6f && z < hall.maxZ + 6f) continue;
                outside.Add(new Vector3(x, 1.0f, z));
                outside.Add(new Vector3(x, 9.0f, z));
            }
        for (float x = hall.minX - 4f; x <= hall.maxX + 4.01f; x += 6f)
            foreach (float z in new[] { hall.backZ - 4f, hall.maxZ + 4f })
                foreach (float h in new[] { 0.5f, 2.5f, 6.5f }) outside.Add(new Vector3(x, hall.floorY + h, z));
        for (float z = hall.backZ - 4f; z <= hall.maxZ + 4.01f; z += 6f)
            foreach (float x in new[] { hall.minX - 4f, hall.maxX + 4f })
                foreach (float h in new[] { 0.5f, 2.5f, 6.5f }) outside.Add(new Vector3(x, hall.floorY + h, z));
        outside = Filter(outside, new List<Bounds>(), 1.0f);

        LightProbeGroup g1 = ChildComponent<LightProbeGroup>(lighting, ShowroomProbeGroupName);
        g1.probePositions = showroom.Select(p => g1.transform.InverseTransformPoint(p)).ToArray();
        g1.dering = true;
        EditorUtility.SetDirty(g1);
        LightProbeGroup g2 = ChildComponent<LightProbeGroup>(lighting, ExteriorProbeGroupName);
        g2.probePositions = outside.Select(p => g2.transform.InverseTransformPoint(p)).ToArray();
        g2.dering = true;
        EditorUtility.SetDirty(g2);
        Note("light probes: showroom " + showroom.Count + ", exterior grid " + outside.Count);
    }

    static List<Vector3> Filter(List<Vector3> points, List<Bounds> solids, float minSpacing)
    {
        var result = new List<Vector3>();
        var cells = new HashSet<Vector3Int>();
        foreach (Vector3 p in points)
        {
            if (solids.Any(b => b.Contains(p))) continue;
            var key = new Vector3Int(Mathf.RoundToInt(p.x / minSpacing), Mathf.RoundToInt(p.y / minSpacing), Mathf.RoundToInt(p.z / minSpacing));
            if (!cells.Add(key)) continue;
            result.Add(p);
        }
        return result;
    }

    // ------------------------------------------------------------------ post-processing

    static void ApplyPostVolume(Scene scene, Transform lighting)
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "AurellePostProfile";
            AssetDatabase.CreateAsset(profile, PostProfilePath);
        }
        var tone = Component<Tonemapping>(profile);
        tone.mode.Override(TonemappingMode.ACES);

        var color = Component<ColorAdjustments>(profile);
        // Premium and realistic rather than graded: a little less exposure for the canopy's added light, a touch more
        // contrast, almost no extra saturation; a slightly cooler balance against the warm wall washers; bloom only
        // on real light sources (threshold above the paint's brightest canopy reflections).
        color.postExposure.Override(0.55f);
        color.contrast.Override(10f);
        color.saturation.Override(2f);
        color.colorFilter.Override(Color.white);
        color.hueShift.Override(0f);

        var wb = Component<WhiteBalance>(profile);
        wb.temperature.Override(-5f);
        wb.tint.Override(1f);

        var bloom = Component<Bloom>(profile);
        bloom.threshold.Override(1.3f);
        bloom.intensity.Override(0.18f);
        bloom.scatter.Override(0.6f);
        bloom.tint.Override(Color.white);
        bloom.clamp.Override(65472f);
        bloom.highQualityFiltering.Override(true);

        var vignette = Component<Vignette>(profile);
        vignette.color.Override(Color.black);
        vignette.center.Override(new Vector2(0.5f, 0.5f));
        vignette.intensity.Override(0.16f);
        vignette.smoothness.Override(0.42f);
        vignette.rounded.Override(false);
        EditorUtility.SetDirty(profile);

        Transform t = lighting.Find(PostVolumeName);
        GameObject go = t != null ? t.gameObject : new GameObject(PostVolumeName);
        if (t == null) go.transform.SetParent(lighting, false);
        go.layer = 0;
        var volume = go.GetComponent<Volume>();
        if (volume == null) volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10f;
        volume.weight = 1f;
        volume.sharedProfile = profile;
        volume.enabled = true;
        go.SetActive(true);
        EditorUtility.SetDirty(volume);
        Note("post volume '" + PostVolumeName + "': ACES, exposure +0.55, contrast 10, saturation 2, WB -5/+1, bloom 1.3/0.18, vignette 0.16");
    }

    static T Component<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet(out T c)) { c.active = true; return c; }
        c = profile.Add<T>(false);
        c.name = typeof(T).Name;
        c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
        AssetDatabase.AddObjectToAsset(c, profile);
        return c;
    }

    static void ApplyCameras(Scene scene)
    {
        // Desktop camera (scene root "Main Camera"): post on. On Quest the Android renderer has no post data, so
        // even this flag cannot turn post on there. The XR rig camera keeps post off (no vignette/bloom in the HMD).
        GameObject camGo = Root(scene, "Main Camera");
        Camera cam = camGo ? camGo.GetComponent<Camera>() : null;
        if (cam == null) { Warn("desktop 'Main Camera' not found"); return; }
        cam.allowHDR = true;
        cam.allowMSAA = true;
        var data = cam.GetComponent<UniversalAdditionalCameraData>();
        if (data == null) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.antialiasingQuality = AntialiasingQuality.High;
        data.dithering = true;
        data.stopNaN = false;
        data.renderShadows = true;
        EditorUtility.SetDirty(data);
        EditorUtility.SetDirty(cam);
        Note("desktop camera: post on, SMAA high, dithering");
    }

    // ------------------------------------------------------------------ hero studio

    // Plan rectangle (x, z) of the softbox canopy: centred on the hero car at rest.
    static Rect CanopyRect(Bounds hero)
    {
        return new Rect(hero.center.x - CanopyHalfWidth, hero.center.z - CanopyHalfLength, CanopyHalfWidth * 2f, CanopyHalfLength * 2f);
    }

    // A flush ceiling frame of four light boxes (white powder-coat housing, opal diffuser 12 mm proud of it, inset by
    // the lip): two long strips over the car's shoulders and two cross strips over its ends. Static for GI and the
    // reflection probes, lit by light probes itself, no shadows (it is a light source).
    static void BuildHeroCanopy(Transform lighting, Hall hall, Bounds hero)
    {
        Transform studio = ChildTransform(lighting, HeroStudioName);
        studio.localPosition = Vector3.zero; studio.localRotation = Quaternion.identity; studio.localScale = Vector3.one;
        Transform canopy = ChildTransform(studio, "Softbox Canopy");
        canopy.position = Vector3.zero; canopy.rotation = Quaternion.identity; canopy.localScale = Vector3.one;
        Material housing = EnsureSoftboxHousingMaterial(), diffuser = EnsureSoftboxMaterial();
        Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        Rect r = CanopyRect(hero);
        float top = hall.ceilY, bottom = hall.ceilY - CanopyDepth;
        float cross = r.width - 2f * CanopyStrip;
        var strips = new[]
        {
            new KeyValuePair<string, Rect>("Softbox Left", new Rect(r.xMin, r.yMin, CanopyStrip, r.height)),
            new KeyValuePair<string, Rect>("Softbox Right", new Rect(r.xMax - CanopyStrip, r.yMin, CanopyStrip, r.height)),
            new KeyValuePair<string, Rect>("Softbox Front", new Rect(r.xMin + CanopyStrip, r.yMax - CanopyStrip, cross, CanopyStrip)),
            new KeyValuePair<string, Rect>("Softbox Rear", new Rect(r.xMin + CanopyStrip, r.yMin, cross, CanopyStrip)),
        };
        var wanted = new HashSet<string>();
        foreach (var strip in strips)
        {
            Rect s = strip.Value;
            // Cross strips butt against the long ones with a 6 mm shadow gap.
            if (strip.Key == "Softbox Front" || strip.Key == "Softbox Rear") s = new Rect(s.xMin + 0.006f, s.yMin, s.width - 0.012f, s.height);
            Vector3 c = new Vector3(s.center.x, (top + bottom + 0.012f) * 0.5f, s.center.y);
            AddBox(canopy, strip.Key + " Housing", c, new Vector3(s.width, top - bottom - 0.012f, s.height), cube, housing);
            Vector3 dc = new Vector3(s.center.x, bottom + 0.009f, s.center.y);
            AddBox(canopy, strip.Key + " Diffuser", dc, new Vector3(s.width - 2f * CanopyLip, 0.018f, s.height - 2f * CanopyLip), cube, diffuser);
            wanted.Add(strip.Key + " Housing"); wanted.Add(strip.Key + " Diffuser");
        }
        foreach (Transform c in canopy.Cast<Transform>().ToList()) if (!wanted.Contains(c.name)) Object.DestroyImmediate(c.gameObject);
        Note("hero canopy: 4 softboxes " + F(r.width) + " x " + F(r.height) + " m frame, strips " + F(CanopyStrip) + " m, flush at " + F(top) + " m over " + F(r.center.x) + ", " + F(r.center.y) + ", " + F(CanopyKelvin) + "K x" + F(CanopyEmission) + " baked emissive");
    }

    static void AddBox(Transform parent, string name, Vector3 center, Vector3 size, Mesh cube, Material mat)
    {
        Transform t = parent.Find(name);
        GameObject go = t != null ? t.gameObject : new GameObject(name);
        if (t == null) go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(center, Quaternion.identity);
        go.transform.localScale = size;
        var mf = go.GetComponent<MeshFilter>();
        if (mf == null) mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = cube;
        var mr = go.GetComponent<MeshRenderer>();
        if (mr == null) mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.receiveGI = ReceiveGI.LightProbes;
        mr.lightProbeUsage = LightProbeUsage.BlendProbes;
        mr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        GameObjectUtility.SetStaticEditorFlags(go, GiFlags);
    }

    static Material EnsureSoftboxMaterial()
    {
        Material mat = EnsureLitMaterial(SoftboxMaterialPath, "AurelleStudioSoftbox");
        mat.SetColor("_BaseColor", new Color(0.96f, 0.96f, 0.95f));
        mat.SetFloat("_Smoothness", 0.25f);
        mat.SetFloat("_Metallic", 0f);
        mat.SetColor("_EmissionColor", Mathf.CorrelatedColorTemperatureToRGB(CanopyKelvin) * CanopyEmission);
        mat.EnableKeyword("_EMISSION");
        // Baked emissive: the canopy lights the podium and the probes in the bake (URP 17.3 keeps _EMISSION only
        // with a Realtime or Baked emissive flag).
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material EnsureSoftboxHousingMaterial()
    {
        Material mat = EnsureLitMaterial(SoftboxHousingMaterialPath, "AurelleStudioSoftboxHousing");
        mat.SetColor("_BaseColor", new Color(0.86f, 0.86f, 0.85f));   // white powder coat, like the ceiling
        mat.SetFloat("_Smoothness", 0.35f);
        mat.SetFloat("_Metallic", 0f);
        mat.SetColor("_EmissionColor", Color.black);
        mat.DisableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material EnsureLitMaterial(string path, string name)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (mat == null)
        {
            mat = new Material(lit) { name = name };
            AssetDatabase.CreateAsset(mat, path);
        }
        if (mat.shader != lit) mat.shader = lit;
        mat.enableInstancing = true;
        return mat;
    }

    // The contact shadow (all quality levels) and the podium's planar reflection (Ultra, desktop) on one object.
    static void ApplyHeroStudio(Scene scene, Transform lighting)
    {
        Transform studio = ChildTransform(lighting, HeroStudioName);
        GameObject car = Root(scene, HeroCarName);
        Transform deckT = FindPath(scene, PodiumDeckPath);
        Renderer deck = deckT != null ? deckT.GetComponent<Renderer>() : null;
        if (car == null || deck == null) { Warn("hero studio: CarConcept or the podium deck missing"); return; }
        float deckTop = deck.bounds.max.y;

        // Contact shadow quad: a flat quad 3 mm above the deck with the generated footprint texture.
        Rect area;
        Texture2D tex = BuildContactShadowTexture(car, deckTop, out area);
        Material mat = EnsureContactShadowMaterial(tex);
        Transform quad = ChildTransform(studio, "Hero Contact Shadow");
        quad.SetPositionAndRotation(new Vector3(area.center.x, deckTop + 0.003f, area.center.y), Quaternion.Euler(90f, 0f, 0f));
        quad.localScale = new Vector3(area.width, area.height, 1f);
        var mf = quad.GetComponent<MeshFilter>();
        if (mf == null) mf = quad.gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var mr = quad.GetComponent<MeshRenderer>();
        if (mr == null) mr = quad.gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        mr.receiveGI = ReceiveGI.LightProbes;
        GameObjectUtility.SetStaticEditorFlags(quad.gameObject, 0);
        var shadow = studio.GetComponent<AurelleHeroContactShadow>();
        if (shadow == null) shadow = studio.gameObject.AddComponent<AurelleHeroContactShadow>();
        shadow.shadowQuad = mr;
        EditorUtility.SetDirty(shadow);

        // Planar reflection: the mirror layer, the SSAO-free renderer and the deck plane.
        EnsureLayerName(MirrorLayer, MirrorLayerName);
        int rendererIndex = EnsureMirrorRenderer();
        var planar = studio.GetComponent<AurellePlanarReflection>();
        if (planar == null) planar = studio.gameObject.AddComponent<AurellePlanarReflection>();
        planar.deck = deck;
        planar.planeHeight = deckTop;
        planar.car = car.transform;
        planar.mirrorLayer = MirrorLayer;
        planar.mirrorRendererIndex = rendererIndex;
        planar.minQualityLevel = PcQualityLevel;
        EditorUtility.SetDirty(planar);
        Note("hero studio: contact shadow " + F(area.width) + " x " + F(area.height) + " m at deck " + F(deckTop) + " (" + ContactShadowTexturePath + "), planar reflection at y " + F(deckTop) + " on layer " + MirrorLayer + " through renderer " + rendererIndex + " (Ultra, desktop)");
    }

    static Material EnsureContactShadowMaterial(Texture2D tex)
    {
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(ContactShadowMaterialPath);
        if (mat == null)
        {
            mat = new Material(unlit) { name = "AurelleHeroContactShadow" };
            AssetDatabase.CreateAsset(mat, ContactShadowMaterialPath);
        }
        if (mat.shader != unlit) mat.shader = unlit;
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", new Color(0f, 0f, 0f, 1f));
        mat.SetFloat("_Surface", 1f);       // transparent
        mat.SetFloat("_Blend", 0f);         // alpha: black over the deck = multiply by (1 - a)
        mat.SetFloat("_AlphaClip", 0f);
        mat.SetFloat("_Cull", 2f);
        if (mat.HasProperty("_QueueControl")) mat.SetFloat("_QueueControl", 0f);
        // URP's material validation (on import, in Play Mode) recomputes the queue from the surface type and
        // _QueueOffset, so the offset carries the "before the car's glass" queue; without it the queue fell back to 3000.
        if (mat.HasProperty("_QueueOffset")) mat.SetFloat("_QueueOffset", -10f);
        BaseShaderGUI.SetMaterialKeywords(mat);
        mat.renderQueue = (int)RenderQueue.Transparent - 10;   // before the car's glass
        mat.enableInstancing = false;
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Ambient occlusion of the car on the deck, from its geometry: the lowest car surface above every 2 cm cell of the
    // footprint, then three blurred layers (tyre contact, underbody, the whole footprint) combined as occlusion.
    // Deterministic, rewritten only when it changes.
    static Texture2D BuildContactShadowTexture(GameObject car, float deckTop, out Rect area)
    {
        Transform v6 = car.transform.Find(V6Name), internals = car.transform.Find("Internals");
        var filters = new List<MeshFilter>();
        Bounds b = new Bounds();
        bool any = false;
        foreach (MeshFilter mf in car.GetComponentsInChildren<MeshFilter>(false))
        {
            var r = mf.GetComponent<MeshRenderer>();
            if (mf.sharedMesh == null || r == null || !r.enabled) continue;
            if ((v6 != null && mf.transform.IsChildOf(v6)) || (internals != null && mf.transform.IsChildOf(internals))) continue;
            filters.Add(mf);
            if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
        }
        const float Margin = 0.5f;
        const int N = 256;
        area = new Rect(b.min.x - Margin, b.min.z - Margin, b.size.x + 2f * Margin, b.size.z + 2f * Margin);
        float cw = area.width / N, ch = area.height / N;
        var height = new float[N * N];
        for (int i = 0; i < height.Length; i++) height[i] = float.MaxValue;
        foreach (MeshFilter mf in filters)
        {
            Mesh mesh = mf.sharedMesh;
            Vector3[] v = mesh.vertices;
            Matrix4x4 m = mf.transform.localToWorldMatrix;
            for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                int[] tris = mesh.GetTriangles(sub);
                for (int t = 0; t + 2 < tris.Length; t += 3)
                    RasterizeLowest(v[tris[t]], v[tris[t + 1]], v[tris[t + 2]], area, cw, ch, N, deckTop, height);
            }
        }
        // Layers: contact (< 6 cm), underbody (< 40 cm), footprint (anything); sigma in metres, weight.
        float[] contact = Layer(height, h => h < 0.06f), under = Layer(height, h => h < 0.4f), all = Layer(height, h => h < float.MaxValue);
        Blur(contact, N, 0.022f / cw, 0.022f / ch);
        Blur(under, N, 0.11f / cw, 0.11f / ch);
        Blur(all, N, 0.32f / cw, 0.32f / ch);
        var pixels = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                float occ = 1f - (1f - 0.95f * contact[i]) * (1f - 0.62f * under[i]) * (1f - 0.42f * all[i]);
                // Fade to nothing at the quad's border.
                float ex = Mathf.Min(x, N - 1 - x) / (N * 0.06f), ey = Mathf.Min(y, N - 1 - y) / (N * 0.06f);
                occ *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(ex, ey)));
                pixels[i] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(Mathf.Clamp01(occ * 0.92f) * 255f));
            }
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false, true);
        tex.SetPixels32(pixels);
        tex.Apply();
        byte[] png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);
        string full = Path.GetFullPath(ContactShadowTexturePath);
        bool changed = !File.Exists(full) || !File.ReadAllBytes(full).SequenceEqual(png);
        if (changed)
        {
            File.WriteAllBytes(full, png);
            AssetDatabase.ImportAsset(ContactShadowTexturePath, ImportAssetOptions.ForceUpdate);
        }
        var ti = AssetImporter.GetAtPath(ContactShadowTexturePath) as TextureImporter;
        if (ti != null)
        {
            bool dirty = ti.sRGBTexture || ti.alphaSource != TextureImporterAlphaSource.FromInput || ti.wrapMode != TextureWrapMode.Clamp || !ti.mipmapEnabled || ti.maxTextureSize != 256 || ti.textureCompression != TextureImporterCompression.Uncompressed;
            if (dirty)
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = false;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.mipmapEnabled = true;
                ti.maxTextureSize = 256;
                ti.textureCompression = TextureImporterCompression.Uncompressed;   // 256 KB, smooth gradients
                ti.SaveAndReimport();
            }
        }
        Note("contact shadow texture: " + filters.Count + " car meshes rasterised into " + N + "x" + N + (changed ? " (rewritten)" : " (unchanged)"));
        return AssetDatabase.LoadAssetAtPath<Texture2D>(ContactShadowTexturePath);
    }

    static void RasterizeLowest(Vector3 a, Vector3 b, Vector3 c, Rect area, float cw, float ch, int n, float deckTop, float[] height)
    {
        float ax = (a.x - area.xMin) / cw, az = (a.z - area.yMin) / ch;
        float bx = (b.x - area.xMin) / cw, bz = (b.z - area.yMin) / ch;
        float cx = (c.x - area.xMin) / cw, cz = (c.z - area.yMin) / ch;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ax, Mathf.Min(bx, cx)))), x1 = Mathf.Min(n - 1, Mathf.CeilToInt(Mathf.Max(ax, Mathf.Max(bx, cx))));
        int z0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(az, Mathf.Min(bz, cz)))), z1 = Mathf.Min(n - 1, Mathf.CeilToInt(Mathf.Max(az, Mathf.Max(bz, cz))));
        float area2 = (bx - ax) * (cz - az) - (cx - ax) * (bz - az);
        float lowest = Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - deckTop;
        if (Mathf.Abs(area2) < 1e-6f)
        {
            // Vertical (edge-on) triangle: mark the cells along it with its lowest point.
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) if (x1 - x0 <= 1 || z1 - z0 <= 1) Lower(height, z * n + x, lowest);
            return;
        }
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                float px = x + 0.5f, pz = z + 0.5f;
                float w0 = ((bx - px) * (cz - pz) - (cx - px) * (bz - pz)) / area2;
                float w1 = ((cx - px) * (az - pz) - (ax - px) * (cz - pz)) / area2;
                float w2 = 1f - w0 - w1;
                if (w0 < -0.02f || w1 < -0.02f || w2 < -0.02f) continue;
                Lower(height, z * n + x, w0 * a.y + w1 * b.y + w2 * c.y - deckTop);
            }
    }

    static void Lower(float[] height, int i, float h)
    {
        if (h < height[i]) height[i] = Mathf.Max(0f, h);
    }

    static float[] Layer(float[] height, Func<float, bool> keep)
    {
        var l = new float[height.Length];
        for (int i = 0; i < height.Length; i++) l[i] = keep(height[i]) ? 1f : 0f;
        return l;
    }

    // Separable Gaussian blur with clamped borders; sigma in cells.
    static void Blur(float[] img, int n, float sigmaX, float sigmaY)
    {
        BlurPass(img, n, Mathf.Max(0.5f, sigmaX), true);
        BlurPass(img, n, Mathf.Max(0.5f, sigmaY), false);
    }

    static void BlurPass(float[] img, int n, float sigma, bool horizontal)
    {
        int radius = Mathf.CeilToInt(sigma * 3f);
        var kernel = new float[radius * 2 + 1];
        float sum = 0f;
        for (int k = -radius; k <= radius; k++) { kernel[k + radius] = Mathf.Exp(-0.5f * k * k / (sigma * sigma)); sum += kernel[k + radius]; }
        for (int k = 0; k < kernel.Length; k++) kernel[k] /= sum;
        var src = (float[])img.Clone();
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float acc = 0f;
                for (int k = -radius; k <= radius; k++)
                {
                    int sx = horizontal ? Mathf.Clamp(x + k, 0, n - 1) : x, sy = horizontal ? y : Mathf.Clamp(y + k, 0, n - 1);
                    acc += src[sy * n + sx] * kernel[k + radius];
                }
                img[y * n + x] = acc;
            }
    }

    // A renderer for the planar reflection camera only: Forward+ like the PC renderer, but without SSAO and without
    // post-processing data. Registered as an extra renderer of the PC pipeline asset; returns its index there.
    static int EnsureMirrorRenderer()
    {
        var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(MirrorRendererPath);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<UniversalRendererData>();
            data.name = "AurelleMirrorRenderer";
            AssetDatabase.CreateAsset(data, MirrorRendererPath);
        }
        var so = new SerializedObject(data);
        SetProp(so, "m_RenderingMode", (int)RenderingMode.ForwardPlus);
        SetProp(so, "m_DepthPrimingMode", 0);
        SetProp(so, "m_IntermediateTextureMode", 0);   // auto: renders straight into the reflection texture
        so.ApplyModifiedPropertiesWithoutUndo();
        if (data.postProcessData != null) data.postProcessData = null;
        if (data.rendererFeatures.Count > 0) Warn("mirror renderer has renderer features; remove them (SSAO would run for the reflection)");
        EditorUtility.SetDirty(data);

        var pc = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcPipelinePath);
        if (pc == null) return -1;
        var pso = new SerializedObject(pc);
        SerializedProperty list = pso.FindProperty("m_RendererDataList");
        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == data) return i;
        list.arraySize++;
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = data;
        pso.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(pc);
        return list.arraySize - 1;
    }

    static void EnsureLayerName(int layer, string name)
    {
        Object tm = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset").FirstOrDefault();
        if (tm == null) return;
        var so = new SerializedObject(tm);
        SerializedProperty layers = so.FindProperty("layers");
        if (layers == null || layer >= layers.arraySize) return;
        SerializedProperty p = layers.GetArrayElementAtIndex(layer);
        if (p.stringValue == name) return;
        if (!string.IsNullOrEmpty(p.stringValue)) { Warn("layer " + layer + " is named '" + p.stringValue + "'; the planar reflection uses it anyway"); return; }
        p.stringValue = name;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------------ lighting settings + bake

    static void ApplyLightingSettings()
    {
        var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
        if (ls == null)
        {
            ls = new LightingSettings { name = "AurelleLighting" };
            AssetDatabase.CreateAsset(ls, LightingSettingsPath);
        }
        ls.bakedGI = true;
        ls.realtimeGI = false;
        ls.mixedBakeMode = MixedLightingMode.IndirectOnly;   // Baked Indirect
        ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
        ls.lightmapResolution = LightmapTexelsPerUnit;
        ls.lightmapPadding = 3;
        ls.lightmapMaxSize = LightmapMaxSize;
        ls.lightmapCompression = LightmapCompression.HighQuality;
        ls.directionalityMode = LightmapsMode.NonDirectional;
        ls.ao = true;
        // Short, soft occlusion: a longer reach smears dark halos from the black display mounts across the walls.
        ls.aoMaxDistance = 0.3f;
        ls.aoExponentIndirect = 0.75f;
        ls.aoExponentDirect = 0f;
        ls.extractAO = false;
        ls.albedoBoost = 1f;
        ls.indirectScale = 1f;
        ls.maxBounces = 3;
        ls.minBounces = 2;
        ls.directSampleCount = 64;
        ls.indirectSampleCount = 512;
        ls.environmentSampleCount = 512;
        ls.lightProbeSampleCountMultiplier = 4f;
        ls.environmentImportanceSampling = true;
        ls.filteringMode = LightingSettings.FilterMode.Auto;
        ls.prioritizeView = false;
        ls.realtimeEnvironmentLighting = false;
        EditorUtility.SetDirty(ls);
        Lightmapping.lightingSettings = ls;
        // The feature wall is seen at arm's length behind the reception and the display boards: double its texel density.
        foreach (Renderer wall in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (wall.name != "Rear Feature Wall") continue;
            var so = new SerializedObject(wall);
            SerializedProperty scale = so.FindProperty("m_ScaleInLightmap");
            if (scale != null && !Mathf.Approximately(scale.floatValue, 2f)) { scale.floatValue = 2f; so.ApplyModifiedPropertiesWithoutUndo(); }
        }
        Note("lighting settings " + LightingSettingsPath + ": baked indirect, " + ls.lightmapper + ", " + F(ls.lightmapResolution) + " texels/m, max " + ls.lightmapMaxSize + ", non-directional, AO, 3 bounces");
    }

    static bool BakeLighting(Scene scene)
    {
        AssetDatabase.SaveAssets();
        Transform lighting = FindPath(scene, LightingRootName);
        Transform occluders = lighting != null ? lighting.Find(BakeOccluderRootName) : null;
        if (occluders != null) occluders.gameObject.SetActive(true);
        var sw = Stopwatch.StartNew();
        bool ok;
        try { ok = Lightmapping.Bake(); }
        finally { if (occluders != null) occluders.gameObject.SetActive(false); }
        sw.Stop();
        Note("bake " + (ok ? "OK" : "FAILED") + " in " + F((float)sw.Elapsed.TotalSeconds) + " s, lightmapper " + (Lightmapping.lightingSettings != null ? Lightmapping.lightingSettings.lightmapper.ToString() : "?"));
        if (!ok) return false;
        PostBakeImportSettings(scene);
        ReportBakeOutput(scene);
        return true;
    }

    // Quest gets smaller copies of the baked textures (1K lightmaps, 128 px probe cubemaps); PC keeps full size.
    static void PostBakeImportSettings(Scene scene)
    {
        var paths = new HashSet<string>();
        foreach (var d in LightmapSettings.lightmaps)
        {
            if (d.lightmapColor) paths.Add(AssetDatabase.GetAssetPath(d.lightmapColor));
            if (d.lightmapDir) paths.Add(AssetDatabase.GetAssetPath(d.lightmapDir));
        }
        foreach (string p in paths) SetAndroidMax(p, 1024);
        foreach (var probe in Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (probe.bakedTexture) SetAndroidMax(AssetDatabase.GetAssetPath(probe.bakedTexture), probe.name == HeroProbeName ? HeroProbeAndroidMax : 128);
        // The sky reflection cubemap generated by the bake (used by everything outside the probes).
        string folder = Path.Combine(Path.GetDirectoryName(scene.path), Path.GetFileNameWithoutExtension(scene.path)).Replace('\\', '/');
        if (ReflectionProbe.defaultTexture != null) SetAndroidMax(AssetDatabase.GetAssetPath(ReflectionProbe.defaultTexture), 128);
        var heroProbe = Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(rp => rp.name == HeroProbeName);
        string heroPath = heroProbe != null && heroProbe.bakedTexture ? AssetDatabase.GetAssetPath(heroProbe.bakedTexture) : null;
        foreach (string guid in AssetDatabase.FindAssets("t:Cubemap", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path != heroPath) SetAndroidMax(path, 128);
        }
    }

    static void SetAndroidMax(string path, int max)
    {
        if (string.IsNullOrEmpty(path)) return;
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;
        var ps = ti.GetPlatformTextureSettings("Android");
        if (ps.overridden && ps.maxTextureSize == max) return;
        ps.overridden = true;
        ps.maxTextureSize = max;
        ps.format = TextureImporterFormat.Automatic;
        ti.SetPlatformTextureSettings(ps);
        ti.SaveAndReimport();
    }

    static void ReportBakeOutput(Scene scene)
    {
        string folder = Path.Combine(Path.GetDirectoryName(scene.path), Path.GetFileNameWithoutExtension(scene.path));
        long total = 0;
        var sb = new StringBuilder();
        if (Directory.Exists(folder))
            foreach (string f in Directory.GetFiles(folder).Where(f => !f.EndsWith(".meta", StringComparison.Ordinal)).OrderBy(f => f))
            {
                long len = new FileInfo(f).Length; total += len;
                sb.Append(Path.GetFileName(f)).Append(' ').Append((len / 1024f).ToString("F0", Inv)).Append(" KB; ");
            }
        var maps = LightmapSettings.lightmaps;
        string sizes = string.Join(", ", maps.Select(m => m.lightmapColor ? m.lightmapColor.width + "x" + m.lightmapColor.height : "?"));
        int probes = LightmapSettings.lightProbes != null ? LightmapSettings.lightProbes.count : 0;
        Note("bake output " + folder + ": " + (total / 1048576f).ToString("F2", Inv) + " MB [" + sb + "] lightmaps " + maps.Length + " (" + sizes + "), light probes " + probes);
    }

    // ------------------------------------------------------------------ helpers

    static GameObject Root(Scene scene, string name)
    {
        return scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);
    }

    static Transform RootOrCreate(Scene scene, string name)
    {
        GameObject g = Root(scene, name);
        if (g == null)
        {
            g = new GameObject(name);
            SceneManager.MoveGameObjectToScene(g, scene);
        }
        return g.transform;
    }

    static Transform FindPath(Scene scene, string path)
    {
        string[] parts = path.Split('/');
        GameObject root = Root(scene, parts[0]);
        if (root == null) return null;
        Transform t = root.transform;
        for (int i = 1; i < parts.Length && t != null; i++) t = t.Find(parts[i]);
        return t;
    }

    static bool TryRenderer(Scene scene, string path, out Renderer r)
    {
        Transform t = FindPath(scene, path);
        r = t ? t.GetComponent<Renderer>() : null;
        return r != null;
    }

    static bool TryRendererBounds(Transform root, out Bounds b)
    {
        b = new Bounds();
        bool any = false;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
        }
        return any;
    }

    // Hero car bounds at rest, without the V6 (hidden while the explorer rests at stage 1).
    static bool TryHeroBounds(Scene scene, out Bounds b)
    {
        b = new Bounds();
        GameObject car = Root(scene, HeroCarName);
        if (car == null) return false;
        Transform v6 = car.transform.Find(V6Name);
        bool any = false;
        foreach (Renderer r in car.GetComponentsInChildren<Renderer>(true))
        {
            if (v6 != null && r.transform.IsChildOf(v6)) continue;
            if (!r.gameObject.activeInHierarchy) continue;
            if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
        }
        return any;
    }

    static IEnumerable<Light> AllLights(Scene scene)
    {
        return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>(true));
    }

    static IEnumerable<Renderer> AllRenderers(Scene scene)
    {
        return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true));
    }

    static Light ChildLight(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        return t ? t.GetComponent<Light>() : null;
    }

    static Transform ChildTransform(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        if (t != null) return t;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static T ChildComponent<T>(Transform parent, string name) where T : Component
    {
        Transform t = parent.Find(name);
        GameObject go;
        if (t == null)
        {
            go = new GameObject(name);
            go.transform.SetParent(parent, false);
        }
        else go = t.gameObject;
        T c = go.GetComponent<T>();
        if (c == null) c = go.AddComponent<T>();
        go.SetActive(true);
        return c;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static void SetProp(SerializedObject so, string name, object value)
    {
        SerializedProperty p = so.FindProperty(name);
        if (p == null) { Warn("property not found: " + so.targetObject.name + "." + name); return; }
        Assign(p, value);
    }

    static void SetRel(SerializedProperty parent, string name, object value)
    {
        SerializedProperty p = parent.FindPropertyRelative(name);
        if (p == null) { Warn("property not found: " + parent.propertyPath + "." + name); return; }
        Assign(p, value);
    }

    static void Assign(SerializedProperty p, object value)
    {
        switch (value)
        {
            case bool b: p.boolValue = b; break;
            case int i: if (p.propertyType == SerializedPropertyType.Enum) p.intValue = i; else if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = i != 0; else p.intValue = i; break;
            case float f: p.floatValue = f; break;
            case Vector3 v: p.vector3Value = v; break;
            case Vector2 v2: p.vector2Value = v2; break;
            default: Warn("unsupported value for " + p.propertyPath); break;
        }
    }

    static string F(float v) { return v.ToString("0.##", Inv); }

    static void Note(string s) { report.AppendLine(s); }

    static void Warn(string s)
    {
        report.AppendLine("WARNING " + s);
        Debug.LogWarning(Tag + s);
    }

    static void FlushReport(bool ok)
    {
        Debug.Log(Tag + (ok ? "done" : "FAILED") + "\n" + report);
    }
}
#endif
