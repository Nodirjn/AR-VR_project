#if UNITY_EDITOR
// Aurelle/Realism/Bicycles
// Idempotent editor tool: rebuilds every exterior cyclist's bicycle in place in SampleScene with the procedural city
// bicycle (ExteriorBicycleBuilder, the same one ExteriorEnvironmentBuilder.CreateCyclist makes), without regenerating the
// district, and seats each rider on it again (seat post fitted to the rider's legs). Meshes go to Assets/Models/Bicycle,
// materials to Assets/Materials/Realism/Bicycle.
// Batch: Unity -batchmode -projectPath <project> -executeMethod AurelleBicycles.Batch
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AurelleBicycles
{
    public const string ScenePath = "Assets/Scenes/SampleScene.unity";

    [MenuItem("Aurelle/Realism/Bicycles")]
    public static void RebuildFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[Bicycles] Exit Play Mode before rebuilding the bicycles.");
            return;
        }
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
        string report = Rebuild(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Bicycles] Rebuilt and saved " + scene.path + ".\n" + report);
    }

    /// <summary>Batch entry: opens SampleScene, rebuilds the bicycles, saves scene and assets, exits in batch mode.</summary>
    public static void Batch()
    {
        int code = 0;
        try
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            string report = Rebuild(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Bicycles] Rebuild done.\n" + report);
            string outDir = Environment.GetEnvironmentVariable("AAC_OUT");
            if (!string.IsNullOrEmpty(outDir))
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "bicycles_report.txt"), report);
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    /// <summary>Rebuilds the bicycle of every ExteriorCyclistAgent in an open scene, in hierarchy order (the palette order
    /// the district builder uses), and returns the report. Safe to run repeatedly.</summary>
    public static string Rebuild(Scene scene)
    {
        var report = new StringBuilder();
        int index = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (ExteriorCyclistAgent agent in root.GetComponentsInChildren<ExteriorCyclistAgent>(true))
                report.AppendLine(ExteriorBicycleBuilder.Rebuild(agent, index++));
        if (index == 0) report.AppendLine("no ExteriorCyclistAgent in " + scene.path);
        AssetDatabase.SaveAssets();
        return report.ToString();
    }
}
#endif
