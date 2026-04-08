#nullable enable

using System;
using UnityEngine;

namespace VividSoul.Runtime.Platform
{
    public interface IWindowService
    {
        bool IsAvailable { get; }

        bool IsTopMost { get; }

        bool IsClickThrough { get; }

        int MonitorCount { get; }

        Rect GetMonitorRect(int monitorIndex);

        Vector2 CursorPosition { get; }

        Vector2 ClientSize { get; }

        Vector2 WindowPosition { get; }

        Vector2 WindowSize { get; }

        void Configure(Camera? camera);

        void SetTopMost(bool enabled);

        void SetClickThrough(bool enabled);

        void MoveToMonitor(int monitorIndex);

        void FitToMonitor(int monitorIndex);

        void SetWindowPosition(Vector2 position);

        void SetWindowSize(Vector2 size);

        void SetWindowRect(Rect rect);

        Vector2 ClampWindowPositionToMonitor(Vector2 position, int monitorIndex);

        void EnsureVisible();

        void RequestApplicationFocus();

        T RunWithTopMostDisabled<T>(Func<T> action);
    }
}
