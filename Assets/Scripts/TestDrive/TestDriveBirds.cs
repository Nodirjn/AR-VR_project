using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Birds: feral pigeons on the dealership forecourt and gulls over the beach and the sea.
///
/// Every bird is a small rig of smooth lofted meshes (body, neck and head, folded wings, open wings in two parts
/// that bend at the wrist, a fanned tail and pink legs with toes) with painted feather textures: the pigeons are
/// blue-bar, chequer, ash-red and dark birds with an iridescent green-and-purple neck, two black wing bars, a dark
/// tail band, an orange eye and a white cere; the gulls are white with a grey mantle, black wing tips with white
/// mirrors and a yellow bill with its red spot.
///
/// The pigeons do what pigeons do: walk with the head-bob (the head holds still while the body walks under it, then
/// thrusts forward), stop to peck, look about in quick jerks, keep a little apart; when someone comes within a few
/// metres they clatter up, nearby birds following a moment later, climb away, circle and glide in to land
/// somewhere else on the forecourt, flaring with their legs out. The gulls wheel over the water, flapping a few
/// beats and gliding on bowed wings, banking into their turns.
/// </summary>
public sealed class TestDriveBirds : MonoBehaviour
{
    private const float GroundY = 0.03f;
    private static readonly Vector2 AreaMin = new Vector2(-8f, 28f), AreaMax = new Vector2(10f, 40f);

    private sealed class Rig
    {
        public Transform root, head, foldL, foldR, armL, armR, handL, handR, legL, legR;
        public float armLength;
    }

    private sealed class Bird
    {
        public Rig rig;
        public bool pigeon, flying;
        // Gulls: a circle over the water.
        public Vector3 centre;
        public float radius, height, speed, phase;
        // Pigeons on the ground.
        public Vector3 ground;
        public float heading, turn, walkSpeed, activityLeft, step, peckClock = -1f, peckWait, lookWait, look, lookTarget, alarm = -1f;
        public int activity;            // 0 walking, 1 pecking about, 2 looking round
        // Pigeons in the air.
        public Vector3 velocity;
        public readonly List<Vector3> route = new List<Vector3>();
        public float bank, flapClock, yaw;
    }

    private readonly List<Bird> birds = new List<Bird>();
    private Transform viewer;
    private System.Random random;
    private bool quest;
    private float nextFlockFlight;

    public static void Install(Transform parent, bool quest)
    {
        var go = new GameObject("Birds");
        go.transform.SetParent(parent, false);
        TestDriveBirds b = go.AddComponent<TestDriveBirds>();
        b.quest = quest;
        b.Build();
    }

    // ------------------------------------------------------------------ meshes

