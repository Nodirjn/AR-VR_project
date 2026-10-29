#if UNITY_EDITOR || CI_TEXTURE_PREVIEW
using System;
using System.Threading.Tasks;

namespace CarInternals
{
    /// <summary>
    /// Pure C# synthesis of the internals' tileable surface maps (no Unity types, so it also runs outside the Editor for
    /// previews). A surface is a function of the tile coordinates (u, v in 0..1, periodic) that gives a height, a linear
    /// colour factor around 1 and a smoothness factor (the opacity for the reservoir). The noise is periodic gradient
    /// noise and cellular noise on integer lattices seeded by integer hashes, so every run gives the same bytes and
    /// every tile edge wraps exactly. Directions follow the mesh kit's UVs: lathes and tubes have u around and v along
    /// the axis or path, sweeps have u around the section and v along the path.
    /// </summary>
    public static class CarTextureSynth
    {
        public sealed class Result
        {
            public string Name;
            public int Size;
            /// <summary>RGBA32, rows bottom to top: RGB the colour factor (sRGB-encoded), A the smoothness factor (linear).</summary>
            public byte[] BaseRgba;
            /// <summary>RGBA32, rows bottom to top: tangent-space normal, +Y along +v (OpenGL convention).</summary>
            public byte[] NormalRgba;
            /// <summary>Mean linear luminance of the colour factor, as stored (after 8-bit rounding).</summary>
            public float MeanColour;
            /// <summary>Mean of A (0..1), as stored.</summary>
            public float MeanAlpha;
        }

        /// <summary>Every surface, in the order the palette documents them.</summary>
        public static readonly string[] Names =
        {
            "Cast", "Machined", "Brushed", "Drawn", "ECoat", "Zinc", "Forged", "Rubber", "Plastic", "Braid", "HeatShield",
            "Fin", "Loom", "Reservoir", "Friction"
        };

        public const int Size = 512;

        private delegate void SurfaceFn(float u, float v, out float height, out float r, out float g, out float b, out float a);

        public static Result Generate(string name)
        {
            switch (name)
            {
                case "Cast": return Cast();
                case "Machined": return Machined();
                case "Brushed": return Brushed("Brushed", false);
                case "Drawn": return Brushed("Drawn", true);
                case "ECoat": return ECoat();
                case "Zinc": return Zinc();
                case "Forged": return Forged();
                case "Rubber": return Rubber();
                case "Plastic": return Plastic();
                case "Braid": return Braid();
                case "HeatShield": return HeatShield();
                case "Fin": return Fin();
                case "Loom": return Loom();
                case "Reservoir": return Reservoir();
                case "Friction": return Friction();
                default: throw new ArgumentException("unknown surface " + name);
            }
        }

        // ------------------------------------------------------------------------------------------------ surfaces

        /// <summary>Sand-cast iron or aluminium: soft undulation, sand grain and scattered fine pits (darker, rougher).</summary>
        private static Result Cast()
        {
            var low = new Fbm(6, 6, 4, 0.5f, 101);
            var grain = new Fbm(160, 160, 2, 0.5f, 102);
            var rough = new Fbm(40, 40, 2, 0.5f, 103);
            var pits = new Cells(56, 56, 104);
            return Render("Cast", 0.6f, (float u, float v, out float h, out float r, out float g, out float b, out float a) =>
            {
                float l = low.Sample(u, v), gr = grain.Sample(u, v), ro = rough.Sample(u, v);
                pits.Sample(u, v, out float f1, out _, out uint id);
                float pit = 0f;
                if ((id & 0xFF) < 40)
                {
                    float radius = 0.12f + 0.14f * ((id >> 8) & 0xFF) / 255f;
                    pit = Smooth(radius, radius * 0.3f, f1) * (0.5f + 0.5f * ((id >> 16) & 0xFF) / 255f);
                }
                h = 0.8f * l + 0.35f * gr + 0.2f * ro - 1.2f * pit;
                float c = 0.94f + 0.04f * l + 0.03f * gr - 0.18f * pit;
                r = c; g = c; b = c * 1.005f;
                a = Clamp01(0.84f + 0.08f * ro + 0.06f * gr - 0.35f * pit);
            });
        }

