#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;

namespace CarInternals
{
    /// <summary>
    /// URP Lit materials for the internals, written to Assets/Materials/CarInternals (updated in place, so their GUIDs
    /// stay). The colours keep the engineering colour code (light zinc and machined metal, near-black paint and rubber,
    /// coloured springs, calipers and high-voltage parts) and stay distinct through the X-ray glass; each material also
    /// gets the finish its part really has (Finishes): a tileable base map whose RGB varies the colour around the
    /// material's own and whose alpha is the smoothness map (URP's Albedo Alpha source), and a normal map, from
    /// CarTextures. The mesh kit's UVs are in metres, so a finish's tile is a physical size. Metallic stays one value per
    /// material. Plain URP Lit with two maps, so the materials stay SRP Batcher compatible and cheap on the Quest.
    /// </summary>
    public sealed class CarPalette
    {
        public const string Folder = "Assets/Materials/CarInternals";
        private static Shader lit;
        private readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        private readonly CarTextures textures = new CarTextures();

        /// <summary>A CarTextureSynth surface, its tile size in metres and the normal strength.</summary>
        private readonly struct Finish
        {
            public readonly string Surface;
            public readonly float Tile, Bump;

            public Finish(string surface, float tile, float bump = 1f)
            {
                Surface = surface;
                Tile = tile;
                Bump = bump;
            }
        }

        // Finish per material name, including the materials the subsystems define through Get. Chrome stays plain.
        private static readonly Dictionary<string, Finish> Finishes = new Dictionary<string, Finish>
        {
            { "CI_Steel_ZincPlated", new Finish("Zinc", 0.12f) },
            { "CI_Steel_BlackPainted", new Finish("ECoat", 0.12f, 0.5f) },
            { "CI_Steel_Forged", new Finish("Forged", 0.2f) },
            { "CI_Steel_Machined", new Finish("Machined", 0.08f) },
            { "CI_Iron_Cast", new Finish("Cast", 0.2f) },
            { "CI_Aluminium_Machined", new Finish("Machined", 0.08f) },
            { "CI_Aluminium_Cast", new Finish("Cast", 0.2f, 0.8f) },
            { "CI_Copper", new Finish("Machined", 0.08f, 0.6f) },
            { "CI_Brass", new Finish("Machined", 0.08f, 0.6f) },
            { "CI_Aluminium_HeatShield", new Finish("HeatShield", 0.06f) },
            { "CI_Hose_Braided", new Finish("Braid", 0.032f) },
            // Red: anodised turned parts (spring perches). Yellow is mostly the powder-coated coil springs.
            { "CI_Anodised_Red", new Finish("Machined", 0.08f, 0.6f) },
            { "CI_Anodised_Yellow", new Finish("ECoat", 0.07f, 0.45f) },
            { "CI_Paint_SpringBlue", new Finish("ECoat", 0.07f, 0.45f) },
            { "CI_Plastic_OrangeHV", new Finish("Plastic", 0.08f) },
            { "CI_Rubber", new Finish("Rubber", 0.08f) },
            { "CI_Plastic_Black", new Finish("Plastic", 0.08f) },
            { "CI_Plastic_Grey", new Finish("Plastic", 0.08f) },
            { "CI_Plastic_BatteryCase", new Finish("Plastic", 0.16f) },
            { "CI_Wire_Red", new Finish("Rubber", 0.05f, 0.6f) },
            { "CI_Wire_Black", new Finish("Loom", 0.048f) },
            // The level line sits at a fixed height above the tank's centre, so this tile is part of the design.
            { "CI_Plastic_TranslucentReservoir", new Finish("Reservoir", 0.25f) },
            { "CI_Steel_BodyECoat", new Finish("ECoat", 0.15f, 0.6f) },
            { "CI_Steel_PressHardened", new Finish("Zinc", 0.2f, 1.4f) },
            { "CI_Aluminium_Extruded", new Finish("Drawn", 0.12f) },
            { "CI_Friction_Lining", new Finish("Friction", 0.1f) },
            { "CI_Aluminium_HeatExchangerFin", new Finish("Fin", 0.0512f) },
            { "CI_Lead_Alloy", new Finish("Cast", 0.1f, 0.7f) },
            { "CI_Brake_CopperNickel", new Finish("Drawn", 0.05f) },
        };

        // Metals
        public Material ZincSteel => Get("CI_Steel_ZincPlated", new Color(0.70f, 0.72f, 0.74f), 1f, 0.58f);
        public Material BlackSteel => Get("CI_Steel_BlackPainted", new Color(0.045f, 0.047f, 0.05f), 0.15f, 0.52f);
        public Material ForgedSteel => Get("CI_Steel_Forged", new Color(0.36f, 0.37f, 0.38f), 1f, 0.42f);
        public Material MachinedSteel => Get("CI_Steel_Machined", new Color(0.78f, 0.78f, 0.79f), 1f, 0.78f);
        public Material CastIron => Get("CI_Iron_Cast", new Color(0.30f, 0.30f, 0.31f), 0.85f, 0.3f);
        public Material MachinedAluminium => Get("CI_Aluminium_Machined", new Color(0.91f, 0.91f, 0.92f), 1f, 0.72f);
        public Material CastAluminium => Get("CI_Aluminium_Cast", new Color(0.72f, 0.73f, 0.74f), 1f, 0.38f);
        public Material Chrome => Get("CI_Chrome", new Color(0.95f, 0.95f, 0.96f), 1f, 0.93f);
        public Material Copper => Get("CI_Copper", new Color(0.95f, 0.64f, 0.54f), 1f, 0.6f);
        public Material Brass => Get("CI_Brass", new Color(0.91f, 0.78f, 0.42f), 1f, 0.55f);
        public Material HeatShield => Get("CI_Aluminium_HeatShield", new Color(0.82f, 0.82f, 0.8f), 1f, 0.5f);
        public Material BraidedHose => Get("CI_Hose_Braided", new Color(0.62f, 0.63f, 0.64f), 0.9f, 0.4f);

