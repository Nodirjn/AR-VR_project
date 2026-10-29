#if UNITY_EDITOR
// Meta Quest (Android) build of the Aurelle showroom.
// Menu:  Aurelle > Build > Quest APK              build Builds/Aurelle-Quest.apk (switches the editor to Android first)
//        Aurelle > Build > Check Quest Settings   report the Quest settings and the OpenXR project validation for Android
// Batch: Unity -batchmode -projectPath <project> -buildTarget Android -executeMethod AurelleBuild.BuildQuestApk
//          writes Builds/Aurelle-Quest.apk, Builds/Aurelle-Quest.txt (commit, result, size, time, errors, warnings)
//          and Builds/Aurelle-Quest-check.txt; exit code 0 when the APK was built, 1 otherwise.
//        Unity -batchmode -projectPath <project> -buildTarget Android -executeMethod AurelleBuild.CheckQuestSettings
//          writes Builds/Aurelle-Quest-check.txt without changing anything; exit code 1 when validation reports errors.
// Product name, company, package id, version, graphics API, colour space and the OpenXR features live in the
// project settings. The build itself only applies what every Quest APK needs: IL2CPP, ARM64, ASTC textures and a
// non-development APK (not an App Bundle).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.OpenXR;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;
using Debug = UnityEngine.Debug;