    /// <summary>A smooth tube through cross-sections (elliptical, the long axis of each section across the body):
    /// u goes round (0 underneath, 0.25 the right side, 0.5 the top), v along the sections.</summary>
    private static Mesh Loft(string name, Vector3[] c, float[] rx, float[] ry, int ring)
    {
        int n = c.Length;
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        var t = new List<int>();
        var tangents = new Vector3[n];
        for (int k = 0; k < n; k++)
        {
            Vector3 T = (c[Mathf.Min(k + 1, n - 1)] - c[Mathf.Max(k - 1, 0)]).normalized;
            tangents[k] = T;
            Vector3 U = Vector3.Cross(T, Vector3.right);
            if (U.sqrMagnitude < 1e-6f) U = Vector3.up;
            U.Normalize();
            if (U.y < 0f) U = -U;
            Vector3 side = Vector3.Cross(U, T).normalized;
            if (side.x < 0f) side = -side;
            for (int i = 0; i <= ring; i++)
            {
                float u = i / (float)ring, th = u * Mathf.PI * 2f - Mathf.PI * 0.5f;
                v.Add(c[k] + side * (Mathf.Cos(th) * rx[k]) + U * (Mathf.Sin(th) * ry[k]));
                uv.Add(new Vector2(u, k / (float)(n - 1)));
            }
        }
        for (int k = 0; k < n - 1; k++)
            for (int i = 0; i < ring; i++)
            {
                int a = k * (ring + 1) + i, b = a + 1, cc = a + ring + 1, d = cc + 1;
                t.Add(a); t.Add(b); t.Add(cc);
                t.Add(b); t.Add(d); t.Add(cc);
            }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(v);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals();
        // Faces outward: the right side of the middle section must face +x (or away from the axis).
        int mid = (n / 2) * (ring + 1) + ring / 4;
        Vector3 probe = mesh.normals[mid], outward = v[mid] - c[n / 2];
        if (Vector3.Dot(probe, outward) < 0f)
        {
            t.Reverse();
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
        }
        Vector3[] normals = mesh.normals;
        for (int k = 0; k < n; k++)
        {
            int s = k * (ring + 1);
            // The seam, closed; the pointed ends, facing along the body.
            Vector3 seam = normals[s] + normals[s + ring];
            normals[s] = normals[s + ring] = seam.sqrMagnitude > 1e-8f ? seam.normalized : Vector3.down;
            bool tip = Mathf.Max(rx[k], ry[k]) < 0.0025f;
            for (int i = 0; i <= ring; i++)
            {
                Vector3 nn = normals[s + i];
                if (tip) nn = k == 0 ? -tangents[k] : (k == n - 1 ? tangents[k] : nn);
                if (float.IsNaN(nn.x) || nn.sqrMagnitude < 0.5f)
                {
                    Vector3 o = v[s + i] - c[k];
                    nn = o.sqrMagnitude > 1e-10f ? o.normalized : tangents[k];
                }
                normals[s + i] = nn.normalized;
            }
        }
        mesh.normals = normals;
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>A thin cambered two-sided panel between two edges (a wing between its leading and trailing edge, a
    /// tail between its sides): u along the edges, v across from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private static Mesh Panel(string name, Vector3[] a, Vector3[] b, float camber, int across)
    {
        int n = a.Length;
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        var t = new List<int>();
        for (int side = 0; side < 2; side++)
            for (int i = 0; i < n; i++)
                for (int j = 0; j <= across; j++)
                {
                    float s = j / (float)across;
                    Vector3 p = Vector3.Lerp(a[i], b[i], s) + Vector3.up * (camber * Mathf.Sin(s * Mathf.PI) * (1f - 0.6f * i / (n - 1f)));
                    p.y += side == 0 ? 0.0012f : -0.0012f;
                    v.Add(p);
                    uv.Add(new Vector2(i / (n - 1f), s));
                }
        int half = n * (across + 1);
        for (int i = 0; i < n - 1; i++)
            for (int j = 0; j < across; j++)
            {
                int p = i * (across + 1) + j, q = p + across + 1;
                t.Add(p); t.Add(p + 1); t.Add(q);
                t.Add(q); t.Add(p + 1); t.Add(q + 1);
                t.Add(half + p); t.Add(half + q); t.Add(half + p + 1);
                t.Add(half + q); t.Add(half + q + 1); t.Add(half + p + 1);
            }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(v);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals();
        if (mesh.normals[0].y < 0f)
        {
            t.Reverse();
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
        }
        Vector3[] normals = mesh.normals;
        for (int i = 0; i < normals.Length; i++)
            if (float.IsNaN(normals[i].x) || normals[i].sqrMagnitude < 0.5f) normals[i] = i < half ? Vector3.up : Vector3.down;
        mesh.normals = normals;
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh Mirror(Mesh m)
    {
        var copy = new Mesh { name = m.name + " (left)" };
        Vector3[] v = m.vertices, n = m.normals;
        for (int i = 0; i < v.Length; i++) { v[i].x = -v[i].x; n[i].x = -n[i].x; }
        int[] t = m.triangles;
        for (int i = 0; i < t.Length; i += 3) { int s = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = s; }
        copy.vertices = v;
        copy.normals = n;
        copy.uv = m.uv;
        copy.triangles = t;
        copy.RecalculateBounds();
        return copy;
    }

    private static Mesh Combine(string name, params Mesh[] parts)
    {
        var ci = new CombineInstance[parts.Length];
        for (int i = 0; i < parts.Length; i++) ci[i] = new CombineInstance { mesh = parts[i], transform = Matrix4x4.identity };
        var m = new Mesh { name = name };
        m.CombineMeshes(ci, true, false);
        m.RecalculateBounds();
        return m;
    }

    private sealed class Shape
    {
        public Mesh body, head, foldR, foldL, armR, armL, handR, handL, tail, legR, legL;
        public Vector3 headPivot, shoulder, hip;
        public float armLength;
    }

    private static Vector3 P(float x, float y, float z) { return new Vector3(x, y, z); }

    /// <summary>A feral pigeon, about 32 cm long and 65 cm across the wings, its feet at the origin, nose +Z.</summary>
    private static Shape PigeonShape()
    {
        var s = new Shape { headPivot = P(0f, 0.135f, 0.062f), shoulder = P(0.042f, 0.128f, 0.045f), hip = P(0.017f, 0.064f, 0.0f), armLength = 0.12f };
        s.body = Loft("Pigeon Body",
            new[] { P(0, 0.097f, -0.098f), P(0, 0.098f, -0.085f), P(0, 0.1f, -0.06f), P(0, 0.102f, -0.02f), P(0, 0.106f, 0.02f), P(0, 0.114f, 0.05f), P(0, 0.128f, 0.07f), P(0, 0.145f, 0.078f), P(0, 0.152f, 0.079f) },
            new[] { 0.002f, 0.028f, 0.042f, 0.056f, 0.06f, 0.052f, 0.038f, 0.026f, 0.002f },
            new[] { 0.0015f, 0.02f, 0.034f, 0.048f, 0.056f, 0.052f, 0.04f, 0.028f, 0.002f }, 16);
        Vector3 hp = s.headPivot;
        s.head = Loft("Pigeon Head",
            new[] { P(0, 0.12f, 0.055f) - hp, P(0, 0.13f, 0.06f) - hp, P(0, 0.155f, 0.072f) - hp, P(0, 0.178f, 0.08f) - hp, P(0, 0.195f, 0.086f) - hp, P(0, 0.203f, 0.1f) - hp, P(0, 0.201f, 0.115f) - hp, P(0, 0.198f, 0.122f) - hp, P(0, 0.194f, 0.136f) - hp, P(0, 0.1935f, 0.139f) - hp },
            new[] { 0.002f, 0.031f, 0.026f, 0.022f, 0.02f, 0.02f, 0.014f, 0.006f, 0.0016f, 0.0004f },
            new[] { 0.002f, 0.031f, 0.027f, 0.023f, 0.02f, 0.02f, 0.015f, 0.0065f, 0.0019f, 0.0004f }, 14);
        s.foldR = Loft("Pigeon Folded Wing",
            new[] { P(0.044f, 0.122f, 0.056f), P(0.046f, 0.123f, 0.05f), P(0.052f, 0.122f, 0.012f), P(0.048f, 0.118f, -0.035f), P(0.037f, 0.11f, -0.085f), P(0.022f, 0.104f, -0.13f), P(0.015f, 0.1f, -0.16f) },
            new[] { 0.002f, 0.008f, 0.01f, 0.009f, 0.006f, 0.0035f, 0.001f },
            new[] { 0.002f, 0.016f, 0.028f, 0.026f, 0.017f, 0.009f, 0.0015f }, 10);
        s.foldL = Mirror(s.foldR);
        const int span = 10;
        var lead = new Vector3[span + 1];
        var trail = new Vector3[span + 1];
        for (int i = 0; i <= span; i++)
        {
            float x = s.armLength * i / span;
            lead[i] = P(x, 0f, 0.03f);
            trail[i] = P(x, 0f, 0.03f - (0.1f + 0.005f * i / span));
        }
        s.armR = Panel("Pigeon Arm", lead, trail, 0.008f, 4);
        s.armL = Mirror(s.armR);
        const float hand = 0.2f;
        const int hs = 16;
        lead = new Vector3[hs + 1];
        trail = new Vector3[hs + 1];
        for (int i = 0; i <= hs; i++)
        {
            float f = i / (float)hs, x = hand * f;
            float le = 0.03f - 0.065f * Mathf.Pow(f, 1.6f);
            float chord = 0.105f * (1f - 0.78f * f * f) - 0.007f * Mathf.Abs(Mathf.Sin(f * Mathf.PI * 5f)) * f;
            lead[i] = P(x, 0f, le);
            trail[i] = P(x, 0f, le - Mathf.Max(0.012f, chord));
        }
        s.handR = Panel("Pigeon Hand", lead, trail, 0.005f, 4);
        s.handL = Mirror(s.handR);
        const int ts = 6;
        var tl = new Vector3[ts + 1];
        var tr = new Vector3[ts + 1];
        for (int i = 0; i <= ts; i++)
        {
            float f = i / (float)ts, z = Mathf.Lerp(-0.07f, -0.178f, f), y = Mathf.Lerp(0.099f, 0.084f, f);
            float w = Mathf.Lerp(0.018f, 0.03f, f);
            tl[i] = P(-w, y, z);
            tr[i] = P(w, y, z);
        }
        s.tail = Panel("Pigeon Tail", tl, tr, 0.004f, 6);
        // A leg: the shank down to the ground and four toes.
        float H = s.hip.y;
        Mesh shank = Loft("Shank", new[] { P(0, 0.002f, 0), P(0, -0.004f, 0.001f), P(0, -0.035f, 0.004f), P(0, -H + 0.006f, 0.006f), P(0, -H + 0.002f, 0.007f) },
            new[] { 0.002f, 0.007f, 0.0042f, 0.0034f, 0.002f }, new[] { 0.002f, 0.007f, 0.0042f, 0.0034f, 0.002f }, 8);
        var toes = new List<Mesh> { shank };
        foreach (Vector3 dir in new[] { P(0f, 0f, 1f), P(0.45f, 0f, 0.9f), P(-0.45f, 0f, 0.9f), P(0f, 0f, -0.7f) })
        {
            Vector3 a = P(0, -H + 0.003f, 0.007f), b = a + dir.normalized * (dir.z < 0f ? 0.016f : 0.026f) + Vector3.down * 0.001f;
            toes.Add(Loft("Toe", new[] { a, Vector3.Lerp(a, b, 0.5f), b }, new[] { 0.0022f, 0.0019f, 0.0008f }, new[] { 0.0018f, 0.0015f, 0.0008f }, 6));
        }
        s.legR = Combine("Pigeon Leg", toes.ToArray());
        s.legL = s.legR;
        return s;
    }

    /// <summary>A herring gull, about 60 cm long and 1.4 m across the wings (it is only ever seen flying).</summary>
    private static Shape GullShape()
    {
        var s = new Shape { headPivot = P(0f, 0.12f, 0.14f), shoulder = P(0.05f, 0.12f, 0.06f), hip = P(0.02f, 0.06f, 0f), armLength = 0.3f };
        s.body = Loft("Gull Body",
            new[] { P(0, 0.1f, -0.18f), P(0, 0.1f, -0.16f), P(0, 0.1f, -0.11f), P(0, 0.1f, -0.04f), P(0, 0.102f, 0.05f), P(0, 0.11f, 0.12f), P(0, 0.125f, 0.16f), P(0, 0.13f, 0.172f) },
            new[] { 0.002f, 0.022f, 0.045f, 0.07f, 0.075f, 0.055f, 0.035f, 0.003f },
            new[] { 0.002f, 0.018f, 0.04f, 0.065f, 0.07f, 0.055f, 0.038f, 0.003f }, 16);
        Vector3 hp = s.headPivot;
        s.head = Loft("Gull Head",
            new[] { P(0, 0.115f, 0.13f) - hp, P(0, 0.12f, 0.14f) - hp, P(0, 0.15f, 0.17f) - hp, P(0, 0.165f, 0.2f) - hp, P(0, 0.17f, 0.23f) - hp, P(0, 0.165f, 0.26f) - hp, P(0, 0.162f, 0.27f) - hp, P(0, 0.157f, 0.31f) - hp, P(0, 0.152f, 0.325f) - hp },
            new[] { 0.002f, 0.035f, 0.032f, 0.03f, 0.03f, 0.02f, 0.009f, 0.005f, 0.001f },
            new[] { 0.002f, 0.035f, 0.034f, 0.032f, 0.03f, 0.022f, 0.013f, 0.009f, 0.001f }, 14);
        const int span = 10;
        var lead = new Vector3[span + 1];
        var trail = new Vector3[span + 1];
        for (int i = 0; i <= span; i++)
        {
            float x = s.armLength * i / span;
            lead[i] = P(x, 0f, 0.05f + 0.01f * i / span);
            trail[i] = P(x, 0f, lead[i].z - 0.2f);
        }
        s.armR = Panel("Gull Arm", lead, trail, 0.016f, 4);
        s.armL = Mirror(s.armR);
        const float hand = 0.4f;
        const int hs = 16;
        lead = new Vector3[hs + 1];
        trail = new Vector3[hs + 1];
        for (int i = 0; i <= hs; i++)
        {
            float f = i / (float)hs, x = hand * f;
            float le = 0.06f - 0.14f * Mathf.Pow(f, 1.7f);
            float chord = 0.2f * (1f - 0.85f * Mathf.Pow(f, 1.5f)) - 0.006f * Mathf.Abs(Mathf.Sin(f * Mathf.PI * 4f)) * f;
            lead[i] = P(x, 0f, le);
            trail[i] = P(x, 0f, le - Mathf.Max(0.02f, chord));
        }
        s.handR = Panel("Gull Hand", lead, trail, 0.01f, 4);
        s.handL = Mirror(s.handR);
        const int ts = 6;
        var tl = new Vector3[ts + 1];
        var tr = new Vector3[ts + 1];
        for (int i = 0; i <= ts; i++)
        {
            float f = i / (float)ts, z = Mathf.Lerp(-0.14f, -0.27f, f);
            float w = Mathf.Lerp(0.035f, 0.055f, f) - (f > 0.8f ? (f - 0.8f) * 0.05f : 0f);
            tl[i] = P(-w, 0.1f, z);
            tr[i] = P(w, 0.1f, z);
        }
        s.tail = Panel("Gull Tail", tl, tr, 0.006f, 6);
        return s;
    }

    // ------------------------------------------------------------------ feathers

    private static float Hash(int x, int y, int seed)
    {
        float h = Mathf.Sin(x * 12.9898f + y * 78.233f + seed * 37.719f) * 43758.5453f;
        return h - Mathf.Floor(h);
    }

    private static Texture2D Paint(string name, int w, int h, System.Func<float, float, int, int, Color> f, int seed, float grain)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name };
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color c = f((x + 0.5f) / w, (y + 0.5f) / h, x, y);
                float n = Hash(x, y, seed) * 0.6f + Hash(x / 2, y / 3, seed + 1) * 0.4f;
                c *= 1f - grain + grain * 2f * n;
                c.a = 1f;
                px[y * w + x] = c;
            }
        tex.SetPixels(px);
        tex.wrapModeU = TextureWrapMode.Repeat;
        tex.wrapModeV = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Trilinear;
        tex.anisoLevel = 2;
        tex.Apply(true, true);
        return tex;
    }

