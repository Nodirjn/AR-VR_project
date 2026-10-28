#if UNITY_EDITOR
// Aurelle/Realism/Upgrade Showroom Interior
// Idempotent editor pass that replaces the cartoon-style showroom interior with CC0 materials (Poly Haven,
// ambientCG), CC0 Poly Haven furniture, modelled fixtures and backlit display screens.
// Batch: Unity -batchmode -projectPath <project> -executeMethod AurelleRealism.InteriorRealismUpgrade.Batch
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AurelleRealism
{
    public static class InteriorRealismUpgrade
    {
        public const string ScenePath = "Assets/Scenes/SampleScene.unity";
        public const string MaterialFolder = "Assets/Materials/Realism/Interior";
        public const string MeshFolder = "Assets/Materials/Realism/Interior/Meshes";
        public const string TextureFolder = "Assets/Materials/Realism/Interior/Textures";
        public const string PolyHaven = "Assets/ThirdParty/PolyHaven";
        public const string AmbientCG = "Assets/ThirdParty/ambientCG";

        internal const float FloorY = -0.05f;
        internal const float CeilingY = 5.3f;

        private static readonly List<string> log = new List<string>();

        [MenuItem("Aurelle/Realism/Upgrade Showroom Interior")]
        public static void UpgradeFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[Aurelle Realism] Exit Play Mode before upgrading the showroom interior.");
                return;
            }
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            Apply(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Aurelle Realism] Interior upgrade applied to " + scene.path + " (save the scene to keep it).\n" + string.Join("\n", log));
        }

        /// <summary>Batch entry: opens SampleScene, applies the interior upgrade, saves scene and assets, exits in batch mode.</summary>
        public static void Batch()
        {
            int code = 0;
            try
            {
                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Apply(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveOpenScenes();
                AssetDatabase.SaveAssets();
                Debug.Log("[Aurelle Realism] Interior upgrade done.\n" + string.Join("\n", log));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                code = 1;
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        /// <summary>Applies every step to an open scene. Safe to run repeatedly.</summary>
        public static void Apply(Scene scene)
        {
            log.Clear();
            EnsureFolder(MaterialFolder);
            EnsureFolder(MeshFolder);
            EnsureFolder(TextureFolder);
            InteriorAssets.PrepareImports();
            var mats = new InteriorMaterialLibrary();
            var ctx = new InteriorContext(scene, mats);
            InteriorArchitecture.Apply(ctx);
            InteriorPodium.Apply(ctx);
            InteriorReception.Apply(ctx);
            InteriorLounge.Apply(ctx);
            InteriorDisplays.Apply(ctx);
            InteriorCeilingFixtures.Apply(ctx);
            Log("done: " + ctx.Summary());
        }

        internal static void Log(string line)
        {
            log.Add(line);
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }

    /// <summary>Scene access shared by the steps.</summary>
    internal sealed class InteriorContext
    {
        public readonly Scene Scene;
        public readonly InteriorMaterialLibrary Mats;
        public int Created, Restyled, Removed;

        public InteriorContext(Scene scene, InteriorMaterialLibrary mats)
        {
            Scene = scene;
            Mats = mats;
        }

        public string Summary() => "created " + Created + ", restyled " + Restyled + ", removed " + Removed;

        public GameObject Root(string name)
        {
            foreach (GameObject root in Scene.GetRootGameObjects())
                if (root.name == name) return root;
            return null;
        }

        /// <summary>Finds "Root/Child/..." (first match per level, inactive objects included).</summary>
        public Transform Find(string path)
        {
            string[] parts = path.Split('/');
            GameObject root = Root(parts[0]);
            if (root == null) return null;
            Transform t = root.transform;
            for (int i = 1; i < parts.Length && t != null; i++) t = Child(t, parts[i]);
            return t;
        }

        public static Transform Child(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == name) return parent.GetChild(i);
            return null;
        }

        public static List<Transform> Children(Transform parent, string name)
        {
            var list = new List<Transform>();
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == name) list.Add(parent.GetChild(i));
            return list;
        }

        /// <summary>Direct child by name, created (empty, identity local transform) when missing.</summary>
        public Transform Ensure(Transform parent, string name)
        {
            Transform t = Child(parent, name);
            if (t != null) return t;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Created++;
            return go.transform;
        }

        public void RemoveChildren(Transform parent, Func<Transform, bool> predicate)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform c = parent.GetChild(i);
                if (!predicate(c)) continue;
                UnityEngine.Object.DestroyImmediate(c.gameObject);
                Removed++;
            }
        }

        /// <summary>True for the original scene pieces built from Unity's default primitive meshes.</summary>
        public static bool IsBuiltinPrimitive(Transform t)
        {
            MeshFilter f = t.GetComponent<MeshFilter>();
            return f != null && f.sharedMesh != null && AssetDatabase.GetAssetPath(f.sharedMesh) == "Library/unity default resources";
        }

        /// <summary>Unity objects overload ==, so '??' cannot be used on components.</summary>
        public static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            if (c == null) c = go.AddComponent<T>();
            return c;
        }

        /// <summary>Mesh renderer child with the given mesh and materials, placed in world space.</summary>
        public GameObject Part(Transform parent, string name, Mesh mesh, Material[] materials, Vector3 worldPosition, Quaternion worldRotation, bool castShadows = true)
        {
            Transform t = Ensure(parent, name);
            GameObject go = t.gameObject;
            t.position = worldPosition;
            t.rotation = worldRotation;
            t.localScale = Vector3.one;
            var filter = GetOrAdd<MeshFilter>(go);
            var renderer = GetOrAdd<MeshRenderer>(go);
            filter.sharedMesh = mesh;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>Replaces mesh and materials of an existing renderer object, keeping its name, parent and colliders.</summary>
        public void Restyle(Transform t, Mesh mesh, params Material[] materials)
        {
            if (t == null) return;
            if (mesh != null)
            {
                var filter = GetOrAdd<MeshFilter>(t.gameObject);
                filter.sharedMesh = mesh;
            }
            var renderer = GetOrAdd<MeshRenderer>(t.gameObject);
            renderer.sharedMaterials = materials;
            Restyled++;
        }

        /// <summary>Static flags for new scenery: GI contribution and reflection probes only. Never batching-static:
        /// ShowroomCollisionSetup sizes runtime colliders from MeshFilter.sharedMesh, which static batching replaces.</summary>
        public static void MarkStatic(GameObject go, bool lightmapped)
        {
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                var flags = StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic;
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
                var r = t.GetComponent<MeshRenderer>();
                if (r != null) r.receiveGI = lightmapped ? ReceiveGI.Lightmaps : ReceiveGI.LightProbes;
            }
        }

        /// <summary>LOD group that only culls: small decor disappears earlier at the Quest quality level (LOD bias 0.4 vs 2 on PC).</summary>
        public static void CullBelow(GameObject go, float screenHeight)
        {
            var group = GetOrAdd<LODGroup>(go);
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            group.SetLODs(new[] { new LOD(screenHeight, renderers) });
            group.fadeMode = LODFadeMode.None;
            group.RecalculateBounds();
        }
    }
}
#endif
