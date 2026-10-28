#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Aurelle > Realism > Upgrade People: realistic people for the showroom presenter and the exterior crowd.
/// <para>
/// Remy.fbx embeds its Mixamo maps (albedo, normal, specular, gloss and opacity per part), but they were never extracted,
/// so the model's own materials imported untextured (the pale pedestrians and riders) and the presenter wore flat colour
/// materials (dark skin, glowing white eyeballs). This tool derives URP textures from those embedded maps, builds
/// Specular-workflow URP Lit materials per part (skin, eyes, eyelashes, hair, top, bottoms, shoes), remaps the model's
/// materials to them, dresses Remy Presenter, and dresses every pedestrian and cyclist rider in its own crowd look through
/// <see cref="ExteriorEnvironmentBuilder.ApplyCrowdLooks"/> (the builder applies the same looks when it regenerates).
/// </para>
/// <para>
/// Idempotent: textures are derived only when missing, materials and import settings are rewritten in place with the
/// same values, and renderers are only touched where an assignment differs.
/// </para>
/// </summary>
public static class AurelleRealismPeople
{
    public const string ScenePath = "Assets/Scenes/SampleScene.unity";
    public const string ModelPath = "Assets/Models/Presenter/Remy.fbx";
    public const string TextureFolder = "Assets/Models/Presenter/Textures";
    public const string MaterialFolder = "Assets/Materials/Realism/People";
    public const string CrowdFolder = MaterialFolder + "/Crowd";
    private const string ExtractFolder = TextureFolder + "/_Embedded";
    private const string PresenterName = "Remy Presenter";

    // ------------------------------------------------------------------ entry points

