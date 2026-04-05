#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VividSoul.Runtime.Content
{
    public sealed class FileSystemContentCatalog
    {
        private const string ManifestFileName = "item.json";

        private static readonly string[] PreviewCandidates =
        {
            "preview.jpg",
            "preview.jpeg",
            "preview.png",
            "thumbnail.jpg",
            "thumbnail.jpeg",
            "thumbnail.png",
            "Prev.jpeg",
            "Prev.jpg",
        };

        private static readonly string[] ThumbnailCandidates =
        {
            "thumbnail.jpg",
            "thumbnail.jpeg",
            "thumbnail.png",
            "preview.jpg",
            "preview.jpeg",
            "preview.png",
            "Prev.jpeg",
            "Prev.jpg",
        };

        private static readonly HashSet<string> ModelExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".vrm",
        };

        private static readonly HashSet<string> AnimationExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".vrma",
        };

        private static readonly HashSet<string> VoiceExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".wav",
            ".ogg",
            ".mp3",
        };

        public IReadOnlyList<ContentItem> Scan(string rootPath, ContentSource source)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new ArgumentException("A root path is required.", nameof(rootPath));
            }

            if (!Directory.Exists(rootPath))
            {
                return Array.Empty<ContentItem>();
            }

            var normalizedRoot = NormalizePath(rootPath);
            var items = new List<ContentItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (TryCreateItem(normalizedRoot, source, out var rootItem))
            {
                items.Add(rootItem);
                seen.Add(rootItem.Id);
            }

            foreach (var directory in Directory.EnumerateDirectories(normalizedRoot, "*", SearchOption.TopDirectoryOnly))
            {
                if (!TryCreateItem(directory, source, out var item))
                {
                    continue;
                }

                if (seen.Add(item.Id))
                {
                    items.Add(item);
                }
            }

            return items;
        }

        public bool TryCreateItem(string directoryPath, ContentSource source, out ContentItem item)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new ArgumentException("A directory path is required.", nameof(directoryPath));
            }

            var normalizedDirectory = NormalizePath(directoryPath);
            if (!Directory.Exists(normalizedDirectory))
            {
                item = default!;
                return false;
            }

            var manifest = TryReadManifest(normalizedDirectory) ?? TryBuildFallbackManifest(normalizedDirectory);
            if (manifest is null)
            {
                item = default!;
                return false;
            }

            var entryPath = TryResolveEntryPath(normalizedDirectory, manifest);
            if (string.IsNullOrEmpty(entryPath))
            {
                item = default!;
                return false;
            }

            var previewPath = TryResolveOptionalPath(normalizedDirectory, manifest.PreviewRelativePath, PreviewCandidates);
            var thumbnailPath = TryResolveOptionalPath(normalizedDirectory, manifest.ThumbnailRelativePath, ThumbnailCandidates);

            item = new ContentItem(
                Id: normalizedDirectory,
                Source: source,
                Type: manifest.Type,
                RootPath: normalizedDirectory,
                EntryPath: entryPath,
                Title: string.IsNullOrWhiteSpace(manifest.Title) ? Path.GetFileName(normalizedDirectory) : manifest.Title,
                Description: manifest.Description ?? string.Empty,
                PreviewPath: previewPath,
                ThumbnailPath: thumbnailPath,
                AgeRating: string.IsNullOrWhiteSpace(manifest.AgeRating) ? "Everyone" : manifest.AgeRating,
                Tags: manifest.Tags);

            return true;
        }

        private static ContentManifest? TryReadManifest(string directoryPath)
        {
            var manifestPath = Path.Combine(directoryPath, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(manifestPath);
                var file = JsonUtility.FromJson<ContentManifestFile>(json);
                if (file is null)
                {
                    return null;
                }

                var type = ParseContentType(file.type);
                if (type == ContentType.Unknown)
                {
                    type = DetectContentType(directoryPath);
                }

                return new ContentManifest(
                    SchemaVersion: file.schemaVersion <= 0 ? 1 : file.schemaVersion,
                    Type: type,
                    Title: file.title ?? string.Empty,
                    Description: file.description ?? string.Empty,
                    EntryRelativePath: file.entry ?? string.Empty,
                    PreviewRelativePath: file.preview ?? string.Empty,
                    ThumbnailRelativePath: file.thumbnail ?? string.Empty,
                    AgeRating: file.ageRating ?? "Everyone",
                    Tags: SanitizeTags(file.tags));
            }
            catch
            {
                return null;
            }
        }

        private static ContentManifest? TryBuildFallbackManifest(string directoryPath)
        {
            var type = DetectContentType(directoryPath);
            if (type == ContentType.Unknown)
            {
                return null;
            }

            return new ContentManifest(
                SchemaVersion: 1,
                Type: type,
                Title: Path.GetFileName(directoryPath),
                Description: string.Empty,
                EntryRelativePath: string.Empty,
                PreviewRelativePath: string.Empty,
                ThumbnailRelativePath: string.Empty,
                AgeRating: "Everyone",
                Tags: Array.Empty<string>());
        }

        private static ContentType DetectContentType(string directoryPath)
        {
            if (TryFindFirstFile(directoryPath, ModelExtensions, out _))
            {
                return ContentType.Model;
            }

            if (TryFindFirstFile(directoryPath, AnimationExtensions, out _))
            {
                return ContentType.Animation;
            }

            if (TryFindFirstFile(directoryPath, VoiceExtensions, out _))
            {
                return ContentType.Voice;
            }

            if (File.Exists(Path.Combine(directoryPath, "behavior.json")))
            {
                return ContentType.Behavior;
            }

            if (File.Exists(Path.Combine(directoryPath, "outfit.prefab")))
            {
                return ContentType.Outfit;
            }

            return ContentType.Unknown;
        }

        private static string TryResolveEntryPath(string directoryPath, ContentManifest manifest)
        {
            if (!string.IsNullOrWhiteSpace(manifest.EntryRelativePath))
            {
                var candidate = NormalizePath(Path.Combine(directoryPath, manifest.EntryRelativePath));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return manifest.Type switch
            {
                ContentType.Model => TryFindFirstFile(directoryPath, ModelExtensions, out var modelPath) ? modelPath : string.Empty,
                ContentType.Animation => TryFindFirstFile(directoryPath, AnimationExtensions, out var animationPath) ? animationPath : string.Empty,
                ContentType.Voice => TryFindFirstFile(directoryPath, VoiceExtensions, out var voicePath) ? voicePath : string.Empty,
                ContentType.Behavior => ResolveIfExists(directoryPath, "behavior.json"),
                ContentType.Outfit => ResolveIfExists(directoryPath, "outfit.prefab"),
                _ => string.Empty,
            };
        }

        private static string TryResolveOptionalPath(string directoryPath, string manifestRelativePath, IReadOnlyList<string> fallbackCandidates)
        {
            if (!string.IsNullOrWhiteSpace(manifestRelativePath))
            {
                var manifestPath = NormalizePath(Path.Combine(directoryPath, manifestRelativePath));
                if (File.Exists(manifestPath))
                {
                    return manifestPath;
                }
            }

            foreach (var candidate in fallbackCandidates)
            {
                var fullPath = NormalizePath(Path.Combine(directoryPath, candidate));
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            return string.Empty;
        }

        private static bool TryFindFirstFile(string directoryPath, ISet<string> extensions, out string filePath)
        {
            foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly))
            {
                if (!extensions.Contains(Path.GetExtension(file)))
                {
                    continue;
                }

                filePath = NormalizePath(file);
                return true;
            }

            filePath = string.Empty;
            return false;
        }

        private static string ResolveIfExists(string directoryPath, string fileName)
        {
            var path = NormalizePath(Path.Combine(directoryPath, fileName));
            return File.Exists(path) ? path : string.Empty;
        }

        private static string NormalizePath(string path)
        {
            return Path.GetFullPath(path).Replace('\\', '/');
        }

        private static ContentType ParseContentType(string rawValue)
        {
            return rawValue.Trim().ToLowerInvariant() switch
            {
                "model" => ContentType.Model,
                "animation" => ContentType.Animation,
                "voice" => ContentType.Voice,
                "behavior" => ContentType.Behavior,
                "outfit" => ContentType.Outfit,
                _ => ContentType.Unknown,
            };
        }

        private static IReadOnlyList<string> SanitizeTags(IEnumerable<string>? tags)
        {
            if (tags is null)
            {
                return Array.Empty<string>();
            }

            return tags
                .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                .Select(static tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
