#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AurelleRealism
{
    /// <summary>
    /// Reception: white marble worktop, fluted walnut front (the 'Reception desk mark' text stays in front of it),
    /// walnut slat backdrop, bronze brand plaque, opal-globe pendants and a few desk accessories.
    /// Object names used by gameplay checks are kept: 'Reception • stone worktop', 'Reception • walnut front',
    /// 'Reception desk mark', 'Reception • Aurelle brand plaque'.
    /// </summary>
    internal static class InteriorReception
    {
        private const string Zone = "Showroom Interior Zones/Reception Zone";

        public static void Apply(InteriorContext ctx)
        {
            Transform zone = ctx.Find(Zone);
            if (zone == null) { InteriorRealismUpgrade.Log("Reception Zone missing"); return; }
            var m = ctx.Mats;

            Transform top = InteriorContext.Child(zone, "Reception • stone worktop");
            if (top != null)
            {
                Vector3 size = InteriorShapes.PrimitiveSize(top, Vector3.one);
                Mesh mesh = InteriorShapes.ForObject(top, "Interior_Desk_MarbleWorktop", 1, true, b => b.RoundedBox(Vector3.zero, size, 0.012f, 3));
                ctx.Restyle(top, mesh, m.WhiteMarble());
            }

            Transform front = InteriorContext.Child(zone, "Reception • walnut front");
            if (front != null)
            {
                Vector3 size = InteriorShapes.PrimitiveSize(front, Vector3.one);
                Mesh mesh = InteriorShapes.ForObject(front, "Interior_Desk_FlutedWalnutFront", 1, true, b => FlutedFront(b, size));
                ctx.Restyle(front, mesh, m.Walnut());
            }

            foreach (Transform t in zone)
            {
                switch (t.name)
                {
                    case "Reception • recessed graphite kick":
                    {
                        // pulled 7 cm behind the desk front: the walnut body reads as floating above a shadow gap
                        Vector3 ks = InteriorShapes.PrimitiveSize(t, Vector3.one);
                        Mesh kick = InteriorShapes.ForObject(t, "Interior_Desk_RecessedKick", 1, true,
                            b => b.RoundedBox(new Vector3(0f, 0f, -0.035f), new Vector3(ks.x - 0.1f, ks.y, ks.z - 0.12f), 0f, 0));
                        ctx.Restyle(t, kick, m.BlackPowder());
                        break;
                    }
                    case "Reception • champagne reveal":
                    case "Reception • plaque lower metal line":
                        ctx.Restyle(t, null, m.PolishedSteel());
                        NoShadows(t);
                        break;
                    case "Reception • integrated warm light":
                        ctx.Restyle(t, null, m.LedWarm());
                        NoShadows(t);
                        break;
                    case "Reception • vertical walnut fluting":
                        ctx.Restyle(t, null, m.Walnut());
                        NoShadows(t);
                        break;
                }
            }

            Transform plaque = InteriorContext.Child(zone, "Reception • Aurelle brand plaque");
            if (plaque != null)
            {
                Vector3 size = InteriorShapes.PrimitiveSize(plaque, Vector3.one);
                Mesh mesh = InteriorShapes.ForObject(plaque, "Interior_Desk_BronzePlaque", 1, true, b => b.RoundedBox(Vector3.zero, size, 0.01f, 2));
                ctx.Restyle(plaque, mesh, m.DarkBronze());
            }

            // The individual battens become one slatted walnut wall behind the desk (it starts right of the wall's
            // left LED line and stops before display 1).
            ctx.RemoveChildren(zone, c => c.name == "Reception • oak wall batten");
            float x0 = -13.15f, x1 = -8.6f, z0 = -9.68f, z1 = -9.612f;
            float y0 = InteriorRealismUpgrade.FloorY, y1 = InteriorRealismUpgrade.CeilingY;
            Vector3 slatSize = new Vector3(x1 - x0, y1 - y0, z1 - z0);
            Vector3 slatCentre = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
            Mesh slats = InteriorMeshes.Build("Interior_Reception_WalnutSlatWall", 2, true,
                b => b.RoundedBox(Vector3.zero, slatSize, 0f, 0, new[] { 0, 0, 1, 1, 0, 1 }));
            GameObject wall = ctx.Part(zone, "Reception • walnut slat wall", slats, new[] { m.WalnutSlats(), m.BlackPowder() }, slatCentre, Quaternion.identity);
            InteriorContext.MarkStatic(wall, true);

            Pendants(ctx, zone, top);
            Accessories(ctx, zone, top);
        }

        // Thin trims hidden behind the desk do not need to be drawn into the shadow maps.
        private static void NoShadows(Transform t)
        {
            var r = t.GetComponent<MeshRenderer>();
            if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Desk front: 4 cm convex flutes across the visitor side, plain veneer on the other faces.</summary>
        private static void FlutedFront(InteriorMeshBuilder b, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            const float inset = 0.006f;
            // body: all faces except the front (+Z), pulled back by the flute depth
            b.RoundedBox(new Vector3(0f, 0f, -inset * 0.5f), new Vector3(size.x, size.y, size.z - inset), 0.004f, 1, new[] { 0, 0, 0, 0, -1, 0 });
            float zFace = h.z - inset;
            int flutes = Mathf.Max(8, Mathf.RoundToInt(size.x / 0.04f));
            float w = size.x / flutes;
            const int seg = 6;
            for (int f = 0; f < flutes; f++)
            {
                float xa = -h.x + f * w;
                var row0 = new List<int>();
                var row1 = new List<int>();
                for (int s = 0; s <= seg; s++)
                {
                    float t = (float)s / seg;
                    float a = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, t);
                    float x = xa + w * t;
                    float z = zFace + inset * Mathf.Cos(a);
                    Vector3 n = new Vector3(Mathf.Sin(a) * 0.8f, 0f, Mathf.Cos(a)).normalized;
                    row0.Add(b.Vertex(new Vector3(x, -h.y + 0.004f, z), n, new Vector2(x, -h.y)));
                    row1.Add(b.Vertex(new Vector3(x, h.y - 0.004f, z), n, new Vector2(x, h.y)));
                }
                for (int s = 0; s < seg; s++)
                {
                    // front faces must satisfy Cross(b - a, c - a) . +Z > 0
                    b.Triangle(0, row0[s], row1[s + 1], row1[s]);
                    b.Triangle(0, row0[s], row0[s + 1], row1[s + 1]);
                }
            }
        }

        /// <summary>Globe bottom height shared by every pendant (one long-cord mesh for all of them).</summary>
        internal const float PendantGlobeBottom = 2.3f;

        private static void Pendants(InteriorContext ctx, Transform zone, Transform top)
        {
            if (top == null) return;
            Bounds desk = top.GetComponent<Renderer>().bounds;
            Transform group = ctx.Ensure(zone, "Reception • pendant lights");
            group.position = Vector3.zero;
            PlacePendants(ctx, group, new[]
            {
                new Vector3(desk.center.x - 1.8f, 0f, desk.center.z), new Vector3(desk.center.x, 0f, desk.center.z), new Vector3(desk.center.x + 1.8f, 0f, desk.center.z),
            });
        }

        /// <summary>modern_ceiling_lamp_01 pendants with the cord lengthened to the 5.3 m ceiling (globe bottom at 2.3 m).</summary>
        internal static void PlacePendants(InteriorContext ctx, Transform group, Vector3[] positions)
        {
            float globeBottom = PendantGlobeBottom;
            const float modelBottom = 0.221f, modelTop = 1.173f; // modern_ceiling_lamp_01 extents (glTF metres)
            float originY = globeBottom - modelBottom;
            float extra = InteriorRealismUpgrade.CeilingY - (originY + modelTop);
            Mesh longLamp = null;
            for (int i = 0; i < positions.Length; i++)
            {
                Transform lamp = InteriorModels.Place(ctx, group, "Pendant " + (i + 1), "modern_ceiling_lamp_01", new Vector3(positions[i].x, originY, positions[i].z), 0f);
                if (lamp == null) continue;
                MeshFilter f = lamp.GetComponentInChildren<MeshFilter>(true);
                if (longLamp == null && f != null)
                {
                    Mesh src = SourceMesh(f);
                    if (src != null) longLamp = InteriorModels.Stretched(src, "Interior_PendantLamp_LongCord", 0.74f, extra);
                }
                if (longLamp != null) InteriorModels.SetMeshEverywhere(lamp, longLamp);
                // submeshes: glass globe, metal fittings, bulb
                foreach (MeshRenderer r in lamp.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.sharedMaterials = new[]
                    {
                        ctx.Mats.GlobeGlow(),
                        ctx.Mats.Model("modern_ceiling_lamp_01", "modern_ceiling_lamp_01", new Color(1f, 1f, 1f), 1f, 1f, 1f),
                        ctx.Mats.LedWarm(),
                    };
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                InteriorContext.MarkStatic(lamp.gameObject, false);
            }
        }

        private static Mesh SourceMesh(MeshFilter f)
        {
            Mesh mesh = f.sharedMesh;
            if (mesh == null) return null;
            if (mesh.name == "Interior_PendantLamp_LongCord")
            {
                GameObject src = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(InteriorAssets.ModelPath("modern_ceiling_lamp_01"));
                MeshFilter sf = src != null ? src.GetComponentInChildren<MeshFilter>(true) : null;
                return sf != null ? sf.sharedMesh : null;
            }
            return mesh;
        }

        private static void Accessories(InteriorContext ctx, Transform zone, Transform top)
        {
            if (top == null) return;
            Bounds desk = top.GetComponent<Renderer>().bounds;
            float y = desk.max.y;
            Transform group = ctx.Ensure(zone, "Reception • desk accessories");
            group.position = Vector3.zero;
            Transform aloe = InteriorModels.Place(ctx, group, "Desk aloe", "potted_plant_04", new Vector3(desk.min.x + 0.45f, y, desk.center.z - 0.12f), 30f);
            InteriorModels.Materials(aloe, n => ctx.Mats.Model("potted_plant_04", "potted_plant_04", new Color(1f, 1f, 1f), 0.9f, 1f, 1f));
            Transform vase = InteriorModels.Place(ctx, group, "Desk vase", "ceramic_vase_03", new Vector3(desk.max.x - 0.55f, y, desk.center.z - 0.2f), 15f);
            InteriorModels.Materials(vase, n => ctx.Mats.Model("ceramic_vase_03", "ceramic_vase_03", new Color(1.05f, 1.08f, 1.1f), 1f, 0.6f, 1f));
            foreach (Transform t in new[] { aloe, vase })
            {
                if (t == null) continue;
                InteriorContext.MarkStatic(t.gameObject, false);
                InteriorContext.CullBelow(t.gameObject, 0.012f);
            }
        }
    }
}
#endif
