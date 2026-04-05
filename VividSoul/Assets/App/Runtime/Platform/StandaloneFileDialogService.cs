#nullable enable

using System;
using System.IO;
using Kirurobo;
using SFB;
using UnityEngine;

namespace VividSoul.Runtime.Platform
{
    public sealed class StandaloneFileDialogService : IFileDialogService
    {
        private static readonly ExtensionFilter[] ModelFilters =
        {
            new ExtensionFilter("VRM Model", "vrm"),
        };

        private static readonly ExtensionFilter[] AnimationFilters =
        {
            new ExtensionFilter("VRMA Motion", "vrma"),
        };

        private static readonly ExtensionFilter[] BehaviorManifestFilters =
        {
            new ExtensionFilter("Behavior manifest", "json"),
        };

        public string? OpenModelFile(string initialDirectory = "")
        {
            var directory = Directory.Exists(initialDirectory)
                ? initialDirectory
                : string.Empty;
            var paths = OpenFilePanel("Select VRM File", directory, ModelFilters);
            return GetValidatedSinglePath(paths, "请选择 `.vrm` 模型文件。", ".vrm");
        }

        public string? OpenAnimationFile(string initialDirectory = "")
        {
            var directory = Directory.Exists(initialDirectory)
                ? initialDirectory
                : string.Empty;
            var paths = OpenFilePanel("Select VRMA File", directory, AnimationFilters);
            return GetValidatedSinglePath(paths, "请选择 `.vrma` 动作文件。", ".vrma");
        }

        public string? OpenAnimationFolder(string initialDirectory = "")
        {
            var directory = Directory.Exists(initialDirectory)
                ? initialDirectory
                : string.Empty;
            var paths = OpenFolderPanel("Select VRMA Folder", directory);

            if (paths == null || paths.Length == 0 || string.IsNullOrWhiteSpace(paths[0]))
            {
                return null;
            }

            return paths[0];
        }

        public string? OpenBehaviorManifestFile(string initialDirectory = "")
        {
            var directory = Directory.Exists(initialDirectory)
                ? initialDirectory
                : string.Empty;
            var paths = OpenFilePanel(
                "Select Behavior Manifest (behavior.json)",
                directory,
                BehaviorManifestFilters);
            return GetValidatedSinglePath(paths, "请选择 `.json` 行为清单文件。", ".json");
        }

        private static string[] OpenFilePanel(string title, string directory, ExtensionFilter[] filters)
        {
            if (Application.platform == RuntimePlatform.OSXPlayer)
            {
                return OpenMacFilePanel(title, directory);
            }

            return StandaloneFileBrowser.OpenFilePanel(title, directory, filters, false);
        }

        private static string[] OpenFolderPanel(string title, string directory)
        {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            if (Application.platform == RuntimePlatform.OSXPlayer)
            {
                return MacOsNativeFileDialog.OpenFolderPanel(title, directory);
            }
#endif

            return StandaloneFileBrowser.OpenFolderPanel(title, directory, false);
        }

        private static string[] OpenMacFilePanel(string title, string directory)
        {
            Debug.Log($"[StandaloneFileDialog] macOS model/file panel via LibUniWinC title={title} dir={directory}");
            var result = Array.Empty<string>();
            var settings = new FilePanel.Settings
            {
                title = title,
                initialDirectory = directory,
                filters = Array.Empty<FilePanel.Filter>(),
            };
            FilePanel.OpenFilePanel(settings, files => result = files ?? Array.Empty<string>());
            Debug.Log($"[StandaloneFileDialog] macOS model/file panel returned count={result.Length}");
            return result;
        }

        private static string? GetValidatedSinglePath(string[]? paths, string userMessage, params string[] allowedExtensions)
        {
            if (paths == null || paths.Length == 0 || string.IsNullOrWhiteSpace(paths[0]))
            {
                return null;
            }

            var path = paths[0];
            if (HasAllowedExtension(path, allowedExtensions))
            {
                return path;
            }

            throw new UserFacingException(userMessage);
        }

        private static bool HasAllowedExtension(string path, string[] allowedExtensions)
        {
            for (var index = 0; index < allowedExtensions.Length; index++)
            {
                if (path.EndsWith(allowedExtensions[index], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
