using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Finds the four road wheels of an imported car model and gives each a steer pivot and a spin pivot at its hub, the
/// same way <see cref="ExteriorTrafficCarAgent"/> rigs the traffic cars: in each quarter of the car the tyre is the
/// tallest wheel part standing on the ground; its box centre is the hub and half its height the rolling radius. A part
/// that spans several wheels (the M4 has one mesh for all four) is split into one mesh per wheel.
/// </summary>
public static class TestDriveWheelRig
{
    public sealed class Wheel
    {
        public Transform steer, spin;
        public Vector3 hub;        // car space, at rest
        public Vector3 axle;       // spin axis at rest in the steer pivot's space
        public float radius, side, restSteer;
        public bool front, left;
    }

    private const float GroundContact = 0.06f;

    private sealed class Piece
    {
        public MeshRenderer renderer;
        public Mesh mesh;
        public int quarter;
        public Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        public double sumX, sumZ, sumXX, sumZZ, sumXZ;
        public int count;

        public Vector3 Centre => (min + max) * 0.5f;
        public float Height => max.y - min.y;

        public void Add(Vector3 p)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
            sumX += p.x; sumZ += p.z; sumXX += p.x * p.x; sumZZ += p.z * p.z; sumXZ += p.x * p.z;
            count++;
        }
    }

    /// <summary>Rigs the wheels under <paramref name="car"/> (nose along +Z, ground at y = 0). Returns the wheels found,
    /// front left, front right, rear left, rear right where present.</summary>
    public static List<Wheel> Build(Transform car)
    {
        var result = new List<Wheel>(4);
        Matrix4x4 toCar = car.worldToLocalMatrix;
        MeshRenderer[] renderers = car.GetComponentsInChildren<MeshRenderer>(true);
        var parts = new List<MeshRenderer>();
        var meshes = new List<Mesh>();
        Vector3 groundMin = Vector3.positiveInfinity, groundMax = Vector3.negativeInfinity;
        for (int i = 0; i < renderers.Length; i++)
        {
            MeshRenderer renderer = renderers[i];
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (!renderer.enabled || filter == null || filter.sharedMesh == null || !IsWheelPart(renderer.transform, car)) continue;
            parts.Add(renderer);
            meshes.Add(filter.sharedMesh);
            Bounds box = CarBox(toCar * renderer.transform.localToWorldMatrix, filter.sharedMesh.bounds);
            if (box.min.y > GroundContact) continue;
            groundMin = Vector3.Min(groundMin, box.min);
            groundMax = Vector3.Max(groundMax, box.max);
        }
        if (parts.Count == 0 || groundMin.x > groundMax.x) return result;
        float middleX = (groundMin.x + groundMax.x) * 0.5f, middleZ = (groundMin.z + groundMax.z) * 0.5f;
        var pieces = new List<Piece>();
        for (int i = 0; i < parts.Count; i++)
            CollectPieces(parts[i], meshes[i], toCar * parts[i].transform.localToWorldMatrix, middleX, middleZ, pieces);

        var tyres = new Piece[4];
        for (int i = 0; i < pieces.Count; i++)
        {
            Piece piece = pieces[i];
            float height = piece.Height;
            if (piece.min.y > GroundContact || height < 0.3f || height > 1.6f) continue;
            if (tyres[piece.quarter] == null || height > tyres[piece.quarter].Height) tyres[piece.quarter] = piece;
        }
        var built = new Wheel[4];
        for (int q = 0; q < 4; q++)
            if (tyres[q] != null) built[q] = CreateWheel(car, tyres[q], middleX, q);
        for (int start = 0; start < pieces.Count;)
        {
            int end = start + 1;
            while (end < pieces.Count && pieces[end].renderer == pieces[start].renderer) end++;
            AttachPieces(car, pieces, start, end, built, tyres, middleX, middleZ);
            start = end;
        }
        // Front left (2), front right (3), rear left (0), rear right (1).
        int[] order = { 2, 3, 0, 1 };
        for (int i = 0; i < order.Length; i++)
            if (built[order[i]] != null) result.Add(built[order[i]]);
        return result;
    }

    private static Wheel CreateWheel(Transform car, Piece tyre, float middleX, int quarter)
    {
        float restSteer = 0f;
        if (tyre.count > 2)
        {
            double n = tyre.count, meanX = tyre.sumX / n, meanZ = tyre.sumZ / n;
            double xx = tyre.sumXX / n - meanX * meanX, zz = tyre.sumZZ / n - meanZ * meanZ, xz = tyre.sumXZ / n - meanX * meanZ;
            double major = 0.5 * System.Math.Atan2(2.0 * xz, xx - zz);
            float x = (float)System.Math.Cos(major), z = (float)System.Math.Sin(major);
            if (z < 0f) { x = -x; z = -z; }
            restSteer = Mathf.Atan2(x, z) * Mathf.Rad2Deg;
            if (Mathf.Abs(restSteer) < 0.5f) restSteer = 0f;
        }
        Vector3 hub = tyre.Centre;
        var wheel = new Wheel
        {
            hub = hub,
            radius = Mathf.Max(0.05f, tyre.Height * 0.5f),
            side = hub.x - middleX,
            front = (quarter & 2) != 0,
            left = (quarter & 1) == 0,
            restSteer = restSteer,
            axle = Quaternion.AngleAxis(restSteer, Vector3.up) * Vector3.right
        };
        string label = (wheel.front ? "Front " : "Rear ") + (wheel.left ? "Left" : "Right");
        wheel.steer = new GameObject(label + " Wheel Steer").transform;
        wheel.steer.SetParent(car, false);
        wheel.steer.localPosition = hub;
        wheel.spin = new GameObject(label + " Wheel Spin").transform;
        wheel.spin.SetParent(wheel.steer, false);
        return wheel;
    }

    private static void AttachPieces(Transform car, List<Piece> pieces, int start, int end, Wheel[] built, Piece[] tyres,
        float middleX, float middleZ)
    {
        var targets = new Transform[4];
        bool any = false;
        for (int i = start; i < end; i++)
        {
            Piece piece = pieces[i];
            Wheel wheel = built[piece.quarter];
            Piece tyre = tyres[piece.quarter];
            if (wheel == null || !Inside(piece, tyre, 0.3f * wheel.radius)) continue;
            Vector3 offset = piece.Centre - tyre.Centre;
            bool rolls = (offset - wheel.axle * Vector3.Dot(offset, wheel.axle)).magnitude <= 0.12f * wheel.radius
                && !NamedLike(piece.renderer.transform, car, "caliper");
            targets[piece.quarter] = rolls ? wheel.spin : wheel.steer;
            any = true;
        }
        if (!any) return;
        Piece first = pieces[start];
        Transform node = first.renderer.transform;
        if (end - start == 1 && node.childCount == 0)
        {
            node.SetParent(targets[first.quarter], true);
            return;
        }
        if (!first.mesh.isReadable) return;
        var origins = new Vector3[4];
        for (int q = 0; q < 4; q++) origins[q] = targets[q] != null ? built[q].steer.localPosition : Vector3.zero;
        Mesh[] split = SplitMesh(first.mesh, car.worldToLocalMatrix * node.localToWorldMatrix, middleX, middleZ, origins);
        for (int q = 0; q < 4; q++)
            if (split[q] != null) CreatePart(first.renderer, split[q], targets[q] != null ? targets[q] : car, q);
        first.renderer.enabled = false;
    }

    private static void CollectPieces(MeshRenderer renderer, Mesh mesh, Matrix4x4 toCar, float middleX, float middleZ, List<Piece> pieces)
    {
        if (!mesh.isReadable)
        {
            Bounds box = CarBox(toCar, mesh.bounds);
            var whole = new Piece { renderer = renderer, mesh = mesh, quarter = Quarter(box.center, middleX, middleZ) };
            whole.Add(box.min);
            whole.Add(box.max);
            pieces.Add(whole);
            return;
        }
        Vector3[] vertices = mesh.vertices;
        for (int v = 0; v < vertices.Length; v++) vertices[v] = toCar.MultiplyPoint3x4(vertices[v]);
        var seen = new byte[vertices.Length];
        var quarters = new Piece[4];
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
            int[] triangles = mesh.GetTriangles(s);
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int q = Quarter((vertices[triangles[t]] + vertices[triangles[t + 1]] + vertices[triangles[t + 2]]) / 3f, middleX, middleZ);
                Piece piece = quarters[q];
                if (piece == null) quarters[q] = piece = new Piece { renderer = renderer, mesh = mesh, quarter = q };
                byte bit = (byte)(1 << q);
                for (int k = 0; k < 3; k++)
                {
                    int index = triangles[t + k];
                    if ((seen[index] & bit) != 0) continue;
                    seen[index] |= bit;
                    piece.Add(vertices[index]);
                }
            }
        }
        for (int q = 0; q < 4; q++)
            if (quarters[q] != null) pieces.Add(quarters[q]);
    }

    private static Mesh[] SplitMesh(Mesh source, Matrix4x4 toCar, float middleX, float middleZ, Vector3[] origins)
    {
        var result = new Mesh[4];
        for (int q = 0; q < 4; q++)
        {
            int quarter = q;
            TestDriveMeshCut.TrianglePredicate inQuarter = (a, b, c, n) => Quarter((a + b + c) / 3f, middleX, middleZ) == quarter;
            result[q] = TestDriveMeshCut.Extract(source, toCar, origins[q], inQuarter, source.name + " Wheel " + q);
        }
        return result;
    }

    public static void CreatePart(MeshRenderer source, Mesh mesh, Transform parent, int index)
    {
        var part = new GameObject(source.name + " (part " + index + ")") { layer = source.gameObject.layer };
        part.transform.SetParent(parent, false);
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = source.sharedMaterials;
        renderer.shadowCastingMode = source.shadowCastingMode;
        renderer.receiveShadows = source.receiveShadows;
        renderer.lightProbeUsage = source.lightProbeUsage;
        renderer.reflectionProbeUsage = source.reflectionProbeUsage;
        renderer.probeAnchor = source.probeAnchor;
        renderer.motionVectorGenerationMode = source.motionVectorGenerationMode;
        renderer.renderingLayerMask = source.renderingLayerMask;
        renderer.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
    }

    private static int Quarter(Vector3 p, float middleX, float middleZ) => (p.x >= middleX ? 1 : 0) | (p.z >= middleZ ? 2 : 0);

    private static bool Inside(Piece piece, Piece tyre, float margin) =>
        piece.min.x >= tyre.min.x - margin && piece.max.x <= tyre.max.x + margin
        && piece.min.y >= tyre.min.y - margin && piece.max.y <= tyre.max.y + margin
        && piece.min.z >= tyre.min.z - margin && piece.max.z <= tyre.max.z + margin;

    public static Bounds CarBox(Matrix4x4 toCar, Bounds local)
    {
        Vector3 e = local.extents;
        Vector3 x = toCar.MultiplyVector(new Vector3(e.x, 0f, 0f)), y = toCar.MultiplyVector(new Vector3(0f, e.y, 0f)), z = toCar.MultiplyVector(new Vector3(0f, 0f, e.z));
        Vector3 extents = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x), Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
        return new Bounds(toCar.MultiplyPoint3x4(local.center), extents * 2f);
    }

    /// <summary>Road wheel parts by name, on the node or an ancestor below the car. A name that also says "steer"
    /// (a steering wheel) does not count by itself.</summary>
    public static bool IsWheelPart(Transform node, Transform car)
    {
        for (Transform t = node; t != null && t != car; t = t.parent)
        {
            string name = t.name;
            if (Has(name, "steer")) continue;
            if (Has(name, "wheel") || Has(name, "tire") || Has(name, "tyre") || Has(name, "caliper")
                || HasWord(name, "rim") || HasWord(name, "rims") || HasWord(name, "hub") || HasWord(name, "brake") || HasWord(name, "disc"))
                return true;
        }
        return false;
    }

    private static bool NamedLike(Transform node, Transform car, string word)
    {
        for (Transform t = node; t != null && t != car; t = t.parent)
            if (Has(t.name, word)) return true;
        return false;
    }

    public static bool Has(string name, string word) => name.IndexOf(word, System.StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool HasWord(string name, string word)
    {
        for (int at = name.IndexOf(word, System.StringComparison.OrdinalIgnoreCase); at >= 0;
             at = name.IndexOf(word, at + 1, System.StringComparison.OrdinalIgnoreCase))
        {
            int after = at + word.Length;
            if ((at == 0 || !char.IsLetter(name[at - 1])) && (after == name.Length || !char.IsLetter(name[after]))) return true;
        }
        return false;
    }
}
