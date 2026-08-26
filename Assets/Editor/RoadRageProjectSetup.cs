using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RoadRage.Editor
{
    public static class ProjectBuilder
    {
        [MenuItem("Road Rage/Setup Unity Remake")]
        public static void Setup()
        {
            Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory("Assets/Settings");

            var rendererPath = "Assets/Settings/RoadRageRenderer.asset";
            var pipelinePath = "Assets/Settings/RoadRageURP.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.name = "Road Rage Mobile URP";
                pipeline.renderScale = 1f;
                pipeline.shadowDistance = 85f;
                pipeline.msaaSampleCount = 4;
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;

            ConfigureTextures();
            var scene = SceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, "Assets/Scenes/Greenwood.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Greenwood.unity", true) };
            PlayerSettings.productName = "Road Rage Unity Remake";
            PlayerSettings.companyName = "Ramline Games";
			PlayerSettings.bundleVersion = "7.0.0";
			PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.ramline.roadrage");
			PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.ramline.roadrage");
			PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.ramline.roadrage");
			PlayerSettings.Android.bundleVersionCode = 53;
			PlayerSettings.iOS.buildNumber = "3";
			PlayerSettings.iOS.targetOSVersionString = "14.0";
			PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
			PlayerSettings.allowedAutorotateToLandscapeLeft = true;
			PlayerSettings.allowedAutorotateToLandscapeRight = true;
			PlayerSettings.allowedAutorotateToPortrait = false;
			PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            AssetDatabase.SaveAssets();
            Debug.Log("ROAD_RAGE_SETUP_COMPLETE");
        }

        [MenuItem("Road Rage/Build Windows Prototype")]
        [MenuItem("Road Rage/Build Windows (Release, for capture)")]
        public static void BuildWindowsRelease() => BuildWindowsInternal(BuildOptions.None,
            "Builds/WindowsRelease/RoadRageUnity.exe");

        public static void BuildWindows() => BuildWindowsInternal(BuildOptions.Development,
            "Builds/Windows/RoadRageUnity.exe");

        /// Development builds burn Unity's "Development Build" watermark into the bottom
        /// right of every frame. It survives any in-game UI toggle, so store and press
        /// captures must come from a release build.
        private static void BuildWindowsInternal(BuildOptions options, string path)
        {
            Setup();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Greenwood.unity" },
                locationPathName = path,
                target = BuildTarget.StandaloneWindows64,
                options = options
            });
            if (report.summary.result != BuildResult.Succeeded)
				throw new System.Exception($"Road Rage build failed: {report.summary.result}");
            Debug.Log($"ROAD_RAGE_BUILD_COMPLETE bytes={report.summary.totalSize}");
        }

		[MenuItem("Road Rage/Build Android Test APK")]
		public static void BuildAndroidTest()
		{
			Setup();
			Directory.CreateDirectory("Builds/Android");
			const string outputPath = "Builds/Android/RoadRageUnity-Test.apk";
			// Distinct package id: the SHIPPED Godot build (com.ramline.roadrage) is
			// installed on the test phone and is debug-signature-incompatible with this
			// one. Installing over it would require uninstalling the live game and
			// destroying its save data.
			PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,
				"com.ramline.roadrage.unitytest");
			PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
			PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
			PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
			PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
			EditorUserBuildSettings.buildAppBundle = false;
			var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
			{
				scenes = new[] { "Assets/Scenes/Greenwood.unity" },
				locationPathName = outputPath,
				target = BuildTarget.Android,
				options = BuildOptions.Development
			});
			if (report.summary.result != BuildResult.Succeeded)
				throw new System.Exception($"Road Rage Android build failed: {report.summary.result}");
			var apkBytes = new FileInfo(outputPath).Length;
			Debug.Log($"ROAD_RAGE_ANDROID_COMPLETE apkBytes={apkBytes} reportBytes={report.summary.totalSize}");
		}

		[MenuItem("Road Rage/Generate iOS Xcode Project")]
		public static void BuildIOSProject()
		{
			Setup();
			Directory.CreateDirectory("Builds/iOS");
			PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
			var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
			{
				scenes = new[] { "Assets/Scenes/Greenwood.unity" },
				locationPathName = "Builds/iOS/RoadRageUnityXcode",
				target = BuildTarget.iOS,
				options = BuildOptions.Development
			});
			if (report.summary.result != BuildResult.Succeeded)
				throw new System.Exception($"Road Rage iOS project generation failed: {report.summary.result}");
			Debug.Log($"ROAD_RAGE_IOS_PROJECT_COMPLETE bytes={report.summary.totalSize}");
		}

        private static void ConfigureTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Hideout/Textures" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
                importer.maxTextureSize = 1024;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.mipmapEnabled = true;
                if (path.Contains("_normal")) importer.textureType = TextureImporterType.NormalMap;
                else importer.sRGBTexture = !path.Contains("_aorm");
                importer.SaveAndReimport();
            }
        }
    }
}
