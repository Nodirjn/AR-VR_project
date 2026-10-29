#if UNITY_EDITOR
// Aurelle realism pass, cars: planar-UV copies of a few CarConcept meshes.
//
// The GLB maps its calipers into a few texels of a logo atlas and gives the windscreen no UVs at all, so no texture
// can follow their shape. For these renderers the tool keeps a readable copy of the glTF mesh (same vertices,
// normals, second UVs, submeshes and bounds) whose first UVs are a planar projection, with tangents for that
// projection, saved in Assets/Materials/Realism/Cars/Meshes (written only when the content changes, GUIDs kept) and
// assigned as a prefab-instance override of the MeshFilter:
//   Wheel*BrakePad    a frame centred on the caliper body, along its arc: an upright AURELLE logo, read from outside
//   BodyWindshield    the glass's own plane, unit square over its outline: the black ceramic frit border
// Their materials do without the GLB's occlusion map (on wheel-plane rim copies it darkened the faces in blotches).
// The projections are computed from the glTF meshes in the GLB, so a second run produces the same copies. Copies
// the tool no longer makes (the rims' wheel-plane copies of an earlier version) are put back on their glTF meshes
// and deleted.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class CarsRealism
{
    public const string ConceptMeshFolder = MaterialFolder + "/Meshes";
    private const float CaliperUvSpan = 0.30f;    // metres across the caliper UV square

    private enum UvKind { None, Caliper, Windscreen }

    private static UvKind UvKindOf(string rendererName)
    {
        if (rendererName == null) return UvKind.None;
        if (rendererName.StartsWith("Wheel", StringComparison.Ordinal) && rendererName.EndsWith("BrakePad", StringComparison.Ordinal)) return UvKind.Caliper;
        if (rendererName == "BodyWindshield") return UvKind.Windscreen;
        return UvKind.None;
    }

    /// <summary>Gives the CarConcept's calipers and windscreen their planar-UV mesh copies.</summary>
    private static string AssignConceptUvMeshes(Transform concept, string skipChild)
    {
        int created = 0, updated = 0, unchanged = 0, assigned = 0, restored = 0, deleted = 0;
        var produced = new HashSet<string>();
        Bounds car = new Bounds(concept.position, Vector3.zero);
        bool any = false;
        foreach (Renderer r in concept.GetComponentsInChildren<Renderer>(true))
        {
            if (r.transform.parent != concept || !r.name.StartsWith("Body", StringComparison.Ordinal)) continue;
            if (!any) { car = r.bounds; any = true; } else car.Encapsulate(r.bounds);
        }
        foreach (MeshFilter filter in concept.GetComponentsInChildren<MeshFilter>(true))
        {
            UvKind kind = UvKindOf(filter.name);
            if (skipChild != null && IsUnder(filter.transform, concept, skipChild)) continue;
            if (kind == UvKind.None)
            {
                // A copy this tool assigned before and no longer makes: back to the glTF mesh (override reverted).
                if (filter.sharedMesh != null && AssetDatabase.GetAssetPath(filter.sharedMesh).StartsWith(ConceptMeshFolder + "/", StringComparison.Ordinal) &&
                    PrefabUtility.IsPartOfPrefabInstance(filter))
                {
                    var so = new SerializedObject(filter);
                    SerializedProperty prop = so.FindProperty("m_Mesh");
                    if (prop != null && prop.prefabOverride) PrefabUtility.RevertPropertyOverride(prop, InteractionMode.AutomatedAction);
                    restored++;
                }
                continue;
            }
            MeshFilter original = PrefabUtility.IsPartOfPrefabInstance(filter) ? PrefabUtility.GetCorrespondingObjectFromOriginalSource(filter) : null;
            Mesh source = original != null ? original.sharedMesh : null;
            if (source == null || AssetDatabase.GetAssetPath(source) != ConceptGlb || !source.isReadable) continue;
            Vector2[] uv = PlanarUvs(kind, source, filter.transform, car.center, out Vector3 uAxis, out Vector3 vAxis);
            Vector4[] tangents = PlanarTangents(source, uAxis, vAxis);
            string path = ConceptMeshFolder + "/Concept_" + filter.name + ".asset";
            produced.Add(path);
            Mesh copy = WriteUvCopy(source, uv, tangents, path, ref created, ref updated, ref unchanged);
            if (filter.sharedMesh == copy) continue;
            Undo.RecordObject(filter, "Concept UV meshes");
            filter.sharedMesh = copy;
            PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
            assigned++;
        }
        if (AssetDatabase.IsValidFolder(ConceptMeshFolder))
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { ConceptMeshFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (produced.Contains(path) || !Path.GetFileName(path).StartsWith("Concept_", StringComparison.Ordinal)) continue;
                AssetDatabase.DeleteAsset(path);
                deleted++;
            }
        }
        return created + " created, " + updated + " updated, " + unchanged + " unchanged, " + assigned + " assigned, " + restored + " put back on the glTF mesh, " + deleted + " stale deleted";
    }

    /// <summary>Planar UVs, and the mesh-space directions in which u and v grow.</summary>
    private static Vector2[] PlanarUvs(UvKind kind, Mesh mesh, Transform t, Vector3 carCentre, out Vector3 uAxis, out Vector3 vAxis)
    {
        Vector3[] v = mesh.vertices;
        var uv = new Vector2[v.Length];
        uAxis = Vector3.right; vAxis = Vector3.up;
        switch (kind)
        {
            case UvKind.Caliper:
            {
                Vector3 c = mesh.bounds.center;
                var er = new Vector3(0f, c.y, c.z).normalized;             // radial, away from the hub: the logo's up
                var et = new Vector3(0f, -er.z, er.y);                      // along the caliper's arc: reading direction
                Vector3 outward = Vector3.ProjectOnPlane(t.position - carCentre, Vector3.up).normalized;
                // Unity is left-handed: right x up points away from a viewer outside the car (into the car).
                if (Vector3.Dot(Vector3.Cross(t.TransformDirection(et), t.TransformDirection(er)), outward) > 0f) et = -et;
                for (int i = 0; i < v.Length; i++)
                {
                    var d = new Vector3(0f, v[i].y - c.y, v[i].z - c.z);
                    uv[i] = new Vector2(0.5f + Vector3.Dot(d, et) / CaliperUvSpan, 0.5f - Vector3.Dot(d, er) / CaliperUvSpan);
                }
                uAxis = et; vAxis = -er;
                break;
            }
            case UvKind.Windscreen:
                WindscreenFrame(mesh, out Vector3 a, out Vector3 b, out Vector2 min, out Vector2 size);
                for (int i = 0; i < v.Length; i++)
                    uv[i] = new Vector2((Vector3.Dot(v[i], a) - min.x) / size.x, (Vector3.Dot(v[i], b) - min.y) / size.y);
                uAxis = a; vAxis = b;
                break;
        }
        return uv;
    }

    /// <summary>
    /// Tangents of a planar projection, per vertex: u's direction in the surface plane (or v's on faces edge-on to the
    /// projection, where u does not change), the sign so the bitangent points to decreasing v - the glTFast graphs
    /// sample with V flipped (_ST y = -1), so that is the texture's "up". Never degenerate, unlike RecalculateTangents
    /// on the faces a projection sees edge-on.
    /// </summary>
    private static Vector4[] PlanarTangents(Mesh mesh, Vector3 uAxis, Vector3 vAxis)
    {
        Vector3[] normals = mesh.normals;
        var tangents = new Vector4[normals.Length];
        for (int i = 0; i < normals.Length; i++)
        {
            Vector3 n = normals[i].sqrMagnitude > 1e-12f ? normals[i].normalized : Vector3.up;
            Vector3 t = uAxis - n * Vector3.Dot(n, uAxis);
            if (t.sqrMagnitude < 1e-4f) t = Vector3.Cross(n, vAxis);
            if (t.sqrMagnitude < 1e-8f) t = Vector3.Cross(n, Mathf.Abs(n.x) < 0.9f ? Vector3.right : Vector3.up);
            t.Normalize();
            float w = Vector3.Dot(Vector3.Cross(n, t), -vAxis) >= 0f ? 1f : -1f;
            tangents[i] = new Vector4(t.x, t.y, t.z, w);
        }
        return tangents;
    }

    /// <summary>The windscreen's plane: a along the car's width (local X), b across it; extents of the projection.</summary>
    private static void WindscreenFrame(Mesh mesh, out Vector3 a, out Vector3 b, out Vector2 min, out Vector2 size)
    {
        Vector3[] v = mesh.vertices, n = mesh.normals;
        Vector3 avg = Vector3.zero;
        foreach (Vector3 x in n) avg += Vector3.Dot(x, avg) < 0f ? -x : x; // one side
        avg = avg.sqrMagnitude > 0f ? avg.normalized : Vector3.up;
        a = Vector3.ProjectOnPlane(Vector3.right, avg).normalized;
        b = Vector3.Cross(avg, a).normalized;
        // Make b run from the bottom edge to the top edge of the glass (b toward +Y in the mesh's own space).
        if (Vector3.Dot(b, Vector3.up) < 0f && Vector3.Dot(b, Vector3.forward) < 0f) b = -b;
        min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector3 x in v)
        {
            var p = new Vector2(Vector3.Dot(x, a), Vector3.Dot(x, b));
            min = Vector2.Min(min, p); max = Vector2.Max(max, p);
        }
        size = Vector2.Max(max - min, new Vector2(1e-4f, 1e-4f));
    }

    private static Mesh WriteUvCopy(Mesh source, Vector2[] uv, Vector4[] tangents, string path, ref int created, ref int updated, ref int unchanged)
    {
        EnsureFolderPath(ConceptMeshFolder);
        string name = Path.GetFileNameWithoutExtension(path);
        var fresh = new Mesh();
        FillUvCopy(fresh, source, uv, tangents, name);
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(fresh, path);
            created++;
            return fresh;
        }
        bool same = SameUvCopy(existing, fresh);
        UnityEngine.Object.DestroyImmediate(fresh);
        if (same) { unchanged++; return existing; }
        FillUvCopy(existing, source, uv, tangents, name);
        EditorUtility.SetDirty(existing);
        updated++;
        return existing;
    }

    private static void FillUvCopy(Mesh target, Mesh source, Vector2[] uv, Vector4[] tangents, string name)
    {
        target.Clear();
        target.name = name;
        target.indexFormat = source.indexFormat;
        target.vertices = source.vertices;
        target.normals = source.normals;
        var uv1 = new List<Vector2>();
        source.GetUVs(1, uv1);
        target.uv = uv;
        if (uv1.Count == source.vertexCount) target.SetUVs(1, uv1);
        Color[] colors = source.colors;
        if (colors != null && colors.Length == source.vertexCount) target.colors = colors;
        target.subMeshCount = source.subMeshCount;
        for (int s = 0; s < source.subMeshCount; s++)
            target.SetTriangles(source.GetTriangles(s, true), s, false, 0);
        target.tangents = tangents;
        target.bounds = source.bounds;
    }

    private static bool SameUvCopy(Mesh a, Mesh b)
    {
        if (a.name != b.name || a.vertexCount != b.vertexCount || a.subMeshCount != b.subMeshCount || a.bounds != b.bounds || !a.isReadable) return false;
        if (!Same(a.vertices, b.vertices) || !Same(a.normals, b.normals) || !Same(a.uv, b.uv) || !Same(a.uv2, b.uv2) || !Same(a.tangents, b.tangents)) return false;
        for (int s = 0; s < a.subMeshCount; s++)
        {
            int[] x = a.GetTriangles(s, true), y = b.GetTriangles(s, true);
            if (x.Length != y.Length) return false;
            for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
        }
        return true;
    }

    private static bool Same<T>(T[] x, T[] y) where T : struct
    {
        if (x == null || y == null) return x == y;
        if (x.Length != y.Length) return false;
        var cmp = EqualityComparer<T>.Default;
        for (int i = 0; i < x.Length; i++) if (!cmp.Equals(x[i], y[i])) return false;
        return true;
    }

    // ------------------------------------------------------------------------------------------------ their textures

    // The windscreen's glass colour and opacity outside the frit (the material's base factor is white and opaque).
    private static readonly Color WindscreenTint = new Color(0.08f, 0.10f, 0.10f);
    private const float WindscreenAlpha = 0.26f;

    /// <summary>Caliper paint (linear colour) with the AURELLE logo in white, centred on the caliper frame, upright.</summary>
    private static Gen CaliperBase(int size, Color paint)
    {
        var g = new Gen { W = size, H = size, Px = new Color32[size * size], Kind = Kind.Color, AndroidMax = 256, Aniso = 4 };
        TextStrip logo = TextStrip.Render("AURELLE", 96, FontStyle.Bold, 6);
        float capPx = size * (0.019f / CaliperUvSpan);                   // 19 mm capitals
        float scale = logo.CapHeight / capPx;                             // strip px per texture px
        var white = new Color(0.86f, 0.86f, 0.86f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float sx = logo.W * 0.5f + (x + 0.5f - size * 0.5f) * scale;
                float sy = logo.Baseline - (size * 0.5f + capPx * 0.5f - (y + 0.5f)) * scale;
                float a = logo.A.Length > 1 ? Bilinear(logo.A, logo.W, logo.H, sx, sy) : 0f;
                Color c = Color.Lerp(paint, white, a);
                g.Px[y * size + x] = new Color32(B(Mathf.LinearToGammaSpace(c.r)), B(Mathf.LinearToGammaSpace(c.g)), B(Mathf.LinearToGammaSpace(c.b)), 255);
            }
        return g;
    }

    /// <summary>
    /// Windscreen tint with the black ceramic frit along its outline (6 cm solid, then a 3.5 cm dot gradient), in the
    /// UVs PlanarUvs gives the glTF windscreen. RGB is the glass colour, alpha its opacity (the material's factor is 1).
    /// </summary>
    private static Gen WindscreenFrit(Mesh mesh, int size, Color tint, float tintAlpha)
    {
        var g = new Gen { W = size, H = size, Px = new Color32[size * size], Kind = Kind.Color, AndroidMax = 512, Aniso = 4, Alpha = true };
        WindscreenFrame(mesh, out Vector3 a, out Vector3 b, out Vector2 min, out Vector2 span);
        Vector3[] v = mesh.vertices;
        int[] tri = mesh.triangles;
        var inside = new bool[size * size];
        var p = new Vector2[v.Length];
        for (int i = 0; i < v.Length; i++)
            p[i] = new Vector2((Vector3.Dot(v[i], a) - min.x) / span.x * size, (Vector3.Dot(v[i], b) - min.y) / span.y * size);
        for (int t = 0; t + 2 < tri.Length; t += 3)
        {
            Vector2 p0 = p[tri[t]], p1 = p[tri[t + 1]], p2 = p[tri[t + 2]];
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x)))), x1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y)))), y1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))));
            float area = (p1.x - p0.x) * (p2.y - p0.y) - (p2.x - p0.x) * (p1.y - p0.y);
            if (Mathf.Abs(area) < 1e-6f) continue;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var q = new Vector2(x + 0.5f, y + 0.5f);
                    float w0 = ((p1.x - q.x) * (p2.y - q.y) - (p2.x - q.x) * (p1.y - q.y)) / area;
                    float w1 = ((p2.x - q.x) * (p0.y - q.y) - (p0.x - q.x) * (p2.y - q.y)) / area;
                    float w2 = 1f - w0 - w1;
                    if (w0 >= -0.01f && w1 >= -0.01f && w2 >= -0.01f) inside[y * size + x] = true;
                }
        }
        // Distance to the outline in metres (two-pass chamfer with the texel's metric size per axis).
        float dx = span.x / size, dy = span.y / size, dd = Mathf.Sqrt(dx * dx + dy * dy);
        var dist = new float[size * size];
        for (int i = 0; i < dist.Length; i++) dist[i] = inside[i] ? 1e6f : 0f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int k = y * size + x;
                if (dist[k] == 0f) continue;
                float d = dist[k];
                if (x > 0) d = Mathf.Min(d, dist[k - 1] + dx);
                if (y > 0) d = Mathf.Min(d, dist[k - size] + dy);
                if (x > 0 && y > 0) d = Mathf.Min(d, dist[k - size - 1] + dd);
                if (x < size - 1 && y > 0) d = Mathf.Min(d, dist[k - size + 1] + dd);
                dist[k] = d;
            }
        for (int y = size - 1; y >= 0; y--)
            for (int x = size - 1; x >= 0; x--)
            {
                int k = y * size + x;
                if (dist[k] == 0f) continue;
                float d = dist[k];
                if (x < size - 1) d = Mathf.Min(d, dist[k + 1] + dx);
                if (y < size - 1) d = Mathf.Min(d, dist[k + size] + dy);
                if (x < size - 1 && y < size - 1) d = Mathf.Min(d, dist[k + size + 1] + dd);
                if (x > 0 && y < size - 1) d = Mathf.Min(d, dist[k + size - 1] + dd);
                dist[k] = d;
            }
        const float solid = 0.06f, band = 0.035f, pitch = 0.0065f;
        Color frit = new Color(0.012f, 0.012f, 0.013f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int k = y * size + x;
                float d = dist[k];
                float coverage;
                if (!inside[k])
                {
                    // Outside the glass: frit just around the outline (mip bleed), clear tint further out (so the
                    // texture's corner, which a mesh without these UVs samples, stays clear glass).
                    bool near = false;
                    for (int j = -3; j <= 3 && !near; j++)
                        for (int i = -3; i <= 3 && !near; i++)
                        {
                            int xx = x + i, yy = y + j;
                            if (xx >= 0 && yy >= 0 && xx < size && yy < size && inside[yy * size + xx]) near = true;
                        }
                    coverage = near ? 1f : 0f;
                }
                else if (d < solid) coverage = 1f;
                else if (d < solid + band)
                {
                    // Dots on a metric grid, shrinking across the band.
                    float mx = (x + 0.5f) * dx, my = (y + 0.5f) * dy;
                    float lx = Mathf.Repeat(mx, pitch) - pitch * 0.5f, ly = Mathf.Repeat(my, pitch) - pitch * 0.5f;
                    float radius = pitch * 0.62f * (1f - (d - solid) / band);
                    float r = Mathf.Sqrt(lx * lx + ly * ly);
                    coverage = Mathf.Clamp01((radius - r) / (Mathf.Min(dx, dy) * 0.75f) + 0.5f);
                }
                else coverage = 0f;
                Color c = Color.Lerp(tint, frit, coverage);
                float alpha = Mathf.Lerp(tintAlpha, 1f, coverage);
                g.Px[k] = new Color32(B(Mathf.LinearToGammaSpace(c.r)), B(Mathf.LinearToGammaSpace(c.g)), B(Mathf.LinearToGammaSpace(c.b)), B(alpha));
            }
        return g;
    }

    /// <summary>The glTF windscreen mesh of CarConcept.glb (null when missing or unreadable).</summary>
    private static Mesh ConceptGlbMesh(string name)
    {
        foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(ConceptGlb))
            if (o is Mesh m && m.name == name && m.isReadable) return m;
        return null;
    }
}
#endif