    [MenuItem("Aurelle/Realism/Upgrade People")]
    public static void UpgradeFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Stop Play Mode before upgrading the people materials.");
            return;
        }
        Apply();
        AssetDatabase.SaveAssets();
        Debug.Log("Aurelle people upgraded; save the scene to keep the presenter and crowd assignments.");
    }

    /// <summary>Rebuilds the derived textures from Remy.fbx in place (same files and GUIDs), e.g. after a remap change.</summary>
    [MenuItem("Aurelle/Realism/Upgrade People (Rebuild Textures)")]
    public static void RebuildFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Apply(true);
        AssetDatabase.SaveAssets();
    }

    /// <summary>Batch entry: opens SampleScene, applies the upgrade, saves scene and assets, exits in batch mode.</summary>
    public static void Batch() => RunBatch(false);

    /// <summary>Batch entry that also rebuilds the derived textures in place.</summary>
    public static void BatchRebuildTextures() => RunBatch(true);

    private static void RunBatch(bool rebuildTextures)
    {
        int code = 0;
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Apply(rebuildTextures);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    /// <summary>Applies the upgrade to the project assets and to every loaded scene.</summary>
    public static void Apply() => Apply(false);

    /// <summary>Applies the upgrade; <paramref name="rebuildTextures"/> re-derives every texture, not only missing ones.</summary>
    public static void Apply(bool rebuildTextures)
    {
        EnsureFolder(TextureFolder);
        EnsureFolder(MaterialFolder);
        EnsureFolder(CrowdFolder);
        int derived = DeriveTextures(rebuildTextures);
        foreach (TextureSpec spec in Textures) ConfigureTexture(spec);
        Dictionary<string, Material> remy = BuildRemyMaterials();
        int crowdMaterials = BuildCrowdMaterials();
        bool remapped = RemapModelMaterials(remy);
        int presenterParts = DressPresenters(remy);
        int crowd = DressCrowds();
        Debug.Log("AurelleRealismPeople: textures derived " + derived + ", Remy materials " + remy.Count + ", crowd materials " + crowdMaterials
            + ", model remap " + (remapped ? "updated" : "unchanged") + ", presenter parts changed " + presenterParts + ", crowd parts changed " + crowd);
    }

    // ------------------------------------------------------------------ textures

    private sealed class TextureSpec
    {
        public string Name;
        public bool Normal, Alpha, HighQuality, Clamp;
        public int Standalone, Android;
        public float AlphaTest; // > 0: alpha-tested map, mip coverage preserved at this cutoff
    }

    // Standalone keeps the source resolution (2K only for the face/body albedo and normal); Android gets 1K at most.
    private static readonly TextureSpec[] Textures =
    {
        new TextureSpec { Name = "Remy_Body_Albedo.jpg", Standalone = 2048, Android = 1024, HighQuality = true },
        new TextureSpec { Name = "Remy_Body_NormalMap.png", Normal = true, Standalone = 2048, Android = 1024, HighQuality = true },
        new TextureSpec { Name = "Remy_Body_SpecGloss.png", Alpha = true, Standalone = 1024, Android = 512 },
        new TextureSpec { Name = "Remy_Eyelashes_Albedo.png", Alpha = true, Standalone = 256, Android = 256, Clamp = true, AlphaTest = EyelashCutoff },
        new TextureSpec { Name = "Remy_Top_Albedo.jpg", Standalone = 1024, Android = 1024 },
        new TextureSpec { Name = "Remy_Top_NormalMap.png", Normal = true, Standalone = 1024, Android = 512 },
        new TextureSpec { Name = "Remy_Top_SpecGloss.png", Alpha = true, Standalone = 512, Android = 256 },
        new TextureSpec { Name = "Remy_Bottom_Albedo.jpg", Standalone = 1024, Android = 1024 },
        new TextureSpec { Name = "Remy_Bottom_NormalMap.png", Normal = true, Standalone = 1024, Android = 512 },
        new TextureSpec { Name = "Remy_Bottom_SpecGloss.png", Alpha = true, Standalone = 512, Android = 256 },
        new TextureSpec { Name = "Remy_Shoes_Albedo.jpg", Standalone = 1024, Android = 512 },
        new TextureSpec { Name = "Remy_Shoes_NormalMap.png", Normal = true, Standalone = 1024, Android = 512 },
        new TextureSpec { Name = "Remy_Shoes_SpecGloss.png", Alpha = true, Standalone = 512, Android = 256 },
        new TextureSpec { Name = "Remy_Hair_Albedo.png", Alpha = true, Standalone = 1024, Android = 1024, AlphaTest = HairCutoff },
        new TextureSpec { Name = "Remy_Hair_NormalMap.png", Normal = true, Standalone = 512, Android = 512 },
        new TextureSpec { Name = "Remy_Hair_SpecGloss.png", Alpha = true, Standalone = 512, Android = 256 },
        new TextureSpec { Name = "Crowd_Top_Neutral.jpg", Standalone = 1024, Android = 512 },
        new TextureSpec { Name = "Crowd_Bottom_Neutral.jpg", Standalone = 1024, Android = 512 },
        new TextureSpec { Name = "Crowd_Hair_Neutral.png", Alpha = true, Standalone = 1024, Android = 512, AlphaTest = HairCutoff },
    };

    private const float HairCutoff = 0.4f, EyelashCutoff = 0.5f;

    // The lash strips sit in a 256 px window of the 2048 px body atlas (texels 998..1254 x 1202..1458, from the bottom left);
    // the eyelash material maps the atlas UVs onto the cropped texture with this scale and offset. Of the four strips, the
    // two in crop columns 85..170 are the lower lashes; their opacity is reduced so they read finer than the upper ones.
    private const int LashX = 998, LashY = 1202, LashSize = 256, AtlasSize = 2048, LowerLashMin = 85, LowerLashMax = 170;
    private const float LowerLashOpacity = 0.55f;

    private static readonly Vector2 LashTiling = new Vector2(AtlasSize / (float)LashSize, AtlasSize / (float)LashSize);
    private static readonly Vector2 LashOffset = new Vector2(-LashX / (float)LashSize, -LashY / (float)LashSize);

    // Mixamo's specular and gloss maps are legacy Phong intensities, not PBR values. Each part maps them to a linear
    // reflectance F0 (stored sRGB-encoded in RGB, like the albedo) and a smoothness (alpha, linear):
    //   F0 = clamp(f0Base + specular * f0Gain, f0Min, f0Max), smoothness = clamp(sBias + gloss * sGain, 0, sMax).
    private struct SpecGlossRemap
    {
        public float F0Base, F0Gain, F0Min, F0Max, SBias, SGain, SMax;
        public SpecGlossRemap(float f0Base, float f0Gain, float f0Min, float f0Max, float sBias, float sGain, float sMax)
        {
            F0Base = f0Base; F0Gain = f0Gain; F0Min = f0Min; F0Max = f0Max; SBias = sBias; SGain = sGain; SMax = sMax;
        }
    }

    // Skin: the map's typical 0.09 becomes F0 0.028 (skin), lips at most 0.045; gloss 0.30 becomes smoothness 0.41 and the
    // lips at most 0.55 (glossier lips glint along the seam, which URP does not occlude). Cloth: F0 0.03-0.10 (fibres,
    // prints), smoothness about 0.19 for the cotton and the shorts. Shoes: rubber and glossy trims. Hair: F0 about 0.046
    // (keratin), smoothness about 0.53.
    private static readonly SpecGlossRemap SkinRemap = new SpecGlossRemap(0f, 0.311f, 0.012f, 0.045f, 0.06f, 1.15f, 0.55f);
    private static readonly SpecGlossRemap ClothRemap = new SpecGlossRemap(0.03f, 0.07f, 0.03f, 0.1f, 0.15f, 0.55f, 0.7f);
    private static readonly SpecGlossRemap ShoeRemap = new SpecGlossRemap(0.035f, 0.1f, 0.035f, 0.12f, 0.12f, 0.6f, 0.75f);
    private static readonly SpecGlossRemap HairRemap = new SpecGlossRemap(0f, 0.17f, 0.025f, 0.08f, 0.1f, 0.85f, 0.8f);

    private static int DeriveTextures(bool all)
    {
        var missing = new List<TextureSpec>();
        foreach (TextureSpec spec in Textures)
            if (all || !File.Exists(Absolute(TextureFolder + "/" + spec.Name))) missing.Add(spec);
        if (missing.Count == 0) return 0;

        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null) throw new InvalidOperationException("People upgrade: " + ModelPath + " is missing.");
        EnsureFolder(ExtractFolder);
        try
        {
            if (!importer.ExtractTextures(ExtractFolder) || !File.Exists(Absolute(ExtractFolder + "/Remy_Body_Diffuse.png")))
                throw new InvalidOperationException("People upgrade: the embedded textures of " + ModelPath + " could not be extracted.");
            foreach (TextureSpec spec in missing) Derive(spec.Name);
        }
        finally
        {
            AssetDatabase.DeleteAsset(ExtractFolder);
        }
        foreach (TextureSpec spec in missing)
            AssetDatabase.ImportAsset(TextureFolder + "/" + spec.Name, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        return missing.Count;
    }

    private static void Derive(string name)
    {
        string output = Absolute(TextureFolder + "/" + name);
        switch (name)
        {
            case "Remy_Body_Albedo.jpg": WriteJpg(output, Load("Remy_Body_Diffuse.png")); break;
            case "Remy_Body_NormalMap.png": File.Copy(Source("Remy_Body_Normal.png"), output, true); break;
            case "Remy_Body_SpecGloss.png": WritePng(output, SpecGloss("Remy_Body", SkinRemap, 1024)); break;
            case "Remy_Eyelashes_Albedo.png": WritePng(output, EyelashCrop()); break;
            case "Remy_Top_Albedo.jpg": WriteJpg(output, Load("Remy_Top_Diffuse.png")); break;
            case "Remy_Top_NormalMap.png": File.Copy(Source("Remy_Top_Normal.png"), output, true); break;
            case "Remy_Top_SpecGloss.png": WritePng(output, SpecGloss("Remy_Top", ClothRemap, 512)); break;
            case "Remy_Bottom_Albedo.jpg": WriteJpg(output, Load("Remy_Bottom_Diffuse.png")); break;
            case "Remy_Bottom_NormalMap.png": File.Copy(Source("Remy_Bottom_Normal.png"), output, true); break;
            case "Remy_Bottom_SpecGloss.png": WritePng(output, SpecGloss("Remy_Bottom", ClothRemap, 512)); break;
            case "Remy_Shoes_Albedo.jpg": WriteJpg(output, Load("Remy_Shoes_Diffuse.png")); break;
            case "Remy_Shoes_NormalMap.png": File.Copy(Source("Remy_Shoes_Normal.png"), output, true); break;
            case "Remy_Shoes_SpecGloss.png": WritePng(output, SpecGloss("Remy_Shoes", ShoeRemap, 512)); break;
            case "Remy_Hair_Albedo.png": WritePng(output, WithAlpha(Load("Remy_Hair_Diffuse.png"), Load("Remy_Hair_Opacity.png"))); break;
            case "Remy_Hair_NormalMap.png": File.Copy(Source("Remy_Hair_Normal.png"), output, true); break;
            case "Remy_Hair_SpecGloss.png": WritePng(output, SpecGloss("Remy_Hair", HairRemap, 512)); break;
            // Neutral (grey) garments keep the knit, seams and pockets but take any colour from the material's base colour.
            case "Crowd_Top_Neutral.jpg": WriteJpg(output, Neutral(Load("Remy_Top_Diffuse.png"), 0.8f, 0.3f, 0.85f, 1.15f)); break;
            case "Crowd_Bottom_Neutral.jpg": WriteJpg(output, Neutral(Load("Remy_Bottom_Diffuse.png"), 0.78f, 0.6f, 0.45f, 1.25f)); break;
            case "Crowd_Hair_Neutral.png":
                WritePng(output, WithAlpha(Neutral(Load("Remy_Hair_Diffuse.png"), 0.72f, 1f, 0f, 2f), Load("Remy_Hair_Opacity.png")));
                break;
            default: throw new ArgumentException("Unknown people texture " + name);
        }
    }

    private sealed class Pixels
    {
        public int Width, Height;
        public Color32[] Data;
    }

    private static string Source(string file) => Absolute(ExtractFolder + "/" + file);

    private static Pixels Load(string file)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!texture.LoadImage(File.ReadAllBytes(Source(file)))) throw new InvalidOperationException("Cannot decode " + file);
            return new Pixels { Width = texture.width, Height = texture.height, Data = texture.GetPixels32() };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static Pixels SpecGloss(string part, SpecGlossRemap remap, int size)
    {
        Pixels specular = Load(part + "_Specular.png"), gloss = Load(part + "_Gloss.png");
        var result = new Pixels { Width = specular.Width, Height = specular.Height, Data = new Color32[specular.Data.Length] };
        for (int i = 0; i < result.Data.Length; i++)
        {
            float f0 = Mathf.Clamp(remap.F0Base + specular.Data[i].r / 255f * remap.F0Gain, remap.F0Min, remap.F0Max);
            float smooth = Mathf.Clamp(remap.SBias + gloss.Data[i].r / 255f * remap.SGain, 0f, remap.SMax);
            byte f = ToByte(Mathf.LinearToGammaSpace(f0));
            result.Data[i] = new Color32(f, f, f, ToByte(smooth));
        }
        return Downsample(result, size);
    }

    private static Pixels WithAlpha(Pixels color, Pixels opacity)
    {
        if (opacity.Width != color.Width || opacity.Height != color.Height) throw new InvalidOperationException("Opacity map size differs from its albedo.");
        for (int i = 0; i < color.Data.Length; i++) color.Data[i].a = opacity.Data[i].r;
        return color;
    }

    private static Pixels EyelashCrop()
    {
        Pixels albedo = WithAlpha(Load("Remy_Body_Diffuse.png"), Load("Remy_Body_Opacity.png"));
        var crop = new Pixels { Width = LashSize, Height = LashSize, Data = new Color32[LashSize * LashSize] };
        for (int y = 0; y < LashSize; y++)
            for (int x = 0; x < LashSize; x++)
            {
                Color32 c = albedo.Data[(LashY + y) * albedo.Width + LashX + x];
                if (x >= LowerLashMin && x < LowerLashMax) c.a = (byte)Mathf.RoundToInt(c.a * LowerLashOpacity);
                crop.Data[y * LashSize + x] = c;
            }
        return crop;
    }

    // Grey version of a garment albedo: luminance relative to the garment's mean (texels under 3 % are the gaps between
    // UV islands), with its contrast compressed by 'contrast' and clamped to [low, high], scaled to 'target'.
    private static Pixels Neutral(Pixels source, float target, float contrast, float low, float high)
    {
        double sum = 0;
        int count = 0;
        foreach (Color32 c in source.Data)
        {
            float l = Luminance(c);
            if (l <= 0.03f) continue;
            sum += l;
            count++;
        }
        float mean = count > 0 ? (float)(sum / count) : 0.5f;
        var result = new Pixels { Width = source.Width, Height = source.Height, Data = new Color32[source.Data.Length] };
        for (int i = 0; i < source.Data.Length; i++)
        {
            float ratio = Mathf.Max(Luminance(source.Data[i]), 1e-4f) / mean;
            byte v = ToByte(target * Mathf.Clamp(Mathf.Pow(ratio, contrast), low, high));
            result.Data[i] = new Color32(v, v, v, source.Data[i].a);
        }
        return result;
    }

    private static float Luminance(Color32 c) => (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f;

    private static Pixels Downsample(Pixels source, int size)
    {
        if (source.Width <= size) return source;
        int factor = source.Width / size;
        var result = new Pixels { Width = size, Height = source.Height / factor, Data = new Color32[size * (source.Height / factor)] };
        float norm = 1f / (factor * factor);
        for (int y = 0; y < result.Height; y++)
            for (int x = 0; x < result.Width; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (int dy = 0; dy < factor; dy++)
                    for (int dx = 0; dx < factor; dx++)
                    {
                        Color32 c = source.Data[(y * factor + dy) * source.Width + x * factor + dx];
                        r += c.r; g += c.g; b += c.b; a += c.a;
                    }
                result.Data[y * result.Width + x] = new Color32((byte)(r * norm + 0.5f), (byte)(g * norm + 0.5f), (byte)(b * norm + 0.5f), (byte)(a * norm + 0.5f));
            }
        return result;
    }

    private static byte ToByte(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);

    private static void WritePng(string path, Pixels pixels) => File.WriteAllBytes(path, Encode(pixels, true));

    private static void WriteJpg(string path, Pixels pixels) => File.WriteAllBytes(path, Encode(pixels, false));

    private static byte[] Encode(Pixels pixels, bool png)
    {
        var texture = new Texture2D(pixels.Width, pixels.Height, png ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
        try
        {
            texture.SetPixels32(pixels.Data);
            texture.Apply(false);
            return png ? texture.EncodeToPNG() : texture.EncodeToJPG(95);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void ConfigureTexture(TextureSpec spec)
    {
        string path = TextureFolder + "/" + spec.Name;
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogWarning("People upgrade: texture " + path + " is missing.");
            return;
        }
        bool changed = false;
        TextureImporterType type = spec.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (importer.textureType != type) { importer.textureType = type; changed = true; }
        // Albedo and the specular colour (RGB of the SpecGloss maps) are colour data; the alpha channel is always linear.
        if (!spec.Normal && !importer.sRGBTexture) { importer.sRGBTexture = true; changed = true; }
        TextureImporterAlphaSource alpha = spec.Alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        if (importer.alphaSource != alpha) { importer.alphaSource = alpha; changed = true; }
        bool alphaTested = spec.AlphaTest > 0f;
        if (importer.alphaIsTransparency != alphaTested) { importer.alphaIsTransparency = alphaTested; changed = true; }
        if (!importer.mipmapEnabled) { importer.mipmapEnabled = true; changed = true; }
        if (importer.mipMapsPreserveCoverage != alphaTested) { importer.mipMapsPreserveCoverage = alphaTested; changed = true; }
        if (alphaTested && !Mathf.Approximately(importer.alphaTestReferenceValue, spec.AlphaTest)) { importer.alphaTestReferenceValue = spec.AlphaTest; changed = true; }
        TextureWrapMode wrap = spec.Clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        if (importer.wrapMode != wrap) { importer.wrapMode = wrap; changed = true; }
        if (importer.maxTextureSize != spec.Standalone) { importer.maxTextureSize = spec.Standalone; changed = true; }
        TextureImporterCompression compression = spec.HighQuality ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Compressed;
        if (importer.textureCompression != compression) { importer.textureCompression = compression; changed = true; }
        changed |= PlatformOverride(importer, "Standalone", spec.Standalone, compression);
        changed |= PlatformOverride(importer, "Android", spec.Android, TextureImporterCompression.Compressed);
        if (changed) importer.SaveAndReimport();
    }

    private static bool PlatformOverride(TextureImporter importer, string platform, int size, TextureImporterCompression compression)
    {
        TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
        if (settings.overridden && settings.maxTextureSize == size && settings.format == TextureImporterFormat.Automatic
            && settings.textureCompression == compression) return false;
        settings.overridden = true;
        settings.maxTextureSize = size;
        settings.format = TextureImporterFormat.Automatic;
        settings.textureCompression = compression;
        importer.SetPlatformTextureSettings(settings);
        return true;
    }

    private static Texture2D Tex(string name)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "/" + name);
        if (texture == null) Debug.LogWarning("People upgrade: texture " + name + " is missing.");
        return texture;
    }

    // ------------------------------------------------------------------ materials

    private sealed class LitSpec
    {
        public Texture BaseMap, Normal, SpecGloss;
        public Color BaseColor = Color.white;
        public Vector2 Tiling = Vector2.one, Offset = Vector2.zero;
        public Color SpecColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        public float Smoothness = 1f, Cutoff = 0.5f;
        public bool AlphaClip, DoubleSided;
    }

    /// <summary>Renderer name in Remy.fbx (and its instances) to the Remy material made for it.</summary>
    private static Dictionary<string, Material> BuildRemyMaterials()
    {
        Texture2D bodyAlbedo = Tex("Remy_Body_Albedo.jpg"), bodyNormal = Tex("Remy_Body_NormalMap.png");
        var parts = new Dictionary<string, Material>
        {
            // Skin: Mixamo albedo, normal and remapped spec/gloss (F0 about 0.028, smoothness about 0.41).
            ["Body"] = MakeLit("People_Remy_Skin", new LitSpec { BaseMap = bodyAlbedo, Normal = bodyNormal, SpecGloss = Tex("Remy_Body_SpecGloss.png") }),
            // Eyes: the iris and sclera from the atlas, slightly darkened for the lid's occlusion; a wet cornea (F0 about 0.03,
            // smoothness 0.9) without emission, so the eyes no longer glow.
            ["Eyes"] = MakeLit("People_Remy_Eyes", new LitSpec
            {
                BaseMap = bodyAlbedo, Normal = bodyNormal, BaseColor = new Color(0.86f, 0.86f, 0.86f, 1f),
                SpecColor = new Color(0.2f, 0.2f, 0.2f, 1f), Smoothness = 0.9f
            }),
            ["Eyelashes"] = MakeLit("People_Remy_Eyelashes", new LitSpec
            {
                BaseMap = Tex("Remy_Eyelashes_Albedo.png"), Tiling = LashTiling, Offset = LashOffset, AlphaClip = true, Cutoff = EyelashCutoff,
                DoubleSided = true, SpecColor = new Color(0.18f, 0.18f, 0.18f, 1f), Smoothness = 0.35f
            }),
            ["Hair"] = MakeLit("People_Remy_Hair", Hair(Tex("Remy_Hair_Albedo.png"), Color.white)),
            ["Tops"] = MakeLit("People_Remy_Top", Garment("Top", Tex("Remy_Top_Albedo.jpg"), Color.white)),
            ["Bottoms"] = MakeLit("People_Remy_Bottom", Garment("Bottom", Tex("Remy_Bottom_Albedo.jpg"), Color.white)),
            ["Shoes"] = MakeLit("People_Remy_Shoes", Garment("Shoes", Tex("Remy_Shoes_Albedo.jpg"), Color.white)),
        };
        return parts;
    }

    private static LitSpec Garment(string part, Texture albedo, Color tint) => new LitSpec
    {
        BaseMap = albedo, BaseColor = tint, Normal = Tex("Remy_" + part + "_NormalMap.png"), SpecGloss = Tex("Remy_" + part + "_SpecGloss.png")
    };

    private static LitSpec Hair(Texture albedo, Color tint) => new LitSpec
    {
        BaseMap = albedo, BaseColor = tint, Normal = Tex("Remy_Hair_NormalMap.png"), SpecGloss = Tex("Remy_Hair_SpecGloss.png"),
        AlphaClip = true, Cutoff = HairCutoff, DoubleSided = true
    };

    // Crowd palettes (sRGB base colours). Garments and hair multiply a neutral grey texture, so the base colour is about the
    // garment's colour divided by the texture's mean (0.8 tops, 0.78 bottoms, 0.72 hair); skin and shoes tint Remy's own maps.
    // The names are the ones ExteriorEnvironmentBuilder's PedestrianLooks and CyclistLooks refer to.
    private static readonly (string Name, Color Tint)[] CrowdTops =
    {
        ("White", new Color(1f, 1f, 0.98f)), ("HeatherGrey", new Color(0.69f, 0.69f, 0.7f)), ("Charcoal", new Color(0.2f, 0.2f, 0.21f)),
        ("Navy", new Color(0.19f, 0.25f, 0.43f)), ("Burgundy", new Color(0.45f, 0.11f, 0.15f)), ("Olive", new Color(0.41f, 0.44f, 0.28f)),
        ("SkyBlue", new Color(0.6f, 0.78f, 0.98f)), ("Mustard", new Color(0.9f, 0.69f, 0.23f)),
        ("SteelBlue", new Color(0.28f, 0.43f, 0.6f)), ("Rust", new Color(0.71f, 0.29f, 0.16f)),
    };
    private static readonly (string Name, Color Tint)[] CrowdBottoms =
    {
        ("Denim", new Color(0.32f, 0.42f, 0.62f)), ("DarkDenim", new Color(0.18f, 0.22f, 0.33f)), ("Black", new Color(0.14f, 0.14f, 0.15f)),
        ("Khaki", new Color(0.85f, 0.74f, 0.56f)), ("Grey", new Color(0.54f, 0.54f, 0.55f)),
    };
    private static readonly (string Name, Color Tint)[] CrowdShoes =
    {
        ("Classic", Color.white), ("Dark", new Color(0.32f, 0.31f, 0.3f)), ("Tan", new Color(0.78f, 0.62f, 0.45f)),
    };
    private static readonly (string Name, Color Tint)[] CrowdHair =
    {
        ("Black", new Color(0.125f, 0.11f, 0.104f)), ("DarkBrown", new Color(0.31f, 0.21f, 0.14f)), ("Chestnut", new Color(0.62f, 0.42f, 0.28f)),
        ("Auburn", new Color(0.62f, 0.28f, 0.14f)), ("Blond", new Color(0.97f, 0.78f, 0.5f)), ("Ash", new Color(0.58f, 0.5f, 0.42f)),
    };
    private static readonly (string Name, Color Tint)[] CrowdSkin =
    {
        ("Light", Color.white), ("Medium", new Color(0.88f, 0.76f, 0.66f)), ("Tan", new Color(0.7f, 0.53f, 0.42f)), ("Deep", new Color(0.47f, 0.33f, 0.26f)),
    };

    private static int BuildCrowdMaterials()
    {
        int count = 0;
        Texture2D top = Tex("Crowd_Top_Neutral.jpg"), bottom = Tex("Crowd_Bottom_Neutral.jpg"), hair = Tex("Crowd_Hair_Neutral.png");
        Texture2D shoes = Tex("Remy_Shoes_Albedo.jpg"), skin = Tex("Remy_Body_Albedo.jpg");
        foreach (var (name, tint) in CrowdTops) { MakeLit("Crowd/Crowd_Top_" + name, Garment("Top", top, tint)); count++; }
        foreach (var (name, tint) in CrowdBottoms) { MakeLit("Crowd/Crowd_Bottoms_" + name, Garment("Bottom", bottom, tint)); count++; }
        foreach (var (name, tint) in CrowdShoes) { MakeLit("Crowd/Crowd_Shoes_" + name, Garment("Shoes", shoes, tint)); count++; }
        foreach (var (name, tint) in CrowdHair) { MakeLit("Crowd/Crowd_Hair_" + name, Hair(hair, tint)); count++; }
        foreach (var (name, tint) in CrowdSkin)
        {
            MakeLit("Crowd/Crowd_Skin_" + name, new LitSpec
            {
                BaseMap = skin, BaseColor = tint, Normal = Tex("Remy_Body_NormalMap.png"), SpecGloss = Tex("Remy_Body_SpecGloss.png")
            });
            count++;
        }
        return count;
    }

    private static Material MakeLit(string name, LitSpec spec)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) throw new InvalidOperationException("People upgrade: shader Universal Render Pipeline/Lit is missing.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool create = material == null;
        if (create) material = new Material(lit) { name = Path.GetFileNameWithoutExtension(path) };
        else if (material.shader != lit) material.shader = lit;

        material.SetFloat("_WorkflowMode", 0f); // Specular: Mixamo authored specular colour and gloss maps
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_AlphaClip", spec.AlphaClip ? 1f : 0f);
        material.SetFloat("_Cutoff", spec.Cutoff);
        material.SetFloat("_Cull", spec.DoubleSided ? (float)CullMode.Off : (float)CullMode.Back);
        material.SetTexture("_BaseMap", spec.BaseMap);
        material.SetTextureScale("_BaseMap", spec.Tiling);
        material.SetTextureOffset("_BaseMap", spec.Offset);
        material.SetColor("_BaseColor", spec.BaseColor);
        material.SetTexture("_BumpMap", spec.Normal);
        material.SetFloat("_BumpScale", 1f);
        material.SetTexture("_SpecGlossMap", spec.SpecGloss);
        material.SetColor("_SpecColor", spec.SpecColor);
        material.SetFloat("_Smoothness", spec.Smoothness);
        material.SetFloat("_SmoothnessTextureChannel", 0f);
        material.SetTexture("_MetallicGlossMap", null);
        material.SetFloat("_Metallic", 0f);
        material.SetTexture("_OcclusionMap", null);
        material.SetTexture("_ParallaxMap", null);
        material.SetTexture("_EmissionMap", null);
        material.SetColor("_EmissionColor", Color.black);
        material.SetFloat("_SpecularHighlights", 1f);
        material.SetFloat("_EnvironmentReflections", 1f);
        material.SetFloat("_ReceiveShadows", 1f);
        material.SetFloat("_QueueOffset", 0f);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        // URP's own Lit inspector setup: workflow, map and alpha-test keywords, alpha-to-coverage, render queue and passes.
        BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords);

        if (create) AssetDatabase.CreateAsset(material, path);
        else EditorUtility.SetDirty(material);
        return material;
    }

    // ------------------------------------------------------------------ model and scene

    // Remy.fbx's own materials (Bodymat, Topmat, ...) are remapped to the Remy materials, so any new instance is realistic
    // by default. The Eyes mesh shares the body's source material; scenes give it People_Remy_Eyes.
    private static bool RemapModelMaterials(Dictionary<string, Material> remy)
    {
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (importer == null || model == null) return false;
        Dictionary<AssetImporter.SourceAssetIdentifier, UnityEngine.Object> map = importer.GetExternalObjectMap();
        // The rig's humanoid mapping is left unspecified in the .meta and generated on every import. Saving the importer
        // would write the generated mapping into the .meta, so it is cleared again below: only the material remap changes.
        bool generatedAvatar = File.ReadAllText(Absolute(ModelPath + ".meta")).Contains("    human: []");
        bool changed = false;
        foreach (SkinnedMeshRenderer part in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (part.name == "Eyes" || !remy.TryGetValue(part.name, out Material target)) continue;
            Material current = part.sharedMaterial;
            string source = null;
            foreach (var entry in map)
                if (entry.Key.type == typeof(Material) && entry.Value == current) source = entry.Key.name;
            if (source == null && current != null && AssetDatabase.GetAssetPath(current) == ModelPath) source = current.name;
            if (source == null) continue;
            var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), source);
            if (map.TryGetValue(id, out UnityEngine.Object mapped) && mapped == target) continue;
            importer.AddRemap(id, target);
            changed = true;
        }
        if (!changed) return false;
        if (generatedAvatar)
        {
            HumanDescription human = importer.humanDescription;
            human.human = Array.Empty<HumanBone>();
            human.skeleton = Array.Empty<SkeletonBone>();
            importer.humanDescription = human;
        }
        importer.SaveAndReimport();
        return true;
    }

    private static int DressPresenters(Dictionary<string, Material> remy)
    {
        int changed = 0;
        foreach (GameObject root in LoadedSceneRoots())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != PresenterName) continue;
                foreach (SkinnedMeshRenderer part in t.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!remy.TryGetValue(part.name, out Material material) || material == null) continue;
                    ShadowCastingMode shadows = part.name == "Eyes" || part.name == "Eyelashes" ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    if (part.sharedMaterial == material && part.shadowCastingMode == shadows) continue;
                    Undo.RecordObject(part, "Upgrade People");
                    part.sharedMaterial = material;
                    part.shadowCastingMode = shadows;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(part);
                    EditorSceneManager.MarkSceneDirty(part.gameObject.scene);
                    changed++;
                }
            }
        return changed;
    }

    private static int DressCrowds()
    {
        int changed = 0;
        foreach (GameObject root in LoadedSceneRoots())
            foreach (ExteriorEnvironmentBuilder builder in root.GetComponentsInChildren<ExteriorEnvironmentBuilder>(true))
            {
                int parts = ExteriorEnvironmentBuilder.ApplyCrowdLooks(builder.transform);
                if (parts > 0) EditorSceneManager.MarkSceneDirty(builder.gameObject.scene);
                changed += parts;
            }
        return changed;
    }

    private static IEnumerable<GameObject> LoadedSceneRoots()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            foreach (GameObject root in scene.GetRootGameObjects()) yield return root;
        }
    }

    // ------------------------------------------------------------------ helpers

    private static string Absolute(string assetPath) => Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
#endif
