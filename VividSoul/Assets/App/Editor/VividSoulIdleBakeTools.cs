#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UniGLTF;
using UniVRM10;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;
using VividSoul.Runtime.Animation;

namespace VividSoul.Editor
{
    public static class VividSoulIdleBakeTools
    {
        private const string ExportRootDirectoryName = "Exports";
        private const string ExportProjectDirectoryName = "VividSoul";
        private const string GeneratedDirectoryName = "generated";
        private const string GlbDirectoryName = "glb";
        private const string VrmaDirectoryName = "vrma";
        private const string OutputBaseName = "Idle_UpperBody_Base_10s";
        private const int SampleFrameRate = 30;
        private const float SampleDurationSeconds = 10f;
        private const string DefaultModelRelativePath = "Defaults/Models/3822753043679029706.vrm";
        private const string DefaultPoseRelativePath = "Defaults/Animations/VRMA_MotionPack/VRMA_01.vrma";

        private static readonly HumanBodyBones[] DynamicBones =
        {
            HumanBodyBones.Spine,
            HumanBodyBones.Chest,
            HumanBodyBones.UpperChest,
            HumanBodyBones.Neck,
            HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm,
            HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftLowerArm,
            HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftHand,
            HumanBodyBones.RightHand,
        };

        [MenuItem("VividSoul/Export/Bake Idle Base Assets")]
        public static void BakeIdleBaseAssets()
        {
            ExportIdleBaseAssetsCore();
        }

        public static void ExportIdleBaseAssetsBatch()
        {
            ExportIdleBaseAssetsCore();
        }

        private static void ExportIdleBaseAssetsCore()
        {
            Debug.Log("Idle bake export: start");
            var idleProfile = ReadIdleProfile();
            var modelPath = GetAbsoluteStreamingAssetPath(DefaultModelRelativePath);
            var posePath = GetAbsoluteStreamingAssetPath(DefaultPoseRelativePath);
            EnsureFileExists(modelPath, nameof(modelPath));
            EnsureFileExists(posePath, nameof(posePath));

            var glbOutputDirectory = GetOutputDirectory(GlbDirectoryName);
            var vrmaOutputDirectory = GetOutputDirectory(VrmaDirectoryName);
            Directory.CreateDirectory(glbOutputDirectory);
            Directory.CreateDirectory(vrmaOutputDirectory);
            var glbPath = Path.Combine(glbOutputDirectory, $"{OutputBaseName}.glb");
            var vrmaPath = Path.Combine(vrmaOutputDirectory, $"{OutputBaseName}.vrma");

            Vrm10Instance? sourceModel = null;
            Vrm10Instance? exportModel = null;
            AnimationClip? clip = null;

            try
            {
                Debug.Log($"Idle bake export: loading source model from '{modelPath}'");
                sourceModel = LoadModel(modelPath, ControlRigGenerationOption.Generate);
                Debug.Log("Idle bake export: sampling default pose");
                var defaultPose = CaptureDefaultPose(sourceModel, posePath, idleProfile);

                Debug.Log("Idle bake export: loading export model");
                exportModel = LoadModel(modelPath, ControlRigGenerationOption.None);
                var exportBoneMap = CreateBoneMap(exportModel);
                Debug.Log("Idle bake export: creating animation clip");
                clip = CreateIdleAnimationClip(exportModel.gameObject, exportBoneMap, defaultPose, idleProfile);

                RestoreBasePose(exportBoneMap);
                Debug.Log($"Idle bake export: exporting glb to '{glbPath}'");
                ExportGlb(exportModel.gameObject, clip, glbPath);

                ApplyIdlePose(exportBoneMap, defaultPose, idleProfile, 0f);
                Debug.Log($"Idle bake export: exporting vrma to '{vrmaPath}'");
                ExportVrma(exportModel.gameObject, exportBoneMap, defaultPose, idleProfile, vrmaPath);

                Debug.Log($"Idle assets exported to '{glbOutputDirectory}' and '{vrmaOutputDirectory}'.");
            }
            finally
            {
                if (clip != null)
                {
                    UnityEngine.Object.DestroyImmediate(clip);
                }

                if (sourceModel != null)
                {
                    UnityEngine.Object.DestroyImmediate(sourceModel.gameObject);
                }

                if (exportModel != null)
                {
                    UnityEngine.Object.DestroyImmediate(exportModel.gameObject);
                }
            }
        }

