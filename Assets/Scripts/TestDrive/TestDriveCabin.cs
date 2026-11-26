using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reads a car's cabin from its triangles: the steering wheel (a node named "steering", or else the wheel-sized,
/// nearly upright ring of connected triangles ahead of the driver's seat) and the seat cushion under the driver.
/// The test car uses it to put the eye, hands and seat in the right place; traffic cars use it to seat a driver
/// behind their own steering wheel. Results are cached per model, since traffic cars share meshes.
/// </summary>
public static class TestDriveCabin
{
    public sealed class SteeringWheel
    {
        public Vector3 centre;          // car space
        public Vector3 axis;            // column axis, pointing forward (away from the driver)
        public float radius;            // rim radius
        public MeshRenderer renderer;   // the body mesh it is part of (island case)
        public TestDriveMeshCut.IslandMap map;
        public int island = -1;
    }

    public sealed class Layout
    {
        public SteeringWheel wheel;
        public float cushion = float.NaN; // car-space height of the driver's seat cushion
        public Vector3 eye;
    }

    private static readonly Dictionary<int, Layout> cache = new Dictionary<int, Layout>();

    /// <summary>The steering wheel as a separate ring of triangles in one of the body meshes.</summary>
    public static SteeringWheel FindWheelIsland(Transform root, List<MeshRenderer> renderers, float ground, float frontZ,
        float rearZ, float top, bool driverOnLeft)
    {
        Matrix4x4 rootFromWorld = root.worldToLocalMatrix;
        SteeringWheel best = null;
        int bestTriangles = 0;
        for (int r = 0; r < renderers.Count; r++)
        {
            MeshFilter filter = renderers[r].GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
            Matrix4x4 toCar = rootFromWorld * renderers[r].transform.localToWorldMatrix;
            Bounds reach = TestDriveWheelRig.CarBox(toCar, filter.sharedMesh.bounds);
            if (reach.max.y < ground + 0.55f || reach.min.y > top - 0.3f || reach.max.z < rearZ || reach.min.z > frontZ) continue;
            TestDriveMeshCut.IslandMap map = TestDriveMeshCut.Islands(filter.sharedMesh, toCar);
            for (int i = 0; i < map.bounds.Count; i++)
            {
                Bounds b = map.bounds[i];
                Vector3 size = b.size, c = b.center;
                float side = driverOnLeft ? -c.x : Mathf.Abs(c.x);
                if (side < 0.15f || side > 0.8f) continue;
                if (size.x < 0.28f || size.x > 0.48f || size.y < 0.22f || size.y > 0.48f || size.z > 0.3f) continue;
                if (c.y < ground + 0.55f || c.y > top - 0.3f) continue;
                if (c.z > frontZ - 0.1f || c.z < rearZ + (frontZ - rearZ) * 0.3f) continue;
                if (map.triangles[i] < 40 || map.triangles[i] <= bestTriangles) continue;
                bestTriangles = map.triangles[i];
                best = new SteeringWheel { renderer = renderers[r], map = map, island = i, centre = c };
            }
        }
        if (best == null) return null;

        // Column axis: the direction of least spread of the ring's corners; radius: their distance from that axis.
        Mesh mesh = best.renderer.GetComponent<MeshFilter>().sharedMesh;
        Matrix4x4 m = rootFromWorld * best.renderer.transform.localToWorldMatrix;
        Vector3[] vertices = mesh.vertices;
        var points = new List<Vector3>();
        for (int s = 0; s < best.map.island.Length; s++)
        {
            int[] tris = best.map.selection.triangles[s];
            int[] ids = best.map.island[s];
            for (int t = 0, i = 0; t + 2 < tris.Length; t += 3, i++)
            {
                if (ids[i] != best.island) continue;
                for (int k = 0; k < 3 && points.Count < 12000; k++) points.Add(m.MultiplyPoint3x4(vertices[tris[t + k]]));
            }
        }
        Vector3 axis;
        float radius;
        Fit(points, best.centre, out axis, out radius);
        if (axis.z < 0f) axis = -axis;
        if (axis.z < 0.5f) return null;
        best.axis = axis;
        best.radius = radius;
        return best;
    }

