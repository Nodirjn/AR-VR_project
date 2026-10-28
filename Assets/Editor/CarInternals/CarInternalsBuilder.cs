#if UNITY_EDITOR
// Aurelle/Car Internals/Build
// Idempotent editor tool that builds the hero car's procedural internals (suspension, steering and the systems added
// by later builders) under CarConcept/Internals, regroups the V6 into functional sub-assemblies and tags every node
// with CarPartInfo for the Car Explorer's structure tree.
// Batch: Unity -batchmode -projectPath <project> -executeMethod AurelleCarInternals.Batch
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CarInternals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class AurelleCarInternals
{
    public const string ScenePath = "Assets/Scenes/SampleScene.unity";
    public const string CarName = "CarConcept";
    public const string RootName = "Internals";

    [MenuItem("Aurelle/Car Internals/Build")]
    public static void BuildFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[Car Internals] Exit Play Mode before building the car internals.");
            return;
        }
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
        string report = Build(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Car Internals] Built and saved " + scene.path + ".\n" + report);
    }

    /// <summary>Batch entry: opens SampleScene, rebuilds the internals, saves scene and assets, exits in batch mode.</summary>
    public static void Batch()
    {
        int code = 0;
        try
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            string report = Build(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Car Internals] Build done.\n" + report);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    /// <summary>Rebuilds everything in an open scene and returns the report. Safe to run repeatedly.</summary>
    public static string Build(Scene scene)
    {
        GameObject carObject = scene.GetRootGameObjects().FirstOrDefault(g => g.name == CarName);
        if (carObject == null) throw new InvalidOperationException(CarName + " not found in " + scene.path);
        Transform car = carObject.transform;
        for (int i = car.childCount - 1; i >= 0; i--)
            if (car.GetChild(i).name == RootName) UnityEngine.Object.DestroyImmediate(car.GetChild(i).gameObject);

        var report = new StringBuilder();
        InternalsContext.EnsureFolder(CarPalette.Folder);
        InternalsContext.EnsureFolder(InternalsContext.MeshFolder);
        using (var probe = new InternalsProbe(car, null))
        {
            CarLayout layout = CarLayout.Measure(car, probe);
            report.AppendLine(layout.Report());
            report.AppendLine("V6: " + V6Regroup.Apply(car, layout));

            var rootObject = new GameObject(RootName);
            Transform root = rootObject.transform;
            root.SetParent(car, false);
            root.SetPositionAndRotation(layout.WorldOrigin, layout.WorldRotation);
            Vector3 s = car.lossyScale;
            root.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);

            var palette = new CarPalette();
            palette.CreateAll();
            var ctx = new InternalsContext(layout, palette, root, probe);
            foreach (var (type, attribute) in Subsystems())
            {
                var builder = (ICarInternalsSubsystem)Activator.CreateInstance(type);
                int before = ctx.PartCount;
                builder.Build(ctx);
                report.AppendLine("subsystem " + type.Name + " (" + attribute.System + "): " + (ctx.PartCount - before) + " parts");
            }
            ctx.SortSystems();
            ctx.SaveMeshes();
            report.Append(ctx.Summary());
            List<string> clashes = probe.Interference(root, ctx.IsAllowedContact, out int checkedParts);
            report.AppendLine("interference check of " + checkedParts + " parts: " + (clashes.Count == 0 ? "clear" : clashes.Count + " pairs"));
            foreach (string line in clashes) report.AppendLine("  CLASH " + line);
        }
        AssetDatabase.SaveAssets();
        string text = report.ToString();
        string outDir = Environment.GetEnvironmentVariable("AAC_OUT");
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "internals_report.txt"), text);
        }
        return text;
    }

    /// <summary>Every builder marked with [CarInternalsSubsystem], in order.</summary>
    public static List<(Type, CarInternalsSubsystemAttribute)> Subsystems()
    {
        var list = new List<(Type, CarInternalsSubsystemAttribute)>();
        foreach (Type type in TypeCache.GetTypesWithAttribute<CarInternalsSubsystemAttribute>())
        {
            if (type.IsAbstract || !typeof(ICarInternalsSubsystem).IsAssignableFrom(type)) continue;
            var attribute = (CarInternalsSubsystemAttribute)type.GetCustomAttributes(typeof(CarInternalsSubsystemAttribute), false)[0];
            list.Add((type, attribute));
        }
        return list.OrderBy(x => x.Item2.Order).ThenBy(x => x.Item1.FullName, StringComparer.Ordinal).ToList();
    }
}