        // Coloured finishes
        public Material AnodisedRed => Get("CI_Anodised_Red", new Color(0.62f, 0.05f, 0.04f), 0.45f, 0.62f);
        public Material AnodisedYellow => Get("CI_Anodised_Yellow", new Color(0.93f, 0.68f, 0.06f), 0.35f, 0.6f);
        public Material SpringBlue => Get("CI_Paint_SpringBlue", new Color(0.06f, 0.2f, 0.55f), 0.2f, 0.6f);
        public Material OrangeHighVoltage => Get("CI_Plastic_OrangeHV", new Color(0.9f, 0.35f, 0.03f), 0f, 0.45f);

        // Non-metals
        public Material Rubber => Get("CI_Rubber", new Color(0.03f, 0.03f, 0.032f), 0f, 0.28f);
        public Material BlackPlastic => Get("CI_Plastic_Black", new Color(0.035f, 0.036f, 0.04f), 0f, 0.42f);
        public Material GreyPlastic => Get("CI_Plastic_Grey", new Color(0.24f, 0.25f, 0.26f), 0f, 0.38f);
        public Material BatteryCase => Get("CI_Plastic_BatteryCase", new Color(0.12f, 0.13f, 0.14f), 0f, 0.35f);
        public Material WireRed => Get("CI_Wire_Red", new Color(0.55f, 0.04f, 0.03f), 0f, 0.45f);
        public Material WireBlack => Get("CI_Wire_Black", new Color(0.02f, 0.02f, 0.022f), 0f, 0.45f);
        public Material Reservoir => Get("CI_Plastic_TranslucentReservoir", new Color(0.92f, 0.9f, 0.82f, 0.55f), 0f, 0.7f, true);

        public int Count => cache.Count;

        /// <summary>Texture files the finishes wrote in this build (0 when nothing changed).</summary>
        public int TexturesWritten => textures.Written;

        /// <summary>
        /// The material of that name with these values (created or updated in place). The colour, metallic and smoothness
        /// are the material's averages: a finish divides the colour by its map's mean colour and the smoothness by the
        /// map's mean smoothness factor (for the transparent reservoir: the opacity by the mean opacity factor).
        /// </summary>
        public Material Get(string name, Color baseColor, float metallic, float smoothness, bool transparent = false)
        {
            if (cache.TryGetValue(name, out Material cached)) return cached;
            if (lit == null) lit = Shader.Find("Universal Render Pipeline/Lit");
            string path = Folder + "/" + name + ".mat";
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
            Texture2D baseMap = null, normalMap = null;
            Vector2 tiling = Vector2.one;
            float bump = 1f, channel = 0f;
            if (Finishes.TryGetValue(name, out Finish finish))
            {
                CarTextures.Surface s = textures.Get(finish.Surface);
                baseMap = s.Base;
                normalMap = s.Normal;
                tiling = Vector2.one * (1f / finish.Tile);
                bump = finish.Bump;
                float k = 1f / Mathf.Max(0.2f, s.MeanColour);
                baseColor = new Color(Mathf.Min(1f, baseColor.r * k), Mathf.Min(1f, baseColor.g * k), Mathf.Min(1f, baseColor.b * k), baseColor.a);
                if (transparent)
                {
                    baseColor.a = Mathf.Min(1f, baseColor.a / Mathf.Max(0.2f, s.MeanAlpha));
                }
                else
                {
                    smoothness = Mathf.Min(1f, smoothness / Mathf.Max(0.2f, s.MeanAlpha));
                    channel = 1f;
                }
            }
            m.SetFloat("_WorkflowMode", 1f);
            m.SetColor("_BaseColor", baseColor);
            m.SetTexture("_BaseMap", baseMap);
            m.SetTextureScale("_BaseMap", tiling);
            m.SetTextureOffset("_BaseMap", Vector2.zero);
            m.SetTexture("_BumpMap", normalMap);
            m.SetFloat("_BumpScale", bump);
            m.SetTexture("_MetallicGlossMap", null);
            m.SetTexture("_OcclusionMap", null);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_SmoothnessTextureChannel", channel);
            m.SetFloat("_SpecularHighlights", 1f);
            m.SetFloat("_EnvironmentReflections", 1f);
            m.SetFloat("_ReceiveShadows", 1f);
            m.SetFloat("_Cull", transparent ? 0f : 2f);
            m.SetFloat("_AlphaClip", 0f);
            m.SetFloat("_Surface", transparent ? 1f : 0f);
            m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_BlendModePreserveSpecular")) m.SetFloat("_BlendModePreserveSpecular", 1f);
            m.SetColor("_EmissionColor", Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            if (m.HasProperty("_QueueControl")) m.SetFloat("_QueueControl", 0f);
            BaseShaderGUI.SetMaterialKeywords(m, LitGUI.SetMaterialKeywords);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            cache[name] = m;
            return m;
        }

        /// <summary>
        /// Every palette entry, created now (so the folder always holds the full palette), and the maps of surfaces no
        /// finish uses any more removed.
        /// </summary>
        public void CreateAll()
        {
            foreach (var p in typeof(CarPalette).GetProperties())
                if (p.PropertyType == typeof(Material)) p.GetValue(this);
            var used = new HashSet<string>();
            foreach (Finish f in Finishes.Values) used.Add(f.Surface);
            textures.RemoveUnused(used);
        }
    }
}
#endif
