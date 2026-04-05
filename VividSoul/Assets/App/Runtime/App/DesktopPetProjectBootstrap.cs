#nullable enable

using UnityEngine;
using UnityEngine.Rendering;
using VividSoul.Runtime.Animation;
using VividSoul.Runtime.Interaction;
using VividSoul.Runtime.Movement;

namespace VividSoul.Runtime.App
{
    public sealed class DesktopPetProjectBootstrap : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntimeExists()
        {
            ConfigureApplication();
            EnsureSceneCameraExists();
            EnsureSceneLightExists();

            var existingRuntime = Object.FindFirstObjectByType<DesktopPetRuntimeController>();
            if (existingRuntime != null)
            {
                EnsureRuntimeComponents(existingRuntime.gameObject);
                return;
            }

            var runtimeObject = new GameObject("DesktopPetRuntime");
            runtimeObject.SetActive(false);
            EnsureRuntimeComponents(runtimeObject);
            runtimeObject.SetActive(true);
        }

        private static void ConfigureApplication()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            Screen.fullScreenMode = FullScreenMode.Windowed;
            Screen.fullScreen = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.30f, 0.32f, 0.36f, 1f);
        }

        private static void EnsureSceneCameraExists()
        {
            if (Camera.main != null)
            {
                ConfigureCamera(Camera.main);
                return;
            }

            var existingCamera = Object.FindFirstObjectByType<Camera>();
            if (existingCamera != null)
            {
                existingCamera.tag = "MainCamera";
                ConfigureCamera(existingCamera);
                return;
            }

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";

            var camera = cameraObject.AddComponent<Camera>();
            ConfigureCamera(camera);
        }

        private static void EnsureSceneLightExists()
        {
            var existingLight = Object.FindFirstObjectByType<Light>();
            if (existingLight != null)
            {
                ConfigureLight(existingLight);
                return;
            }

            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            ConfigureLight(light);
        }

        private static void ConfigureCamera(Camera camera)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.allowHDR = false;
            camera.orthographic = true;
            camera.orthographicSize = 1.6f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.transform.rotation = Quaternion.identity;
        }

        private static void ConfigureLight(Light light)
        {
            light.type = LightType.Directional;
            light.intensity = 0.64f;
            light.color = new Color(1f, 0.96f, 0.92f, 1f);
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(35f, -25f, 0f);
        }

        private static void EnsureRuntimeComponents(GameObject runtimeObject)
        {
            AddComponentIfMissing<DesktopPetAnimationController>(runtimeObject);
            AddComponentIfMissing<DesktopPetFallbackMotionController>(runtimeObject);
            AddComponentIfMissing<DesktopPetMovementController>(runtimeObject);
            AddComponentIfMissing<DesktopPetDragController>(runtimeObject);
            AddComponentIfMissing<DesktopPetScaleController>(runtimeObject);
            AddComponentIfMissing<DesktopPetRotationController>(runtimeObject);
            AddComponentIfMissing<DesktopPetClickInteractionController>(runtimeObject);
            AddComponentIfMissing<DesktopPetRuntimeHud>(runtimeObject);
            AddComponentIfMissing<DesktopPetRuntimeController>(runtimeObject);
        }

        private static T AddComponentIfMissing<T>(GameObject target)
            where T : Component
        {
            var existing = target.GetComponent<T>();
            if (existing != null)
            {
                return existing;
            }

            return target.AddComponent<T>();
        }
    }
}