        /// <summary>
        /// Turned or ground metal: fine feed grooves around the axis (varying along v), slightly wandering, with streaks
        /// of different depth and broad chatter bands; the light runs along the grooves.
        /// </summary>
        private static Result Machined()
        {
            var wander = new Fbm(2, 6, 3, 0.5f, 201);
            var streak = new Fbm(1, 96, 2, 0.6f, 202);
            var bands = new Fbm(1, 10, 2, 0.5f, 203);
            var fine = new Fbm(64, 64, 2, 0.5f, 204);
            return Render("Machined", 0.7f, (float u, float v, out float h, out float r, out float g, out float b, out float a) =>
            {
                float w = wander.Sample(u, v), s = streak.Sample(u, v), bd = bands.Sample(u, v), f = fine.Sample(u, v);
                float groove = (float)Math.Sin(2.0 * Math.PI * (v * 128f + 0.35f * w));
                h = 0.32f * groove + 0.9f * s + 0.3f * bd + 0.12f * f;
                float c = 0.95f + 0.035f * s + 0.01f * bd + 0.01f * groove;
                r = c; g = c; b = c;
                a = Clamp01(0.90f + 0.08f * s - 0.04f * Math.Abs(bd) + 0.02f * f);
            });
        }

        /// <summary>Brushed finish: long fine streaks along u (Brushed) or along v (Drawn: extrusions, drawn tube).</summary>
        private static Result Brushed(string name, bool alongV)
        {
            var streak = new Fbm(3, 128, 2, 0.55f, 301);
            var faint = new Fbm(2, 40, 2, 0.5f, 302);
            var scratches = new Fbm(1, 256, 1, 0.5f, 303);
            var blotch = new Fbm(4, 4, 3, 0.5f, 304);
            return Render(name, 0.55f, (float u, float v, out float h, out float r, out float g, out float b, out float a) =>
            {
                float x = alongV ? v : u, y = alongV ? u : v;
                float s = streak.Sample(x, y), fa = faint.Sample(x, y), sc = scratches.Sample(x, y), bl = blotch.Sample(x, y);
                float scratch = Smooth(0.55f, 0.85f, sc);
                h = 0.8f * s + 0.5f * fa - 0.6f * scratch;
                float c = 0.94f + 0.035f * s + 0.02f * fa + 0.015f * bl + 0.03f * scratch;
                r = c; g = c; b = c;
                a = Clamp01(0.86f + 0.07f * s + 0.05f * fa - 0.05f * Math.Abs(bl));
            });
        }

        /// <summary>E-coat or paint: a slight orange peel (soft bumps of a few millimetres) and a faint waviness.</summary>
        private static Result ECoat()
        {
            var peel = new Fbm(40, 40, 3, 0.42f, 401);
            var wave = new Fbm(4, 4, 2, 0.5f, 402);
            var tone = new Fbm(8, 8, 3, 0.5f, 403);
            return Render("ECoat", 0.55f, (float u, float v, out float h, out float r, out float g, out float b, out float a) =>
            {
                float p = peel.Sample(u, v), w = wave.Sample(u, v), t = tone.Sample(u, v);
                h = 1.0f * p + 0.5f * w;
                float c = 0.975f + 0.015f * t;
                r = c; g = c; b = c;
                a = Clamp01(0.95f + 0.04f * p - 0.02f * Math.Abs(t));
            });
        }

        /// <summary>Zinc plating: satin crystalline grain and broad passivation patches with a slight gold or blue hue.</summary>
        private static Result Zinc()
        {
            var hue1 = new Fbm(3, 3, 4, 0.55f, 501);
            var hue2 = new Fbm(3, 3, 4, 0.55f, 502);
            var grain = new Fbm(128, 128, 2, 0.5f, 503);
            var soft = new Fbm(20, 20, 2, 0.5f, 504);
            return Render("Zinc", 0.45f, (float u, float v, out float h, out float r, out float g, out float b, out float a) =>
            {
                float p1 = hue1.Sample(u, v), p2 = hue2.Sample(u, v), gr = grain.Sample(u, v), so = soft.Sample(u, v);
                h = 0.55f * gr + 0.45f * so;
                float c = 0.955f + 0.02f * gr + 0.015f * so;
                r = c + 0.035f * p1 - 0.012f * p2;
                g = c + 0.012f * p1 + 0.006f * p2;
                b = c - 0.035f * p1 + 0.04f * p2;
                a = Clamp01(0.86f + 0.08f * gr - 0.06f * p1 + 0.03f * so);
            });
        }

