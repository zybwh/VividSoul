#nullable enable

using System;
using Kirurobo;
using UnityEngine;

namespace VividSoul.Runtime.Platform
{
    public sealed class UniWindowWindowService : IWindowService
    {
        private readonly GameObject hostObject;
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
            resolved.hitTestType = UniWindowController.HitTestType.Opacity;
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
            var monitorRect = UniWindowController.GetMonitorRect(monitorIndex);
            if (monitorRect == Rect.zero)
            {
                return;
            }

            var windowPosition = resolved.windowPosition;
            if (monitorRect.Contains(windowPosition))
            {
                return;
            }

            var windowSize = resolved.windowSize;
            resolved.windowPosition = new Vector2(
                monitorRect.center.x - (windowSize.x * 0.5f),
                monitorRect.center.y - (windowSize.y * 0.5f));
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

            resolved.hitTestType = UniWindowController.HitTestType.Opacity;
            resolved.isHitTestEnabled = true;
            if (clickThroughWasEnabled)
            {
                resolved.isClickThrough = false;
                clickThroughWasEnabled = false;
            }
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