public static class AurelleBuild
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string ApkPath = "Builds/Aurelle-Quest.apk";
    private const string SummaryPath = "Builds/Aurelle-Quest.txt";
    private const string CheckPath = "Builds/Aurelle-Quest-check.txt";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [MenuItem("Aurelle/Build/Quest APK", false, 400)]
    private static void BuildQuestApkMenu()
    {
        bool ok = BuildQuest();
        EditorUtility.DisplayDialog("Aurelle Quest build",
            ok ? "Built " + ApkPath + "\nSummary: " + SummaryPath : "The build failed. See " + SummaryPath + " and the Console.", "OK");
        if (ok) EditorUtility.RevealInFinder(ApkPath);
    }

    [MenuItem("Aurelle/Build/Check Quest Settings", false, 401)]
    private static void CheckQuestSettingsMenu()
    {
        int errors = WriteCheck(false);
        EditorUtility.DisplayDialog("Aurelle Quest settings",
            (errors == 0 ? "No validation errors." : errors + " validation error(s).") + "\nReport: " + CheckPath, "OK");
    }

    // Batch entry point: -buildTarget Android -executeMethod AurelleBuild.BuildQuestApk
    public static void BuildQuestApk()
    {
        bool ok = false;
        try { ok = BuildQuest(); }
        catch (Exception e) { Debug.LogException(e); }
        EditorApplication.Exit(ok ? 0 : 1);
    }

    // Batch entry point: -buildTarget Android -executeMethod AurelleBuild.CheckQuestSettings (changes nothing)
    public static void CheckQuestSettings()
    {
        int errors = 1;
        try { errors = WriteCheck(true); }
        catch (Exception e) { Debug.LogException(e); }
        EditorApplication.Exit(errors == 0 ? 0 : 1);
    }

    // ---------------------------------------------------------------- build

    private static bool BuildQuest()
    {
        var clock = Stopwatch.StartNew();
        var sb = new StringBuilder();
        sb.AppendLine("Aurelle Quest build " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv) + ", Unity " + Application.unityVersion);
        Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            sb.AppendLine("switching the editor from " + EditorUserBuildSettings.activeBuildTarget + " to Android (start Unity with -buildTarget Android to skip this)");
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                return Fail(sb, "could not switch the active build target to Android (is Android Build Support installed?)");
        }

        ApplyQuestBuildSettings();
        sb.AppendLine("commit: " + GitHead());
        sb.AppendLine("settings: " + DescribeBuildSettings());

        if (!HasOpenXRLoader())
            return Fail(sb, "the OpenXR loader is not active for Android (Project Settings > XR Plug-in Management > Android)");
        int validationErrors = WriteCheck(false);
        sb.AppendLine("OpenXR project validation for Android: " + validationErrors + " error(s), details in " + CheckPath);
        if (validationErrors > 0)
            return Fail(sb, "OpenXR project validation for Android reports errors (" + CheckPath + ")");

        // A failed build must not leave the previous APK behind under the same name.
        if (File.Exists(ApkPath)) File.Delete(ApkPath);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = ApkPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        bool ok = summary.result == BuildResult.Succeeded && File.Exists(ApkPath);
        long size = File.Exists(ApkPath) ? new FileInfo(ApkPath).Length : 0;
        sb.AppendLine("result=" + summary.result + " apk=" + Path.GetFullPath(ApkPath));
        sb.AppendLine("size=" + size + " bytes (" + Mb(size) + ")");
        sb.AppendLine("build time=" + summary.totalTime.TotalSeconds.ToString("F0", Inv) + " s, total with checks=" + clock.Elapsed.TotalSeconds.ToString("F0", Inv) + " s");
        sb.AppendLine("errors=" + summary.totalErrors + " warnings=" + summary.totalWarnings);
        foreach (BuildStep step in report.steps)
        {
            sb.AppendLine("step " + step.name + " " + step.duration.TotalSeconds.ToString("F1", Inv) + " s");
            foreach (BuildStepMessage message in step.messages)
            {
                if (message.type == LogType.Error || message.type == LogType.Exception || message.type == LogType.Warning || message.type == LogType.Assert)
                    sb.AppendLine("  [" + message.type + "] " + Plain(message.content));
            }
        }
        WriteSummary(sb);
        return ok;
    }

    // The per-build settings every Quest APK needs. Everything else is stored in the project settings.
    private static void ApplyQuestBuildSettings()
    {
        if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP)
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.development = false;
        EditorUserBuildSettings.connectProfiler = false;
        EditorUserBuildSettings.allowDebugging = false;
    }

    private static string DescribeBuildSettings()
    {
        return PlayerSettings.productName + " / " + PlayerSettings.companyName
            + ", " + PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)
            + " " + PlayerSettings.bundleVersion + " (" + PlayerSettings.Android.bundleVersionCode + ")"
            + ", " + PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)
            + " " + PlayerSettings.Android.targetArchitectures
            + ", minSdk " + (int)PlayerSettings.Android.minSdkVersion + " targetSdk " + (int)PlayerSettings.Android.targetSdkVersion
            + ", " + string.Join("+", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android).Select(a => a.ToString()))
            + (PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android) ? " (auto)" : "")
            + ", " + PlayerSettings.colorSpace
            + ", textures " + EditorUserBuildSettings.androidBuildSubtarget
            + ", development " + EditorUserBuildSettings.development;
    }

    private static bool Fail(StringBuilder sb, string reason)
    {
        sb.AppendLine("FAILED: " + reason);
        Debug.LogError("Aurelle Quest build: " + reason);
        WriteSummary(sb);
        return false;
    }

    private static void WriteSummary(StringBuilder sb)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SummaryPath));
        File.WriteAllText(SummaryPath, sb.ToString());
        Debug.Log("Aurelle Quest build summary (" + SummaryPath + ")\n" + sb);
    }

    // ---------------------------------------------------------------- check

    private static int WriteCheck(bool openScene)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Aurelle Quest settings " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv) + ", Unity " + Application.unityVersion
            + ", active target " + EditorUserBuildSettings.activeBuildTarget);
        sb.AppendLine("player: " + DescribeBuildSettings());
        sb.AppendLine("player: entry " + PlayerSettings.Android.applicationEntry + ", orientation " + PlayerSettings.defaultInterfaceOrientation
            + ", stripping " + PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Android) + ", strip engine code " + PlayerSettings.stripEngineCode
            + ", app bundle " + EditorUserBuildSettings.buildAppBundle);

        XRGeneralSettings general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
        if (general == null || general.Manager == null) sb.AppendLine("xr: no XR settings for Android");
        else sb.AppendLine("xr: init on start " + general.InitManagerOnStart + ", loaders " + string.Join(", ", general.Manager.activeLoaders.Select(l => l == null ? "null" : l.GetType().Name)));

        OpenXRSettings openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        if (openxr == null) sb.AppendLine("openxr: no settings for Android");
        else
        {
            sb.AppendLine("openxr: render mode " + openxr.renderMode + ", depth submission " + openxr.depthSubmissionMode + ", latency " + openxr.latencyOptimization);
            foreach (OpenXRFeature feature in openxr.GetFeatures().Where(f => f != null && f.enabled).OrderBy(f => f.GetType().Name, StringComparer.Ordinal))
                sb.AppendLine("openxr feature on: " + FeatureLabel(feature));
            var quest = openxr.GetFeature<MetaQuestFeature>();
            if (quest != null)
            {
                SerializedProperty devices = new SerializedObject(quest).FindProperty("targetDevices");
                var names = new List<string>();
                for (int i = 0; devices != null && i < devices.arraySize; i++)
                {
                    SerializedProperty d = devices.GetArrayElementAtIndex(i);
                    names.Add(d.FindPropertyRelative("visibleName").stringValue + "=" + (d.FindPropertyRelative("enabled").boolValue ? "on" : "off"));
                }
                sb.AppendLine("openxr meta quest target devices: " + string.Join(", ", names));
            }
        }

        if (openScene) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int errors = 0;
        var issues = new List<OpenXRFeature.ValidationRule>();
        try
        {
            OpenXRProjectValidation.GetCurrentValidationIssues(issues, BuildTargetGroup.Android);
            sb.AppendLine("OpenXR project validation (Android): " + issues.Count(i => i.error) + " error(s), " + issues.Count(i => !i.error) + " warning(s)");
        }
        catch (Exception e)
        {
            // Some Meta Quest rules of OpenXR 1.17 read the selected build target's settings and throw on other targets.
            errors++;
            sb.AppendLine("OpenXR project validation (Android) could not run: " + e.GetType().Name + ": " + e.Message
                + (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android ? " Switch the editor to Android and check again." : ""));
        }
        foreach (OpenXRFeature.ValidationRule issue in issues)
        {
            if (issue.error) errors++;
            sb.AppendLine("  [" + (issue.error ? "error" : "warning") + "] " + Plain(issue.message));
        }
        AppendProjectValidation(sb);

        Directory.CreateDirectory(Path.GetDirectoryName(CheckPath));
        File.WriteAllText(CheckPath, sb.ToString());
        Debug.Log("Aurelle Quest settings (" + CheckPath + ")\n" + sb);
        return errors;
    }

    // The XR Project Validation window (XR Core Utils) also lists the rules of the other XR packages. It has no public
    // query, so it is read by reflection and only reported; the build gate is the OpenXR validation above.
    private static void AppendProjectValidation(StringBuilder sb)
    {
        try
        {
            Type validator = Type.GetType("Unity.XR.CoreUtils.Editor.BuildValidator, Unity.XR.CoreUtils.Editor");
            PropertyInfo rulesProperty = validator?.GetProperty("PlatformRules", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            var all = rulesProperty?.GetValue(null) as IDictionary;
            var rules = all != null && all.Contains(BuildTargetGroup.Android) ? all[BuildTargetGroup.Android] as IEnumerable : null;
            if (rules == null) { sb.AppendLine("XR project validation (Android): not available"); return; }
            var lines = new List<string>();
            foreach (object rule in rules)
            {
                Type t = rule.GetType();
                var enabled = t.GetProperty("IsRuleEnabled")?.GetValue(rule) as Func<bool>;
                var check = t.GetProperty("CheckPredicate")?.GetValue(rule) as Func<bool>;
                bool failed;
                try { failed = (enabled == null || enabled()) && check != null && !check(); }
                catch (Exception e) { lines.Add("  [exception] " + t.GetProperty("Message")?.GetValue(rule) + ": " + e.GetType().Name); continue; }
                if (!failed) continue;
                bool error = (bool)(t.GetProperty("Error")?.GetValue(rule) ?? false);
                lines.Add("  [" + (error ? "error" : "warning") + "] " + t.GetProperty("Category")?.GetValue(rule) + ": " + Plain(t.GetProperty("Message")?.GetValue(rule) as string));
            }
            sb.AppendLine("XR project validation (Android, all XR packages): " + lines.Count + " issue(s)");
            foreach (string line in lines) sb.AppendLine(line);
        }
        catch (Exception e)
        {
            sb.AppendLine("XR project validation (Android): not available (" + e.GetType().Name + ")");
        }
    }

    // ---------------------------------------------------------------- helpers

    // The checked-out commit, read from .git so that no git executable is needed.
    private static string GitHead()
    {
        try
        {
            string head = File.ReadAllText(".git/HEAD").Trim();
            if (!head.StartsWith("ref: ", StringComparison.Ordinal)) return head;
            string name = head.Substring(5);
            string file = Path.Combine(".git", name);
            if (File.Exists(file)) return name + " " + File.ReadAllText(file).Trim();
            foreach (string line in File.ReadAllLines(".git/packed-refs"))
                if (line.EndsWith(" " + name, StringComparison.Ordinal)) return name + " " + line.Substring(0, line.IndexOf(' '));
        }
        catch (Exception) { }
        return "unknown";
    }

    private static bool HasOpenXRLoader()
    {
        XRGeneralSettings general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
        return general != null && general.Manager != null && general.Manager.activeLoaders.Any(l => l is OpenXRLoader);
    }

    private static string FeatureLabel(OpenXRFeature feature)
    {
        var so = new SerializedObject(feature);
        string ui = so.FindProperty("nameUi")?.stringValue;
        string id = so.FindProperty("featureIdInternal")?.stringValue;
        return (string.IsNullOrEmpty(ui) ? feature.name : ui) + " [" + feature.GetType().FullName + (string.IsNullOrEmpty(id) ? "" : ", " + id) + "]";
    }

    private static string Plain(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "").Replace("\n", " ").Trim();
    }

    private static string Mb(long bytes)
    {
        return (bytes / (1024.0 * 1024.0)).ToString("F1", Inv) + " MB";
    }
}
#endif
