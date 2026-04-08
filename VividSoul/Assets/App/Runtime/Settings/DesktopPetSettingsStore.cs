#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VividSoul.Runtime.Avatar;
using VividSoul.Runtime.Content;
using UnityEngine;

namespace VividSoul.Runtime.Settings
{
    public interface IDesktopPetSettingsStore
    {
        DesktopPetSettingsData Load();

        void Save(DesktopPetSettingsData settings);
    }

    public sealed class DesktopPetSettingsStore : IDesktopPetSettingsStore
    {
        private const string FileName = "desktop-pet-settings.json";
        private const string DefaultModelRelativePath = "Defaults/Models/8329754995701333594.vrm";
        private const string DefaultModelDisplayName = "Shiroko";
        private readonly string filePath;

        public DesktopPetSettingsStore(string? baseDirectory = null)
        {
            var directory = string.IsNullOrWhiteSpace(baseDirectory)
                ? Application.persistentDataPath
                : baseDirectory;
            filePath = Path.Combine(directory, FileName);
        }

        public DesktopPetSettingsData Load()
        {
            if (!File.Exists(filePath))
            {
                return CreateDefault();
            }

            var json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return CreateDefault();
            }

            var file = JsonUtility.FromJson<DesktopPetSettingsFile>(json);
            return Normalize(file != null ? file.ToData() : CreateDefault());
        }

        public void Save(DesktopPetSettingsData settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("A valid settings directory is required.");
            }

