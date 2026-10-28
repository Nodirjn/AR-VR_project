#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CarInternals
{
    /// <summary>
    /// Body structure: the load-carrying members inside the existing shell. Front and rear crash structures (impact
    /// beams and crash boxes inside the fascias), the side sills with floor rails and seat crossmembers, the cross-car
    /// beam behind the steering wheel, the seat mounting frames, and the roll structure (A- and B-pillar
    /// reinforcements, overhead bows). This concept car has a very low floor, doors that reach almost to it and seats
    /// that sit on it, so every member is fitted to the free space measured with rays: the sills are wide and flat
    /// under the sill trim, the floor members are low hats, the beams pass behind the trims.
    /// </summary>
    [CarInternalsSubsystem("Structure", 80)]
    public sealed class BodyStructure : ICarInternalsSubsystem
    {
        private static readonly string[] FloorNames = { "InteriorFloor", "CarConcept" };
        private InternalsContext ctx;
        private CarPalette m;
        private CarLayout L;
        private Material ecoat, hot, alu;
        private float sillIn = 0.86f, sillOut = 1.04f, sillRear = -0.9f, sillFront = 0.88f;
        private readonly (float[] z, float[] top)[] sillProfile = new (float[], float[])[2];

        /// <summary>Top of the sill on a side at z (car space y), or -9 outside its length.</summary>
        private float SillTop(float s, float z)
        {
            var (zs, top) = sillProfile[s < 0 ? 0 : 1];
            if (zs == null || zs.Length < 2 || z < zs[0] - 0.05f || z > zs[zs.Length - 1] + 0.05f) return -9f;
            float best = -9f;
            for (int i = 0; i < zs.Length; i++) if (Mathf.Abs(zs[i] - z) < 0.08f) best = Mathf.Max(best, top[i]);
            return best;
        }

        public void Build(InternalsContext context)
        {
            ctx = context;
            m = ctx.Mats;
            L = ctx.Layout;
            // Flat finishes for the body-in-white: e-coated mild / high-strength steel, press-hardened boron steel
            // (Al-Si coated, slightly blue-grey) and extruded aluminium.
            ecoat = m.Get("CI_Steel_BodyECoat", new Color(0.34f, 0.355f, 0.37f), 0.6f, 0.42f);
            hot = m.Get("CI_Steel_PressHardened", new Color(0.40f, 0.44f, 0.50f), 0.75f, 0.34f);
            alu = m.Get("CI_Aluminium_Extruded", new Color(0.80f, 0.81f, 0.83f), 1f, 0.5f);
            Transform system = ctx.System("Structure");
            // Integration (combined car): the nose has no room for the front crash structure as fitted here. The
            // beam stands in the condenser's slot, the crash boxes and rail extensions run through the A/C
            // compressor, the alternator, the ABS unit, the clutch, the bell housing, the starter and the engine
            // mounts. It stays out of the build until rails can be carried outboard of the engine and transaxle.
            if (BuildFrontCrash) FrontCrash(system);
            else ctx.Note("structure: front crash structure not built in the combined car (nose filled by the condenser, accessory drive and transaxle)");
            Sills(system);
            CrossCarBeam(system);
            SeatFrames(system);
            RollStructure(system);
            RearCrash(system);
            // Designed joints: feet bolted to the floor tub, the beam's column mount on the column bracket, the
            // cross-car beam behind the dash and pillar trims (open shells in the glTF), seat rails on the floor.
            ctx.AllowContact("structure.front-crash.rail-left.foot", "CarConcept");
            ctx.AllowContact("structure.front-crash.rail-right.foot", "CarConcept");
            ctx.AllowContact("structure.front-crash.rail-left.foot", "InteriorFloor");
            ctx.AllowContact("structure.front-crash.rail-right.foot", "InteriorFloor");
            ctx.AllowContact("structure.cross-car-beam.column-mount", "steering.column.jacket");
            ctx.AllowContact("structure.cross-car-beam", "InteriorDash");
            ctx.AllowContact("structure.cross-car-beam", "InteriorMid");
            ctx.AllowContact("structure.cross-car-beam.end-brackets", "structure.roll-structure.a-pillar");
            ctx.AllowContact("structure.seat-frames", "InteriorFloor");
            // The carpet (InteriorFloormats) is laid over the floor rails and crossmembers welded to the floor pan.
            ctx.AllowContact("structure.sills.floor-rails", "InteriorFloormats");
            ctx.AllowContact("structure.sills.seat-crossmembers", "InteriorFloormats");
            ctx.AllowContact("structure.rear-crash.crash-box-left.bolt", "InteriorFloor");
            ctx.AllowContact("structure.rear-crash.crash-box-right.bolt", "InteriorFloor");
            ctx.AllowContact("structure.rear-crash.crash-box-left.bolt", "CarConcept");
            ctx.AllowContact("structure.rear-crash.crash-box-right.bolt", "CarConcept");
            ctx.AllowContact("structure.seat-frames", "CarConcept");
        }

        // ------------------------------------------------------------------------------------------------ probes

        private float Ray(Vector3 o, Vector3 d, float max, params string[] prefixes)
            => ctx.Probe.Cast(o, d, max, out float h, prefixes) ? h : float.PositiveInfinity;

        private float FloorTop(float x, float z, float from = 0.45f)
            => ctx.Probe.Cast(new Vector3(x, from, z), Vector3.down, from + 0.1f, out float h, FloorNames) ? from - h : L.FloorTopY;

        /// <summary>First hit on the car's surfaces whose name does not start with any of the excluded prefixes.</summary>
        private float RayExcept(Vector3 o, Vector3 d, float max, params string[] exclude)
        {
            Vector3 wo = L.CarToWorld.MultiplyPoint3x4(o), wd = L.CarToWorld.MultiplyVector(d).normalized;
            bool bf = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            float best = float.PositiveInfinity;
            foreach (RaycastHit h in Physics.RaycastAll(wo, wd, max, 1 << InternalsProbe.EnvLayer, QueryTriggerInteraction.Ignore))
            {
                string n = h.collider.gameObject.name;
                if (exclude.Any(e => n.StartsWith(e, StringComparison.Ordinal))) continue;
                best = Mathf.Min(best, h.distance);
            }
            Physics.queriesHitBackfaces = bf;
            return best;
        }

        private static readonly string[] FloorAndCarpet = { "InteriorFloor", "CarConcept" };

        /// <summary>Top of the floor pan itself at (x, z), under the carpet (the carpet lies over the floor members).</summary>
        private float FloorPan(float x, float z)
        {
            Vector3 wo = L.CarToWorld.MultiplyPoint3x4(new Vector3(x, 0.45f, z)), wd = L.CarToWorld.MultiplyVector(Vector3.down);
            bool bf = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            float best = float.PositiveInfinity;
            foreach (RaycastHit h in Physics.RaycastAll(wo, wd, 0.55f, 1 << InternalsProbe.EnvLayer, QueryTriggerInteraction.Ignore))
            {
                string n = h.collider.gameObject.name;
                if (n == "InteriorFloor" || n == "CarConcept") best = Mathf.Min(best, h.distance);
            }
            Physics.queriesHitBackfaces = bf;
            return float.IsInfinity(best) ? L.FloorTopY : 0.45f - best;
        }

        /// <summary>True when an axis-aligned box (car space) crosses no surface other than the floor and the carpet.</summary>
        private bool BoxFree(Vector3 min, Vector3 max, float step = 0.02f)
        {
            Vector3 size = max - min;
            for (int axis = 0; axis < 3; axis++)
            {
                int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
                int n1 = Mathf.Max(1, Mathf.CeilToInt(size[a1] / step)), n2 = Mathf.Max(1, Mathf.CeilToInt(size[a2] / step));
                for (int i = 0; i <= n1; i++)
                    for (int j = 0; j <= n2; j++)
                    {
                        Vector3 o = min;
                        o[a1] += size[a1] * i / n1;
                        o[a2] += size[a2] * j / n2;
                        Vector3 d = Vector3.zero;
                        d[axis] = 1f;
                        if (size[axis] > 1e-4f && !float.IsInfinity(RayExcept(o, d, size[axis], FloorAndCarpet))) return false;
                    }
            }
            return true;
        }

        /// <summary>Free height above y at (x, z): distance to the first surface of any kind going up.</summary>
        private float Clear(float x, float y, float z, float max = 0.6f) => Mathf.Min(max, Ray(new Vector3(x, y, z), Vector3.up, max));

        private static Txt T(string en, string uz, string de, string du) => new Txt(en, uz, de, du);

        /// <summary>Car-space AABB of one of the car's renderers (its own mesh only), or null.</summary>
        private Bounds? MeshBounds(string path)
        {
            Transform t = L.Car.Find(path);
            MeshFilter f = t != null ? t.GetComponent<MeshFilter>() : null;
            if (f == null || f.sharedMesh == null) return null;
            Vector3[] v;
            try { v = f.sharedMesh.vertices; } catch { return null; }
            if (v.Length == 0) return null;
            Matrix4x4 toCar = L.WorldToCar * t.localToWorldMatrix;
            var b = new Bounds(toCar.MultiplyPoint3x4(v[0]), Vector3.zero);
            foreach (Vector3 p in v) b.Encapsulate(toCar.MultiplyPoint3x4(p));
            return b;
        }

        private float DoorFrontZ => MeshBounds("BodyDoorLColor1")?.max.z ?? 0.908f;
        private float DoorRearZ => MeshBounds("BodyDoorLColor1")?.min.z ?? -0.761f;
        private float HingeZ0 => DoorFrontZ + 0.045f;
        private float HingeZ1 => DoorFrontZ + 0.115f;

        private readonly Dictionary<float, (List<float> ys, List<Vector2> x)> hingeRuns = new Dictionary<float, (List<float>, List<Vector2>)>();

        /// <summary>Longest run of free stations in the hinge-pillar cavity: heights and (inner, outer) |x| per height.</summary>
        private (List<float> ys, List<Vector2> x) HingeRun(float s)
        {
            if (hingeRuns.TryGetValue(s, out var cached)) return cached;
            float z0 = HingeZ0, z1 = HingeZ1;
            const float x0 = 0.97f;
            var ys = new List<float>();
            var xs = new List<Vector2>();
            var bestYs = new List<float>();
            var bestXs = new List<Vector2>();
            float yLow = Mathf.Max(FloorTop(s * x0, (z0 + z1) * 0.5f, 0.85f), FloorTop(s * x0, z1, 0.85f)) + 0.006f;
            for (float y = yLow; y <= 0.88f; y += 0.02f)
            {
                float xi = 0f, xo = 9f;
                foreach (float dy in new[] { -0.01f, 0f, 0.01f })
                {
                    var (a0, b0) = HingeCavity(s, y + dy, z0, z1);
                    xi = Mathf.Max(xi, a0 + 0.002f);
                    xo = Mathf.Min(xo, b0 - 0.002f);
                }
                float c = 9f;
                foreach (float ax in new[] { xi + 0.004f, (xi + xo) * 0.5f, xo - 0.004f })
                    foreach (float zz in new[] { z0 + 0.004f, (z0 + z1) * 0.5f, z1 - 0.004f })
                        c = Mathf.Min(c, Clear(s * ax, y, zz, 0.021f));
                bool ok = xo - xi >= 0.035f && c >= 0.0205f;
                if (ok) { ys.Add(y); xs.Add(new Vector2(xi, xo)); }
                if (!ok || y + 0.02f > 0.88f)
                {
                    if (ys.Count > bestYs.Count) { bestYs = ys; bestXs = xs; }
                    ys = new List<float>();
                    xs = new List<Vector2>();
                }
            }
            var run = (bestYs, bestXs);
            hingeRuns[s] = run;
            return run;
        }

        /// <summary>C-section (web on the outer side, legs toward the cabin) in the (x, z) plane, for pillars.</summary>
        private static Vector2[] PillarSection(float s, float xi, float xo, float z0, float z1, float t)
        {
            float a = s * xi, b = s * xo, w = s * t;
            return new[]
            {
                new Vector2(a, z0), new Vector2(b, z0), new Vector2(b, z1), new Vector2(a, z1),
                new Vector2(a, z1 - t), new Vector2(b - w, z1 - t), new Vector2(b - w, z0 + t), new Vector2(a, z0 + t)
            };
        }

        /// <summary>Hinge-pillar cavity ahead of the door at height y: free |x| range (inner, outer) over z0..z1.</summary>
        private (float xi, float xo) HingeCavity(float s, float y, float z0, float z1)
        {
            const float x0 = 0.97f;
            float xo = 1.1f, xi = 0.8f;
            foreach (float z in new[] { z0 + 0.005f, (z0 + z1) * 0.5f, z1 - 0.005f })
            {
                float o = Ray(new Vector3(s * x0, y, z), new Vector3(s, 0f, 0f), 0.2f);
                float i = Ray(new Vector3(s * x0, y, z), new Vector3(-s, 0f, 0f), 0.2f);
                if (!float.IsInfinity(o)) xo = Mathf.Min(xo, x0 + o);
                if (!float.IsInfinity(i)) xi = Mathf.Max(xi, x0 - i);
            }
            return (xi + 0.004f, xo - 0.004f);
        }

        private Mesh LoftMesh(string key, Vector3 pivot, Action<MeshKit> build) => ctx.Mesh(key, k =>
        {
            k.Push(Matrix4x4.Translate(-pivot));
            build(k);
            k.Pop();
        });

        // ------------------------------------------------------------------------------------------------ front

        private void FrontCrash(Transform system)
        {
            const string id = "structure.front-crash";
            Transform asm = ctx.Assembly(system, id, T("Front Crash Structure", "Old zarba yutish tuzilmasi",
                    "Impact beam, two crash boxes and rail extensions behind the front fascia: they collapse in a frontal impact and feed the load into the floor.",
                    "Old qoplama ortidagi zarba to‘sini, ikki zarba qutisi va lonjeron uzaytmalari: old to‘qnashuvda ezilib, kuchni polga uzatadi."),
                "Aluminium and steel", Vector3.forward, 1.3f);
            float bay = Mathf.Max(L.FloorTopY + 0.02f, FloorTop(0f, 2.05f));
            const float H = 0.10f, D = 0.045f, box = 0.034f;
            float yc = bay + 0.035f + H * 0.5f;
            // Crash boxes just outboard of the engine at beam height.
            float v6x = 0f;
            foreach (float y in new[] { yc - 0.045f, yc, yc + 0.045f })
                for (float z = 1.70f; z <= 2.20f; z += 0.05f)
                    foreach (float s in new[] { -1f, 1f })
                    {
                        float d = Ray(new Vector3(s * 0.8f, y, z), new Vector3(-s, 0f, 0f), 0.8f, "V6/");
                        if (!float.IsInfinity(d)) v6x = Mathf.Max(v6x, 0.8f - d);
                    }
            float xc = Mathf.Max(0.36f, v6x + 0.014f + box);
            Bounds nose = L.Reserved.TryGetValue("cooling.nose", out Bounds nb) ? nb : new Bounds(new Vector3(0f, 0.3f, 9f), Vector3.zero);
            // Beam centre line: as far forward as the fascia and the condenser allow, clear of the engine's front.
            var pts = new List<Vector3>();
            int n = 13;
            float xe = xc + 0.05f;
            var zc = new float[n];
            var front = new float[n];
            var back = new float[n];
            for (int i = 0; i < n; i++)
            {
                float x = Mathf.Lerp(-xe, xe, (float)i / (n - 1));
                float zf = L.FrontBumperZ;
                foreach (float y in new[] { yc - H * 0.45f, yc, yc + H * 0.45f })
                    zf = Mathf.Min(zf, 1.85f + Ray(new Vector3(x, y, 1.85f), Vector3.forward, 0.6f, "Body"));
                float lim = zf - 0.012f - D * 0.5f;
                if (Mathf.Abs(x) < nose.max.x + 0.03f && yc - H * 0.5f < nose.max.y) lim = Mathf.Min(lim, nose.min.z - 0.004f - D * 0.5f);
                float zv = -9f;
                foreach (float y in new[] { yc - H * 0.5f, yc - H * 0.25f, yc, yc + H * 0.25f, yc + H * 0.5f })
                {
                    float d = Ray(new Vector3(x, y, zf - 0.003f), Vector3.back, 0.6f, "V6/");
                    if (!float.IsInfinity(d)) zv = Mathf.Max(zv, zf - 0.003f - d);
                }
                front[i] = lim;
                back[i] = zv + 0.008f + D * 0.5f;
                zc[i] = lim;
            }
            for (int pass = 0; pass < 4; pass++)
                for (int i = 1; i < n - 1; i++) zc[i] = Mathf.Min(front[i], (zc[i - 1] + 2f * zc[i] + zc[i + 1]) * 0.25f);
            for (int i = 0; i < n; i++)
            {
                if (zc[i] < back[i]) ctx.Warn("structure.front-crash: beam squeezed between fascia and engine at x " + CarLayout.F(Mathf.Lerp(-xe, xe, (float)i / (n - 1))));
                pts.Add(new Vector3(Mathf.Lerp(-xe, xe, (float)i / (n - 1)), yc, zc[i]));
            }
            List<Vector3> path = MeshKit.CatmullRom(pts, 3);
            float BeamZ(float x)
            {
                float t = Mathf.InverseLerp(-xe, xe, x) * (n - 1);
                int i0 = Mathf.Clamp(Mathf.FloorToInt(t), 0, n - 2);
                return Mathf.Lerp(zc[i0], zc[i0 + 1], t - i0);
            }
            ctx.Note("front crash: beam y " + CarLayout.F(yc) + " z " + CarLayout.F(zc[n / 2]) + " (centre) .. " + CarLayout.F(zc[0]) + " (ends), crash boxes x " + CarLayout.F(xc) + ", engine half-width at beam height " + CarLayout.F(v6x));

            Transform beam = ctx.SubAssembly(asm, id + ".beam", T("Front Impact Beam", "Old zarba to‘sini",
                    "Curved two-chamber aluminium extrusion that spreads an impact across both crash boxes.",
                    "Zarbani ikkala zarba qutisiga taqsimlovchi egik ikki kamerali alyuminiy profil."),
                "Aluminium 7003-T7", Vector3.forward, 0.8f);
            Vector3 pivot = new Vector3(0f, yc, zc[n / 2]);
            Mesh beamMesh = LoftMesh("front-impact-beam", pivot, k =>
            {
                k.BoxTube(path, D, H, 0.003f, Vector3.up);
                k.Sweep(Profiles.Rect(D - 0.005f, 0.003f), path, Vector3.up, false, false);
            });
            ctx.Part(beam, id + ".beam.extrusion", T("Beam extrusion", "To‘sin profili",
                    "Stretch-bent 7003 aluminium extrusion with a centre web: two closed cells that fold progressively.",
                    "Markaziy devorli egib cho‘zilgan 7003 alyuminiy profil: bosqichma-bosqich buklanuvchi ikki yopiq kamera."),
                "Aluminium 7003-T7", beamMesh, new[] { alu }, pivot, Quaternion.identity);
            float tx = 0.22f;
            Vector3 towAt = new Vector3(tx, yc, BeamZ(tx));
            Mesh tow = ctx.Mesh("towing-eye-bush", k =>
            {
                k.Lathe(new[] { new Vector2(0.008f, -D * 0.5f - 0.002f), new Vector2(0.013f, -D * 0.5f - 0.002f), new Vector2(0.013f, D * 0.5f), new Vector2(0.016f, D * 0.5f),
                    new Vector2(0.016f, D * 0.5f + 0.004f), new Vector2(0.008f, D * 0.5f + 0.004f), new Vector2(0.008f, -D * 0.5f - 0.002f) }, 16);
            });
            ctx.Part(beam, id + ".beam.towing-eye-bush", T("Towing-eye bush", "Shatakka olish vtulkasi",
                    "Threaded steel bush welded through the beam; the screw-in towing eye goes into it.",
                    "To‘sin orqali payvandlangan rezbali po‘lat vtulka; buraladigan shatak ilmog‘i shunga kiradi."),
                "Steel, M24 thread", tow, new[] { m.ZincSteel }, towAt, InternalsContext.AlongY(Vector3.forward), false, Vector3.forward, 0.5f);

            Mesh tube = ctx.Mesh("crash-box-front", k => CrushTube(k, box, 0.16f));
            Mesh plateF = ctx.Mesh("crash-box-plate-beam", k => k.RoundedBox(Vector3.zero, new Vector3(0.10f, 0.095f, 0.006f), 0.004f, 1));
            Mesh plateR = ctx.Mesh("crash-box-plate-rail", k => k.RoundedBox(Vector3.zero, new Vector3(0.12f, 0.11f, 0.008f), 0.004f, 1));
            Mesh bolt = ctx.Mesh("bolt-M10x30", k => Fasteners.HexBolt(k, 0.01f, 0.03f));
            float zEnd = 1.715f;
            foreach (float s in new[] { -1f, 1f })
            {
                string side = s < 0 ? "left" : "right";
                string Side = s < 0 ? "Left" : "Right";
                string uz = s < 0 ? "Chap" : "O‘ng";
                float x = s * xc;
                float z0 = BeamZ(x) - D * 0.5f;
                Transform cb = ctx.SubAssembly(asm, id + ".crash-box-" + side, T(Side + " Crash Box", uz + " zarba qutisi",
                        "Octagonal aluminium crush can with trigger beads: folds like a concertina to absorb a low-speed impact before the rails are loaded.",
                        "Boshlovchi bo‘rtmali sakkiz qirrali alyuminiy ezilish qutisi: lonjeronlarga kuch tushishidan oldin past tezlikdagi zarbani garmondek buklanib yutadi."),
                    "Aluminium 6063-T6", new Vector3(s * 0.6f, 0f, 0.8f), 0.9f);
                float zp = z0 - 0.003f;
                ctx.Part(cb, id + ".crash-box-" + side + ".beam-plate", T("Beam flange plate", "To‘sin flanets plastinasi",
                        "Welded front plate that the impact beam bolts to.", "Zarba to‘sini boltlanadigan payvandlangan old plastina."),
                    "Aluminium 6082-T6", plateF, new[] { alu }, new Vector3(x, yc, zp), Quaternion.identity);
                float len = 0.16f;
                ctx.Part(cb, id + ".crash-box-" + side + ".tube", T("Crush tube", "Ezilish quvuri",
                        "Thin-wall octagonal extrusion; the two beads start the folding at a controlled force.",
                        "Yupqa devorli sakkiz qirrali profil; ikki bo‘rtma buklanishni nazorat qilingan kuchda boshlaydi."),
                    "Aluminium 6063-T6", tube, new[] { alu }, new Vector3(x, yc, z0 - 0.006f), InternalsContext.AlongY(Vector3.back), false, Vector3.forward, 0.5f);
                float z1 = z0 - 0.006f - len;
                ctx.Part(cb, id + ".crash-box-" + side + ".rail-plate", T("Rail flange plate", "Lonjeron flanets plastinasi",
                        "Rear plate bolted to the rail extension's flange; the crash box is replaced after a minor impact.",
                        "Lonjeron uzaytmasining flanetsiga boltlanadigan orqa plastina; yengil zarbadan keyin quti almashtiriladi."),
                    "Aluminium 6082-T6", plateR, new[] { alu }, new Vector3(x, yc, z1 - 0.004f), Quaternion.identity);
                int b = 0;
                foreach (float bx in new[] { -0.045f, 0.045f })
                    foreach (float by in new[] { -0.042f, 0.042f })
                        ctx.Part(cb, id + ".crash-box-" + side + ".bolt-" + (++b), T("Flange bolt M10", "Flanets bolti M10",
                                "Bolt into the weld nut of the rail flange, property class 10.9.", "Lonjeron flanetsining payvand gaykasiga buraladigan bolt, 10.9 sinf."),
                            "Steel 10.9, zinc flake", bolt, new[] { m.ZincSteel }, new Vector3(x + bx, yc + by, z1), InternalsContext.AlongY(Vector3.forward), true, Vector3.forward, 0.4f);

                // Rail extension: box section back to the floor, flange in front, foot on the bay floor.
                Transform rail = ctx.SubAssembly(asm, id + ".rail-" + side, T(Side + " Front Rail Extension", uz + " old lonjeron uzaytmasi",
                        "Box-section rail from the crash box back toward the bulkhead; carries crash and towing loads into the floor.",
                        "Zarba qutisidan bo‘lim devori tomon boruvchi quti kesimli lonjeron; to‘qnashuv va shatak kuchlarini polga uzatadi."),
                    "Dual-phase steel DP800", new Vector3(s, -0.3f, 0f), 0.8f);
                float zr0 = z1 - 0.016f;
                float zr1 = Mathf.Min(zEnd, zr0 - 0.05f);
                Vector3 r0 = new Vector3(x, yc - 0.005f, zr0), r1 = new Vector3(x, yc - 0.005f, zr1);
                Mesh railMesh = ctx.Mesh("front-rail-extension-" + Mathf.RoundToInt((zr0 - zr1) * 1000f), k => k.BoxTube(new[] { Vector3.zero, Vector3.back * (zr0 - zr1) }, 0.06f, 0.08f, 0.0025f, Vector3.up));
                ctx.Part(rail, id + ".rail-" + side + ".rail", T("Rail extension", "Lonjeron uzaytmasi",
                        "Closed box of two welded DP800 pressings; its section steps up toward the floor so it buckles in order.",
                        "Ikki payvandlangan DP800 shtampovkadan yopiq quti; kesimi pol tomon kattalashib, ketma-ket egiladi."),
                    "Dual-phase steel DP800", railMesh, new[] { ecoat }, r0, Quaternion.identity);
                Mesh flange = ctx.Mesh("front-rail-flange", k => k.RoundedBox(Vector3.zero, new Vector3(0.12f, 0.11f, 0.008f), 0.004f, 1));
                ctx.Part(rail, id + ".rail-" + side + ".flange", T("Front flange", "Old flanets",
                        "Welded end plate with weld nuts for the crash box.", "Zarba qutisi uchun payvand gaykali payvandlangan uchlik plastina."),
                    "Steel", flange, new[] { ecoat }, new Vector3(x, yc, z1 - 0.012f), Quaternion.identity);
                float fy = Mathf.Max(bay, FloorTop(x, zr1 + 0.03f));
                float footTop = yc - 0.045f;
                Mesh foot = ctx.Mesh("front-rail-foot-" + Mathf.RoundToInt((footTop - fy) * 1000f), k =>
                {
                    float h = footTop - fy;
                    k.Box(new Vector3(0f, h * 0.5f, 0.0f), new Vector3(0.07f, h, 0.004f));
                    k.Box(new Vector3(0f, 0.002f, -0.022f), new Vector3(0.07f, 0.004f, 0.048f));
                    k.PlanarPrism(new[] { new Vector3(0f, 0.004f, -0.002f), new Vector3(0f, h - 0.004f, -0.002f), new Vector3(0f, 0.004f, -0.04f) }, 0.004f);
                });
                ctx.Part(rail, id + ".rail-" + side + ".foot", T("Floor mounting foot", "Polga mahkamlash tayanchi",
                        "Gusseted angle bracket that ties the rail's rear end to the bay floor.", "Lonjeron orqa uchini dvigatel bo‘limi poliga bog‘lovchi qovurg‘ali burchak kronshteyn."),
                    "Pressed steel", foot, new[] { ecoat }, new Vector3(x, fy + 0.0015f, zr1 + 0.002f), Quaternion.identity, false, Vector3.down, 0.5f);
                Mesh fb = ctx.Mesh("bolt-M8x20", k => Fasteners.HexBolt(k, 0.008f, 0.02f));
                int fbn = 0;
                foreach (float bx in new[] { -0.022f, 0.022f })
                    ctx.Part(rail, id + ".rail-" + side + ".foot-bolt-" + (++fbn),
                        T("Foot bolt M8", "Tayanch bolti M8", "Bolts the foot to a weld nut in the floor.", "Tayanchni poldagi payvand gaykaga mahkamlaydi."),
                        "Steel 8.8", fb, new[] { m.ZincSteel }, new Vector3(x + bx, fy + 0.0055f, zr1 - 0.028f), Quaternion.identity, true, Vector3.up, 0.4f);
            }
        }

        /// <summary>Octagonal crush tube along +Y from 0 to length, two trigger beads.</summary>
        private static void CrushTube(MeshKit k, float r, float length)
        {
            float ri = r - 0.0025f;
            var p = new List<Vector2> { new Vector2(ri, 0f), new Vector2(r, 0f) };
            foreach (float b in new[] { 0.045f, 0.095f })
            {
                p.Add(new Vector2(r, b - 0.007f));
                p.Add(new Vector2(r - 0.0045f, b));
                p.Add(new Vector2(r, b + 0.007f));
            }
            p.Add(new Vector2(r, length));
            p.Add(new Vector2(ri, length));
            p.Add(new Vector2(ri, 0f));
            k.Lathe(p, 8, 30f, 22.5f, 360f, true);
        }

        // ------------------------------------------------------------------------------------------------ sills

        private void Sills(Transform system)
        {
            const string id = "structure.sills";
            Transform asm = ctx.Assembly(system, id, T("Side Sills and Floor Rails", "Ostonalar va pol lonjeronlari",
                    "Closed sill boxes along both door openings, floor rails and seat crossmembers: the floor's backbone in bending and in a side impact.",
                    "Ikkala eshik ochig‘i bo‘ylab yopiq ostona qutilari, pol lonjeronlari va o‘rindiq ko‘ndalang to‘sinlari: egilish va yon zarbada polning tayanchi."),
                "Steel", Vector3.down, 0.8f);
            MeasureSills();
            foreach (float s in new[] { -1f, 1f }) Sill(asm, id, s);
            FloorRails(asm, id);
            SeatCrossmembers(asm, id);
        }

        private void MeasureSills()
        {
            float mid = 0.95f;
            // Ends: the wheel-well walls ahead of and behind the door opening.
            float yp = FloorTop(-mid, 0f) + 0.02f;
            float rearWall = -Ray(new Vector3(-mid, yp, 0f), Vector3.back, 1.4f, FloorNames);
            float frontWall = Ray(new Vector3(-mid, yp, 0f), Vector3.forward, 1.4f, FloorNames);
            sillRear = float.IsInfinity(rearWall) ? L.RearAxleZ + 0.48f : Mathf.Max(L.RearAxleZ + 0.4f, rearWall + 0.01f);
            sillFront = float.IsInfinity(frontWall) ? L.FrontAxleZ - 0.48f : Mathf.Min(L.FrontAxleZ - 0.4f, frontWall - 0.01f);
            // Width: between the body side (outer skin, lower panel) and the sill trim's inner wall, measured at every
            // height up to the trim above (the trim's inner wall steps outward higher up).
            float xo = 1.1f, xi = 0.8f;
            for (float z = sillRear + 0.01f; z <= sillFront - 0.01f; z += 0.025f)
                foreach (float s in new[] { -1f, 1f })
                {
                    float f = FloorTop(s * mid, z);
                    float top = 9f;
                    for (float ax = 0.86f; ax <= 1.041f; ax += 0.045f)
                    {
                        float fx = FloorTop(s * ax, z);
                        top = Mathf.Min(top, fx + 0.003f + Clear(s * ax, fx + 0.003f, z, 0.4f));
                    }
                    for (float y = f + 0.006f; y <= Mathf.Min(top - 0.004f, f + 0.13f); y += 0.012f)
                    {
                        float o = Ray(new Vector3(s * mid, y, z), new Vector3(s, 0f, 0f), 0.3f);
                        float i = Ray(new Vector3(s * mid, y, z), new Vector3(-s, 0f, 0f), 0.3f);
                        if (!float.IsInfinity(o)) xo = Mathf.Min(xo, mid + o);
                        if (!float.IsInfinity(i) && mid - i < 0.92f) xi = Mathf.Max(xi, mid - i);
                    }
                }
            sillOut = Mathf.Min(1.025f, xo - 0.02f);
            sillIn = Mathf.Clamp(xi + 0.005f, 0.80f, 0.90f);
            if (sillOut - sillIn < 0.1f) ctx.Warn("structure.sills: sill cavity only " + CarLayout.F(sillOut - sillIn) + " wide");
            ctx.Note("sills: |x| " + CarLayout.F(sillIn) + ".." + CarLayout.F(sillOut) + ", z " + CarLayout.F(sillRear) + ".." + CarLayout.F(sillFront));
        }

        private void Sill(Transform asm, string id, float s)
        {
            string side = s < 0 ? "left" : "right";
            string Side = s < 0 ? "Left" : "Right";
            string uz = s < 0 ? "Chap" : "O‘ng";
            const float t = 0.0025f;
            // Stations along z: floor below, trim above, across the sill's width.
            var zs = new List<float>();
            for (float z = sillRear; z < sillFront - 0.02f; z += 0.04f) zs.Add(z);
            zs.Add(sillFront);
            int n = zs.Count;
            var bot = new float[n];
            var top = new float[n];
            for (int i = 0; i < n; i++)
            {
                float fb = -9f, ct = 9f;
                for (int q = 0; q <= 6; q++)
                {
                    float ax = Mathf.Lerp(sillIn + 0.003f, sillOut - 0.003f, q / 6f);
                    foreach (float dz in new[] { -0.02f, 0f, 0.02f })
                    {
                        float f = FloorTop(s * ax, zs[i] + dz);
                        fb = Mathf.Max(fb, f);
                        ct = Mathf.Min(ct, f + 0.003f + Clear(s * ax, f + 0.003f, zs[i] + dz, 0.5f));
                    }
                }
                bot[i] = fb + 0.003f;
                top[i] = ct - 0.007f;
            }
            // Drop end stations where the trim leaves no room (the ends tuck under the arch trims).
            int first = 0, last = n - 1;
            while (first < n - 2 && top[first] - bot[first] < 0.024f) first++;
            while (last > first + 1 && top[last] - bot[last] < 0.024f) last--;
            if (first > 0 || last < n - 1)
            {
                zs = zs.GetRange(first, last - first + 1);
                bot = bot.Skip(first).Take(last - first + 1).ToArray();
                top = top.Skip(first).Take(last - first + 1).ToArray();
                n = zs.Count;
            }
            var b2 = (float[])bot.Clone();
            var t2 = (float[])top.Clone();
            for (int i = 0; i < n; i++)
            {
                for (int j = Mathf.Max(0, i - 1); j <= Mathf.Min(n - 1, i + 1); j++) { b2[i] = Mathf.Max(b2[i], bot[j]); t2[i] = Mathf.Min(t2[i], top[j]); }
                t2[i] = Mathf.Min(t2[i], b2[i] + 0.075f);
                if (t2[i] - b2[i] < 0.022f) { ctx.Warn("structure.sills." + side + ": only " + CarLayout.F(t2[i] - b2[i]) + " m under the trim at z " + CarLayout.F(zs[i])); t2[i] = b2[i] + 0.022f; }
            }
            float minH = 9f, maxH = 0f;
            for (int i = 0; i < n; i++) { minH = Mathf.Min(minH, t2[i] - b2[i]); maxH = Mathf.Max(maxH, t2[i] - b2[i]); }
            sillProfile[s < 0 ? 0 : 1] = (zs.ToArray(), (float[])t2.Clone());
            ctx.Note("sill " + side + ": height " + CarLayout.F(minH) + ".." + CarLayout.F(maxH));

            float xo = s * sillOut, xi = s * sillIn, xm = s * (sillIn + sillOut) * 0.5f, lap = s * 0.012f;
            var origins = zs.Select(z => new Vector3(0f, 0f, z)).ToList();
            var sides = Enumerable.Repeat(Vector3.right, n).ToList();
            var ups = Enumerable.Repeat(Vector3.up, n).ToList();
            var outer = new List<Vector2[]>();
            var inner = new List<Vector2[]>();
            var reinf = new List<Vector2[]>();
            for (int i = 0; i < n; i++)
            {
                float yb = b2[i], yt = t2[i];
                outer.Add(new[]
                {
                    new Vector2(xm - lap, yb), new Vector2(xo, yb), new Vector2(xo, yt), new Vector2(xm - lap, yt),
                    new Vector2(xm - lap, yt - t), new Vector2(xo - s * t, yt - t), new Vector2(xo - s * t, yb + t), new Vector2(xm - lap, yb + t)
                });
                inner.Add(new[]
                {
                    new Vector2(xi, yb + t), new Vector2(xm + lap, yb + t), new Vector2(xm + lap, yb + 2f * t), new Vector2(xi + s * t, yb + 2f * t),
                    new Vector2(xi + s * t, yt - 2f * t), new Vector2(xm + lap, yt - 2f * t), new Vector2(xm + lap, yt - t), new Vector2(xi, yt - t)
                });
                float rb = yb + t + 0.002f, rt = yt - t - 0.002f, ra = xm + s * 0.004f, rc = xo - s * (t + 0.003f), tr = 0.002f;
                reinf.Add(new[]
                {
                    new Vector2(ra, rb), new Vector2(ra + s * tr, rb), new Vector2(ra + s * tr, rt - tr), new Vector2(rc - s * tr, rt - tr),
                    new Vector2(rc - s * tr, rb), new Vector2(rc, rb), new Vector2(rc, rt), new Vector2(ra, rt)
                });
            }
            Transform sill = ctx.SubAssembly(asm, id + "." + side, T(Side + " Sill", uz + " ostona",
                    "Closed box of an outer and an inner pressing spot-welded along lap flanges, with a press-hardened insert; it carries the floor in bending and resists side impacts.",
                    "Ustma-ust flanetslar bo‘ylab nuqtali payvandlangan tashqi va ichki shtampovkadan iborat yopiq quti, ichida press-toblangan qo‘yma; polni egilishda ko‘taradi va yon zarbaga qarshilik qiladi."),
                "High-strength steel", new Vector3(s, 0f, 0f), 1f);
            Vector3 pivot = new Vector3(xm, (b2[n / 2] + t2[n / 2]) * 0.5f, (sillRear + sillFront) * 0.5f);
            Mesh mo = LoftMesh("sill-outer-" + side, pivot, k => StructureKit.Loft(k, outer, origins, sides, ups));
            Mesh mi = LoftMesh("sill-inner-" + side, pivot, k => StructureKit.Loft(k, inner, origins, sides, ups));
            Mesh mr = LoftMesh("sill-reinforcement-" + side, pivot, k => StructureKit.Loft(k, reinf, origins, sides, ups));
            ctx.Part(sill, id + "." + side + ".outer", T("Sill outer", "Ostona tashqi qismi",
                    "Outer C-pressing of the sill box in HSLA steel; its top and bottom legs lap over the inner pressing.",
                    "Ostona qutisining HSLA po‘latdan tashqi C-shtampovkasi; ustki va pastki qanotlari ichki qism ustiga tushadi."),
                "HSLA steel HC420LA, 1.2 mm", mo, new[] { ecoat }, pivot, Quaternion.identity, false, new Vector3(s, 0f, 0f), 0.8f);
            ctx.Part(sill, id + "." + side + ".inner", T("Sill inner", "Ostona ichki qismi",
                    "Inner C-pressing that closes the box toward the cabin; the floor and the seat crossmembers weld to it.",
                    "Qutini salon tomondan yopuvchi ichki C-shtampovka; pol va o‘rindiq to‘sinlari unga payvandlanadi."),
                "HSLA steel HC340LA, 1.0 mm", mi, new[] { ecoat }, pivot, Quaternion.identity, false, new Vector3(-s, 0f, 0f), 0.6f);
            ctx.Part(sill, id + "." + side + ".reinforcement", T("Sill reinforcement", "Ostona kuchaytirgichi",
                    "Hot-stamped boron-steel channel inside the box: keeps a side impact from crushing the sill.",
                    "Quti ichidagi issiq shtamplangan bor po‘lati kanal: yon zarbada ostonaning ezilishiga yo‘l qo‘ymaydi."),
                "Press-hardened 22MnB5, 1500 MPa", mr, new[] { hot }, pivot, Quaternion.identity, false, Vector3.up, 0.6f);
            foreach (bool frontEnd in new[] { false, true })
            {
                int i = frontEnd ? n - 1 : 0;
                float z = zs[i] + (frontEnd ? -0.006f : 0.006f);
                float h = t2[i] - b2[i];
                Mesh plate = ctx.Mesh("sill-closure-" + side + (frontEnd ? "-front" : "-rear"), k => k.Box(Vector3.zero, new Vector3(sillOut - sillIn, h, 0.004f)));
                ctx.Part(sill, id + "." + side + "." + (frontEnd ? "front" : "rear") + "-closure", T((frontEnd ? "Front" : "Rear") + " closing plate", (frontEnd ? "Old" : "Orqa") + " yopuvchi plastina",
                        "Welded end plate that closes the sill box against the wheel arch.", "Ostona qutisini g‘ildirak arkasi tomondan yopuvchi payvandlangan plastina."),
                    "Steel", plate, new[] { ecoat }, new Vector3(xm, (b2[i] + t2[i]) * 0.5f, z), Quaternion.identity, false, new Vector3(0f, 0f, frontEnd ? 1f : -1f), 0.5f);
            }
        }

        /// <summary>False in the combined car (see Build).</summary>
        public static readonly bool BuildFrontCrash = false;

        private float railX = 0.585f;

        private void FloorRails(Transform asm, string id)
        {
            Transform rails = ctx.SubAssembly(asm, id + ".floor-rails", T("Floor Rails", "Pol lonjeronlari",
                    "Two longitudinal hat sections welded to the floor between the wheel arches; they stiffen the floor and carry the seat loads.",
                    "G‘ildirak arkalari orasida polga payvandlangan ikki bo‘ylama shlyapa kesimli profil; polni qattiqlashtiradi va o‘rindiq yuklarini ko‘taradi."),
                "High-strength steel", Vector3.down, 0.7f);
            foreach (float s in new[] { -1f, 1f })
            {
                string side = s < 0 ? "left" : "right";
                float x = s * railX;
                float y0 = FloorTop(x, 0f) + 0.02f;
                float rear = -Ray(new Vector3(x, y0, 0f), Vector3.back, 1.4f, FloorNames);
                float front = Ray(new Vector3(x, y0, 0f), Vector3.forward, 1.4f, FloorNames);
                float zr = float.IsInfinity(rear) ? sillRear : rear + 0.01f;
                float zf = float.IsInfinity(front) ? sillFront : front - 0.01f;
                zr = Mathf.Max(zr, sillRear - 0.05f);
                zf = Mathf.Min(zf, sillFront + 0.05f);
                var zs = new List<float>();
                for (float z = zr; z < zf - 0.02f; z += 0.025f) zs.Add(z);
                zs.Add(zf);
                var fbs = new float[zs.Count];
                var cls = new float[zs.Count];
                for (int i = 0; i < zs.Count; i++)
                {
                    float fb = -9f, clear = 9f;
                    foreach (float dx in new[] { -0.036f, -0.018f, 0f, 0.018f, 0.036f })
                        foreach (float dz in new[] { -0.013f, 0f, 0.013f })
                        {
                            float f = FloorPan(x + dx, zs[i] + dz);
                            fb = Mathf.Max(fb, f);
                            clear = Mathf.Min(clear, f + 0.003f + Mathf.Min(0.3f, RayExcept(new Vector3(x + dx, f + 0.003f, zs[i] + dz), Vector3.up, 0.3f, FloorAndCarpet)));
                        }
                    fbs[i] = fb;
                    cls[i] = clear;
                }
                // Segments of stations with room for the hat (the side seats sit almost on the floor).
                var segs = new List<(List<float> z, List<Vector2[]> s)>();
                var curZ = new List<float>();
                var curS = new List<Vector2[]>();
                for (int i = 0; i < zs.Count; i++)
                {
                    float fb = fbs[i], clear = cls[i];
                    for (int j = Mathf.Max(0, i - 1); j <= Mathf.Min(zs.Count - 1, i + 1); j++) { fb = Mathf.Max(fb, fbs[j]); clear = Mathf.Min(clear, cls[j]); }
                    float h = Mathf.Min(clear - fb - 0.012f, 0.055f);
                    if (h >= 0.014f && i < zs.Count - 1 && !BoxFree(new Vector3(x - 0.035f, fb + 0.005f, zs[i]), new Vector3(x + 0.035f, fb + 0.004f + h, zs[i + 1]))) h = 0f;
                    if (h >= 0.014f)
                    {
                        curZ.Add(zs[i]);
                        curS.Add(StructureKit.Hat(x, 0.042f, fb + 0.004f, h, 0.013f, 0.002f));
                    }
                    if (h < 0.014f || i == zs.Count - 1)
                    {
                        if (curZ.Count >= 3) segs.Add((curZ, curS));
                        curZ = new List<float>();
                        curS = new List<Vector2[]>();
                    }
                }
                ctx.Note("floor rail " + side + ": " + segs.Count + " segment(s)");
                Vector3 pivot = new Vector3(x, FloorTop(x, 0f), (zr + zf) * 0.5f);
                Mesh mesh = LoftMesh("floor-rail-" + side, pivot, k =>
                {
                    foreach (var (sz, ss) in segs)
                        StructureKit.Loft(k, ss, sz.Select(z => new Vector3(0f, 0f, z)).ToList(), Enumerable.Repeat(Vector3.right, sz.Count).ToList(), Enumerable.Repeat(Vector3.up, sz.Count).ToList());
                });
                ctx.Part(rails, id + ".floor-rails." + side, T((s < 0 ? "Left" : "Right") + " floor rail", (s < 0 ? "Chap" : "O‘ng") + " pol lonjeroni",
                        "Top-hat rail spot-welded to the floor; its height follows the room under the seats.",
                        "Polga nuqtali payvandlangan shlyapa kesimli lonjeron; balandligi o‘rindiqlar ostidagi joyga moslangan."),
                    "Dual-phase steel DP600, 1.5 mm", mesh, new[] { ecoat }, pivot, Quaternion.identity, false, new Vector3(s * 0.4f, -1f, 0f), 0.6f);
            }
        }

        /// <summary>Rear and front of a side seat's shell at floor level (z), measured beside its centre line.</summary>
        private (float rear, float front) SideSeatSpan(float s)
        {
            float front = -9f, rear = 9f;
            foreach (float ax in new[] { 0.42f, 0.55f, 0.68f })
                foreach (float dy in new[] { 0.02f, 0.04f, 0.07f, 0.1f })
                {
                    float fy = FloorTop(s * ax, 0f) + dy;
                    float d = Ray(new Vector3(s * ax, fy, 0.75f), Vector3.back, 0.75f, "InteriorSeats");
                    if (!float.IsInfinity(d)) front = Mathf.Max(front, 0.75f - d);
                    d = Ray(new Vector3(s * ax, fy, -0.75f), Vector3.forward, 0.75f, "InteriorSeats");
                    if (!float.IsInfinity(d)) rear = Mathf.Min(rear, -0.75f + d);
                }
            if (front < -1f || front < 0f) front = 0.335f;
            if (rear > 1f || rear > 0f) rear = -0.295f;
            return (rear, front);
        }

        private void SeatCrossmembers(Transform asm, string id)
        {
            Transform cms = ctx.SubAssembly(asm, id + ".seat-crossmembers", T("Seat Crossmembers", "O‘rindiq ko‘ndalang to‘sinlari",
                    "Transverse hat sections ahead of and behind the side seats, from the floor rails out to the sills: with them the floor works as a ladder and the sills are braced in a side impact.",
                    "Yon o‘rindiqlar oldida va ortida pol lonjeronlaridan ostonalargacha ko‘ndalang shlyapa profillar: ular bilan pol narvon kabi ishlaydi va yon zarbada ostonalar tiraladi."),
                "High-strength steel", Vector3.up, 0.7f);
            foreach (float s in new[] { -1f, 1f })
            {
                string side = s < 0 ? "left" : "right";
                float sx = s * 0.55f;
                var (seatRear, seatFront) = SideSeatSpan(s);
                foreach (bool fr in new[] { false, true })
                {
                    float z = fr ? seatFront + 0.04f : seatRear - 0.04f;
                    float x0 = railX + 0.035f;
                    // Out to the sill trim's inner wall (the joint to the sill is behind the trim).
                    float x1 = sillIn;
                    // Integration (combined car): on the left the main harness, the battery cable and the rear brake
                    // lines run along the floor edge at |x| 0.70..0.75, so the crossmember stops short of that channel.
                    if (s < 0f) x1 = Mathf.Min(x1, 0.688f);
                    foreach (float dy in new[] { 0.008f, 0.02f, 0.035f })
                        foreach (float dz in new[] { -0.034f, 0f, 0.034f })
                        {
                            float f0 = FloorPan(s * 0.7f, z + dz);
                            float d = RayExcept(new Vector3(s * 0.7f, f0 + dy, z + dz), new Vector3(s, 0f, 0f), 0.3f, "InteriorFloormats");
                            if (!float.IsInfinity(d)) x1 = Mathf.Min(x1, 0.7f + d - 0.005f);
                        }
                    var xs = new List<float>();
                    for (float ax = x0; ax < x1 - 0.01f; ax += 0.02f) xs.Add(ax);
                    xs.Add(x1);
                    float fb = -9f, clear = 9f;
                    foreach (float ax in xs)
                        foreach (float dz in new[] { -0.034f, -0.017f, 0f, 0.017f, 0.034f })
                        {
                            float f = FloorPan(s * ax, z + dz);
                            fb = Mathf.Max(fb, f);
                            clear = Mathf.Min(clear, f + 0.003f + Mathf.Min(0.3f, RayExcept(new Vector3(s * ax, f + 0.003f, z + dz), Vector3.up, 0.3f, FloorAndCarpet)));
                        }
                    float h = Mathf.Clamp(clear - fb - 0.008f, 0.016f, 0.04f);
                    if (h > 0.016f && !BoxFree(new Vector3(Mathf.Min(s * x0, s * x1), fb + 0.004f, z - 0.034f), new Vector3(Mathf.Max(s * x0, s * x1), fb + 0.003f + h, z + 0.034f))) h = 0.016f;
                    var secs = new List<Vector2[]>();
                    foreach (float ax in xs) secs.Add(StructureKit.Hat(0f, 0.044f, fb + 0.003f, h, 0.012f, 0.002f));
                    var origins = xs.Select(ax => new Vector3(s * ax, 0f, z)).ToList();
                    Vector3 dir = new Vector3(s, 0f, 0f);
                    Vector3 sideAxis = Vector3.Cross(Vector3.up, dir);
                    Vector3 pivot = new Vector3(s * (x0 + x1) * 0.5f, fb, z);
                    string key = side + (fr ? "-front" : "-rear");
                    Mesh mesh = LoftMesh("seat-crossmember-" + key, pivot, k => StructureKit.Loft(k, secs, origins,
                        Enumerable.Repeat(sideAxis, xs.Count).ToList(), Enumerable.Repeat(Vector3.up, xs.Count).ToList()));
                    ctx.Part(cms, id + ".seat-crossmembers." + key, T((s < 0 ? "Left " : "Right ") + (fr ? "front" : "rear") + " seat crossmember",
                            (s < 0 ? "Chap " : "O‘ng ") + (fr ? "old" : "orqa") + " o‘rindiq to‘sini",
                            "Hat section spot-welded to the floor, the floor rail and, under the sill trim, the sill inner.",
                            "Polga, pol lonjeroniga va ostona qoplamasi ostida ostona ichki qismiga nuqtali payvandlangan shlyapa profil."),
                        "Dual-phase steel DP600, 1.2 mm", mesh, new[] { ecoat }, pivot, Quaternion.identity, false, Vector3.up, 0.5f);
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ cross-car beam

        private void CrossCarBeam(Transform system)
        {
            const string id = "structure.cross-car-beam";
            Transform asm = ctx.Assembly(system, id, T("Cross-Car Beam", "Ko‘ndalang panel to‘sini",
                    "Steel tube between the A-pillars behind the dashboard: carries the steering column, the knee bolsters and the centre stack, and ties the pillars together in a side impact.",
                    "Panel ortida A-ustunlar orasidagi po‘lat quvur: rul kolonkasi, tizza tayanchlari va markaziy konsolni ko‘taradi, yon zarbada ustunlarni bog‘laydi."),
                "Steel", Vector3.up, 1f);
            // Column bracket on the column jacket (Steering builder's geometry): top plate of the bracket.
            Vector3 axis = L.SteeringAxis;
            Vector3 start = L.SteeringWheelCentre + axis * 0.035f;
            Vector3 b0 = start + axis * 0.2f;
            Vector3 sideV = Vector3.Cross(axis, Vector3.up).normalized;
            Vector3 brUp = Vector3.Cross(sideV, axis).normalized;
            Vector3 bracketTop = b0 + brUp * 0.05f;
            // Beam centre: lowest free line above the steering hub and handles, behind the display.
            float yC = 0.725f, zC = bracketTop.z - 0.015f;
            const float rC = 0.032f, rO = 0.024f;
            float bestY = 9f, bestZ = zC;
            for (float z = bracketTop.z - 0.05f; z <= bracketTop.z + 0.01f; z += 0.01f)
                for (float y = bracketTop.y + 0.05f; y <= bracketTop.y + 0.16f; y += 0.005f)
                {
                    if (y >= bestY) break;
                    bool free = true;
                    for (int a = 0; a < 12 && free; a++)
                    {
                        float ang = a * Mathf.PI / 6f;
                        Vector3 o = new Vector3(-0.32f, y + Mathf.Sin(ang) * (rC + 0.004f), z + Mathf.Cos(ang) * (rC + 0.004f));
                        if (!float.IsInfinity(Ray(o, Vector3.right, 0.64f))) free = false;
                    }
                    if (free) { bestY = y; bestZ = z; }
                }
            if (bestY < 9f) { yC = bestY; zC = bestZ; } else ctx.Warn("structure.cross-car-beam: no free line found above the column, using defaults");
            // Ends: in the hinge pillars ahead of the door openings.
            float hz0 = HingeZ0, hz1 = HingeZ1;
            float zEnd = (hz0 + hz1) * 0.5f;
            float xEnd = 0.955f, yEnd = yC + 0.008f;
            foreach (float s in new[] { -1f, 1f })
            {
                var (hy, hx) = HingeRun(s);
                if (hy.Count >= 3)
                {
                    yEnd = (hy[0] + hy[hy.Count - 1]) * 0.5f;
                    for (int i = 0; i < hy.Count; i++)
                        if (Mathf.Abs(hy[i] - yEnd) < 0.05f) xEnd = Mathf.Min(xEnd, hx[i].x - 0.006f);
                }
                else xEnd = Mathf.Min(xEnd, HingeCavity(s, yEnd, hz0, hz1).xi - 0.006f);
            }
            Vector3 pc0 = new Vector3(-0.30f, yC, zC), pc1 = new Vector3(0.30f, yC, zC);
            ctx.Note("cross-car beam: centre y " + CarLayout.F(yC) + " z " + CarLayout.F(zC) + ", ends at |x| " + CarLayout.F(xEnd) + " y " + CarLayout.F(yEnd) + " z " + CarLayout.F(zEnd) + ", column bracket top " + CarLayout.V(bracketTop));

            Transform tube = ctx.SubAssembly(asm, id + ".tube", T("Beam Tube", "To‘sin quvuri",
                    "Stepped tube: a large centre section where the column hangs, smaller outer sections to the pillars.",
                    "Pog‘onali quvur: kolonka osilgan katta markaziy qism va ustunlarga boruvchi kichikroq chekka qismlar."),
                "Steel E355", Vector3.up, 0.7f);
            Vector3 pivotC = new Vector3(0f, yC, zC);
            Mesh centre = ctx.Mesh("ccb-centre-tube", k =>
            {
                k.Push(Vector3.zero, InternalsContext.AlongY(Vector3.right));
                k.Sleeve(rC, rC - 0.002f, -0.30f, 0.30f, 24);
                foreach (float e in new[] { -0.30f, 0.30f })
                    k.Lathe(new[] { new Vector2(rO, e - 0.004f), new Vector2(rC + 0.003f, e - 0.004f), new Vector2(rC + 0.003f, e + 0.004f), new Vector2(rO, e + 0.004f), new Vector2(rO, e - 0.004f) }, 24);
                k.Pop();
            });
            ctx.Part(tube, id + ".tube.centre", T("Centre tube", "Markaziy quvur",
                    "Ø64 welded steel tube with collars where the outer tubes are pressed in and welded.",
                    "Chekka quvurlar presslab payvandlanadigan halqali Ø64 payvand po‘lat quvur."),
                "Steel E355, Ø64 x 2", centre, new[] { ecoat }, pivotC, Quaternion.identity);
            foreach (float s in new[] { -1f, 1f })
            {
                string side = s < 0 ? "left" : "right";
                Vector3 a = new Vector3(s * 0.27f, yC, zC);
                Vector3 mid = new Vector3(s * Mathf.Min(0.70f, xEnd - 0.1f), Mathf.Lerp(yC, yEnd, 0.6f), Mathf.Lerp(zC, zEnd, 0.6f));
                Vector3 e = new Vector3(s * xEnd, yEnd, zEnd);
                List<Vector3> path = MeshKit.Bends(new[] { a, new Vector3(s * 0.48f, yC, zC), mid, e }, 0.12f, 8);
                Mesh om = ctx.Mesh("ccb-outer-tube-" + side, k =>
                {
                    k.Push(Matrix4x4.Translate(-a));
                    k.Tube(path, rO, 18, true, true, Vector3.up);
                    k.Pop();
                });
                ctx.Part(tube, id + ".tube." + side, T((s < 0 ? "Left" : "Right") + " outer tube", (s < 0 ? "Chap" : "O‘ng") + " chekka quvur",
                        "Ø48 bent tube from the centre section to the A-pillar end bracket.", "Markaziy qismdan A-ustun uchlik kronshteynigacha egilgan Ø48 quvur."),
                    "Steel E355, Ø48 x 1.8", om, new[] { ecoat }, a, Quaternion.identity, false, new Vector3(s, 0f, 0f), 0.6f);
            }

            Transform ends = ctx.SubAssembly(asm, id + ".end-brackets", T("Pillar End Brackets", "Ustun uchlik kronshteynlari",
                    "Pressed brackets welded to the tube ends and bolted to the A-pillar reinforcements.",
                    "Quvur uchlariga payvandlangan va A-ustun kuchaytirgichlariga boltlangan shtamplangan kronshteynlar."),
                "Pressed steel", Vector3.up, 0.6f);
            Mesh endPlate = ctx.Mesh("ccb-end-bracket", k =>
            {
                k.RoundedBox(Vector3.zero, new Vector3(0.005f, 0.072f, 0.064f), 0.002f, 1);
                k.Push(Vector3.zero, InternalsContext.AlongY(Vector3.right));
                k.Sleeve(rO + 0.004f, rO - 0.001f, -0.03f, 0.0f, 18);
                k.Pop();
            });
            Mesh boltM8 = ctx.Mesh("bolt-M8x25", k => Fasteners.HexBolt(k, 0.008f, 0.025f));
            foreach (float s in new[] { -1f, 1f })
            {
                string side = s < 0 ? "left" : "right";
                Vector3 p = new Vector3(s * (xEnd + 0.0025f), yEnd, zEnd);
                Quaternion r = s < 0 ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
                ctx.Part(ends, id + ".end-brackets." + side, T((s < 0 ? "Left" : "Right") + " end bracket", (s < 0 ? "Chap" : "O‘ng") + " uchlik kronshteyn",
                        "Flange plate with a socket for the tube; two bolts into the hinge pillar.", "Quvur uyasi bo‘lgan flanets plastina; ikki bolt bilan ustunga mahkamlanadi."),
                    "Pressed steel S420", endPlate, new[] { ecoat }, p, r, false, new Vector3(s, 0f, 0f), 0.5f);
                foreach (float dz in new[] { -0.022f, 0.022f })
                    ctx.Part(ends, id + ".end-brackets." + side + "-bolt-" + (dz < 0 ? "rear" : "front"), T("Bolt M8", "Bolt M8",
                            "Bolts the end bracket to the pillar's weld nut.", "Uchlik kronshteynni ustundagi payvand gaykaga mahkamlaydi."),
                        "Steel 8.8", boltM8, new[] { m.ZincSteel }, new Vector3(s * (xEnd - 0.0005f), yEnd + 0.024f, zEnd + dz), InternalsContext.AlongY(new Vector3(-s, 0f, 0f)), true, new Vector3(-s, 0f, 0f), 0.4f);
            }

            // Column mount: a base plate on the column bracket's top edge and a hanger up to the tube.
            Transform mount = ctx.SubAssembly(asm, id + ".column-mount", T("Steering Column Mount", "Rul kolonkasi tayanchi",
                    "Welded hanger and base plate that carry the column bracket; the column's two bolts go through the base.",
                    "Kolonka kronshteynini ko‘taruvchi payvandlangan osma va asos plastinasi; kolonkaning ikki bolti asosdan o‘tadi."),
                "Pressed steel", Vector3.up, 0.6f);
            Quaternion plateRot = Quaternion.LookRotation(axis, brUp);
            Mesh basePlate = ctx.Mesh("ccb-column-base", k => k.RoundedBox(Vector3.zero, new Vector3(0.116f, 0.004f, 0.05f), 0.0015f, 1));
            ctx.Part(mount, id + ".column-mount.base", T("Base plate", "Asos plastinasi",
                    "Thick plate on top of the column bracket with the two bolt holes.", "Kolonka kronshteyni ustidagi ikki bolt teshikli qalin plastina."),
                "Steel S355, 4 mm", basePlate, new[] { ecoat }, bracketTop, plateRot, false, Vector3.up, 0.4f);
            float hangLow = bracketTop.y + 0.002f, hangHigh = yC;
            float back = zC - bracketTop.z;
            Mesh hanger = ctx.Mesh("ccb-column-hanger", k =>
            {
                float h = hangHigh - hangLow;
                k.PlanarPrism(new[] { new Vector3(0f, 0f, -0.022f), new Vector3(0f, 0f, 0.022f), new Vector3(0f, h, back + 0.026f), new Vector3(0f, h, back - 0.026f) }, 0.004f);
            });
            foreach (float s in new[] { -1f, 1f })
                ctx.Part(mount, id + ".column-mount.hanger-" + (s < 0 ? "left" : "right"), T((s < 0 ? "Left" : "Right") + " hanger", (s < 0 ? "Chap" : "O‘ng") + " osma",
                        "Vertical plate welded between the tube and the base plate.", "Quvur va asos plastinasi orasiga payvandlangan tik plastina."),
                    "Steel S355, 4 mm", hanger, new[] { ecoat }, new Vector3(bracketTop.x + s * 0.03f, hangLow, bracketTop.z), Quaternion.identity, false, new Vector3(s, 0f, 0f), 0.4f);

            // Knee bolster brackets beside the column shroud.
            Transform knee = ctx.SubAssembly(asm, id + ".knee-bolsters", T("Knee Bolster Brackets", "Tizza tayanchi kronshteynlari",
                    "Energy-absorbing brackets under the beam either side of the column: they stop the driver's knees and fold in a controlled way.",
                    "Kolonkaning ikki tomonida to‘sin ostidagi energiya yutuvchi kronshteynlar: haydovchi tizzalarini to‘xtatadi va nazorat ostida buklanadi."),
                "Pressed steel", Vector3.back, 0.6f);
            foreach (float s in new[] { -1f, 1f })
            {
                Mesh kneeMesh = ctx.Mesh("ccb-knee-bracket-" + (s < 0 ? "left" : "right"), k =>
                {
                    k.PlanarPrism(new[] { new Vector3(0f, 0f, 0.03f), new Vector3(0f, 0f, -0.03f), new Vector3(0f, -0.21f, -0.11f), new Vector3(0f, -0.25f, -0.07f), new Vector3(0f, -0.25f, -0.035f) }, 0.004f);
                    k.Box(new Vector3(-s * 0.012f, -0.235f, -0.06f), new Vector3(0.024f, 0.03f, 0.004f));
                });
                ctx.Part(knee, id + ".knee-bolsters." + (s < 0 ? "left" : "right"), T((s < 0 ? "Left" : "Right") + " knee bracket", (s < 0 ? "Chap" : "O‘ng") + " tizza kronshteyni",
                        "Folded plate with a crush bead; the knee bolster trim clips to its foot.", "Ezilish bo‘rtmali bukilgan plastina; tizza tayanchi qoplamasi uning tagiga mahkamlanadi."),
                    "Steel DC04, 2 mm", kneeMesh, new[] { ecoat }, new Vector3(s * 0.235f, yC - rC + 0.004f, zC), Quaternion.identity, false, new Vector3(s * 0.3f, -1f, -0.3f), 0.5f);
            }

            Transform stack = ctx.SubAssembly(asm, id + ".centre-stack", T("Centre Stack Brackets", "Markaziy konsol kronshteynlari",
                    "Forward arms on the beam that carry the centre display and the climate module.", "Markaziy displey va iqlim modulini ko‘taruvchi to‘sindagi oldinga yo‘nalgan qo‘llar."),
                "Pressed steel", Vector3.forward, 0.6f);
            Mesh arm = ctx.Mesh("ccb-stack-arm", k =>
            {
                k.PlanarPrism(new[] { new Vector3(0f, -0.02f, 0f), new Vector3(0f, 0.02f, 0f), new Vector3(0f, 0.012f, 0.13f), new Vector3(0f, -0.012f, 0.13f) }, 0.004f);
                k.Box(new Vector3(0.012f, 0f, 0.128f), new Vector3(0.024f, 0.024f, 0.004f));
            });
            foreach (float s in new[] { -1f, 1f })
                ctx.Part(stack, id + ".centre-stack." + (s < 0 ? "left" : "right"), T((s < 0 ? "Left" : "Right") + " stack arm", (s < 0 ? "Chap" : "O‘ng") + " konsol qo‘li",
                        "Pressed arm with a nut plate at its tip.", "Uchida gayka plastinasi bo‘lgan shtamplangan qo‘l."),
                    "Steel DC04, 2 mm", arm, new[] { ecoat }, new Vector3(s * 0.33f, yC, zC + rC - 0.002f), s < 0 ? Quaternion.Euler(0f, 0f, 180f) : Quaternion.identity, false, Vector3.forward, 0.4f);
        }

        // ------------------------------------------------------------------------------------------------ seat frames

        private sealed class SeatSpec
        {
            public string Key, En, Uz;
            public float X, Z0, Z1, Half;
            public bool BuckleRight;
        }

        private void SeatFrames(Transform system)
        {
            const string id = "structure.seat-frames";
            Transform asm = ctx.Assembly(system, id, T("Seat Frames", "O‘rindiq karkaslari",
                    "Mounting frames of the four bucket seats: sliding tracks, side brackets and belt buckles bolted to the floor structure.",
                    "To‘rtta chuqur o‘rindiqning mahkamlash karkaslari: surilma relslar, yon kronshteynlar va kamar qulflari pol tuzilmasiga boltlangan."),
                "Steel", Vector3.up, 1f);
            var seats = new[]
            {
                new SeatSpec { Key = "driver", En = "Driver Seat Frame", Uz = "Haydovchi o‘rindig‘i karkasi", X = 0f, Z0 = 0.35f, Z1 = 0.98f, Half = 0.28f, BuckleRight = true },
                new SeatSpec { Key = "left", En = "Left Seat Frame", Uz = "Chap o‘rindiq karkasi", X = -0.54f, Z0 = -0.30f, Z1 = 0.33f, Half = 0.24f, BuckleRight = true },
                new SeatSpec { Key = "right", En = "Right Seat Frame", Uz = "O‘ng o‘rindiq karkasi", X = 0.54f, Z0 = -0.30f, Z1 = 0.33f, Half = 0.24f, BuckleRight = false },
                new SeatSpec { Key = "rear", En = "Rear Seat Frame", Uz = "Orqa o‘rindiq karkasi", X = 0f, Z0 = -1.075f, Z1 = -0.45f, Half = 0.28f, BuckleRight = true },
            };
            foreach (SeatSpec s in seats) SeatFrame(asm, id, s);
        }

        private void SeatFrame(Transform asm, string id, SeatSpec seat)
        {
            string sid = id + "." + seat.Key;
            Transform frame = ctx.SubAssembly(asm, sid, T(seat.En, seat.Uz,
                    "Two sliding tracks beside the shell, side brackets that hold the shell and the belt buckle.",
                    "Qobiq yonidagi ikki surilma rels, qobiqni ushlab turuvchi yon kronshteynlar va kamar qulfi."),
                "Steel", new Vector3(seat.X * 0.8f, 1f, 0f), 0.9f);
            // Shell extent at floor level and up to the bracket height, along the seat.
            float yF = FloorTop(seat.X, (seat.Z0 + seat.Z1) * 0.5f);
            float zFront = seat.Z1, zRear = seat.Z0;
            {
                float bestF = -9f, bestR = 9f;
                foreach (float dy in new[] { 0.015f, 0.04f, 0.07f })
                    foreach (float dx in new[] { -0.12f, 0f, 0.12f })
                    {
                        float d = Ray(new Vector3(seat.X + dx, yF + dy, seat.Z1 + 0.12f), Vector3.back, 0.3f, "InteriorSeats");
                        if (!float.IsInfinity(d)) bestF = Mathf.Max(bestF, seat.Z1 + 0.12f - d);
                        d = Ray(new Vector3(seat.X + dx, yF + dy, seat.Z0 - 0.12f), Vector3.forward, 0.3f, "InteriorSeats");
                        if (!float.IsInfinity(d)) bestR = Mathf.Min(bestR, seat.Z0 - 0.12f + d);
                    }
                if (bestF > -9f) zFront = bestF;
                if (bestR < 9f) zRear = bestR;
            }
            var half = new float[2];
            var halfHigh = new float[2];
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                float sgn = sideIndex == 0 ? -1f : 1f;
                float best = seat.Half - 0.04f, bestHigh = seat.Half - 0.04f;
                for (float z = zRear + 0.005f; z <= zFront - 0.005f; z += 0.02f)
                    foreach (float dy in new[] { 0.004f, 0.012f, 0.02f, 0.03f, 0.04f, 0.05f, 0.07f, 0.09f, 0.11f, 0.13f, 0.16f })
                    {
                        float from = seat.Half + 0.12f;
                        float d = Ray(new Vector3(seat.X + sgn * from, yF + dy, z), new Vector3(-sgn, 0f, 0f), 0.2f, "InteriorSeats");
                        if (float.IsInfinity(d) || from - d > seat.Half + 0.1f) continue;
                        if (dy <= 0.05f) best = Mathf.Max(best, from - d);
                        bestHigh = Mathf.Max(bestHigh, from - d);
                    }
                half[sideIndex] = best;
                halfHigh[sideIndex] = Mathf.Max(best, bestHigh);
            }
            ctx.Note("seat " + seat.Key + ": shell z " + CarLayout.F(zRear) + ".." + CarLayout.F(zFront) + ", half width at floor " + CarLayout.F(half[0]) + " / " + CarLayout.F(half[1]) + ", above " + CarLayout.F(halfHigh[0]) + " / " + CarLayout.F(halfHigh[1]));
            float railLen = Mathf.Max(0.2f, zFront - zRear + 0.02f);
            float zMid = (zFront + zRear) * 0.5f;
            Transform recliners = ctx.SubAssembly(frame, sid + ".recliners", T("Recliner Mechanisms", "Suyanchiq mexanizmlari",
                    "Pair of disc recliners at the backrest pivot, one on each side bracket.", "Suyanchiq o‘qidagi juft diskli mexanizm, har bir yon kronshteynda bittadan."),
                "Steel", Vector3.up, 0.5f);
            Transform tracks = ctx.SubAssembly(frame, sid + ".tracks", T("Seat Tracks", "O‘rindiq relslari",
                    "Pair of sliding tracks: the lower rails are bolted down, the upper rails carry the seat and lock in steps.",
                    "Juft surilma rels: pastki relslar polga boltlangan, ustki relslar o‘rindiqni ko‘taradi va pog‘onali qulflanadi."),
                "Steel", Vector3.up, 0.6f);
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                float sgn = sideIndex == 0 ? -1f : 1f;
                string sd = sideIndex == 0 ? "left" : "right";
                float inner = seat.X + sgn * (half[sideIndex] + 0.006f);
                float wall = 9f;
                for (float z = zRear; z <= zFront + 0.001f; z += 0.04f)
                    foreach (float dy in new[] { 0.006f, 0.018f, 0.03f })
                    {
                        float d = Ray(new Vector3(inner, FloorTop(inner, z) + dy, z), new Vector3(sgn, 0f, 0f), 0.1f);
                        if (!float.IsInfinity(d)) wall = Mathf.Min(wall, d);
                    }
                float railW = Mathf.Clamp(wall - 0.004f, 0.016f, 0.026f);
                float x = inner + sgn * railW * 0.5f;
                // Integration (combined car): the floor corridor left of the two central seats carries the fuel supply
                // and purge lines (x -0.30 / -0.289) and is no wider than the lines, so those seats keep only their
                // right-hand track there; the left side bracket stands on the floor beside the shell.
                bool corridor = sideIndex == 0 && (seat.Key == "driver" || seat.Key == "rear");
                float fy = Mathf.Max(FloorTop(x - railW * 0.5f, zMid), FloorTop(x + railW * 0.5f, zMid));
                float ceil = 9f;
                for (float z = zRear - 0.04f; z <= zFront + 0.04f; z += 0.01f)
                    foreach (float dx in new[] { -railW * 0.5f, 0f, railW * 0.5f })
                    {
                        float f = FloorTop(x + dx, z);
                        fy = Mathf.Max(fy, f);
                        ceil = Mathf.Min(ceil, f + 0.002f + Mathf.Min(0.06f, RayExcept(new Vector3(x + dx, f + 0.002f, z), Vector3.up, 0.06f, FloorAndCarpet)));
                    }
                float room = ceil - (fy + 0.001f) - 0.005f;
                float railH = Mathf.Clamp(room, 0.012f, 0.031f);
                if (room < 0.012f) ctx.Warn("structure.seat-frames." + seat.Key + ": only " + CarLayout.F(room) + " m under the shell edge for the " + sd + " track");
                foreach (float z in new[] { zRear, zFront }) fy = Mathf.Max(fy, Mathf.Max(FloorTop(x - 0.013f, z), FloorTop(x + 0.013f, z)));
                float lowH = Mathf.Round(railH * 0.65f * 1000f) / 1000f;
                Mesh lowerW = ctx.Mesh("seat-lower-rail-" + Mathf.RoundToInt(railLen * 1000f) + "-w" + Mathf.RoundToInt(railW * 1000f) + "-h" + Mathf.RoundToInt(lowH * 1000f), k =>
                {
                    k.Sweep(StructureKit.ChannelUp(railW, lowH, 0.0018f), new[] { new Vector3(0f, 0f, -railLen * 0.5f), new Vector3(0f, 0f, railLen * 0.5f) }, Vector3.up);
                    foreach (float e in new[] { -1f, 1f })
                        k.Box(new Vector3(0f, 0.002f, e * (railLen * 0.5f + 0.012f)), new Vector3(railW, 0.004f, 0.026f));
                });
                if (!corridor)
                ctx.Part(tracks, sid + ".tracks.lower-rail-" + sd, T((sideIndex == 0 ? "Left" : "Right") + " lower rail", (sideIndex == 0 ? "Chap" : "O‘ng") + " pastki rels",
                        "Roll-formed channel bolted down at both feet; holds the ball cages and the locking teeth.",
                        "Ikkala oyog‘idan boltlangan prokat kanal; sharik separatorlari va qulflash tishlarini ushlaydi."),
                    "Steel HC420, zinc plated", lowerW, new[] { m.ZincSteel }, new Vector3(x, fy + 0.001f, zMid), Quaternion.identity);
                float upH = railH - railH * 0.29f;
                Mesh upperW = ctx.Mesh("seat-upper-rail-" + Mathf.RoundToInt(railLen * 1000f) + "-h" + Mathf.RoundToInt(upH * 1000f), k =>
                {
                    float l = railLen - 0.06f;
                    k.Sweep(StructureKit.ChannelDown(0.014f, upH, 0.0016f), new[] { new Vector3(0f, 0f, -l * 0.5f), new Vector3(0f, 0f, l * 0.5f) }, Vector3.up);
                });
                if (!corridor)
                ctx.Part(tracks, sid + ".tracks.upper-rail-" + sd, T((sideIndex == 0 ? "Left" : "Right") + " upper rail", (sideIndex == 0 ? "Chap" : "O‘ng") + " ustki rels",
                        "Inverted channel that slides on balls inside the lower rail and carries the seat bracket.",
                        "Pastki rels ichida shariklarda suriladigan va o‘rindiq kronshteynini ko‘taruvchi teskari kanal."),
                    "Steel HC420, zinc plated", upperW, new[] { m.ZincSteel }, new Vector3(x, fy + 0.001f + railH * 0.29f, zMid + 0.02f), Quaternion.identity, false, Vector3.forward, 0.4f);
                // Side bracket from the upper rail up the shell side, at the backrest pivot.
                float bx = seat.X + sgn * (halfHigh[sideIndex] + 0.006f);
                float railTop = fy + 0.001f + railH;
                float zPivot = zRear + 0.09f;
                Mesh br = ctx.Mesh("seat-side-bracket", k => k.PlanarPrism(new[]
                {
                    new Vector3(0f, 0f, -0.075f), new Vector3(0f, 0f, 0.075f), new Vector3(0f, 0.07f, 0.04f), new Vector3(0f, 0.09f, 0f), new Vector3(0f, 0.07f, -0.04f)
                }, 0.004f));
                ctx.Part(frame, sid + ".bracket-" + sd, T((sideIndex == 0 ? "Left" : "Right") + " side bracket", (sideIndex == 0 ? "Chap" : "O‘ng") + " yon kronshteyn",
                        "Pressed bracket that bolts the shell's side to the upper rail and carries the backrest pivot.", "Qobiq yonini ustki relsga boltlovchi va suyanchiq o‘qini ko‘taruvchi shtamplangan kronshteyn."),
                    "Steel S420, 3 mm", br, new[] { m.BlackSteel }, new Vector3(bx, railTop, zPivot), Quaternion.identity, false, new Vector3(sgn, 0f, 0f), 0.5f);
                Mesh rec = ctx.Mesh("seat-recliner", k =>
                {
                    k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.026f, 0f), new Vector2(0.028f, 0.002f), new Vector2(0.028f, 0.009f), new Vector2(0.024f, 0.012f),
                        new Vector2(0.009f, 0.012f), new Vector2(0.009f, 0.018f), new Vector2(0f, 0.018f) }, 24, 40f);
                });
                ctx.Part(recliners, sid + ".recliners." + sd, T((sideIndex == 0 ? "Left" : "Right") + " recliner", (sideIndex == 0 ? "Chap" : "O‘ng") + " suyanchiq mexanizmi",
                        "Disc recliner: an eccentric gear set that locks the backrest angle; the two sides turn together on a synchronising rod.",
                        "Diskli suyanchiq mexanizmi: suyanchiq burchagini qulflovchi eksantrik tishli juftlik; ikki tomon sinxron val orqali birga buriladi."),
                    "Fine-blanked steel", rec, new[] { m.MachinedSteel }, new Vector3(bx + sgn * 0.002f, railTop + 0.055f, zPivot), InternalsContext.AlongY(new Vector3(sgn, 0f, 0f)), false, new Vector3(sgn, 0f, 0f), 0.4f);
                if (!corridor && Mathf.Abs(bx - x) > 0.006f)
                {
                    float w = Mathf.Abs(bx - x) + 0.004f;
                    Mesh tab = ctx.Mesh("seat-bracket-tab-" + Mathf.RoundToInt(w * 1000f), k => k.Box(new Vector3(0f, 0f, 0f), new Vector3(w, 0.003f, 0.05f)));
                    ctx.Part(frame, sid + ".tab-" + sd, T((sideIndex == 0 ? "Left" : "Right") + " rail tab", (sideIndex == 0 ? "Chap" : "O‘ng") + " rels qulog‘i",
                            "Welded tab that joins the side bracket to the upper rail.", "Yon kronshteynni ustki relsga ulovchi payvandlangan quloq."),
                        "Steel", tab, new[] { m.BlackSteel }, new Vector3((bx + x) * 0.5f, railTop + 0.0015f, zPivot), Quaternion.identity, true);
                }
            }
            // Adjuster lever on the outer front of the left track; belt buckle on the inner side.
            {
                // Central seats: on the right track (the left corridor carries the fuel lines).
                float sgn = seat.X < -0.1f ? -1f : 1f;
                int si = sgn < 0 ? 0 : 1;
                float x = seat.X + sgn * (half[si] + 0.006f + 0.008f);
                float fy = FloorTop(x, zFront);
                Mesh lever = ctx.Mesh("seat-adjuster-lever", k => k.Tube(MeshKit.Bends(new[] { new Vector3(0f, 0f, -0.04f), new Vector3(0f, 0f, 0.02f), new Vector3(0.0f, 0.012f, 0.06f), new Vector3(0f, 0.016f, 0.08f) }, 0.015f), 0.0045f, 8));
                ctx.Part(tracks, sid + ".tracks.adjuster", T("Adjuster lever", "Rostlash dastagi",
                        "Spring-loaded bar that lifts the locking pawls so the seat can slide.", "O‘rindiq surilishi uchun qulflash tilchalarini ko‘taruvchi prujinali dastak."),
                    "Steel rod, plastic grip", lever, new[] { m.BlackSteel }, new Vector3(x, fy + 0.026f, zFront - 0.01f), Quaternion.identity, true, Vector3.forward, 0.4f);
            }
            {
                float sgn = seat.BuckleRight ? 1f : -1f;
                int si = sgn < 0 ? 0 : 1;
                float x = seat.X + sgn * Mathf.Max(half[si] + 0.019f, halfHigh[si] + 0.017f);
                float z = zRear + (zFront - zRear) * 0.3f;
                float fy = FloorTop(x, z);
                Mesh buckle = ctx.Mesh("seat-belt-buckle", k =>
                {
                    k.Box(new Vector3(0f, 0.06f, 0f), new Vector3(0.004f, 0.11f, 0.018f));
                    k.RoundedBox(new Vector3(0f, 0.13f, 0f), new Vector3(0.022f, 0.05f, 0.034f), 0.006f, 2);
                });
                ctx.Part(frame, sid + ".buckle", T("Belt buckle", "Kamar qulfi",
                        "Buckle on a steel stalk anchored to the track, so the belt geometry moves with the seat.",
                        "Rels bilan bog‘langan po‘lat oyoqdagi qulf, kamar holati o‘rindiq bilan birga siljiydi."),
                    "Steel stalk, plastic buckle", buckle, new[] { m.BlackPlastic }, new Vector3(x, fy + 0.03f, z), Quaternion.identity, false, Vector3.up, 0.4f);
            }
        }

        // ------------------------------------------------------------------------------------------------ roll structure

        private void RollStructure(Transform system)
        {
            const string id = "structure.roll-structure";
            Transform asm = ctx.Assembly(system, id, T("Roll Structure", "Himoya karkasi",
                    "Press-hardened A- and B-pillar reinforcements and the overhead bows: the safety cage that keeps the cabin's shape in a rollover or side impact.",
                    "Press-toblangan A va B ustun kuchaytirgichlari hamda yuqori yoylar: ag‘darilish yoki yon zarbada salon shaklini saqlovchi xavfsizlik karkasi."),
                "Press-hardened steel", Vector3.up, 1.2f);
            foreach (float s in new[] { -1f, 1f })
            {
                APillar(asm, id, s);
                BPillar(asm, id, s);
            }
            Bows(asm, id);
        }

        private void APillar(Transform asm, string id, float s)
        {
            string side = s < 0 ? "left" : "right";
            Transform a = ctx.SubAssembly(asm, id + ".a-pillar-" + side, T((s < 0 ? "Left" : "Right") + " A-Pillar Reinforcement", (s < 0 ? "Chap" : "O‘ng") + " A-ustun kuchaytirgichi",
                    "Hinge-pillar box and a hot-formed tube along the windscreen edge up to the overhead header.",
                    "Lo‘lali ustun qutisi va old oyna cheti bo‘ylab yuqori to‘singacha boruvchi issiq shakllangan quvur."),
                "Press-hardened steel", new Vector3(s, 0.4f, 0f), 0.8f);
            // Hinge pillar: box in the cavity ahead of the door, from the wheel-arch top to the screen base.
            float z0 = HingeZ0, z1 = HingeZ1;
            const float x0 = 0.97f;
            var (ys, hx) = HingeRun(s);
            var secs = hx.Select(v => PillarSection(s, v.x, v.y, z0, z1, 0.0025f)).ToList();
            if (ys.Count >= 3)
            {
                // Sections in the (x, z) plane stacked along y: side = +x, up = +z... use a frame with up = +z.
                var origins = ys.Select(y => new Vector3(0f, y, 0f)).ToList();
                Vector3 pivot = new Vector3(s * x0, (ys[0] + ys[ys.Count - 1]) * 0.5f, (z0 + z1) * 0.5f);
                Mesh hinge = LoftMesh("a-pillar-hinge-" + side, pivot, k => StructureKit.Loft(k, secs, origins,
                    Enumerable.Repeat(Vector3.right, ys.Count).ToList(), Enumerable.Repeat(Vector3.forward, ys.Count).ToList()));
                ctx.Part(a, id + ".a-pillar-" + side + ".hinge-pillar", T("Hinge-pillar reinforcement", "Lo‘lali ustun kuchaytirgichi",
                        "Closed box ahead of the door that carries the hinges and the cross-car beam's end bracket.",
                        "Eshik oldidagi yopiq quti: lo‘lalar va ko‘ndalang to‘sin uchlik kronshteynini ko‘taradi."),
                    "Press-hardened 22MnB5, 1.6 mm", hinge, new[] { hot }, pivot, Quaternion.identity);
                ctx.Note("A-pillar " + side + ": hinge box y " + CarLayout.F(ys[0]) + ".." + CarLayout.F(ys[ys.Count - 1]));
            }
            else ctx.Warn("structure.roll-structure: no hinge-pillar cavity on the " + side);
            // Upper A-pillar: tube along the windscreen's side edge, under the pillar skin.
            List<Vector3> edge = ScreenEdge(s);
            if (edge.Count >= 3)
            {
                List<Vector3> path = MeshKit.CatmullRom(edge, 4);
                Vector3 pv = path[0];
                Mesh tube = ctx.Mesh("a-pillar-tube-" + side, k =>
                {
                    k.Push(Matrix4x4.Translate(-pv));
                    k.Tube(path, PillarTubeR, 12, true, true, Vector3.up);
                    k.Pop();
                });
                ctx.Part(a, id + ".a-pillar-" + side + ".tube", T("A-pillar tube", "A-ustun quvuri",
                        "Hot-formed ultra-high-strength steel tube inside the slim pillar: strength without blocking the view.",
                        "Ingichka ustun ichidagi issiq shakllangan o‘ta mustahkam po‘lat quvur: ko‘rinishni to‘smasdan mustahkamlik beradi."),
                    "Hot-formed steel tube, 1500 MPa", tube, new[] { hot }, pv, Quaternion.identity, false, new Vector3(s, 0.5f, 0f), 0.5f);
            }
        }

        /// <summary>Line just outboard of and under the windscreen's side edge, with clearance checked by rays.</summary>
        private List<Vector3> ScreenEdge(float s)
        {
            var result = new List<Vector3>();
            Transform ws = L.Car.Find("BodyWindshield");
            MeshFilter mf = ws != null ? ws.GetComponent<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null) return result;
            Vector3[] verts;
            try { verts = mf.sharedMesh.vertices; } catch { return result; }
            Matrix4x4 toCar = L.WorldToCar * ws.localToWorldMatrix;
            var pts = verts.Select(v => toCar.MultiplyPoint3x4(v)).Where(p => p.x * s > 0f).ToList();
            const float r = PillarTubeR;
            Vector3? prev = null;
            Vector2? prevOff = null;
            // Glass edge per station, smoothed along the pillar so the target line has no steps.
            var edges = new List<Vector3>();
            for (float z = 0.86f; z >= 0.0f; z -= 0.05f)
            {
                var bin = pts.Where(p => Mathf.Abs(p.z - z) < 0.04f).ToList();
                if (bin.Count == 0) continue;
                Vector3 raw = bin.OrderByDescending(p => p.x * s).First();
                edges.Add(new Vector3(raw.x, raw.y, z));
            }
            var smooth = new List<Vector3>();
            for (int i = 0; i < edges.Count; i++)
            {
                Vector3 sum = Vector3.zero;
                int cnt = 0;
                for (int j = Mathf.Max(0, i - 2); j <= Mathf.Min(edges.Count - 1, i + 2); j++) { sum += edges[j]; cnt++; }
                smooth.Add(new Vector3(sum.x / cnt, sum.y / cnt, edges[i].z));
            }
            foreach (Vector3 e in smooth)
            {
                float z = e.z;
                // Nearest free point to the line just under the glass edge (clear of glass, gasket, skin and trims).
                Vector3 want = new Vector3(e.x + s * 0.01f, e.y - 0.03f, z);
                Vector3 best = Vector3.zero;
                float bestD = 9f;
                for (float dx = -0.08f; dx <= 0.06f; dx += 0.006f)
                    for (float dy = -0.09f; dy <= 0.03f; dy += 0.006f)
                    {
                        Vector3 c = want + new Vector3(s * dx, dy, 0f);
                        Vector2 off = new Vector2(dx, dy);
                        float d2 = 0.15f * off.sqrMagnitude + (prevOff.HasValue ? 4f * (off - prevOff.Value).sqrMagnitude : 0f);
                        if (d2 >= bestD) continue;
                        bool free = true;
                        for (int a = 0; a < 12 && free; a++)
                        {
                            float ang = a * Mathf.PI / 6f;
                            Vector3 d = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
                            if (!float.IsInfinity(Ray(c, d, r + 0.004f))) free = false;
                        }
                        foreach (float dz in new[] { -0.03f, 0.03f })
                            if (free && !float.IsInfinity(Ray(c, new Vector3(0f, 0f, Mathf.Sign(dz)), Mathf.Abs(dz)))) free = false;
                        if (free && prev.HasValue)
                        {
                            Vector3 seg = c - prev.Value;
                            Vector3 u = Vector3.Cross(seg, Vector3.up).normalized, v = Vector3.Cross(u, seg).normalized;
                            foreach (Vector3 o in new[] { Vector3.zero, u * r, -u * r, v * r, -v * r })
                                if (free && !float.IsInfinity(Ray(prev.Value + o, seg, seg.magnitude))) free = false;
                        }
                        if (free) { best = c; bestD = d2; }
                    }
                if (bestD >= 9f) { if (result.Count >= 3) break; result.Clear(); prev = null; prevOff = null; continue; }
                if (prev.HasValue && (best - prev.Value).magnitude > 0.16f) { if (result.Count >= 3) break; result.Clear(); }
                result.Add(best);
                prev = best;
                prevOff = new Vector2(s * (best.x - want.x), best.y - want.y);
            }
            // Fair the found line; a point keeps its smoothed position only where the tube still clears everything.
            for (int pass = 0; pass < 4; pass++)
                for (int i = 1; i < result.Count - 1; i++)
                {
                    Vector3 c = (result[i - 1] + 2f * result[i] + result[i + 1]) * 0.25f;
                    bool free = true;
                    for (int a = 0; a < 12 && free; a++)
                    {
                        float ang = a * Mathf.PI / 6f;
                        if (!float.IsInfinity(Ray(c, new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f), r + 0.004f))) free = false;
                    }
                    foreach (Vector3 nb in new[] { result[i - 1], result[i + 1] })
                    {
                        Vector3 seg = nb - c;
                        if (free && !float.IsInfinity(Ray(c, seg, seg.magnitude))) free = false;
                    }
                    if (free) result[i] = c;
                }
            return result;
        }

        private const float PillarTubeR = 0.0095f;

        private void BPillar(Transform asm, string id, float s)
        {
            string side = s < 0 ? "left" : "right";
            float doorRear = DoorRearZ;
            float zc = doorRear - 0.05f, z0 = zc - 0.032f, z1 = zc + 0.032f;
            float x0 = 0.96f;
            var segs = new List<(List<float> ys, List<Vector2[]> secs)>();
            var ys = new List<float>();
            var secs = new List<Vector2[]>();
            float yLow = Mathf.Max(FloorTop(s * x0, zc, 0.45f) + 0.005f, SillTop(s, zc) + 0.006f);
            for (float y = yLow; y <= 0.95f; y += 0.02f)
            {
                float xo = 1.1f, xi = 0.8f;
                foreach (float z in new[] { z0 + 0.002f, zc - 0.016f, zc, zc + 0.016f, z1 - 0.002f })
                    foreach (float dy in new[] { -0.01f, 0f, 0.01f })
                    {
                        float o = Ray(new Vector3(s * x0, y + dy, z), new Vector3(s, 0f, 0f), 0.2f);
                        float i = Ray(new Vector3(s * x0, y + dy, z), new Vector3(-s, 0f, 0f), 0.2f);
                        if (!float.IsInfinity(o)) xo = Mathf.Min(xo, x0 + o);
                        if (!float.IsInfinity(i)) xi = Mathf.Max(xi, x0 - i);
                    }
                xo -= 0.006f; xi += 0.006f;
                float c = 9f;
                foreach (float ax in new[] { xi + 0.003f, (xi + xo) * 0.5f, xo - 0.003f })
                    foreach (float zz in new[] { z0 + 0.003f, zc, z1 - 0.003f })
                        c = Mathf.Min(c, Clear(s * ax, y, zz, 0.021f));
                bool ok = xo - xi >= 0.03f && c >= 0.0205f;
                if (ok)
                {
                    ys.Add(y);
                    secs.Add(PillarSection(s, xi, xo, z0, z1, 0.0025f));
                }
                else if (ys.Count > 0)
                {
                    if (ys.Count >= 3) segs.Add((ys, secs));
                    ys = new List<float>();
                    secs = new List<Vector2[]>();
                }
            }
            if (ys.Count >= 3) segs.Add((ys, secs));
            if (segs.Count == 0) { ctx.Warn("structure.roll-structure: no B-pillar cavity on the " + side); return; }
            Vector3 pivot = new Vector3(s * x0, segs[0].ys[0], zc);
            Mesh mesh = LoftMesh("b-pillar-" + side, pivot, k =>
            {
                foreach (var (sy, ss) in segs)
                    StructureKit.Loft(k, ss, sy.Select(y => new Vector3(0f, y, 0f)).ToList(), Enumerable.Repeat(Vector3.right, sy.Count).ToList(), Enumerable.Repeat(Vector3.forward, sy.Count).ToList());
            });
            Transform b = ctx.SubAssembly(asm, id + ".b-pillar-" + side, T((s < 0 ? "Left" : "Right") + " B-Pillar Reinforcement", (s < 0 ? "Chap" : "O‘ng") + " B-ustun kuchaytirgichi",
                    "Press-hardened pillar behind the door, from the sill to the overhead rail; the door's lock striker is on it.",
                    "Eshik ortidagi press-toblangan ustun, ostonadan yuqori relsgacha; eshik qulfining tutqichi unda."),
                "Press-hardened steel", new Vector3(s, 0f, -0.3f), 0.8f);
            ctx.Part(b, id + ".b-pillar-" + side + ".reinforcement", T("B-pillar reinforcement", "B-ustun kuchaytirgichi",
                    "Tailor-rolled boron-steel pressing: hard above to protect the occupants, softer at the foot to absorb energy.",
                    "Har xil qalinlikdagi bor po‘lati shtampovka: tepasi qattiq — yo‘lovchilarni himoya qiladi, pasti yumshoqroq — energiyani yutadi."),
                "Press-hardened 22MnB5, tailor rolled", mesh, new[] { hot }, pivot, Quaternion.identity);
            ctx.Note("B-pillar " + side + ": " + segs.Count + " segment(s), y " + CarLayout.F(segs[0].ys[0]) + ".." + CarLayout.F(segs[segs.Count - 1].ys[segs[segs.Count - 1].ys.Count - 1]));
        }

        private void Bows(Transform asm, string id)
        {
            Transform bows = ctx.SubAssembly(asm, id + ".bows", T("Overhead Bows", "Yuqori yoylar",
                    "Front header, centre bow and rear header under the fixed top skin: they hold the canopy rails apart and carry the top in a rollover.",
                    "Qo‘zg‘almas ustki qoplama ostidagi old to‘sin, o‘rta yoy va orqa to‘sin: kanopi relslarini ushlab turadi va ag‘darilishda ustki qismni ko‘taradi."),
                "Steel", Vector3.up, 0.7f);
            Transform roofT = L.Car.Find("BodyRoofPanel");
            float zA = 0.2f, zB = -0.53f;
            if (roofT != null && roofT.GetComponent<MeshFilter>() != null)
            {
                Mesh rm = roofT.GetComponent<MeshFilter>().sharedMesh;
                Matrix4x4 toCar = L.WorldToCar * roofT.localToWorldMatrix;
                try
                {
                    var vs = rm.vertices.Select(v => toCar.MultiplyPoint3x4(v)).ToList();
                    zA = vs.Max(p => p.z);
                    zB = vs.Min(p => p.z);
                }
                catch { }
            }
            // Between the windscreen's rear edge and the rear screen's front edge (the top skin overlaps both).
            float zWs = MeshBounds("BodyWindshield")?.min.z ?? zA;
            float zRw = MeshBounds("BodyRearPanelsColor1/BodyRearwindow")?.max.z ?? zB;
            float zF = Mathf.Min(zA, zWs) - 0.05f, zR = Mathf.Max(zB, zRw) + 0.05f;
            var spec = new[] { ("front", "Front header", "Old yuqori to‘sin", zF), ("centre", "Centre bow", "O‘rta yoy", (zF + zR) * 0.5f), ("rear", "Rear header", "Orqa yuqori to‘sin", zR) };
            foreach (var (key, en, uz, z) in spec)
            {
                var xs = new List<float>();
                var secs = new List<Vector2[]>();
                for (float x = -0.66f; x <= 0.661f; x += 0.03f)
                {
                    float yt = 9f, yb = -9f;
                    bool ok = true;
                    foreach (float dz in new[] { -0.036f, -0.018f, 0f, 0.018f, 0.036f })
                    {
                        float up = Ray(new Vector3(x, 0.9f, z + dz), Vector3.up, 0.4f, "BodyRoof");
                        if (float.IsInfinity(up)) { ok = false; break; }
                        float top = 0.9f + up;
                        // First surface under the skin (another skin layer, the headliner or the cage).
                        float down = Ray(new Vector3(x, top - 0.003f, z + dz), Vector3.down, 0.3f);
                        yt = Mathf.Min(yt, top - 0.005f);
                        yb = Mathf.Max(yb, float.IsInfinity(down) ? top - 0.3f : top - 0.003f - down);
                    }
                    if (!ok) continue;
                    float h = Mathf.Min(0.028f, yt - yb - 0.008f);
                    if (h < 0.012f) continue;
                    xs.Add(x);
                    secs.Add(StructureKit.HatDown(0f, 0.04f, yt, h, 0.012f, 0.0018f));
                }
                if (xs.Count < 3) { ctx.Warn("structure.roll-structure: no room under the top skin for the " + key + " bow"); continue; }
                Vector3 dir = Vector3.right;
                Vector3 sideAxis = Vector3.Cross(Vector3.up, dir);
                var origins = xs.Select(x => new Vector3(x, 0f, z)).ToList();
                Vector3 pivot = new Vector3(0f, 1.05f, z);
                Mesh mesh = LoftMesh("overhead-bow-" + key, pivot, k => StructureKit.Loft(k, secs, origins, Enumerable.Repeat(sideAxis, xs.Count).ToList(), Enumerable.Repeat(Vector3.up, xs.Count).ToList()));
                ctx.Part(bows, id + ".bows." + key, T(en, uz,
                        "Hat-section bow bonded to the top skin with structural adhesive and welded to the side rails.",
                        "Ustki qoplamaga konstruktiv yelim bilan yopishtirilgan va yon relslarga payvandlangan shlyapa kesimli yoy."),
                    "Steel DP600, 1.0 mm", mesh, new[] { ecoat }, pivot, Quaternion.identity, false, Vector3.up, 0.4f);
            }
        }

        // ------------------------------------------------------------------------------------------------ rear

        private void RearCrash(Transform system)
        {
            const string id = "structure.rear-crash";
            Transform asm = ctx.Assembly(system, id, T("Rear Crash Structure", "Orqa zarba yutish tuzilmasi",
                    "Rear impact beam and two crash boxes inside the rear fascia, bolted to the rear rail ends.",
                    "Orqa qoplama ichidagi orqa zarba to‘sini va ikki zarba qutisi, orqa lonjeron uchlariga boltlangan."),
                "Aluminium", Vector3.back, 1.2f);
            const float H = 0.09f, D = 0.042f, box = 0.03f;
            float floor = FloorTop(0f, -1.9f, 0.6f);
            float yc = floor + 0.025f + H * 0.5f;
            float xc = Mathf.Min(0.52f, L.RearWellWallX - 0.008f - box);
            int n = 17;
            float xe = xc + 0.045f;
            var zc = new float[n];
            var pts = new List<Vector3>();
            for (int i = 0; i < n; i++)
            {
                float x = Mathf.Lerp(-xe, xe, (float)i / (n - 1));
                float zf = L.RearBumperZ;
                foreach (float y in new[] { yc - H * 0.5f, yc - H * 0.25f, yc, yc + H * 0.25f, yc + H * 0.5f })
                    foreach (float dx in new[] { -0.03f, 0f, 0.03f })
                    {
                        float d = Ray(new Vector3(x + dx, y, -1.55f), Vector3.back, 0.6f, "Body");
                        if (!float.IsInfinity(d)) zf = Mathf.Max(zf, -1.55f - d);
                    }
                zc[i] = zf + 0.016f + D * 0.5f;
            }
            for (int pass = 0; pass < 3; pass++)
                for (int i = 1; i < n - 1; i++) zc[i] = Mathf.Max(zc[i], (zc[i - 1] + 2f * zc[i] + zc[i + 1]) * 0.25f);
            for (int i = 0; i < n; i++) pts.Add(new Vector3(Mathf.Lerp(-xe, xe, (float)i / (n - 1)), yc, zc[i]));
            float BeamZ(float x)
            {
                float t = Mathf.InverseLerp(-xe, xe, x) * (n - 1);
                int i0 = Mathf.Clamp(Mathf.FloorToInt(t), 0, n - 2);
                return Mathf.Lerp(zc[i0], zc[i0 + 1], t - i0);
            }
            List<Vector3> path = MeshKit.CatmullRom(pts, 3);
            ctx.Note("rear crash: beam y " + CarLayout.F(yc) + " z " + CarLayout.F(zc[n / 2]) + ", crash boxes x " + CarLayout.F(xc));
            Transform beam = ctx.SubAssembly(asm, id + ".beam", T("Rear Impact Beam", "Orqa zarba to‘sini",
                    "Two-chamber aluminium extrusion across the tail that spreads a rear impact over both crash boxes.",
                    "Orqa zarbani ikkala zarba qutisiga taqsimlovchi ikki kamerali ko‘ndalang alyuminiy profil."),
                "Aluminium 7003-T7", Vector3.back, 0.8f);
            Vector3 pivot = new Vector3(0f, yc, zc[n / 2]);
            Mesh beamMesh = LoftMesh("rear-impact-beam", pivot, k =>
            {
                k.BoxTube(path, D, H, 0.003f, Vector3.up);
                k.Sweep(Profiles.Rect(D - 0.005f, 0.003f), path, Vector3.up, false, false);
            });
            ctx.Part(beam, id + ".beam.extrusion", T("Beam extrusion", "To‘sin profili",
                    "Stretch-bent extrusion with a centre web; the two cells fold progressively in a rear impact.",
                    "Markaziy devorli egib cho‘zilgan profil; orqa zarbada ikki kamera bosqichma-bosqich buklanadi."),
                "Aluminium 7003-T7", beamMesh, new[] { alu }, pivot, Quaternion.identity);
            // Its own mesh key: the front beam's bush (deeper beam) is not built in the combined car, and an empty
            // builder here left the rear bush without geometry.
            Mesh tow = ctx.Mesh("towing-eye-bush-rear", k =>
            {
                k.Lathe(new[] { new Vector2(0.008f, -D * 0.5f - 0.002f), new Vector2(0.013f, -D * 0.5f - 0.002f), new Vector2(0.013f, D * 0.5f), new Vector2(0.016f, D * 0.5f),
                    new Vector2(0.016f, D * 0.5f + 0.004f), new Vector2(0.008f, D * 0.5f + 0.004f), new Vector2(0.008f, -D * 0.5f - 0.002f) }, 16);
            });
            Vector3 towAt = new Vector3(-0.2f, yc, BeamZ(-0.2f));
            ctx.Part(beam, id + ".beam.towing-eye-bush", T("Towing-eye bush", "Shatakka olish vtulkasi",
                    "Threaded bush welded through the beam for the screw-in towing eye.", "Buraladigan shatak ilmog‘i uchun to‘sin orqali payvandlangan rezbali vtulka."),
                "Steel, M24 thread", tow, new[] { m.ZincSteel }, towAt, InternalsContext.AlongY(Vector3.back), false, Vector3.back, 0.5f);
            Mesh tube = ctx.Mesh("crash-box-rear", k => CrushTube(k, box, 0.12f));
            Mesh plate = ctx.Mesh("crash-box-plate-rear", k => k.RoundedBox(Vector3.zero, new Vector3(0.09f, 0.085f, 0.006f), 0.004f, 1));
            Mesh bolt = ctx.Mesh("bolt-M10x30", k => Fasteners.HexBolt(k, 0.01f, 0.03f));
            foreach (float s in new[] { -1f, 1f })
            {
                string side = s < 0 ? "left" : "right";
                float x = s * xc;
                float z0 = BeamZ(x) + D * 0.5f;
                Transform cb = ctx.SubAssembly(asm, id + ".crash-box-" + side, T((s < 0 ? "Left" : "Right") + " Rear Crash Box", (s < 0 ? "Chap" : "O‘ng") + " orqa zarba qutisi",
                        "Octagonal aluminium crush can between the beam and the rear rail end; replaceable after a minor impact.",
                        "To‘sin va orqa lonjeron uchi orasidagi sakkiz qirrali alyuminiy ezilish qutisi; yengil zarbadan keyin almashtiriladi."),
                    "Aluminium 6063-T6", new Vector3(s * 0.6f, 0f, -0.8f), 0.9f);
                ctx.Part(cb, id + ".crash-box-" + side + ".beam-plate", T("Beam flange plate", "To‘sin flanets plastinasi",
                        "Welded plate that the beam bolts to.", "To‘sin boltlanadigan payvandlangan plastina."),
                    "Aluminium 6082-T6", plate, new[] { alu }, new Vector3(x, yc, z0 + 0.003f), Quaternion.identity);
                ctx.Part(cb, id + ".crash-box-" + side + ".tube", T("Crush tube", "Ezilish quvuri",
                        "Thin-wall octagonal extrusion with two trigger beads.", "Ikki boshlovchi bo‘rtmali yupqa devorli sakkiz qirrali profil."),
                    "Aluminium 6063-T6", tube, new[] { alu }, new Vector3(x, yc, z0 + 0.006f), InternalsContext.AlongY(Vector3.forward), false, Vector3.back, 0.5f);
                float z1 = z0 + 0.006f + 0.12f;
                ctx.Part(cb, id + ".crash-box-" + side + ".rail-plate", T("Rail flange plate", "Lonjeron flanets plastinasi",
                        "Front plate bolted to the rear rail's end flange.", "Orqa lonjeron uchidagi flanetsga boltlanadigan old plastina."),
                    "Aluminium 6082-T6", plate, new[] { alu }, new Vector3(x, yc, z1 + 0.003f), Quaternion.identity);
                int b = 0;
                foreach (float bx in new[] { -0.033f, 0.033f })
                    foreach (float by in new[] { -0.03f, 0.03f })
                        ctx.Part(cb, id + ".crash-box-" + side + ".bolt-" + (++b), T("Flange bolt M10", "Flanets bolti M10",
                                "Bolt into the weld nut of the rail end, property class 10.9.", "Lonjeron uchidagi payvand gaykaga buraladigan bolt, 10.9 sinf."),
                            "Steel 10.9, zinc flake", bolt, new[] { m.ZincSteel }, new Vector3(x + bx, yc + by, z1), InternalsContext.AlongY(Vector3.back), true, Vector3.back, 0.4f);
            }
        }
    }

    /// <summary>
    /// Section helpers for pressed body members (candidates for the kit): a flat-shaded loft through stations whose
    /// sections change size along the path, and channel / hat outlines placed by their real edges.
    /// </summary>
    public static class StructureKit
    {
        /// <summary>
        /// Loft through closed sections (same vertex count) given in each station's (side, up) frame; flat-shaded
        /// quads, caps at both ends. Use it where a member must follow measured free space (its height changes).
        /// </summary>
        public static void Loft(MeshKit k, IList<Vector2[]> sections, IList<Vector3> origins, IList<Vector3> sides, IList<Vector3> ups, bool capStart = true, bool capEnd = true)
        {
            int n = sections.Count;
            if (n < 2) return;
            int m = sections[0].Length;
            float sign = MeshKit.SignedArea(sections[0]) >= 0f ? 1f : -1f;
            Vector3 P(int i, int j) => origins[i] + sides[i] * sections[i][j].x + ups[i] * sections[i][j].y;
            float along = 0f;
            for (int i = 0; i < n - 1; i++)
            {
                float step = (origins[i + 1] - origins[i]).magnitude;
                float u = 0f;
                for (int j = 0; j < m; j++)
                {
                    int j1 = (j + 1) % m;
                    Vector3 a = P(i, j), b = P(i, j1), c = P(i + 1, j1), d = P(i + 1, j);
                    Vector2 e = sections[i][j1] - sections[i][j];
                    Vector2 n2 = new Vector2(e.y, -e.x) * sign;
                    Vector3 hint = sides[i] * n2.x + ups[i] * n2.y;
                    Vector3 nrm = Vector3.Cross(b - a, d - a);
                    if (nrm.sqrMagnitude < 1e-14f) nrm = Vector3.Cross(c - d, a - d);
                    if (nrm.sqrMagnitude < 1e-14f) nrm = hint;
                    nrm.Normalize();
                    if (Vector3.Dot(nrm, hint) < 0f) nrm = -nrm;
                    float el = e.magnitude;
                    int ia = k.Vertex(a, nrm, new Vector2(u, along)), ib = k.Vertex(b, nrm, new Vector2(u + el, along));
                    int ic = k.Vertex(c, nrm, new Vector2(u + el, along + step)), id = k.Vertex(d, nrm, new Vector2(u, along + step));
                    k.Quad(ia, ib, ic, id);
                    u += el;
                }
                along += step;
            }
            if (capStart) k.PolygonCap(sections[0], origins[0], sides[0], ups[0], (origins[0] - origins[1]).normalized);
            if (capEnd) k.PolygonCap(sections[n - 1], origins[n - 1], sides[n - 1], ups[n - 1], (origins[n - 1] - origins[n - 2]).normalized);
        }

        /// <summary>Top-hat outline (crown up, flanges on the bottom at yBottom), centred on x = xc.</summary>
        public static Vector2[] Hat(float xc, float w, float yBottom, float h, float flange, float t)
        {
            Vector2[] p = Profiles.Hat(w, h, flange, t);
            var r = new Vector2[p.Length];
            for (int i = 0; i < p.Length; i++) r[i] = new Vector2(p[i].x + xc, p[i].y + h * 0.5f + yBottom);
            return r;
        }

        /// <summary>Hat hanging down from yTop (crown down, flanges on top against a skin).</summary>
        public static Vector2[] HatDown(float xc, float w, float yTop, float h, float flange, float t)
        {
            Vector2[] p = Profiles.Hat(w, h, flange, t);
            var r = new Vector2[p.Length];
            for (int i = 0; i < p.Length; i++) r[p.Length - 1 - i] = new Vector2(p[i].x + xc, yTop - (p[i].y + h * 0.5f));
            return r;
        }

        /// <summary>Channel open at the top (U), width w, height h, bottom at y = 0.</summary>
        public static Vector2[] ChannelUp(float w, float h, float t) => new[]
        {
            new Vector2(-w * 0.5f, 0f), new Vector2(w * 0.5f, 0f), new Vector2(w * 0.5f, h), new Vector2(w * 0.5f - t, h),
            new Vector2(w * 0.5f - t, t), new Vector2(-w * 0.5f + t, t), new Vector2(-w * 0.5f + t, h), new Vector2(-w * 0.5f, h)
        };

        /// <summary>Channel open at the bottom (inverted U), bottom edge at y = 0.</summary>
        public static Vector2[] ChannelDown(float w, float h, float t) => new[]
        {
            new Vector2(-w * 0.5f, 0f), new Vector2(-w * 0.5f + t, 0f), new Vector2(-w * 0.5f + t, h - t), new Vector2(w * 0.5f - t, h - t),
            new Vector2(w * 0.5f - t, 0f), new Vector2(w * 0.5f, 0f), new Vector2(w * 0.5f, h), new Vector2(-w * 0.5f, h)
        };
    }
}
#endif
