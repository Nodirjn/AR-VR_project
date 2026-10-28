#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AurelleRealism
{
    /// <summary>
    /// Procedural geometry for the interior pass. All UVs are in metres (box or cylindrical projection), so one
    /// material tiling value (1 / texture size in metres) gives the same texel density on every mesh.
    /// </summary>
    internal sealed class InteriorMeshBuilder
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<List<int>> submeshes = new List<List<int>>();

        public InteriorMeshBuilder(int submeshCount)
        {
            for (int i = 0; i < Mathf.Max(1, submeshCount); i++) submeshes.Add(new List<int>());
        }

        public int VertexCount => vertices.Count;

        public int Vertex(Vector3 position, Vector3 normal, Vector2 uv)
        {
            vertices.Add(position);
            normals.Add(normal);
            uvs.Add(uv);
            return vertices.Count - 1;
        }

        public void Triangle(int submesh, int a, int b, int c)
        {
            List<int> list = submeshes[submesh];
            list.Add(a); list.Add(b); list.Add(c);
        }

        /// <summary>Quad a-b-c-d (clockwise seen from the front, Unity winding) with a flat normal.</summary>
        public void Quad(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int i0 = Vertex(a, normal, ua), i1 = Vertex(b, normal, ub), i2 = Vertex(c, normal, uc), i3 = Vertex(d, normal, ud);
            Triangle(submesh, i0, i1, i2);
            Triangle(submesh, i0, i2, i3);
        }

        public static Vector2 BoxUV(Vector3 p, Vector3 faceNormal)
        {
            Vector3 n = new Vector3(Mathf.Abs(faceNormal.x), Mathf.Abs(faceNormal.y), Mathf.Abs(faceNormal.z));
            if (n.y >= n.x && n.y >= n.z) return new Vector2(p.x, p.z);
            if (n.x >= n.z) return new Vector2(faceNormal.x >= 0f ? -p.z : p.z, p.y);
            return new Vector2(faceNormal.z >= 0f ? p.x : -p.x, p.y);
        }

        /// <summary>
        /// Box with rounded edges (radius 0 gives a plain box). Six faces, each a grid whose edge rows follow the
        /// fillet; normals are analytic, so the box shades as one soft object. <paramref name="faceSubmesh"/> maps the
        /// faces (+X, -X, +Y, -Y, +Z, -Z) to submeshes; null puts everything in submesh 0. A face whose submesh is
        /// negative is skipped. <paramref name="bulge"/> pushes the middle of the faces outwards (cushions).
        /// </summary>
        public void RoundedBox(Vector3 center, Vector3 size, float radius, int segments, int[] faceSubmesh = null, float bulge = 0f, Quaternion? rotation = null)
        {
            Vector3 h = size * 0.5f;
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.999f);
            if (radius <= 0.0001f) segments = 0;
            Quaternion rot = rotation ?? Quaternion.identity;
            float[] ax = AxisSamples(h.x, radius, segments);
            float[] ay = AxisSamples(h.y, radius, segments);
            float[] az = AxisSamples(h.z, radius, segments);
            Vector3 inner = new Vector3(h.x - radius, h.y - radius, h.z - radius);
            // face: fixed axis, sign, the two varying axes
            for (int face = 0; face < 6; face++)
            {
                int sub = faceSubmesh != null ? faceSubmesh[face] : 0;
                if (sub < 0) continue;
                int axis = face / 2;
                float sign = (face % 2 == 0) ? 1f : -1f;
                int ua = axis == 0 ? 2 : 0;          // first varying axis
                int va = axis == 1 ? 2 : 1;          // second varying axis
                float[] su = ua == 0 ? ax : (ua == 1 ? ay : az);
                float[] sv = va == 0 ? ax : (va == 1 ? ay : az);
                Vector3 faceN = Vector3.zero; faceN[axis] = sign;
                int nu = su.Length, nv = sv.Length;
                int start = vertices.Count;
                for (int j = 0; j < nv; j++)
                {
                    for (int i = 0; i < nu; i++)
                    {
                        Vector3 p = Vector3.zero;
                        p[axis] = sign * h[axis];
                        p[ua] = su[i];
                        p[va] = sv[j];
                        Vector3 q = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                        Vector3 d = p - q;
                        Vector3 n = d.sqrMagnitude > 1e-12f ? d.normalized : faceN;
                        Vector3 pos = radius > 0f ? q + n * radius : p;
                        if (bulge != 0f)
                        {
                            float fu = h[ua] > 0f ? Mathf.Clamp01(1f - Mathf.Pow(Mathf.Abs(p[ua]) / h[ua], 2f)) : 0f;
                            float fv = h[va] > 0f ? Mathf.Clamp01(1f - Mathf.Pow(Mathf.Abs(p[va]) / h[va], 2f)) : 0f;
                            pos += faceN * bulge * fu * fv;
                        }
                        Vector3 world = center + rot * pos;
                        Vector3 wn = rot * n;
                        vertices.Add(world);
                        normals.Add(wn);
                        uvs.Add(BoxUV(world, rot * faceN));
                    }
                }
                // winding: make triangles face outwards
                for (int j = 0; j < nv - 1; j++)
                {
                    for (int i = 0; i < nu - 1; i++)
                    {
                        int a = start + j * nu + i, b = a + 1, c = a + nu + 1, e = a + nu;
                        Vector3 pa = vertices[a], pb = vertices[b], pc = vertices[c], pe = vertices[e];
                        // Unity front faces are clockwise from the front: Cross(b - a, c - a) points along the normal.
                        Vector3 tn = Vector3.Cross(pb - pa, pc - pa) + Vector3.Cross(pc - pa, pe - pa);
                        if (Vector3.Dot(tn, rot * faceN) > 0f)
                        {
                            Triangle(sub, a, b, c); Triangle(sub, a, c, e);
                        }
                        else
                        {
                            Triangle(sub, a, c, b); Triangle(sub, a, e, c);
                        }
                    }
                }
            }
        }

        private static float[] AxisSamples(float half, float radius, int segments)
        {
            var list = new List<float>();
            if (segments <= 0 || radius <= 0f)
            {
                list.Add(-half); list.Add(half);
                return list.ToArray();
            }
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                list.Add(-(half - radius) - radius * Mathf.Cos(t * Mathf.PI * 0.5f));
            }
            for (int i = segments; i >= 0; i--)
            {
                float t = (float)i / segments;
                float v = (half - radius) + radius * Mathf.Cos(t * Mathf.PI * 0.5f);
                if (Mathf.Abs(v - list[list.Count - 1]) > 1e-6f) list.Add(v);
            }
            return list.ToArray();
        }

        /// <summary>
        /// Surface of revolution around the local Y axis through <paramref name="center"/>. The profile is a list of
        /// (radius, height) points; each consecutive pair becomes a hard-edged band (flat profile normal). UVs: u is
        /// the arc length at the band's mean radius, v the height, so tiled materials keep their scale.
        /// </summary>
        public void Lathe(Vector3 center, IList<Vector2> profile, int segments, int submesh, Quaternion? rotation = null, bool[] smoothBands = null, float arcStart = 0f, float arcEnd = 360f)
        {
            Quaternion rot = rotation ?? Quaternion.identity;
            float a0 = arcStart * Mathf.Deg2Rad, a1 = arcEnd * Mathf.Deg2Rad;
            for (int k = 0; k < profile.Count - 1; k++)
            {
                Vector2 p0 = profile[k], p1 = profile[k + 1];
                Vector2 dir = p1 - p0;
                if (dir.sqrMagnitude < 1e-12f) continue;
                // outward normal in the (r, y) plane for a profile running counter-clockwise around the solid
                Vector2 pn = new Vector2(dir.y, -dir.x).normalized;
                float meanR = Mathf.Max(0.0005f, (p0.x + p1.x) * 0.5f);
                int start = vertices.Count;
                for (int s = 0; s <= segments; s++)
                {
                    float a = Mathf.Lerp(a0, a1, (float)s / segments);
                    float c = Mathf.Cos(a), sn = Mathf.Sin(a);
                    Vector3 radial = new Vector3(c, 0f, sn);
                    Vector3 n = (radial * pn.x + Vector3.up * pn.y).normalized;
                    float u = a * meanR;
                    vertices.Add(center + rot * new Vector3(c * p0.x, p0.y, sn * p0.x));
                    normals.Add(rot * n);
                    uvs.Add(Mathf.Abs(pn.y) > 0.7f ? new Vector2(c * p0.x, sn * p0.x) : new Vector2(u, p0.y));
                    vertices.Add(center + rot * new Vector3(c * p1.x, p1.y, sn * p1.x));
                    normals.Add(rot * n);
                    uvs.Add(Mathf.Abs(pn.y) > 0.7f ? new Vector2(c * p1.x, sn * p1.x) : new Vector2(u, p1.y));
                }
                // With the profile normal (dy, -dr) and increasing angles, Cross(v1 - v0, v2 - v0) = r * normal, so
                // (i0, i1, i2) is always clockwise from the outside (front-facing in Unity).
                bool flip = a1 < a0;
                for (int s = 0; s < segments; s++)
                {
                    int i0 = start + s * 2, i1 = i0 + 1, i2 = i0 + 2, i3 = i0 + 3;
                    if (!flip) { Triangle(submesh, i0, i1, i2); Triangle(submesh, i2, i1, i3); }
                    else { Triangle(submesh, i0, i2, i1); Triangle(submesh, i2, i3, i1); }
                }
            }
        }

        /// <summary>Thin cylinder between two points (cables, rods, legs).</summary>
        public void Rod(Vector3 from, Vector3 to, float radius, int segments, int submesh, bool caps = true)
        {
            Vector3 axis = to - from;
            float len = axis.magnitude;
            if (len < 1e-5f) return;
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, axis / len);
            var profile = caps
                ? new List<Vector2> { new Vector2(0f, 0f), new Vector2(radius, 0f), new Vector2(radius, len), new Vector2(0f, len) }
                : new List<Vector2> { new Vector2(radius, 0f), new Vector2(radius, len) };
            Lathe(from, profile, segments, submesh, rot);
        }

        public Mesh ToMesh(string name, bool lightmapUVs)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = submeshes.Count;
            for (int i = 0; i < submeshes.Count; i++) mesh.SetTriangles(submeshes[i], i, true);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            if (lightmapUVs)
            {
                var settings = new UnwrapParam();
                UnwrapParam.SetDefaults(out settings);
                settings.packMargin = 0.01f;
                Unwrapping.GenerateSecondaryUVSet(mesh, settings);
            }
            return mesh;
        }
    }

    internal static class InteriorMeshes
    {
        /// <summary>Saves the mesh at Assets/Materials/Realism/Interior/Meshes/&lt;name&gt;.asset, updating an existing asset in place so its GUID stays stable.</summary>
        public static Mesh Save(Mesh built, string name)
        {
            string path = InteriorRealismUpgrade.MeshFolder + "/" + name + ".asset";
            InteriorRealismUpgrade.EnsureFolder(InteriorRealismUpgrade.MeshFolder);
            built.name = name;
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(built, path);
                return built;
            }
            EditorUtility.CopySerialized(built, existing);
            existing.name = name;
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(built);
            return existing;
        }

        public static Mesh Build(string name, int submeshes, bool lightmapUVs, System.Action<InteriorMeshBuilder> fill)
        {
            var b = new InteriorMeshBuilder(submeshes);
            fill(b);
            return Save(b.ToMesh(name, lightmapUVs), name);
        }

        /// <summary>Rounded box centred on the object's pivot.</summary>
        public static Mesh Box(string name, Vector3 size, float radius, int segments, int[] faceSubmesh = null, int submeshes = 1, bool lightmapUVs = true, float bulge = 0f)
        {
            return Build(name, submeshes, lightmapUVs, b => b.RoundedBox(Vector3.zero, size, radius, segments, faceSubmesh, bulge));
        }

        /// <summary>Floor slab for 'Showroom Floor': the plane's own local space (10 x 10 units, scale 5), split into
        /// the polished interior (submesh 0) and the honed forecourt ring (submesh 1). UVs are world metres.</summary>
        public static Mesh ShowroomFloor(string name, Vector2 interiorMinWorld, Vector2 interiorMaxWorld, float scale)
        {
            return Build(name, 2, false, b =>
            {
                float half = 5f;
                Vector2 iMin = interiorMinWorld / scale, iMax = interiorMaxWorld / scale;
                void Rect(int sub, float x0, float z0, float x1, float z1)
                {
                    if (x1 - x0 < 1e-5f || z1 - z0 < 1e-5f) return;
                    // subdivide so vertex lighting/fog interpolation stays smooth over 50 m
                    int nx = Mathf.Max(1, Mathf.CeilToInt((x1 - x0) * scale / 2.5f));
                    int nz = Mathf.Max(1, Mathf.CeilToInt((z1 - z0) * scale / 2.5f));
                    for (int i = 0; i < nx; i++)
                    for (int j = 0; j < nz; j++)
                    {
                        float ax = Mathf.Lerp(x0, x1, (float)i / nx), bx = Mathf.Lerp(x0, x1, (float)(i + 1) / nx);
                        float az = Mathf.Lerp(z0, z1, (float)j / nz), bz = Mathf.Lerp(z0, z1, (float)(j + 1) / nz);
                        Vector3 p00 = new Vector3(ax, 0f, az), p10 = new Vector3(bx, 0f, az), p11 = new Vector3(bx, 0f, bz), p01 = new Vector3(ax, 0f, bz);
                        b.Quad(sub, p00, p01, p11, p10, Vector3.up,
                            new Vector2(ax, az) * scale, new Vector2(ax, bz) * scale, new Vector2(bx, bz) * scale, new Vector2(bx, az) * scale);
                    }
                }
                Rect(0, iMin.x, iMin.y, iMax.x, iMax.y);
                Rect(1, -half, -half, half, iMin.y);          // back strip
                Rect(1, -half, iMax.y, half, half);           // front strip
                Rect(1, -half, iMin.y, iMin.x, iMax.y);       // left strip
                Rect(1, iMax.x, iMin.y, half, iMax.y);        // right strip
            });
        }

        public static void ManualLightmapUVs(Mesh mesh, Rect bounds)
        {
            var v = mesh.vertices;
            var uv2 = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++)
                uv2[i] = new Vector2((v[i].x - bounds.xMin) / bounds.width, (v[i].z - bounds.yMin) / bounds.height) * 0.98f + Vector2.one * 0.01f;
            mesh.uv2 = uv2;
        }
    }
}
#endif