        /// <summary>Forged and shot-blasted steel: dense peening dimples, soft forging undulation and darker scale patches.</summary>
        private static Result Forged()
        {
            var peen = new Cells(140, 140, 601);
            var und = new Fbm(5, 5, 3, 0.5f, 602);
            var scale = new Fbm(4, 4, 4, 0.55f, 603);
            var fine = new Fbm(160, 160, 1, 0.5f, 604);
            return Render("Forged", 0.26f, (float u, float v, out float h, out float r, out float g, out float b, out float a) =>
            {
                peen.Sample(u, v, out float f1, out float f2, out uint id);
                float dimple = f1 * f1;                       // bowl around each peening hit
                float ridge = Smooth(0.0f, 0.18f, f2 - f1);   // rims between hits
                float un = und.Sample(u, v), sc = Smooth(0.05f, 0.45f, scale.Sample(u, v)), fi = fine.Sample(u, v);
                h = 0.9f * dimple + 0.25f * ridge + 0.35f * un + 0.15f * fi;
                float c = 0.92f + 0.04f * un - 0.2f * sc + 0.03f * (1f - ridge) + 0.02f * fi;
                r = c; g = c; b = c * (1f + 0.02f * sc);
                a = Clamp01(0.76f + 0.12f * (1f - dimple) - 0.12f * sc + 0.04f * fi);
            });
        }

        /// <summary>Moulded rubber: fine matte grain and a soft mould texture, slightly greyer in patches (bloom).</summary>
        private static Result Rubber()
        {
            var grain = new Fbm(128, 128, 2, 0.55f, 701);
            var mould = new Fbm(16, 16, 3, 0.5f, 702);
            var bloom = new Fbm(3, 3, 3, 0.5f, 703);
            return Render("Rubber", 0.35f, (float u, float v, out float h, out float r, out float g, out float b, out float a) =>
            {
                float gr = grain.Sample(u, v), mo = mould.Sample(u, v), bl = bloom.Sample(u, v);
                h = 0.7f * gr + 0.45f * mo;
                float c = 0.93f + 0.04f * mo + 0.05f * bl + 0.015f * gr;
                r = c; g = c; b = c;
                a = Clamp01(0.78f + 0.16f * gr + 0.06f * mo - 0.06f * bl);
            });
        }

        /// <summary>Moulded plastic with a stipple grain (rounded mounds) and a fine overlay.</summary>
        private static Result Plastic()
        {
            var stipple = new Cells(64, 64, 801);
            var fine = new Fbm(160, 160, 1, 0.5f, 802);
            var tone = new Fbm(5, 5, 3, 0.5f, 803);
            return Render("Plastic", 0.75f, (float u, float v, out float h, out float r, out float g, out float b, out float a) =>
            {
                stipple.Sample(u, v, out float f1, out _, out uint id);
                float mound = 1f - Smooth(0f, 0.75f, f1);
                mound *= 0.6f + 0.4f * (id & 0xFF) / 255f;
                float fi = fine.Sample(u, v), t = tone.Sample(u, v);
                h = 0.9f * mound + 0.2f * fi;
                float c = 0.965f + 0.02f * mound + 0.012f * t;
                r = c; g = c; b = c;
                a = Clamp01(0.72f + 0.26f * mound + 0.02f * fi);
            });
        }

        /// <summary>
        /// Stainless wire braid over a hose: carriers of four wires crossing at ±45° in an over-under weave, with dark
        /// gaps between them. Diagonal in (u, v), so on a tube the strands spiral along it.
        /// </summary>
        private static Result Braid()
        {
            const int carriers = 16, wires = 4;
            var jitter = new Fbm(32, 32, 2, 0.5f, 901);
            return Render("Braid", 1.6f, (float u, float v, out float h, out float r, out float g, out float b, out float a) =>
            {
                float s = (u + v) * carriers, t = (u - v) * carriers;
                int iA = (int)Math.Floor(s), iB = (int)Math.Floor(t);
                // Elevation along each family: up where it crosses over, down where it passes under.
                float eA = 0.5f + 0.5f * (float)Math.Cos(Math.PI * (t - 0.5f) + Math.PI * iA);
                float eB = 0.5f + 0.5f * (float)Math.Cos(Math.PI * (s - 0.5f) + Math.PI * iB + Math.PI);
                float wA = WireProfile(Frac(s) * wires), wB = WireProfile(Frac(t) * wires);
                float edgeA = Smooth(0f, 0.08f, Math.Min(Frac(s), 1f - Frac(s))), edgeB = Smooth(0f, 0.08f, Math.Min(Frac(t), 1f - Frac(t)));
                float hA = (0.65f * eA + 0.35f * wA) * edgeA, hB = (0.65f * eB + 0.35f * wB) * edgeB;
                float top = Math.Max(hA, hB);
                float j = jitter.Sample(u, v);
                h = 2.0f * top + 0.05f * j;
                float c = 0.42f + 0.6f * top + 0.03f * j;
                r = c; g = c; b = c * 1.01f;
                a = Clamp01(0.55f + 0.45f * top);
            });
        }

