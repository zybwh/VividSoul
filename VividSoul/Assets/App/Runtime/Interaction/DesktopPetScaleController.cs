#nullable enable

using UnityEngine;
using VividSoul.Runtime.App;

namespace VividSoul.Runtime.Interaction
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DesktopPetRuntimeController))]
    public sealed class DesktopPetScaleController : MonoBehaviour
    {
        [SerializeField] private float scrollSensitivity = 0.1f;
        [SerializeField] private float minScale = 0.5f;
        [SerializeField] private float maxScale = 2.5f;
        [SerializeField, Range(0.02f, 0.4f)] private float minViewportWidthRatio = 0.08f;
        [SerializeField, Range(0.05f, 0.6f)] private float minViewportHeightRatio = 0.18f;
        [SerializeField, Range(0.25f, 0.95f)] private float maxViewportWidthRatio = 0.5f;
        [SerializeField, Range(0.25f, 0.95f)] private float maxViewportHeightRatio = 0.78f;
        [SerializeField, Range(0f, 0.25f)] private float viewportPadding = 0.02f;
        [SerializeField] private bool requirePointerOverModel = true;

        private DesktopPetBoundsService? boundsService;
        private DesktopPetDragController? dragController;
        private DesktopPetRotationController? rotationController;
        private DesktopPetRuntimeController? runtimeController;
        private Vector2Int lastScreenSize;

        private void Awake()
        {
            boundsService = new DesktopPetBoundsService();
            dragController = GetComponent<DesktopPetDragController>();
            rotationController = GetComponent<DesktopPetRotationController>();
            runtimeController = GetComponent<DesktopPetRuntimeController>();
            lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        }

        private void Update()
        {
            if (runtimeController == null || boundsService == null)
            {
                return;
            }

            var currentScreenSize = new Vector2Int(Screen.width, Screen.height);
            if (currentScreenSize != lastScreenSize)
            {
                lastScreenSize = currentScreenSize;
                ConstrainCurrentModelTransform(persistState: true);
            }

            if (dragController != null && dragController.IsDragging)
            {
                return;
            }

            if (rotationController != null && rotationController.IsRotating)
            {
                return;
            }

            var currentModelRoot = runtimeController.CurrentModelRoot;
            var interactionCamera = runtimeController.InteractionCamera;
            if (currentModelRoot == null || interactionCamera == null || runtimeController.IsModelInteractionBlocked)
            {
                return;
            }

            var scrollDelta = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scrollDelta) <= Mathf.Epsilon)
            {
                return;
            }

            if (requirePointerOverModel
                && !boundsService.ContainsScreenPoint(interactionCamera, currentModelRoot, Input.mousePosition))
            {
                return;
            }

            var currentScale = currentModelRoot.transform.localScale.x;
            var scaleLimits = boundsService.GetModelScaleLimits(
                interactionCamera,
                currentModelRoot,
                currentScale,
                minScale,
                maxScale,
                minViewportWidthRatio,
                minViewportHeightRatio,
                maxViewportWidthRatio,
                maxViewportHeightRatio);
            var nextScale = Mathf.Clamp(currentScale + (scrollDelta * scrollSensitivity), scaleLimits.MinScale, scaleLimits.MaxScale);
            if (Mathf.Abs(nextScale - currentScale) <= Mathf.Epsilon)
            {
                return;
            }

            currentModelRoot.transform.localScale = Vector3.one * nextScale;
            currentModelRoot.transform.position = boundsService.ClampModelWorldPosition(
                interactionCamera,
                currentModelRoot,
                currentModelRoot.transform.position,
                viewportPadding);
            runtimeController.SaveCurrentTransformState();
        }

        public void ConstrainCurrentModelTransform(bool persistState = false)
        {
            if (runtimeController == null || boundsService == null)
            {
                return;
            }

            var currentModelRoot = runtimeController.CurrentModelRoot;
            var interactionCamera = runtimeController.InteractionCamera;
            if (currentModelRoot == null || interactionCamera == null)
            {
                return;
            }

            var currentScale = currentModelRoot.transform.localScale.x;
            var scaleLimits = boundsService.GetModelScaleLimits(
                interactionCamera,
                currentModelRoot,
                currentScale,
                minScale,
                maxScale,
                minViewportWidthRatio,
                minViewportHeightRatio,
                maxViewportWidthRatio,
                maxViewportHeightRatio);
            var clampedScale = Mathf.Clamp(currentScale, scaleLimits.MinScale, scaleLimits.MaxScale);
            if (Mathf.Abs(clampedScale - currentScale) > Mathf.Epsilon)
            {
                currentModelRoot.transform.localScale = Vector3.one * clampedScale;
            }

            currentModelRoot.transform.position = boundsService.ClampModelWorldPosition(
                interactionCamera,
                currentModelRoot,
                currentModelRoot.transform.position,
                viewportPadding);

            if (persistState)
            {
                runtimeController.SaveCurrentTransformState();
            }
        }
    }
}
