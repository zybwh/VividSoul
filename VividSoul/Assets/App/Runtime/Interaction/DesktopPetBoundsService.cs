#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VividSoul.Runtime.Interaction
{
    public sealed class DesktopPetBoundsService
    {
        private Mesh? bakedMeshScratch;

        public readonly struct ModelScaleLimits
        {
            public ModelScaleLimits(float minScale, float maxScale)
            {
                MinScale = minScale;
                MaxScale = maxScale;
            }

            public float MinScale { get; }

            public float MaxScale { get; }
        }

        public bool ContainsScreenPoint(Camera camera, GameObject modelRoot, Vector2 screenPoint)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            if (modelRoot == null)
            {
                throw new ArgumentNullException(nameof(modelRoot));
            }

            return TryGetScreenRect(camera, modelRoot, out var screenRect)
                && screenRect.Contains(screenPoint);
        }

        public Vector3 ClampWorldPosition(Camera camera, Vector3 desiredWorldPosition, float viewportPadding)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            var screenPosition = camera.WorldToViewportPoint(desiredWorldPosition);
            var padding = Mathf.Clamp01(viewportPadding);
            screenPosition.x = Mathf.Clamp(screenPosition.x, padding, 1f - padding);
            screenPosition.y = Mathf.Clamp(screenPosition.y, padding, 1f - padding);

            if (screenPosition.z <= 0f)
            {
                screenPosition.z = Mathf.Max(camera.nearClipPlane + 0.01f, 0.01f);
            }

            return camera.ViewportToWorldPoint(screenPosition);
        }

        public ModelScaleLimits GetModelScaleLimits(
            Camera camera,
            GameObject modelRoot,
            float currentScale,
            float minScale,
            float maxScale,
            float minViewportWidthRatio,
            float minViewportHeightRatio,
            float maxViewportWidthRatio,
            float maxViewportHeightRatio)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            if (modelRoot == null)
            {
                throw new ArgumentNullException(nameof(modelRoot));
            }

            var sanitizedMinScale = Mathf.Max(0.01f, minScale);
            var sanitizedMaxScale = Mathf.Max(sanitizedMinScale, maxScale);
            if (!TryGetScreenRect(camera, modelRoot, out var screenRect))
            {
                return new ModelScaleLimits(sanitizedMinScale, sanitizedMaxScale);
            }

            var safeScale = Mathf.Max(Mathf.Abs(currentScale), 0.0001f);
            var widthPerScale = screenRect.width / safeScale;
            var heightPerScale = screenRect.height / safeScale;
            if (widthPerScale <= Mathf.Epsilon || heightPerScale <= Mathf.Epsilon)
            {
                return new ModelScaleLimits(sanitizedMinScale, sanitizedMaxScale);
            }

            var minWidthPixels = Screen.width * Mathf.Clamp01(minViewportWidthRatio);
            var minHeightPixels = Screen.height * Mathf.Clamp01(minViewportHeightRatio);
            var maxWidthPixels = Screen.width * Mathf.Clamp01(maxViewportWidthRatio);
            var maxHeightPixels = Screen.height * Mathf.Clamp01(maxViewportHeightRatio);

            var minScaleFromViewport = Mathf.Max(
                minWidthPixels / widthPerScale,
                minHeightPixels / heightPerScale);
            var maxScaleFromViewport = Mathf.Min(
                maxWidthPixels / widthPerScale,
                maxHeightPixels / heightPerScale);

            var resolvedMaxScale = Mathf.Min(sanitizedMaxScale, maxScaleFromViewport);
            if (resolvedMaxScale <= Mathf.Epsilon)
            {
                resolvedMaxScale = sanitizedMinScale;
            }

            var resolvedMinScale = Mathf.Max(sanitizedMinScale, minScaleFromViewport);
            if (resolvedMinScale > resolvedMaxScale)
            {
                resolvedMinScale = resolvedMaxScale;
            }

            return new ModelScaleLimits(resolvedMinScale, resolvedMaxScale);
        }

        public Vector3 ClampModelWorldPosition(Camera camera, GameObject modelRoot, Vector3 desiredWorldPosition, float viewportPadding)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            if (modelRoot == null)
            {
                throw new ArgumentNullException(nameof(modelRoot));
            }

            if (!TryGetViewportRect(camera, modelRoot, out var viewportRect))
            {
                return ClampWorldPosition(camera, desiredWorldPosition, viewportPadding);
            }

            var padding = Mathf.Clamp01(viewportPadding);
            var currentViewportPosition = camera.WorldToViewportPoint(modelRoot.transform.position);
            var desiredViewportPosition = camera.WorldToViewportPoint(desiredWorldPosition);
            var clampedDelta = new Vector2(
                ClampViewportDelta(viewportRect.xMin, viewportRect.xMax, padding, desiredViewportPosition.x - currentViewportPosition.x),
                ClampViewportDelta(viewportRect.yMin, viewportRect.yMax, padding, desiredViewportPosition.y - currentViewportPosition.y));

            return camera.ViewportToWorldPoint(new Vector3(
                currentViewportPosition.x + clampedDelta.x,
                currentViewportPosition.y + clampedDelta.y,
                GetDepth(camera, modelRoot.transform)));
        }

        public Vector3 MoveModelByScreenDelta(Camera camera, GameObject modelRoot, Vector2 screenDelta, float viewportPadding)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            if (modelRoot == null)
            {
                throw new ArgumentNullException(nameof(modelRoot));
            }

            var modelScreenPosition = camera.WorldToScreenPoint(modelRoot.transform.position);
            var depth = GetDepth(camera, modelRoot.transform);
            var targetWorldPosition = camera.ScreenToWorldPoint(new Vector3(
                modelScreenPosition.x + screenDelta.x,
                modelScreenPosition.y + screenDelta.y,
                depth));
            targetWorldPosition.z = modelRoot.transform.position.z;
            return ClampModelWorldPosition(camera, modelRoot, targetWorldPosition, viewportPadding);
        }

        public float GetDepth(Camera camera, Transform modelTransform)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            if (modelTransform == null)
            {
                throw new ArgumentNullException(nameof(modelTransform));
            }

            var depth = camera.WorldToScreenPoint(modelTransform.position).z;
            return depth > 0f ? depth : Mathf.Max(camera.nearClipPlane + 1f, 1f);
        }

        public bool TryGetScreenRect(Camera camera, GameObject modelRoot, out Rect screenRect)
        {
            if (!TryGetViewportRect(camera, modelRoot, out var viewportRect))
            {
                screenRect = default;
                return false;
            }

            screenRect = Rect.MinMaxRect(
                viewportRect.xMin * Screen.width,
                viewportRect.yMin * Screen.height,
                viewportRect.xMax * Screen.width,
                viewportRect.yMax * Screen.height);
            return true;
        }

        private bool TryGetViewportRect(Camera camera, GameObject modelRoot, out Rect viewportRect)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            if (modelRoot == null)
            {
                throw new ArgumentNullException(nameof(modelRoot));
            }

            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            var hasViewportPoint = false;
            var renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled)
                {
                    continue;
                }

                if (!TryGetRendererViewportRect(camera, renderer, out var rendererViewportRect))
                {
                    continue;
                }

                min = Vector2.Min(min, rendererViewportRect.min);
                max = Vector2.Max(max, rendererViewportRect.max);
                hasViewportPoint = true;
            }

            if (!hasViewportPoint)
            {
                if (!TryGetWorldBounds(modelRoot, out var bounds))
                {
                    viewportRect = default;
                    return false;
                }

                return TryGetViewportRect(camera, bounds, out viewportRect);
            }

            viewportRect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return true;
        }

        public string DescribeRightEdgeContributors(Camera camera, GameObject modelRoot, int maxEntries = 3)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            if (modelRoot == null)
            {
                throw new ArgumentNullException(nameof(modelRoot));
            }

            var entries = new List<(string Name, Rect ScreenRect)>();
            var renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled || !TryGetRendererViewportRect(camera, renderer, out var viewportRect))
                {
                    continue;
                }

                entries.Add((
                    renderer.name,
                    Rect.MinMaxRect(
                        viewportRect.xMin * Screen.width,
                        viewportRect.yMin * Screen.height,
                        viewportRect.xMax * Screen.width,
                        viewportRect.yMax * Screen.height)));
            }

            if (entries.Count == 0)
            {
                return "none";
            }

            entries.Sort((left, right) => right.ScreenRect.xMax.CompareTo(left.ScreenRect.xMax));
            var builder = new StringBuilder();
            var count = Mathf.Min(Mathf.Max(maxEntries, 1), entries.Count);
            for (var i = 0; i < count; i++)
            {
                var entry = entries[i];
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(entry.Name);
                builder.Append(':');
                builder.Append(entry.ScreenRect.xMax.ToString("F2"));
                builder.Append('/');
                builder.Append(entry.ScreenRect.width.ToString("F2"));
            }

            return builder.ToString();
        }

        private static bool TryGetViewportRect(Camera camera, Bounds bounds, out Rect viewportRect)
        {
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

            foreach (var corner in EnumerateCorners(bounds))
            {
                TryAccumulateViewportPoint(camera, corner, ref min, ref max);
            }

            if (!float.IsFinite(min.x) || !float.IsFinite(min.y) || !float.IsFinite(max.x) || !float.IsFinite(max.y))
            {
                viewportRect = default;
                return false;
            }

            viewportRect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return true;
        }

        private static float ClampViewportDelta(float currentMin, float currentMax, float padding, float desiredDelta)
        {
            var availableSize = Mathf.Max(0f, 1f - (padding * 2f));
            var currentSize = currentMax - currentMin;
            if (currentSize >= availableSize)
            {
                return (padding + (availableSize * 0.5f)) - ((currentMin + currentMax) * 0.5f);
            }

            var minDelta = padding - currentMin;
            var maxDelta = (1f - padding) - currentMax;
            return Mathf.Clamp(desiredDelta, minDelta, maxDelta);
        }

        private bool TryGetRendererViewportRect(Camera camera, Renderer renderer, out Rect viewportRect)
        {
            if (TryGetRendererLocalBounds(renderer, out var localBounds))
            {
                var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                var localToWorldMatrix = renderer.transform.localToWorldMatrix;
                foreach (var corner in EnumerateCorners(localBounds))
                {
                    var worldCorner = localToWorldMatrix.MultiplyPoint3x4(corner);
                    TryAccumulateViewportPoint(camera, worldCorner, ref min, ref max);
                }

                if (float.IsFinite(min.x) && float.IsFinite(min.y) && float.IsFinite(max.x) && float.IsFinite(max.y))
                {
                    viewportRect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                    return true;
                }
            }

            return TryGetViewportRect(camera, renderer.bounds, out viewportRect);
        }

        private bool TryGetRendererLocalBounds(Renderer renderer, out Bounds localBounds)
        {
            switch (renderer)
            {
                case SkinnedMeshRenderer skinnedMeshRenderer when skinnedMeshRenderer.sharedMesh != null:
                    var bakedMesh = GetOrCreateBakedMeshScratch();
                    bakedMesh.Clear();
                    skinnedMeshRenderer.BakeMesh(bakedMesh);
                    bakedMesh.RecalculateBounds();
                    localBounds = bakedMesh.bounds;
                    return true;
                case MeshRenderer meshRenderer when meshRenderer.TryGetComponent<MeshFilter>(out var meshFilter) && meshFilter.sharedMesh != null:
                    localBounds = meshFilter.sharedMesh.bounds;
                    return true;
                default:
                    localBounds = default;
                    return false;
            }
        }

        private static bool TryAccumulateViewportPoint(
            Camera camera,
            Vector3 worldPoint,
            ref Vector2 min,
            ref Vector2 max)
        {
            var viewportPoint = camera.WorldToViewportPoint(worldPoint);
            if (viewportPoint.z <= 0f
                || !float.IsFinite(viewportPoint.x)
                || !float.IsFinite(viewportPoint.y))
            {
                return false;
            }

            min = Vector2.Min(min, viewportPoint);
            max = Vector2.Max(max, viewportPoint);
            return true;
        }

        private Mesh GetOrCreateBakedMeshScratch()
        {
            if (bakedMeshScratch != null)
            {
                return bakedMeshScratch;
            }

            bakedMeshScratch = new Mesh
            {
                name = nameof(DesktopPetBoundsService),
                hideFlags = HideFlags.HideAndDontSave,
            };
            return bakedMeshScratch;
        }

        public bool TryGetWorldBounds(GameObject modelRoot, out Bounds bounds)
        {
            var renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
            var hasBounds = false;
            bounds = default;

            foreach (var renderer in renderers)
            {
                if (!renderer.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                    continue;
                }

                bounds.Encapsulate(renderer.bounds);
            }

            if (!hasBounds)
            {
                bounds = new Bounds(modelRoot.transform.position, Vector3.one);
            }

            return true;
        }

        private static Vector3[] EnumerateCorners(Bounds bounds)
        {
            var min = bounds.min;
            var max = bounds.max;

            return new[]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z),
            };
        }
    }
}
