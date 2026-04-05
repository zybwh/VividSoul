#nullable enable

using System.IO;
using SFB;

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
            var paths = StandaloneFileBrowser.OpenFilePanel("Select VRM File", directory, ModelFilters, false);

            if (paths == null || paths.Length == 0 || string.IsNullOrWhiteSpace(paths[0]))
            {
                return null;
            }

            return paths[0];
        }

        public string? OpenAnimationFile(string initialDirectory = "")
        {
            var directory = Directory.Exists(initialDirectory)
                ? initialDirectory
                : string.Empty;
            var paths = StandaloneFileBrowser.OpenFilePanel("Select VRMA File", directory, AnimationFilters, false);

            if (paths == null || paths.Length == 0 || string.IsNullOrWhiteSpace(paths[0]))
            {
                return null;
            }

            return paths[0];
        }

        public string? OpenAnimationFolder(string initialDirectory = "")
        {
            var directory = Directory.Exists(initialDirectory)
                ? initialDirectory
                : string.Empty;
            var paths = StandaloneFileBrowser.OpenFolderPanel("Select VRMA Folder", directory, false);

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
            var paths = StandaloneFileBrowser.OpenFilePanel(
                "Select Behavior Manifest (behavior.json)",
                directory,
                BehaviorManifestFilters,
                false);

            if (paths == null || paths.Length == 0 || string.IsNullOrWhiteSpace(paths[0]))
            {
                return null;
            }

            return paths[0];
        }
    }
}
