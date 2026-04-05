#nullable enable

using System;
using UnityEngine;

namespace VividSoul.Runtime.Interaction
{
    public sealed class DesktopPetBoundsService
    {
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

            if (!TryGetWorldBounds(modelRoot, out var bounds))
            {
                return ClampWorldPosition(camera, desiredWorldPosition, viewportPadding);
            }

            if (!TryGetViewportRect(camera, bounds, out var viewportRect))
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
            if (!TryGetWorldBounds(modelRoot, out var bounds))
            {
                screenRect = default;
                return false;
            }

            if (!TryGetViewportRect(camera, bounds, out var viewportRect))
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

        private static bool TryGetViewportRect(Camera camera, Bounds bounds, out Rect viewportRect)
        {
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

            foreach (var corner in EnumerateCorners(bounds))
            {
                var viewportPoint = camera.WorldToViewportPoint(corner);
                if (viewportPoint.z <= 0f)
                {
                    continue;
                }

                min = Vector2.Min(min, viewportPoint);
                max = Vector2.Max(max, viewportPoint);
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
