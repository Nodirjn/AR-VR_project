using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Legacy TextMesh labels render with the built-in "GUI/Text Shader", which uses ZTest Always and shows
/// every sign through walls, buildings and the car. Apply swaps that font material for a depth-tested
/// copy ("Aurelle/Depth Tested Text"); every TextMesh in a loaded scene is swept after it loads.
/// </summary>
public static class DepthTestedText
{
    private const string ShaderResourceName = "AurelleDepthTestedText";
    private const string ShaderName = "Aurelle/Depth Tested Text";
    private const string BuiltinTextShaderName = "GUI/Text Shader";

    private static readonly Dictionary<Font, Material> materialsByFont = new Dictionary<Font, Material>();
    private static Shader shader;
    private static bool shaderResolved;
    private static bool fontCallbackRegistered;

    public static void Apply(TextMesh textMesh)
    {
        // Runtime materials must never be serialized into a scene by an edit-mode caller.
        if (textMesh == null || !Application.isPlaying) return;

        Font font = textMesh.font;
        MeshRenderer renderer = textMesh.GetComponent<MeshRenderer>();
        if (font == null || renderer == null) return;

        // Only the stock font material is replaced; custom label materials are left alone.
        Material current = renderer.sharedMaterial;
        if (current != null && current != font.material && !UsesBuiltinTextShader(current)) return;

        Material material = GetMaterial(font);
        if (material != null) renderer.sharedMaterial = material;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        // Enter Play Mode without a domain reload keeps statics alive between sessions.
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Font.textureRebuilt -= OnFontTextureRebuilt;
        fontCallbackRegistered = false;
        materialsByFont.Clear();
        shader = null;
        shaderResolved = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplyToLoadedScenes()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        for (int i = 0; i < SceneManager.sceneCount; i++)
            ApplyToScene(SceneManager.GetSceneAt(i));
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyToScene(scene);
    }

    private static void ApplyToScene(Scene scene)
    {
        if (!scene.isLoaded || ResolveShader() == null) return;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (TextMesh textMesh in root.GetComponentsInChildren<TextMesh>(true))
                Apply(textMesh);
        }
    }

    private static Material GetMaterial(Font font)
    {
        if (materialsByFont.TryGetValue(font, out Material material) && material != null)
        {
            SyncFontTexture(font, material);
            return material;
        }

        Shader depthTestedShader = ResolveShader();
        if (depthTestedShader == null) return null;

        material = new Material(depthTestedShader) { name = font.name + " (Depth Tested)" };
        if (font.material != null) material.CopyPropertiesFromMaterial(font.material);
        SyncFontTexture(font, material);
        materialsByFont[font] = material;

        if (!fontCallbackRegistered)
        {
            // Dynamic fonts rebuild their glyph atlas when new characters are requested.
            Font.textureRebuilt += OnFontTextureRebuilt;
            fontCallbackRegistered = true;
        }
        return material;
    }

    private static void OnFontTextureRebuilt(Font font)
    {
        if (font != null && materialsByFont.TryGetValue(font, out Material material) && material != null)
            SyncFontTexture(font, material);
    }

    private static void SyncFontTexture(Font font, Material material)
    {
        Texture atlas = font.material != null ? font.material.mainTexture : null;
        if (atlas != null && material.mainTexture != atlas) material.mainTexture = atlas;
    }

    private static Shader ResolveShader()
    {
        if (shaderResolved) return shader;

        shaderResolved = true;
        shader = Resources.Load<Shader>(ShaderResourceName);
        if (shader == null) shader = Shader.Find(ShaderName);
        if (shader != null && !shader.isSupported) shader = null;
        if (shader == null)
            Debug.LogWarning("[Depth Tested Text] Shader '" + ShaderName +
                             "' is unavailable; TextMesh labels keep the built-in text shader.");
        return shader;
    }

    private static bool UsesBuiltinTextShader(Material material)
    {
        return material.shader != null && material.shader.name == BuiltinTextShaderName;
    }
}
