#nullable enable

using UnityEngine;
using VividSoul.Runtime.App;

namespace VividSoul.Runtime.Interaction
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DesktopPetRuntimeController))]
    public sealed class DesktopPetDragController : MonoBehaviour
    {
        private const float EdgeDragViewportPadding = 0f;

        [SerializeField] private int mouseButton = 0;

        private DesktopPetBoundsService? boundsService;
        private DesktopPetRuntimeController? runtimeController;
        private float nextDiagnosticsAtTime;
        private Vector2 previousGlobalCursorPosition;

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

                var containsScreenPoint = boundsService.ContainsScreenPoint(interactionCamera, currentModelRoot, Input.mousePosition);
                if (!containsScreenPoint)
                {
                    return;
                }

                runtimeController.ClampCurrentWindowPositionToMonitor();
                BeginDrag();
                return;
            }

            if (!Input.GetMouseButton(mouseButton))
            {
                EndDrag();
                return;
            }

            var currentGlobalCursorPosition = runtimeController.GetGlobalCursorPosition();
            var globalCursorDelta = currentGlobalCursorPosition - previousGlobalCursorPosition;
            if (globalCursorDelta.sqrMagnitude > 0f)
            {
                var currentWindowPosition = runtimeController.ClampCurrentWindowPositionToMonitor();
                var desiredWindowPosition = currentWindowPosition + globalCursorDelta;
                var clampedWindowPosition = runtimeController.ClampWindowPositionToMonitor(desiredWindowPosition);
                runtimeController.SetWindowPosition(clampedWindowPosition);

                var residualWindowDelta = desiredWindowPosition - clampedWindowPosition;
                var modelScreenDelta = ConvertWindowDeltaToScreenDelta(residualWindowDelta);
                if (modelScreenDelta.sqrMagnitude > 0f)
                {
                    MoveModelWithinWindow(interactionCamera, currentModelRoot, modelScreenDelta);
                    LogEdgeDiagnostics(interactionCamera, currentModelRoot, residualWindowDelta, modelScreenDelta);
                }
            }

            previousGlobalCursorPosition = currentGlobalCursorPosition;
        }

        private void BeginDrag()
        {
            previousGlobalCursorPosition = runtimeController!.GetGlobalCursorPosition();
            IsDragging = true;
        }

        private void EndDrag()
        {
            IsDragging = false;
            runtimeController!.SaveCurrentTransformState();
            runtimeController!.SaveCurrentWindowPosition();
        }

        private void MoveModelWithinWindow(
            Camera interactionCamera,
            GameObject currentModelRoot,
            Vector2 screenDelta)
        {
            currentModelRoot.transform.position = boundsService!.MoveModelByScreenDelta(
                interactionCamera,
                currentModelRoot,
                screenDelta,
                EdgeDragViewportPadding);
        }

        private Vector2 ConvertWindowDeltaToScreenDelta(Vector2 windowDelta)
        {
            var clientSize = runtimeController!.GetWindowClientSize();
            return new Vector2(
                clientSize.x > Mathf.Epsilon ? windowDelta.x * (Screen.width / clientSize.x) : windowDelta.x,
                clientSize.y > Mathf.Epsilon ? windowDelta.y * (Screen.height / clientSize.y) : windowDelta.y);
        }

        private void LogEdgeDiagnostics(
            Camera interactionCamera,
            GameObject currentModelRoot,
            Vector2 residualWindowDelta,
            Vector2 modelScreenDelta)
        {
            var shouldLogRight = residualWindowDelta.x > 0.25f;
            var shouldLogTop = residualWindowDelta.y > 0.25f;
            if ((!shouldLogRight && !shouldLogTop) || Time.unscaledTime < nextDiagnosticsAtTime)
            {
                return;
            }

            nextDiagnosticsAtTime = Time.unscaledTime + 0.25f;
            if (!boundsService!.TryGetScreenRect(interactionCamera, currentModelRoot, out var screenRect))
            {
                return;
            }

            var clientSize = runtimeController!.GetWindowClientSize();
            var windowPosition = runtimeController.GetWindowPosition();
            if (shouldLogRight)
            {
                var contributors = Screen.width - screenRect.xMax <= 0.5f
                    ? boundsService.DescribeRightEdgeContributors(interactionCamera, currentModelRoot)
                    : "n/a";
                Debug.Log(
                    $"[DesktopPetDrag] right residualX={residualWindowDelta.x:F2} modelDeltaX={modelScreenDelta.x:F2} gapRight={(Screen.width - screenRect.xMax):F2} "
                    + $"screenRect=({screenRect.xMin:F2},{screenRect.xMax:F2},{screenRect.width:F2}) "
                    + $"windowPos=({windowPosition.x:F2},{windowPosition.y:F2}) "
                    + $"client=({clientSize.x:F2},{clientSize.y:F2}) screen=({Screen.width},{Screen.height}) "
                    + $"contributors={contributors}");
            }

            if (shouldLogTop)
            {
                Debug.Log(
                    $"[DesktopPetDrag] top residualY={residualWindowDelta.y:F2} modelDeltaY={modelScreenDelta.y:F2} gapTop={(Screen.height - screenRect.yMax):F2} "
                    + $"screenRect=({screenRect.yMin:F2},{screenRect.yMax:F2},{screenRect.height:F2}) "
                    + $"windowPos=({windowPosition.x:F2},{windowPosition.y:F2}) "
                    + $"client=({clientSize.x:F2},{clientSize.y:F2}) screen=({Screen.width},{Screen.height})");
            }
        }
    }
}