            Directory.CreateDirectory(directory);
            var json = JsonUtility.ToJson(DesktopPetSettingsFile.FromData(settings), true);
            File.WriteAllText(filePath, json);
        }

        private static DesktopPetSettingsData CreateDefault()
        {
            return new DesktopPetSettingsData(
                SelectedContent: CreateDefaultSelectedContent(),
                CachedModels: CreateDefaultCachedModels(),
                PositionX: 0f,
                PositionY: 0f,
                Scale: 1f,
                RotationY: 0f,
                IsTopMost: true,
                IsClickThrough: false,
                MonitorIndex: 0,
                VoiceVolume: 1f,
                HeadFollowEnabled: true,
                HandFollowEnabled: true,
                CompactWindowEnabled: false,
                HasWindowPosition: false,
                WindowPositionX: 0f,
                WindowPositionY: 0f,
                VrmImportPerformanceMode: VrmImportPerformanceMode.Balanced);
        }

        private static DesktopPetSettingsData Normalize(DesktopPetSettingsData settings)
        {
            var defaultSelectedContent = CreateDefaultSelectedContent();
            var selectedContent = settings.SelectedContent != null
                && !string.IsNullOrWhiteSpace(settings.SelectedContent.Data)
                && File.Exists(settings.SelectedContent.Data)
                ? settings.SelectedContent
                : defaultSelectedContent;
            var cachedModels = NormalizeCachedModels(settings.CachedModels, selectedContent);

            return settings with
            {
                SelectedContent = selectedContent,
                CachedModels = cachedModels,
                IsClickThrough = false,
                CompactWindowEnabled = false,
            };
        }

        private static SelectedContentState? CreateDefaultSelectedContent()
        {
            var defaultModelPath = Path.Combine(Application.streamingAssetsPath, DefaultModelRelativePath);
            if (!File.Exists(defaultModelPath))
            {
                return null;
            }

            return new SelectedContentState(
                SelectedContentSource.BuiltIn,
                ContentType.Model,
                defaultModelPath);
        }

        private static IReadOnlyList<CachedModelState> CreateDefaultCachedModels()
        {
            var selectedContent = CreateDefaultSelectedContent();
            if (selectedContent == null || string.IsNullOrWhiteSpace(selectedContent.Data))
            {
                return System.Array.Empty<CachedModelState>();
            }

            return new[]
            {
                new CachedModelState(DefaultModelDisplayName, selectedContent.Data),
            };
        }

        private static IReadOnlyList<CachedModelState> NormalizeCachedModels(
            IReadOnlyList<CachedModelState> cachedModels,
            SelectedContentState? selectedContent)
        {
            var normalizedModels = cachedModels
                .Where(static model => model != null)
                .Where(static model => !string.IsNullOrWhiteSpace(model.Path))
                .Select(model => new CachedModelState(
                    string.IsNullOrWhiteSpace(model.DisplayName)
                        ? Path.GetFileNameWithoutExtension(model.Path)
                        : model.DisplayName.Trim(),
                    Path.GetFullPath(model.Path)))
                .Where(model => File.Exists(model.Path))
                .GroupBy(model => model.Path, System.StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (selectedContent != null
                && !string.IsNullOrWhiteSpace(selectedContent.Data)
                && File.Exists(selectedContent.Data)
                && normalizedModels.All(model => !string.Equals(model.Path, selectedContent.Data, System.StringComparison.OrdinalIgnoreCase)))
            {
                normalizedModels.Insert(0, new CachedModelState(
                    string.Equals(selectedContent.Data, Path.Combine(Application.streamingAssetsPath, DefaultModelRelativePath), System.StringComparison.OrdinalIgnoreCase)
                        ? DefaultModelDisplayName
                        : Path.GetFileNameWithoutExtension(selectedContent.Data),
                    Path.GetFullPath(selectedContent.Data)));
            }

            if (normalizedModels.Count > 0)
            {
                return normalizedModels;
            }

            return CreateDefaultCachedModels();
        }

        [Serializable]
        private sealed class DesktopPetSettingsFile
        {
            public SelectedContentStateFile? selectedContent;
            public CachedModelStateFile[] cachedModels = System.Array.Empty<CachedModelStateFile>();
            public float positionX;
            public float positionY;
            public float scale = 1f;
            public float rotationY;
            public bool isTopMost = true;
            public bool isClickThrough;
            public int monitorIndex;
            public float voiceVolume = 1f;
            public bool headFollowEnabled = true;
            public bool handFollowEnabled = true;
            public bool compactWindowEnabled;
            public bool hasWindowPosition;
            public float windowPositionX;
            public float windowPositionY;
            public int vrmImportPerformanceMode = (int)VrmImportPerformanceMode.Balanced;

            public DesktopPetSettingsData ToData()
            {
                return new DesktopPetSettingsData(
                    selectedContent != null ? selectedContent.ToData() : null,
                    cachedModels != null ? cachedModels.Select(static model => model.ToData()).ToArray() : System.Array.Empty<CachedModelState>(),
                    positionX,
                    positionY,
                    scale > 0f ? scale : 1f,
                    rotationY,
                    isTopMost,
                    isClickThrough,
                    monitorIndex,
                    voiceVolume > 0f ? voiceVolume : 1f,
                    headFollowEnabled,
                    handFollowEnabled,
                    compactWindowEnabled,
                    hasWindowPosition,
                    windowPositionX,
                    windowPositionY,
                    Enum.IsDefined(typeof(VrmImportPerformanceMode), vrmImportPerformanceMode)
                        ? (VrmImportPerformanceMode)vrmImportPerformanceMode
                        : VrmImportPerformanceMode.Balanced);
            }

            public static DesktopPetSettingsFile FromData(DesktopPetSettingsData data)
            {
                return new DesktopPetSettingsFile
                {
                    selectedContent = data.SelectedContent != null ? SelectedContentStateFile.FromData(data.SelectedContent) : null,
                    cachedModels = data.CachedModels.Select(static model => CachedModelStateFile.FromData(model)).ToArray(),
                    positionX = data.PositionX,
                    positionY = data.PositionY,
                    scale = data.Scale,
                    rotationY = data.RotationY,
                    isTopMost = data.IsTopMost,
                    isClickThrough = data.IsClickThrough,
                    monitorIndex = data.MonitorIndex,
                    voiceVolume = data.VoiceVolume,
                    headFollowEnabled = data.HeadFollowEnabled,
                    handFollowEnabled = data.HandFollowEnabled,
                    compactWindowEnabled = data.CompactWindowEnabled,
                    hasWindowPosition = data.HasWindowPosition,
                    windowPositionX = data.WindowPositionX,
                    windowPositionY = data.WindowPositionY,
                    vrmImportPerformanceMode = (int)data.VrmImportPerformanceMode,
                };
            }
        }

        [Serializable]
        private sealed class SelectedContentStateFile
        {
            public int source;
            public int type;
            public string data = string.Empty;

            public SelectedContentState ToData()
            {
                return new SelectedContentState(
                    (SelectedContentSource)source,
                    (ContentType)type,
                    data ?? string.Empty);
            }

            public static SelectedContentStateFile FromData(SelectedContentState data)
            {
                return new SelectedContentStateFile
                {
                    source = (int)data.Source,
                    type = (int)data.Type,
                    data = data.Data,
                };
            }
        }

        [Serializable]
        private sealed class CachedModelStateFile
        {
            public string displayName = string.Empty;
            public string path = string.Empty;

            public CachedModelState ToData()
            {
                return new CachedModelState(displayName ?? string.Empty, path ?? string.Empty);
            }

            public static CachedModelStateFile FromData(CachedModelState data)
            {
                return new CachedModelStateFile
                {
                    displayName = data.DisplayName,
                    path = data.Path,
                };
            }
        }
    }
}