        private static Vrm10Instance LoadModel(string path, ControlRigGenerationOption controlRigGenerationOption)
        {
            return Vrm10.LoadPathAsync(
                path,
                canLoadVrm0X: true,
                controlRigGenerationOption: controlRigGenerationOption,
                showMeshes: true,
                awaitCaller: new ImmediateCaller(),
                materialGenerator: new BuiltInVrm10MaterialDescriptorGenerator()).Result;
        }

        private static IdlePoseSample CaptureDefaultPose(Vrm10Instance sourceModel, string posePath, IdleProfile idleProfile)
        {
            Vrm10AnimationInstance? animationInstance = null;
            ITimeControl? timeControl = null;
            var basePose = IdlePoseSample.FromBoneMap(CreateBoneMap(sourceModel));

            try
            {
                animationInstance = LoadAnimationImmediate(posePath);
                animationInstance.transform.SetParent(sourceModel.transform, false);
                if (animationInstance.TryGetComponent<UnityEngine.Animation>(out var animation) == false || animation.clip == null)
                {
                    throw new InvalidOperationException("The default pose VRMA does not contain a playable animation clip.");
                }

                timeControl = animationInstance as ITimeControl
                              ?? throw new InvalidOperationException("The default pose VRMA does not expose timeline time control.");

                var duration = Mathf.Max(animation.clip.length, 0.0001f);
                var sampleTime = Mathf.Clamp01(idleProfile.DefaultPoseSampleNormalizedTime) * duration;

                timeControl.OnControlTimeStart();
                timeControl.SetTime(sampleTime);
                sourceModel.Runtime.VrmAnimation = animationInstance;
                sourceModel.Runtime.Process();

                return IdlePoseSample
                    .FromBoneMap(CreateBoneMap(sourceModel))
                    .MergeWithBaseLowerBody(basePose);
            }
            finally
            {
                if (ReferenceEquals(sourceModel.Runtime.VrmAnimation, animationInstance))
                {
                    sourceModel.Runtime.VrmAnimation = null;
                }

                timeControl?.OnControlTimeStop();
                if (animationInstance != null)
                {
                    UnityEngine.Object.DestroyImmediate(animationInstance.gameObject);
                }
            }
        }

        private static Vrm10AnimationInstance LoadAnimationImmediate(string path)
        {
            var bytes = File.ReadAllBytes(path);
            using var gltfData = new GlbLowLevelParser(path, bytes).Parse();
            using var loader = new VrmAnimationImporter(gltfData);
            var loadTask = loader.LoadAsync(new ImmediateCaller());
            if (!loadTask.IsCompleted)
            {
                throw new InvalidOperationException("Immediate VRMA loading did not complete synchronously.");
            }

            var gltfInstance = loadTask.Result;
            if (!gltfInstance.TryGetComponent<Vrm10AnimationInstance>(out var animationInstance))
            {
                throw new InvalidOperationException("Failed to create a VRMA runtime instance.");
            }

            animationInstance.ShowBoxMan(false);
            animationInstance.gameObject.name = $"VRMA:{Path.GetFileNameWithoutExtension(path)}";
            return animationInstance;
        }

