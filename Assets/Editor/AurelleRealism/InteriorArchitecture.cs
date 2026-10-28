#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AurelleRealism
{
    internal static class InteriorShapes
    {
        /// <summary>
        /// Builds a mesh in metres around the object's pivot and stores it in the object's unscaled local space, so the
        /// existing transform (and any serialized MeshCollider using the old mesh) stays untouched.
        /// </summary>
        public static Mesh ForObject(Transform t, string meshName, int submeshes, bool lightmapUVs, Action<InteriorMeshBuilder> fill)
        {
            var b = new InteriorMeshBuilder(submeshes);
            fill(b);
            Mesh mesh = b.ToMesh(meshName, lightmapUVs);
            Vector3 s = t != null ? t.lossyScale : Vector3.one;
            if ((s - Vector3.one).sqrMagnitude > 1e-8f)
            {
                var v = mesh.vertices;
                var n = mesh.normals;
                for (int i = 0; i < v.Length; i++)
                {
                    v[i] = new Vector3(v[i].x / s.x, v[i].y / s.y, v[i].z / s.z);
                    n[i] = new Vector3(n[i].x * s.x, n[i].y * s.y, n[i].z * s.z).normalized;
                }
                mesh.vertices = v;
                mesh.normals = n;
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
            }
            return InteriorMeshes.Save(mesh, meshName);
        }

        /// <summary>World-space size of a renderer object that was built from a unit primitive (cube/cylinder).</summary>
        public static Vector3 PrimitiveSize(Transform t, Vector3 unitSize)
        {
            Vector3 s = t.lossyScale;
            return new Vector3(Mathf.Abs(s.x) * unitSize.x, Mathf.Abs(s.y) * unitSize.y, Mathf.Abs(s.z) * unitSize.z);
        }

        // face order for RoundedBox: +X, -X, +Y, -Y, +Z, -Z
        public static readonly int[] AllZero = { 0, 0, 0, 0, 0, 0 };
        public static readonly int[] FrontOne = { 0, 0, 0, 0, 1, 0 };
    }

    /// <summary>Floor, walls, ceiling and glazing of the pavilion.</summary>
    internal static class InteriorArchitecture
    {
        private const string Arch = "Showroom Environment/Showroom Architecture/";

        public static void Apply(InteriorContext ctx)
        {
            Floor(ctx);
            Walls(ctx);
            Ceiling(ctx);
            Glazing(ctx);
        }

        private static void Floor(InteriorContext ctx)
        {
            Transform floor = ctx.Find("Showroom Environment/Showroom Floor");
            if (floor == null) { InteriorRealismUpgrade.Log("Showroom Floor missing"); return; }
            Vector3 s = floor.lossyScale;
            // Polished stone inside the glass line and the entrance; honed (same stone, rougher) on the forecourt ring.
            Mesh mesh = InteriorMeshes.ShowroomFloor("Interior_ShowroomFloor", new Vector2(-17.44f, -18f), new Vector2(19.44f, 18.1f), s.x);
            InteriorMeshes.ManualLightmapUVs(mesh, new Rect(-5f, -5f, 10f, 10f));
            UnityEditor.EditorUtility.SetDirty(mesh);
            ctx.Restyle(floor, mesh, ctx.Mats.FloorPolished(), ctx.Mats.FloorHoned());
        }

        private static void Walls(InteriorContext ctx)
        {
            var m = ctx.Mats;
            // Main feature wall: board-marked architectural concrete on every vertical face; black powder coat on the
            // hidden top/bottom. Material slot 0 stays a dark metal because the desktop entrance doors copy it.
            Transform feature = ctx.Find(Arch + "Rear Feature Wall");
            if (feature != null)
            {
                Vector3 size = InteriorShapes.PrimitiveSize(feature, Vector3.one);
                Mesh mesh = InteriorShapes.ForObject(feature, "Interior_RearFeatureWall", 2, true,
                    b => b.RoundedBox(Vector3.zero, size, 0.006f, 1, new[] { 1, 1, 0, 0, 1, 1 }));
                ctx.Restyle(feature, mesh, m.BlackPowder(), m.ConcreteWall());
            }

            Transform rear = ctx.Find(Arch + "Rear Graphite Feature Wall");
            if (rear != null)
            {
                Vector3 size = InteriorShapes.PrimitiveSize(rear, Vector3.one);
                Mesh mesh = InteriorShapes.ForObject(rear, "Interior_RearPlasterWall", 1, true,
                    b => b.RoundedBox(Vector3.zero, size, 0f, 0));
                ctx.Restyle(rear, mesh, m.WhitePlaster());
            }

            if (feature != null)
            {
                // recessed shadow-gap skirting at the foot of the concrete (new object, outside 'Showroom Architecture')
                Bounds fb = feature.GetComponent<Renderer>().bounds;
                GameObject env = ctx.Root("Showroom Environment");
                Transform finishes = ctx.Ensure(env.transform, "Showroom Interior Finishes");
                finishes.position = Vector3.zero; finishes.rotation = Quaternion.identity; finishes.localScale = Vector3.one;
                float y0 = InteriorRealismUpgrade.FloorY;
                Mesh gap = InteriorMeshes.Box("Interior_FeatureWall_ShadowGap", new Vector3(fb.size.x + 0.004f, 0.05f, 0.004f), 0f, 0, null, 1, true);
                GameObject g = ctx.Part(finishes, "Feature wall shadow-gap skirting", gap, new[] { m.BlackPowder() },
                    new Vector3(fb.center.x, y0 + 0.025f, fb.max.z + 0.0015f), Quaternion.identity, false);
                InteriorContext.MarkStatic(g, false);
            }

            Transform architecture = ctx.Find("Showroom Environment/Showroom Architecture");
            if (architecture == null) return;
            foreach (Transform t in architecture)
            {
                string n = t.name;
                if (n.StartsWith("Rear Wall Graphite Panel", StringComparison.Ordinal) || n.StartsWith("Feature Wall Vertical Reveal", StringComparison.Ordinal)
                    || n == "Feature Wall Lower Reveal" || n.StartsWith("Ceiling Front Beam", StringComparison.Ordinal) || n.StartsWith("Ceiling Rear Beam", StringComparison.Ordinal))
                    ctx.Restyle(t, null, m.BlackPowder());
                else if (n == "Rear Wall Metallic Reveal")
                    ctx.Restyle(t, null, m.BrushedSteel());
                else if (n == "Feature Wall Upper Cove Light" || n.StartsWith("Feature Wall Side Light", StringComparison.Ordinal))
                {
                    ctx.Restyle(t, null, m.LedNeutral());
                    t.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
        }

        private static void Ceiling(InteriorContext ctx)
        {
            Transform canopy = ctx.Find(Arch + "Ceiling Canopy");
            if (canopy == null) return;
            Vector3 size = InteriorShapes.PrimitiveSize(canopy, Vector3.one);
            Mesh mesh = InteriorShapes.ForObject(canopy, "Interior_CeilingSlab", 1, true, b => b.RoundedBox(Vector3.zero, size, 0f, 0));
            ctx.Restyle(canopy, mesh, ctx.Mats.CeilingPlaster());
        }

        private static void Glazing(InteriorContext ctx)
        {
            InteriorGlazing.Apply(ctx);
        }
    }

    /// <summary>The hero car's round podium: polished black marble deck, brushed steel band, flush LED line.</summary>
    internal static class InteriorPodium
    {
        private const string Root = "Showroom Environment/Circular Display/";

        public static void Apply(InteriorContext ctx)
        {
            var m = ctx.Mats;
            Transform plinth = ctx.Find(Root + "Raised Circular Display Plinth");
            if (plinth != null)
            {
                float r = plinth.lossyScale.x * 0.5f, h = plinth.lossyScale.y; // unit cylinder: radius 0.5, height 2
                Mesh mesh = InteriorShapes.ForObject(plinth, "Interior_PodiumPlinth", 2, true, b =>
                {
                    b.Lathe(Vector3.zero, new List<Vector2> { new Vector2(0f, -h), new Vector2(r, -h) }, 160, 1);
                    b.Lathe(Vector3.zero, new List<Vector2> { new Vector2(r, -h), new Vector2(r, h - 0.006f), new Vector2(r - 0.006f, h) }, 160, 0);
                    b.Lathe(Vector3.zero, new List<Vector2> { new Vector2(r - 0.006f, h), new Vector2(0f, h) }, 160, 1);
                });
                ctx.Restyle(plinth, mesh, m.BrushedSteel(), m.BlackPowder());
            }

            Transform deck = ctx.Find(Root + "Inset Display Surface");
            if (deck != null)
            {
                float r = deck.lossyScale.x * 0.5f, h = deck.lossyScale.y;
                Mesh mesh = InteriorShapes.ForObject(deck, "Interior_PodiumDeck", 1, true, b =>
                    b.Lathe(Vector3.zero, new List<Vector2> { new Vector2(0f, -h), new Vector2(r, -h), new Vector2(r, h - 0.003f), new Vector2(r - 0.003f, h), new Vector2(0f, h) }, 160, 0));
                ctx.Restyle(deck, mesh, m.BlackMarble());
            }

            float deckTop = deck != null ? deck.position.y + deck.lossyScale.y : 0.018f;
            Vector3 centre = deck != null ? deck.position : new Vector3(1.08f, 0f, 1.31f);

            Transform inlay = ctx.Find(Root + "Platform Metallic Inlay");
            if (inlay != null)
            {
                float y0 = deckTop - inlay.position.y;
                Mesh mesh = InteriorShapes.ForObject(inlay, "Interior_PodiumSteelInlay", 1, true, b =>
                    b.Lathe(new Vector3(centre.x - inlay.position.x, 0f, centre.z - inlay.position.z), new List<Vector2>
                    {
                        new Vector2(2.885f, y0 - 0.002f), new Vector2(2.925f, y0 - 0.002f), new Vector2(2.925f, y0 + 0.006f),
                        new Vector2(2.92f, y0 + 0.009f), new Vector2(2.89f, y0 + 0.009f), new Vector2(2.885f, y0 + 0.006f), new Vector2(2.885f, y0 - 0.002f),
                    }, 192, 0));
                ctx.Restyle(inlay, mesh, m.PolishedSteel());
                inlay.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            Transform led = ctx.Find(Root + "Platform Integrated LED");
            if (led != null)
            {
                float y0 = deckTop - led.position.y;
                Mesh mesh = InteriorShapes.ForObject(led, "Interior_PodiumLedLine", 2, true, b =>
                {
                    Vector3 c = new Vector3(centre.x - led.position.x, 0f, centre.z - led.position.z);
                    // black channel with a frosted LED line on top
                    b.Lathe(c, new List<Vector2> { new Vector2(2.832f, y0 - 0.002f), new Vector2(2.868f, y0 - 0.002f), new Vector2(2.868f, y0 + 0.004f), new Vector2(2.864f, y0 + 0.004f) }, 192, 0);
                    b.Lathe(c, new List<Vector2> { new Vector2(2.836f, y0 + 0.004f), new Vector2(2.832f, y0 + 0.004f), new Vector2(2.832f, y0 - 0.002f) }, 192, 0);
                    b.Lathe(c, new List<Vector2> { new Vector2(2.864f, y0 + 0.0045f), new Vector2(2.836f, y0 + 0.0045f) }, 192, 1);
                });
                ctx.Restyle(led, mesh, m.BlackPowder(), m.LedPodium());
                led.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
    }
}
#endif
