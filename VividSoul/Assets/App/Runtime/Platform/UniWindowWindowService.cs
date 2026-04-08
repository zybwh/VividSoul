#nullable enable

using System;
using System.Collections.Generic;
using Kirurobo;
using UnityEngine;

namespace VividSoul.Runtime.Platform
{
    public sealed class UniWindowWindowService : IWindowService
    {
        private readonly GameObject hostObject;
        private readonly Dictionary<int, Vector2> monitorVerticalInsets = new();
        private bool clickThroughWasEnabled;
        private bool clickThroughLocked;
        private UniWindowController? controller;

        public UniWindowWindowService(GameObject hostObject)
        {
            this.hostObject = hostObject != null
                ? hostObject
                : throw new ArgumentNullException(nameof(hostObject));
        }

        public bool IsAvailable => ResolveController() != null;

        public bool IsTopMost => ResolveController().isTopmost;

        public bool IsClickThrough => clickThroughLocked;

        public int MonitorCount => UniWindowController.GetMonitorCount();

        public Rect GetMonitorRect(int monitorIndex) => UniWindowController.GetMonitorRect(monitorIndex);

        public Vector2 CursorPosition => UniWindowController.GetCursorPosition();

        public Vector2 ClientSize => ResolveController().clientSize;

        public Vector2 WindowPosition => ResolveController().windowPosition;

        public Vector2 WindowSize => ResolveController().windowSize;

        public void Configure(Camera? camera)
        {
            var resolved = ResolveController();

            if (camera != null)
            {
                resolved.SetCamera(camera);
            }
            else if (Camera.main != null)
            {
                resolved.SetCamera(Camera.main);
            }

            resolved.autoSwitchCameraBackground = true;
            resolved.forceWindowed = true;
            resolved.isTransparent = true;
            resolved.SetTransparentType(UniWindowController.TransparentType.Alpha);
            resolved.hitTestType = UniWindowController.HitTestType.Raycast;
            resolved.opacityThreshold = 0.1f;
            resolved.allowDropFiles = false;

            ApplyClickThroughMode(resolved);
        }

        public void SetTopMost(bool enabled)
        {
            ResolveController().isTopmost = enabled;
        }

        public void SetClickThrough(bool enabled)
        {
            clickThroughLocked = enabled;
            ApplyClickThroughMode(ResolveController());
        }

        public void MoveToMonitor(int monitorIndex)
        {
            FitToMonitor(monitorIndex);
        }

        public void FitToMonitor(int monitorIndex)
        {
            var resolved = ResolveController();
            var monitorCount = MonitorCount;
            if (monitorCount <= 0)
            {
                return;
            }

            var sanitizedIndex = Mathf.Clamp(monitorIndex, 0, monitorCount - 1);
            resolved.monitorToFit = sanitizedIndex;
            resolved.shouldFitMonitor = true;
            EnsureVisible();
            CaptureVisibleMonitorInsets(sanitizedIndex, resolved);
        }

        public void SetWindowPosition(Vector2 position)
        {
            var resolved = ResolveController();
            if ((resolved.windowPosition - position).sqrMagnitude <= 0.0001f)
            {
                return;
            }

            resolved.shouldFitMonitor = false;
            resolved.isZoomed = false;
            resolved.windowPosition = position;
        }

        public void SetWindowSize(Vector2 size)
        {
            var resolved = ResolveController();
            if ((resolved.windowSize - size).sqrMagnitude <= 0.0001f)
            {
                return;
            }

            resolved.shouldFitMonitor = false;
            resolved.isZoomed = false;
            resolved.windowSize = size;
        }

        public void SetWindowRect(Rect rect)
        {
            var resolved = ResolveController();
            resolved.shouldFitMonitor = false;
            resolved.isZoomed = false;
            resolved.windowSize = rect.size;
            resolved.windowPosition = rect.position;
        }

