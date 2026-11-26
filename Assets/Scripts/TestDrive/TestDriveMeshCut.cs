using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Splits readable meshes by triangle: picks triangles with a test in car space and builds a new mesh from them
/// (vertices moved into a pivot's space), or a copy of the source without them. Used for the wheels that share one
/// mesh and for the driver's door of models whose body is a single mesh.
/// </summary>
public static class TestDriveMeshCut
{
    /// <summary>Triangle test: the three corners and the face normal, all in car space.</summary>
    public delegate bool TrianglePredicate(Vector3 a, Vector3 b, Vector3 c, Vector3 normal);

    public sealed class Selection
    {
        public int[][] triangles;
        public bool[][] picked;
        public int count;
    }

    public static Selection Select(Mesh mesh, Matrix4x4 toCar, TrianglePredicate predicate)
    {
        var selection = new Selection
        {
            triangles = new int[mesh.subMeshCount][],
            picked = new bool[mesh.subMeshCount][]
        };
        if (!mesh.isReadable) return selection;
        Vector3[] vertices = mesh.vertices;
        for (int v = 0; v < vertices.Length; v++) vertices[v] = toCar.MultiplyPoint3x4(vertices[v]);
        bool flip = toCar.determinant < 0f;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            int[] tris = mesh.GetTopology(s) == MeshTopology.Triangles ? mesh.GetTriangles(s) : new int[0];
            selection.triangles[s] = tris;
            var picked = new bool[tris.Length / 3];
            selection.picked[s] = picked;
            for (int t = 0, i = 0; t + 2 < tris.Length; t += 3, i++)
            {
                Vector3 a = vertices[tris[t]], b = vertices[tris[t + 1]], c = vertices[tris[t + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (flip) n = -n;
                float length = n.magnitude;
                n = length > 1e-9f ? n / length : Vector3.zero;
                if (!predicate(a, b, c, n)) continue;
                picked[i] = true;
                selection.count++;
            }
        }
        return selection;
    }

    public static Mesh Extract(Mesh source, Matrix4x4 toCar, Vector3 origin, TrianglePredicate predicate, string name)
    {
        Selection selection = Select(source, toCar, predicate);
        return selection.count == 0 ? null : Extract(source, toCar, selection, origin, name);
    }

    /// <summary>A new mesh of the picked triangles, its vertices in car space less <paramref name="origin"/>.</summary>
    public static Mesh Extract(Mesh source, Matrix4x4 toCar, Selection selection, Vector3 origin, string name)
    {
        if (selection.count == 0) return null;
        Vector3[] vertices = source.vertices;
        Vector3[] normals = source.normals;
        Vector4[] tangents = source.tangents;
        Color32[] colors = source.colors32;
        bool hasNormals = normals.Length == vertices.Length, hasTangents = tangents.Length == vertices.Length, hasColors = colors.Length == vertices.Length;
        bool flip = toCar.determinant < 0f;
        Matrix4x4 normalMatrix = toCar.inverse.transpose;
        var uvs = new List<Vector4>[4];
        var uvDimensions = new int[4];
        for (int c = 0; c < 4; c++)
        {
            var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + c);
            if (!source.HasVertexAttribute(attribute)) continue;
            uvDimensions[c] = source.GetVertexAttributeDimension(attribute);
            uvs[c] = new List<Vector4>(vertices.Length);
            source.GetUVs(c, uvs[c]);
        }
        var remap = new int[vertices.Length];
        for (int v = 0; v < remap.Length; v++) remap[v] = -1;
        var positions = new List<Vector3>();
        var outNormals = new List<Vector3>();
        var outTangents = new List<Vector4>();
        var outColors = new List<Color32>();
        var outUvs4 = new List<Vector4>[4];
        var outUvs2 = new List<Vector2>[4];
        for (int c = 0; c < 4; c++)
        {
            if (uvs[c] == null) continue;
            if (uvDimensions[c] <= 2) outUvs2[c] = new List<Vector2>();
            else outUvs4[c] = new List<Vector4>();
        }
        int subCount = selection.triangles.Length;
        var indices = new List<int>[subCount];
        for (int s = 0; s < subCount; s++)
        {
            var list = new List<int>();
            indices[s] = list;
            int[] tris = selection.triangles[s];
            bool[] picked = selection.picked[s];
            if (tris == null) continue;
            for (int t = 0, i = 0; t + 2 < tris.Length; t += 3, i++)
            {
                if (!picked[i]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int index = tris[t + (flip && k > 0 ? 3 - k : k)];
                    if (remap[index] < 0)
                    {
                        remap[index] = positions.Count;
                        positions.Add(toCar.MultiplyPoint3x4(vertices[index]) - origin);
                        if (hasNormals) outNormals.Add(normalMatrix.MultiplyVector(normals[index]).normalized);
                        if (hasTangents)
                        {
                            Vector4 tangent = tangents[index];
                            Vector3 direction = toCar.MultiplyVector(tangent).normalized;
                            outTangents.Add(new Vector4(direction.x, direction.y, direction.z, flip ? -tangent.w : tangent.w));
                        }
                        if (hasColors) outColors.Add(colors[index]);
                        for (int c = 0; c < 4; c++)
                        {
                            if (uvs[c] == null) continue;
                            if (uvDimensions[c] <= 2) outUvs2[c].Add(uvs[c][index]);
                            else outUvs4[c].Add(uvs[c][index]);
                        }
                    }
                    list.Add(remap[index]);
                }
            }
        }
        var mesh = new Mesh { name = name };
        if (positions.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(positions);
        if (hasNormals) mesh.SetNormals(outNormals);
        if (hasTangents) mesh.SetTangents(outTangents);
        if (hasColors) mesh.SetColors(outColors);
        for (int c = 0; c < 4; c++)
        {
            if (uvs[c] == null) continue;
            if (uvDimensions[c] <= 2) mesh.SetUVs(c, outUvs2[c]);
            else mesh.SetUVs(c, outUvs4[c]);
        }
        mesh.subMeshCount = subCount;
        for (int s = 0; s < subCount; s++) mesh.SetTriangles(indices[s], s, false);
        mesh.RecalculateBounds();
        return mesh;
    }

    // ------------------------------------------------------------------ clipping

    private struct Corner
    {
        public Vector3 position;   // mesh space
        public Vector3 car;        // car space, for the plane tests
        public Vector3 normal;
        public Vector4 tangent;
        public Color color;
        public Vector4 uv0, uv1, uv2, uv3;

        public static Corner Lerp(Corner a, Corner b, float t)
        {
            Corner c;
            c.position = Vector3.LerpUnclamped(a.position, b.position, t);
            c.car = Vector3.LerpUnclamped(a.car, b.car, t);
            c.normal = Vector3.LerpUnclamped(a.normal, b.normal, t).normalized;
            Vector3 tan = Vector3.LerpUnclamped(a.tangent, b.tangent, t).normalized;
            c.tangent = new Vector4(tan.x, tan.y, tan.z, a.tangent.w);
            c.color = Color.LerpUnclamped(a.color, b.color, t);
            c.uv0 = Vector4.LerpUnclamped(a.uv0, b.uv0, t);
            c.uv1 = Vector4.LerpUnclamped(a.uv1, b.uv1, t);
            c.uv2 = Vector4.LerpUnclamped(a.uv2, b.uv2, t);
            c.uv3 = Vector4.LerpUnclamped(a.uv3, b.uv3, t);
            return c;
        }
    }

    private sealed class Builder
    {
        public readonly List<Vector3> positions = new List<Vector3>();
        public readonly List<Vector3> cars = new List<Vector3>();
        public readonly List<Vector3> normals = new List<Vector3>();
        public readonly List<Vector4> tangents = new List<Vector4>();
        public readonly List<Color> colors = new List<Color>();
        public readonly List<Vector4>[] uvs = { new List<Vector4>(), new List<Vector4>(), new List<Vector4>(), new List<Vector4>() };
        public List<int>[] indices;

        public int Add(Corner c)
        {
            positions.Add(c.position);
            cars.Add(c.car);
            normals.Add(c.normal);
            tangents.Add(c.tangent);
            colors.Add(c.color);
            uvs[0].Add(c.uv0); uvs[1].Add(c.uv1); uvs[2].Add(c.uv2); uvs[3].Add(c.uv3);
            return positions.Count - 1;
        }

        public Corner Get(int index)
        {
            Corner c;
            c.position = positions[index];
            c.car = cars[index];
            c.normal = normals[index];
            c.tangent = tangents[index];
            c.color = colors[index];
            c.uv0 = uvs[0][index]; c.uv1 = uvs[1][index]; c.uv2 = uvs[2][index]; c.uv3 = uvs[3][index];
            return c;
        }

        public Mesh ToMesh(string name, bool hasNormals, bool hasTangents, bool hasColors, int[] uvDimensions)
        {
            var mesh = new Mesh { name = name };
            if (positions.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions);
            if (hasNormals) mesh.SetNormals(normals);
            if (hasTangents) mesh.SetTangents(tangents);
            if (hasColors) mesh.SetColors(colors);
            for (int c = 0; c < 4; c++)
            {
                if (uvDimensions[c] == 0) continue;
                if (uvDimensions[c] <= 2)
                {
                    var two = new List<Vector2>(uvs[c].Count);
                    for (int i = 0; i < uvs[c].Count; i++) two.Add(uvs[c][i]);
                    mesh.SetUVs(c, two);
                }
                else mesh.SetUVs(c, uvs[c]);
            }
            mesh.subMeshCount = indices.Length;
            for (int s = 0; s < indices.Length; s++) mesh.SetTriangles(indices[s], s, false);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>
    /// Cuts the region bounded by <paramref name="planes"/> (car space, inside where n·p + w ≥ 0) out of the triangles
    /// that pass <paramref name="band"/>. Triangles crossing a plane are clipped, so the cut runs along straight lines
    /// instead of the mesh's own edges. Both meshes stay in the source's space with all its vertex data.
    /// </summary>
    public static bool CutRegion(Mesh source, Matrix4x4 toCar, TrianglePredicate band, Vector4[] planes,
        out Mesh inside, out Mesh remainder, out int insideTriangles)
    {
        inside = remainder = null;
        insideTriangles = 0;
        if (!source.isReadable) return false;
        Vector3[] vertices = source.vertices;
        Vector3[] normals = source.normals;
        Vector4[] tangents = source.tangents;
        Color[] colors = source.colors;
        bool hasNormals = normals.Length == vertices.Length, hasTangents = tangents.Length == vertices.Length, hasColors = colors.Length == vertices.Length;
        var uvDimensions = new int[4];
        var uvs = new List<Vector4>[4];
        for (int c = 0; c < 4; c++)
        {
            var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + c);
            if (!source.HasVertexAttribute(attribute)) continue;
            uvDimensions[c] = source.GetVertexAttributeDimension(attribute);
            uvs[c] = new List<Vector4>(vertices.Length);
            source.GetUVs(c, uvs[c]);
        }
        var car = new Vector3[vertices.Length];
        for (int v = 0; v < vertices.Length; v++) car[v] = toCar.MultiplyPoint3x4(vertices[v]);
        bool flip = toCar.determinant < 0f;

        var body = new Builder();
        var door = new Builder();
        var doorRemap = new int[vertices.Length];
        for (int v = 0; v < vertices.Length; v++)
        {
            doorRemap[v] = -1;
            body.Add(Original(v, vertices, car, normals, tangents, colors, uvs, hasNormals, hasTangents, hasColors));
        }
        int subCount = source.subMeshCount;
        body.indices = new List<int>[subCount];
        door.indices = new List<int>[subCount];
        var poly = new List<Corner>(8);
        var inner = new List<Corner>(8);
        var outer = new List<Corner>(8);
        for (int s = 0; s < subCount; s++)
        {
            var bodyList = new List<int>();
            var doorList = new List<int>();
            body.indices[s] = bodyList;
            door.indices[s] = doorList;
            int[] tris = source.GetTopology(s) == MeshTopology.Triangles ? source.GetTriangles(s) : new int[0];
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                Vector3 a = car[i0], b = car[i1], c = car[i2];
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (flip) n = -n;
                float length = n.magnitude;
                n = length > 1e-9f ? n / length : Vector3.zero;
                if (!band(a, b, c, n))
                {
                    bodyList.Add(i0); bodyList.Add(i1); bodyList.Add(i2);
                    continue;
                }
                int state = Classify(a, b, c, planes);
                if (state < 0)
                {
                    bodyList.Add(i0); bodyList.Add(i1); bodyList.Add(i2);
                    continue;
                }
                if (state > 0)
                {
                    doorList.Add(DoorIndex(door, doorRemap, i0, body));
                    doorList.Add(DoorIndex(door, doorRemap, i1, body));
                    doorList.Add(DoorIndex(door, doorRemap, i2, body));
                    insideTriangles++;
                    continue;
                }
                // Crossing: clip plane by plane; what falls outside each plane stays with the body.
                poly.Clear();
                poly.Add(body.Get(i0)); poly.Add(body.Get(i1)); poly.Add(body.Get(i2));
                for (int p = 0; p < planes.Length && poly.Count >= 3; p++)
                {
                    Split(poly, planes[p], inner, outer);
                    if (outer.Count >= 3) Fan(outer, body, bodyList);
                    poly.Clear();
                    poly.AddRange(inner);
                }
                if (poly.Count >= 3)
                {
                    Fan(poly, door, doorList);
                    insideTriangles++;
                }
            }
        }
        if (insideTriangles == 0) return false;
        inside = door.ToMesh(source.name + " Driver Door", hasNormals, hasTangents, hasColors, uvDimensions);
        remainder = body.ToMesh(source.name + " Without Driver Door", hasNormals, hasTangents, hasColors, uvDimensions);
        return true;
    }

    private static Corner Original(int v, Vector3[] vertices, Vector3[] car, Vector3[] normals, Vector4[] tangents, Color[] colors,
        List<Vector4>[] uvs, bool hasNormals, bool hasTangents, bool hasColors)
    {
        Corner c;
        c.position = vertices[v];
        c.car = car[v];
        c.normal = hasNormals ? normals[v] : Vector3.up;
        c.tangent = hasTangents ? tangents[v] : new Vector4(1f, 0f, 0f, 1f);
        c.color = hasColors ? colors[v] : Color.white;
        c.uv0 = uvs[0] != null ? uvs[0][v] : Vector4.zero;
        c.uv1 = uvs[1] != null ? uvs[1][v] : Vector4.zero;
        c.uv2 = uvs[2] != null ? uvs[2][v] : Vector4.zero;
        c.uv3 = uvs[3] != null ? uvs[3][v] : Vector4.zero;
        return c;
    }

    private static int DoorIndex(Builder door, int[] remap, int original, Builder body)
    {
        if (remap[original] < 0) remap[original] = door.Add(body.Get(original));
        return remap[original];
    }

    // 1: all corners inside every plane; -1: all corners outside one plane; 0: crossing.
    private static int Classify(Vector3 a, Vector3 b, Vector3 c, Vector4[] planes)
    {
        bool allInside = true;
        for (int p = 0; p < planes.Length; p++)
        {
            float da = Distance(planes[p], a), db = Distance(planes[p], b), dc = Distance(planes[p], c);
            if (da < 0f && db < 0f && dc < 0f) return -1;
            if (da < 0f || db < 0f || dc < 0f) allInside = false;
        }
        return allInside ? 1 : 0;
    }

    private static float Distance(Vector4 plane, Vector3 p) => plane.x * p.x + plane.y * p.y + plane.z * p.z + plane.w;

    private static void Split(List<Corner> poly, Vector4 plane, List<Corner> inner, List<Corner> outer)
    {
        inner.Clear();
        outer.Clear();
        for (int i = 0; i < poly.Count; i++)
        {
            Corner a = poly[i], b = poly[(i + 1) % poly.Count];
            float da = Distance(plane, a.car), db = Distance(plane, b.car);
            if (da >= 0f) inner.Add(a);
            else outer.Add(a);
            if ((da >= 0f) != (db >= 0f))
            {
                Corner cut = Corner.Lerp(a, b, da / (da - db));
                inner.Add(cut);
                outer.Add(cut);
            }
        }
    }

    private static void Fan(List<Corner> poly, Builder target, List<int> indices)
    {
        int first = target.Add(poly[0]);
        int previous = target.Add(poly[1]);
        for (int i = 2; i < poly.Count; i++)
        {
            int next = target.Add(poly[i]);
            indices.Add(first);
            indices.Add(previous);
            indices.Add(next);
            previous = next;
        }
    }

    // ------------------------------------------------------------------ ray

    /// <summary>Nearest hit of a car-space ray on a readable mesh (both faces), beyond <paramref name="minDistance"/>
    /// and nearer than <paramref name="best"/>; updates <paramref name="best"/> and the face normal.</summary>
    public static bool Raycast(Mesh mesh, Matrix4x4 toCar, Ray ray, float minDistance, ref float best, ref Vector3 normal)
    {
        if (mesh == null || !mesh.isReadable) return false;
        Vector3[] vertices = mesh.vertices;
        for (int v = 0; v < vertices.Length; v++) vertices[v] = toCar.MultiplyPoint3x4(vertices[v]);
        bool found = false;
        Vector3 o = ray.origin, d = ray.direction;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
            int[] tris = mesh.GetTriangles(s);
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                Vector3 a = vertices[tris[t]], b = vertices[tris[t + 1]], c = vertices[tris[t + 2]];
                Vector3 e1 = b - a, e2 = c - a;
                Vector3 p = Vector3.Cross(d, e2);
                float det = Vector3.Dot(e1, p);
                if (det > -1e-9f && det < 1e-9f) continue;
                float inv = 1f / det;
                Vector3 q = o - a;
                float u = Vector3.Dot(q, p) * inv;
                if (u < 0f || u > 1f) continue;
                Vector3 r = Vector3.Cross(q, e1);
                float w = Vector3.Dot(d, r) * inv;
                if (w < 0f || u + w > 1f) continue;
                float distance = Vector3.Dot(e2, r) * inv;
                if (distance <= minDistance || distance >= best) continue;
                best = distance;
                normal = Vector3.Cross(e1, e2).normalized;
                found = true;
            }
        }
        return found;
    }

    // ------------------------------------------------------------------ islands

    /// <summary>
    /// The connected pieces of a mesh: triangles that share a corner position (welded at 2 mm in car space, so seams
    /// split for normals or UVs still join) belong to the same island. Many models build a door as its own pieces
    /// (outer skin, frame, inner card) inside the one body mesh; picking those islands gives a clean door with the
    /// model's own edges.
    /// </summary>
    public sealed class IslandMap
    {
        public Selection selection;
        public int[][] island;          // per sub-mesh, per triangle
        public readonly List<Bounds> bounds = new List<Bounds>();
        public readonly List<int> triangles = new List<int>();

        /// <summary>Marks every triangle of the given islands as picked; returns how many.</summary>
        public int Pick(HashSet<int> islands)
        {
            selection.count = 0;
            for (int s = 0; s < island.Length; s++)
            {
                bool[] picked = selection.picked[s];
                int[] ids = island[s];
                for (int i = 0; i < ids.Length; i++)
                {
                    picked[i] = islands.Contains(ids[i]);
                    if (picked[i]) selection.count++;
                }
            }
            return selection.count;
        }
    }

    public static IslandMap Islands(Mesh mesh, Matrix4x4 toCar)
    {
        var map = new IslandMap
        {
            selection = new Selection { triangles = new int[mesh.subMeshCount][], picked = new bool[mesh.subMeshCount][] },
            island = new int[mesh.subMeshCount][]
        };
        if (!mesh.isReadable)
        {
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                map.selection.triangles[s] = new int[0];
                map.selection.picked[s] = new bool[0];
                map.island[s] = new int[0];
            }
            return map;
        }
        Vector3[] vertices = mesh.vertices;
        var weld = new int[vertices.Length];
        var ids = new Dictionary<Vector3Int, int>(vertices.Length);
        for (int v = 0; v < vertices.Length; v++)
        {
            Vector3 p = toCar.MultiplyPoint3x4(vertices[v]);
            vertices[v] = p;
            var key = new Vector3Int(Mathf.RoundToInt(p.x * 500f), Mathf.RoundToInt(p.y * 500f), Mathf.RoundToInt(p.z * 500f));
            int id;
            if (!ids.TryGetValue(key, out id)) { id = ids.Count; ids.Add(key, id); }
            weld[v] = id;
        }
        var parent = new int[ids.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        System.Func<int, int> find = x =>
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        };
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            int[] tris = mesh.GetTopology(s) == MeshTopology.Triangles ? mesh.GetTriangles(s) : new int[0];
            map.selection.triangles[s] = tris;
            map.selection.picked[s] = new bool[tris.Length / 3];
            map.island[s] = new int[tris.Length / 3];
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int a = find(weld[tris[t]]);
                int b = find(weld[tris[t + 1]]);
                if (b != a) parent[b] = a;
                int c = find(weld[tris[t + 2]]);
                if (c != a) parent[c] = a;
            }
        }
        var islandOfRoot = new Dictionary<int, int>();
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            int[] tris = map.selection.triangles[s];
            int[] owner = map.island[s];
            for (int t = 0, i = 0; t + 2 < tris.Length; t += 3, i++)
            {
                int r = find(weld[tris[t]]);
                int id;
                Vector3 a = vertices[tris[t]], b = vertices[tris[t + 1]], c = vertices[tris[t + 2]];
                if (!islandOfRoot.TryGetValue(r, out id))
                {
                    id = map.bounds.Count;
                    islandOfRoot.Add(r, id);
                    map.bounds.Add(new Bounds(a, Vector3.zero));
                    map.triangles.Add(0);
                }
                Bounds box = map.bounds[id];
                box.Encapsulate(a);
                box.Encapsulate(b);
                box.Encapsulate(c);
                map.bounds[id] = box;
                map.triangles[id]++;
                owner[i] = id;
            }
        }
        return map;
    }

    /// <summary>A copy of <paramref name="source"/> without the picked triangles (same vertices and space).</summary>
    public static Mesh Remainder(Mesh source, Selection selection, string name)
    {
        Mesh copy = Object.Instantiate(source);
        copy.name = name;
        for (int s = 0; s < selection.triangles.Length; s++)
        {
            int[] tris = selection.triangles[s];
            bool[] picked = selection.picked[s];
            if (tris == null || tris.Length == 0) continue;
            var kept = new List<int>(tris.Length);
            for (int t = 0, i = 0; t + 2 < tris.Length; t += 3, i++)
            {
                if (picked[i]) continue;
                kept.Add(tris[t]);
                kept.Add(tris[t + 1]);
                kept.Add(tris[t + 2]);
            }
            copy.SetTriangles(kept, s, false);
        }
        return copy;
    }
}