        private static AnimationClip CreateIdleAnimationClip(
            GameObject exportRoot,
            IReadOnlyDictionary<HumanBodyBones, BoneTransformInfo> boneMap,
            IdlePoseSample defaultPose,
            IdleProfile idleProfile)
        {
            var clip = new AnimationClip
            {
                name = OutputBaseName,
                frameRate = SampleFrameRate,
                legacy = true,
                wrapMode = WrapMode.Loop,
            };

            var sampleCount = Mathf.RoundToInt(SampleDurationSeconds * SampleFrameRate);
            var dynamicBoneSet = new HashSet<HumanBodyBones>(DynamicBones);

            foreach (var entry in boneMap)
            {
                if (!defaultPose.Bones.TryGetValue(entry.Key, out var pose))
                {
                    continue;
                }

                if (dynamicBoneSet.Contains(entry.Key))
                {
                    var rotationX = new AnimationCurve();
                    var rotationY = new AnimationCurve();
                    var rotationZ = new AnimationCurve();
                    var rotationW = new AnimationCurve();

                    for (var sampleIndex = 0; sampleIndex <= sampleCount; sampleIndex++)
                    {
                        var sampleTime = sampleIndex / (float)SampleFrameRate;
                        ApplyIdlePose(boneMap, defaultPose, idleProfile, sampleTime);
                        var sampledRotation = entry.Value.Transform.localRotation;

                        rotationX.AddKey(sampleTime, sampledRotation.x);
                        rotationY.AddKey(sampleTime, sampledRotation.y);
                        rotationZ.AddKey(sampleTime, sampledRotation.z);
                        rotationW.AddKey(sampleTime, sampledRotation.w);
                    }

                    SetRotationCurves(clip, entry.Value.Path, rotationX, rotationY, rotationZ, rotationW);
                    continue;
                }

                var constantRotationX = CreateConstantCurve(0f, SampleDurationSeconds, pose.LocalRotation.x);
                var constantRotationY = CreateConstantCurve(0f, SampleDurationSeconds, pose.LocalRotation.y);
                var constantRotationZ = CreateConstantCurve(0f, SampleDurationSeconds, pose.LocalRotation.z);
                var constantRotationW = CreateConstantCurve(0f, SampleDurationSeconds, pose.LocalRotation.w);
                SetRotationCurves(clip, entry.Value.Path, constantRotationX, constantRotationY, constantRotationZ, constantRotationW);

                if (entry.Key == HumanBodyBones.Hips)
                {
                    SetPositionCurves(
                        clip,
                        entry.Value.Path,
                        CreateConstantCurve(0f, SampleDurationSeconds, pose.LocalPosition.x),
                        CreateConstantCurve(0f, SampleDurationSeconds, pose.LocalPosition.y),
                        CreateConstantCurve(0f, SampleDurationSeconds, pose.LocalPosition.z));
                }
            }

            clip.EnsureQuaternionContinuity();
            RestoreBasePose(boneMap);

            var animationComponent = exportRoot.GetComponent<UnityEngine.Animation>() ?? exportRoot.AddComponent<UnityEngine.Animation>();
            animationComponent.playAutomatically = false;
            animationComponent.AddClip(clip, clip.name);
            animationComponent.clip = clip;
            return clip;
        }

