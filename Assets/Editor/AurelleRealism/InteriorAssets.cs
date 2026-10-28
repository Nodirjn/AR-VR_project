#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;

namespace AurelleRealism
{
    internal enum InteriorTexKind { Color, Normal, Mask, Emissive }

    /// <summary>Import settings for the CC0 textures and models used by the interior pass.</summary>
    internal static class InteriorAssets
    {
        // (path relative to Assets/ThirdParty, kind, PC max size, Android max size)
        private static readonly (string path, InteriorTexKind kind, int pc, int android)[] Textures =
        {
            ("PolyHaven/granite_tile/granite_tile_diff_2k.jpg", InteriorTexKind.Color, 2048, 1024),
            ("PolyHaven/granite_tile/granite_tile_nor_gl_2k.jpg", InteriorTexKind.Normal, 2048, 1024),
            ("PolyHaven/granite_tile/granite_tile_mask_1k.png", InteriorTexKind.Mask, 1024, 512),
            ("PolyHaven/concrete_wall_001/concrete_wall_001_diff_2k.jpg", InteriorTexKind.Color, 2048, 1024),
            ("PolyHaven/concrete_wall_001/concrete_wall_001_nor_gl_1k.jpg", InteriorTexKind.Normal, 1024, 1024),
            ("PolyHaven/concrete_wall_001/concrete_wall_001_mask_1k.png", InteriorTexKind.Mask, 1024, 512),
            ("PolyHaven/white_stucco/white_stucco_diff_1k.jpg", InteriorTexKind.Color, 1024, 512),
            ("PolyHaven/white_stucco/white_stucco_nor_gl_1k.jpg", InteriorTexKind.Normal, 1024, 512),
            ("PolyHaven/white_stucco/white_stucco_mask_1k.png", InteriorTexKind.Mask, 1024, 512),
            ("PolyHaven/synthetic_wood/synthetic_wood_diff_1k.jpg", InteriorTexKind.Color, 1024, 1024),
            ("PolyHaven/synthetic_wood/synthetic_wood_nor_gl_1k.jpg", InteriorTexKind.Normal, 1024, 1024),
            ("PolyHaven/synthetic_wood/synthetic_wood_mask_1k.png", InteriorTexKind.Mask, 1024, 512),
            ("ambientCG/Marble016/Marble016_2K_Color.jpg", InteriorTexKind.Color, 2048, 1024),
            ("ambientCG/Marble016/Marble016_1K_NormalGL.jpg", InteriorTexKind.Normal, 1024, 512),
            ("ambientCG/Marble016/Marble016_1K_Mask.png", InteriorTexKind.Mask, 1024, 512),
            ("ambientCG/Marble012/Marble012_1K_Color.jpg", InteriorTexKind.Color, 1024, 1024),
            ("ambientCG/Marble012/Marble012_1K_NormalGL.jpg", InteriorTexKind.Normal, 1024, 512),
            ("ambientCG/Marble012/Marble012_1K_Mask.png", InteriorTexKind.Mask, 1024, 512),
            ("ambientCG/Wood066/Wood066_1K_Color.jpg", InteriorTexKind.Color, 1024, 1024),
            ("ambientCG/Wood066/Wood066_1K_NormalGL.jpg", InteriorTexKind.Normal, 1024, 512),
            ("ambientCG/Wood066/Wood066_1K_Mask.png", InteriorTexKind.Mask, 1024, 512),
            ("ambientCG/Metal009/Metal009_1K_Color.jpg", InteriorTexKind.Color, 1024, 512),
            ("ambientCG/Metal009/Metal009_1K_NormalGL.jpg", InteriorTexKind.Normal, 1024, 512),
            ("ambientCG/Metal009/Metal009_1K_Mask.png", InteriorTexKind.Mask, 1024, 512),
            ("ambientCG/Carpet016/Carpet016_1K_Color.jpg", InteriorTexKind.Color, 1024, 512),
            ("ambientCG/Carpet016/Carpet016_1K_NormalGL.jpg", InteriorTexKind.Normal, 1024, 512),
            ("ambientCG/Carpet016/Carpet016_1K_Mask.png", InteriorTexKind.Mask, 1024, 512),
            ("ambientCG/Fabric030/Fabric030_1K_Color.jpg", InteriorTexKind.Color, 1024, 512),
            ("ambientCG/Fabric030/Fabric030_1K_NormalGL.jpg", InteriorTexKind.Normal, 1024, 512),
            ("ambientCG/Fabric030/Fabric030_1K_Mask.png", InteriorTexKind.Mask, 1024, 512),
        };

