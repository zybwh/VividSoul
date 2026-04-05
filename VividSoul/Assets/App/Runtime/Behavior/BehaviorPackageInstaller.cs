#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VividSoul.Runtime.Animation;
using VividSoul.Runtime.Content;

namespace VividSoul.Runtime.Behavior
{
    public sealed class BehaviorPackageInstaller
    {
        public async Task<IBehaviorPreset> InstallAsync(
            ContentItem behaviorContent,
            DesktopPetAnimationController animationController,
            CancellationToken cancellationToken = default)
        {
            if (behaviorContent == null)
            {
                throw new ArgumentNullException(nameof(behaviorContent));
            }

            if (animationController == null)
            {
                throw new ArgumentNullException(nameof(animationController));
            }

            if (behaviorContent.Type != ContentType.Behavior)
            {
                throw new InvalidOperationException("Only behavior content packages can be installed as behavior packages.");
            }

            var preset = CreatePreset(behaviorContent.EntryPath);
            await animationController.ApplyBehaviorPresetAsync(preset, cancellationToken);
            return preset;
        }

        public IBehaviorPreset CreatePreset(string behaviorManifestPath)
        {
            if (string.IsNullOrWhiteSpace(behaviorManifestPath))
            {
                throw new ArgumentException("A behavior manifest path is required.", nameof(behaviorManifestPath));
            }

            if (!File.Exists(behaviorManifestPath))
            {
                throw new FileNotFoundException("Behavior manifest was not found.", behaviorManifestPath);
            }

            var rootPath = Path.GetDirectoryName(behaviorManifestPath);
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new InvalidOperationException("Behavior manifest root path could not be resolved.");
            }

            var json = File.ReadAllText(behaviorManifestPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("Behavior manifest is empty.");
            }

            var manifest = JsonUtility.FromJson<BehaviorManifestFile>(json);
            if (manifest == null)
            {
                throw new InvalidOperationException("Behavior manifest could not be parsed.");
            }

            return new BehaviorPreset(
                name: string.IsNullOrWhiteSpace(manifest.name) ? Path.GetFileName(rootPath) : manifest.name,
                rootPath: rootPath,
                movement: ResolveMovementPreset(rootPath, manifest.movement),
                idleAnimationPath: ResolveAnimationPath(rootPath, manifest.idle),
                clickAnimationPath: ResolveAnimationPath(rootPath, manifest.click),
                poseAnimationPath: ResolveAnimationPath(rootPath, manifest.pose),
                actionAnimations: manifest.actions
                    .Where(binding => !string.IsNullOrWhiteSpace(binding.key))
                    .Select(binding => new BehaviorAnimationBinding(
                        binding.key,
                        ResolveAnimationPath(rootPath, binding.animation)))
                    .Where(binding => !string.IsNullOrWhiteSpace(binding.AnimationPath))
                    .ToArray(),
                poseAnimations: manifest.poses
                    .Where(binding => !string.IsNullOrWhiteSpace(binding.key))
                    .Select(binding => new BehaviorAnimationBinding(
                        binding.key,
                        ResolveAnimationPath(rootPath, binding.animation)))
                    .Where(binding => !string.IsNullOrWhiteSpace(binding.AnimationPath))
                    .ToArray(),
                expressions: manifest.expressions
                    .Where(binding => !string.IsNullOrWhiteSpace(binding.key) && !string.IsNullOrWhiteSpace(binding.expression))
                    .Select(binding => new BehaviorExpressionBinding(binding.key, binding.expression))
                    .ToArray());
        }

        private static BehaviorMovementPreset ResolveMovementPreset(string rootPath, BehaviorMovementFile? movement)
        {
            if (movement == null)
            {
                return BehaviorMovementPreset.Default;
            }

            return new BehaviorMovementPreset(
                ParseMovementType(movement.type),
                ResolveAnimationPath(rootPath, movement.start),
                ResolveAnimationPath(rootPath, movement.loop),
                ResolveAnimationPath(rootPath, movement.stop),
                ResolveAnimationPath(rootPath, movement.loopVertical),
                movement.speedMultiplier > 0f ? movement.speedMultiplier : 1f,
                movement.faceVelocity);
        }