        /// <summary>Embossed aluminium heat shield: hexagonally packed round bosses about 4 mm apart on a slightly wavy sheet.</summary>
        private static Result HeatShield()
        {
            const int cols = 15, rows = 18;   // rows of a hex lattice; 18 * sin(60°) ≈ 15.6, close to square
            var wave = new Fbm(3, 3, 3, 0.5f, 1001);
            var grain = new Fbm(96, 96, 1, 0.5f, 1002);
            return Render("HeatShield", 1.3f, (float u, float v, out float h, out float r, out float g, out float b, out float a) =>
            {
                float y = v * rows;
                int row = (int)Math.Floor(y);
                float best = 9f;
                for (int dr = -1; dr <= 1; dr++)
                {
                    int rr = row + dr;
                    float cy = rr + 0.5f;
                    float shift = (Mod(rr, 2) == 1) ? 0.5f : 0f;
                    float x = u * cols - shift;
                    float cx = (float)Math.Floor(x) + 0.5f;
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        // distances in column spacings (rows are cols / rows of a column spacing apart)
                        float dx = x - (cx + dc), dy = (y - cy) * cols / rows;
                        float d = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (d < best) best = d;
                    }
                }
                float boss = best < 0.42f ? (float)Math.Sqrt(1f - (best / 0.42f) * (best / 0.42f)) : 0f;
                float fillet = Smooth(0.52f, 0.42f, best) * 0.15f;
                float w = wave.Sample(u, v), gr = grain.Sample(u, v);
                h = 1.7f * boss + fillet + 1.0f * w + 0.08f * gr;
                float c = 0.9f + 0.09f * boss + 0.02f * w + 0.01f * gr;
                r = c; g = c; b = c * 0.99f;
                a = Clamp01(0.72f + 0.24f * boss + 0.03f * gr);
            });
        }

        /// <summary>
        /// Radiator or condenser face (planar UV across the core): the edges of the folded fins every 1.6 mm with dark
        /// passages between them and bright louvre slits inside each passage.
        /// </summary>
        private static Result Fin()
        {
            const int fins = 32, louvres = 48;
            var tone = new Fbm(4, 4, 3, 0.5f, 1101);
            return Render("Fin", 1.4f, (float u, float v, out float h, out float r, out float g, out float b, out float a) =>
            {
                float x = Frac(u * fins + 0.5f), y = Frac(v * louvres + 0.5f);
                float toEdge = Math.Min(x, 1f - x);
                float edge = 1f - Smooth(0.0f, 0.1f, toEdge);
                float louvre = Smooth(0.3f, 0.45f, y) * Smooth(0.7f, 0.55f, y) * Smooth(0.14f, 0.26f, toEdge);
                float t = tone.Sample(u, v);
                h = 1.0f * edge + 0.35f * louvre;
                float c = 0.32f + 0.66f * edge + 0.28f * louvre + 0.03f * t;
                r = c; g = c; b = c;
                a = Clamp01(0.5f + 0.45f * edge + 0.2f * louvre);
            });
        }

        /// <summary>Corrugated loom over the wiring: rounded ribs around the tube every 3.2 mm (varying along v).</summary>
        private static Result Loom()
        {
            const int ribs = 15;
            var grain = new Fbm(96, 96, 2, 0.5f, 1201);
            return Render("Loom", 2.0f, (float u, float v, out float h, out float r, out float g, out float b, out float a) =>
            {
                float p = 0.5f - 0.5f * (float)Math.Cos(2.0 * Math.PI * v * ribs);
                float rib = (float)Math.Pow(p, 0.7);
                float gr = grain.Sample(u, v);
                h = 1.6f * rib + 0.06f * gr;
                float c = 0.6f + 0.4f * rib + 0.015f * gr;
                r = c; g = c; b = c;
                a = Clamp01(0.55f + 0.45f * rib + 0.03f * gr);
            });
        }

