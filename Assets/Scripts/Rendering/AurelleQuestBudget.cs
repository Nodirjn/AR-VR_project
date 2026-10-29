using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// The Quest's budget. Runs only when the scene starts at quality level 0 or 1 (Very Low, Low: the Android default), so
// the PC's Ultra look is never touched. One frame after the scene's Start calls (the traffic cars rig their runtime wheel
// meshes in Start), it trims what a headset cannot draw at 72 Hz without changing what the visitor works with:
// - Half the street life: every second traffic car, pedestrian and cyclist is switched off before it starts.
// - Merged static meshes. The district's static pieces (thousands of boxes: curbs, joints, markings, windows, frames)
//   are merged per 40 m cell and material, and each premium display car per material; the originals keep their
//   colliders and only stop rendering. Signals (their lamps change material), text, movers, lightmapped renderers,
//   renderers with property blocks and meshes without CPU access stay as they are.
// - Cull-only LOD groups. Each exterior mover (traffic and parked cars, pedestrians, cyclists with their bikes) becomes
//   one unit that disappears when it is small in the view; small exterior details, small parts of the premium display
//   cars and the hero car's inner content (V6 and internals, one group per part, so it moves with the part) disappear
//   when they are tiny in the view. A culled renderer stays enabled, so the explorer's picking, highlight, take-out and
//   restore see no difference.
// - Fewer real-time shadow casters. At these levels only the sun casts real-time shadows (the Quest pipeline has no
//   additional-light shadows), and indoors the roof already shades everything: the hero car, the premium cars, the
//   furniture and Remy stop casting (their contact shadows are baked: the hero car's quad and the bake occluders);
//   flat geometry on the ground (roads, sidewalks, markings, floors) and small details stop casting everywhere, and the
//   district's static pieces cast only when large. The roof, the walls, the building masses, the trees, the cars and
//   the people keep casting.
public sealed class AurelleQuestBudget : MonoBehaviour
{
    public const int MaxQualityLevel = 1;

    // Screen-relative heights (before the quality level's LOD bias, 0.4 at Low) below which a group is culled.
    private const float CarCull = 0.012f;
    private const float PersonCull = 0.009f;
    private const float DetailCull = 0.005f;
    private const float InnerCull = 0.002f;
    // Effective sizes (square root of the two largest extents, metres) below which a renderer is a "detail".
    private const float ExteriorDetailSize = 6f;
    private const float DisplayDetailSize = 1.5f;
    private const float ShadowDetailSize = 0.5f;
    private const float StaticShadowSize = 3f;

    public static bool Applied { get; private set; }
    public static int LodGroupsAdded { get; private set; }
    public static int ShadowCastersRemoved { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Applied = false;
        LodGroupsAdded = 0;
        ShadowCastersRemoved = 0;
        AgentsParked = 0;
        RenderersMerged = 0;
        MergedDraws = 0;
        mergedSources.Clear();
    }

    public static int AgentsParked { get; private set; }
    public static int RenderersMerged { get; private set; }
    public static int MergedDraws { get; private set; }