    private static float Top(float u) { return 0.5f + 0.5f * Mathf.Cos((u - 0.5f) * Mathf.PI * 2f); }
    private static float Band(float x, float a, float b, float soft) { return Mathf.Clamp01((x - a) / soft + 0.5f) * Mathf.Clamp01((b - x) / soft + 0.5f); }

    private sealed class Look
    {
        public Material body, head, fold, wing, tail, leg;
    }

    private static Material Mat(Texture2D tex, Color tint, float smooth)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (tex != null) m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", tint);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", 0f);
        m.enableInstancing = true;
        return m;
    }

    /// <summary>Feral pigeon plumage. variant 0 blue-bar, 1 chequer, 2 ash-red, 3 dark.</summary>
    private static Look PigeonLook(int variant)
    {
        Color grey = new Color(0.5f, 0.53f, 0.6f), bar = new Color(0.08f, 0.08f, 0.1f), tailBand = new Color(0.1f, 0.1f, 0.12f);
        if (variant == 1) { grey = new Color(0.4f, 0.42f, 0.48f); }
        if (variant == 2) { grey = new Color(0.66f, 0.58f, 0.55f); bar = new Color(0.42f, 0.22f, 0.16f); tailBand = new Color(0.5f, 0.32f, 0.26f); }
        if (variant == 3) { grey = new Color(0.24f, 0.24f, 0.27f); bar = new Color(0.07f, 0.07f, 0.08f); }
        Color green = new Color(0.22f, 0.46f, 0.33f), purple = new Color(0.46f, 0.27f, 0.5f);
        if (variant == 2) { green = new Color(0.5f, 0.45f, 0.35f); purple = new Color(0.6f, 0.38f, 0.42f); }
        int seed = 11 + variant * 7;
        var look = new Look();
        look.body = Mat(Paint("Pigeon Body", 128, 64, (u, v, x, y) =>
        {
            float top = Top(u);
            Color c = grey * Mathf.Lerp(1.07f, 0.96f, top);
            if (variant == 0) c = Color.Lerp(c, new Color(0.74f, 0.76f, 0.8f), Band(v, -0.1f, 0.24f, 0.08f) * Mathf.Clamp01((top - 0.5f) * 3f));
            Color irid = Color.Lerp(purple, green, Mathf.Clamp01(top * 1.3f - 0.2f + (Hash(x / 3, y / 2, seed) - 0.5f) * 0.5f));
            c = Color.Lerp(c, Color.Lerp(grey * 0.95f, purple, 0.55f), Mathf.SmoothStep(0f, 1f, (v - 0.62f) / 0.25f) * (1f - top * 0.5f));
            c = Color.Lerp(c, irid, Mathf.SmoothStep(0f, 1f, (v - 0.82f) / 0.12f));
            // Feather scallops on the back.
            c *= 1f - 0.05f * top * Mathf.Abs(Mathf.Sin(v * 50f + Mathf.Sin(u * 60f) * 0.8f));
            return c;
        }, seed, 0.05f), Color.white, 0.22f);
        look.head = Mat(Paint("Pigeon Head", 128, 64, (u, v, x, y) =>
        {
            float top = Top(u);
            Color irid = Color.Lerp(purple, green, Mathf.Clamp01(top * 1.2f + (Hash(x / 3, y / 2, seed + 3) - 0.5f) * 0.6f - 0.1f));
            Color headGrey = grey * 0.93f;
            Color c = Color.Lerp(irid, headGrey, Mathf.SmoothStep(0f, 1f, (v - 0.3f) / 0.12f));
            // The eye: an orange iris round a black pupil, on each side.
            foreach (float eu in new[] { 0.27f, 0.73f })
            {
                float du = (u - eu) / 0.032f, dv = (v - 0.55f) / 0.045f, r = Mathf.Sqrt(du * du + dv * dv);
                if (r < 1.25f) c = Color.Lerp(c, new Color(0.25f, 0.24f, 0.26f), Mathf.Clamp01((1.25f - r) * 4f));  // the eye ring
                if (r < 1f) c = new Color(0.95f, 0.45f, 0.1f);
                if (r < 0.5f) c = new Color(0.02f, 0.02f, 0.02f);
            }
            // The white cere at the base of the bill, then the dark bill.
            c = Color.Lerp(c, new Color(0.86f, 0.85f, 0.8f), Band(v, 0.67f, 0.77f, 0.03f) * Mathf.Clamp01((top - 0.45f) * 3f));
            c = Color.Lerp(c, new Color(0.17f, 0.16f, 0.16f), Mathf.SmoothStep(0f, 1f, (v - 0.74f) / 0.04f) * (1f - Band(v, 0.67f, 0.77f, 0.03f) * Mathf.Clamp01((top - 0.45f) * 3f)));
            return c;
        }, seed + 1, 0.04f), Color.white, 0.32f);
        System.Func<float, float, int, int, Color> wingFold = (u, v, x, y) =>
        {
            Color c = grey * 1.1f;
            if (variant == 1 && v < 0.66f && Hash(Mathf.FloorToInt(u * 14f), Mathf.FloorToInt(v * 12f), seed + 5) > 0.45f) c = Color.Lerp(c, bar, 0.7f);
            if (variant != 1) c = Color.Lerp(c, bar, Mathf.Max(Band(v, 0.42f, 0.5f, 0.025f), Band(v, 0.56f, 0.64f, 0.025f)));
            c = Color.Lerp(c, grey * 0.42f, Mathf.SmoothStep(0f, 1f, (v - 0.7f) / 0.2f));
            c *= 1f - 0.06f * Mathf.Abs(Mathf.Sin(v * 70f));
            return c;
        };
        look.fold = Mat(Paint("Pigeon Folded Wing", 64, 64, wingFold, seed + 2, 0.04f), Color.white, 0.2f);
        look.wing = Mat(Paint("Pigeon Wing", 128, 64, (u, v, x, y) =>
        {
            // u: shoulder (0) to the tip (1, arm 0-0.375, hand beyond); v: leading (0) to trailing edge (1).
            Color c = grey * 1.14f;
            float arm = u < 0.375f ? 1f : 0f;
            if (variant == 1 && arm > 0f && v < 0.75f && Hash(Mathf.FloorToInt(u * 24f), Mathf.FloorToInt(v * 8f), seed + 6) > 0.45f) c = Color.Lerp(c, bar, 0.7f);
            if (variant != 1) c = Color.Lerp(c, bar, arm * Mathf.Max(Band(v, 0.52f, 0.64f, 0.03f), Band(v, 0.74f, 0.86f, 0.03f)));
            float primaries = Mathf.SmoothStep(0f, 1f, (u - 0.36f) / 0.5f);
            c = Color.Lerp(c, grey * 0.38f, primaries * Mathf.Lerp(0.5f, 1f, v));
            c *= 1f - 0.12f * primaries * Mathf.Abs(Mathf.Sin(u * Mathf.PI * 14f));      // the separate primaries
            c *= Mathf.Lerp(0.85f, 1f, Mathf.Clamp01(v * 10f));                            // a darker leading edge
            c = Color.Lerp(c, grey * 0.35f, Mathf.Clamp01((v - 0.92f) * 12f));             // and trailing edge
            return c;
        }, seed + 3, 0.04f), Color.white, 0.2f);
        look.tail = Mat(Paint("Pigeon Tail", 64, 64, (u, v, x, y) =>
        {
            // u along the tail (base to tip), v across.
            Color c = grey * 0.95f;
            c = Color.Lerp(c, new Color(0.78f, 0.78f, 0.8f), (v < 0.07f || v > 0.93f) && u < 0.78f ? 0.6f : 0f);
            c = Color.Lerp(c, tailBand, Mathf.SmoothStep(0f, 1f, (u - 0.74f) / 0.06f));
            c *= 1f - 0.08f * Mathf.Abs(Mathf.Sin(v * Mathf.PI * 6f));
            return c;
        }, seed + 4, 0.04f), Color.white, 0.2f);
        look.leg = Mat(null, new Color(0.72f, 0.3f, 0.32f), 0.35f);
        return look;
    }

    private static Look GullLook()
    {
        Color white = new Color(0.95f, 0.95f, 0.94f), mantle = new Color(0.64f, 0.68f, 0.73f);
        var look = new Look();
        look.body = Mat(Paint("Gull Body", 128, 64, (u, v, x, y) => Color.Lerp(white, mantle, Mathf.Clamp01((Top(u) - 0.8f) * 6f) * Band(v, 0.25f, 0.85f, 0.1f)), 3, 0.025f), Color.white, 0.25f);
        look.head = Mat(Paint("Gull Head", 128, 64, (u, v, x, y) =>
        {
            Color c = white;
            foreach (float eu in new[] { 0.28f, 0.72f })
            {
                float du = (u - eu) / 0.025f, dv = (v - 0.47f) / 0.04f, r = Mathf.Sqrt(du * du + dv * dv);
                if (r < 1f) c = new Color(0.9f, 0.85f, 0.45f);
                if (r < 0.45f) c = new Color(0.02f, 0.02f, 0.02f);
            }
            if (v > 0.72f) c = new Color(0.95f, 0.78f, 0.22f);
            if (v > 0.84f && v < 0.92f && Top(u) < 0.35f) c = new Color(0.85f, 0.15f, 0.1f);
            return c;
        }, 4, 0.02f), Color.white, 0.3f);
        look.wing = Mat(Paint("Gull Wing", 128, 64, (u, v, x, y) =>
        {
            Color c = mantle;
            c = Color.Lerp(c, white, Mathf.Clamp01((v - 0.84f) * 10f) * (u < 0.8f ? 1f : 0f));      // white trailing edge
            float tip = Mathf.SmoothStep(0f, 1f, (u - 0.78f) / 0.06f);
            c = Color.Lerp(c, new Color(0.05f, 0.05f, 0.06f), tip);
            if (u > 0.9f && u < 0.97f && v > 0.3f && v < 0.62f) c = white;                          // the mirrors
            c *= 1f - 0.07f * tip * Mathf.Abs(Mathf.Sin(u * Mathf.PI * 20f));
            return c;
        }, 5, 0.03f), Color.white, 0.25f);
        look.tail = Mat(Paint("Gull Tail", 64, 32, (u, v, x, y) => white, 6, 0.02f), Color.white, 0.25f);
        look.fold = look.wing;
        look.leg = Mat(null, new Color(0.85f, 0.6f, 0.55f), 0.3f);
        return look;
    }

    // ------------------------------------------------------------------ building

    private Transform Part(string name, Transform parent, Mesh mesh, Material m, Vector3 at, bool shadows)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = at;
        if (mesh != null)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = true;
        }
        return go.transform;
    }

    private Rig MakeRig(string name, Shape s, Look look, bool legs, bool shadows)
    {
        var rig = new Rig { armLength = s.armLength };
        rig.root = Part(name, transform, null, null, Vector3.zero, false);
        Part("Body", rig.root, s.body, look.body, Vector3.zero, shadows);
        Part("Tail", rig.root, s.tail, look.tail, Vector3.zero, shadows);
        rig.head = Part("Head", rig.root, s.head, look.head, s.headPivot, shadows);
        if (s.foldR != null)
        {
            rig.foldR = Part("Folded Wing R", rig.root, s.foldR, look.fold, Vector3.zero, shadows);
            rig.foldL = Part("Folded Wing L", rig.root, s.foldL, look.fold, Vector3.zero, shadows);
        }
        rig.armR = Part("Wing R", rig.root, s.armR, look.wing, s.shoulder, shadows);
        rig.armL = Part("Wing L", rig.root, s.armL, look.wing, new Vector3(-s.shoulder.x, s.shoulder.y, s.shoulder.z), shadows);
        rig.handR = Part("Hand R", rig.armR, s.handR, look.wing, new Vector3(s.armLength, 0f, 0f), shadows);
        rig.handL = Part("Hand L", rig.armL, s.handL, look.wing, new Vector3(-s.armLength, 0f, 0f), shadows);
        // The hand panels use the far part of the wing texture.
        if (legs && s.legR != null)
        {
            rig.legR = Part("Leg R", rig.root, s.legR, look.leg, s.hip, shadows);
            rig.legL = Part("Leg L", rig.root, s.legL, look.leg, new Vector3(-s.hip.x, s.hip.y, s.hip.z), shadows);
        }
        return rig;
    }

    private void Build()
    {
        random = new System.Random(9);
        Shape pigeonShape = PigeonShape(), gullShape = GullShape();
        // The hand panels' u runs 0-1 over the hand alone: shift them to the far part of the shared wing texture.
        RemapU(pigeonShape.handR, 0.375f, 1f); RemapU(pigeonShape.handL, 0.375f, 1f);
        RemapU(pigeonShape.armR, 0f, 0.375f); RemapU(pigeonShape.armL, 0f, 0.375f);
        RemapU(gullShape.handR, 0.43f, 1f); RemapU(gullShape.handL, 0.43f, 1f);
        RemapU(gullShape.armR, 0f, 0.43f); RemapU(gullShape.armL, 0f, 0.43f);

        Look gull = GullLook();
        int gulls = quest ? 8 : 18;
        for (int i = 0; i < gulls; i++)
        {
            var b = new Bird { rig = MakeRig("Gull", gullShape, gull, false, false), flying = true };
            b.rig.root.localScale = Vector3.one * (0.95f + (float)random.NextDouble() * 0.15f);
            b.centre = new Vector3(Mathf.Lerp(-300f, 300f, (float)random.NextDouble()), 0f, TestDriveCity.OceanZ - 35f - (float)random.NextDouble() * 50f);
            b.radius = 14f + (float)random.NextDouble() * 25f;
            b.height = 7f + (float)random.NextDouble() * 18f;
            b.speed = (9f + (float)random.NextDouble() * 4f) / b.radius * (random.NextDouble() < 0.5 ? 1f : -1f);
            b.phase = (float)random.NextDouble() * 100f;
            birds.Add(b);
        }

        var looks = new[] { PigeonLook(0), PigeonLook(1), PigeonLook(2), PigeonLook(3) };
        int[] mix = { 0, 0, 0, 1, 1, 0, 3, 1, 0, 2, 0, 1, 3, 0 };
        int pigeons = quest ? 8 : 14;
        for (int i = 0; i < pigeons; i++)
        {
            var b = new Bird { pigeon = true, rig = MakeRig("Pigeon", pigeonShape, looks[mix[i % mix.Length]], true, !quest) };
            b.rig.root.localScale = Vector3.one * (0.92f + (float)random.NextDouble() * 0.14f);
            // A loose flock: most within a few metres of each other.
            Vector2 c = new Vector2(Mathf.Lerp(AreaMin.x + 3f, AreaMax.x - 3f, 0.45f), Mathf.Lerp(AreaMin.y + 2f, AreaMax.y - 2f, 0.5f));
            Vector2 o = new Vector2((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f) * 7f;
            b.ground = new Vector3(c.x + o.x, GroundY, c.y + o.y);
            b.heading = (float)random.NextDouble() * 360f;
            b.phase = (float)random.NextDouble() * 10f;
            NewActivity(b);
            Fold(b, true);
            birds.Add(b);
        }
        nextFlockFlight = Time.time + 60f + (float)random.NextDouble() * 60f;
    }

    private static void RemapU(Mesh m, float from, float to)
    {
        Vector2[] uv = m.uv;
        for (int i = 0; i < uv.Length; i++) uv[i].x = Mathf.Lerp(from, to, uv[i].x);
        m.uv = uv;
    }

    private static void Fold(Bird b, bool folded)
    {
        Rig r = b.rig;
        if (r.foldR != null) { r.foldR.gameObject.SetActive(folded); r.foldL.gameObject.SetActive(folded); }
        r.armR.gameObject.SetActive(!folded);
        r.armL.gameObject.SetActive(!folded);
    }

    // ------------------------------------------------------------------ behaviour

    private void Update()
    {
        if (viewer == null || !viewer.gameObject.activeInHierarchy)
            viewer = Camera.main != null ? Camera.main.transform : null;
        float t = Time.time, dt = Mathf.Min(Time.deltaTime, 0.05f);
        Vector3 eye = viewer != null ? viewer.position : new Vector3(9999f, 0f, 9999f);

        // Now and then the whole flock goes up for a turn round the block, as pigeons do for no reason at all.
        if (t > nextFlockFlight)
        {
            nextFlockFlight = t + 90f + (float)random.NextDouble() * 90f;
            foreach (Bird b in birds) if (b.pigeon && !b.flying) b.alarm = t + (float)random.NextDouble() * 0.8f;
        }
        for (int i = 0; i < birds.Count; i++)
        {
            Bird b = birds[i];
            if (!b.pigeon) Gull(b, t);
            else if (b.flying) Fly(b, t, dt, eye);
            else Walk(b, t, dt, eye);
        }
    }

    private static void Wings(Rig r, float arm, float hand, float sweep)
    {
        r.armR.localRotation = Quaternion.Euler(0f, sweep, arm);
        r.armL.localRotation = Quaternion.Euler(0f, -sweep, -arm);
        r.handR.localRotation = Quaternion.Euler(0f, sweep * 0.8f, hand);
        r.handL.localRotation = Quaternion.Euler(0f, -sweep * 0.8f, -hand);
    }

    private static void Gull(Bird b, float t)
    {
        float a = t * b.speed + b.phase;
        float bob = Mathf.Sin(t * 0.4f + b.phase) * 2f;
        Vector3 p = b.centre + new Vector3(Mathf.Cos(a) * b.radius, b.height + bob, Mathf.Sin(a) * b.radius);
        Vector3 tangent = new Vector3(-Mathf.Sin(a), Mathf.Cos(a * 0.4f) * 0.04f, Mathf.Cos(a)) * Mathf.Sign(b.speed);
        b.rig.root.position = p;
        float bank = -Mathf.Sign(b.speed) * Mathf.Clamp(Mathf.Abs(b.speed) * b.radius * b.radius * 0.0012f + 12f, 12f, 30f);
        b.rig.root.rotation = Quaternion.LookRotation(tangent, Vector3.up) * Quaternion.Euler(0f, 0f, bank);
        // A few deep, slow beats, then a long glide on bowed wings (arms up, hands down).
        float cycle = Mathf.Repeat(t * 0.12f + b.phase, 1f);
        if (cycle < 0.3f)
        {
            float w = t * Mathf.PI * 2f * 3.1f + b.phase;
            Wings(b.rig, 6f + Mathf.Sin(w) * 30f, Mathf.Sin(w - 0.8f) * 18f - 4f, Mathf.Max(0f, Mathf.Cos(w)) * 12f);
        }
        else
        {
            float s = Mathf.Sin(t * 1.3f + b.phase);
            Wings(b.rig, 8f + s * 1.5f, -12f + s * 1f, 4f);
        }
        b.rig.head.localRotation = Quaternion.Euler(6f, Mathf.Sin(t * 0.5f + b.phase) * 15f, 0f);
    }

    private void NewActivity(Bird b)
    {
        double r = random.NextDouble();
        b.activity = r < 0.45 ? 0 : (r < 0.8 ? 1 : 2);
        b.activityLeft = 1.2f + (float)random.NextDouble() * (b.activity == 2 ? 2.5f : 4f);
        b.turn = ((float)random.NextDouble() - 0.5f) * 110f;
        b.walkSpeed = 0.16f + (float)random.NextDouble() * 0.12f;
        b.peckWait = (float)random.NextDouble() * 0.6f;
    }

    private void Walk(Bird b, float t, float dt, Vector3 eye)
    {
        Rig r = b.rig;
        b.activityLeft -= dt;
        if (b.activityLeft <= 0f) NewActivity(b);

        // Keep to the forecourt and a little apart from the others.
        Vector2 at = new Vector2(b.ground.x, b.ground.z);
        Vector2 steer = Vector2.zero;
        if (at.x < AreaMin.x + 1f || at.x > AreaMax.x - 1f || at.y < AreaMin.y + 1f || at.y > AreaMax.y - 1f)
            steer += ((AreaMin + AreaMax) * 0.5f - at).normalized;
        foreach (Bird o in birds)
        {
            if (o == b || !o.pigeon || o.flying) continue;
            Vector2 d = at - new Vector2(o.ground.x, o.ground.z);
            if (d.sqrMagnitude < 0.09f && d.sqrMagnitude > 1e-6f) steer += d.normalized * 0.8f;
        }
        Vector3 flatEye = new Vector3(eye.x, 0f, eye.z), flat = new Vector3(b.ground.x, 0f, b.ground.z);
        float near = Vector3.Distance(flatEye, flat);
        if (near < 6f) steer += new Vector2(flat.x - flatEye.x, flat.z - flatEye.z).normalized * 0.6f;   // wary, walking away
        float desired = b.heading + b.turn * dt;
        if (steer.sqrMagnitude > 0.01f)
            desired = Mathf.MoveTowardsAngle(b.heading, Mathf.Atan2(steer.x, steer.y) * Mathf.Rad2Deg, 140f * dt);
        b.heading = desired;

        bool walking = b.activity == 0 || (b.activity == 1 && b.peckClock < 0f && Mathf.Repeat(t * 0.7f + b.phase, 1f) < 0.4f) || near < 6f;
        float speed = walking ? b.walkSpeed * (near < 6f ? 1.6f : 1f) : 0f;
        Vector3 forward = Quaternion.Euler(0f, b.heading, 0f) * Vector3.forward;
        b.ground += forward * speed * dt;

        // Steps at about three a second; the head holds still in space while the body walks under it, then thrusts.
        float stepLength = Mathf.Max(0.04f, speed / 3.2f);
        if (speed > 0.01f) b.step += speed / stepLength * dt;
        float s = Mathf.Repeat(b.step, 1f);
        float headZ = 0f, headY = 0f;
        if (speed > 0.01f)
        {
            float d = stepLength * 0.85f;
            headZ = s < 0.62f ? d * (0.5f - s / 0.62f) : d * (-0.5f + Mathf.SmoothStep(0f, 1f, (s - 0.62f) / 0.38f));
            headY = s < 0.62f ? 0f : -0.004f * Mathf.Sin((s - 0.62f) / 0.38f * Mathf.PI);
        }
        float stride = b.step * 0.5f * Mathf.PI * 2f;
        float legSwing = speed > 0.01f ? Mathf.Sin(stride) * 26f : 0f;
        r.legR.localRotation = Quaternion.Euler(-legSwing, 0f, 0f);
        r.legL.localRotation = Quaternion.Euler(legSwing, 0f, 0f);
        float waddle = speed > 0.01f ? Mathf.Sin(stride) * 4f : 0f;
        r.root.position = b.ground + Vector3.up * (speed > 0.01f ? Mathf.Abs(Mathf.Sin(stride)) * 0.004f : 0f);
        r.root.rotation = Quaternion.Euler(0f, b.heading, waddle);

        // Pecking: a quick dip of the head to the ground and up again.
        float pitch = 0f;
        if (b.activity == 1 && !walking)
        {
            if (b.peckClock < 0f)
            {
                b.peckWait -= dt;
                if (b.peckWait <= 0f) b.peckClock = 0f;
            }
        }
        if (b.peckClock >= 0f)
        {
            b.peckClock += dt;
            const float peckTime = 0.32f;
            pitch = Mathf.Sin(Mathf.Clamp01(b.peckClock / peckTime) * Mathf.PI) * 78f;
            headZ += pitch / 78f * 0.012f;
            if (b.peckClock > peckTime)
            {
                b.peckClock = -1f;
                b.peckWait = 0.25f + (float)random.NextDouble() * 1.2f;
            }
        }
        // Looking about in little jerks.
        b.lookWait -= dt;
        if (b.lookWait <= 0f)
        {
            b.lookWait = 0.4f + (float)random.NextDouble() * (b.activity == 2 ? 0.8f : 2f);
            b.lookTarget = walking ? ((float)random.NextDouble() - 0.5f) * 20f : ((float)random.NextDouble() - 0.5f) * 100f;
        }
        b.look = Mathf.MoveTowardsAngle(b.look, b.lookTarget, 600f * dt);
        r.head.localPosition = PigeonHeadPivot + new Vector3(0f, headY, headZ);
        r.head.localRotation = Quaternion.Euler(pitch, b.look, (b.activity == 2 ? Mathf.Sin(t * 0.9f + b.phase) * 10f : 0f) - waddle * 0.8f);
        r.foldR.localRotation = r.foldL.localRotation = Quaternion.identity;

        // Startled: by someone coming close, or by the others going up.
        bool scared = near < 2.8f && Mathf.Abs(eye.y - b.ground.y) < 3f;
        if (scared && b.alarm < 0f) b.alarm = t;
        if (b.alarm >= 0f && t >= b.alarm) TakeOff(b, t, eye);
    }

    private static readonly Vector3 PigeonHeadPivot = new Vector3(0f, 0.135f, 0.062f);

    private void TakeOff(Bird b, float t, Vector3 eye)
    {
        b.alarm = -1f;
        b.flying = true;
        b.peckClock = -1f;
        Fold(b, false);
        Vector3 p = b.rig.root.position;
        Vector3 away = new Vector3(p.x - eye.x, 0f, p.z - eye.z);
        if (away.sqrMagnitude < 0.01f || away.sqrMagnitude > 400f) away = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f) * Vector3.forward;
        away.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, away) * (random.NextDouble() < 0.5 ? 1f : -1f);
        b.velocity = away * 1.4f + Vector3.up * 2.6f;
        b.yaw = Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg;
        // Others nearby go up a moment later.
        foreach (Bird o in birds)
            if (o.pigeon && !o.flying && o.alarm < 0f && (o.ground - b.ground).sqrMagnitude < 5f * 5f)
                o.alarm = t + 0.08f + (float)random.NextDouble() * 0.5f;
        // Up and away, a turn round, and in to land somewhere else on the forecourt, away from the viewer.
        Vector3 land = b.ground;
        for (int k = 0; k < 12; k++)
        {
            land = new Vector3(Mathf.Lerp(AreaMin.x + 1.5f, AreaMax.x - 1.5f, (float)random.NextDouble()), GroundY, Mathf.Lerp(AreaMin.y + 1.5f, AreaMax.y - 1.5f, (float)random.NextDouble()));
            if (Vector3.Distance(new Vector3(eye.x, GroundY, eye.z), land) > 7f) break;
        }
        b.route.Clear();
        b.route.Add(p + away * 5f + Vector3.up * 3.5f);
        Vector3 wide = p + away * (14f + (float)random.NextDouble() * 8f) + side * (6f + (float)random.NextDouble() * 8f) + Vector3.up * (8f + (float)random.NextDouble() * 4f);
        b.route.Add(wide);
        Vector3 back = land - wide; back.y = 0f;
        b.route.Add(land - back.normalized * 7f + Vector3.up * 2.2f);
        b.route.Add(land);
        b.flapClock = 0f;
    }

    private void Fly(Bird b, float t, float dt, Vector3 eye)
    {
        Rig r = b.rig;
        Vector3 p = r.root.position;
        Vector3 target = b.route[0];
        bool last = b.route.Count == 1;
        Vector3 to = target - p;
        float dist = to.magnitude;
        float want = last ? Mathf.Clamp(dist * 1.5f, 0.6f, 7f) : 8.5f;
        b.velocity = Vector3.MoveTowards(b.velocity, to.normalized * want, (last ? 9f : 7f) * dt);
        Vector3 before = b.velocity;
        p += b.velocity * dt;
        if (!last && dist < 2.5f) b.route.RemoveAt(0);
        if (last && (dist < 0.06f || p.y < GroundY))
        {
            // Down: wings folded, a look round.
            b.flying = false;
            b.ground = new Vector3(target.x, GroundY, target.z);
            b.heading = b.yaw;
            b.activity = 2;
            b.activityLeft = 1f + (float)random.NextDouble() * 2f;
            Fold(b, true);
            Wings(r, 0f, 0f, 0f);
            Walk(b, t, dt, eye);
            return;
        }
        r.root.position = p;

        Vector3 flat = new Vector3(b.velocity.x, 0f, b.velocity.z);
        float newYaw = flat.sqrMagnitude > 0.04f ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg : b.yaw;
        float yawRate = Mathf.DeltaAngle(b.yaw, newYaw) / Mathf.Max(dt, 1e-4f);
        b.yaw = Mathf.MoveTowardsAngle(b.yaw, newYaw, 360f * dt);
        b.bank = Mathf.Lerp(b.bank, Mathf.Clamp(-yawRate * 0.35f, -45f, 45f), 1f - Mathf.Exp(-4f * dt));
        bool flare = last && dist < 2.2f;
        float climb = Mathf.Atan2(b.velocity.y, Mathf.Max(0.3f, flat.magnitude)) * Mathf.Rad2Deg;
        float pitch = flare ? -38f * Mathf.Clamp01(1.2f - dist / 2.2f) : -Mathf.Clamp(climb, -25f, 30f);
        r.root.rotation = Quaternion.Euler(pitch, b.yaw, b.bank);

        // Wings: hard beating on the climb, slow and in the landing; a glide on raised wings on the way down.
        bool gliding = !last && b.velocity.y < -0.4f && b.route.Count <= 2 && flat.magnitude > 4f;
        if (gliding)
        {
            float wob = Mathf.Sin(t * 3f + b.phase) * 2f;
            Wings(r, 24f + wob, -6f, 2f);
        }
        else
        {
            float rate = flare ? 9.5f : (b.velocity.y > 0.5f ? 9f : 7.5f);
            b.flapClock += dt * rate * Mathf.PI * 2f;
            float w = b.flapClock;
            float amp = flare ? 62f : 52f;
            Wings(r, 12f + Mathf.Sin(w) * amp, Mathf.Sin(w - 0.9f) * 26f - 4f, Mathf.Max(0f, Mathf.Cos(w)) * (flare ? 10f : 22f));
        }
        // Legs tucked back in flight, swung down and forward to land; the head level.
        float legs = flare ? -15f : 70f;
        r.legR.localRotation = Quaternion.Euler(legs, 0f, 0f);
        r.legL.localRotation = Quaternion.Euler(legs, 0f, 0f);
        r.head.localPosition = PigeonHeadPivot;
        r.head.localRotation = Quaternion.Euler(-pitch * 0.7f, 0f, -b.bank * 0.5f);
    }
}