        /// <summary>
        /// Translucent blow-moulded reservoir with the tiling fixed at one tile per 0.25 m, so v = 0 is the tank's centre
        /// height: fluid below v = 7.5 mm (warmer, more opaque), the meniscus line, moulded MAX and MIN ribs at ±15 mm,
        /// clearer above. A is the opacity factor.
        /// </summary>
        private static Result Reservoir()
        {
            var mould = new Fbm(40, 40, 2, 0.5f, 1301);
            var haze = new Fbm(6, 6, 3, 0.5f, 1302);
            return Render("Reservoir", 0.9f, (float u, float v, out float h, out float r, out float g, out float b, out float a) =>
            {
                float vv = v > 0.5f ? v - 1f : v;            // -0.5..0.5 tiles = -125..125 mm
                const float level = 0.030f, line = 0.006f;
                float fluid = Smooth(level + 0.0015f, level - 0.0015f, vv);
                float meniscus = Smooth(line, 0f, Math.Abs(vv - level - 0.001f));
                float ribMax = Smooth(0.0035f, 0f, Math.Abs(vv - 0.06f)), ribMin = Smooth(0.0035f, 0f, Math.Abs(vv + 0.06f));
                float m = mould.Sample(u, v), hz = haze.Sample(u, v);
                h = 0.25f * m + 1.6f * (ribMax + ribMin);
                float c = 0.96f + 0.02f * hz;
                r = c * (1f - 0.02f * fluid) - 0.35f * meniscus;
                g = c * (1f - 0.17f * fluid) - 0.4f * meniscus;
                b = c * (1f - 0.48f * fluid) - 0.45f * meniscus;
                a = Clamp01(0.16f + 0.64f * fluid + 0.5f * meniscus + 0.25f * (ribMax + ribMin) + 0.03f * hz);
            });
        }

        /// <summary>Friction lining: coarse resin-bonded grain with bright metal flecks and dark fibre specks.</summary>
        private static Result Friction()
        {
            var specks = new Cells(90, 90, 1401);
            var grain = new Fbm(120, 120, 2, 0.55f, 1402);
            var low = new Fbm(5, 5, 3, 0.5f, 1403);
            return Render("Friction", 0.8f, (float u, float v, out float h, out float r, out float g, out float b, out float a) =>
            {
                specks.Sample(u, v, out float f1, out _, out uint id);
                float sel = (id & 0xFF) / 255f;
                float dot = Smooth(0.32f, 0.12f, f1);
                float bright = sel < 0.12f ? dot : 0f, dark = sel > 0.7f ? dot : 0f;
                float gr = grain.Sample(u, v), lo = low.Sample(u, v);
                h = 0.7f * gr + 0.3f * lo - 0.4f * dark + 0.2f * bright;
                float c = 0.92f + 0.05f * gr + 0.03f * lo - 0.35f * dark;
                r = c + 0.5f * bright; g = c + 0.38f * bright; b = c + 0.22f * bright;
                a = Clamp01(0.62f + 0.08f * gr + 0.3f * bright);
            });
        }

        private static float WireProfile(float x)
        {
            float f = Frac(x) * 2f - 1f;
            return (float)Math.Sqrt(Math.Max(0f, 1f - f * f));
        }

        // ------------------------------------------------------------------------------------------------ render

