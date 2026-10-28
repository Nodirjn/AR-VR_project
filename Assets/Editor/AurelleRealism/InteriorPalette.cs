#if UNITY_EDITOR
using UnityEngine;

namespace AurelleRealism
{
    /// <summary>
    /// Every interior material, by role. Textures: Poly Haven and ambientCG (CC0). Tiling values are 1 / texture size
    /// in metres for the metre-UV meshes built by InteriorMeshes.
    /// </summary>
    internal static class InteriorPalette
    {
        private const string PH = "Assets/ThirdParty/PolyHaven/";
        private const string ACG = "Assets/ThirdParty/ambientCG/";

        private static Vector2 Metres(float size) => new Vector2(1f / size, 1f / size);

        // ------------------------------------------------------------------ floor
        public static Material FloorPolished(this InteriorMaterialLibrary l) => l.Get("Interior_Floor_Granite_Polished", new InteriorLitDef
        {
            BaseMap = PH + "granite_tile/granite_tile_diff_2k.jpg",
            NormalMap = PH + "granite_tile/granite_tile_nor_gl_2k.jpg",
            MaskMap = PH + "granite_tile/granite_tile_mask_1k.png",
            Tiling = Metres(6f), NormalScale = 0.45f, Smoothness = 1f, Occlusion = 0.85f,
            BaseColor = new Color(1.05f, 1.05f, 1.06f),
        });

        public static Material FloorHoned(this InteriorMaterialLibrary l) => l.Get("Interior_Floor_Granite_Honed", new InteriorLitDef
        {
            BaseMap = PH + "granite_tile/granite_tile_diff_2k.jpg",
            NormalMap = PH + "granite_tile/granite_tile_nor_gl_2k.jpg",
            MaskMap = PH + "granite_tile/granite_tile_mask_1k.png",
            Tiling = Metres(6f), NormalScale = 0.8f, Smoothness = 0.55f, Occlusion = 1f,
            BaseColor = new Color(1.1f, 1.1f, 1.1f),
        });

        // ------------------------------------------------------------------ walls and ceiling
        public static Material ConcreteWall(this InteriorMaterialLibrary l) => l.Get("Interior_Wall_ArchitecturalConcrete", new InteriorLitDef
        {
            BaseMap = PH + "concrete_wall_001/concrete_wall_001_diff_2k.jpg",
            NormalMap = PH + "concrete_wall_001/concrete_wall_001_nor_gl_1k.jpg",
            MaskMap = PH + "concrete_wall_001/concrete_wall_001_mask_1k.png",
            Tiling = Metres(2.4f), NormalScale = 0.9f, Smoothness = 0.45f, Occlusion = 1f,
            BaseColor = new Color(0.96f, 0.95f, 0.93f),
        });

        public static Material WhitePlaster(this InteriorMaterialLibrary l) => l.Get("Interior_Plaster_White", new InteriorLitDef
        {
            BaseMap = PH + "white_stucco/white_stucco_diff_1k.jpg",
            NormalMap = PH + "white_stucco/white_stucco_nor_gl_1k.jpg",
            MaskMap = PH + "white_stucco/white_stucco_mask_1k.png",
            Tiling = Metres(2f), NormalScale = 0.35f, Smoothness = 0.9f, Occlusion = 0.6f,
            BaseColor = new Color(0.93f, 0.92f, 0.9f),
        });

        public static Material CeilingPlaster(this InteriorMaterialLibrary l) => l.Get("Interior_Ceiling_WhitePlaster", new InteriorLitDef
        {
            BaseMap = PH + "white_stucco/white_stucco_diff_1k.jpg",
            NormalMap = PH + "white_stucco/white_stucco_nor_gl_1k.jpg",
            MaskMap = PH + "white_stucco/white_stucco_mask_1k.png",
            Tiling = Metres(2.5f), NormalScale = 0.25f, Smoothness = 0.85f, Occlusion = 0.5f,
            BaseColor = new Color(1f, 0.965f, 0.91f),
        });