        public Vector2 ClampWindowPositionToMonitor(Vector2 position, int monitorIndex)
        {
            var resolved = ResolveController();
            var monitorCount = MonitorCount;
            if (monitorCount <= 0)
            {
                return position;
            }

            var sanitizedIndex = Mathf.Clamp(monitorIndex, 0, monitorCount - 1);
            var monitorRect = NormalizeMonitorRect(sanitizedIndex, UniWindowController.GetMonitorRect(sanitizedIndex));
            if (monitorRect == Rect.zero)
            {
                return position;
            }

            var windowSize = resolved.windowSize;
            var maxX = Mathf.Max(monitorRect.xMin, monitorRect.xMax - windowSize.x);
            var minY = monitorRect.yMin;
            var maxYLimit = monitorRect.yMax;
            if (monitorVerticalInsets.TryGetValue(sanitizedIndex, out var verticalInsets))
            {
                minY += verticalInsets.x;
                maxYLimit -= verticalInsets.y;
            }

            var maxY = Mathf.Max(minY, maxYLimit - windowSize.y);
            return new Vector2(
                Mathf.Clamp(position.x, monitorRect.xMin, maxX),
                Mathf.Clamp(position.y, minY, maxY));
        }

        public void RequestApplicationFocus()
        {
            StandaloneApplicationFocus.Request();
        }

        public void EnsureVisible()
        {
            var resolved = ResolveController();
            var monitorCount = MonitorCount;
            if (monitorCount <= 0)
            {
                return;
            }

            var monitorIndex = Mathf.Clamp(resolved.monitorToFit, 0, monitorCount - 1);
            resolved.windowPosition = ClampWindowPositionToMonitor(resolved.windowPosition, monitorIndex);
        }

        public T RunWithTopMostDisabled<T>(Func<T> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            var restoreTopMost = IsTopMost;
            if (restoreTopMost)
            {
                SetTopMost(false);
            }

            try
            {
                return action();
            }
            finally
            {
                if (restoreTopMost)
                {
                    SetTopMost(true);
                }
            }
        }

        private void ApplyClickThroughMode(UniWindowController resolved)
        {
            resolved.isTransparent = true;

            if (clickThroughLocked)
            {
                resolved.isHitTestEnabled = false;
                resolved.isClickThrough = true;
                clickThroughWasEnabled = true;
                return;
            }

            resolved.hitTestType = UniWindowController.HitTestType.Raycast;
            resolved.isHitTestEnabled = true;
            if (clickThroughWasEnabled)
            {
                resolved.isClickThrough = false;
                clickThroughWasEnabled = false;
            }
        }

        private void CaptureVisibleMonitorInsets(int monitorIndex, UniWindowController resolved)
        {
            var monitorRect = NormalizeMonitorRect(monitorIndex, UniWindowController.GetMonitorRect(monitorIndex));
            if (monitorRect == Rect.zero)
            {
                return;
            }

            var fittedRect = new Rect(resolved.windowPosition, resolved.windowSize);
            var bottomInset = Mathf.Max(0f, fittedRect.yMin - monitorRect.yMin);
            var topInset = Mathf.Max(0f, monitorRect.yMax - fittedRect.yMax);
            monitorVerticalInsets[monitorIndex] = new Vector2(bottomInset, topInset);
        }

        private static Rect NormalizeMonitorRect(int monitorIndex, Rect monitorRect)
        {
            if (monitorRect == Rect.zero || monitorIndex != 0)
            {
                return monitorRect;
            }

            return new Rect(0f, 0f, monitorRect.width, monitorRect.height);
        }

        private UniWindowController ResolveController()
        {
            if (controller != null)
            {
                return controller;
            }

            controller = UnityEngine.Object.FindAnyObjectByType<UniWindowController>();
            if (controller != null)
            {
                return controller;
            }

            controller = hostObject.GetComponent<UniWindowController>();
            if (controller != null)
            {
                return controller;
            }

            controller = hostObject.AddComponent<UniWindowController>();
            return controller;
        }
    }
}