    /// <summary>Least-spread axis of a point set (power iteration on trace·I − C) and the RMS distance from it.</summary>
    public static void Fit(List<Vector3> points, Vector3 mean, out Vector3 axis, out float radius)
    {
        float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 d = points[i] - mean;
            xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z;
            yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
        }
        float trace = xx + yy + zz;
        Vector3 v = new Vector3(0.1f, 0.2f, 1f).normalized;
        for (int k = 0; k < 40; k++)
        {
            Vector3 cv = new Vector3(xx * v.x + xy * v.y + xz * v.z, xy * v.x + yy * v.y + yz * v.z, xz * v.x + yz * v.y + zz * v.z);
            Vector3 next = trace * v - cv;
            float length = next.magnitude;
            if (length < 1e-9f) break;
            v = next / length;
        }
        axis = v;
        double sum = 0;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 d = points[i] - mean;
            float along = Vector3.Dot(d, v);
            sum += Mathf.Max(0f, d.sqrMagnitude - along * along);
        }
        radius = points.Count > 0 ? Mathf.Clamp(Mathf.Sqrt((float)(sum / points.Count)), 0.15f, 0.21f) : 0.18f;
    }

    /// <summary>Seat cushion height: the 60th percentile of upward faces just ahead of the hip point.</summary>
    public static float FindCushion(Transform root, List<MeshRenderer> renderers, float driverX, float eyeZ, float ground, float top)
    {
        var cushions = new List<float>();
        Matrix4x4 rootFromWorld = root.worldToLocalMatrix;
        float guess = top - 0.27f;
        for (int r = 0; r < renderers.Count; r++)
        {
            MeshFilter filter = renderers[r].GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
            Matrix4x4 toCar = rootFromWorld * renderers[r].transform.localToWorldMatrix;
            Bounds reach = TestDriveWheelRig.CarBox(toCar, filter.sharedMesh.bounds);
            if (reach.max.z < eyeZ - 0.3f || reach.min.z > eyeZ + 0.1f) continue;
            TestDriveMeshCut.Select(filter.sharedMesh, toCar, (a, b, c, n) =>
            {
                Vector3 mid = (a + b + c) / 3f;
                if (Mathf.Abs(mid.x - driverX) < 0.08f && Mathf.Abs(mid.z - (eyeZ - 0.1f)) < 0.12f
                    && mid.y > ground + 0.18f && mid.y < Mathf.Min(guess - 0.38f, top - 0.8f) && n.y > 0.6f)
                    cushions.Add(mid.y);
                return false;
            });
        }
        if (cushions.Count == 0) return float.NaN;
        cushions.Sort();
        return cushions[Mathf.Min(cushions.Count - 1, (int)(cushions.Count * 0.6f))];
    }

    /// <summary>
    /// The driver's place in a traffic car (car-local space of <paramref name="root"/>): steering wheel, cushion and
    /// eye. Null when the car has no recognisable steering wheel. Cached per model.
    /// </summary>
    public static Layout ForTraffic(Transform root, List<MeshRenderer> renderers, Bounds local)
    {
        if (renderers.Count == 0) return null;
        int key = 17;
        for (int i = 0; i < renderers.Count; i++)
        {
            MeshFilter f = renderers[i].GetComponent<MeshFilter>();
            key = key * 31 + (f != null && f.sharedMesh != null ? f.sharedMesh.GetInstanceID() : 0);
        }
        Layout layout;
        if (cache.TryGetValue(key, out layout)) return layout;

        float ground = local.min.y, top = local.max.y;
        float frontZ = local.max.z - local.size.z * 0.19f, rearZ = local.min.z + local.size.z * 0.2f;
        SteeringWheel wheel = null;
        Matrix4x4 rootFromWorld = root.worldToLocalMatrix;
        // A named steering node first (the Tesla), else a ring of triangles.
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!TestDriveWheelRig.Has(t.name, "steering") || TestDriveWheelRig.Has(t.name, "column")) continue;
            var points = new List<Vector3>();
            foreach (MeshFilter filter in t.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
                Matrix4x4 m = rootFromWorld * filter.transform.localToWorldMatrix;
                Vector3[] v = filter.sharedMesh.vertices;
                int step = Mathf.Max(1, v.Length / 4000);
                for (int i = 0; i < v.Length; i += step) points.Add(m.MultiplyPoint3x4(v[i]));
            }
            if (points.Count < 30) continue;
            Vector3 mean = Vector3.zero;
            for (int i = 0; i < points.Count; i++) mean += points[i];
            mean /= points.Count;
            Vector3 axis;
            float radius;
            Fit(points, mean, out axis, out radius);
            if (axis.z < 0f) axis = -axis;
            if (axis.z < 0.5f || Mathf.Abs(mean.x) < 0.1f || mean.y < ground + 0.45f) continue;
            wheel = new SteeringWheel { centre = mean, axis = axis, radius = radius };
            break;
        }
        if (wheel == null) wheel = FindWheelIsland(root, renderers, ground, frontZ, rearZ, top, false);
        if (wheel == null)
        {
            cache[key] = null;
            return null;
        }
        layout = new Layout { wheel = wheel };
        float eyeZ = wheel.centre.z - 0.5f;
        layout.cushion = FindCushion(root, renderers, wheel.centre.x, eyeZ, ground, top);
        float eyeY = top - 0.27f;
        if (!float.IsNaN(layout.cushion)) eyeY = Mathf.Clamp(layout.cushion + 0.68f, top - 0.39f, top - 0.25f);
        layout.eye = new Vector3(wheel.centre.x, eyeY, eyeZ);
        wheel.map = null;   // the island map is only needed while building the test car
        cache[key] = layout;
        return layout;
    }
}