        public static Material WalnutSlats(this InteriorMaterialLibrary l) => l.Get("Interior_Wood_WalnutSlatPanel", new InteriorLitDef
        {
            BaseMap = PH + "synthetic_wood/synthetic_wood_diff_1k.jpg",
            NormalMap = PH + "synthetic_wood/synthetic_wood_nor_gl_1k.jpg",
            MaskMap = PH + "synthetic_wood/synthetic_wood_mask_1k.png",
            Tiling = Metres(2f), NormalScale = 1.2f, Smoothness = 0.9f, Occlusion = 1f,
            BaseColor = new Color(0.92f, 0.86f, 0.8f),
        });

        // ------------------------------------------------------------------ glass and metals
        // Laminated low-iron facade glass with a neutral solar coating, one surface per wall. The alpha is the light
        // it holds back (about 30 %, as a coated facade unit transmits about 70 %), with a faint green-blue body
        // colour. Reflections keep full strength (preserve specular) with an F0 of about 9 % (both faces of the pane
        // and the coating), so the hall's lights and the room show in the glass from inside, and the reflection takes
        // over at grazing angles. It receives shadows, so the sun's highlight stays off faces the canopy and walls shade.
        public static Material ClearGlass(this InteriorMaterialLibrary l) => l.Get("Interior_Glass_LowIronClear", new InteriorLitDef
        {
            BaseColor = new Color(0.09f, 0.15f, 0.15f, 0.3f), Specular = new Color(0.085f, 0.092f, 0.092f), Smoothness = 0.975f,
            Transparent = true,
        });

        // Black structural silicone between the panes, flush with both faces.
        public static Material GlassSilicone(this InteriorMaterialLibrary l) => l.Get("Interior_Glazing_SiliconeBlack", new InteriorLitDef
        {
            BaseColor = new Color(0.022f, 0.023f, 0.025f), Smoothness = 0.62f,
        });

        // Frosted safety manifestation film (dots and the Aurelle band) on both faces of every pane. The texture is
        // generated by InteriorGlazing; its clear parts must not reflect, so specular follows the alpha. The weak
        // emission stands for the daylight the film scatters through from the far side.
        public static Material GlassManifestation(this InteriorMaterialLibrary l) => l.Get("Interior_Glazing_ManifestationFilm", new InteriorLitDef
        {
            BaseMap = InteriorGlazing.ManifestationTexture,
            BaseColor = new Color(0.96f, 0.97f, 0.97f, 0.66f), Smoothness = 0.3f,
            Emission = new Color(0.15f, 0.155f, 0.16f), EmissionMap = InteriorGlazing.ManifestationTexture,
            Transparent = true, PreserveSpecular = false,
        });

        public static Material BlackPowder(this InteriorMaterialLibrary l) => l.Get("Interior_Metal_BlackPowderCoat", new InteriorLitDef
        {
            BaseColor = new Color(0.035f, 0.036f, 0.038f), Smoothness = 0.42f, Metallic = 0f,
        });

        public static Material DarkBronze(this InteriorMaterialLibrary l) => l.Get("Interior_Metal_DarkBronzeBrushed", new InteriorLitDef
        {
            BaseMap = ACG + "Metal009/Metal009_1K_Color.jpg",
            NormalMap = ACG + "Metal009/Metal009_1K_NormalGL.jpg",
            MaskMap = ACG + "Metal009/Metal009_1K_Mask.png",
            Tiling = Metres(0.6f), NormalScale = 0.6f, Smoothness = 0.78f,
            BaseColor = new Color(0.4f, 0.31f, 0.23f),
        });

        public static Material BrushedSteel(this InteriorMaterialLibrary l) => l.Get("Interior_Metal_BrushedStainless", new InteriorLitDef
        {
            BaseMap = ACG + "Metal009/Metal009_1K_Color.jpg",
            NormalMap = ACG + "Metal009/Metal009_1K_NormalGL.jpg",
            MaskMap = ACG + "Metal009/Metal009_1K_Mask.png",
            Tiling = Metres(0.6f), NormalScale = 0.6f, Smoothness = 1f,
            BaseColor = new Color(2f, 2f, 2f),
        });

        public static Material PolishedSteel(this InteriorMaterialLibrary l) => l.Get("Interior_Metal_PolishedStainless", new InteriorLitDef
        {
            BaseMap = ACG + "Metal009/Metal009_1K_Color.jpg",
            NormalMap = ACG + "Metal009/Metal009_1K_NormalGL.jpg",
            Tiling = Metres(0.6f), NormalScale = 0.12f, Smoothness = 0.9f, Metallic = 1f,
            BaseColor = new Color(2.05f, 2.05f, 2.05f),
        });

