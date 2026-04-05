#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UniVRM10;
using UnityEngine;
using UnityEngine.Timeline;
using VividSoul.Runtime.App;
using VividSoul.Runtime.Avatar;
using VividSoul.Runtime.Behavior;

namespace VividSoul.Runtime.Animation
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DesktopPetRuntimeController))]
    public sealed class DesktopPetAnimationController : MonoBehaviour
    {
        [SerializeField, TextArea] private string animationStateSummary = "No animation package installed.";
        [SerializeField] private bool replayIdleOnModelLoad = true;

        private DesktopPetRuntimeController? runtimeController;
        private IAnimationLoader? animationLoader;
        private AnimationPackage? currentAnimationPackage;
        private IBehaviorPreset? currentBehaviorPreset;
        private AnimationPlaybackSession? currentPlaybackSession;
        private CancellationTokenSource? playbackCancellationTokenSource;
        private bool isSubscribedToRuntimeController;
        private bool returningToIdle;

        public string AnimationStateSummary => animationStateSummary;

        public event Action? PlayOncePlaybackCompleting;

        public bool HasActivePlayback => currentPlaybackSession != null;

        public bool HasIdleAnimation => !string.IsNullOrWhiteSpace(ResolveIdleAnimationPath());

        public bool HasClickAnimation => !string.IsNullOrWhiteSpace(ResolveClickAnimationPath());

        public bool HasPoseAnimation => !string.IsNullOrWhiteSpace(ResolvePoseAnimationPath());

        private void Awake()
        {
            runtimeController = GetComponent<DesktopPetRuntimeController>();
            animationLoader = new VrmaAnimationLoaderService();
        }

        private void OnEnable()
        {
            TrySubscribeToRuntimeController();
        }

        private void Start()
        {
            TrySubscribeToRuntimeController();
        }

        private void OnDisable()
        {
            if (runtimeController != null && isSubscribedToRuntimeController)
            {
                runtimeController.ModelLoaded -= HandleModelLoaded;
                runtimeController.ModelCleared -= HandleModelCleared;
                isSubscribedToRuntimeController = false;
            }

            CancelPlaybackLoad();
            ClearCurrentPlaybackSession();
        }

        private void Update()
        {
            if (currentPlaybackSession == null)
            {
                return;
            }

            if (runtimeController == null || runtimeController.CurrentModelRoot == null)
            {
                ClearCurrentPlaybackSession();
                return;
            }

            var nextTime = currentPlaybackSession.Time + Time.deltaTime;
            switch (currentPlaybackSession.Mode)
            {
                case AnimationPlaybackMode.Loop:
                    currentPlaybackSession.Time = Mathf.Repeat(nextTime, currentPlaybackSession.Duration);
                    currentPlaybackSession.TimeControl.SetTime(currentPlaybackSession.Time);
                    return;
                case AnimationPlaybackMode.HoldLastFrame:
                    currentPlaybackSession.Time = Mathf.Min(nextTime, currentPlaybackSession.Duration);
                    currentPlaybackSession.TimeControl.SetTime(currentPlaybackSession.Time);
                    return;
                case AnimationPlaybackMode.ReturnToIdle:
                    currentPlaybackSession.Time = Mathf.Min(nextTime, currentPlaybackSession.Duration);
                    currentPlaybackSession.TimeControl.SetTime(currentPlaybackSession.Time);
                    if (currentPlaybackSession.Time >= currentPlaybackSession.Duration && !returningToIdle)
                    {
                        currentPlaybackSession.Complete();
                        returningToIdle = true;
                        _ = ResumeIdleAfterPlaybackAsync();
                    }
                    return;
                case AnimationPlaybackMode.PlayOnce:
                    currentPlaybackSession.Time = Mathf.Min(nextTime, currentPlaybackSession.Duration);
                    currentPlaybackSession.TimeControl.SetTime(currentPlaybackSession.Time);
                    if (currentPlaybackSession.Time >= currentPlaybackSession.Duration)
                    {
                        currentPlaybackSession.Complete();
                        var completedPath = currentPlaybackSession.Path;
                        PlayOncePlaybackCompleting?.Invoke();
                        ClearCurrentPlaybackSession();
                        UpdateAnimationSummary(completedPath, "pose completed");
                    }
                    return;
                default:
                    throw new InvalidOperationException($"Unsupported playback mode: {currentPlaybackSession.Mode}");
            }
        }

        public async Task<AnimationPackage> ApplyAnimationPackageAsync(
            AnimationPackage animationPackage,
            CancellationToken cancellationToken = default)
        {
            currentAnimationPackage = animationPackage ?? throw new ArgumentNullException(nameof(animationPackage));
            UpdateAnimationSummary(animationPackage.EntryPath, "animation package installed");

            if (!string.IsNullOrWhiteSpace(animationPackage.IdleAnimationPath) && runtimeController!.CurrentModelRoot != null)
            {
                await PlayIdleAsync(cancellationToken);
            }

            return animationPackage;
        }

        public async Task<IBehaviorPreset> ApplyBehaviorPresetAsync(
            IBehaviorPreset behaviorPreset,
            CancellationToken cancellationToken = default)
        {
            currentBehaviorPreset = behaviorPreset ?? throw new ArgumentNullException(nameof(behaviorPreset));
            UpdateAnimationSummary(behaviorPreset.Name, "behavior preset installed");

            if (!string.IsNullOrWhiteSpace(behaviorPreset.IdleAnimationPath) && runtimeController!.CurrentModelRoot != null)
            {
                await PlayIdleAsync(cancellationToken);
            }

            return behaviorPreset;
        }

        public Task PlayIdleAsync(CancellationToken cancellationToken = default)
        {
            var idleAnimationPath = ResolveIdleAnimationPath();
            if (string.IsNullOrWhiteSpace(idleAnimationPath))
            {
                throw new InvalidOperationException("No idle animation is available for the current package.");
            }

            return PlayAnimationAsync(idleAnimationPath, AnimationPlaybackMode.Loop, cancellationToken);
        }

        public Task PlayClickAsync(CancellationToken cancellationToken = default)
        {
            var clickAnimationPath = ResolveClickAnimationPath();
            if (string.IsNullOrWhiteSpace(clickAnimationPath))
            {
                throw new InvalidOperationException("No click animation is available for the current package.");
            }

            return PlayAnimationAsync(clickAnimationPath, AnimationPlaybackMode.ReturnToIdle, cancellationToken);
        }

        public Task PlayPoseAsync(CancellationToken cancellationToken = default)
        {
            var poseAnimationPath = ResolvePoseAnimationPath();
            if (string.IsNullOrWhiteSpace(poseAnimationPath))
            {
                throw new InvalidOperationException("No pose animation is available for the current package.");
            }

            return PlayAnimationAsync(poseAnimationPath, AnimationPlaybackMode.HoldLastFrame, cancellationToken);
        }

        public Task PlayPoseOnceAsync(string animationPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(animationPath))
            {
                throw new ArgumentException("An animation path is required.", nameof(animationPath));
            }

            return PlayAnimationAsync(animationPath, AnimationPlaybackMode.PlayOnce, cancellationToken);
        }

        public Task PlayLoopPathAsync(string animationPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(animationPath))
            {
                throw new ArgumentException("An animation path is required.", nameof(animationPath));
            }

            return PlayAnimationAsync(animationPath, AnimationPlaybackMode.Loop, cancellationToken);
        }

        public async Task PlayOneShotPathAsync(
            string animationPath,
            bool returnToIdle,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(animationPath))
            {
                throw new ArgumentException("An animation path is required.", nameof(animationPath));
            }

            var completionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellationRegistration = cancellationToken.Register(() => completionSource.TrySetCanceled());

            await PlayAnimationAsync(
                animationPath,
                returnToIdle ? AnimationPlaybackMode.ReturnToIdle : AnimationPlaybackMode.PlayOnce,
                cancellationToken,
                completionSource);
            await completionSource.Task;
        }

        public Task ReturnToIdleAsync(CancellationToken cancellationToken = default)
        {
            var idleAnimationPath = ResolveIdleAnimationPath();
            if (string.IsNullOrWhiteSpace(idleAnimationPath))
            {
                CancelPlaybackLoad();
                ClearCurrentPlaybackSession();
                animationStateSummary = "No idle animation available.";
                return Task.CompletedTask;
            }

            return PlayAnimationAsync(idleAnimationPath, AnimationPlaybackMode.Loop, cancellationToken);
        }

        public Task PlayActionAsync(string actionKey, CancellationToken cancellationToken = default)
        {
            if (currentBehaviorPreset == null)
            {
                throw new InvalidOperationException("No behavior preset is currently installed.");
            }

            if (!currentBehaviorPreset.TryGetActionAnimationPath(actionKey, out var animationPath))
            {
                throw new InvalidOperationException($"Behavior preset does not define an action mapping for '{actionKey}'.");
            }

            return PlayAnimationAsync(animationPath, AnimationPlaybackMode.ReturnToIdle, cancellationToken);
        }

        public Task PlayPoseKeyAsync(string poseKey, CancellationToken cancellationToken = default)
        {
            if (currentBehaviorPreset == null)
            {
                throw new InvalidOperationException("No behavior preset is currently installed.");
            }

            if (!currentBehaviorPreset.TryGetPoseAnimationPath(poseKey, out var animationPath))
            {
                throw new InvalidOperationException($"Behavior preset does not define a pose mapping for '{poseKey}'.");
            }

            return PlayAnimationAsync(animationPath, AnimationPlaybackMode.HoldLastFrame, cancellationToken);
        }

        public void ClearPackages()
        {
            currentAnimationPackage = null;
            currentBehaviorPreset = null;
            animationStateSummary = "No animation package installed.";
            ClearCurrentPlaybackSession();
        }

        private async Task PlayAnimationAsync(
            string animationPath,
            AnimationPlaybackMode playbackMode,
            CancellationToken cancellationToken,
            TaskCompletionSource<bool>? completionSource = null)
        {
            if (string.IsNullOrWhiteSpace(animationPath))
            {
                throw new ArgumentException("An animation path is required.", nameof(animationPath));
            }

            var modelInstance = GetCurrentModelInstance();
            var linkedToken = BeginPlaybackLoad(cancellationToken);
            Debug.Log($"[DesktopPetAnimation] request mode={playbackMode} path={animationPath}");

            try
            {
                ClearCurrentPlaybackSession();

                var animationInstance = await animationLoader!.LoadAsync(animationPath, linkedToken.Token);
                linkedToken.Token.ThrowIfCancellationRequested();

                animationInstance.transform.SetParent(transform, false);
                if (animationInstance.TryGetComponent<UnityEngine.Animation>(out var animation) == false || animation.clip == null)
                {
                    UnityEngine.Object.Destroy(animationInstance.gameObject);
                    throw new InvalidOperationException("Loaded VRMA instance does not contain a playable animation clip.");
                }

                var timeControl = animationInstance as ITimeControl;
                if (timeControl == null)
                {
                    UnityEngine.Object.Destroy(animationInstance.gameObject);
                    throw new InvalidOperationException("Loaded VRMA instance does not expose timeline time control.");
                }

                timeControl.OnControlTimeStart();
                timeControl.SetTime(0d);

                modelInstance.Runtime.VrmAnimation = animationInstance;
                currentPlaybackSession = new AnimationPlaybackSession(
                    animationPath,
                    animationInstance,
                    timeControl,
                    animation.clip.length,
                    playbackMode,
                    completionSource);
                returningToIdle = false;
                UpdateAnimationSummary(animationPath, playbackMode.ToString());
                Debug.Log(
                    $"[DesktopPetAnimation] started mode={playbackMode} path={animationPath} " +
                    $"clip={animation.clip.name} length={animation.clip.length:F3}");
            }
            catch (OperationCanceledException)
            {
                completionSource?.TrySetCanceled();
            }
            finally
            {
                if (playbackCancellationTokenSource == linkedToken)
                {
                    playbackCancellationTokenSource = null;
                }

                linkedToken.Dispose();
            }
        }

        private CancellationTokenSource BeginPlaybackLoad(CancellationToken cancellationToken)
        {
            CancelPlaybackLoad();
            playbackCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            return playbackCancellationTokenSource;
        }

        private void CancelPlaybackLoad()
        {
            if (playbackCancellationTokenSource == null)
            {
                return;
            }

            playbackCancellationTokenSource.Cancel();
            playbackCancellationTokenSource.Dispose();
            playbackCancellationTokenSource = null;
        }

        private void ClearCurrentPlaybackSession()
        {
            if (currentPlaybackSession == null)
            {
                return;
            }

            if (TryGetCurrentModelInstance(out var modelInstance)
                && ReferenceEquals(modelInstance.Runtime.VrmAnimation, currentPlaybackSession.AnimationInstance))
            {
                modelInstance.Runtime.VrmAnimation = null;
            }

            currentPlaybackSession.Cancel();
            currentPlaybackSession.TimeControl.OnControlTimeStop();
            UnityEngine.Object.Destroy(currentPlaybackSession.AnimationInstance.gameObject);
            currentPlaybackSession = null;
            returningToIdle = false;
        }

        private void HandleModelCleared()
        {
            ClearCurrentPlaybackSession();
        }

        private async void HandleModelLoaded(ModelLoadResult _)
        {
            CancelPlaybackLoad();
            ClearCurrentPlaybackSession();

            if (!replayIdleOnModelLoad)
            {
                return;
            }

            try
            {
                var idleAnimationPath = ResolveIdleAnimationPath();
                if (!string.IsNullOrWhiteSpace(idleAnimationPath))
                {
                    await PlayIdleAsync();
                    return;
                }

                var poseAnimationPath = ResolvePoseAnimationPath();
                if (!string.IsNullOrWhiteSpace(poseAnimationPath))
                {
                    await PlayPoseAsync();
                }
            }
            catch (Exception exception)
            {
                animationStateSummary = exception.Message;
            }
        }

        private async Task ResumeIdleAfterPlaybackAsync()
        {
            try
            {
                await ReturnToIdleAsync();
            }
            catch (Exception exception)
            {
                animationStateSummary = exception.Message;
            }
            finally
            {
                returningToIdle = false;
            }
        }

        private void TrySubscribeToRuntimeController()
        {
            if (isSubscribedToRuntimeController)
            {
                return;
            }

            runtimeController ??= GetComponent<DesktopPetRuntimeController>();
            if (runtimeController == null)
            {
                return;
            }

            runtimeController.ModelLoaded += HandleModelLoaded;
            runtimeController.ModelCleared += HandleModelCleared;
            isSubscribedToRuntimeController = true;
        }

        private Vrm10Instance GetCurrentModelInstance()
        {
            if (!TryGetCurrentModelInstance(out var modelInstance))
            {
                throw new InvalidOperationException("A loaded VRM10 model is required before playing animation packages.");
            }

            return modelInstance;
        }

        private bool TryGetCurrentModelInstance(out Vrm10Instance modelInstance)
        {
            modelInstance = null!;

            if (runtimeController == null || runtimeController.CurrentModelRoot == null)
            {
                return false;
            }

            modelInstance = runtimeController.CurrentModelRoot.GetComponentInChildren<Vrm10Instance>();
            return modelInstance != null;
        }

        private string ResolveIdleAnimationPath()
        {
            if (currentBehaviorPreset != null && !string.IsNullOrWhiteSpace(currentBehaviorPreset.IdleAnimationPath))
            {
                return currentBehaviorPreset.IdleAnimationPath;
            }

            return currentAnimationPackage != null ? currentAnimationPackage.IdleAnimationPath : string.Empty;
        }

        private string ResolveClickAnimationPath()
        {
            if (currentBehaviorPreset != null && !string.IsNullOrWhiteSpace(currentBehaviorPreset.ClickAnimationPath))
            {
                return currentBehaviorPreset.ClickAnimationPath;
            }

            return currentAnimationPackage != null ? currentAnimationPackage.ClickAnimationPath : string.Empty;
        }

        private string ResolvePoseAnimationPath()
        {
            if (currentBehaviorPreset != null && !string.IsNullOrWhiteSpace(currentBehaviorPreset.PoseAnimationPath))
            {
                return currentBehaviorPreset.PoseAnimationPath;
            }

            return currentAnimationPackage != null ? currentAnimationPackage.PoseAnimationPath : string.Empty;
        }

        private void UpdateAnimationSummary(string value, string state)
        {
            animationStateSummary = $"{state}: {Path.GetFileNameWithoutExtension(value)}";
        }

        private enum AnimationPlaybackMode
        {
            Loop = 0,
            ReturnToIdle = 1,
            HoldLastFrame = 2,
            PlayOnce = 3,
        }

        private sealed class AnimationPlaybackSession
        {
            public AnimationPlaybackSession(
                string path,
                Vrm10AnimationInstance animationInstance,
                ITimeControl timeControl,
                float duration,
                AnimationPlaybackMode mode,
                TaskCompletionSource<bool>? completionSource)
            {
                Path = path;
                AnimationInstance = animationInstance;
                TimeControl = timeControl;
                Duration = duration > 0f ? duration : 0.0001f;
                Mode = mode;
                this.completionSource = completionSource;
            }

            private readonly TaskCompletionSource<bool>? completionSource;

            public Vrm10AnimationInstance AnimationInstance { get; }

            public float Duration { get; }

            public AnimationPlaybackMode Mode { get; }

            public string Path { get; }

            public float Time { get; set; }

            public ITimeControl TimeControl { get; }

            public void Complete()
            {
                completionSource?.TrySetResult(true);
            }

            public void Cancel()
            {
                completionSource?.TrySetCanceled();
            }
        }
    }
}
