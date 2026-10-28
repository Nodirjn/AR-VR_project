#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AurelleRealism
{
    /// <summary>
    /// Customer lounge: modelled modern sofa (fabric), two Poly Haven armchairs, round marble coffee table, oak side
    /// table, wool rug, money-tree plants in fibre-cement planters. Everything stays under
    /// 'Showroom Interior Zones/Customer Lounge Zone', so ShowroomCollisionSetup gives the pieces box colliders.
    /// 'Sofa • seat cushion' and 'Coffee table • rounded stone top' keep their names (gameplay checks).
    /// </summary>
    internal static class InteriorLounge
    {
        private const string Zone = "Showroom Interior Zones/Customer Lounge Zone";
        private static readonly string[] OldPrefixes = { "Sofa • ", "Armchair 1 • ", "Armchair 2 • ", "Coffee table • ", "Side table • ", "Indoor ficus ", "Lounge • rug border" };

        public static void Apply(InteriorContext ctx)
        {
            Transform zone = ctx.Find(Zone);
            if (zone == null) { InteriorRealismUpgrade.Log("Customer Lounge Zone missing"); return; }
            var m = ctx.Mats;
            float floor = InteriorRealismUpgrade.FloorY;
            // only the original primitive-built pieces go; new objects that reuse a checked name are kept
            ctx.RemoveChildren(zone, c => Array.Exists(OldPrefixes, p => c.name.StartsWith(p, StringComparison.Ordinal)) && InteriorContext.IsBuiltinPrimitive(c));

            // rug
            const float rugThickness = 0.012f;
            float rugTop = floor + rugThickness;
            Transform rug = InteriorContext.Child(zone, "Lounge • woven area rug");
            if (rug != null)
            {
                rug.position = new Vector3(-12.6f, floor + rugThickness * 0.5f, 0f);
                rug.rotation = Quaternion.identity;
                rug.localScale = Vector3.one;
                Mesh mesh = InteriorMeshes.Box("Interior_Lounge_WoolRug", new Vector3(4.6f, rugThickness, 5.0f), 0.004f, 1);
                ctx.Restyle(rug, mesh, m.Rug());
                var rr = rug.GetComponent<MeshRenderer>();
                rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                InteriorContext.MarkStatic(rug.gameObject, true);
            }

            Sofa(ctx, zone, new Vector3(-14.35f, floor, 0f));
            Transform lamps = ctx.Ensure(zone, "Lounge pendant lights");
            lamps.position = Vector3.zero;
            InteriorReception.PlacePendants(ctx, lamps, new[] { new Vector3(-13.0f, 0f, -0.32f), new Vector3(-12.5f, 0f, 0.3f) });

            // armchairs face each other across the table, opened 15 degrees towards the showroom
            Material legs = m.Model("modern_arm_chair_01", "modern_arm_chair_01_legs", new Color(0.92f, 0.86f, 0.8f), 0.9f, 1f);
            // The scanned leather is worn (scuffs, stains): keep only its normal map on a clean, uniform black leather.
            Material leather = m.Get("PH_modern_arm_chair_01_pillow_clean", new InteriorLitDef
            {
                BaseColor = new Color(0.032f, 0.03f, 0.029f), Smoothness = 0.52f,
                NormalMap = InteriorRealismUpgrade.PolyHaven + "/modern_arm_chair_01/modern_arm_chair_01_pillow_nor_gl_1k.jpg", NormalScale = 0.6f,
            });
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Transform chair = InteriorModels.Place(ctx, zone, "Lounge armchair " + (i + 1), "modern_arm_chair_01",
                    new Vector3(-12.75f, rugTop, side * 1.62f), i == 0 ? 15f : 165f);
                InteriorModels.Materials(chair, n => n.EndsWith("legs", StringComparison.Ordinal) ? legs : leather);
                if (chair != null) InteriorContext.MarkStatic(chair.gameObject, false);
            }

            // coffee table (white marble top, chrome base); the single-node model keeps the checked name
            Transform table = InteriorModels.Place(ctx, zone, "Coffee table • rounded stone top", "coffee_table_round_01", new Vector3(-12.75f, rugTop, 0f), 0f);
            if (table != null)
            {
                InteriorModels.Materials(table, n => m.Model("coffee_table_round_01", "coffee_table_round_01", new Color(1.08f, 1.08f, 1.08f), 1f, 0.8f));
                InteriorContext.MarkStatic(table.gameObject, false);
                float tableTop = rugTop + 0.49f;
                Transform vase = InteriorModels.Place(ctx, zone, "Lounge • coffee table vase", "ceramic_vase_01", new Vector3(-12.55f, tableTop, -0.22f), 20f);
                InteriorModels.Materials(vase, n => m.Model("ceramic_vase_01", "ceramic_vase_01", new Color(1f, 1f, 1f), 1f, 0.5f));
                if (vase != null) { InteriorContext.MarkStatic(vase.gameObject, false); InteriorContext.CullBelow(vase.gameObject, 0.015f); }
            }

            Transform sideTable = InteriorModels.Place(ctx, zone, "Lounge side table", "side_table_01", new Vector3(-14.45f, floor, 1.6f), 90f);
            if (sideTable != null)
            {
                InteriorModels.Materials(sideTable, n => m.Model("side_table_01", "side_table_01", new Color(1f, 0.97f, 0.94f), 0.9f, 1f));
                InteriorContext.MarkStatic(sideTable.gameObject, false);
                Transform vase = InteriorModels.Place(ctx, zone, "Lounge • side table vase", "ceramic_vase_03", new Vector3(-14.5f, floor + 0.548f, 1.62f), 10f);
                InteriorModels.Materials(vase, n => m.Model("ceramic_vase_03", "ceramic_vase_03", new Color(0.32f, 0.31f, 0.3f), 1f, 0.6f));
                if (vase != null) { InteriorContext.MarkStatic(vase.gameObject, false); InteriorContext.CullBelow(vase.gameObject, 0.015f); }
            }

            // plants: pachira variants d (1.9 m), a (1.3 m) and c (1.15 m) in round fibre-cement planters
            Plant(ctx, zone, "Lounge plant tall", "_d", new Vector3(-16.45f, floor, -2.0f), 0.34f, 0.52f, 40f, 1.25f);
            Plant(ctx, zone, "Lounge plant medium", "_a", new Vector3(-16.45f, floor, 2.45f), 0.29f, 0.55f, 200f, 1.2f);
            Transform reception = ctx.Find("Showroom Interior Zones/Reception Zone");
            Plant(ctx, reception != null ? reception : zone, "Reception plant", "_c", new Vector3(-14.05f, floor, -8.05f), 0.3f, 0.46f, 120f, 1.15f);
        }

        private static void Sofa(InteriorContext ctx, Transform zone, Vector3 origin)
        {
            var m = ctx.Mats;
            Transform sofa = ctx.Ensure(zone, "Lounge sofa");
            sofa.position = origin;
            sofa.rotation = Quaternion.identity;
            sofa.localScale = Vector3.one;
            Material fabric = m.SofaFabric(), pillow = m.PillowFabric(), black = m.BlackPowder();
            void Piece(string name, string meshName, Vector3 localCentre, Action<InteriorMeshBuilder> fill, Material mat, bool shadows = true)
            {
                Mesh mesh = InteriorMeshes.Build(meshName, 1, true, fill);
                ctx.Part(sofa, name, mesh, new[] { mat }, sofa.TransformPoint(localCentre), sofa.rotation, shadows);
            }
            // +x is the front of the sofa, z its length
            Piece("Sofa • legs", "Interior_Sofa_Legs", new Vector3(0f, 0.05f, 0f), b =>
            {
                foreach (float x in new[] { -0.4f, 0.4f })
                foreach (float z in new[] { -1.15f, 1.15f })
                    b.RoundedBox(new Vector3(x, 0f, z), new Vector3(0.035f, 0.1f, 0.035f), 0.004f, 1);
            }, black, false);
            Piece("Sofa • base frame", "Interior_Sofa_BaseFrame", new Vector3(0f, 0.2f, 0f),
                b => b.RoundedBox(Vector3.zero, new Vector3(0.95f, 0.2f, 2.24f), 0.025f, 3), fabric);
            Piece("Sofa • upholstered arm L", "Interior_Sofa_Arm", new Vector3(0f, 0.36f, -1.185f),
                b => b.RoundedBox(Vector3.zero, new Vector3(0.95f, 0.52f, 0.13f), 0.045f, 3, null, 0.004f), fabric);
            Piece("Sofa • upholstered arm R", "Interior_Sofa_Arm", new Vector3(0f, 0.36f, 1.185f),
                b => b.RoundedBox(Vector3.zero, new Vector3(0.95f, 0.52f, 0.13f), 0.045f, 3, null, 0.004f), fabric);
            Piece("Sofa • back panel", "Interior_Sofa_BackPanel", new Vector3(-0.3775f, 0.5f, 0f),
                b => b.RoundedBox(Vector3.zero, new Vector3(0.195f, 0.4f, 2.24f), 0.035f, 3), fabric);
            Piece("Sofa • seat cushion", "Interior_Sofa_SeatCushions", new Vector3(0.095f, 0.375f, 0f), b =>
            {
                foreach (float z in new[] { -0.5625f, 0.5625f })
                    b.RoundedBox(new Vector3(0f, 0f, z), new Vector3(0.75f, 0.15f, 1.112f), 0.055f, 3, null, 0.014f);
            }, fabric);
            Piece("Sofa • back cushion", "Interior_Sofa_BackCushions", new Vector3(-0.19f, 0.64f, 0f), b =>
            {
                foreach (float z in new[] { -0.5625f, 0.5625f })
                    b.RoundedBox(new Vector3(0f, 0f, z), new Vector3(0.2f, 0.44f, 1.112f), 0.07f, 3, null, 0.012f, Quaternion.Euler(0f, 0f, 9f));
            }, fabric);
            Piece("Sofa • throw pillow L", "Interior_Sofa_ThrowPillow", new Vector3(-0.07f, 0.63f, -0.88f),
                b => b.RoundedBox(Vector3.zero, new Vector3(0.13f, 0.42f, 0.42f), 0.065f, 3, null, 0.03f, Quaternion.Euler(-8f, 18f, 12f)), pillow);
            Piece("Sofa • throw pillow R", "Interior_Sofa_ThrowPillowR", new Vector3(-0.07f, 0.63f, 0.88f),
                b => b.RoundedBox(Vector3.zero, new Vector3(0.13f, 0.42f, 0.42f), 0.065f, 3, null, 0.03f, Quaternion.Euler(8f, -18f, 12f)), pillow);
            InteriorContext.MarkStatic(sofa.gameObject, false);
        }

        private static void Plant(InteriorContext ctx, Transform parent, string name, string variant, Vector3 floorPosition, float radius, float height, float yaw, float plantScale)
        {
            var m = ctx.Mats;
            Transform group = ctx.Ensure(parent, name);
            group.position = floorPosition;
            group.rotation = Quaternion.Euler(0f, yaw, 0f);
            group.localScale = Vector3.one;
            float soil = height - 0.04f;
            Mesh planter = InteriorMeshes.Build("Interior_Planter_R" + Mathf.RoundToInt(radius * 100f) + "_H" + Mathf.RoundToInt(height * 100f), 2, true, b =>
            {
                float rb = radius * 0.86f, rt = radius;
                b.Lathe(Vector3.zero, new List<Vector2>
                {
                    new Vector2(0f, 0.004f), new Vector2(rb - 0.01f, 0.004f), new Vector2(rb, 0.014f), new Vector2(rt, height - 0.012f),
                    new Vector2(rt - 0.006f, height), new Vector2(rt - 0.022f, height), new Vector2(rt - 0.026f, height - 0.006f), new Vector2(rt - 0.026f, soil),
                }, 48, 0);
                b.Lathe(Vector3.zero, new List<Vector2> { new Vector2(rt - 0.026f, soil), new Vector2(0f, soil) }, 48, 1);
            });
            ctx.Part(group, name + " • planter", planter, new[] { m.Planter(), m.Soil() }, group.position, group.rotation);
            Transform plant = InteriorModels.Place(ctx, group, name + " • money tree", "pachira_aquatica_01", group.position + Vector3.up * (soil - 0.01f), yaw,
                n => n.EndsWith(variant, StringComparison.Ordinal));
            if (plant != null)
            {
                plant.localScale = Vector3.one * plantScale;
                Material bark = m.Model("pachira_aquatica_01", "pachira_aquatica_01_bark", new Color(1f, 1f, 1f), 0.8f, 1f);
                Material leaves = m.Model("pachira_aquatica_01", "pachira_aquatica_01_leaves", new Color(1.05f, 1.08f, 1f), 1f, 1f);
                foreach (MeshRenderer r in plant.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.name.Contains("leaves")) r.sharedMaterials = new[] { leaves };
                    else r.sharedMaterials = new[] { bark };
                }
            }
            InteriorContext.MarkStatic(group.gameObject, false);
            InteriorContext.CullBelow(group.gameObject, 0.012f);
        }
    }
}
#endif