        public static Material WhitePowder(this InteriorMaterialLibrary l) => l.Get("Interior_Metal_WhitePowderCoat", new InteriorLitDef
        {
            BaseColor = new Color(0.86f, 0.86f, 0.85f), Smoothness = 0.45f,
        });

        public static Material Cable(this InteriorMaterialLibrary l) => l.Get("Interior_Metal_SteelCable", new InteriorLitDef
        {
            BaseColor = new Color(0.6f, 0.6f, 0.62f), Smoothness = 0.6f, Metallic = 1f,
        });

        // ------------------------------------------------------------------ stone and wood
        public static Material BlackMarble(this InteriorMaterialLibrary l) => l.Get("Interior_Stone_NeroMarquinaPolished", new InteriorLitDef
        {
            BaseMap = ACG + "Marble016/Marble016_2K_Color.jpg",
            NormalMap = ACG + "Marble016/Marble016_1K_NormalGL.jpg",
            MaskMap = ACG + "Marble016/Marble016_1K_Mask.png",
            Tiling = Metres(2.2f), NormalScale = 0.3f, Smoothness = 1f,
            BaseColor = new Color(0.95f, 0.95f, 0.95f),
        });

        public static Material WhiteMarble(this InteriorMaterialLibrary l) => l.Get("Interior_Stone_WhiteMarblePolished", new InteriorLitDef
        {
            BaseMap = ACG + "Marble012/Marble012_1K_Color.jpg",
            NormalMap = ACG + "Marble012/Marble012_1K_NormalGL.jpg",
            MaskMap = ACG + "Marble012/Marble012_1K_Mask.png",
            Tiling = Metres(1.6f), NormalScale = 0.25f, Smoothness = 0.97f,
            BaseColor = new Color(1.35f, 1.35f, 1.33f),
        });

        public static Material Walnut(this InteriorMaterialLibrary l) => l.Get("Interior_Wood_WalnutVeneer", new InteriorLitDef
        {
            BaseMap = ACG + "Wood066/Wood066_1K_Color.jpg",
            NormalMap = ACG + "Wood066/Wood066_1K_NormalGL.jpg",
            MaskMap = ACG + "Wood066/Wood066_1K_Mask.png",
            Tiling = Metres(0.9f), NormalScale = 0.5f, Smoothness = 0.82f,
            BaseColor = new Color(0.82f, 0.78f, 0.74f),
        });

        // ------------------------------------------------------------------ soft furnishings
        public static Material SofaFabric(this InteriorMaterialLibrary l) => l.Get("Interior_Fabric_SofaLightGrey", new InteriorLitDef
        {
            BaseMap = ACG + "Fabric030/Fabric030_1K_Color.jpg",
            NormalMap = ACG + "Fabric030/Fabric030_1K_NormalGL.jpg",
            MaskMap = ACG + "Fabric030/Fabric030_1K_Mask.png",
            Tiling = Metres(0.45f), NormalScale = 1.1f, Smoothness = 0.8f, Occlusion = 0.8f,
            BaseColor = new Color(1.85f, 1.8f, 1.72f),
        });

        public static Material PillowFabric(this InteriorMaterialLibrary l) => l.Get("Interior_Fabric_PillowCharcoal", new InteriorLitDef
        {
            BaseMap = ACG + "Fabric030/Fabric030_1K_Color.jpg",
            NormalMap = ACG + "Fabric030/Fabric030_1K_NormalGL.jpg",
            MaskMap = ACG + "Fabric030/Fabric030_1K_Mask.png",
            Tiling = Metres(0.35f), NormalScale = 1f, Smoothness = 0.8f, Occlusion = 0.8f,
            BaseColor = new Color(0.55f, 0.52f, 0.5f),
        });

        public static Material Rug(this InteriorMaterialLibrary l) => l.Get("Interior_Rug_WoolBeige", new InteriorLitDef
        {
            BaseMap = ACG + "Carpet016/Carpet016_1K_Color.jpg",
            NormalMap = ACG + "Carpet016/Carpet016_1K_NormalGL.jpg",
            MaskMap = ACG + "Carpet016/Carpet016_1K_Mask.png",
            Tiling = Metres(0.9f), NormalScale = 1f, Smoothness = 0.5f, Occlusion = 1f,
            BaseColor = new Color(0.64f, 0.67f, 0.84f), SpecularHighlights = false,
        });

