#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace CarInternals
{
    /// <summary>
    /// Temporary physics copies of the hero car's own meshes (body, cabin, wheels, V6, never the internals) on a free
    /// layer, for measuring the layout with rays and for checking that generated parts stay clear of the car.
    /// Car-space queries in, car-space answers out. Dispose removes every temporary object.
    /// </summary>
    public sealed class InternalsProbe : IDisposable
    {
        public const int EnvLayer = 31;
        public const int PartLayer = 30;
        private readonly CarLayoutFrame frame;
        private readonly GameObject envRoot;
        private readonly Dictionary<Collider, string> envNames = new Dictionary<Collider, string>();
        private readonly List<(Mesh mesh, Matrix4x4 toWorld, string name)> envMeshes = new List<(Mesh, Matrix4x4, string)>();

        /// <summary>Car-space frame used before the layout exists (the layout fills it in).</summary>
        public sealed class CarLayoutFrame
        {
            public Matrix4x4 CarToWorld = Matrix4x4.identity, WorldToCar = Matrix4x4.identity;
        }

        public CarLayoutFrame Frame => frame;

        public InternalsProbe(Transform car, Transform internalsRoot)
        {
            frame = new CarLayoutFrame();
            envRoot = new GameObject("Car Internals Probe (temporary)") { hideFlags = HideFlags.HideAndDontSave };
            foreach (Renderer r in car.GetComponentsInChildren<Renderer>(true))
            {
                if (internalsRoot != null && r.transform.IsChildOf(internalsRoot)) continue;
                Mesh m = r.GetComponent<MeshFilter>()?.sharedMesh;
                if (m == null || !r.enabled) continue;
                string name = EnvName(car, r.transform);
                var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave, layer = EnvLayer };
                go.transform.SetParent(envRoot.transform, false);
                go.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                go.transform.localScale = r.transform.lossyScale;
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = m;
                envNames[mc] = name;
                envMeshes.Add((m, r.transform.localToWorldMatrix, name));
            }
            Physics.SyncTransforms();
        }

        /// <summary>"V6/&lt;node&gt;" for the engine, the top-level node name for the car ("CarConcept" for its own mesh).</summary>
        private static string EnvName(Transform car, Transform t)
        {
            if (t == car) return "CarConcept";
            Transform v6 = car.Find(CarLayout.V6Name);
            if (v6 != null && t.IsChildOf(v6))
            {
                Transform top = t;
                while (top.parent != null && top.parent != v6) top = top.parent;
                return "V6/" + top.name;
            }
            Transform node = t;
            while (node.parent != null && node.parent != car) node = node.parent;
            return node.name == t.name ? t.name : node.name + "/" + t.name;
        }

        /// <summary>First hit of a car-space ray on the car's surfaces whose name starts with one of the prefixes (all when none).</summary>
        public bool Cast(Vector3 origin, Vector3 direction, float distance, out float hitDistance, params string[] prefixes)
        {
            hitDistance = 0f;
            Vector3 o = frame.CarToWorld.MultiplyPoint3x4(origin);
            Vector3 d = frame.CarToWorld.MultiplyVector(direction).normalized;
            bool backfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            RaycastHit[] hits = Physics.RaycastAll(o, d, distance, 1 << EnvLayer, QueryTriggerInteraction.Ignore);
            Physics.queriesHitBackfaces = backfaces;
            float best = float.MaxValue;
            foreach (RaycastHit h in hits)
            {
                if (!envNames.TryGetValue(h.collider, out string name)) continue;
                if (prefixes.Length > 0 && !prefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal))) continue;
                best = Mathf.Min(best, h.distance);
            }
            if (best == float.MaxValue) return false;
            hitDistance = best;
            return true;
        }

        public bool Down(Vector3 origin, float distance, out float y)
        {
            y = 0f;
            if (!Cast(origin, Vector3.down, distance, out float d, "InteriorFloor", "CarConcept")) return false;
            y = origin.y - d;
            return true;
        }

        public bool Up(Vector3 origin, float distance, out float y)
        {
            y = 0f;
            if (!Cast(origin, Vector3.up, distance, out float d, "InteriorFloor", "CarConcept")) return false;
            y = origin.y + d;
            return true;
        }

        // ------------------------------------------------------------------------------------------------ checks

        /// <summary>
        /// Interference check of the generated parts against the car (both ways: part edges through car surfaces and
        /// car edges through parts) and between parts of different assemblies. Returns one line per offending pair;
        /// pairs allowed by <paramref name="allowed"/> (part id, other name) are skipped.
        /// </summary>
        public List<string> Interference(Transform internalsRoot, Func<string, string, bool> allowed, out int checkedParts)
        {
            var lines = new List<string>();
            var parts = internalsRoot.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).ToList();
            checkedParts = parts.Count;
            var partRoot = new GameObject("Car Internals Probe Parts (temporary)") { hideFlags = HideFlags.HideAndDontSave };
            var partOf = new Dictionary<Collider, CarPartInfo>();
            bool backfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            try
            {
                foreach (MeshFilter f in parts)
                {
                    var go = new GameObject(f.name) { hideFlags = HideFlags.HideAndDontSave, layer = PartLayer };
                    go.transform.SetParent(partRoot.transform, false);
                    go.transform.SetPositionAndRotation(f.transform.position, f.transform.rotation);
                    go.transform.localScale = f.transform.lossyScale;
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = f.sharedMesh;
                    partOf[mc] = f.GetComponent<CarPartInfo>();
                }
                Physics.SyncTransforms();
                var found = new Dictionary<string, int>();
                void Note(CarPartInfo part, string other)
                {
                    if (part == null) return;
                    if (allowed != null && allowed(part.id, other)) return;
                    string key = part.id + " x " + other;
                    found[key] = found.TryGetValue(key, out int n) ? n + 1 : 1;
                }
                // 1) part edges through car surfaces and through other assemblies' parts
                foreach (MeshFilter f in parts)
                {
                    CarPartInfo info = f.GetComponent<CarPartInfo>();
                    string assembly = AssemblyOf(info);
                    Matrix4x4 m = f.transform.localToWorldMatrix;
                    foreach (var (a, b) in Edges(f.sharedMesh))
                    {
                        Vector3 pa = m.MultiplyPoint3x4(a), pb = m.MultiplyPoint3x4(b);
                        Vector3 d = pb - pa;
                        float len = d.magnitude;
                        if (len < 1e-5f) continue;
                        foreach (RaycastHit h in Physics.RaycastAll(pa, d / len, len, (1 << EnvLayer) | (1 << PartLayer), QueryTriggerInteraction.Ignore))
                        {
                            if (envNames.TryGetValue(h.collider, out string env)) Note(info, env);
                            else if (partOf.TryGetValue(h.collider, out CarPartInfo other) && other != null && other != info && AssemblyOf(other) != assembly)
                                Note(info, other.id);
                        }
                    }
                }
                // 2) car edges through parts (near the internals only)
                Bounds near = new Bounds();
                bool any = false;
                foreach (MeshFilter f in parts)
                {
                    Bounds b = f.GetComponent<Renderer>().bounds;
                    if (any) near.Encapsulate(b); else { near = b; any = true; }
                }
                foreach (var (mesh, toWorld, name) in envMeshes)
                {
                    Vector3[] verts;
                    try { verts = mesh.vertices; } catch { continue; }
                    var world = new Vector3[verts.Length];
                    for (int i = 0; i < verts.Length; i++) world[i] = toWorld.MultiplyPoint3x4(verts[i]);
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        int[] tris = mesh.GetTriangles(s);
                        for (int t = 0; t < tris.Length; t += 3)
                            for (int e = 0; e < 3; e++)
                            {
                                Vector3 pa = world[tris[t + e]], pb = world[tris[t + (e + 1) % 3]];
                                if (!near.Contains(pa) && !near.Contains(pb)) continue;
                                Vector3 d = pb - pa;
                                float len = d.magnitude;
                                if (len < 1e-5f) continue;
                                if (!Physics.Raycast(pa, d / len, out RaycastHit h, len, 1 << PartLayer, QueryTriggerInteraction.Ignore)) continue;
                                if (partOf.TryGetValue(h.collider, out CarPartInfo part)) Note(part, name);
                            }
                    }
                }
                foreach (var kv in found.OrderBy(k => k.Key)) lines.Add(kv.Key + " (" + kv.Value + " edges)");
            }
            finally
            {
                Physics.queriesHitBackfaces = backfaces;
                UnityEngine.Object.DestroyImmediate(partRoot);
            }
            return lines;
        }

        private static string AssemblyOf(CarPartInfo info)
        {
            if (info == null) return "";
            for (Transform t = info.transform; t != null; t = t.parent)
            {
                CarPartInfo p = t.GetComponent<CarPartInfo>();
                if (p != null && p.kind == CarNodeKind.Assembly) return p.id;
            }
            return info.id;
        }

        private static IEnumerable<(Vector3, Vector3)> Edges(Mesh mesh)
        {
            Vector3[] v = mesh.vertices;
            var seen = new HashSet<long>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                int[] tris = mesh.GetTriangles(s);
                for (int t = 0; t < tris.Length; t += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        int a = tris[t + e], b = tris[t + (e + 1) % 3];
                        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                        if (seen.Add(key)) yield return (v[a], v[b]);
                    }
            }
        }

        public void Dispose()
        {
            if (envRoot != null) UnityEngine.Object.DestroyImmediate(envRoot);
            Physics.SyncTransforms();
        }
    }
}
#endif
