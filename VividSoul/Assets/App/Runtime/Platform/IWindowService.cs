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

        void Configure(Camera? camera);

        void SetTopMost(bool enabled);

        void SetClickThrough(bool enabled);

        void MoveToMonitor(int monitorIndex);

        void FitToMonitor(int monitorIndex);

        void EnsureVisible();

        void RequestApplicationFocus();

        T RunWithTopMostDisabled<T>(Func<T> action);
    }
}