        public static Material Planter(this InteriorMaterialLibrary l) => l.Get("Interior_Planter_FibreCement", new InteriorLitDef
        {
            BaseMap = PH + "white_stucco/white_stucco_diff_1k.jpg",
            NormalMap = PH + "white_stucco/white_stucco_nor_gl_1k.jpg",
            MaskMap = PH + "white_stucco/white_stucco_mask_1k.png",
            Tiling = Metres(0.8f), NormalScale = 0.8f, Smoothness = 1f, Occlusion = 0.6f,
            BaseColor = new Color(0.36f, 0.355f, 0.35f),
        });

        public static Material Soil(this InteriorMaterialLibrary l) => l.Get("Interior_Planter_Mulch", new InteriorLitDef
        {
            BaseMap = ACG + "Carpet016/Carpet016_1K_Color.jpg",
            NormalMap = ACG + "Carpet016/Carpet016_1K_NormalGL.jpg",
            Tiling = Metres(0.25f), NormalScale = 1.5f, Smoothness = 0.08f,
            BaseColor = new Color(0.16f, 0.12f, 0.09f), SpecularHighlights = false,
        });

        // ------------------------------------------------------------------ light emitters
        public static Material LedNeutral(this InteriorMaterialLibrary l) => l.Get("Interior_LED_Neutral4000K", new InteriorLitDef
        {
            BaseColor = new Color(0.95f, 0.95f, 0.93f), Smoothness = 0.3f,
            Emission = new Color(1f, 0.93f, 0.84f) * 3.2f, BakedEmission = true,
        });

        public static Material LedWarm(this InteriorMaterialLibrary l) => l.Get("Interior_LED_Warm3000K", new InteriorLitDef
        {
            BaseColor = new Color(0.95f, 0.92f, 0.86f), Smoothness = 0.3f,
            Emission = new Color(1f, 0.78f, 0.52f) * 2.4f, BakedEmission = true,
        });

        public static Material LedPodium(this InteriorMaterialLibrary l) => l.Get("Interior_LED_PodiumLine", new InteriorLitDef
        {
            BaseColor = new Color(0.95f, 0.95f, 0.95f), Smoothness = 0.5f,
            Emission = new Color(1f, 0.95f, 0.9f) * 2.2f, BakedEmission = true,
        });

        public static Material Diffuser(this InteriorMaterialLibrary l) => l.Get("Interior_LED_OpalDiffuserWarm", new InteriorLitDef
        {
            BaseColor = new Color(0.95f, 0.93f, 0.9f), Smoothness = 0.2f,
            Emission = new Color(1f, 0.84f, 0.64f) * 1.8f, BakedEmission = true,
        });

        public static Material GlobeGlow(this InteriorMaterialLibrary l) => l.Get("Interior_LED_OpalGlobe", new InteriorLitDef
        {
            BaseMap = PH + "modern_ceiling_lamp_01/modern_ceiling_lamp_01_diff_1k.jpg",
            BaseColor = new Color(1f, 1f, 1f), Smoothness = 0.85f,
            Emission = new Color(1f, 0.86f, 0.68f) * 1.25f, EmissionMap = PH + "modern_ceiling_lamp_01/modern_ceiling_lamp_01_diff_1k.jpg",
            BakedEmission = true,
        });

        // ------------------------------------------------------------------ Poly Haven model surfaces
        public static Material Model(this InteriorMaterialLibrary l, string asset, string stem, Color tint, float smoothness = 1f, float normalScale = 1f, float occlusion = 1f, float metallic = 0f)
        {
            string dir = PH + asset + "/";
            return l.Get("PH_" + stem, new InteriorLitDef
            {
                BaseMap = dir + stem + "_diff_1k.jpg",
                NormalMap = dir + stem + "_nor_gl_1k.jpg",
                MaskMap = dir + stem + "_mask_1k.png",
                BaseColor = tint, Smoothness = smoothness, NormalScale = normalScale, Occlusion = occlusion, Metallic = metallic,
            });
        }
    }
}
#endif
