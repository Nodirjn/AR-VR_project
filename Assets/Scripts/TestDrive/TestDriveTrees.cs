using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Real trees for the district. The environment builder plants "Street Tree" stand-ins (a cylinder trunk and three
/// sphere canopies); at load each one keeps its collider but stops rendering, and a generated tree takes its place:
/// branching trunk with bark texture, thousands of alpha-tested leaf cards (ez-tree, MIT). Four models, chosen by
/// position so the same street always looks the same, each scaled to 7.5–11 m and turned at random. On the headset
/// the leaves do not cast shadows and each tree disappears when it is small in the view.
/// </summary>
public static class TestDriveTrees
{
    private static readonly string[] Models = { "TestDrive/Trees/oak_a", "TestDrive/Trees/oak_b", "TestDrive/Trees/oak_c", "TestDrive/Trees/aspen_g" };

    public static int Replaced { get; private set; }

    public static void ReplaceStreetTrees()
    {
        var prefabs = new List<GameObject>();
        var heights = new List<float>();
        var bases = new List<float>();
        for (int i = 0; i < Models.Length; i++)
        {
            GameObject model = Resources.Load<GameObject>(Models[i]);
            if (model == null) continue;
            Bounds b;
            if (!ModelBounds(model, out b) || b.size.y < 0.01f) continue;
            prefabs.Add(model);
            heights.Add(b.size.y);
            bases.Add(b.min.y);
        }
        if (prefabs.Count == 0)
        {
            Debug.LogWarning("[Test Drive] No tree models in Resources/TestDrive/Trees; the street trees stay as they are.");
            return;
        }
        bool quest = QualitySettings.GetQualityLevel() <= 1 || Application.platform == RuntimePlatform.Android;
        var stand = new List<Transform>();
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == "Street Tree" && t.Find("Tapered Tree Trunk") != null) stand.Add(t);

        foreach (Transform tree in stand)
        {
            if (tree.Find("Generated Tree") != null) continue;
            float height = 0f;
            foreach (Renderer r in tree.GetComponentsInChildren<Renderer>(true))
            {
                height = Mathf.Max(height, r.bounds.max.y - tree.position.y);
                r.enabled = false;
            }
            Vector3 p = tree.position;
            int hash = Mathf.Abs(Mathf.RoundToInt(p.x * 7.13f) * 73856093 ^ Mathf.RoundToInt(p.z * 3.71f) * 19349663);
            var random = new System.Random(hash);
            int pick = random.Next(prefabs.Count);
            float target = Mathf.Clamp(height * 1.35f, 7.5f, 11f) * (0.9f + 0.2f * (float)random.NextDouble());
            GameObject copy = Object.Instantiate(prefabs[pick], tree, false);
            copy.name = "Generated Tree";
            Transform ct = copy.transform;
                        ct.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
            Vector3 parentScale = tree.lossyScale;
            float s = target / heights[pick];
            ct.localScale = new Vector3(s / Mathf.Max(1e-4f, parentScale.x), s / Mathf.Max(1e-4f, parentScale.y), s / Mathf.Max(1e-4f, parentScale.z));
            // Trunk base on the stand-in's root (the ground).
            ct.position = tree.position - Vector3.up * (bases[pick] * s);
            foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            var renderers = copy.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                bool leaves = r.name.ToLowerInvariant().Contains("lea") || HasMaskedMaterial(r);
                r.shadowCastingMode = quest && leaves ? ShadowCastingMode.Off : ShadowCastingMode.On;
                r.receiveShadows = !quest;
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
            }
            if (quest)
            {
                LODGroup lod = copy.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(0.035f, renderers) });
                lod.RecalculateBounds();
            }
            Replaced++;
        }
        if (Replaced > 0) Debug.Log("[Test Drive] " + Replaced + " street trees replaced with generated trees.");
    }

    private static bool HasMaskedMaterial(Renderer r)
    {
        foreach (Material m in r.sharedMaterials)
            if (m != null && (m.IsKeywordEnabled("_ALPHATEST_ON") || m.name.ToLowerInvariant().Contains("lea"))) return true;
        return false;
    }

    private static bool ModelBounds(GameObject model, out Bounds bounds)
    {
        bounds = new Bounds();
        bool found = false;
        Matrix4x4 toRoot = model.transform.worldToLocalMatrix;
        foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            Bounds b = TestDriveWheelRig.CarBox(toRoot * filter.transform.localToWorldMatrix, filter.sharedMesh.bounds);
            if (!found) { bounds = b; found = true; }
            else bounds.Encapsulate(b);
        }
        return found;
    }
}
