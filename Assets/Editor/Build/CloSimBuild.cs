// CloSim Online Multiplayer — headless Windows build entry point.
// Owned by the Build/QA & Integration Harness (additive tooling; touches no gameplay).
//
// Batchmode usage (invoked by Tools/build-windows.ps1 / .sh and by CI):
//
//   "<UnityEditor>" -batchmode -quit -nographics \
//       -projectPath "<repo root>" \
//       -executeMethod CloSimBuild.BuildWindows \
//       -logFile "-" \
//       -buildOutput "Build/Windows/CloSim.exe"    # optional; default shown
//       -development                                # optional; makes a Development build
//
// NOTE: the class is intentionally in the GLOBAL namespace so the -executeMethod
// target is exactly "CloSimBuild.BuildWindows" (no namespace prefix to remember).
// This file lives under Assets/Editor/, so it compiles into the predefined
// Assembly-CSharp-Editor assembly and needs no .asmdef.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Static headless build helpers for CloSim. Safe to call from the Editor menu
/// (CloSim/Build) or from the command line via -executeMethod.
/// </summary>
public static class CloSimBuild
{
    private const string DefaultOutput = "Build/Windows/CloSim.exe";

    /// <summary>
    /// Builds a Windows 64-bit Standalone player. This is the -executeMethod target.
    /// Reads scenes from the project's Build Settings (EditorBuildSettings.scenes).
    /// Honors -buildOutput &lt;path&gt; and -development from the command line.
    /// Exits the Editor with code 0 on success, 1 on failure when in batch mode.
    /// </summary>
    [MenuItem("CloSim/Build/Windows (x64)")]
    public static void BuildWindows()
    {
        try
        {
            string output = GetArg("-buildOutput") ?? DefaultOutput;
            bool development = HasFlag("-development");
            BuildReport report = BuildWindowsTo(output, development);

            BuildSummary summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[CloSimBuild] SUCCESS -> {summary.outputPath} " +
                          $"({summary.totalSize / (1024f * 1024f):F1} MB, {summary.totalTime.TotalSeconds:F1}s)");
                Exit(0);
            }
            else
            {
                Debug.LogError($"[CloSimBuild] FAILED: result={summary.result}, " +
                               $"errors={summary.totalErrors}, warnings={summary.totalWarnings}");
                Exit(1);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CloSimBuild] EXCEPTION: {ex}");
            Exit(1);
        }
    }

    /// <summary>
    /// Core build routine, factored out so it can be reused/tested. Returns the BuildReport.
    /// Creates the output directory if missing and validates that at least one scene exists.
    /// </summary>
    public static BuildReport BuildWindowsTo(string outputPath, bool development)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            outputPath = DefaultOutput;

        string[] scenes = GetEnabledScenes();
        if (scenes.Length == 0)
        {
            throw new InvalidOperationException(
                "[CloSimBuild] No enabled scenes in Build Settings. " +
                "Add scenes under File > Build Settings before building.");
        }

        // Resolve to an absolute path anchored at the project root so CI/relative paths work.
        string absoluteOutput = Path.IsPathRooted(outputPath)
            ? outputPath
            : Path.GetFullPath(Path.Combine(GetProjectRoot(), outputPath));

        string outputDir = Path.GetDirectoryName(absoluteOutput);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = absoluteOutput,
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = development
                ? (BuildOptions.Development | BuildOptions.AllowDebugging)
                : BuildOptions.None,
        };

        Debug.Log($"[CloSimBuild] Building StandaloneWindows64 -> {absoluteOutput} " +
                  $"(development={development}, scenes={scenes.Length})");

        return BuildPipeline.BuildPlayer(options);
    }

    private static string[] GetEnabledScenes()
    {
        var list = new List<string>();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                list.Add(scene.path);
        }
        return list.ToArray();
    }

    private static string GetProjectRoot()
    {
        // Application.dataPath = "<root>/Assets"; the project root is its parent.
        return Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
    }

    private static void Exit(int code)
    {
        // Only force-exit in batch mode; interactive menu builds should not quit the Editor.
        if (Application.isBatchMode)
            EditorApplication.Exit(code);
    }

    private static string GetArg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static bool HasFlag(string name)
    {
        foreach (string arg in Environment.GetCommandLineArgs())
        {
            if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