namespace CarInternals
{
    /// <summary>
    /// Marks a subsystem builder. The tool finds every class with this attribute (TypeCache), creates it with its
    /// parameterless constructor and calls Build in ascending Order; a new subsystem is a new file, nothing else.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class CarInternalsSubsystemAttribute : Attribute
    {
        public readonly string System;
        public readonly int Order;

        public CarInternalsSubsystemAttribute(string system, int order = 100)
        {
            System = system;
            Order = order;
        }
    }

    public interface ICarInternalsSubsystem
    {
        void Build(InternalsContext ctx);
    }

    /// <summary>English and Uzbek name and one-line description of a node.</summary>
    public readonly struct Txt
    {
        public readonly string En, Uz, DescEn, DescUz;

        public Txt(string en, string uz, string descEn, string descUz)
        {
            En = en; Uz = uz; DescEn = descEn; DescUz = descUz;
        }
    }

    /// <summary>
    /// What a subsystem builder works with: the measured layout, the palette, node creation with CarPartInfo, and
    /// cached mesh assets. Nodes are posed in car space: systems and groups sit at the Internals origin with no
    /// rotation, parts carry their own position and rotation, so explode directions are car-space directions.
    /// </summary>
    public sealed class InternalsContext
    {
        public const string MeshFolder = "Assets/Models/CarInternals";
        // Words the old explorer reads as body panels; kept out of node names.
        private static readonly string[] ShellWords = { "Window", "Glass", "Windshield", "Door", "Hood", "Bonnet", "Bumper", "Fender", "Roof", "Mirror", "Trunk", "Panel" };

        public readonly CarLayout Layout;
        public readonly CarPalette Mats;
        public readonly Transform Root;
        public readonly InternalsProbe Probe;
        private readonly Dictionary<string, Transform> systems = new Dictionary<string, Transform>();
        private readonly Dictionary<string, (Mesh mesh, string system)> meshes = new Dictionary<string, (Mesh, string)>();
        private readonly HashSet<string> ids = new HashSet<string>();
        private readonly List<(string id, string other)> allowed = new List<(string, string)>();
        private readonly List<string> warnings = new List<string>();
        private readonly List<string> notes = new List<string>();
        private string currentSystem = "Misc";

        public int PartCount { get; private set; }

        public InternalsContext(CarLayout layout, CarPalette mats, Transform root, InternalsProbe probe)
        {
            Layout = layout;
            Mats = mats;
            Root = root;
            Probe = probe;
        }

        /// <summary>Plain grouping object for a system (English name from CarSystems), created on demand.</summary>
        public Transform System(string english)
        {
            currentSystem = english;
            if (systems.TryGetValue(english, out Transform t)) return t;
            if (Array.IndexOf(CarSystems.English, english) < 0) Warn("system '" + english + "' is not in CarSystems.English");
            t = new GameObject(english).transform;
            t.SetParent(Root, false);
            systems[english] = t;
            return t;
        }

        public Transform Assembly(Transform parent, string id, Txt text, string material = "", Vector3? explode = null, float distance = 1f)
            => Group(parent, CarNodeKind.Assembly, id, text, material, explode, distance);

        public Transform SubAssembly(Transform parent, string id, Txt text, string material = "", Vector3? explode = null, float distance = 1f)
            => Group(parent, CarNodeKind.SubAssembly, id, text, material, explode, distance);

        private Transform Group(Transform parent, CarNodeKind kind, string id, Txt text, string material, Vector3? explode, float distance)
        {
            var go = new GameObject(NodeName(text.En));
            go.transform.SetParent(parent, false);
            Tag(go, kind, id, text, material, explode, distance, false);
            return go.transform;
        }

