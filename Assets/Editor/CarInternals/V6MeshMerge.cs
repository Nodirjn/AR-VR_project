#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CarInternals
{
    /// <summary>
    /// The V6 CAD export splits its meshes into up to 653 submeshes that share one material, so its 182 renderers draw
    /// 6,250 submeshes, each a draw call in every pass. This step gives each V6 renderer a merged, readable copy of its
    /// glTF mesh: the same vertex streams byte for byte (layout, positions, normals and any other channel) and one
    /// triangle submesh whose index list is the source's index lists with their base vertices applied, in submesh
    /// order, so every triangle is the same. The renderer keeps the one material it used, in a single slot.
    /// Renderers that share a glTF mesh (identical CAD parts) share its copy. Copies live in Assets/Models/CarInternals/V6
    /// under the glTF mesh's name, and V6_mesh_map.txt there records each copy's source (glb, GUID and local file ID of
    /// the glTF mesh, its name and size).
    /// Idempotent: the copies are always rebuilt from the glTF meshes (a merged mesh is never merged again), written
    /// only when their content differs and then in place, so their GUIDs stay; a material array that a later tool
    /// filled up again is trimmed back to one slot. <see cref="Restore"/> is the way back to the glTF meshes.
    /// </summary>
    public static class V6MeshMerge
    {
        public const string Folder = InternalsContext.MeshFolder + "/V6";
        public const string MapPath = Folder + "/V6_mesh_map.txt";
        private const string MapHeader =
            "# Merged V6 meshes, written by the car internals tool (V6MeshMerge). One line per copy:\n" +
            "# asset\tsource glb\tsource GUID\tsource local file ID\tsource mesh\tsource submeshes\tvertices\ttriangles\n";

        private sealed class Entry
        {
            public string Asset, Glb, Guid, MeshName;
            public long LocalId;
            public int Submeshes, Vertices, Triangles;
            public string Key => Guid + ":" + LocalId;
        }

        /// <summary>Points every V6 renderer at the merged copy of its glTF mesh; returns the report line.</summary>
        public static string Apply(Transform v6)
        {
            InternalsContext.EnsureFolder(Folder);
            Dictionary<string, Entry> byAsset = ReadMap();
            var bySource = new Dictionary<string, Entry>();
            foreach (Entry e in byAsset.Values) bySource[e.Key] = e;
            var sources = new Dictionary<string, Dictionary<long, Mesh>>();
            var builtThisRun = new Dictionary<string, Mesh>();
            var keep = new HashSet<string>();
            var used = new Dictionary<string, Entry>();
            var warnings = new List<string>();
            int renderers = 0, merged = 0, created = 0, updated = 0, unchanged = 0, repointed = 0, trimmed = 0, sourceSubmeshes = 0;

            foreach (MeshRenderer r in v6.GetComponentsInChildren<MeshRenderer>(true).OrderBy(x => PathOf(x.transform, v6), StringComparer.Ordinal))
            {
                renderers++;
                MeshFilter filter = r.GetComponent<MeshFilter>();
                Mesh current = filter != null ? filter.sharedMesh : null;
                if (current == null) { warnings.Add(PathOf(r.transform, v6) + ": no mesh"); continue; }
                string currentPath = AssetDatabase.GetAssetPath(current);
                bool isCopy = IsCopy(currentPath);
                if (isCopy) keep.Add(currentPath);

                // The glTF mesh behind the renderer, and the copy's path.
                Entry entry;
                Mesh source;
                if (isCopy)
                {
                    if (!byAsset.TryGetValue(currentPath, out entry) || (source = LoadSource(entry, sources)) == null)
                    {
                        warnings.Add(PathOf(r.transform, v6) + ": " + currentPath + " has no known glTF source; kept as it is");
                        if (TrimMaterials(r, 1)) trimmed++;
                        continue;
                    }
                }
                else
                {
                    source = current;
                    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long local) || string.IsNullOrEmpty(currentPath))
                    {
                        warnings.Add(PathOf(r.transform, v6) + ": mesh '" + source.name + "' is not an asset; not merged");
                        continue;
                    }
                    string key = guid + ":" + local;
                    if (!bySource.TryGetValue(key, out entry))
                    {
                        entry = new Entry { Asset = FreePath(source.name, byAsset, used), Glb = currentPath, Guid = guid, LocalId = local, MeshName = source.name };
                        bySource[key] = entry;
                    }
                }
                entry.Glb = AssetDatabase.GUIDToAssetPath(entry.Guid);
                entry.MeshName = source.name;
                entry.Submeshes = source.subMeshCount;
                entry.Vertices = source.vertexCount;
                entry.Triangles = Triangles(source);
                if (isCopy) used[entry.Asset] = entry; // the map keeps the source of every copy in use

                // One material in every slot the source draws (a slot past the submeshes would draw the last one again).
                Material material = SingleMaterial(r.sharedMaterials, isCopy ? 1 : source.subMeshCount);
                if (material == null)
                {
                    warnings.Add(PathOf(r.transform, v6) + ": " + r.sharedMaterials.Length + " slots for " + source.subMeshCount + " submeshes are not one material; not merged");
                    continue;
                }
                if (!IsTriangles(source))
                {
                    warnings.Add(PathOf(r.transform, v6) + ": mesh '" + source.name + "' has non-triangle submeshes; not merged");
                    continue;
                }

                if (!builtThisRun.TryGetValue(entry.Key, out Mesh copy))
                {
                    copy = WriteCopy(source, entry.Asset, ref created, ref updated, ref unchanged);
                    builtThisRun[entry.Key] = copy;
                }
                used[entry.Asset] = entry;
                byAsset[entry.Asset] = entry;
                keep.Add(entry.Asset);
                if (filter.sharedMesh != copy) { filter.sharedMesh = copy; repointed++; }
                if (r.sharedMaterials.Length != 1 || r.sharedMaterials[0] != material)
                {
                    r.sharedMaterials = new[] { material };
                    trimmed++;
                }
                merged++;
                sourceSubmeshes += source.subMeshCount;
            }

            WriteMap(used.Values);
            int deleted = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { Folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (keep.Contains(path)) continue;
                AssetDatabase.DeleteAsset(path);
                deleted++;
            }

            var sb = new StringBuilder();
            sb.Append("meshes merged: " + merged + " of " + renderers + " renderers draw one submesh each (" + sourceSubmeshes + " glTF submeshes); copies " +
                      created + " created, " + updated + " updated, " + unchanged + " unchanged, " + deleted + " stale deleted; " +
                      repointed + " mesh filters and " + trimmed + " material arrays changed");
            foreach (string w in warnings)
            {
                sb.Append("\nWARNING V6 mesh " + w);
                Debug.LogWarning("[Car Internals] V6 mesh " + w);
            }
            return sb.ToString();
        }

        /// <summary>The way back: points the V6 renderers at their glTF meshes again, one material slot per submesh.</summary>
        public static string Restore(Transform v6)
        {
            Dictionary<string, Entry> byAsset = ReadMap();
            var sources = new Dictionary<string, Dictionary<long, Mesh>>();
            int restored = 0, missing = 0;
            foreach (MeshRenderer r in v6.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter filter = r.GetComponent<MeshFilter>();
                string path = filter != null && filter.sharedMesh != null ? AssetDatabase.GetAssetPath(filter.sharedMesh) : "";
                if (!IsCopy(path)) continue;
                Mesh source = byAsset.TryGetValue(path, out Entry entry) ? LoadSource(entry, sources) : null;
                if (source == null) { missing++; continue; }
                Material material = r.sharedMaterials.FirstOrDefault(m => m != null);
                filter.sharedMesh = source;
                r.sharedMaterials = Enumerable.Repeat(material, Mathf.Max(1, source.subMeshCount)).ToArray();
                restored++;
            }
            return restored + " V6 renderers back on their glTF meshes" + (missing > 0 ? ", " + missing + " copies without a known source" : "");
        }

        [MenuItem("Aurelle/Car Internals/Restore V6 glTF Meshes")]
        private static void RestoreFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != AurelleCarInternals.ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(AurelleCarInternals.ScenePath, OpenSceneMode.Single);
            }
            GameObject car = scene.GetRootGameObjects().FirstOrDefault(g => g.name == AurelleCarInternals.CarName);
            Transform v6 = car != null ? car.transform.Find(CarLayout.V6Name) : null;
            if (v6 == null) { Debug.LogError("[Car Internals] " + CarLayout.V6Name + " not found."); return; }
            string report = Restore(v6);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Car Internals] " + report + ". The next Build merges them again.");
        }

        // ---- copies

        private static bool IsCopy(string path) => !string.IsNullOrEmpty(path) && path.StartsWith(Folder + "/", StringComparison.Ordinal);

        // Writes the merged copy of a glTF mesh to path: created, overwritten in place when it differs, else untouched.
        private static Mesh WriteCopy(Mesh source, string path, ref int created, ref int updated, ref int unchanged)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                if (File.Exists(path)) AssetDatabase.DeleteAsset(path); // something else under that name
                var fresh = new Mesh();
                Fill(fresh, source, name);
                AssetDatabase.CreateAsset(fresh, path);
                created++;
                return fresh;
            }
            var probe = new Mesh();
            Fill(probe, source, name);
            bool same = SameMesh(existing, probe);
            UnityEngine.Object.DestroyImmediate(probe);
            if (same) { unchanged++; return existing; }
            Fill(existing, source, name);
            EditorUtility.SetDirty(existing);
            updated++;
            return existing;
        }

        private static void Fill(Mesh target, Mesh source, string name)
        {
            const MeshUpdateFlags Quiet = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            using (Mesh.MeshDataArray array = Mesh.AcquireReadOnlyMeshData(source))
            {
                Mesh.MeshData data = array[0];
                uint[] indices = MergedIndices(data, out uint max);
                IndexFormat format = max > ushort.MaxValue ? IndexFormat.UInt32 : data.indexFormat;
                target.Clear();
                target.name = name;
                target.SetVertexBufferParams(source.vertexCount, source.GetVertexAttributes());
                for (int s = 0; s < data.vertexBufferCount; s++)
                {
                    NativeArray<byte> bytes = data.GetVertexData<byte>(s);
                    target.SetVertexBufferData(bytes, 0, 0, bytes.Length, s, Quiet);
                }
                target.SetIndexBufferParams(indices.Length, format);
                if (format == IndexFormat.UInt16)
                {
                    var narrow = new ushort[indices.Length];
                    for (int i = 0; i < indices.Length; i++) narrow[i] = (ushort)indices[i];
                    target.SetIndexBufferData(narrow, 0, 0, narrow.Length, Quiet);
                }
                else target.SetIndexBufferData(indices, 0, 0, indices.Length, Quiet);
                target.subMeshCount = 1;
                target.SetSubMesh(0, new SubMeshDescriptor(0, indices.Length, MeshTopology.Triangles));
                target.bounds = source.bounds; // the glTF bounds, so culling is unchanged
            }
        }

        // The source's index lists with their base vertices applied, in submesh order.
        private static uint[] MergedIndices(Mesh.MeshData data, out uint max)
        {
            int total = 0;
            for (int s = 0; s < data.subMeshCount; s++) total += data.GetSubMesh(s).indexCount;
            var indices = new uint[total];
            bool wide = data.indexFormat == IndexFormat.UInt32;
            NativeArray<ushort> narrow = wide ? default : data.GetIndexData<ushort>();
            NativeArray<uint> full = wide ? data.GetIndexData<uint>() : default;
            int k = 0;
            max = 0;
            for (int s = 0; s < data.subMeshCount; s++)
            {
                SubMeshDescriptor sm = data.GetSubMesh(s);
                for (int i = sm.indexStart; i < sm.indexStart + sm.indexCount; i++)
                {
                    uint v = (uint)((wide ? (long)full[i] : narrow[i]) + sm.baseVertex);
                    indices[k++] = v;
                    if (v > max) max = v;
                }
            }
            return indices;
        }

        private static bool SameMesh(Mesh a, Mesh b)
        {
            if (a.name != b.name || a.vertexCount != b.vertexCount || a.subMeshCount != b.subMeshCount || a.indexFormat != b.indexFormat ||
                !a.isReadable || a.bounds != b.bounds || !a.GetVertexAttributes().SequenceEqual(b.GetVertexAttributes()))
                return false;
            for (int s = 0; s < a.subMeshCount; s++)
            {
                SubMeshDescriptor x = a.GetSubMesh(s), y = b.GetSubMesh(s);
                if (x.indexStart != y.indexStart || x.indexCount != y.indexCount || x.baseVertex != y.baseVertex || x.topology != y.topology) return false;
            }
            using (Mesh.MeshDataArray da = Mesh.AcquireReadOnlyMeshData(a))
            using (Mesh.MeshDataArray db = Mesh.AcquireReadOnlyMeshData(b))
            {
                if (da[0].vertexBufferCount != db[0].vertexBufferCount) return false;
                for (int s = 0; s < da[0].vertexBufferCount; s++)
                    if (!Same(da[0].GetVertexData<byte>(s), db[0].GetVertexData<byte>(s))) return false;
                return Same(da[0].GetIndexData<byte>(), db[0].GetIndexData<byte>());
            }
        }

        private static bool Same(NativeArray<byte> a, NativeArray<byte> b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        private static bool IsTriangles(Mesh mesh)
        {
            for (int s = 0; s < mesh.subMeshCount; s++)
                if (mesh.GetTopology(s) != MeshTopology.Triangles) return false;
            return true;
        }

        private static int Triangles(Mesh mesh)
        {
            long n = 0;
            for (int s = 0; s < mesh.subMeshCount; s++) n += mesh.GetIndexCount(s) / 3;
            return (int)n;
        }

        // The one material of a renderer whose slots (at least `needed` of them) all hold the same material, else null.
        private static Material SingleMaterial(Material[] slots, int needed)
        {
            if (slots.Length < Mathf.Max(1, needed)) return null;
            Material only = slots[0];
            if (only == null) return null;
            foreach (Material m in slots)
                if (m != only) return null;
            return only;
        }

        private static bool TrimMaterials(Renderer r, int needed)
        {
            Material material = SingleMaterial(r.sharedMaterials, needed);
            if (material == null || r.sharedMaterials.Length == 1) return false;
            r.sharedMaterials = new[] { material };
            return true;
        }

        // A free asset path named after the glTF mesh (identical CAD parts share one mesh: 37 for the 182 renderers).
        private static string FreePath(string mesh, Dictionary<string, Entry> byAsset, Dictionary<string, Entry> used)
        {
            var sb = new StringBuilder();
            foreach (char ch in mesh.Trim())
                sb.Append(char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_' || ch == '.' || ch == '(' || ch == ')' ? ch : '_');
            string stem = sb.Length > 0 ? sb.ToString() : "V6 part";
            string path = Folder + "/" + stem + ".asset";
            for (int k = 2; byAsset.ContainsKey(path) || used.ContainsKey(path); k++) path = Folder + "/" + stem + " " + k + ".asset";
            return path;
        }

        private static Mesh LoadSource(Entry entry, Dictionary<string, Dictionary<long, Mesh>> cache)
        {
            string glb = AssetDatabase.GUIDToAssetPath(entry.Guid);
            if (string.IsNullOrEmpty(glb)) return null;
            if (!cache.TryGetValue(glb, out Dictionary<long, Mesh> meshes))
            {
                meshes = new Dictionary<long, Mesh>();
                foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(glb))
                    if (o is Mesh m && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m, out string _, out long local))
                        meshes[local] = m;
                cache[glb] = meshes;
            }
            return meshes.TryGetValue(entry.LocalId, out Mesh mesh) ? mesh : null;
        }

        // ---- map

        private static Dictionary<string, Entry> ReadMap()
        {
            var map = new Dictionary<string, Entry>();
            if (!File.Exists(MapPath)) return map;
            foreach (string line in File.ReadAllLines(MapPath))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                string[] f = line.Split('\t');
                if (f.Length < 8 || !long.TryParse(f[3], out long local)) continue;
                int.TryParse(f[5], out int sub);
                int.TryParse(f[6], out int verts);
                int.TryParse(f[7], out int tris);
                map[f[0]] = new Entry { Asset = f[0], Glb = f[1], Guid = f[2], LocalId = local, MeshName = f[4], Submeshes = sub, Vertices = verts, Triangles = tris };
            }
            return map;
        }

        private static void WriteMap(IEnumerable<Entry> entries)
        {
            var sb = new StringBuilder(MapHeader);
            foreach (Entry e in entries.OrderBy(x => x.Asset, StringComparer.Ordinal))
                sb.Append(e.Asset).Append('\t').Append(e.Glb).Append('\t').Append(e.Guid).Append('\t').Append(e.LocalId).Append('\t').Append(e.MeshName).Append('\t')
                  .Append(e.Submeshes).Append('\t').Append(e.Vertices).Append('\t').Append(e.Triangles).Append('\n');
            string text = sb.ToString();
            if (File.Exists(MapPath) && File.ReadAllText(MapPath) == text) return;
            File.WriteAllText(MapPath, text);
            AssetDatabase.ImportAsset(MapPath);
        }

        private static string PathOf(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (; t != null && t != root; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
#endif