        /// <summary>
        /// Samples a surface on the grid, derives the normal map from the height (central differences, wrapped) and packs
        /// both maps. The four corner texels get the maps' means and a flat normal: faces whose UVs are all zero sample
        /// exactly between them and so show the average surface.
        /// </summary>
        private static Result Render(string name, float strength, SurfaceFn fn)
        {
            int n = Size;
            var height = new float[n * n];
            var colour = new float[n * n * 4];
            Parallel.For(0, n, y =>
            {
                for (int x = 0; x < n; x++)
                {
                    fn((x + 0.5f) / n, (y + 0.5f) / n, out float h, out float r, out float g, out float b, out float a);
                    int i = y * n + x;
                    height[i] = h;
                    colour[i * 4] = r; colour[i * 4 + 1] = g; colour[i * 4 + 2] = b; colour[i * 4 + 3] = a;
                }
            });
            var baseRgba = new byte[n * n * 4];
            var normalRgba = new byte[n * n * 4];
            // Height units are "texels at 512": the slope is scaled to the size so a 1024 map looks the same.
            float k = strength * n / 512f * 0.5f;
            Parallel.For(0, n, y =>
            {
                int ym = (y + n - 1) % n, yp = (y + 1) % n;
                for (int x = 0; x < n; x++)
                {
                    int xm = (x + n - 1) % n, xp = (x + 1) % n, i = y * n + x;
                    float dx = (height[y * n + xp] - height[y * n + xm]) * k;
                    float dy = (height[yp * n + x] - height[ym * n + x]) * k;
                    float len = (float)Math.Sqrt(dx * dx + dy * dy + 1f);
                    normalRgba[i * 4] = ToByte(-dx / len * 0.5f + 0.5f);
                    normalRgba[i * 4 + 1] = ToByte(-dy / len * 0.5f + 0.5f);
                    normalRgba[i * 4 + 2] = ToByte(1f / len * 0.5f + 0.5f);
                    normalRgba[i * 4 + 3] = 255;
                    baseRgba[i * 4] = ToByte(LinearToSrgb(colour[i * 4]));
                    baseRgba[i * 4 + 1] = ToByte(LinearToSrgb(colour[i * 4 + 1]));
                    baseRgba[i * 4 + 2] = ToByte(LinearToSrgb(colour[i * 4 + 2]));
                    baseRgba[i * 4 + 3] = ToByte(colour[i * 4 + 3]);
                }
            });
            double sumLum = 0, sumR = 0, sumG = 0, sumB = 0, sumA = 0;
            for (int i = 0; i < n * n; i++)
            {
                double r = SrgbToLinear(baseRgba[i * 4] / 255f), g = SrgbToLinear(baseRgba[i * 4 + 1] / 255f), b = SrgbToLinear(baseRgba[i * 4 + 2] / 255f);
                sumR += r; sumG += g; sumB += b;
                sumLum += 0.2126 * r + 0.7152 * g + 0.0722 * b;
                sumA += baseRgba[i * 4 + 3] / 255.0;
            }
            double count = n * n;
            byte mr = ToByte(LinearToSrgb((float)(sumR / count))), mg = ToByte(LinearToSrgb((float)(sumG / count)));
            byte mb = ToByte(LinearToSrgb((float)(sumB / count))), ma = ToByte((float)(sumA / count));
            foreach (int i in new[] { 0, n - 1, (n - 1) * n, n * n - 1 })
            {
                baseRgba[i * 4] = mr; baseRgba[i * 4 + 1] = mg; baseRgba[i * 4 + 2] = mb; baseRgba[i * 4 + 3] = ma;
                normalRgba[i * 4] = 128; normalRgba[i * 4 + 1] = 128; normalRgba[i * 4 + 2] = 255;
            }
            return new Result
            {
                Name = name, Size = n, BaseRgba = baseRgba, NormalRgba = normalRgba,
                MeanColour = (float)(sumLum / count), MeanAlpha = (float)(sumA / count)
            };
        }

        // ------------------------------------------------------------------------------------------------ noise

