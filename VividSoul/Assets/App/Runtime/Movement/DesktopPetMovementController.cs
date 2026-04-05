#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VividSoul.Runtime.Animation;
using VividSoul.Runtime.App;
using VividSoul.Runtime.Avatar;
using VividSoul.Runtime.Behavior;
using VividSoul.Runtime.Interaction;

namespace VividSoul.Runtime.Movement
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DesktopPetRuntimeController))]
    [RequireComponent(typeof(DesktopPetAnimationController))]
    public sealed class DesktopPetMovementController : MonoBehaviour
    {
        private const float MinimumMoveDistance = 0.01f;

        [SerializeField, Range(0f, 0.2f)] private float viewportPadding = 0.02f;
        [SerializeField, Range(0f, 0.4f)] private float sampleViewportPadding = 0.12f;
        [SerializeField] private float walkUnitsPerSecond = 0.42f;
        [SerializeField] private float flyUnitsPerSecond = 2.4f;
        [SerializeField] private float hopUnitsPerSecond = 1.4f;
        [SerializeField] private float hopArcHeight = 0.35f;

        [Header("Orientation")]
        [SerializeField]
        [Tooltip("Local Y when the pet faces the user (matches saved spawn facing).")]
        private float facingTowardUserLocalY = 180f;

        [SerializeField]
        [Tooltip("Local Y when moving along +screen X (right), for profile walk loops authored in sagittal stride.")]
        private float profileLocalYawScreenRight = 90f;

        [SerializeField]
        [Tooltip("Local Y when moving along -screen X (left). Tune with profile walk VRMA so stride matches travel.")]
        private float profileLocalYawScreenLeft = 270f;

        [SerializeField, Range(0.8f, 2.5f)]
        [Tooltip("Vertical axis must exceed horizontal by this factor to use front-facing + lean path.")]
        private float verticalVersusHorizontalBias = 1.12f;

        [SerializeField, Range(0f, 12f)]
        [Tooltip("Roll lean toward up/down travel when using the front-facing move path.")]
        private float verticalTravelLeanZDegrees = 5.5f;

        [SerializeField, Range(0.08f, 1.2f)] private float turnToMoveSeconds = 0.38f;
        [SerializeField, Range(0.08f, 1.2f)] private float turnToFrontSeconds = 0.42f;
        [SerializeField, Range(0f, 0.5f)]
        [Tooltip("Walk loop starts partway through the turn-in for a softer transition.")]
        private float loopLeadInSeconds = 0.14f;

        [SerializeField, TextArea] private string movementStateSummary = "Movement idle.";

        private readonly DesktopPetBoundsService boundsService = new();
        private DesktopPetAnimationController? animationController;
        private DesktopPetDragController? dragController;
        private DesktopPetRotationController? rotationController;
        private DesktopPetRuntimeController? runtimeController;
        private CancellationTokenSource? movementCancellationTokenSource;
        private IBehaviorPreset? currentBehaviorPreset;
        private bool isSubscribedToRuntimeController;
        private int moveOperationId;

        public bool IsMoving { get; private set; }

        public string MovementStateSummary => movementStateSummary;

        public bool HasConfiguredMovementAnimation
            => currentBehaviorPreset != null
               && (!string.IsNullOrWhiteSpace(currentBehaviorPreset.Movement.StartAnimationPath)
                   || !string.IsNullOrWhiteSpace(currentBehaviorPreset.Movement.LoopAnimationPath)
                   || !string.IsNullOrWhiteSpace(currentBehaviorPreset.Movement.LoopVerticalAnimationPath)
                   || !string.IsNullOrWhiteSpace(currentBehaviorPreset.Movement.StopAnimationPath));

        public bool HasConfiguredMovementLoopAnimation
            => currentBehaviorPreset != null
               && (!string.IsNullOrWhiteSpace(currentBehaviorPreset.Movement.LoopAnimationPath)
                   || !string.IsNullOrWhiteSpace(currentBehaviorPreset.Movement.LoopVerticalAnimationPath));

        private void Awake()
        {
            animationController = GetComponent<DesktopPetAnimationController>();
            dragController = GetComponent<DesktopPetDragController>();
            rotationController = GetComponent<DesktopPetRotationController>();
            runtimeController = GetComponent<DesktopPetRuntimeController>();
        }

        private void OnEnable()
        {
            TrySubscribeToRuntimeController();
        }

        private void OnDisable()
        {
            CancelCurrentMove();

            if (runtimeController != null && isSubscribedToRuntimeController)
            {
                runtimeController.ModelCleared -= HandleModelCleared;
                runtimeController.ModelLoaded -= HandleModelLoaded;
                isSubscribedToRuntimeController = false;
            }
        }

        public void ApplyBehaviorPreset(IBehaviorPreset behaviorPreset)
        {
            currentBehaviorPreset = behaviorPreset ?? throw new ArgumentNullException(nameof(behaviorPreset));
            movementStateSummary = $"Movement preset ready: {currentBehaviorPreset.Movement.Type}";
            Debug.Log(
                $"[DesktopPetMovement] behavior={currentBehaviorPreset.Name} type={currentBehaviorPreset.Movement.Type} " +
                $"start={currentBehaviorPreset.Movement.StartAnimationPath} " +
                $"loop={currentBehaviorPreset.Movement.LoopAnimationPath} " +
                $"loopVertical={currentBehaviorPreset.Movement.LoopVerticalAnimationPath} " +
                $"stop={currentBehaviorPreset.Movement.StopAnimationPath}");
        }

        public void ClearBehaviorPreset()
        {
            CancelCurrentMove();
            currentBehaviorPreset = null;
            movementStateSummary = "Movement preset cleared.";
        }

        [ContextMenu("Move To Sampled Desktop Point")]
        public async void MoveToSampledPoint()
        {
            try
            {
                await MoveToSampledPointAsync();
            }
            catch (Exception exception)
            {
                movementStateSummary = exception.Message;
                Debug.LogException(exception);
            }
        }

        public Task MoveToSampledPointAsync(CancellationToken cancellationToken = default)
        {
            var padding = Mathf.Clamp(sampleViewportPadding, 0f, 0.45f);
            var viewportPoint = new Vector2(
                UnityEngine.Random.Range(padding, 1f - padding),
                UnityEngine.Random.Range(padding, 1f - padding));
            return MoveToViewportPointAsync(viewportPoint, cancellationToken);
        }

        public Task MoveToViewportPointAsync(Vector2 viewportPoint, CancellationToken cancellationToken = default)
        {
            var modelRoot = GetCurrentModelRoot();
            var interactionCamera = GetInteractionCamera();
            var depth = boundsService.GetDepth(interactionCamera, modelRoot.transform);
            var clampedViewportPoint = new Vector2(
                Mathf.Clamp01(viewportPoint.x),
                Mathf.Clamp01(viewportPoint.y));
            var worldPosition = interactionCamera.ViewportToWorldPoint(new Vector3(
                clampedViewportPoint.x,
                clampedViewportPoint.y,
                depth));
            worldPosition.z = modelRoot.transform.position.z;
            return MoveToWorldPositionAsync(worldPosition, cancellationToken);
        }

        public async Task MoveToWorldPositionAsync(Vector3 worldPosition, CancellationToken cancellationToken = default)
        {
            var modelRoot = GetCurrentModelRoot();
            var interactionCamera = GetInteractionCamera();
            if (dragController != null && dragController.IsDragging)
            {
                throw new InvalidOperationException("Cannot start desktop movement while the pet is being dragged.");
            }

            if (rotationController != null && rotationController.IsRotating)
            {
                throw new InvalidOperationException("Cannot start desktop movement while the pet is being rotated.");
            }

            CancelCurrentMove();
            var movement = currentBehaviorPreset?.Movement ?? BehaviorMovementPreset.Default;
            var startPosition = modelRoot.transform.position;
            var targetPosition = boundsService.ClampModelWorldPosition(
                interactionCamera,
                modelRoot,
                new Vector3(worldPosition.x, worldPosition.y, startPosition.z),
                viewportPadding);
            var distance = Vector3.Distance(startPosition, targetPosition);

            if (distance <= MinimumMoveDistance)
            {
                modelRoot.transform.position = targetPosition;
                runtimeController!.SaveCurrentTransformState();
                movementStateSummary = "Movement snapped to target.";
                return;
            }

            var operationId = ++moveOperationId;
            var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            movementCancellationTokenSource = linkedCancellationTokenSource;
            var linkedCancellationToken = linkedCancellationTokenSource.Token;

            var playedMovementAnimation = false;
            IsMoving = true;
            movementStateSummary = $"{movement.Type} -> ({targetPosition.x:F2}, {targetPosition.y:F2})";

            var plan = BuildOrientationPlan(
                movement,
                interactionCamera,
                startPosition,
                targetPosition,
                distance);

            try
            {
                if (movement.Type == BehaviorMovementType.Teleport)
                {
                    await RunTeleportAsync(modelRoot, targetPosition, plan, movement, linkedCancellationToken);
                }
                else
                {
                    playedMovementAnimation = await RunTravelAsync(
                        modelRoot,
                        startPosition,
                        targetPosition,
                        plan,
                        movement,
                        linkedCancellationToken);
                }

                await RestoreNeutralPoseAsync(modelRoot, movement, playedMovementAnimation, linkedCancellationToken);
                runtimeController!.SaveCurrentTransformState();
                movementStateSummary = $"{movement.Type} completed.";
            }
            catch (OperationCanceledException)
            {
                if (operationId == moveOperationId && runtimeController?.CurrentModelRoot != null)
                {
                    try
                    {
                        await SmoothLocalOrientationAsync(
                            modelRoot.transform,
                            facingTowardUserLocalY,
                            0f,
                            0f,
                            turnToFrontSeconds * 0.75f,
                            CancellationToken.None);
                        await animationController!.ReturnToIdleAsync(CancellationToken.None);
                    }
                    catch (Exception)
                    {
                    }
                }

                movementStateSummary = "Movement cancelled.";
            }
            finally
            {
                IsMoving = false;

                if (ReferenceEquals(movementCancellationTokenSource, linkedCancellationTokenSource))
                {
                    movementCancellationTokenSource = null;
                }

                linkedCancellationTokenSource.Dispose();
            }
        }

        public void CancelCurrentMove()
        {
            if (movementCancellationTokenSource == null)
            {
                return;
            }

            movementCancellationTokenSource.Cancel();
            movementCancellationTokenSource.Dispose();
            movementCancellationTokenSource = null;
            IsMoving = false;
        }

        private readonly struct OrientationPlan
        {
            public OrientationPlan(float targetYaw, float targetLeanZ, string loopPath)
            {
                TargetYaw = targetYaw;
                TargetLeanZ = targetLeanZ;
                LoopPath = loopPath;
            }

            public float TargetYaw { get; }

            public float TargetLeanZ { get; }

            public string LoopPath { get; }
        }

        private OrientationPlan BuildOrientationPlan(
            BehaviorMovementPreset movement,
            Camera camera,
            Vector3 startWorld,
            Vector3 endWorld,
            float distance)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            var startVp = camera.WorldToViewportPoint(startWorld);
            var endVp = camera.WorldToViewportPoint(endWorld);
            var dvx = endVp.x - startVp.x;
            var dvy = endVp.y - startVp.y;
            var adx = Mathf.Abs(dvx);
            var ady = Mathf.Abs(dvy);
            var verticalDominant = ady > adx * verticalVersusHorizontalBias;

            if (!movement.FaceVelocity)
            {
                if (verticalDominant)
                {
                    var lean = distance > 0.0001f
                        ? Mathf.Sign(dvy) * verticalTravelLeanZDegrees * Mathf.Clamp01(ady / distance)
                        : 0f;
                    var loop = !string.IsNullOrWhiteSpace(movement.LoopVerticalAnimationPath)
                        ? movement.LoopVerticalAnimationPath
                        : movement.LoopAnimationPath;
                    return new OrientationPlan(facingTowardUserLocalY, lean, loop);
                }

                return new OrientationPlan(
                    facingTowardUserLocalY,
                    0f,
                    movement.LoopAnimationPath);
            }

            if (verticalDominant)
            {
                var lean = distance > 0.0001f
                    ? Mathf.Sign(dvy) * verticalTravelLeanZDegrees * Mathf.Clamp01(ady / distance)
                    : 0f;
                var loop = !string.IsNullOrWhiteSpace(movement.LoopVerticalAnimationPath)
                    ? movement.LoopVerticalAnimationPath
                    : movement.LoopAnimationPath;
                return new OrientationPlan(facingTowardUserLocalY, lean, loop);
            }

            var rightwardOnScreen = dvx >= 0f;
            var yaw = rightwardOnScreen ? profileLocalYawScreenRight : profileLocalYawScreenLeft;
            return new OrientationPlan(yaw, 0f, movement.LoopAnimationPath);
        }

        private async Task RunTeleportAsync(
            GameObject modelRoot,
            Vector3 targetPosition,
            OrientationPlan plan,
            BehaviorMovementPreset movement,
            CancellationToken cancellationToken)
        {
            await TryPlayStartAnimationAsync(movement, cancellationToken);
            await SmoothLocalOrientationAsync(
                modelRoot.transform,
                plan.TargetYaw,
                plan.TargetLeanZ,
                0f,
                turnToMoveSeconds * 0.55f,
                cancellationToken);
            modelRoot.transform.position = targetPosition;
        }

        private async Task<bool> RunTravelAsync(
            GameObject modelRoot,
            Vector3 startPosition,
            Vector3 targetPosition,
            OrientationPlan plan,
            BehaviorMovementPreset movement,
            CancellationToken cancellationToken)
        {
            var playedMovementAnimation = false;
            var hasLoop = !string.IsNullOrWhiteSpace(plan.LoopPath);

            if (!hasLoop)
            {
                movementStateSummary = $"Movement loop missing for {movement.Type}.";
                throw new InvalidOperationException(
                    $"No movement loop animation is configured for '{movement.Type}'.");
            }

            Debug.Log(
                $"[DesktopPetMovement] moveStart type={movement.Type} from={startPosition} to={targetPosition} " +
                $"yaw={plan.TargetYaw:F1} leanZ={plan.TargetLeanZ:F1} loop={plan.LoopPath}");

            await TryPlayStartAnimationAsync(movement, cancellationToken);

            var turnTask = SmoothLocalOrientationAsync(
                modelRoot.transform,
                plan.TargetYaw,
                plan.TargetLeanZ,
                0f,
                turnToMoveSeconds,
                cancellationToken);

            Task loopTask = Task.CompletedTask;
            if (hasLoop)
            {
                loopTask = StartLoopAfterLeadInAsync(plan.LoopPath, loopLeadInSeconds, cancellationToken);
            }

            await Task.WhenAll(turnTask, loopTask);
            if (hasLoop)
            {
                playedMovementAnimation = true;
            }

            await RunMovementAsync(modelRoot, startPosition, targetPosition, movement, cancellationToken);
            return playedMovementAnimation;
        }

        private async Task StartLoopAfterLeadInAsync(string loopPath, float leadInSeconds, CancellationToken cancellationToken)
        {
            if (leadInSeconds > 0.0001f)
            {
                await WaitSecondsAsync(leadInSeconds, cancellationToken);
            }

            Debug.Log($"[DesktopPetMovement] playLoop path={loopPath}");
            await animationController!.PlayLoopPathAsync(loopPath, cancellationToken);
        }

        private async Task RestoreNeutralPoseAsync(
            GameObject modelRoot,
            BehaviorMovementPreset movement,
            bool movementAnimationWasPlayed,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(movement.StopAnimationPath))
            {
                await animationController!.PlayOneShotPathAsync(
                    movement.StopAnimationPath,
                    returnToIdle: false,
                    cancellationToken);
            }

            await SmoothLocalOrientationAsync(
                modelRoot.transform,
                facingTowardUserLocalY,
                0f,
                0f,
                turnToFrontSeconds,
                cancellationToken);

            if (!movementAnimationWasPlayed && string.IsNullOrWhiteSpace(movement.StopAnimationPath))
            {
                return;
            }

            await animationController!.ReturnToIdleAsync(cancellationToken);
        }

        private async Task RunMovementAsync(
            GameObject modelRoot,
            Vector3 startPosition,
            Vector3 targetPosition,
            BehaviorMovementPreset movement,
            CancellationToken cancellationToken)
        {
            var speed = ResolveSpeed(movement);
            var duration = Mathf.Max(Vector3.Distance(startPosition, targetPosition) / speed, 0.0001f);
            var elapsed = 0f;

            while (elapsed < duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureMovementCanContinue(modelRoot);

                await Task.Yield();

                cancellationToken.ThrowIfCancellationRequested();
                EnsureMovementCanContinue(modelRoot);

                elapsed = Mathf.Min(elapsed + Time.deltaTime, duration);
                var normalizedTime = Mathf.Clamp01(elapsed / duration);
                var nextPosition = EvaluatePosition(startPosition, targetPosition, normalizedTime, movement);
                modelRoot.transform.position = nextPosition;
            }

            modelRoot.transform.position = targetPosition;
        }

        private void EnsureMovementCanContinue(GameObject modelRoot)
        {
            if (runtimeController == null
                || runtimeController.CurrentModelRoot == null
                || !ReferenceEquals(runtimeController.CurrentModelRoot, modelRoot))
            {
                throw new OperationCanceledException();
            }

            if ((dragController != null && dragController.IsDragging)
                || (rotationController != null && rotationController.IsRotating))
            {
                throw new OperationCanceledException();
            }
        }

        private async Task<bool> TryPlayStartAnimationAsync(
            BehaviorMovementPreset movement,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(movement.StartAnimationPath))
            {
                return false;
            }

            await animationController!.PlayOneShotPathAsync(
                movement.StartAnimationPath,
                returnToIdle: false,
                cancellationToken);
            return true;
        }

        private float ResolveSpeed(BehaviorMovementPreset movement)
        {
            var baseSpeed = movement.Type switch
            {
                BehaviorMovementType.Fly => flyUnitsPerSecond,
                BehaviorMovementType.Hop => hopUnitsPerSecond,
                BehaviorMovementType.Teleport => float.PositiveInfinity,
                _ => walkUnitsPerSecond,
            };
            return Mathf.Max(0.01f, baseSpeed * movement.ResolvedSpeedMultiplier);
        }

        private Vector3 EvaluatePosition(
            Vector3 startPosition,
            Vector3 targetPosition,
            float normalizedTime,
            BehaviorMovementPreset movement)
        {
            var easedTime = EaseInOutSine(normalizedTime);
            var nextPosition = Vector3.Lerp(startPosition, targetPosition, easedTime);
            if (movement.Type == BehaviorMovementType.Hop)
            {
                nextPosition.y += Mathf.Sin(easedTime * Mathf.PI) * hopArcHeight;
            }

            return nextPosition;
        }

        private async Task SmoothLocalOrientationAsync(
            Transform modelTransform,
            float targetYaw,
            float targetLeanZ,
            float targetLeanX,
            float duration,
            CancellationToken cancellationToken)
        {
            var safeDuration = Mathf.Max(duration, 0.0001f);
            var startEuler = modelTransform.localEulerAngles;
            var elapsed = 0f;

            while (elapsed < safeDuration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                elapsed = Mathf.Min(elapsed + Time.deltaTime, safeDuration);
                var t = EaseInOutSine(elapsed / safeDuration);
                var y = Mathf.LerpAngle(startEuler.y, targetYaw, t);
                var z = Mathf.LerpAngle(startEuler.z, targetLeanZ, t);
                var x = Mathf.LerpAngle(startEuler.x, targetLeanX, t);
                modelTransform.localRotation = Quaternion.Euler(x, y, z);
                await Task.Yield();
            }

            modelTransform.localRotation = Quaternion.Euler(targetLeanX, targetYaw, targetLeanZ);
        }

        private static async Task WaitSecondsAsync(float seconds, CancellationToken cancellationToken)
        {
            var t = 0f;
            while (t < seconds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                t += Time.deltaTime;
                await Task.Yield();
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

            runtimeController.ModelCleared += HandleModelCleared;
            runtimeController.ModelLoaded += HandleModelLoaded;
            isSubscribedToRuntimeController = true;
        }

        private void HandleModelCleared()
        {
            CancelCurrentMove();
        }

        private void HandleModelLoaded(ModelLoadResult _)
        {
            CancelCurrentMove();
        }

        private GameObject GetCurrentModelRoot()
        {
            if (runtimeController?.CurrentModelRoot == null)
            {
                throw new InvalidOperationException("A loaded desktop pet model is required before moving the pet.");
            }

            return runtimeController.CurrentModelRoot;
        }

        private Camera GetInteractionCamera()
        {
            if (runtimeController?.InteractionCamera == null)
            {
                throw new InvalidOperationException("An interaction camera is required before moving the pet.");
            }

            return runtimeController.InteractionCamera;
        }

        private static float EaseInOutSine(float value)
        {
            var clampedValue = Mathf.Clamp01(value);
            return 0.5f - (Mathf.Cos(clampedValue * Mathf.PI) * 0.5f);
        }
    }
}