    private const float MergeCell = 40f;
    private static readonly HashSet<Renderer> mergedSources = new HashSet<Renderer>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (QualitySettings.GetQualityLevel() > MaxQualityLevel || FindAnyObjectByType<AurelleQuestBudget>() != null)
            return;
        ThinOutAgents();
        new GameObject("Aurelle Quest Budget").AddComponent<AurelleQuestBudget>();
    }

    // Half the street life, before any agent starts: every second traffic car, pedestrian and cyclist (in hierarchy
    // order) is switched off, so it leaves the agents' registries (OnDisable) and never starts. Pedestrian 3, who walks
    // the crossing in front of the dealership, always stays.
    private static void ThinOutAgents()
    {
        Thin(FindObjectsByType<ExteriorTrafficCarAgent>(FindObjectsSortMode.None));
        Thin(FindObjectsByType<ExteriorPedestrianAgent>(FindObjectsSortMode.None));
        Thin(FindObjectsByType<ExteriorCyclistAgent>(FindObjectsSortMode.None));
    }

    private static void Thin<T>(T[] agents) where T : MonoBehaviour
    {
        var ordered = new List<T>(agents);
        ordered.Sort((a, b) => string.CompareOrdinal(Path(a.transform), Path(b.transform)));
        bool keepNext = true;
        foreach (T agent in ordered)
        {
            bool keep = keepNext || agent.name == "Pedestrian 3";
            keepNext = !keepNext;
            if (keep) continue;
            agent.gameObject.SetActive(false);
            AgentsParked++;
        }
    }

    private static string Path(Transform t)
    {
        string path = t.GetSiblingIndex().ToString("D4");
        for (Transform p = t.parent; p != null; p = p.parent) path = p.GetSiblingIndex().ToString("D4") + "/" + path;
        return path;
    }

    private IEnumerator Start()
    {
        yield return null;
        if (!Applied && QualitySettings.GetQualityLevel() <= MaxQualityLevel)
            Apply(gameObject.scene);
    }

    private static void Apply(Scene scene)
    {
        Applied = true;
        Transform exterior = null, premium = null, car = null, zones = null;
        var shadowRoots = new List<Transform>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            switch (root.name)
            {
                case "Exterior Environment": exterior = root.transform; shadowRoots.Add(root.transform); break;
                case "Showroom Premium Collection": premium = root.transform; break;
                case "Showroom Environment": shadowRoots.Add(root.transform); break;
                case "Showroom Interior Zones": zones = root.transform; break;
                case "CarConcept": car = root.transform; break;
            }
        }

        var indoor = new List<Renderer>();
        foreach (Transform root in new[] { car, premium, zones })
            if (root != null) indoor.AddRange(root.GetComponentsInChildren<Renderer>(true));
        foreach (ShowroomPresenterAgent remy in FindObjectsByType<ShowroomPresenterAgent>(FindObjectsSortMode.None))
            indoor.AddRange(remy.GetComponentsInChildren<Renderer>(true));
        foreach (Renderer r in indoor)
            if (r != null && r.shadowCastingMode == ShadowCastingMode.On)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                ShadowCastersRemoved++;
            }
        var moverRoots = new List<Transform>();
        foreach (var agent in FindObjectsByType<ExteriorTrafficCarAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None)) moverRoots.Add(agent.transform);
        foreach (var agent in FindObjectsByType<ExteriorPedestrianAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None)) moverRoots.Add(agent.transform);
        foreach (var agent in FindObjectsByType<ExteriorCyclistAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None)) moverRoots.Add(agent.transform);
        if (exterior != null)
            foreach (Transform t in exterior.GetComponentsInChildren<Transform>(true))
                if (t.name == "Parked Customer Vehicle") moverRoots.Add(t);
        foreach (Transform root in shadowRoots)
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.shadowCastingMode != ShadowCastingMode.On || !TryGetExtents(r, out Vector3 extents)) continue;
                Bounds world = r.bounds;
                bool flatOnGround = world.size.y < 0.25f && world.min.y < 0.4f;
                // The district's static pieces cast only when large (building masses, parapets, canopies, tree crowns):
                // windows, frames, mullions, signs and posts add draws for shadows nobody sees. Cars and people, and
                // the showroom's own architecture (its roof and walls keep the sun out), keep theirs.
                float minSize = root == exterior && !IsUnder(r.transform, moverRoots) ? StaticShadowSize : ShadowDetailSize;
                if (flatOnGround || EffectiveSize(extents) < minSize)
                {
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    ShadowCastersRemoved++;
                }
            }
        var movers = new List<(Transform root, float cull)>();
        if (exterior != null)
        {
            foreach (var agent in FindObjectsByType<ExteriorTrafficCarAgent>(FindObjectsSortMode.None)) movers.Add((agent.transform, CarCull));
            foreach (var agent in FindObjectsByType<ExteriorPedestrianAgent>(FindObjectsSortMode.None)) movers.Add((agent.transform, PersonCull));
            foreach (var agent in FindObjectsByType<ExteriorCyclistAgent>(FindObjectsSortMode.None)) movers.Add((agent.transform, PersonCull));
            foreach (Transform t in exterior.GetComponentsInChildren<Transform>(true))
                if (t.name == "Parked Customer Vehicle") movers.Add((t, CarCull));
            var keep = new List<Transform>();
            foreach (var (root, _) in movers) keep.Add(root);
            foreach (var signal in exterior.GetComponentsInChildren<ExteriorTrafficSignal>(true)) keep.Add(signal.transform);
            var mergedRoot = new GameObject("Aurelle Quest Merged District").transform;
            mergedRoot.SetParent(exterior, false);
            Merge(exterior, keep, mergedRoot, MergeCell);
        }
        if (premium != null)
            foreach (Transform display in premium)
                Merge(display, null, display, 0f);

        var grouped = new HashSet<Renderer>();
        foreach (LODGroup existing in FindObjectsByType<LODGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            foreach (LOD lod in existing.GetLODs())
                foreach (Renderer r in lod.renderers)
                    if (r != null) grouped.Add(r);

        if (exterior != null)
        {
            foreach (var (root, cull) in movers)
                AddGroup(root, root.GetComponentsInChildren<Renderer>(true), cull, -1f, grouped);
            foreach (Renderer r in exterior.GetComponentsInChildren<Renderer>(true))
                AddDetail(r, ExteriorDetailSize, DetailCull, grouped);
        }
        if (premium != null)
            foreach (Renderer r in premium.GetComponentsInChildren<Renderer>(true))
                AddDetail(r, DisplayDetailSize, DetailCull, grouped);
        if (car != null)
        {
            foreach (Transform inner in new[] { car.Find("Internals"), car.Find("V6_Engine_Prototype(Clone)") })
                if (inner != null)
                    foreach (Renderer r in inner.GetComponentsInChildren<Renderer>(true))
                        AddDetail(r, float.MaxValue, InnerCull, grouped);
        }

        Debug.Log("Aurelle Quest budget (quality " + QualitySettings.names[QualitySettings.GetQualityLevel()] + "): " + AgentsParked
                  + " street agents off, " + RenderersMerged + " static renderers merged into " + MergedDraws + ", " + LodGroupsAdded
                  + " cull-only LOD groups, " + ShadowCastersRemoved + " shadow casters off.");
    }

    // Merges the enabled, static mesh renderers under root (not under any of keep) per cell (cell <= 0: one cell), material
    // and shadow settings into new renderers under parent; the originals stop rendering (enabled = false) and keep their
    // colliders.
    private static void Merge(Transform root, List<Transform> keep, Transform parent, float cell)
    {
        var groups = new Dictionary<(int, int, Material, ShadowCastingMode, bool), List<CombineInstance>>();
        var order = new List<(int, int, Material, ShadowCastingMode, bool)>();
        var sources = new List<Renderer>();
        foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(false))
        {
            if (!r.enabled || r.HasPropertyBlock() || (r.lightmapIndex >= 0 && r.lightmapIndex < 65534)) continue;
            if (r.GetComponent<TextMesh>() != null || r.GetComponentInParent<LODGroup>() != null) continue;
            if (keep != null && IsUnder(r.transform, keep)) continue;
            MeshFilter filter = r.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            // A static-batched renderer's own matrix is the batch's (its vertices are already in world space); its
            // transform still holds the pose the box was authored with.
            Matrix4x4 world = r.isPartOfStaticBatch ? r.transform.localToWorldMatrix : r.localToWorldMatrix;
            if (mesh != null && !mesh.isReadable && (IsBuiltinCube(mesh) || IsStaticBatchedCube(r, mesh)) && FitsUnitCube(r, world)) mesh = ReadableCube;
            if (mesh == null || !mesh.isReadable || world.determinant < 0f) continue;
            Material[] materials = r.sharedMaterials;
            if (materials.Length < mesh.subMeshCount) continue;
            Vector3 c = r.bounds.center;
            int cx = cell > 0f ? Mathf.FloorToInt(c.x / cell) : 0, cz = cell > 0f ? Mathf.FloorToInt(c.z / cell) : 0;
            Matrix4x4 m = parent.worldToLocalMatrix * world;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (materials[sub] == null) continue;
                var key = (cx, cz, materials[sub], r.shadowCastingMode, r.receiveShadows);
                if (!groups.TryGetValue(key, out List<CombineInstance> list)) { groups[key] = list = new List<CombineInstance>(); order.Add(key); }
                list.Add(new CombineInstance { mesh = mesh, subMeshIndex = sub, transform = m });
            }
            sources.Add(r);
        }
        if (sources.Count < 2) return;
        foreach (var key in order)
        {
            var mesh = new Mesh { name = "Quest Merged " + key.Item3.name, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(groups[key].ToArray(), true, true, false);
            mesh.UploadMeshData(true);
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = key.Item3;
            renderer.shadowCastingMode = key.Item4;
            renderer.receiveShadows = key.Item5;
            MergedDraws++;
        }
        foreach (Renderer r in sources) { r.enabled = false; mergedSources.Add(r); }
        RenderersMerged += sources.Count;
    }

    // The district is built from Unity's built-in cube, which has no CPU-side data in a player. Its stand-in has the same
    // 24 vertices, normals, tangents, UVs and triangles (checked against the built-in cube in the Editor).
    private static Mesh readableCube;

    public static Mesh ReadableCube
    {
        get
        {
            if (readableCube != null) return readableCube;
            var v = new[]
            {
                new Vector3(0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f)
            };
            var uv = new[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0),
                new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0)
            };
            int[] t = { 0, 2, 3, 0, 3, 1, 8, 4, 5, 8, 5, 9, 10, 6, 7, 10, 7, 11, 12, 13, 14, 12, 14, 15, 16, 17, 18, 16, 18, 19, 20, 21, 22, 20, 22, 23 };
            readableCube = new Mesh { name = "Quest Readable Cube", vertices = v, uv = uv, triangles = t };
            readableCube.RecalculateNormals();
            readableCube.RecalculateTangents();
            readableCube.RecalculateBounds();
            return readableCube;
        }
    }

    // A renderer that static batching (at build time on a player, at load in the Editor) has put on a combined mesh keeps
    // its transform; its own range of the combined mesh tells a box (24 vertices, 36 indices) from the rest.
    private static bool IsStaticBatchedCube(MeshRenderer r, Mesh combined)
    {
        if (!r.isPartOfStaticBatch || r.sharedMaterials.Length != 1) return false;
        try
        {
            int first = r.subMeshStartIndex;
            if (first < 0 || first >= combined.subMeshCount) return false;
            SubMeshDescriptor range = combined.GetSubMesh(first);
            return range.vertexCount == 24 && range.indexCount == 36 && range.topology == MeshTopology.Triangles;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    // The stand-in replaces a mesh only where the renderer's world bounds are those of the unit cube under its transform
    // (within 1 cm): a box mesh of another size, or one whose size is baked into its vertices, keeps its own renderer.
    private static bool FitsUnitCube(Renderer r, Matrix4x4 m)
    {
        Bounds expected = new Bounds(m.MultiplyPoint3x4(new Vector3(-0.5f, -0.5f, -0.5f)), Vector3.zero);
        for (int i = 1; i < 8; i++)
            expected.Encapsulate(m.MultiplyPoint3x4(new Vector3((i & 1) != 0 ? 0.5f : -0.5f, (i & 2) != 0 ? 0.5f : -0.5f, (i & 4) != 0 ? 0.5f : -0.5f)));
        Bounds actual = r.bounds;
        return (expected.center - actual.center).sqrMagnitude < 1e-4f && (expected.size - actual.size).sqrMagnitude < 1e-4f;
    }

    public static bool IsBuiltinCube(Mesh mesh) =>
        mesh != null && mesh.name == "Cube" && mesh.vertexCount == 24 && mesh.subMeshCount == 1 && mesh.GetIndexCount(0) == 36;

    private static bool IsUnder(Transform t, List<Transform> roots)
    {
        foreach (Transform root in roots)
            if (root != null && t.IsChildOf(root)) return true;
        return false;
    }

    private static void AddDetail(Renderer r, float maxSize, float cull, HashSet<Renderer> grouped)
    {
        if (r == null || mergedSources.Contains(r) || !r.gameObject.activeInHierarchy || grouped.Contains(r) || r.GetComponent<LODGroup>() != null ||
            !TryGetExtents(r, out Vector3 extents))
            return;
        float size = EffectiveSize(extents);
        if (size >= maxSize) return;
        AddGroup(r.transform, new[] { r }, cull, size, grouped);
    }

    private static void AddGroup(Transform at, Renderer[] renderers, float cull, float worldSize, HashSet<Renderer> grouped)
    {
        var members = new List<Renderer>();
        foreach (Renderer r in renderers)
            if (r != null && !grouped.Contains(r) && !(r is ParticleSystemRenderer)) members.Add(r);
        if (members.Count == 0 || at.GetComponent<LODGroup>() != null) return;
        LODGroup group = at.gameObject.AddComponent<LODGroup>();
        group.fadeMode = LODFadeMode.None;
        group.SetLODs(new[] { new LOD(cull, members.ToArray()) });
        group.RecalculateBounds();
        if (worldSize > 0f)
        {
            Vector3 s = at.lossyScale;
            float scale = Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
            if (scale > 1e-5f) group.size = worldSize / scale;
        }
        foreach (Renderer r in members) grouped.Add(r);
        LodGroupsAdded++;
    }

    // The renderer's own extents in metres (mesh bounds times the absolute lossy scale), independent of its rotation and
    // of whether it is enabled. A static-batched renderer's mesh is the whole batch, so its world bounds stand in.
    private static bool TryGetExtents(Renderer r, out Vector3 extents)
    {
        extents = Vector3.zero;
        if (r.isPartOfStaticBatch)
        {
            extents = r.bounds.size;
            return extents.sqrMagnitude > 0f;
        }
        Mesh mesh = null;
        if (r is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
        else if (r is MeshRenderer)
        {
            MeshFilter filter = r.GetComponent<MeshFilter>();
            if (filter != null) mesh = filter.sharedMesh;
        }
        if (mesh == null) return false;
        Vector3 s = r.transform.lossyScale;
        extents = Vector3.Scale(mesh.bounds.size, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));
        return true;
    }

    // Square root of the two largest extents: a thin bar or a lane line counts as small, a flat panel by its area.
    private static float EffectiveSize(Vector3 e)
    {
        float a = Mathf.Max(e.x, Mathf.Max(e.y, e.z));
        float c = Mathf.Min(e.x, Mathf.Min(e.y, e.z));
        float b = e.x + e.y + e.z - a - c;
        return Mathf.Sqrt(a * b);
    }
}
