#nullable enable

using UnityEngine;
using VividSoul.Runtime.App;

namespace VividSoul.Runtime.Interaction
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DesktopPetRuntimeController))]
    public sealed class DesktopPetDragController : MonoBehaviour
    {
        [SerializeField] private int mouseButton = 0;
        [SerializeField, Range(0f, 0.25f)] private float viewportPadding = 0.02f;

        private DesktopPetBoundsService? boundsService;
        private DesktopPetRuntimeController? runtimeController;
        private Vector3 grabOffset;

        public bool IsDragging { get; private set; }

        private void Awake()
        {
            boundsService = new DesktopPetBoundsService();
            runtimeController = GetComponent<DesktopPetRuntimeController>();
        }

        private void Update()
        {
            if (runtimeController == null || boundsService == null)
            {
                return;
            }

            var currentModelRoot = runtimeController.CurrentModelRoot;
            var interactionCamera = runtimeController.InteractionCamera;
            if (currentModelRoot == null || interactionCamera == null || runtimeController.IsModelInteractionBlocked)
            {
                if (IsDragging)
                {
                    EndDrag();
                }

                return;
            }

            if (!IsDragging)
            {
                if (!Input.GetMouseButtonDown(mouseButton))
                {
                    return;
                }

                if (!boundsService.ContainsScreenPoint(interactionCamera, currentModelRoot, Input.mousePosition))
                {
                    return;
                }

                BeginDrag(interactionCamera, currentModelRoot.transform);
                return;
            }

            if (!Input.GetMouseButton(mouseButton))
            {
                EndDrag();
                return;
            }

            var depth = boundsService.GetDepth(interactionCamera, currentModelRoot.transform);
            var pointerWorldPosition = interactionCamera.ScreenToWorldPoint(new Vector3(
                Input.mousePosition.x,
                Input.mousePosition.y,
                depth));
            var targetPosition = pointerWorldPosition + grabOffset;
            currentModelRoot.transform.position = boundsService.ClampModelWorldPosition(
                interactionCamera,
                currentModelRoot,
                targetPosition,
                viewportPadding);
        }

        private void BeginDrag(Camera interactionCamera, Transform modelTransform)
        {
            var depth = boundsService!.GetDepth(interactionCamera, modelTransform);
            var pointerWorldPosition = interactionCamera.ScreenToWorldPoint(new Vector3(
                Input.mousePosition.x,
                Input.mousePosition.y,
                depth));

            grabOffset = modelTransform.position - pointerWorldPosition;
            IsDragging = true;
        }

        private void EndDrag()
        {
            IsDragging = false;
            runtimeController!.SaveCurrentTransformState();
        }
    }
}