        private static void ExportGlb(GameObject exportRoot, AnimationClip clip, string glbPath)
        {
            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip));
            }

            var data = new ExportingGltfData();
            using (var exporter = new gltfExporter(
                       data,
                       new GltfExportSettings(),
                       animationExporter: new ClipAnimationExporter(),
                       materialExporter: new BuiltInGltfMaterialExporter(),
                       textureSerializer: new RuntimeTextureSerializer()))
            {
                exporter.Prepare(exportRoot);
                exporter.Export();
            }

            File.WriteAllBytes(glbPath, data.ToGlbBytes());
        }

        private static void ExportVrma(
            GameObject exportRoot,
            IReadOnlyDictionary<HumanBodyBones, BoneTransformInfo> boneMap,
            IdlePoseSample defaultPose,
            IdleProfile idleProfile,
            string vrmaPath)
        {
            if (!boneMap.TryGetValue(HumanBodyBones.Hips, out var hips))
            {
                throw new InvalidOperationException("The exported model does not expose a humanoid hips bone.");
            }

            var data = new ExportingGltfData();
            using var exporter = new VrmAnimationExporter(data, new GltfExportSettings());
            exporter.Prepare(exportRoot);
            exporter.SetPositionBoneAndParent(hips.Transform, exportRoot.transform);

            foreach (var entry in boneMap.Where(static entry => entry.Key != HumanBodyBones.LastBone))
            {
                var parent = ResolveExportParent(boneMap, entry.Key) ?? exportRoot.transform;
                exporter.AddRotationBoneAndParent(entry.Key, entry.Value.Transform, parent);
            }

            var sampleCount = Mathf.RoundToInt(SampleDurationSeconds * SampleFrameRate);
            exporter.Export(vrma =>
            {
                for (var sampleIndex = 0; sampleIndex <= sampleCount; sampleIndex++)
                {
                    var sampleTime = sampleIndex / (float)SampleFrameRate;
                    ApplyIdlePose(boneMap, defaultPose, idleProfile, sampleTime);
                    vrma.AddFrame(TimeSpan.FromSeconds(sampleTime));
                }
            });

            File.WriteAllBytes(vrmaPath, data.ToGlbBytes());
            RestoreBasePose(boneMap);
        }

        private static void ApplyIdlePose(
            IReadOnlyDictionary<HumanBodyBones, BoneTransformInfo> boneMap,
            IdlePoseSample defaultPose,
            IdleProfile idleProfile,
            float sampleTime)
        {
            foreach (var entry in boneMap)
            {
                if (!defaultPose.Bones.TryGetValue(entry.Key, out var pose))
                {
                    continue;
                }

                entry.Value.Transform.localRotation = pose.LocalRotation;
                if (entry.Key == HumanBodyBones.Hips)
                {
                    entry.Value.Transform.localPosition = pose.LocalPosition;
                }
            }

            var breath = Mathf.Sin(sampleTime * idleProfile.ChestBreathFrequency * Mathf.PI * 2f) * idleProfile.ChestBreathAmplitude;
            var headYaw = Mathf.Sin(sampleTime * idleProfile.HeadYawFrequency * Mathf.PI * 2f) * idleProfile.HeadYawAmplitude;
            var torsoRoll = Mathf.Sin(sampleTime * idleProfile.HorizontalSwayFrequency * Mathf.PI * 2f) * idleProfile.HorizontalSwayAmplitude * 70f;
            var torsoPitch = Mathf.Sin(sampleTime * idleProfile.VerticalBobFrequency * Mathf.PI * 2f) * idleProfile.VerticalBobAmplitude * 35f;
            var shoulderRoll = Mathf.Sin((sampleTime * idleProfile.HorizontalSwayFrequency * Mathf.PI * 2f) + (Mathf.PI * 0.35f))
                               * idleProfile.HorizontalSwayAmplitude
                               * 110f;
            var handPitch = Mathf.Sin((sampleTime * idleProfile.VerticalBobFrequency * Mathf.PI * 2f) + (Mathf.PI * 0.5f))
                            * idleProfile.VerticalBobAmplitude
                            * 50f;

            foreach (var bone in DynamicBones)
            {
                if (!boneMap.TryGetValue(bone, out var boneInfo) || !defaultPose.Bones.TryGetValue(bone, out var pose))
                {
                    continue;
                }

                boneInfo.Transform.localRotation = EvaluateIdleRotation(
                    bone,
                    pose.LocalRotation,
                    breath,
                    headYaw,
                    torsoRoll,
                    torsoPitch,
                    shoulderRoll,
                    handPitch);
            }
        }

        private static Quaternion EvaluateIdleRotation(
            HumanBodyBones bone,
            Quaternion baseRotation,
            float breath,
            float headYaw,
            float torsoRoll,
            float torsoPitch,
            float shoulderRoll,
            float handPitch)
        {
            return baseRotation * (bone switch
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
            });
        }

        private static IReadOnlyDictionary<HumanBodyBones, BoneTransformInfo> CreateBoneMap(Vrm10Instance instance)
        {
            var animator = instance.GetComponent<Animator>() ?? instance.GetComponentInChildren<Animator>();
            var map = new Dictionary<HumanBodyBones, BoneTransformInfo>();

            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone)
                {
                    continue;
                }

                Transform? transform = null;
                if (instance.TryGetBoneTransform(bone, out var vrmBone))
                {
                    transform = vrmBone;
                }
                else if (animator != null && animator.avatar != null && animator.avatar.isHuman)
                {
                    transform = animator.GetBoneTransform(bone);
                }

                if (transform == null)
                {
                    continue;
                }

                map[bone] = new BoneTransformInfo(
                    bone,
                    transform,
                    AnimationUtility.CalculateTransformPath(transform, instance.transform),
                    transform.localRotation,
                    transform.localPosition);
            }

            return map;
        }

        private static void RestoreBasePose(IReadOnlyDictionary<HumanBodyBones, BoneTransformInfo> boneMap)
        {
            foreach (var entry in boneMap.Values)
            {
                entry.Transform.localRotation = entry.BaseLocalRotation;
                if (entry.Bone == HumanBodyBones.Hips)
                {
                    entry.Transform.localPosition = entry.BaseLocalPosition;
                }
            }
        }

        private static Transform? ResolveExportParent(
            IReadOnlyDictionary<HumanBodyBones, BoneTransformInfo> boneMap,
            HumanBodyBones bone)
        {
            var current = Vrm10HumanoidBoneSpecification.ConvertFromUnityBone(bone);
            while (current != Vrm10HumanoidBones.Hips)
            {
                var parentBoneValue = Vrm10HumanoidBoneSpecification.GetDefine(current).ParentBone;
                if (!parentBoneValue.HasValue)
                {
                    return null;
                }

                var parentBone = parentBoneValue.Value;
                var unityParentBone = Vrm10HumanoidBoneSpecification.ConvertToUnityBone(parentBone);
                if (boneMap.TryGetValue(unityParentBone, out var parentInfo))
                {
                    return parentInfo.Transform;
                }

                current = parentBone;
            }

            return null;
        }

        private static void SetRotationCurves(
            AnimationClip clip,
            string path,
            AnimationCurve xCurve,
            AnimationCurve yCurve,
            AnimationCurve zCurve,
            AnimationCurve wCurve)
        {
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation.x"), xCurve);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation.y"), yCurve);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation.z"), zCurve);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation.w"), wCurve);
        }

        private static void SetPositionCurves(
            AnimationClip clip,
            string path,
            AnimationCurve xCurve,
            AnimationCurve yCurve,
            AnimationCurve zCurve)
        {
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.x"), xCurve);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.y"), yCurve);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.z"), zCurve);
        }

        private static AnimationCurve CreateConstantCurve(float startTime, float endTime, float value)
        {
            return new AnimationCurve(
                new Keyframe(startTime, value),
                new Keyframe(endTime, value));
        }

        private static IdleProfile ReadIdleProfile()
        {
            var temporaryObject = new GameObject("IdleBakeProfileProbe");
            try
            {
                var controller = temporaryObject.AddComponent<DesktopPetFallbackMotionController>();
                var serializedObject = new SerializedObject(controller);
                return new IdleProfile(
                    DefaultPoseSampleNormalizedTime: serializedObject.FindProperty("defaultPoseSampleNormalizedTime").floatValue,
                    HorizontalSwayAmplitude: serializedObject.FindProperty("horizontalSwayAmplitude").floatValue,
                    HorizontalSwayFrequency: serializedObject.FindProperty("horizontalSwayFrequency").floatValue,
                    VerticalBobAmplitude: serializedObject.FindProperty("verticalBobAmplitude").floatValue,
                    VerticalBobFrequency: serializedObject.FindProperty("verticalBobFrequency").floatValue,
                    HeadYawAmplitude: serializedObject.FindProperty("headYawAmplitude").floatValue,
                    HeadYawFrequency: serializedObject.FindProperty("headYawFrequency").floatValue,
                    ChestBreathAmplitude: serializedObject.FindProperty("chestBreathAmplitude").floatValue,
                    ChestBreathFrequency: serializedObject.FindProperty("chestBreathFrequency").floatValue);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(temporaryObject);
            }
        }

        private static string GetAbsoluteStreamingAssetPath(string relativePath)
        {
            return Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, relativePath));
        }

        private static string GetOutputDirectory(string assetTypeDirectoryName)
        {
            return Path.GetFullPath(
                Path.Combine(
                    Application.dataPath,
                    "..",
                    "..",
                    ExportRootDirectoryName,
                    ExportProjectDirectoryName,
                    GeneratedDirectoryName,
                    assetTypeDirectoryName));
        }

        private static void EnsureFileExists(string path, string parameterName)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"The required export input file does not exist for {parameterName}.", path);
            }
        }

        private static bool ShouldKeepSampledDefaultPose(HumanBodyBones bone)
        {
            return bone switch
            {
                HumanBodyBones.Spine => true,
                HumanBodyBones.Chest => true,
                HumanBodyBones.UpperChest => true,
                HumanBodyBones.Neck => true,
                HumanBodyBones.Head => true,
                HumanBodyBones.LeftEye => true,
                HumanBodyBones.RightEye => true,
                HumanBodyBones.Jaw => true,
                HumanBodyBones.LeftShoulder => true,
                HumanBodyBones.RightShoulder => true,
                HumanBodyBones.LeftUpperArm => true,
                HumanBodyBones.RightUpperArm => true,
                HumanBodyBones.LeftLowerArm => true,
                HumanBodyBones.RightLowerArm => true,
                HumanBodyBones.LeftHand => true,
                HumanBodyBones.RightHand => true,
                HumanBodyBones.LeftThumbProximal => true,
                HumanBodyBones.LeftThumbIntermediate => true,
                HumanBodyBones.LeftThumbDistal => true,
                HumanBodyBones.LeftIndexProximal => true,
                HumanBodyBones.LeftIndexIntermediate => true,
                HumanBodyBones.LeftIndexDistal => true,
                HumanBodyBones.LeftMiddleProximal => true,
                HumanBodyBones.LeftMiddleIntermediate => true,
                HumanBodyBones.LeftMiddleDistal => true,
                HumanBodyBones.LeftRingProximal => true,
                HumanBodyBones.LeftRingIntermediate => true,
                HumanBodyBones.LeftRingDistal => true,
                HumanBodyBones.LeftLittleProximal => true,
                HumanBodyBones.LeftLittleIntermediate => true,
                HumanBodyBones.LeftLittleDistal => true,
                HumanBodyBones.RightThumbProximal => true,
                HumanBodyBones.RightThumbIntermediate => true,
                HumanBodyBones.RightThumbDistal => true,
                HumanBodyBones.RightIndexProximal => true,
                HumanBodyBones.RightIndexIntermediate => true,
                HumanBodyBones.RightIndexDistal => true,
                HumanBodyBones.RightMiddleProximal => true,
                HumanBodyBones.RightMiddleIntermediate => true,
                HumanBodyBones.RightMiddleDistal => true,
                HumanBodyBones.RightRingProximal => true,
                HumanBodyBones.RightRingIntermediate => true,
                HumanBodyBones.RightRingDistal => true,
                HumanBodyBones.RightLittleProximal => true,
                HumanBodyBones.RightLittleIntermediate => true,
                HumanBodyBones.RightLittleDistal => true,
                _ => false,
            };
        }

        private sealed record IdleProfile(
            float DefaultPoseSampleNormalizedTime,
            float HorizontalSwayAmplitude,
            float HorizontalSwayFrequency,
            float VerticalBobAmplitude,
            float VerticalBobFrequency,
            float HeadYawAmplitude,
            float HeadYawFrequency,
            float ChestBreathAmplitude,
            float ChestBreathFrequency);

        private sealed record BonePose(
            Quaternion LocalRotation,
            Vector3 LocalPosition);

        private sealed class IdlePoseSample
        {
            public Dictionary<HumanBodyBones, BonePose> Bones { get; } = new();

            public static IdlePoseSample FromBoneMap(IReadOnlyDictionary<HumanBodyBones, BoneTransformInfo> boneMap)
            {
                var sample = new IdlePoseSample();
                foreach (var entry in boneMap)
                {
                    sample.Bones[entry.Key] = new BonePose(
                        entry.Value.Transform.localRotation,
                        entry.Value.Transform.localPosition);
                }

                return sample;
            }

            public IdlePoseSample MergeWithBaseLowerBody(IdlePoseSample basePose)
            {
                if (basePose == null)
                {
                    throw new ArgumentNullException(nameof(basePose));
                }

                var merged = new IdlePoseSample();
                foreach (var entry in Bones)
                {
                    if (!ShouldKeepSampledDefaultPose(entry.Key)
                        && basePose.Bones.TryGetValue(entry.Key, out var baseBonePose))
                    {
                        merged.Bones[entry.Key] = baseBonePose;
                        continue;
                    }

                    merged.Bones[entry.Key] = entry.Value;
                }

                foreach (var entry in basePose.Bones)
                {
                    if (!merged.Bones.ContainsKey(entry.Key))
                    {
                        merged.Bones[entry.Key] = entry.Value;
                    }
                }

                return merged;
            }
        }

        private sealed record BoneTransformInfo(
            HumanBodyBones Bone,
            Transform Transform,
            string Path,
            Quaternion BaseLocalRotation,
            Vector3 BaseLocalPosition);

        private sealed class ClipAnimationExporter : IAnimationExporter
        {
            public void Export(ExportingGltfData data, GameObject copy, List<Transform> nodes)
            {
                if (!copy.TryGetComponent<UnityEngine.Animation>(out var animation))
                {
                    return;
                }

                foreach (AnimationState state in animation)
                {
                    if (state.clip == null)
                    {
                        continue;
                    }

                    ExportClip(data, copy.transform, nodes, state.clip);
                }
            }

            private static void ExportClip(ExportingGltfData data, Transform root, List<Transform> nodes, AnimationClip clip)
            {
                var animation = new glTFAnimation
                {
                    name = clip.name,
                };

                var samplers = new Dictionary<int, SamplerData>();
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    var property = ResolveProperty(binding.propertyName);
                    if (property == glTFAnimationTarget.AnimationProperties.NotImplemented)
                    {
                        continue;
                    }

                    var targetTransform = string.IsNullOrWhiteSpace(binding.path)
                        ? root
                        : root.Find(binding.path);
                    if (targetTransform == null)
                    {
                        continue;
                    }

                    var nodeIndex = nodes.IndexOf(targetTransform);
                    if (nodeIndex < 0)
                    {
                        continue;
                    }

                    var samplerIndex = animation.AddChannelAndGetSampler(nodeIndex, property);
                    if (!samplers.TryGetValue(samplerIndex, out var samplerData))
                    {
                        samplerData = new SamplerData(property, glTFAnimationTarget.GetElementCount(property));
                        samplers.Add(samplerIndex, samplerData);
                    }

                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null || curve.keys == null || curve.keys.Length == 0)
                    {
                        continue;
                    }

                    var elementOffset = ResolveElementOffset(binding.propertyName);
                    foreach (var keyframe in curve.keys)
                    {
                        samplerData.SetValue(keyframe.time, elementOffset, keyframe.value);
                    }
                }

                foreach (var sampler in samplers.OrderBy(static pair => pair.Key))
                {
                    var input = sampler.Value.GetInput();
                    var output = sampler.Value.GetOutput();
                    var inputAccessorIndex = data.ExtendBufferAndGetAccessorIndex(input);
                    var outputAccessorIndex = data.ExtendBufferAndGetAccessorIndex(output);
                    animation.samplers[sampler.Key].input = inputAccessorIndex;
                    animation.samplers[sampler.Key].output = outputAccessorIndex;
                    animation.samplers[sampler.Key].interpolation = glTFAnimationTarget.Interpolations.LINEAR.ToString();

                    var outputAccessor = data.Gltf.accessors[outputAccessorIndex];
                    outputAccessor.type = sampler.Value.Property switch
                    {
                        glTFAnimationTarget.AnimationProperties.Translation => "VEC3",
                        glTFAnimationTarget.AnimationProperties.Rotation => "VEC4",
                        _ => outputAccessor.type,
                    };
                    outputAccessor.count = sampler.Value.KeyframeCount;
                }

                if (animation.channels.Count > 0)
                {
                    data.Gltf.animations.Add(animation);
                }
            }

            private static glTFAnimationTarget.AnimationProperties ResolveProperty(string propertyName)
            {
                if (propertyName.StartsWith("m_LocalPosition.", StringComparison.Ordinal))
                {
                    return glTFAnimationTarget.AnimationProperties.Translation;
                }

                if (propertyName.StartsWith("m_LocalRotation.", StringComparison.Ordinal))
                {
                    return glTFAnimationTarget.AnimationProperties.Rotation;
                }

                return glTFAnimationTarget.AnimationProperties.NotImplemented;
            }

            private static int ResolveElementOffset(string propertyName)
            {
                if (propertyName.EndsWith(".x", StringComparison.Ordinal))
                {
                    return 0;
                }

                if (propertyName.EndsWith(".y", StringComparison.Ordinal))
                {
                    return 1;
                }

                if (propertyName.EndsWith(".z", StringComparison.Ordinal))
                {
                    return 2;
                }

                if (propertyName.EndsWith(".w", StringComparison.Ordinal))
                {
                    return 3;
                }

                throw new InvalidOperationException($"Unsupported animation property component: {propertyName}");
            }
        }

        private sealed class SamplerData
        {
            private readonly SortedDictionary<float, float[]> valuesByTime = new();

            public SamplerData(glTFAnimationTarget.AnimationProperties property, int elementCount)
            {
                Property = property;
                ElementCount = elementCount;
            }

            public int ElementCount { get; }

            public int KeyframeCount => valuesByTime.Count;

            public glTFAnimationTarget.AnimationProperties Property { get; }

            public float[] GetInput()
            {
                return valuesByTime.Keys.ToArray();
            }

            public float[] GetOutput()
            {
                var output = new float[valuesByTime.Count * ElementCount];
                var keyframeIndex = 0;
                foreach (var values in valuesByTime.Values)
                {
                    var converted = ConvertToRightHand(values);
                    Buffer.BlockCopy(converted, 0, output, keyframeIndex * ElementCount * sizeof(float), ElementCount * sizeof(float));
                    keyframeIndex++;
                }

                return output;
            }

            public void SetValue(float time, int elementOffset, float value)
            {
                if (!valuesByTime.TryGetValue(time, out var values))
                {
                    values = new float[ElementCount];
                    valuesByTime.Add(time, values);
                }

                values[elementOffset] = value;
            }

            private float[] ConvertToRightHand(float[] values)
            {
                return Property switch
                {
                    glTFAnimationTarget.AnimationProperties.Translation => UniGLTF.UnityExtensions
                        .ReverseZ(new Vector3(values[0], values[1], values[2]))
                        .ToArray(),
                    glTFAnimationTarget.AnimationProperties.Rotation => UniGLTF.UnityExtensions
                        .ReverseZ(new Quaternion(values[0], values[1], values[2], values[3]))
                        .ToArray(),
                    _ => values,
                };
            }
        }
    }
}
