#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace AurelleRealism
{
    /// <summary>
    /// Rear-wall boards become framed backlit displays: black aluminium lightbox frame, glossy black screen whose
    /// emission map is a studio render of the hero car (Textures/Interior_Poster_N.png). The three TextMesh lines of
    /// each board are kept; the cartoon pictogram and the coloured accent bars are removed.
    /// </summary>
    internal static class InteriorDisplays
    {
        private const string Zone = "Showroom Interior Zones/Rear Wall Advertising Displays";

        public static void Apply(InteriorContext ctx)
        {
            Transform zone = ctx.Find(Zone);
            if (zone == null) { InteriorRealismUpgrade.Log("Rear Wall Advertising Displays missing"); return; }
            ctx.RemoveChildren(zone, c => (c.name.StartsWith("Display ", StringComparison.Ordinal) && c.name.Contains(" • coupe "))
                                          || c.name.EndsWith("• carmine header", StringComparison.Ordinal)
                                          || c.name.EndsWith("• lower accent", StringComparison.Ordinal));
            for (int i = 1; i <= 3; i++)
            {
                Transform screen = InteriorContext.Child(zone, "Wall display " + i + " • illuminated advertising panel");
                Transform bezel = InteriorContext.Child(zone, "Wall display " + i + " • slim graphite bezel");
                if (screen == null) continue;
                Vector3 s = InteriorShapes.PrimitiveSize(screen, Vector3.one);
                float w = s.x, h = s.y, d = s.z;
                Mesh screenMesh = InteriorShapes.ForObject(screen, "Interior_Display_Screen", 2, true, b =>
                {
                    // screen face (+Z, towards the showroom) with 0..1 UVs; seen from +Z, -X is to the right
                    Vector3 a = new Vector3(w * 0.5f, -h * 0.5f, d * 0.5f), bb = new Vector3(w * 0.5f, h * 0.5f, d * 0.5f);
                    Vector3 c = new Vector3(-w * 0.5f, h * 0.5f, d * 0.5f), e = new Vector3(-w * 0.5f, -h * 0.5f, d * 0.5f);
                    b.Quad(0, a, bb, c, e, Vector3.forward, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f));
                    b.RoundedBox(Vector3.zero, new Vector3(w, h, d), 0f, 0, new[] { 1, 1, 1, 1, -1, 1 });
                });
                ctx.Restyle(screen, screenMesh, PosterMaterial(ctx, i), ctx.Mats.BlackPowder());
                var sr = screen.GetComponent<MeshRenderer>();
                sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                if (bezel != null)
                {
                    Vector3 bp = bezel.position, sp = screen.position;
                    float back = bp.z - InteriorShapes.PrimitiveSize(bezel, Vector3.one).z * 0.5f; // against the wall
                    float screenBack = sp.z - d * 0.5f, screenFront = sp.z + d * 0.5f;
                    Mesh frame = InteriorShapes.ForObject(bezel, "Interior_Display_LightboxFrame", 1, true, b =>
                    {
                        Vector3 o = sp - bp;                      // screen centre relative to the bezel pivot
                        const float lip = 0.022f, proud = 0.002f;
                        float boxDepth = screenBack - back;
                        b.RoundedBox(new Vector3(o.x, o.y, back - bp.z + boxDepth * 0.5f), new Vector3(w + 2f * lip, h + 2f * lip, boxDepth), 0.006f, 1);
                        float lipDepth = screenFront + proud - screenBack;
                        float zc = screenBack - bp.z + lipDepth * 0.5f;
                        b.RoundedBox(new Vector3(o.x, o.y + h * 0.5f + lip * 0.5f, zc), new Vector3(w + 2f * lip, lip, lipDepth), 0.003f, 1);
                        b.RoundedBox(new Vector3(o.x, o.y - h * 0.5f - lip * 0.5f, zc), new Vector3(w + 2f * lip, lip, lipDepth), 0.003f, 1);
                        b.RoundedBox(new Vector3(o.x + w * 0.5f + lip * 0.5f, o.y, zc), new Vector3(lip, h, lipDepth), 0.003f, 1);
                        b.RoundedBox(new Vector3(o.x - w * 0.5f - lip * 0.5f, o.y, zc), new Vector3(lip, h, lipDepth), 0.003f, 1);
                    });
                    ctx.Restyle(bezel, frame, ctx.Mats.Get("Interior_Metal_DisplayFrameAnodised", new InteriorLitDef
                    {
                        BaseColor = new Color(0.09f, 0.09f, 0.095f), Smoothness = 0.62f, Metallic = 0.85f,
                    }));
                    bezel.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
        }

        private static Material PosterMaterial(InteriorContext ctx, int index)
        {
            string poster = InteriorRealismUpgrade.TextureFolder + "/Interior_Poster_" + index + ".jpg";
            bool has = AssetDatabase.LoadAssetAtPath<Texture2D>(poster) != null;
            if (has) InteriorAssets.ConfigureTexture(poster, InteriorTexKind.Emissive, 2048, 1024);
            return ctx.Mats.Get("Interior_Display_Poster_" + index, new InteriorLitDef
            {
                BaseColor = new Color(0.012f, 0.012f, 0.014f), Smoothness = 0.9f, SpecularHighlights = false,
                EmissionMap = has ? poster : null,
                Emission = has ? new Color(1f, 1f, 1f) * 1.15f : new Color(0.02f, 0.025f, 0.03f),
                BakedEmission = false,
            });
        }
    }
}
#endif
