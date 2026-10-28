#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AurelleRealism
{
    /// <summary>
    /// The pavilion's glazing, so it reads as glass from inside as well as from the plaza: the two side curtain walls,
    /// a glazed front around the entrance opening (the desktop walkthrough hangs its doors there at runtime), black
    /// silicone joints that split each bay into panes, frosted safety manifestation on every pane, and a fill
    /// reflection probe that gives the glass's inner faces the hall's reflection instead of the sky.
    /// </summary>
    internal static class InteriorGlazing
    {
        public const string ManifestationTexture = InteriorRealismUpgrade.TextureFolder + "/Interior_Glass_Manifestation.png";

        private const float GlassThickness = 0.12f;  // the side sections: unit cubes scaled to 0.12 m
        private const float GlassBottom = 0.05f, GlassTop = 5.05f;
        private const float MullionWidth = 0.2f, MullionDepth = 0.22f, CornerHalf = 0.11f;
        private const float JointWidth = 0.02f;      // structural silicone between panes
        private const float FilmGap = 0.012f;        // film edge to a joint or frame
        private const float FilmOffset = 0.0015f;    // film off the glass face
        private const float TileLength = 1.5f;       // one Aurelle mark per texture tile
        private const float BandHeight = 0.1875f;    // 256 texture rows at the tile's horizontal texel density
        // Manifestation zones above the floor (UK Approved Documents K and M: 850-1000 mm and 1400-1600 mm):
        // dots low, the Aurelle band high. The texture keeps the dots in its lower half and the band in its upper half.
        private const float DotsCentre = 0.94f, BandCentre = 1.5f;

        // DesktopWalkthroughController.BuildEntranceDoor hangs two 1.65 m leaves on pivots at x = 1 ± 1.65 m on z = 18,
        // under a 3.55 m header at 3.2-3.36 m. The jambs carry the pivots and the door head encloses the runtime
        // header, so the opening reads the same with the desktop doors and in XR, where there are none.
        private const float EntranceX = 1f, JambCentre = 1.69f, JambWidth = 0.1f, HeadWidth = 3.58f;
        private const float DoorHeadBottom = 3.19f, DoorHeadTop = 3.4f;

        private sealed class Run
        {
            public Vector3 Origin;   // on the glass mid-plane at y = 0; positions s are measured from here
            public Vector3 Along;    // unit, horizontal
            public Vector3 Inward;   // unit, horizontal, towards the hall
            public readonly List<Vector2> Dividers = new List<Vector2>(); // (s, half width) of frames and joints, sorted
        }

        public static void Apply(InteriorContext ctx)
        {
            Transform architecture = ctx.Find("Showroom Environment/Showroom Architecture");
            if (architecture == null) return;
            var m = ctx.Mats;
            EnsureManifestationTexture();

            // Existing side walls: four glass sections and five mullions per wall, a transom at 2.3 m and a sill.
            float westX = float.NaN, eastX = float.NaN;
            var mullionZ = new List<Vector2>(); // (x, z)
            foreach (Transform t in architecture)
            {
                string n = t.name;
                if (n.StartsWith("Smoked Glass Wall Section", StringComparison.Ordinal))
                {
                    ctx.Restyle(t, null, m.ClearGlass());
                    var r = t.GetComponent<MeshRenderer>();
                    if (r != null) r.shadowCastingMode = ShadowCastingMode.Off;
                    if (n.StartsWith("Smoked Glass Wall Section 0", StringComparison.Ordinal)) westX = t.position.x;
                    else if (n.StartsWith("Smoked Glass Wall Section 1", StringComparison.Ordinal)) eastX = t.position.x;
                }
                else if (n.StartsWith("Graphite Glass Mullion", StringComparison.Ordinal))
                {
                    ctx.Restyle(t, null, m.BlackPowder());
                    mullionZ.Add(new Vector2(t.position.x, t.position.z));
                }
                else if (n.StartsWith("Glazed Wall Horizontal Transom", StringComparison.Ordinal) || n.StartsWith("Graphite Side Sill", StringComparison.Ordinal))
                    ctx.Restyle(t, null, m.BlackPowder());
            }
            if (float.IsNaN(westX) || float.IsNaN(eastX) || mullionZ.Count < 4)
            {
                InteriorRealismUpgrade.Log("glazing: side walls not found, details skipped");
                return;
            }
            var west = new Run { Origin = new Vector3(westX, 0f, 0f), Along = Vector3.forward, Inward = Vector3.right };
            var east = new Run { Origin = new Vector3(eastX, 0f, 0f), Along = Vector3.forward, Inward = Vector3.left };
            float frontZ = float.MinValue;
            foreach (Vector2 p in mullionZ)
            {
                Run run = Mathf.Abs(p.x - westX) < Mathf.Abs(p.x - eastX) ? west : east;
                run.Dividers.Add(new Vector2(p.y, MullionWidth * 0.5f));
                frontZ = Mathf.Max(frontZ, p.y);
            }
            AddJoints(west);
            AddJoints(east);

            // Glazed front on the corner mullions' line, open between the entrance jambs.
            var frontLeft = new Run { Origin = new Vector3(0f, 0f, frontZ), Along = Vector3.right, Inward = Vector3.back };
            var frontRight = new Run { Origin = frontLeft.Origin, Along = Vector3.right, Inward = Vector3.back };
            float jambL = EntranceX - JambCentre, jambR = EntranceX + JambCentre;
            frontLeft.Dividers.Add(new Vector2(westX, CornerHalf));
            frontLeft.Dividers.Add(new Vector2((westX + jambL) * 0.5f, MullionWidth * 0.5f));
            frontLeft.Dividers.Add(new Vector2(jambL, JambWidth * 0.5f));
            frontRight.Dividers.Add(new Vector2(jambR, JambWidth * 0.5f));
            frontRight.Dividers.Add(new Vector2((jambR + eastX) * 0.5f, MullionWidth * 0.5f));
            frontRight.Dividers.Add(new Vector2(eastX, CornerHalf));
            Front(ctx, architecture, frontLeft, frontRight, frontZ, westX, eastX, jambL, jambR);
            AddJoints(frontLeft);
            AddJoints(frontRight);

            GameObject env = ctx.Root("Showroom Environment");
            Transform finishes = ctx.Ensure(env.transform, "Showroom Interior Finishes");
            Transform group = ctx.Ensure(finishes, "Glazing details");
            group.localPosition = Vector3.zero; group.localRotation = Quaternion.identity; group.localScale = Vector3.one;
            Details(ctx, group, "West", new[] { west });
            Details(ctx, group, "East", new[] { east });
            Details(ctx, group, "Front", new[] { frontLeft, frontRight });
            FillProbe(ctx, group, westX, eastX, frontZ);
            InteriorRealismUpgrade.Log("glazing: side walls at x " + westX + " / " + eastX + ", glazed front at z " + frontZ + ", entrance jambs at x " + jambL + " / " + jambR);
        }

        // Two silicone joints per frame bay: 9 m bays become three panes of about 2.9 m, as large toughened panes are made.
        private static void AddJoints(Run run)
        {
            run.Dividers.Sort((a, b) => a.x.CompareTo(b.x));
            var frames = new List<Vector2>(run.Dividers);
            for (int i = 0; i + 1 < frames.Count; i++)
            {
                float a = frames[i].x, b = frames[i + 1].x;
                for (int k = 1; k <= 2; k++) run.Dividers.Add(new Vector2(Mathf.Lerp(a, b, k / 3f), JointWidth * 0.5f));
            }
            run.Dividers.Sort((a, b) => a.x.CompareTo(b.x));
        }

        // ------------------------------------------------------------------ glazed front

        private static void Front(InteriorContext ctx, Transform architecture, Run left, Run right, float z, float westX, float eastX, float jambL, float jambR)
        {
            var m = ctx.Mats;
            float glassH = GlassTop - GlassBottom, glassY = (GlassTop + GlassBottom) * 0.5f;
            int bay = 0;
            foreach (Run run in new[] { left, right })
            {
                // Two equal bays per run (the mullion sits halfway); each pane runs 3.5 cm into the frames on both
                // sides, like the side sections.
                int k = run == left ? 0 : 1;
                float bayLength = run.Dividers[1].x - run.Dividers[0].x;
                Mesh pane = InteriorMeshes.Box("Interior_FrontGlass_Pane_" + k, new Vector3(bayLength - 0.07f, glassH, GlassThickness), 0f, 0, null, 1, false);
                for (int i = 0; i + 1 < run.Dividers.Count; i++)
                    Glass(ctx, architecture, "Front Glass Wall Section " + bay++, pane, new Vector3((run.Dividers[i].x + run.Dividers[i + 1].x) * 0.5f, glassY, z));
                // Intermediate mullion, transom at the side walls' 2.3 m and sill, as on the side walls. The transom
                // ends in the corner post; the sill runs to the side sill's outer face so the corner closes.
                Frame(ctx, architecture, "Front Glass Mullion " + k, "Interior_FrontMullion",
                    new Vector3(MullionWidth, 5.4f, MullionDepth), new Vector3(run.Dividers[1].x, 2.7f, z), m.BlackPowder());
                float t0 = run == left ? westX + 0.1f : jambR + JambWidth * 0.5f, t1 = run == left ? jambL - JambWidth * 0.5f : eastX - 0.1f;
                Frame(ctx, architecture, "Front Glazed Wall Transom " + k, "Interior_FrontTransom_" + k,
                    new Vector3(t1 - t0, 0.14f, 0.2f), new Vector3((t0 + t1) * 0.5f, 2.3f, z), m.BlackPowder());
                float s0 = run == left ? westX - 0.16f : jambR + JambWidth * 0.5f, s1 = run == left ? jambL - JambWidth * 0.5f : eastX + 0.16f;
                Frame(ctx, architecture, "Front Graphite Sill " + k, "Interior_FrontSill_" + k,
                    new Vector3(s1 - s0, 0.42f, 0.32f), new Vector3((s0 + s1) * 0.5f, 0.32f, z), m.BlackPowder());
            }

            // Entrance opening: jambs, a door head that encloses the runtime header, and a glass transom above it.
            foreach (float x in new[] { jambL, jambR })
                Frame(ctx, architecture, x < EntranceX ? "Entrance Door Jamb Left" : "Entrance Door Jamb Right", "Interior_EntranceJamb",
                    new Vector3(JambWidth, GlassTop, 0.2f), new Vector3(x, GlassTop * 0.5f, z), m.BlackPowder());
            Frame(ctx, architecture, "Entrance Door Head", "Interior_EntranceHead",
                new Vector3(HeadWidth, DoorHeadTop - DoorHeadBottom, 0.22f), new Vector3(EntranceX, (DoorHeadTop + DoorHeadBottom) * 0.5f, z), m.BlackPowder());
            float transomW = (jambR - jambL) - JambWidth + 0.04f, transomH = GlassTop - DoorHeadTop + 0.02f;
            Mesh transom = InteriorMeshes.Box("Interior_FrontGlass_DoorTransom", new Vector3(transomW, transomH, GlassThickness), 0f, 0, null, 1, false);
            Glass(ctx, architecture, "Entrance Transom Glass", transom, new Vector3(EntranceX, DoorHeadTop - 0.02f + transomH * 0.5f, z));
        }

        private static void Glass(InteriorContext ctx, Transform parent, string name, Mesh mesh, Vector3 position)
        {
            GameObject go = ctx.Part(parent, name, mesh, new[] { ctx.Mats.ClearGlass() }, position, Quaternion.identity, false);
            // Transparent: out of GI, but captured by the reflection probes (as the render setup leaves the side glass).
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ReflectionProbeStatic);
            go.GetComponent<MeshRenderer>().receiveGI = ReceiveGI.LightProbes;
        }

        private static void Frame(InteriorContext ctx, Transform parent, string name, string meshName, Vector3 size, Vector3 position, Material material)
        {
            Mesh mesh = InteriorMeshes.Box(meshName, size, 0f, 0, null, 1, true);
            GameObject go = ctx.Part(parent, name, mesh, new[] { material }, position, Quaternion.identity, true);
            InteriorContext.MarkStatic(go, true);
        }

        // ------------------------------------------------------------------ joints and manifestation

        private static void Details(InteriorContext ctx, Transform group, string side, Run[] runs)
        {
            var m = ctx.Mats;
            float jointH = GlassTop - GlassBottom, jointY = (GlassTop + GlassBottom) * 0.5f;
            Mesh joints = InteriorMeshes.Build("Interior_Glazing_Joints_" + side, 1, false, b =>
            {
                foreach (Run run in runs)
                    foreach (Vector2 d in run.Dividers)
                    {
                        if (d.y > JointWidth) continue; // frames are separate objects
                        Vector3 c = run.Origin + run.Along * d.x + Vector3.up * jointY;
                        Vector3 size = Abs(run.Along * JointWidth + run.Inward * (GlassThickness + 0.003f)) + Vector3.up * jointH;
                        b.RoundedBox(c, size, 0f, 0);
                    }
            });
            GameObject j = ctx.Part(group, side + " glazing joints", joints, new[] { m.GlassSilicone() }, Vector3.zero, Quaternion.identity, false);
            InteriorContext.MarkStatic(j, false);

            const float floor = InteriorRealismUpgrade.FloorY;
            Mesh films = InteriorMeshes.Build("Interior_Glazing_Manifestation_" + side, 1, false, b =>
            {
                foreach (Run run in runs)
                    for (int i = 0; i + 1 < run.Dividers.Count; i++)
                    {
                        float s0 = run.Dividers[i].x + run.Dividers[i].y + FilmGap, s1 = run.Dividers[i + 1].x - run.Dividers[i + 1].y - FilmGap;
                        if (s1 - s0 < 0.3f) continue;
                        Film(b, run, s0, s1, floor + DotsCentre, 0f);
                        Film(b, run, s0, s1, floor + BandCentre, 0.5f);
                    }
            });
            GameObject f = ctx.Part(group, side + " glazing manifestation", films, new[] { m.GlassManifestation() }, Vector3.zero, Quaternion.identity, false);
            GameObjectUtility.SetStaticEditorFlags(f, StaticEditorFlags.ReflectionProbeStatic);
            f.GetComponent<MeshRenderer>().receiveGI = ReceiveGI.LightProbes;
        }

        // One film strip on each face of the pane between s0 and s1, centred at height y. The tiles fit the pane exactly
        // (at most a few per cent stretch), and each face's U runs left to right as seen from that side, so the mark
        // reads the right way round from inside and from outside.
        private static void Film(InteriorMeshBuilder b, Run run, float s0, float s1, float y, float v0)
        {
            int tiles = Mathf.Max(1, Mathf.RoundToInt((s1 - s0) / TileLength));
            float half = GlassThickness * 0.5f + FilmOffset;
            foreach (float side in new[] { 1f, -1f })
            {
                Vector3 normal = run.Inward * side;
                Vector3 right = Vector3.Cross(normal, Vector3.up); // viewer's right, looking at this face
                Vector3 c = run.Origin + run.Along * ((s0 + s1) * 0.5f) + normal * half + Vector3.up * y;
                float w = (s1 - s0) * 0.5f, h = BandHeight * 0.5f;
                Vector3 r = right * w, u = Vector3.up * h;
                b.Quad(0, c - r - u, c - r + u, c + r + u, c + r - u, normal,
                    new Vector2(0f, v0), new Vector2(0f, v0 + 0.5f), new Vector2(tiles, v0 + 0.5f), new Vector2(tiles, v0));
            }
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        // ------------------------------------------------------------------ reflections

        // The hall's box-projected probe ends at the glass's inner faces and fades over its 1 m blend distance, so the
        // glass and everything near the walls fell back to the sky reflection: from inside the glass reflected nothing
        // of the room. This probe is captured from the same point (same content) with the box out to the glass
        // mid-planes and a 3 cm blend: it sorts after the hall probe (lower importance), fills only the weight the hall
        // probe leaves, and stops between the glass faces, so the outer faces keep the sky.
        private static void FillProbe(InteriorContext ctx, Transform group, float westX, float eastX, float frontZ)
        {
            Transform hall = ctx.Find("Showroom Lighting/Showroom Reflection Probe");
            ReflectionProbe main = hall != null ? hall.GetComponent<ReflectionProbe>() : null;
            if (main == null)
            {
                InteriorRealismUpgrade.Log("glazing: 'Showroom Reflection Probe' missing, fill probe skipped");
                return;
            }
            Transform t = ctx.Ensure(group, "Glazing Reflection Probe");
            ReflectionProbe p = InteriorContext.GetOrAdd<ReflectionProbe>(t.gameObject);
            Vector3 min = hall.position + main.center - main.size * 0.5f, max = hall.position + main.center + main.size * 0.5f;
            min.x = Mathf.Min(min.x, westX);
            max.x = Mathf.Max(max.x, eastX);
            max.z = Mathf.Max(max.z, frontZ);
            t.position = hall.position;
            t.rotation = Quaternion.identity;
            t.localScale = Vector3.one;
            p.mode = ReflectionProbeMode.Baked;
            p.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            p.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            p.center = (min + max) * 0.5f - hall.position;
            p.size = max - min;
            p.boxProjection = true;
            p.importance = Mathf.Max(0, main.importance - 1);
            p.blendDistance = 0.03f;
            p.resolution = main.resolution;
            p.hdr = main.hdr;
            p.intensity = main.intensity;
            p.clearFlags = main.clearFlags;
            p.backgroundColor = main.backgroundColor;
            p.cullingMask = main.cullingMask;
            p.shadowDistance = main.shadowDistance;
            p.nearClipPlane = main.nearClipPlane;
            p.farClipPlane = main.farClipPlane;
            EditorUtility.SetDirty(p);
        }

        // ------------------------------------------------------------------ manifestation texture

        // 2048 x 512 RGBA, one 1.5 m tile (1365 texels per metre both ways): lower half three staggered rows of 12 mm
        // frosted dots at 20 mm, upper half a 100 mm frosted band with the AURELLE mark (44 mm capitals) left clear in
        // its middle. White, coverage in alpha; rewritten only when the pixels change, so the asset and its GUID stay
        // stable.
        private static void EnsureManifestationTexture()
        {
            const int w = 2048, h = 512;
            var alpha = new float[w * h];

            const int dotsPerTile = 75;
            float pitch = w / (float)dotsPerTile, radius = 8.2f;
            for (int row = -1; row <= 1; row++)
            {
                float cy = 128f + row * pitch, shift = row == 0 ? pitch * 0.5f : 0f;
                for (int i = 0; i < dotsPerTile; i++)
                {
                    float cx = shift + pitch * (i + 0.5f);
                    for (int y = Mathf.FloorToInt(cy - radius - 1f); y <= Mathf.CeilToInt(cy + radius + 1f); y++)
                        for (int x = Mathf.FloorToInt(cx - radius - 1f); x <= Mathf.CeilToInt(cx + radius + 1f); x++)
                        {
                            float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                            int xi = ((x % w) + w) % w;
                            alpha[y * w + xi] = Mathf.Max(alpha[y * w + xi], Mathf.Clamp01(radius - d + 0.5f));
                        }
                }
            }

            const float bandBottom = 384f - 68f, bandTop = 384f + 68f, cap = 60f;
            for (int y = (int)bandBottom - 1; y <= (int)bandTop + 1; y++)
            {
                float edge = Mathf.Clamp01(Mathf.Min(y + 0.5f - bandBottom, bandTop - (y + 0.5f)) + 0.5f);
                for (int x = 0; x < w; x++) alpha[y * w + x] = edge;
            }
            float word = MarkWidth();
            float x0 = w * 0.5f - word * cap * 0.5f, y0 = 384f - cap * 0.5f;
            for (int y = (int)y0 - 4; y <= (int)(y0 + cap) + 4; y++)
                for (int x = (int)x0 - 4; x <= (int)(x0 + word * cap) + 4; x++)
                {
                    float letter = MarkCoverage((x + 0.5f - x0) / cap, (y + 0.5f - y0) / cap, cap);
                    alpha[y * w + x] *= 1f - letter;
                }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha[i] * 255f));
            tex.SetPixels32(px);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);

            string full = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", ManifestationTexture);
            bool same = File.Exists(full) && SameBytes(File.ReadAllBytes(full), png);
            if (!same)
            {
                File.WriteAllBytes(full, png);
                AssetDatabase.ImportAsset(ManifestationTexture, ImportAssetOptions.ForceUpdate);
            }
            ConfigureFilmTexture();
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static void ConfigureFilmTexture()
        {
            var ti = AssetImporter.GetAtPath(ManifestationTexture) as TextureImporter;
            if (ti == null) { InteriorRealismUpgrade.Log("glazing: manifestation texture did not import"); return; }
            bool changed = false;
            void Set<T>(T current, T wanted, Action<T> apply)
            {
                if (EqualityComparer<T>.Default.Equals(current, wanted)) return;
                apply(wanted);
                changed = true;
            }
            Set(ti.textureType, TextureImporterType.Default, v => ti.textureType = v);
            Set(ti.sRGBTexture, true, v => ti.sRGBTexture = v);
            Set(ti.alphaSource, TextureImporterAlphaSource.FromInput, v => ti.alphaSource = v);
            Set(ti.alphaIsTransparency, true, v => ti.alphaIsTransparency = v);
            Set(ti.mipmapEnabled, true, v => ti.mipmapEnabled = v);
            Set(ti.wrapModeU, TextureWrapMode.Repeat, v => ti.wrapModeU = v);
            Set(ti.wrapModeV, TextureWrapMode.Clamp, v => ti.wrapModeV = v);
            Set(ti.filterMode, FilterMode.Trilinear, v => ti.filterMode = v);
            Set(ti.anisoLevel, 8, v => ti.anisoLevel = v);
            Set(ti.maxTextureSize, 2048, v => ti.maxTextureSize = v);
            Set(ti.textureCompression, TextureImporterCompression.CompressedHQ, v => ti.textureCompression = v);
            foreach (var (platform, max, quality) in new[] { ("Standalone", 2048, TextureImporterCompression.CompressedHQ), ("Android", 1024, TextureImporterCompression.Compressed) })
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

        // AURELLE in a light geometric capital, stroke centre lines in cap-height units (x from each letter's left).
        private const float Stroke = 0.12f, Tracking = 0.34f;

        private static readonly (float width, float[][] strokes)[] Letters =
        {
            (0.74f, new[] { new[] { 0.05f, 0f, 0.37f, 0.97f, 0.69f, 0f }, new[] { 0.17f, 0.33f, 0.57f, 0.33f } }),          // A
            (0.62f, new[] { Arc(0.06f, 1f, 0.31f, 0.32f, 0.25f, 180f, 360f, 0.56f, 1f) }),                                   // U
            (0.6f, new[] { new[] { 0.06f, 0f, 0.06f, 1f }, Arc(0.06f, 0.94f, 0.33f, 0.72f, 0.22f, 90f, -90f, 0.06f, 0.5f), new[] { 0.3f, 0.5f, 0.58f, 0f } }), // R
            (0.52f, new[] { new[] { 0.5f, 0.94f, 0.06f, 0.94f, 0.06f, 0.06f, 0.5f, 0.06f }, new[] { 0.06f, 0.5f, 0.44f, 0.5f } }), // E
            (0.48f, new[] { new[] { 0.06f, 1f, 0.06f, 0.06f, 0.46f, 0.06f } }),                                               // L
            (0.48f, new[] { new[] { 0.06f, 1f, 0.06f, 0.06f, 0.46f, 0.06f } }),                                               // L
            (0.52f, new[] { new[] { 0.5f, 0.94f, 0.06f, 0.94f, 0.06f, 0.06f, 0.5f, 0.06f }, new[] { 0.06f, 0.5f, 0.44f, 0.5f } }), // E
        };

        // Polyline: start point, an arc around (cx, cy) from a0 to a1 degrees, end point.
        private static float[] Arc(float sx, float sy, float cx, float cy, float r, float a0, float a1, float ex, float ey)
        {
            var pts = new List<float> { sx, sy };
            const int steps = 16;
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(a0, a1, i / (float)steps);
                pts.Add(cx + r * Mathf.Cos(a));
                pts.Add(cy + r * Mathf.Sin(a));
            }
            pts.Add(ex);
            pts.Add(ey);
            return pts.ToArray();
        }

        private static float MarkWidth()
        {
            float wsum = 0f;
            foreach (var l in Letters) wsum += l.width;
            return wsum + Tracking * (Letters.Length - 1);
        }

        // Anti-aliased coverage of the mark at (x, y) in cap units; px = pixels per cap height. Clipped to the cap box.
        private static float MarkCoverage(float x, float y, float px)
        {
            float best = float.MaxValue, left = 0f;
            foreach (var l in Letters)
            {
                if (x > left - Stroke && x < left + l.width + Stroke)
                    foreach (float[] s in l.strokes)
                        for (int i = 0; i + 3 < s.Length; i += 2)
                            best = Mathf.Min(best, SegmentDistance(x - left, y, s[i], s[i + 1], s[i + 2], s[i + 3]));
                left += l.width + Tracking;
            }
            float c = Mathf.Clamp01((Stroke * 0.5f - best) * px + 0.5f);
            return c * Mathf.Clamp01(y * px + 0.5f) * Mathf.Clamp01((1f - y) * px + 0.5f);
        }

        private static float SegmentDistance(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float len2 = dx * dx + dy * dy;
            float t = len2 > 1e-8f ? Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / len2) : 0f;
            float ex = px - (ax + t * dx), ey = py - (ay + t * dy);
            return Mathf.Sqrt(ex * ex + ey * ey);
        }
    }
}
#endif
