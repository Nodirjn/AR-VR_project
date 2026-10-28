#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AurelleRealism
{
    /// <summary>
    /// Light fixtures as emissive geometry (no Light components are touched): the three circular rings become
    /// suspended LED ring luminaires, the six recessed spot lenses become surface-mounted downlight cans, the rear
    /// wall-wash spots get a suspended track with spot heads, the rim spot a pendant head, and four linear LED lines
    /// run front to back between the downlight columns. Fixture positions are read from the scene's lights, so a
    /// re-run follows lights that were moved.
    /// </summary>
    internal static class InteriorCeilingFixtures
    {
        private const string Arch = "Showroom Environment/Showroom Architecture/";
        private static float Ceiling => InteriorRealismUpgrade.CeilingY;

        public static void Apply(InteriorContext ctx)
        {
            GameObject env = ctx.Root("Showroom Environment");
            if (env == null) return;
            Transform group = ctx.Ensure(env.transform, "Showroom Ceiling Fixtures");
            group.position = Vector3.zero; group.rotation = Quaternion.identity; group.localScale = Vector3.one;
            Rings(ctx);
            Downlights(ctx);
            LinearLines(ctx, group);
            WallWashTrack(ctx, group);
            PendantSpots(ctx, group);
            InteriorContext.MarkStatic(group.gameObject, false);
        }

        private static void Rings(InteriorContext ctx)
        {
            var m = ctx.Mats;
            foreach (string name in new[] { "Ceiling LED Ring Large", "Ceiling LED Ring Mid", "Ceiling LED Ring Inner" })
            {
                Transform t = ctx.Find(Arch + name);
                var r = t != null ? t.GetComponent<MeshRenderer>() : null;
                if (r == null) continue;
                float radius = Mathf.Round(r.bounds.extents.x * 1000f) / 1000f;   // stable across re-runs
                Vector3 centre = t.position;
                Mesh mesh = InteriorShapes.ForObject(t, "Interior_RingLuminaire_" + name.Substring("Ceiling LED Ring ".Length), 3, true, b =>
                {
                    float ri = radius - 0.04f, ro = radius, hh = 0.045f;
                    int seg = Mathf.Clamp(Mathf.RoundToInt(radius * 64f), 96, 288);
                    b.Lathe(Vector3.zero, new List<Vector2> { new Vector2(ri + 0.004f, -hh), new Vector2(ro - 0.004f, -hh) }, seg, 1);
                    b.Lathe(Vector3.zero, new List<Vector2>
                    {
                        new Vector2(ro - 0.004f, -hh), new Vector2(ro, -hh + 0.004f), new Vector2(ro, hh - 0.004f), new Vector2(ro - 0.004f, hh),
                        new Vector2(ri + 0.004f, hh), new Vector2(ri, hh - 0.004f), new Vector2(ri, -hh + 0.004f), new Vector2(ri + 0.004f, -hh),
                    }, seg, 0);
                    float top = Ceiling - centre.y;
                    for (int k = 0; k < 4; k++)
                    {
                        float a = (45f + 90f * k) * Mathf.Deg2Rad;
                        float rc = radius - 0.02f;
                        Vector3 p = new Vector3(Mathf.Cos(a) * rc, 0f, Mathf.Sin(a) * rc);
                        b.Rod(p + Vector3.up * hh, p + Vector3.up * (top - 0.012f), 0.002f, 6, 2, false);
                        b.Lathe(p + Vector3.up * (top - 0.012f), new List<Vector2> { new Vector2(0f, 0f), new Vector2(0.022f, 0f), new Vector2(0.022f, 0.012f) }, 16, 0);
                    }
                });
                ctx.Restyle(t, mesh, m.BlackPowder(), m.Diffuser(), m.Cable());
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private static void Downlights(InteriorContext ctx)
        {
            var m = ctx.Mats;
            for (int i = 1; i <= 12; i++)
            {
                Transform lens = ctx.Find(Arch + "Recessed Spotlight Lens " + i);
                if (lens == null) continue;
                Transform light = ctx.Find(Arch + "Recessed Ceiling Spotlight " + i);
                Vector3 axis = light != null ? light.position : lens.position;
                Mesh mesh = InteriorShapes.ForObject(lens, "Interior_DownlightCan", 3, true, b =>
                {
                    Vector3 c = new Vector3(axis.x - lens.position.x, 0f, axis.z - lens.position.z);
                    float yc = Ceiling - lens.position.y;
                    const float ro = 0.075f, ri = 0.058f, len = 0.17f, recess = 0.04f;
                    b.Lathe(c, new List<Vector2> { new Vector2(ri, yc - len), new Vector2(ro - 0.003f, yc - len), new Vector2(ro, yc - len + 0.003f), new Vector2(ro, yc) }, 40, 0);
                    b.Lathe(c, new List<Vector2> { new Vector2(ri, yc - len + recess), new Vector2(ri, yc - len) }, 40, 1);
                    b.Lathe(c, new List<Vector2> { new Vector2(0f, yc - len + recess), new Vector2(ri, yc - len + recess) }, 40, 2);
                });
                ctx.Restyle(lens, mesh, m.WhitePowder(), m.BlackPowder(), m.LedNeutral());
                lens.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private static void LinearLines(InteriorContext ctx, Transform group)
        {
            var m = ctx.Mats;
            float[] xs = { -8.6f, -3.8f, 5.8f, 10.6f };
            const float z0 = -7.6f, z1 = 16.8f;
            float length = z1 - z0;
            Mesh mesh = InteriorMeshes.Build("Interior_LinearLedLine", 2, true, b =>
            {
                b.RoundedBox(new Vector3(0f, -0.006f, 0f), new Vector3(0.07f, 0.012f, length), 0f, 0, new[] { 0, 0, -1, 0, 0, 0 });
                b.RoundedBox(new Vector3(0f, -0.0135f, 0f), new Vector3(0.042f, 0.003f, length - 0.03f), 0f, 0, new[] { 1, 1, -1, 1, 1, 1 });
            });
            for (int i = 0; i < xs.Length; i++)
            {
                GameObject go = ctx.Part(group, "Ceiling linear LED " + (i + 1), mesh, new[] { m.WhitePowder(), m.LedNeutral() },
                    new Vector3(xs[i], Ceiling, (z0 + z1) * 0.5f), Quaternion.identity, false);
            }
        }

        private static void WallWashTrack(InteriorContext ctx, Transform group)
        {
            var m = ctx.Mats;
            var spots = new List<Transform>();
            for (int i = 1; i <= 12; i++)
            {
                Transform t = ctx.Find("Showroom Lighting/Rear Wall Wash Spotlight " + i);
                if (t != null && t.position.y < Ceiling - 0.2f) spots.Add(t);
            }
            if (spots.Count == 0) return;
            float y = 0f, z = 0f, xMin = float.MaxValue, xMax = float.MinValue;
            foreach (Transform t in spots) { y += t.position.y; z += t.position.z; xMin = Mathf.Min(xMin, t.position.x); xMax = Mathf.Max(xMax, t.position.x); }
            y /= spots.Count; z /= spots.Count;
            const float headR = 0.045f, track = 0.036f;
            float trackY = y + headR + 0.03f + track * 0.5f;
            Vector3 origin = new Vector3((xMin + xMax) * 0.5f, trackY, z);
            float len = xMax - xMin + 2.4f;
            Mesh mesh = InteriorMeshes.Build("Interior_WallWashTrack", 3, true, b =>
            {
                b.RoundedBox(Vector3.zero, new Vector3(len, track, track), 0.004f, 1);
                // suspension cables every ~4 m
                int n = Mathf.Max(2, Mathf.CeilToInt(len / 4f) + 1);
                for (int k = 0; k < n; k++)
                {
                    float x = -len * 0.5f + 0.2f + (len - 0.4f) * k / (n - 1);
                    b.Rod(new Vector3(x, track * 0.5f, 0f), new Vector3(x, Ceiling - trackY - 0.012f, 0f), 0.002f, 6, 2, false);
                    b.Lathe(new Vector3(x, Ceiling - trackY - 0.012f, 0f), new List<Vector2> { new Vector2(0f, 0f), new Vector2(0.022f, 0f), new Vector2(0.022f, 0.012f) }, 16, 0);
                }
                foreach (Transform t in spots)
                {
                    Vector3 c = t.position - origin;
                    SpotHead(b, c, t.forward, 0, 1);
                    // adapter between the head and the track
                    float a0 = c.y + headR - 0.004f, a1 = -track * 0.5f + 0.004f;
                    b.RoundedBox(new Vector3(c.x, (a0 + a1) * 0.5f, c.z), new Vector3(0.028f, Mathf.Max(0.01f, a1 - a0), 0.05f), 0.003f, 1);
                }
            });
            ctx.Part(group, "Wall-wash track", mesh, new[] { m.BlackPowder(), m.LedNeutral(), m.Cable() }, origin, Quaternion.identity, false);
        }

        private static void PendantSpots(InteriorContext ctx, Transform group)
        {
            var m = ctx.Mats;
            Transform rim = ctx.Find("Showroom Lighting/Showroom Rim Spot");
            if (rim == null || rim.position.y > Ceiling - 0.2f) return;
            Vector3 p = rim.position;
            Mesh mesh = InteriorMeshes.Build("Interior_RimSpotPendant", 3, true, b =>
            {
                SpotHead(b, Vector3.zero, rim.forward, 0, 1);
                b.Rod(new Vector3(0f, 0.04f, 0f), new Vector3(0f, Ceiling - p.y - 0.012f, 0f), 0.006f, 10, 0, false);
                b.Lathe(new Vector3(0f, Ceiling - p.y - 0.012f, 0f), new List<Vector2> { new Vector2(0f, 0f), new Vector2(0.03f, 0f), new Vector2(0.03f, 0.012f) }, 16, 0);
            });
            ctx.Part(group, "Rim spot pendant", mesh, new[] { m.BlackPowder(), m.LedNeutral(), m.Cable() }, p, Quaternion.identity, false);
        }

        /// <summary>Cylindrical spot head centred on <paramref name="c"/>, aimed along <paramref name="forward"/>, with a recessed emissive lens.</summary>
        private static void SpotHead(InteriorMeshBuilder b, Vector3 c, Vector3 forward, int housing, int lens)
        {
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, forward.normalized);
            const float r = 0.045f, half = 0.08f, ri = 0.034f, recess = 0.02f;
            b.Lathe(c, new List<Vector2>
            {
                new Vector2(0f, -half), new Vector2(r - 0.006f, -half), new Vector2(r, -half + 0.006f), new Vector2(r, half - 0.003f),
                new Vector2(r - 0.003f, half), new Vector2(ri, half), new Vector2(ri, half - recess),
            }, 28, housing, rot);
            // drawn inwards so the lens normal points along +axis (forward)
            b.Lathe(c, new List<Vector2> { new Vector2(ri, half - recess), new Vector2(0f, half - recess) }, 28, lens, rot);
        }
    }
}
#endif