        /// <summary>A part: one mesh renderer posed in car space (position and rotation of its own origin).</summary>
        public Transform Part(Transform parent, string id, Txt text, string material, Mesh mesh, Material[] materials,
            Vector3 position, Quaternion rotation, bool fine = false, Vector3? explode = null, float distance = 1f)
        {
            var go = new GameObject(NodeName(text.En));
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(Root.TransformPoint(position), Root.rotation * rotation);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            if (materials.Length < mesh.subMeshCount)
            {
                var full = new Material[mesh.subMeshCount];
                for (int i = 0; i < full.Length; i++) full[i] = materials[Mathf.Min(i, materials.Length - 1)];
                materials = full;
            }
            r.sharedMaterials = materials.Take(Mathf.Max(1, mesh.subMeshCount)).ToArray();
            r.shadowCastingMode = fine ? ShadowCastingMode.Off : ShadowCastingMode.On;
            r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.BlendProbes;
            r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            Tag(go, CarNodeKind.Part, id, text, material, explode, distance, fine);
            PartCount++;
            return go.transform;
        }

        private void Tag(GameObject go, CarNodeKind kind, string id, Txt text, string material, Vector3? explode, float distance, bool fine)
        {
            if (!ids.Add(id)) Warn("duplicate id " + id);
            var info = go.AddComponent<CarPartInfo>();
            info.kind = kind;
            info.id = id;
            info.nameEnglish = text.En;
            info.nameUzbek = text.Uz;
            info.descriptionEnglish = text.DescEn;
            info.descriptionUzbek = text.DescUz;
            info.system = currentSystem;
            info.material = material ?? "";
            info.explodeDirection = explode.HasValue && explode.Value.sqrMagnitude > 1e-8f ? explode.Value.normalized : Vector3.zero;
            info.explodeDistance = distance;
            info.fineDetail = fine;
        }

        private string NodeName(string english)
        {
            foreach (string w in ShellWords)
                if (english.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) Warn("node name '" + english + "' contains '" + w + "'");
            if (english.StartsWith("Body", StringComparison.Ordinal)) Warn("node name '" + english + "' starts with Body");
            return english;
        }

        /// <summary>
        /// Mesh built once per key and shared by every part that uses it (bolts, left and right copies). Saved as
        /// Assets/Models/CarInternals/&lt;system&gt;/&lt;key&gt;.asset; an existing asset is overwritten in place.
        /// </summary>
        public Mesh Mesh(string key, Action<MeshKit> build, int submeshes = 1)
        {
            if (meshes.TryGetValue(key, out var cached)) return cached.mesh;
            var kit = new MeshKit(submeshes);
            build(kit);
            Mesh mesh = kit.ToMesh(key);
            meshes[key] = (mesh, currentSystem);
            return mesh;
        }

        /// <summary>Contacts by design (a stud in its boss, a shaft through a seal): not reported as clashes.</summary>
        public void AllowContact(string idPrefix, string otherPrefix) => allowed.Add((idPrefix, otherPrefix));

        public bool IsAllowedContact(string id, string other)
        {
            foreach (var (a, b) in allowed)
                if ((id.StartsWith(a, StringComparison.Ordinal) && other.StartsWith(b, StringComparison.Ordinal)) ||
                    (other.StartsWith(a, StringComparison.Ordinal) && id.StartsWith(b, StringComparison.Ordinal)))
                    return true;
            return false;
        }

        /// <summary>A line for the build report (hardpoints, measured values).</summary>
        public void Note(string line) => notes.Add(line);

        public void Warn(string message)
        {
            warnings.Add(message);
            Debug.LogWarning("[Car Internals] " + message);
        }

        /// <summary>Car-space rotation that turns local +Y onto a direction (lathe parts, bolts).</summary>
        public static Quaternion AlongY(Vector3 direction, Vector3? forwardHint = null)
        {
            Vector3 d = direction.normalized;
            Vector3 hint = forwardHint ?? (Mathf.Abs(d.z) < 0.9f ? Vector3.forward : Vector3.right);
            Vector3 f = Vector3.ProjectOnPlane(hint, d);
            if (f.sqrMagnitude < 1e-8f) f = Vector3.ProjectOnPlane(Vector3.right, d);
            return Quaternion.LookRotation(f.normalized, d);
        }

