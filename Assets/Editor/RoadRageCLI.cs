using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RoadRage.Editor
{
    /// Batchmode entry points, so a build is one command and its result is a number.
    ///
    /// PRODUCTION-GATES requires that a change "survives a fresh -batchmode player build
    /// (not just the editor)" and that it is "verified by a measurement, not a
    /// screenshot". Neither is convenient while building means clicking through the
    /// Editor, so neither was happening.
    ///
    ///   Unity -quit -batchmode -nographics -projectPath . \
    ///         -executeMethod RoadRage.Editor.RoadRageCLI.BuildWindows \
    ///         -logFile build.log
    ///
    /// Exits non-zero when the build fails, so CI and a shell can both branch on it.
    public static class RoadRageCLI
    {
        /// The scene the game actually ships.
        ///
        /// EditorBuildSettings lists exactly one enabled scene,
        /// Assets/ACC_Drift_Lite/Scenes/Demo.unity, and no such path exists in this
        /// project - the pack here is ACC_Lite. Assets/Scenes/Greenwood.unity, the
        /// project's own scene, is not in the list at all. It has gone unnoticed because
        /// RoadRageBootstrap builds the whole game from a RuntimeInitializeOnLoadMethod
        /// hook after scene load, so it comes up whatever scene ships - or none.
        ///
        /// These builds name the scene explicitly rather than trusting that list.
        private const string MainScene = "Assets/Scenes/Greenwood.unity";

        [MenuItem("Road Rage/Build/Windows 64")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Build/Windows/RoadRage.exe");

        [MenuItem("Road Rage/Build/Android")]
        public static void BuildAndroid() => Build(BuildTarget.Android, "Build/Android/RoadRage.apk");

        private static void Build(BuildTarget target, string defaultOutput)
        {
            var output = ArgValue("-outputPath") ?? defaultOutput;
            var directory = Path.GetDirectoryName(Path.GetFullPath(output));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            if (!File.Exists(MainScene))
            {
                Fail($"scene missing: {MainScene}");
                return;
            }

            var options = new BuildPlayerOptions
            {
                scenes = new[] { MainScene },
                locationPathName = output,
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = BuildOptions.None,
            };

            Debug.Log($"RR_BUILD start target={target} scene={MainScene} output={output}");
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            // Size is a gate in its own right: PRODUCTION-GATES sets 300 MB, and the
            // last recorded Windows build was 648 MB.
            var megabytes = summary.totalSize / (1024f * 1024f);
            Debug.Log($"RR_BUILD result={summary.result} size={megabytes:0.0}MB " +
                      $"errors={summary.totalErrors} warnings={summary.totalWarnings} " +
                      $"time={summary.totalTime.TotalSeconds:0}s output={output}");

            if (summary.result != BuildResult.Succeeded)
            {
                Fail($"build {summary.result} with {summary.totalErrors} errors");
                return;
            }
            EditorApplication.Exit(0);
        }

        /// Reports what would actually ship. Run before a build to see whether the scene
        /// list is pointing where you think it is.
        [MenuItem("Road Rage/Build/Report scene list")]
        public static void ReportSceneList()
        {
            var configured = EditorBuildSettings.scenes;
            Debug.Log($"RR_BUILD configured scenes={configured.Length}");
            foreach (var scene in configured)
                Debug.Log($"RR_BUILD   enabled={scene.enabled} exists={File.Exists(scene.path)} {scene.path}");
            Debug.Log($"RR_BUILD CLI builds ship: {MainScene} (exists={File.Exists(MainScene)})");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void Fail(string reason)
        {
            Debug.LogError($"RR_BUILD FAILED: {reason}");
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }

        private static string ArgValue(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, flag);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
