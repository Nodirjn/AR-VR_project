#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AurelleRealism
{
    /// <summary>Places Poly Haven glTF models (imported by glTFast) as unpacked scene objects with URP Lit materials.</summary>
    internal static class InteriorModels
    {
        /// <summary>
        /// Returns parent/name, instantiating and unpacking the model when it is missing. The object is then moved to
        /// <paramref name="worldPosition"/> with the given yaw. <paramref name="keepNodes"/> (optional) keeps only the
        /// glTF nodes whose name passes the filter and re-centres them horizontally (used for the plant variant sets).
        /// </summary>
        public static Transform Place(InteriorContext ctx, Transform parent, string name, string asset, Vector3 worldPosition, float yaw, Func<string, bool> keepNodes = null)
        {
            Transform t = InteriorContext.Child(parent, name);
            if (t == null)
            {
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(InteriorAssets.ModelPath(asset));
                if (source == null)
                {
                    InteriorRealismUpgrade.Log("model not imported: " + asset);
                    return null;
                }
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, ctx.Scene);
                if (PrefabUtility.IsPartOfPrefabInstance(instance))
                    PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = name;
                instance.transform.SetParent(parent, false);
                t = instance.transform;
                if (keepNodes != null)
                {
                    foreach (MeshFilter f in t.GetComponentsInChildren<MeshFilter>(true).ToArray())
                    {
                        if (!keepNodes(f.name)) UnityEngine.Object.DestroyImmediate(f.gameObject);
                    }
                    foreach (MeshFilter f in t.GetComponentsInChildren<MeshFilter>(true))
                    {
                        Vector3 lp = f.transform.localPosition;
                        f.transform.localPosition = new Vector3(0f, lp.y, 0f);
                    }
                }
                ctx.Created++;
            }
            t.position = worldPosition;
            t.rotation = Quaternion.Euler(0f, yaw, 0f);
            t.localScale = Vector3.one;
            return t;
        }

        /// <summary>Assigns materials per glTF material name (the glTFast material keeps the glTF name).</summary>
        public static void Materials(Transform root, Func<string, Material> byGltfName)
        {
            if (root == null) return;
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material[] current = r.sharedMaterials;
                var names = SubmeshNames(r);
                var next = new Material[current.Length];
                for (int i = 0; i < current.Length; i++)
                {
                    string gltfName = i < names.Count ? names[i] : (current[i] != null ? current[i].name : "");
                    Material mapped = byGltfName(gltfName);
                    next[i] = mapped != null ? mapped : current[i];
                }
                r.sharedMaterials = next;
            }
        }

        // The glTF material names of a renderer's submeshes, recovered from the imported model asset (the scene copy
        // may already carry our replacement materials, so the original names come from the model's own renderer).
        private static List<string> SubmeshNames(MeshRenderer r)
        {
            var list = new List<string>();
            MeshFilter f = r.GetComponent<MeshFilter>();
            Mesh mesh = f != null ? f.sharedMesh : null;
            if (mesh == null) return list;
            string path = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(path)) return list;
            foreach (MeshRenderer src in AssetDatabase.LoadAllAssetsAtPath(path).OfType<GameObject>().SelectMany(g => g.GetComponents<MeshRenderer>()))
            {
                MeshFilter sf = src.GetComponent<MeshFilter>();
                if (sf == null || sf.sharedMesh != mesh) continue;
                foreach (Material m in src.sharedMaterials) list.Add(m != null ? m.name : "");
                break;
            }
            return list;
        }

        /// <summary>Copy of a model mesh with every vertex above <paramref name="splitY"/> moved up (lengthens pendant cords).</summary>
        public static Mesh Stretched(Mesh source, string name, float splitY, float extra)
        {
            Mesh copy = UnityEngine.Object.Instantiate(source);
            var v = copy.vertices;
            for (int i = 0; i < v.Length; i++)
                if (v[i].y > splitY) v[i].y += extra;
            copy.vertices = v;
            copy.RecalculateBounds();
            return InteriorMeshes.Save(copy, name);
        }

        public static void SetMeshEverywhere(Transform root, Mesh mesh)
        {
            foreach (MeshFilter f in root.GetComponentsInChildren<MeshFilter>(true)) f.sharedMesh = mesh;
        }
    }
}
#endif