        internal void SortSystems()
        {
            var ordered = systems.OrderBy(kv => CarSystems.Order(kv.Key)).Select(kv => kv.Value).ToList();
            for (int i = 0; i < ordered.Count; i++) ordered[i].SetSiblingIndex(i);
        }

        internal void SaveMeshes()
        {
            var written = new HashSet<string>();
            foreach (var kv in meshes)
            {
                string folder = MeshFolder + "/" + kv.Value.system;
                EnsureFolder(folder);
                string path = folder + "/" + Safe(kv.Key) + ".asset";
                written.Add(path);
                Mesh fresh = kv.Value.mesh;
                Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing == null)
                {
                    AssetDatabase.CreateAsset(fresh, path);
                    continue;
                }
                // Overwrite in place: the asset keeps its GUID, every renderer is pointed at it.
                CopyMesh(fresh, existing);
                EditorUtility.SetDirty(existing);
                foreach (MeshFilter f in Root.GetComponentsInChildren<MeshFilter>(true))
                    if (f.sharedMesh == fresh) f.sharedMesh = existing;
                UnityEngine.Object.DestroyImmediate(fresh);
            }
            // Meshes no builder wrote this time are stale (the V6's merged copies are V6MeshMerge's).
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { MeshFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!written.Contains(path) && !path.StartsWith(V6MeshMerge.Folder + "/", StringComparison.Ordinal)) AssetDatabase.DeleteAsset(path);
            }
        }

        private static void CopyMesh(Mesh from, Mesh to)
        {
            to.Clear();
            to.name = from.name;
            to.indexFormat = from.indexFormat;
            to.vertices = from.vertices;
            to.normals = from.normals;
            to.uv = from.uv;
            to.subMeshCount = from.subMeshCount;
            for (int s = 0; s < from.subMeshCount; s++) to.SetTriangles(from.GetTriangles(s), s, false);
            to.RecalculateBounds();
        }

        public string Summary()
        {
            var sb = new StringBuilder();
            int total = 0, fineTris = 0, fineParts = 0;
            foreach (var kv in systems.OrderBy(k => CarSystems.Order(k.Key)))
            {
                int tris = 0, parts = 0;
                foreach (MeshFilter f in kv.Value.GetComponentsInChildren<MeshFilter>(true))
                {
                    int t = Tris(f.sharedMesh);
                    tris += t;
                    parts++;
                    CarPartInfo info = f.GetComponent<CarPartInfo>();
                    if (info != null && info.fineDetail) { fineTris += t; fineParts++; }
                }
                total += tris;
                sb.AppendLine("system " + kv.Key + ": " + parts + " parts, " + tris + " triangles");
                foreach (Transform assembly in kv.Value)
                {
                    int at = 0, ap = 0;
                    foreach (MeshFilter f in assembly.GetComponentsInChildren<MeshFilter>(true)) { at += Tris(f.sharedMesh); ap++; }
                    sb.AppendLine("  " + assembly.name + ": " + ap + " parts, " + at + " triangles, depth " + Depth(assembly));
                }
            }
            foreach (string n in notes) sb.AppendLine(n);
            sb.AppendLine("internals total: " + PartCount + " parts (" + fineParts + " fine detail), " + total + " triangles (" + fineTris + " fine detail), " + meshes.Count + " meshes");
            foreach (string w in warnings) sb.AppendLine("WARNING " + w);
            return sb.ToString();
        }

        private static int Depth(Transform t)
        {
            int best = 0;
            foreach (Transform c in t) best = Mathf.Max(best, Depth(c));
            return best + 1;
        }

        private static int Tris(Mesh m)
        {
            if (m == null) return 0;
            int n = 0;
            for (int s = 0; s < m.subMeshCount; s++) n += (int)(m.GetIndexCount(s) / 3);
            return n;
        }

        private static string Safe(string key)
        {
            var sb = new StringBuilder();
            foreach (char c in key) sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '.' || c == '_' ? c : '_');
            return sb.ToString();
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
#endif
