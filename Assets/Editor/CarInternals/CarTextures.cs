#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CarInternals
{
    /// <summary>
    /// The internals' surface maps as assets: CarTextureSynth's output saved as PNG in Folder (Git LFS, like the other
    /// generated realism textures) and imported tileable: mipmaps, trilinear and 4x anisotropic filtering, repeat
    /// wrapping, 512 px with high-quality compression on PC and 256 px ASTC 6x6 on Android. Per surface a base map
    /// (colour factor in RGB, sRGB; smoothness factor in A) and a normal map. A file is written only when its bytes
    /// change and an importer saved only when a setting differs, so a rebuild leaves files, GUIDs and metas untouched.
    /// </summary>
    public sealed class CarTextures
    {
        public const string Folder = "Assets/Materials/Realism/CarInternals";
        private const int AndroidSize = 256;

        public sealed class Surface
        {
            public Texture2D Base, Normal;
            /// <summary>Mean linear luminance of the base map's colour factor.</summary>
            public float MeanColour;
            /// <summary>Mean of the base map's alpha (smoothness or opacity factor).</summary>
            public float MeanAlpha;
        }

        private readonly Dictionary<string, Surface> cache = new Dictionary<string, Surface>();

        /// <summary>Texture files written (new or changed) by this instance; 0 when a rebuild changed nothing.</summary>
        public int Written { get; private set; }

        public int Count => cache.Count;

        /// <summary>The maps of a CarTextureSynth surface, generated, saved and imported on first use.</summary>
        public Surface Get(string surface)
        {
            if (cache.TryGetValue(surface, out Surface s)) return s;
            InternalsContext.EnsureFolder(Folder);
            CarTextureSynth.Result r = CarTextureSynth.Generate(surface);
            string basePath = Folder + "/CI_" + surface + "_Base.png";
            string normalPath = Folder + "/CI_" + surface + "_Normal.png";
            Save(basePath, Encode(r.BaseRgba, r.Size, true));
            Save(normalPath, Encode(r.NormalRgba, r.Size, false));
            Configure(basePath, false);
            Configure(normalPath, true);
            s = new Surface
            {
                Base = AssetDatabase.LoadAssetAtPath<Texture2D>(basePath),
                Normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath),
                MeanColour = r.MeanColour,
                MeanAlpha = r.MeanAlpha
            };
            if (s.Base == null || s.Normal == null) Debug.LogWarning("[Car Internals] surface maps for " + surface + " did not import");
            cache[surface] = s;
            return s;
        }

        /// <summary>Deletes the maps in Folder of surfaces not in the set (files this class names CI_&lt;surface&gt;_Base/_Normal.png).</summary>
        public void RemoveUnused(ICollection<string> surfaces)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) return;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = Path.GetFileNameWithoutExtension(path);
                if (!file.StartsWith("CI_")) continue;
                int cut = file.LastIndexOf('_');
                if (cut <= 3) continue;
                string surface = file.Substring(3, cut - 3), kind = file.Substring(cut + 1);
                if ((kind == "Base" || kind == "Normal") && !surfaces.Contains(surface)) AssetDatabase.DeleteAsset(path);
            }
        }

        /// <summary>PNG bytes of an RGBA32 buffer (rows bottom to top); RGB only when alpha is not used.</summary>
        private static byte[] Encode(byte[] rgba, int size, bool keepAlpha)
        {
            Texture2D tex;
            if (keepAlpha)
            {
                tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
                tex.SetPixelData(rgba, 0);
            }
            else
            {
                var rgb = new byte[size * size * 3];
                for (int i = 0, j = 0; i < rgba.Length; i += 4, j += 3)
                {
                    rgb[j] = rgba[i]; rgb[j + 1] = rgba[i + 1]; rgb[j + 2] = rgba[i + 2];
                }
                tex = new Texture2D(size, size, TextureFormat.RGB24, false, true);
                tex.SetPixelData(rgb, 0);
            }
            tex.Apply(false, false);
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            return png;
        }

        private void Save(string assetPath, byte[] png)
        {
            string full = Path.GetFullPath(assetPath);
            if (File.Exists(full))
            {
                byte[] old = File.ReadAllBytes(full);
                if (Same(old, png)) return;
            }
            File.WriteAllBytes(full, png);
            Written++;
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        private static void Configure(string path, bool normal)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null)
            {
                Debug.LogWarning("[Car Internals] no texture importer for " + path);
                return;
            }
            bool changed = false;
            void Set<T>(T current, T wanted, System.Action<T> apply)
            {
                if (EqualityComparer<T>.Default.Equals(current, wanted)) return;
                apply(wanted);
                changed = true;
            }
            Set(ti.textureType, normal ? TextureImporterType.NormalMap : TextureImporterType.Default, v => ti.textureType = v);
            Set(ti.textureShape, TextureImporterShape.Texture2D, v => ti.textureShape = v);
            Set(ti.sRGBTexture, !normal, v => ti.sRGBTexture = v);
            Set(ti.alphaSource, normal ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput, v => ti.alphaSource = v);
            Set(ti.alphaIsTransparency, false, v => ti.alphaIsTransparency = v);
            Set(ti.convertToNormalmap, false, v => ti.convertToNormalmap = v);
            Set(ti.mipmapEnabled, true, v => ti.mipmapEnabled = v);
            Set(ti.mipmapFilter, TextureImporterMipFilter.KaiserFilter, v => ti.mipmapFilter = v);
            Set(ti.wrapMode, TextureWrapMode.Repeat, v => ti.wrapMode = v);
            Set(ti.filterMode, FilterMode.Trilinear, v => ti.filterMode = v);
            Set(ti.anisoLevel, 4, v => ti.anisoLevel = v);
            Set(ti.isReadable, false, v => ti.isReadable = v);
            Set(ti.npotScale, TextureImporterNPOTScale.None, v => ti.npotScale = v);
            Set(ti.maxTextureSize, CarTextureSynth.Size, v => ti.maxTextureSize = v);
            Set(ti.textureCompression, TextureImporterCompression.CompressedHQ, v => ti.textureCompression = v);
            TextureImporterPlatformSettings android = ti.GetPlatformTextureSettings("Android");
            if (!android.overridden || android.maxTextureSize != AndroidSize || android.format != TextureImporterFormat.ASTC_6x6)
            {
                android.overridden = true;
                android.maxTextureSize = AndroidSize;
                android.format = TextureImporterFormat.ASTC_6x6;
                ti.SetPlatformTextureSettings(android);
                changed = true;
            }
            if (changed) ti.SaveAndReimport();
        }
    }
}
#endif