        // Poly Haven models: textures next to the .gltf follow <stem>_diff_1k.jpg / _nor_gl_1k.jpg / _mask_1k.png.
        internal static readonly (string asset, string[] stems, int android)[] Models =
        {
            ("modern_arm_chair_01", new[] { "modern_arm_chair_01_legs" }, 1024),
            ("coffee_table_round_01", new[] { "coffee_table_round_01" }, 1024),
            ("side_table_01", new[] { "side_table_01" }, 512),
            ("pachira_aquatica_01", new[] { "pachira_aquatica_01_bark", "pachira_aquatica_01_leaves" }, 1024),
            ("modern_ceiling_lamp_01", new[] { "modern_ceiling_lamp_01" }, 512),
            ("ceramic_vase_01", new[] { "ceramic_vase_01" }, 512),
            ("ceramic_vase_03", new[] { "ceramic_vase_03" }, 512),
            ("potted_plant_04", new[] { "potted_plant_04" }, 512),
        };

        public static string ThirdParty(string relative) => "Assets/ThirdParty/" + relative;

        public static string ModelPath(string asset) => InteriorRealismUpgrade.PolyHaven + "/" + asset + "/" + asset + ".gltf";

        public static void PrepareImports()
        {
            foreach (var t in Textures) ConfigureTexture(ThirdParty(t.path), t.kind, t.pc, t.android);
            foreach (var m in Models)
            {
                foreach (string stem in m.stems)
                {
                    string dir = InteriorRealismUpgrade.PolyHaven + "/" + m.asset + "/";
                    ConfigureTexture(dir + stem + "_diff_1k.jpg", InteriorTexKind.Color, 1024, m.android);
                    ConfigureTexture(dir + stem + "_nor_gl_1k.jpg", InteriorTexKind.Normal, 1024, Mathf.Min(512, m.android));
                    ConfigureTexture(dir + stem + "_mask_1k.png", InteriorTexKind.Mask, 1024, 512);
                }
                ConfigureModel(ModelPath(m.asset));
            }
            // normal map of the cleaned armchair leather (its worn colour and roughness maps are not used)
            ConfigureTexture(InteriorRealismUpgrade.PolyHaven + "/modern_arm_chair_01/modern_arm_chair_01_pillow_nor_gl_1k.jpg", InteriorTexKind.Normal, 1024, 512);
        }

