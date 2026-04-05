#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UniVRM10;
using UnityEngine;
using UnityEngine.Timeline;
using VividSoul.Runtime.App;
using VividSoul.Runtime.Movement;

namespace VividSoul.Runtime.Animation
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DesktopPetRuntimeController))]
    [RequireComponent(typeof(DesktopPetAnimationController))]
    public sealed class DesktopPetFallbackMotionController : MonoBehaviour
    {
        private static readonly BoneTarget[] LegacyBoneTargets =
        {
            new(HumanBodyBones.Spine, new Vector3(-6f, 0f, 0f)),
            new(HumanBodyBones.Chest, new Vector3(8f, 0f, 0f)),
            new(HumanBodyBones.UpperChest, new Vector3(10f, 0f, 0f)),
            new(HumanBodyBones.Neck, new Vector3(-4f, 0f, 0f)),
            new(HumanBodyBones.Head, new Vector3(4f, 0f, 0f)),
            new(HumanBodyBones.LeftUpperArm, new Vector3(8f, 0f, 20f)),
            new(HumanBodyBones.RightUpperArm, new Vector3(-8f, 0f, -20f)),
            new(HumanBodyBones.LeftLowerArm, new Vector3(-10f, 0f, 10f)),
            new(HumanBodyBones.RightLowerArm, new Vector3(-10f, 0f, -10f)),
            new(HumanBodyBones.LeftHand, new Vector3(0f, -4f, 6f)),
            new(HumanBodyBones.RightHand, new Vector3(0f, 4f, -6f)),
        };

        [SerializeField] private bool enableRandomAmbientPoses = true;
        [SerializeField] private float randomPoseIntervalMin = 6f;
        [SerializeField] private float randomPoseIntervalMax = 14f;
        [SerializeField, Range(0f, 1f)] private float defaultPoseSampleNormalizedTime = 1f;
        [SerializeField] private float returnToDefaultDuration = 0.7f;
        [SerializeField] private float proceduralOffsetSmoothTime = 0.18f;
        [SerializeField] private float horizontalSwayAmplitude = 0.008f;
        [SerializeField] private float horizontalSwayFrequency = 0.28f;
        [SerializeField] private float verticalBobAmplitude = 0.012f;
        [SerializeField] private float verticalBobFrequency = 0.7f;
        [SerializeField] private float poseBlend = 1f;
        [SerializeField] private float headYawAmplitude = 1.2f;
        [SerializeField] private float headYawFrequency = 0.25f;
        [SerializeField] private float chestBreathAmplitude = 1f;
        [SerializeField] private float chestBreathFrequency = 0.75f;

        private DesktopPetAnimationController? animationController;
        private IAnimationLoader? animationLoader;
        private Animator? animator;
        private readonly List<BoneState> boneStates = new();
        private CancellationTokenSource? captureCancellationTokenSource;
        private GameObject? currentModelRoot;
        private Vector3 currentOffset;
        private Vector3 currentOffsetVelocity;
        private bool hasDefaultPose;
        private bool hasCapturedPlaybackCompletionPose;
        private bool isCapturingDefaultPose;
        private bool isTransitioningToPose;
        private bool isReturningToDefaultPose;
        private bool isWaitingForPosePlayback;
        private string? lastRandomPoseId;
        private float nextRandomPoseInSeconds;
        private string? pendingPosePlaybackPath;
        private CancellationTokenSource? poseEntryCancellationTokenSource;
        private float poseEntryTransitionTime;
        private DesktopPetRuntimeController? runtimeController;
        private DesktopPetMovementController? movementController;
        private float returnToDefaultTime;
        private float time;
        private bool wasPlaybackActive;

        public int CachedBoneCount => boneStates.Count;

        public bool IsActive { get; private set; }

        private void Awake()
        {
            animationController = GetComponent<DesktopPetAnimationController>();
            animationLoader = new VrmaAnimationLoaderService();
            runtimeController = GetComponent<DesktopPetRuntimeController>();
            movementController = GetComponent<DesktopPetMovementController>();
        }

        private void OnEnable()
        {
            if (animationController != null)
            {
                animationController.PlayOncePlaybackCompleting += HandlePlayOncePlaybackCompleting;
            }
        }

        private void OnDisable()
        {
            if (animationController != null)
            {
                animationController.PlayOncePlaybackCompleting -= HandlePlayOncePlaybackCompleting;
            }

            CancelCapture();
            CancelPoseEntryLoad();
            RestoreBasePoseAndOffset();
        }

        private void LateUpdate()
        {
            var nextModelRoot = runtimeController != null ? runtimeController.CurrentModelRoot : null;
            if (!ReferenceEquals(currentModelRoot, nextModelRoot))
            {
                HandleModelChanged(nextModelRoot);
            }

            if (currentModelRoot == null)
            {
                IsActive = false;
                return;
            }

            var playbackActive = animationController != null && animationController.HasActivePlayback;
            var blockAmbientForMovement = movementController != null && movementController.IsMoving;

            if (playbackActive || blockAmbientForMovement)
            {
                if (playbackActive && !wasPlaybackActive)
                {
                    if (!isTransitioningToPose && !isWaitingForPosePlayback)
                    {
                        RemoveCurrentOffset();
                    }

                    currentOffsetVelocity = Vector3.zero;
                    isReturningToDefaultPose = false;
                    ClearPoseEntryTransitionState();
                }
                else if (!playbackActive && blockAmbientForMovement && !wasPlaybackActive)
                {
                    if (!isTransitioningToPose && !isWaitingForPosePlayback)
                    {
                        RemoveCurrentOffset();
                    }

                    currentOffsetVelocity = Vector3.zero;
                    isReturningToDefaultPose = false;
                    ClearPoseEntryTransitionState();
                }

                wasPlaybackActive = playbackActive;
                IsActive = false;
                return;
            }

            if (wasPlaybackActive)
            {
                wasPlaybackActive = false;
                BeginReturnToDefaultPose();
                ScheduleNextRandomPose();
            }

            time += Time.deltaTime;

            if (isCapturingDefaultPose)
            {
                ApplyProceduralOffset();
                IsActive = true;
                return;
            }

            if (HandlePoseEntryTransition())
            {
                IsActive = boneStates.Count > 0;
                return;
            }

            ApplyProceduralOffset();
            ApplyDefaultOrFallbackPose();
            UpdateRandomPoseScheduler();
            IsActive = boneStates.Count > 0;
        }

        private void HandleModelChanged(GameObject? nextModelRoot)
        {
            CancelCapture();
            CancelPoseEntryLoad();
            RestoreBasePoseAndOffset();
            currentModelRoot = nextModelRoot;
            CacheBoneStates(nextModelRoot);
            hasDefaultPose = false;
            isReturningToDefaultPose = false;
            wasPlaybackActive = false;
            returnToDefaultTime = 0f;
            time = 0f;
            lastRandomPoseId = null;
            hasCapturedPlaybackCompletionPose = false;
            currentOffsetVelocity = Vector3.zero;
            ClearPoseEntryTransitionState();
            ScheduleNextRandomPose();

            if (nextModelRoot != null)
            {
                CaptureDefaultPoseAsync(nextModelRoot);
            }
        }

        private void ApplyProceduralOffset()
        {
            if (currentModelRoot == null)
            {
                return;
            }

            var previousOffset = currentOffset;
            RemoveCurrentOffset();
            currentOffset = Vector3.SmoothDamp(
                previousOffset,
                Vector3.zero,
                ref currentOffsetVelocity,
                Mathf.Max(0.01f, proceduralOffsetSmoothTime));
            currentModelRoot.transform.localPosition += currentOffset;
        }

        private void ApplyDefaultOrFallbackPose()
        {
            var breath = Mathf.Sin(time * chestBreathFrequency * Mathf.PI * 2f) * chestBreathAmplitude;
            var headYaw = Mathf.Sin(time * headYawFrequency * Mathf.PI * 2f) * headYawAmplitude;
            var torsoRoll = Mathf.Sin(time * horizontalSwayFrequency * Mathf.PI * 2f) * horizontalSwayAmplitude * 70f;
            var torsoPitch = Mathf.Sin(time * verticalBobFrequency * Mathf.PI * 2f) * verticalBobAmplitude * 35f;
            var shoulderRoll = Mathf.Sin((time * horizontalSwayFrequency * Mathf.PI * 2f) + (Mathf.PI * 0.35f))
                               * horizontalSwayAmplitude
                               * 110f;
            var handPitch = Mathf.Sin((time * verticalBobFrequency * Mathf.PI * 2f) + (Mathf.PI * 0.5f))
                            * verticalBobAmplitude
                            * 50f;

            if (!hasDefaultPose)
            {
                ApplyLegacyFallbackPose(breath, headYaw);
                return;
            }

            var blend = 1f;
            if (isReturningToDefaultPose)
            {
                returnToDefaultTime += Time.deltaTime;
                var duration = Mathf.Max(returnToDefaultDuration, 0.01f);
                blend = EaseOutCubic(Mathf.Clamp01(returnToDefaultTime / duration));
                if (blend >= 1f)
                {
                    isReturningToDefaultPose = false;
                }
            }

            foreach (var boneState in boneStates)
            {
                var targetRotation = GetDefaultPoseRotation(
                    boneState,
                    breath,
                    headYaw,
                    torsoRoll,
                    torsoPitch,
                    shoulderRoll,
                    handPitch);
                boneState.Transform.localRotation = isReturningToDefaultPose
                    ? Quaternion.Slerp(boneState.TransitionSourceRotation, targetRotation, blend)
                    : targetRotation;

                if (boneState.Bone == HumanBodyBones.Hips)
                {
                    boneState.Transform.localPosition = isReturningToDefaultPose
                        ? Vector3.Lerp(boneState.TransitionSourcePosition, boneState.DefaultLocalPosition, blend)
                        : boneState.DefaultLocalPosition;
                }
            }
        }

        private void ApplyLegacyFallbackPose(float breath, float headYaw)
        {
            foreach (var boneState in boneStates)
            {
                var eulerOffset = ResolveLegacyEulerOffset(boneState.Bone);
                if (boneState.Bone == HumanBodyBones.Chest || boneState.Bone == HumanBodyBones.UpperChest)
                {
                    eulerOffset.x += breath;
                }

                if (boneState.Bone == HumanBodyBones.Head)
                {
                    eulerOffset.y += headYaw;
                }

                var targetRotation = boneState.BaseLocalRotation * Quaternion.Euler(eulerOffset);
                boneState.Transform.localRotation = Quaternion.Slerp(
                    boneState.BaseLocalRotation,
                    targetRotation,
                    Mathf.Clamp01(poseBlend));

                if (boneState.Bone == HumanBodyBones.Hips)
                {
                    boneState.Transform.localPosition = boneState.BaseLocalPosition;
                }
            }
        }

        private Quaternion GetDefaultPoseRotation(
            BoneState boneState,
            float breath,
            float headYaw,
            float torsoRoll,
            float torsoPitch,
            float shoulderRoll,
            float handPitch)
        {
            var targetRotation = boneState.DefaultLocalRotation;
            targetRotation *= boneState.Bone switch
            {
                HumanBodyBones.Spine => Quaternion.Euler(torsoPitch * 0.2f, 0f, torsoRoll * 0.2f),
                HumanBodyBones.Chest => Quaternion.Euler(breath + (torsoPitch * 0.35f), 0f, torsoRoll * 0.45f),
                HumanBodyBones.UpperChest => Quaternion.Euler((breath * 0.7f) + (torsoPitch * 0.45f), 0f, torsoRoll * 0.65f),
                HumanBodyBones.Neck => Quaternion.Euler(torsoPitch * 0.1f, 0f, -torsoRoll * 0.25f),
                HumanBodyBones.Head => Quaternion.Euler(torsoPitch * 0.05f, headYaw, -torsoRoll * 0.2f),
                HumanBodyBones.LeftUpperArm => Quaternion.Euler(torsoPitch * 0.1f, 0f, shoulderRoll * 0.45f),
                HumanBodyBones.RightUpperArm => Quaternion.Euler(torsoPitch * 0.1f, 0f, -shoulderRoll * 0.45f),
                HumanBodyBones.LeftLowerArm => Quaternion.Euler(handPitch * 0.2f, 0f, shoulderRoll * 0.15f),
                HumanBodyBones.RightLowerArm => Quaternion.Euler(handPitch * 0.2f, 0f, -shoulderRoll * 0.15f),
                HumanBodyBones.LeftHand => Quaternion.Euler(handPitch * 0.35f, 0f, shoulderRoll * 0.1f),
                HumanBodyBones.RightHand => Quaternion.Euler(handPitch * 0.35f, 0f, -shoulderRoll * 0.1f),
                _ => Quaternion.identity,
            };

            return targetRotation;
        }

        public async Task PlayPoseOnceAsync(string posePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(posePath))
            {
                throw new ArgumentException("A pose path is required.", nameof(posePath));
            }

            if (!CanUsePoseEntryTransition())
            {
                await animationController!.PlayPoseOnceAsync(posePath, cancellationToken);
                return;
            }

            await PreparePoseEntryTransitionAsync(posePath, cancellationToken);
        }

        private bool CanUsePoseEntryTransition()
        {
            return animationController != null
                   && animationLoader != null
                   && runtimeController != null
                   && runtimeController.CurrentModelRoot != null
                   && boneStates.Count > 0
                   && hasDefaultPose
                   && !isCapturingDefaultPose
                   && !animationController.HasActivePlayback;
        }

        private async Task PreparePoseEntryTransitionAsync(string posePath, CancellationToken cancellationToken)
        {
            if (runtimeController?.CurrentModelRoot?.GetComponentInChildren<Vrm10Instance>() is not Vrm10Instance vrmInstance)
            {
                await animationController!.PlayPoseOnceAsync(posePath, cancellationToken);
                return;
            }

            CancelPoseEntryLoad();
            ClearPoseEntryTransitionState();
            poseEntryCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var linkedCancellationToken = poseEntryCancellationTokenSource.Token;

            CaptureCurrentPoseAsTransitionSource();
            Vrm10AnimationInstance? animationInstance = null;
            ITimeControl? timeControl = null;

            try
            {
                animationInstance = await animationLoader!.LoadAsync(posePath, linkedCancellationToken);
                linkedCancellationToken.ThrowIfCancellationRequested();

                animationInstance.transform.SetParent(transform, false);
                if (animationInstance.TryGetComponent<UnityEngine.Animation>(out var animation) == false || animation.clip == null)
                {
                    throw new InvalidOperationException("Loaded pose VRMA does not contain a playable animation clip.");
                }

                timeControl = animationInstance as ITimeControl
                              ?? throw new InvalidOperationException("Loaded pose VRMA does not expose timeline time control.");

                timeControl.OnControlTimeStart();
                timeControl.SetTime(0d);
                vrmInstance.Runtime.VrmAnimation = animationInstance;
                vrmInstance.Runtime.Process();
                CaptureCurrentPoseAsTransitionTarget();
                RestoreTransitionSourcePose();

                pendingPosePlaybackPath = posePath;
                isTransitioningToPose = true;
                poseEntryTransitionTime = 0f;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (ReferenceEquals(vrmInstance.Runtime.VrmAnimation, animationInstance))
                {
                    vrmInstance.Runtime.VrmAnimation = null;
                }

                timeControl?.OnControlTimeStop();
                if (animationInstance != null)
                {
                    Destroy(animationInstance.gameObject);
                }

                if (poseEntryCancellationTokenSource != null
                    && poseEntryCancellationTokenSource.Token == linkedCancellationToken)
                {
                    poseEntryCancellationTokenSource.Dispose();
                    poseEntryCancellationTokenSource = null;
                }
            }
        }

        private async void CaptureDefaultPoseAsync(GameObject modelRoot)
        {
            if (runtimeController == null || animationLoader == null)
            {
                return;
            }

            if (modelRoot.GetComponentInChildren<Vrm10Instance>() is not Vrm10Instance vrmInstance)
            {
                return;
            }

            CancelCapture();
            captureCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = captureCancellationTokenSource.Token;
            isCapturingDefaultPose = true;

            Vrm10AnimationInstance? animationInstance = null;
            ITimeControl? timeControl = null;

            try
            {
                var posePath = runtimeController.GetBuiltInPosePath(runtimeController.BuiltInDefaultPoseId);
                if (!File.Exists(posePath))
                {
                    return;
                }

                animationInstance = await animationLoader.LoadAsync(posePath, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                animationInstance.transform.SetParent(transform, false);
                if (animationInstance.TryGetComponent<UnityEngine.Animation>(out var animation) == false || animation.clip == null)
                {
                    throw new InvalidOperationException("Loaded default pose VRMA does not contain a playable animation clip.");
                }

                timeControl = animationInstance as ITimeControl
                              ?? throw new InvalidOperationException("Loaded default pose VRMA does not expose timeline time control.");

                var duration = Mathf.Max(animation.clip.length, 0.0001f);
                var sampleTime = Mathf.Clamp01(defaultPoseSampleNormalizedTime) * duration;

                timeControl.OnControlTimeStart();
                timeControl.SetTime(sampleTime);
                vrmInstance.Runtime.VrmAnimation = animationInstance;
                vrmInstance.Runtime.Process();
                CaptureCurrentPoseAsDefault();
                ScheduleNextRandomPose();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                hasDefaultPose = false;
            }
            finally
            {
                if (ReferenceEquals(vrmInstance.Runtime.VrmAnimation, animationInstance))
                {
                    vrmInstance.Runtime.VrmAnimation = null;
                }

                timeControl?.OnControlTimeStop();
                if (animationInstance != null)
                {
                    Destroy(animationInstance.gameObject);
                }

                isCapturingDefaultPose = false;
                if (captureCancellationTokenSource != null && captureCancellationTokenSource.Token == cancellationToken)
                {
                    captureCancellationTokenSource.Dispose();
                    captureCancellationTokenSource = null;
                }
            }
        }

        private void CaptureCurrentPoseAsDefault()
        {
            foreach (var boneState in boneStates)
            {
                boneState.DefaultLocalRotation = boneState.Transform.localRotation;
                boneState.TransitionSourceRotation = boneState.Transform.localRotation;
                if (boneState.Bone == HumanBodyBones.Hips)
                {
                    boneState.DefaultLocalPosition = boneState.Transform.localPosition;
                    boneState.TransitionSourcePosition = boneState.Transform.localPosition;
                }
            }

            hasDefaultPose = boneStates.Count > 0;
            hasCapturedPlaybackCompletionPose = false;
            isReturningToDefaultPose = false;
            returnToDefaultTime = 0f;
        }

        private void BeginReturnToDefaultPose()
        {
            if (!hasDefaultPose)
            {
                return;
            }

            if (!hasCapturedPlaybackCompletionPose)
            {
                CaptureCurrentPoseAsTransitionSource();
            }

            hasCapturedPlaybackCompletionPose = false;
            isReturningToDefaultPose = true;
            returnToDefaultTime = 0f;
        }

        private void HandlePlayOncePlaybackCompleting()
        {
            CaptureCurrentPoseAsTransitionSource();
            hasCapturedPlaybackCompletionPose = boneStates.Count > 0;
        }

        private void CaptureCurrentPoseAsTransitionSource()
        {
            foreach (var boneState in boneStates)
            {
                boneState.TransitionSourceRotation = boneState.Transform.localRotation;
                if (boneState.Bone == HumanBodyBones.Hips)
                {
                    boneState.TransitionSourcePosition = boneState.Transform.localPosition;
                }
            }
        }

        private void CaptureCurrentPoseAsTransitionTarget()
        {
            foreach (var boneState in boneStates)
            {
                boneState.TransitionTargetRotation = boneState.Transform.localRotation;
                if (boneState.Bone == HumanBodyBones.Hips)
                {
                    boneState.TransitionTargetPosition = boneState.Transform.localPosition;
                }
            }
        }

        private void RestoreTransitionSourcePose()
        {
            foreach (var boneState in boneStates)
            {
                boneState.Transform.localRotation = boneState.TransitionSourceRotation;
                if (boneState.Bone == HumanBodyBones.Hips)
                {
                    boneState.Transform.localPosition = boneState.TransitionSourcePosition;
                }
            }
        }

        private bool HandlePoseEntryTransition()
        {
            if (!isTransitioningToPose && !isWaitingForPosePlayback)
            {
                return false;
            }

            ApplyPoseEntryOffset();
            if (isTransitioningToPose)
            {
                poseEntryTransitionTime += Time.deltaTime;
                var duration = Mathf.Max(returnToDefaultDuration, 0.01f);
                var blend = EaseOutCubic(Mathf.Clamp01(poseEntryTransitionTime / duration));
                ApplyTransitionToPose(blend);
                if (blend >= 1f)
                {
                    isTransitioningToPose = false;
                    isWaitingForPosePlayback = true;
                    _ = StartPendingPosePlaybackAsync();
                }

                return true;
            }

            ApplyTransitionToPose(1f);
            return true;
        }

        private void ApplyTransitionToPose(float blend)
        {
            foreach (var boneState in boneStates)
            {
                boneState.Transform.localRotation = Quaternion.Slerp(
                    boneState.TransitionSourceRotation,
                    boneState.TransitionTargetRotation,
                    blend);
                if (boneState.Bone == HumanBodyBones.Hips)
                {
                    boneState.Transform.localPosition = Vector3.Lerp(
                        boneState.TransitionSourcePosition,
                        boneState.TransitionTargetPosition,
                        blend);
                }
            }
        }

        private void ApplyPoseEntryOffset()
        {
            if (currentModelRoot == null)
            {
                return;
            }

            var previousOffset = currentOffset;
            RemoveCurrentOffset();
            currentOffset = Vector3.SmoothDamp(
                previousOffset,
                Vector3.zero,
                ref currentOffsetVelocity,
                Mathf.Max(0.01f, proceduralOffsetSmoothTime));
            currentModelRoot.transform.localPosition += currentOffset;
        }

        private async Task StartPendingPosePlaybackAsync()
        {
            if (animationController == null || string.IsNullOrWhiteSpace(pendingPosePlaybackPath))
            {
                ClearPoseEntryTransitionState();
                return;
            }

            try
            {
                await animationController.PlayPoseOnceAsync(pendingPosePlaybackPath);
            }
            catch (OperationCanceledException)
            {
                ClearPoseEntryTransitionState();
            }
            catch (Exception)
            {
                ClearPoseEntryTransitionState();
                ScheduleNextRandomPose();
            }
        }

        private void UpdateRandomPoseScheduler()
        {
            if (!enableRandomAmbientPoses
                || !hasDefaultPose
                || isCapturingDefaultPose
                || isTransitioningToPose
                || isReturningToDefaultPose
                || isWaitingForPosePlayback
                || runtimeController == null
                || runtimeController.IsModelInteractionBlocked
                || boneStates.Count == 0)
            {
                return;
            }

            nextRandomPoseInSeconds -= Time.deltaTime;
            if (nextRandomPoseInSeconds > 0f)
            {
                return;
            }

            var candidates = runtimeController.BuiltInPoses
                .Where(pose => !string.Equals(pose.Id, runtimeController.BuiltInDefaultPoseId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (candidates.Length == 0)
            {
                ScheduleNextRandomPose();
                return;
            }

            var selectedPose = SelectRandomPose(candidates);
            lastRandomPoseId = selectedPose.Id;
            nextRandomPoseInSeconds = float.PositiveInfinity;
            runtimeController.ApplyBuiltInPose(selectedPose.Id);
        }

        private BuiltInPoseOption SelectRandomPose(IReadOnlyList<BuiltInPoseOption> candidates)
        {
            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            var availableCandidates = candidates
                .Where(pose => !string.Equals(pose.Id, lastRandomPoseId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var pool = availableCandidates.Length > 0 ? availableCandidates : candidates.ToArray();
            return pool[UnityEngine.Random.Range(0, pool.Length)];
        }

        private void ScheduleNextRandomPose()
        {
            nextRandomPoseInSeconds = UnityEngine.Random.Range(
                Mathf.Max(0.5f, randomPoseIntervalMin),
                Mathf.Max(randomPoseIntervalMin, randomPoseIntervalMax));
        }

        private void CacheBoneStates(GameObject? modelRoot)
        {
            boneStates.Clear();
            animator = null;
            if (modelRoot == null)
            {
                return;
            }

            foreach (var bone in EnumerateTrackedBones())
            {
                var transform = TryResolveBoneTransform(modelRoot, bone);
                if (transform == null)
                {
                    continue;
                }

                boneStates.Add(new BoneState(bone, transform));
            }
        }

        private void RestoreBasePoseAndOffset()
        {
            RemoveCurrentOffset();

            foreach (var boneState in boneStates)
            {
                boneState.Transform.localRotation = boneState.BaseLocalRotation;
                if (boneState.Bone == HumanBodyBones.Hips)
                {
                    boneState.Transform.localPosition = boneState.BaseLocalPosition;
                }
            }

            hasDefaultPose = false;
            hasCapturedPlaybackCompletionPose = false;
            isCapturingDefaultPose = false;
            isTransitioningToPose = false;
            isReturningToDefaultPose = false;
            isWaitingForPosePlayback = false;
            pendingPosePlaybackPath = null;
            wasPlaybackActive = false;
            currentOffsetVelocity = Vector3.zero;
            poseEntryTransitionTime = 0f;
            IsActive = false;
        }

        private void CancelCapture()
        {
            if (captureCancellationTokenSource == null)
            {
                return;
            }

            captureCancellationTokenSource.Cancel();
            captureCancellationTokenSource.Dispose();
            captureCancellationTokenSource = null;
            isCapturingDefaultPose = false;
        }

        private void CancelPoseEntryLoad()
        {
            if (poseEntryCancellationTokenSource == null)
            {
                return;
            }

            poseEntryCancellationTokenSource.Cancel();
            poseEntryCancellationTokenSource.Dispose();
            poseEntryCancellationTokenSource = null;
        }

        private void ClearPoseEntryTransitionState()
        {
            isTransitioningToPose = false;
            isWaitingForPosePlayback = false;
            pendingPosePlaybackPath = null;
            poseEntryTransitionTime = 0f;
        }

        private void RemoveCurrentOffset()
        {
            if (currentModelRoot == null)
            {
                currentOffset = Vector3.zero;
                return;
            }

            currentModelRoot.transform.localPosition -= currentOffset;
            currentOffset = Vector3.zero;
        }

        private Transform? TryResolveBoneTransform(GameObject modelRoot, HumanBodyBones bone)
        {
            if (modelRoot.GetComponentInChildren<Vrm10Instance>() is Vrm10Instance vrmInstance)
            {
                var runtime = vrmInstance.Runtime;
                animator ??= runtime.ControlRig != null
                    ? runtime.ControlRig.ControlRigAnimator
                    : modelRoot.GetComponentInChildren<Animator>();

                var controlRigBone = runtime.ControlRig?.GetBoneTransform(bone);
                if (controlRigBone != null)
                {
                    return controlRigBone;
                }

                if (vrmInstance.TryGetBoneTransform(bone, out var vrmBone))
                {
                    return vrmBone;
                }
            }

            animator ??= modelRoot.GetComponentInChildren<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                return null;
            }

            return animator.GetBoneTransform(bone);
        }

        private static IEnumerable<HumanBodyBones> EnumerateTrackedBones()
        {
            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone)
                {
                    continue;
                }

                yield return bone;
            }
        }

        private static Vector3 ResolveLegacyEulerOffset(HumanBodyBones bone)
        {
            for (var index = 0; index < LegacyBoneTargets.Length; index++)
            {
                if (LegacyBoneTargets[index].Bone == bone)
                {
                    return LegacyBoneTargets[index].EulerOffset;
                }
            }

            return Vector3.zero;
        }

        private static float EaseOutCubic(float value)
        {
            var t = Mathf.Clamp01(value);
            var inverted = 1f - t;
            return 1f - (inverted * inverted * inverted);
        }

        private readonly struct BoneTarget
        {
            public BoneTarget(HumanBodyBones bone, Vector3 eulerOffset)
            {
                Bone = bone;
                EulerOffset = eulerOffset;
            }

            public HumanBodyBones Bone { get; }

            public Vector3 EulerOffset { get; }
        }

        private sealed class BoneState
        {
            public BoneState(HumanBodyBones bone, Transform transform)
            {
                Bone = bone;
                Transform = transform ?? throw new ArgumentNullException(nameof(transform));
                BaseLocalRotation = transform.localRotation;
                DefaultLocalRotation = transform.localRotation;
                TransitionSourceRotation = transform.localRotation;
                TransitionTargetRotation = transform.localRotation;
                BaseLocalPosition = transform.localPosition;
                DefaultLocalPosition = transform.localPosition;
                TransitionSourcePosition = transform.localPosition;
                TransitionTargetPosition = transform.localPosition;
            }

            public HumanBodyBones Bone { get; }

            public Quaternion BaseLocalRotation { get; }

            public Vector3 BaseLocalPosition { get; }

            public Quaternion DefaultLocalRotation { get; set; }

            public Vector3 DefaultLocalPosition { get; set; }

            public Transform Transform { get; }

            public Quaternion TransitionSourceRotation { get; set; }

            public Vector3 TransitionSourcePosition { get; set; }

            public Quaternion TransitionTargetRotation { get; set; }

            public Vector3 TransitionTargetPosition { get; set; }
        }
    }
}
