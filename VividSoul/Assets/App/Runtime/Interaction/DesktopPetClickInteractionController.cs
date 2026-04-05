#nullable enable

using UnityEngine;
using VividSoul.Runtime.Animation;
using VividSoul.Runtime.App;

namespace VividSoul.Runtime.Interaction
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DesktopPetRuntimeController))]
    [RequireComponent(typeof(DesktopPetAnimationController))]
    public sealed class DesktopPetClickInteractionController : MonoBehaviour
    {
        [SerializeField] private bool enableClickAnimation = false;
        [SerializeField] private int mouseButton = 0;
        [SerializeField] private float maxClickDistance = 8f;

        private DesktopPetAnimationController? animationController;
        private DesktopPetBoundsService? boundsService;
        private DesktopPetRuntimeController? runtimeController;
        private Vector3 pressedMousePosition;
        private bool isPressedOnModel;

        private void Awake()
        {
            animationController = GetComponent<DesktopPetAnimationController>();
            boundsService = new DesktopPetBoundsService();
            runtimeController = GetComponent<DesktopPetRuntimeController>();
        }

        private void Update()
        {
            if (!enableClickAnimation)
            {
                isPressedOnModel = false;
                return;
            }

            if (runtimeController == null || animationController == null || boundsService == null)
            {
                return;
            }

            var currentModelRoot = runtimeController.CurrentModelRoot;
            var interactionCamera = runtimeController.InteractionCamera;
            if (currentModelRoot == null || interactionCamera == null || runtimeController.IsModelInteractionBlocked)
            {
                isPressedOnModel = false;
                return;
            }

            if (Input.GetMouseButtonDown(mouseButton))
            {
                isPressedOnModel = boundsService.ContainsScreenPoint(interactionCamera, currentModelRoot, Input.mousePosition);
                pressedMousePosition = Input.mousePosition;
                return;
            }

            if (!isPressedOnModel || !Input.GetMouseButtonUp(mouseButton))
            {
                return;
            }

            isPressedOnModel = false;
            if (!animationController.HasClickAnimation)
            {
                return;
            }

            if (!boundsService.ContainsScreenPoint(interactionCamera, currentModelRoot, Input.mousePosition))
            {
                return;
            }

            var movement = Vector2.Distance(pressedMousePosition, Input.mousePosition);
            if (movement > maxClickDistance)
            {
                return;
            }

            runtimeController.PlayClickAnimation();
        }
    }
}