        public static void ConfigureTexture(string path, InteriorTexKind kind, int pcMax, int androidMax)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null)
            {
                InteriorRealismUpgrade.Log("missing texture " + path);
                return;
            }
            bool changed = false;
            void Set<T>(T current, T wanted, Action<T> apply)
            {
                if (EqualityComparer<T>.Default.Equals(current, wanted)) return;
                apply(wanted);
                changed = true;
            }
            Set(ti.textureType, kind == InteriorTexKind.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default, v => ti.textureType = v);
            Set(ti.sRGBTexture, kind == InteriorTexKind.Color || kind == InteriorTexKind.Emissive, v => ti.sRGBTexture = v);
            Set(ti.alphaSource, kind == InteriorTexKind.Mask ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None, v => ti.alphaSource = v);
            Set(ti.alphaIsTransparency, false, v => ti.alphaIsTransparency = v);
            Set(ti.mipmapEnabled, true, v => ti.mipmapEnabled = v);
            Set(ti.wrapMode, TextureWrapMode.Repeat, v => ti.wrapMode = v);
            Set(ti.filterMode, FilterMode.Trilinear, v => ti.filterMode = v);
            Set(ti.anisoLevel, kind == InteriorTexKind.Mask ? 2 : 8, v => ti.anisoLevel = v);
            Set(ti.maxTextureSize, pcMax, v => ti.maxTextureSize = v);
            Set(ti.textureCompression, TextureImporterCompression.CompressedHQ, v => ti.textureCompression = v);
            // PC: high-quality block compression (BC7/BC5); Quest: normal-quality ASTC (6x6), 1K at most.
            foreach (var (platform, max, quality) in new[] { ("Standalone", pcMax, TextureImporterCompression.CompressedHQ), ("Android", androidMax, TextureImporterCompression.Compressed) })
            {
                TextureImporterPlatformSettings s = ti.GetPlatformTextureSettings(platform);
                if (!s.overridden || s.maxTextureSize != max || s.format != TextureImporterFormat.Automatic || s.textureCompression != quality)
                {
                    s.overridden = true;
                    s.maxTextureSize = max;
                    s.format = TextureImporterFormat.Automatic;
                    s.textureCompression = quality;
                    ti.SetPlatformTextureSettings(s);
                    changed = true;
                }
            }
            if (changed) ti.SaveAndReimport();
        }

        private static void ConfigureModel(string path)
        {
            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (importer == null)
            {
                InteriorRealismUpgrade.Log("missing model " + path);
                return;
            }
            var so = new SerializedObject(importer);
            SerializedProperty uv2 = so.FindProperty("editorImportSettings.generateSecondaryUVSet");
            SerializedProperty aniso = so.FindProperty("importSettings.anisotropicFilterLevel");
            bool changed = false;
            if (uv2 != null && !uv2.boolValue) { uv2.boolValue = true; changed = true; }
            if (aniso != null && aniso.intValue != 4) { aniso.intValue = 4; changed = true; }
            if (!changed) return;
            so.ApplyModifiedPropertiesWithoutUndo();
            importer.SaveAndReimport();
        }

        public static Texture2D Tex(string path)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t == null) InteriorRealismUpgrade.Log("texture not found " + path);
            return t;
        }
    }

    /// <summary>Surface definition for a URP Lit material.</summary>
    internal sealed class InteriorLitDef
    {
        public Color BaseColor = Color.white;
        public string BaseMap, NormalMap, MaskMap, EmissionMap;
        public float NormalScale = 1f, Smoothness = 0.5f, Metallic, Occlusion = 1f;
        /// <summary>Texture size in metres for meshes with metre UVs; (1,1) tiling for model UVs.</summary>
        public Vector2 Tiling = Vector2.one;
        public Vector2 Offset = Vector2.zero;
        public Color Emission = Color.black;
        public bool BakedEmission;
        public bool Transparent;
        /// <summary>Transparent only: reflections and highlights keep full strength whatever the alpha (glass). Off for
        /// films and decals, whose clear parts must not reflect.</summary>
        public bool PreserveSpecular = true;
        /// <summary>Specular workflow with this F0 instead of the metallic workflow (dielectrics brighter than 4 %).</summary>
        public Color? Specular;
        public bool DoubleSided;
        public bool SpecularHighlights = true;
        public bool EnvironmentReflections = true;
        public bool ReceiveShadows = true;
    }

    /// <summary>Creates or updates the interior materials under Assets/Materials/Realism/Interior.</summary>
    internal sealed class InteriorMaterialLibrary
    {
        private readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        private static Shader lit;

        public Material Get(string name, InteriorLitDef def)
        {
            if (cache.TryGetValue(name, out Material cached)) return cached;
            if (lit == null) lit = Shader.Find("Universal Render Pipeline/Lit");
            string path = InteriorRealismUpgrade.MaterialFolder + "/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(lit) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != lit)
            {
                m.shader = lit;
            }
            Apply(m, def);
            EditorUtility.SetDirty(m);
            cache[name] = m;
            return m;
        }

        private static void Apply(Material m, InteriorLitDef d)
        {
            m.SetFloat("_WorkflowMode", d.Specular.HasValue ? 0f : 1f);
            if (d.Specular.HasValue)
            {
                m.SetColor("_SpecColor", d.Specular.Value);
                m.SetTexture("_SpecGlossMap", null);
            }
            m.SetColor("_BaseColor", d.BaseColor);
            m.SetTexture("_BaseMap", d.BaseMap != null ? InteriorAssets.Tex(d.BaseMap) : null);
            m.SetTextureScale("_BaseMap", d.Tiling);
            m.SetTextureOffset("_BaseMap", d.Offset);
            m.SetTexture("_BumpMap", d.NormalMap != null ? InteriorAssets.Tex(d.NormalMap) : null);
            m.SetFloat("_BumpScale", d.NormalScale);
            Texture2D mask = d.MaskMap != null ? InteriorAssets.Tex(d.MaskMap) : null;
            m.SetTexture("_MetallicGlossMap", mask);
            m.SetTexture("_OcclusionMap", mask);
            m.SetFloat("_OcclusionStrength", mask != null ? d.Occlusion : 1f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.SetFloat("_Smoothness", d.Smoothness);
            m.SetFloat("_Metallic", d.Metallic);
            m.SetFloat("_SpecularHighlights", d.SpecularHighlights ? 1f : 0f);
            m.SetFloat("_EnvironmentReflections", d.EnvironmentReflections ? 1f : 0f);
            m.SetFloat("_ReceiveShadows", d.ReceiveShadows ? 1f : 0f);
            m.SetFloat("_Cull", d.DoubleSided ? 0f : 2f);
            m.SetFloat("_AlphaClip", 0f);
            m.SetFloat("_Surface", d.Transparent ? 1f : 0f);
            m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_BlendModePreserveSpecular")) m.SetFloat("_BlendModePreserveSpecular", d.PreserveSpecular ? 1f : 0f);
            bool emissive = d.Emission.maxColorComponent > 0f;
            m.SetTexture("_EmissionMap", d.EmissionMap != null ? InteriorAssets.Tex(d.EmissionMap) : null);
            m.SetColor("_EmissionColor", emissive ? d.Emission : Color.black);
            m.globalIlluminationFlags = emissive
                ? (d.BakedEmission ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.RealtimeEmissive)
                : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            if (m.HasProperty("_QueueControl")) m.SetFloat("_QueueControl", 0f);
            BaseShaderGUI.SetMaterialKeywords(m, LitGUI.SetMaterialKeywords);
            if (emissive) m.EnableKeyword("_EMISSION");
            m.enableInstancing = true;
        }
    }
}
#endif
