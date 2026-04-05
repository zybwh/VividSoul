#nullable enable

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VividSoul.Editor
{
    public static class VividSoulBuildTools
    {
        private const string BuildRootDirectoryName = "Builds";
        private const string BuildProjectDirectoryName = "VividSoul";
        private const string BuildPlatformDirectoryName = "macOS";
        private const string BuildAppName = "VividSoul.app";
        private const string SceneDirectory = "Assets/App/Scenes";
        private const string ScenePath = SceneDirectory + "/VividSoul.unity";

        [InitializeOnLoadMethod]
        private static void InitializeProject()
        {
            EnsureWindowedPlayerSettings();
            EnsureBootstrapSceneExists();
            EnsureBuildSettings();
        }

        [MenuItem("VividSoul/Open Bootstrap Scene")]
        public static void OpenBootstrapScene()
        {
            EnsureWindowedPlayerSettings();
            EnsureBootstrapSceneExists();
            EnsureBuildSettings();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("VividSoul/Build/macOS")]
        public static void BuildMacOS()
        {
            EnsureWindowedPlayerSettings();
            EnsureBootstrapSceneExists();
            EnsureBuildSettings();
            var buildDirectory = GetBuildDirectory();
            var buildPath = Path.Combine(buildDirectory, BuildAppName);
            Directory.CreateDirectory(buildDirectory);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = buildPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"macOS build failed with result: {report.summary.result}");
            }
        }

        private static void EnsureBootstrapSceneExists()
        {
            if (File.Exists(ScenePath))
            {
                return;
            }

            Directory.CreateDirectory(SceneDirectory);

            var previouslyActiveScene = SceneManager.GetActiveScene();
            var sceneMode = previouslyActiveScene.IsValid() && !string.IsNullOrWhiteSpace(previouslyActiveScene.path)
                ? NewSceneMode.Additive
                : NewSceneMode.Single;
            var bootstrapScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, sceneMode);
            try
            {
                if (!EditorSceneManager.SaveScene(bootstrapScene, ScenePath, true))
                {
                    throw new InvalidOperationException($"Failed to create bootstrap scene at '{ScenePath}'.");
                }
            }
            finally
            {
                if (sceneMode == NewSceneMode.Additive)
                {
                    EditorSceneManager.CloseScene(bootstrapScene, true);
                    if (previouslyActiveScene.IsValid() && !string.IsNullOrWhiteSpace(previouslyActiveScene.path))
                    {
                        EditorSceneManager.OpenScene(previouslyActiveScene.path, OpenSceneMode.Single);
                    }
                }
            }
        }

        private static void EnsureBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 1 && scenes[0].path == ScenePath && scenes[0].enabled)
            {
                return;
            }

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true),
            };
        }

        private static void EnsureWindowedPlayerSettings()
        {
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 800;
            PlayerSettings.runInBackground = true;
            PlayerSettings.allowFullscreenSwitch = false;
        }

        private static string GetBuildDirectory()
        {
            return Path.GetFullPath(
                Path.Combine(
                    Application.dataPath,
                    "..",
                    "..",
                    BuildRootDirectoryName,
                    BuildProjectDirectoryName,
                    BuildPlatformDirectoryName));
        }
    }
}