        private static BehaviorMovementType ParseMovementType(string movementType)
        {
            if (string.IsNullOrWhiteSpace(movementType))
            {
                return BehaviorMovementType.Walk;
            }

            return movementType.Trim().ToLowerInvariant() switch
            {
                "walk" => BehaviorMovementType.Walk,
                "fly" => BehaviorMovementType.Fly,
                "hop" => BehaviorMovementType.Hop,
                "teleport" => BehaviorMovementType.Teleport,
                _ => BehaviorMovementType.Walk,
            };
        }

        private static string ResolveAnimationPath(string rootPath, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return string.Empty;
            }

            var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
            if (!File.Exists(fullPath))
            {
                return string.Empty;
            }

            if (!string.Equals(Path.GetExtension(fullPath), ".vrma", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return fullPath;
        }

        [Serializable]
        private sealed class BehaviorManifestFile
        {
            public int schemaVersion = 1;
            public string name = string.Empty;
            public string idle = string.Empty;
            public string click = string.Empty;
            public string pose = string.Empty;
            public BehaviorMovementFile movement = new();
            public BehaviorAnimationBindingFile[] actions = Array.Empty<BehaviorAnimationBindingFile>();
            public BehaviorAnimationBindingFile[] poses = Array.Empty<BehaviorAnimationBindingFile>();
            public BehaviorExpressionBindingFile[] expressions = Array.Empty<BehaviorExpressionBindingFile>();
        }

        [Serializable]
        private sealed class BehaviorMovementFile
        {
            public string type = "walk";
            public string start = string.Empty;
            public string loop = string.Empty;
            public string stop = string.Empty;
            public string loopVertical = string.Empty;
            public float speedMultiplier = 1f;
            public bool faceVelocity = true;
        }

        [Serializable]
        private sealed class BehaviorAnimationBindingFile
        {
            public string key = string.Empty;
            public string animation = string.Empty;
        }

        [Serializable]
        private sealed class BehaviorExpressionBindingFile
        {
            public string key = string.Empty;
            public string expression = string.Empty;
        }

        private sealed class BehaviorPreset : IBehaviorPreset
        {
            private readonly IReadOnlyDictionary<string, string> actionAnimations;
            private readonly IReadOnlyDictionary<string, string> expressionMappings;
            private readonly IReadOnlyDictionary<string, string> poseAnimations;

            public BehaviorPreset(
                string name,
                string rootPath,
                BehaviorMovementPreset movement,
                string idleAnimationPath,
                string clickAnimationPath,
                string poseAnimationPath,
                IReadOnlyList<BehaviorAnimationBinding> actionAnimations,
                IReadOnlyList<BehaviorAnimationBinding> poseAnimations,
                IReadOnlyList<BehaviorExpressionBinding> expressions)
            {
                Name = name;
                RootPath = rootPath;
                Movement = movement;
                IdleAnimationPath = idleAnimationPath;
                ClickAnimationPath = clickAnimationPath;
                PoseAnimationPath = poseAnimationPath;
                this.actionAnimations = actionAnimations.ToDictionary(
                    binding => binding.Key,
                    binding => binding.AnimationPath,
                    StringComparer.OrdinalIgnoreCase);
                this.poseAnimations = poseAnimations.ToDictionary(
                    binding => binding.Key,
                    binding => binding.AnimationPath,
                    StringComparer.OrdinalIgnoreCase);
                expressionMappings = expressions.ToDictionary(
                    binding => binding.Key,
                    binding => binding.Expression,
                    StringComparer.OrdinalIgnoreCase);
            }

            public string Name { get; }

            public string RootPath { get; }

            public BehaviorMovementPreset Movement { get; }

            public string IdleAnimationPath { get; }

            public string ClickAnimationPath { get; }

            public string PoseAnimationPath { get; }

            public bool TryGetActionAnimationPath(string key, out string animationPath)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    animationPath = string.Empty;
                    return false;
                }

                return actionAnimations.TryGetValue(key, out animationPath!);
            }

            public bool TryGetPoseAnimationPath(string key, out string animationPath)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    animationPath = string.Empty;
                    return false;
                }

                return poseAnimations.TryGetValue(key, out animationPath!);
            }

            public bool TryGetExpression(string key, out string expression)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    expression = string.Empty;
                    return false;
                }

                return expressionMappings.TryGetValue(key, out expression!);
            }
        }
    }
}