        private static uint Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ (uint)seed * 0xcb1ab31fu;
                h ^= h >> 15; h *= 0x2c1b3c6du;
                h ^= h >> 12; h *= 0x297a2d39u;
                h ^= h >> 15;
                return h;
            }
        }

        private static float Rand(int x, int y, int seed) => (Hash(x, y, seed) & 0xFFFFFF) / 16777216f;

        private static int Mod(int a, int m)
        {
            int r = a % m;
            return r < 0 ? r + m : r;
        }

        private static float Frac(float x) => x - (float)Math.Floor(x);

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;

        /// <summary>Smoothstep from e0 to e1 (either order).</summary>
        private static float Smooth(float e0, float e1, float x)
        {
            float t = Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        private static byte ToByte(float x) => (byte)Math.Round(Clamp01(x) * 255f);

        private static float LinearToSrgb(float c)
        {
            c = Clamp01(c);
            return c <= 0.0031308f ? c * 12.92f : 1.055f * (float)Math.Pow(c, 1.0 / 2.4) - 0.055f;
        }

        private static float SrgbToLinear(float c) => c <= 0.04045f ? c / 12.92f : (float)Math.Pow((c + 0.055f) / 1.055f, 2.4);

        /// <summary>Periodic gradient noise on an fx by fy lattice over the unit tile, about -1..1.</summary>
        private sealed class Grad
        {
            private readonly int fx, fy;
            private readonly float[] gx, gy;

            public Grad(int fx, int fy, int seed)
            {
                this.fx = Math.Max(1, fx);
                this.fy = Math.Max(1, fy);
                gx = new float[this.fx * this.fy];
                gy = new float[this.fx * this.fy];
                for (int y = 0; y < this.fy; y++)
                    for (int x = 0; x < this.fx; x++)
                    {
                        double angle = Rand(x, y, seed) * Math.PI * 2.0;
                        gx[y * this.fx + x] = (float)Math.Cos(angle);
                        gy[y * this.fx + x] = (float)Math.Sin(angle);
                    }
            }

            public float Sample(float u, float v)
            {
                float x = u * fx, y = v * fy;
                int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
                float tx = x - x0, ty = y - y0;
                int xa = Mod(x0, fx), xb = Mod(x0 + 1, fx), ya = Mod(y0, fy) * fx, yb = Mod(y0 + 1, fy) * fx;
                float n00 = gx[ya + xa] * tx + gy[ya + xa] * ty;
                float n10 = gx[ya + xb] * (tx - 1f) + gy[ya + xb] * ty;
                float n01 = gx[yb + xa] * tx + gy[yb + xa] * (ty - 1f);
                float n11 = gx[yb + xb] * (tx - 1f) + gy[yb + xb] * (ty - 1f);
                float sx = tx * tx * tx * (tx * (tx * 6f - 15f) + 10f), sy = ty * ty * ty * (ty * (ty * 6f - 15f) + 10f);
                float a = n00 + (n10 - n00) * sx, b = n01 + (n11 - n01) * sx;
                return (a + (b - a) * sy) * 1.4142f;
            }
        }

        /// <summary>Periodic fractal noise: octaves of gradient noise, each with twice the lattice, about -1..1.</summary>
        private sealed class Fbm
        {
            private readonly Grad[] octaves;
            private readonly float[] amplitudes;
            private readonly float norm;

            public Fbm(int fx, int fy, int count, float gain, int seed)
            {
                octaves = new Grad[count];
                amplitudes = new float[count];
                float amplitude = 1f, sum = 0f;
                for (int i = 0; i < count; i++)
                {
                    octaves[i] = new Grad(fx << i, fy << i, seed * 31 + i);
                    amplitudes[i] = amplitude;
                    sum += amplitude;
                    amplitude *= gain;
                }
                norm = 1f / (sum * 0.75f);
            }

            public float Sample(float u, float v)
            {
                float s = 0f;
                for (int i = 0; i < octaves.Length; i++) s += octaves[i].Sample(u, v) * amplitudes[i];
                return Math.Max(-1f, Math.Min(1f, s * norm));
            }
        }

        /// <summary>Periodic cellular noise: one jittered point per cell; distances in cell units, id is the cell's hash.</summary>
        private sealed class Cells
        {
            private readonly int nx, ny;
            private readonly float[] px, py;
            private readonly uint[] ids;

            public Cells(int nx, int ny, int seed)
            {
                this.nx = nx;
                this.ny = ny;
                px = new float[nx * ny];
                py = new float[nx * ny];
                ids = new uint[nx * ny];
                for (int y = 0; y < ny; y++)
                    for (int x = 0; x < nx; x++)
                    {
                        int i = y * nx + x;
                        px[i] = 0.1f + 0.8f * Rand(x, y, seed);
                        py[i] = 0.1f + 0.8f * Rand(x, y, seed + 7919);
                        ids[i] = Hash(x, y, seed + 104729);
                    }
            }

            public void Sample(float u, float v, out float f1, out float f2, out uint id)
            {
                float x = u * nx, y = v * ny;
                int cx = (int)Math.Floor(x), cy = (int)Math.Floor(y);
                f1 = 9f; f2 = 9f; id = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int gx = cx + dx, gy = cy + dy;
                        int i = Mod(gy, ny) * nx + Mod(gx, nx);
                        float ddx = gx + px[i] - x, ddy = gy + py[i] - y;
                        float d = (float)Math.Sqrt(ddx * ddx + ddy * ddy);
                        if (d < f1) { f2 = f1; f1 = d; id = ids[i]; }
                        else if (d < f2) f2 = d;
                    }
            }
        }
    }
}
#endif
