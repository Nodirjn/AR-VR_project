#if UNITY_EDITOR
// Optional, batch only: re-renders the three rear-wall display images (Interior_Poster_1..3.jpg) from a studio set-up
// around the scene's CarConcept. Not part of the upgrade pass; run it only when the hero car's look changes:
//   Unity -batchmode -projectPath <project> -executeMethod AurelleRealism.InteriorPosterRenderer.Batch
// The scene is opened, modified in memory (other roots hidden, a studio added) and never saved.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AurelleRealism
{
    public static class InteriorPosterRenderer
    {
        private const int Width = 2048, Height = 996;   // aspect of the 3.62 x 1.76 m screens
        private const string ProbePath = InteriorRealismUpgrade.TextureFolder + "/_PosterStudioProbe.exr";

        private struct Shot
        {
            public string Name;
            public Color Paint;
            public float Azimuth;   // camera bearing around the car, 0 = in front (car front is local +z)
            public float Height;    // camera height above the floor
            public float Fill;      // fraction of the frame width covered by the car
            public float ShiftX;    // lens shift; keeps the car in the left half (the TextMesh lines sit on the right)
            public float KeyAzimuth;
            public float Exposure;
        }

        public static void Batch()
        {
            int code = 0;
            try { Render(); }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        private static void Render()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("InteriorPosterRenderer modifies the open scene in memory; run it in batch mode only.");
            var scene = EditorSceneManager.OpenScene(InteriorRealismUpgrade.ScenePath, OpenSceneMode.Single);
            GameObject car = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "CarConcept") car = root;
                else root.SetActive(false);
            }
            if (car == null) throw new InvalidOperationException("CarConcept not found");
            foreach (Renderer r in car.GetComponentsInChildren<Renderer>(true))
                for (Transform t = r.transform; t != null; t = t.parent)
                    if (t.name.StartsWith("V6_Engine", StringComparison.Ordinal)) { r.enabled = false; break; }

            Bounds cb = default; bool first = true;
            foreach (Renderer r in car.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                if (first) { cb = r.bounds; first = false; } else cb.Encapsulate(r.bounds);
            }
            float floorY = float.MaxValue;   // lowest tyre vertex
            foreach (MeshFilter mf in car.GetComponentsInChildren<MeshFilter>())
            {
                var rr = mf.GetComponent<Renderer>();
                if (rr == null || !rr.enabled || mf.sharedMesh == null || !mf.name.StartsWith("Node-", StringComparison.Ordinal)) continue;
                foreach (Vector3 v in mf.sharedMesh.vertices) floorY = Mathf.Min(floorY, mf.transform.TransformPoint(v).y);
            }
            if (floorY == float.MaxValue) floorY = cb.min.y;
            Vector3 centre = new Vector3(cb.center.x, floorY, cb.center.z);

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Material Mat(Color c, float smooth, Color? emission = null, bool transparent = false)
            {
                var m = new Material(lit);
                m.SetColor("_BaseColor", c);
                m.SetFloat("_Smoothness", smooth);
                if (emission.HasValue)
                {
                    m.SetColor("_EmissionColor", emission.Value);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                if (transparent)
                {
                    m.SetFloat("_Surface", 1f);
                    m.SetFloat("_Blend", 0f);
                    m.SetFloat("_SpecularHighlights", 0f);
                    m.SetFloat("_EnvironmentReflections", 0f);
                }
                BaseShaderGUI.SetMaterialKeywords(m, LitGUI.SetMaterialKeywords);
                if (emission.HasValue) m.EnableKeyword("_EMISSION");
                return m;
            }
            var studio = new GameObject("Poster Studio");
            GameObject Quad(string name, Vector3 pos, Quaternion rot, Vector2 size, Material m, bool softbox)
            {
                // Unity's quad faces its local -Z
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = name;
                UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
                go.transform.SetPositionAndRotation(pos, rot);
                go.transform.localScale = new Vector3(size.x, size.y, 1f);
                go.transform.SetParent(studio.transform, true);
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = m;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ReflectionProbeStatic);
                if (softbox) { go.layer = 31; r.shadowCastingMode = ShadowCastingMode.Off; }   // seen by the probe, not by the camera
                return go;
            }
            // glossy black floor: a semi-transparent dark plane over a mirrored copy of the car
            Quad("Floor", centre + Vector3.up * 0.0005f, Quaternion.Euler(90f, 0f, 0f), new Vector2(80f, 80f), Mat(new Color(0.012f, 0.012f, 0.014f, 0.86f), 0.9f, null, true), false);
            Quad("Under floor", centre - Vector3.up * 3f, Quaternion.Euler(90f, 0f, 0f), new Vector2(80f, 80f), Mat(Color.black, 0f), false);
            var mirror = new GameObject("Mirror");
            mirror.transform.position = centre;
            var clone = UnityEngine.Object.Instantiate(car);
            clone.transform.SetPositionAndRotation(car.transform.position, car.transform.rotation);
            clone.transform.SetParent(mirror.transform, true);
            mirror.transform.localScale = new Vector3(1f, -1f, 1f);
            foreach (var mb in clone.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(mb);
            Material soft = Mat(Color.black, 0f, new Color(1f, 0.98f, 0.95f) * 9f);
            Material softSide = Mat(Color.black, 0f, new Color(1f, 0.98f, 0.96f) * 5f);
            Material softDim = Mat(Color.black, 0f, new Color(1f, 0.98f, 0.95f) * 1.4f);
            Quad("Softbox top", centre + new Vector3(0f, 4.0f, 0.3f), Quaternion.Euler(-90f, 0f, 0f), new Vector2(3.6f, 8.5f), soft, true);
            Quad("Strip left", centre + new Vector3(-3.8f, 1.25f, 0.3f), Quaternion.Euler(0f, -90f, 0f), new Vector2(8f, 1.1f), softSide, true);
            Quad("Strip right", centre + new Vector3(3.8f, 1.25f, 0.3f), Quaternion.Euler(0f, 90f, 0f), new Vector2(8f, 1.1f), softSide, true);
            Quad("Front panel", centre + new Vector3(0f, 1.6f, 6.5f), Quaternion.identity, new Vector2(6f, 2.6f), softDim, true);
            GameObject camFill = Quad("Camera fill", centre + new Vector3(0f, 1.5f, 7f), Quaternion.identity, new Vector2(7f, 3f), softDim, true);
            GameObject backdrop = Quad("Backdrop", centre + new Vector3(0f, 6f, -22f), Quaternion.identity, new Vector2(60f, 24f), Mat(new Color(0.05f, 0.052f, 0.058f), 0.15f), false);
            backdrop.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            var probe = new GameObject("Studio Probe").AddComponent<ReflectionProbe>();
            probe.transform.position = centre + Vector3.up;
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = new Vector3(60f, 30f, 60f);
            probe.resolution = 256;
            probe.hdr = true;
            probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
            probe.backgroundColor = new Color(0.004f, 0.004f, 0.005f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.035f, 0.036f, 0.04f);
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.006f, 0.006f, 0.008f);
            RenderSettings.fogStartDistance = 14f;
            RenderSettings.fogEndDistance = 34f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;

            Light MakeLight(string name, LightType type, Color c, float intensity, float angle, LightShadows shadows)
            {
                var l = new GameObject(name).AddComponent<Light>();
                l.type = type; l.color = c; l.intensity = intensity; l.spotAngle = angle; l.range = 40f; l.shadows = shadows; l.shadowStrength = 0.85f;
                return l;
            }
            Light key = MakeLight("Key", LightType.Spot, new Color(1f, 0.97f, 0.93f), 220f, 70f, LightShadows.Soft);
            Light rim = MakeLight("Rim", LightType.Spot, new Color(0.88f, 0.92f, 1f), 120f, 55f, LightShadows.None);
            Light glow = MakeLight("Backdrop glow", LightType.Spot, new Color(0.8f, 0.84f, 0.92f), 1300f, 46f, LightShadows.None);
            Light fill = MakeLight("Fill", LightType.Directional, new Color(0.9f, 0.92f, 1f), 0.25f, 30f, LightShadows.None);
            fill.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -3f, -8f));

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(0.35f); bloom.threshold.Override(1.1f); bloom.scatter.Override(0.6f);
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.28f); vig.smoothness.Override(0.5f);
            var ca = profile.Add<ColorAdjustments>(true); ca.contrast.Override(12f);
            var vol = new GameObject("Poster Volume").AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = 100; vol.sharedProfile = profile;

            var cam = new GameObject("Poster Camera").AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.01f, 0.01f, 0.012f);
            cam.allowHDR = true; cam.allowMSAA = true;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 200f;
            cam.cullingMask = ~(1 << 31);
            cam.usePhysicalProperties = true;
            cam.sensorSize = new Vector2(36f, 36f * Height / Width);
            cam.gateFit = Camera.GateFitMode.Horizontal;
            cam.focalLength = 50f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;

            Material paint = car.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null && m.name == "Paint 1 Carmine");
            Color original = paint != null && paint.HasProperty("baseColorFactor") ? paint.GetColor("baseColorFactor") : Color.red;
            var shots = new[]
            {
                new Shot { Name = "1", Paint = original, Azimuth = 36f, Height = 0.8f, Fill = 0.47f, ShiftX = 0.27f, KeyAzimuth = 30f, Exposure = 1.25f },
                new Shot { Name = "2", Paint = new Color(0.86f, 0.87f, 0.89f), Azimuth = 90f, Height = 0.85f, Fill = 0.5f, ShiftX = 0.25f, KeyAzimuth = 60f, Exposure = 1.1f },
                new Shot { Name = "3", Paint = new Color(0.04f, 0.2f, 0.75f), Azimuth = 58f, Height = 0.42f, Fill = 0.48f, ShiftX = 0.27f, KeyAzimuth = 45f, Exposure = 1.35f },
            };
            foreach (Shot s in shots)
            {
                foreach (Renderer r in car.GetComponentsInChildren<Renderer>(true).Concat(clone.GetComponentsInChildren<Renderer>(true)))
                {
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == null || mats[i].name != "Paint 1 Carmine") continue;
                        var mpb = new MaterialPropertyBlock();
                        r.GetPropertyBlock(mpb, i);
                        mpb.SetColor("baseColorFactor", s.Paint);
                        r.SetPropertyBlock(mpb, i);
                    }
                }
                ca.postExposure.Override(s.Exposure);
                Vector3 dir = Quaternion.Euler(0f, s.Azimuth, 0f) * Vector3.forward;
                Vector3 kd = Quaternion.Euler(0f, s.KeyAzimuth, 0f) * Vector3.forward;
                key.transform.position = centre + kd * 4f + Vector3.up * 7f;
                key.transform.rotation = Quaternion.LookRotation(centre + Vector3.up * 0.3f - key.transform.position);
                backdrop.transform.position = centre - dir * 16f + Vector3.up * 8f;
                backdrop.transform.rotation = Quaternion.LookRotation(-dir);
                glow.transform.position = centre - dir * 6f + Vector3.up * 9f;
                glow.transform.rotation = Quaternion.LookRotation(centre - dir * 16f + Vector3.up * 1.5f - glow.transform.position);
                rim.transform.position = centre - dir * 7f + Vector3.up * 3.5f;
                rim.transform.rotation = Quaternion.LookRotation(centre + Vector3.up * 0.8f - rim.transform.position);
                camFill.transform.position = centre + dir * 7.5f + Vector3.up * 1.6f;
                camFill.transform.rotation = Quaternion.LookRotation(dir);
                car.SetActive(false); clone.SetActive(false);
                Lightmapping.BakeReflectionProbe(probe, ProbePath);
                car.SetActive(true); clone.SetActive(true);
                AssetDatabase.ImportAsset(ProbePath, ImportAssetOptions.ForceUpdate);
                var cube = AssetDatabase.LoadAssetAtPath<Texture>(ProbePath);
                if (cube != null) { probe.customBakedTexture = cube; RenderSettings.customReflectionTexture = cube; }

                Vector3 target = new Vector3(cb.center.x, floorY + 0.55f, cb.center.z);
                cam.lensShift = new Vector2(s.ShiftX, 0.06f);
                float d = 12f;
                for (int it = 0; it < 30; it++)
                {
                    cam.transform.position = target + dir * d + Vector3.up * (s.Height - 0.55f);
                    cam.transform.rotation = Quaternion.LookRotation(target - cam.transform.position);
                    float minX = 1f, maxX = 0f;
                    for (int c = 0; c < 8; c++)
                    {
                        Vector3 v = cam.WorldToViewportPoint(new Vector3((c & 1) == 0 ? cb.min.x : cb.max.x, (c & 2) == 0 ? cb.min.y : cb.max.y, (c & 4) == 0 ? cb.min.z : cb.max.z));
                        minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x);
                    }
                    d *= Mathf.Clamp((maxX - minX) / s.Fill, 0.8f, 1.25f);
                }
                var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 4);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(InteriorRealismUpgrade.TextureFolder, "Interior_Poster_" + s.Name + ".jpg"), tex.EncodeToJPG(93));
                RenderTexture.active = null; cam.targetTexture = null; RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(tex);
            }
            AssetDatabase.DeleteAsset(ProbePath);
            AssetDatabase.Refresh();
        }
    }
}
#endif
