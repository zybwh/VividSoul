#nullable enable

using System;
using UnityEngine;

namespace VividSoul.Runtime.Avatar
{
    public sealed class CharacterRuntimeAssembler
    {
        private const string HitProxyRootName = "RuntimeHitProxy";

        public GameObject Assemble(GameObject modelRoot, Transform parent)
        {
            if (modelRoot == null)
            {
                throw new ArgumentNullException(nameof(modelRoot));
            }

            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            var presentationRoot = new GameObject($"{modelRoot.name} Presentation Root");
            presentationRoot.transform.SetParent(parent, false);
            presentationRoot.transform.localPosition = Vector3.zero;
            presentationRoot.transform.localRotation = Quaternion.identity;
            presentationRoot.transform.localScale = Vector3.one;

            modelRoot.transform.SetParent(presentationRoot.transform, false);
            modelRoot.transform.localPosition = Vector3.zero;
            modelRoot.transform.localRotation = Quaternion.identity;
            modelRoot.transform.localScale = Vector3.one;

            foreach (var renderer in modelRoot.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = true;
            }

            foreach (var skinnedMeshRenderer in modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skinnedMeshRenderer.updateWhenOffscreen = false;
            }

            if (modelRoot.TryGetComponent<Animator>(out var animator))
            {
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                animator.updateMode = AnimatorUpdateMode.Normal;
            }

            AttachHitProxyCollider(presentationRoot, modelRoot);

            return presentationRoot;
        }

        public void Destroy(GameObject? modelRoot)
        {
            if (modelRoot == null)
            {
                return;
            }

            UnityEngine.Object.Destroy(modelRoot);
        }

        private static void AttachHitProxyCollider(GameObject presentationRoot, GameObject modelRoot)
        {
            ClearHitProxy(presentationRoot);
            if (!TryBuildLocalBounds(presentationRoot.transform, modelRoot, out var localBounds))
            {
                return;
            }

            var proxyRoot = new GameObject(HitProxyRootName);
            proxyRoot.transform.SetParent(presentationRoot.transform, false);

            AttachTorsoProxy(proxyRoot.transform, localBounds);
            if (modelRoot.TryGetComponent<Animator>(out var animator) && animator.isHuman)
            {
                AttachHumanoidLimbProxies(proxyRoot.transform, animator, localBounds);
            }
        }

        private static void ClearHitProxy(GameObject presentationRoot)
        {
            foreach (var collider in presentationRoot.GetComponents<Collider>())
            {
                UnityEngine.Object.Destroy(collider);
            }

            var existingProxy = presentationRoot.transform.Find(HitProxyRootName);
            if (existingProxy != null)
            {
                UnityEngine.Object.Destroy(existingProxy.gameObject);
            }
        }

        private static void AttachTorsoProxy(Transform proxyRoot, Bounds localBounds)
        {
            var torsoObject = new GameObject("Torso", typeof(CapsuleCollider));
            torsoObject.transform.SetParent(proxyRoot, false);
            torsoObject.transform.localPosition = localBounds.center + new Vector3(0f, localBounds.size.y * 0.02f, 0f);
            torsoObject.transform.localRotation = Quaternion.identity;

            var collider = torsoObject.GetComponent<CapsuleCollider>();
            var radius = Mathf.Max(0.06f, localBounds.size.x * 0.16f);
            collider.direction = 1;
            collider.radius = radius;
            collider.height = Mathf.Max(radius * 2.2f, localBounds.size.y * 0.72f);
            collider.center = Vector3.zero;
        }

        private static void AttachHumanoidLimbProxies(Transform proxyRoot, Animator animator, Bounds localBounds)
        {
            AttachHeadProxy(proxyRoot, animator, localBounds);
            AttachBoneCapsule(animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), animator.GetBoneTransform(HumanBodyBones.LeftLowerArm), "LeftUpperArm", 0.34f, 0.025f, 0.09f);
            AttachBoneCapsule(animator.GetBoneTransform(HumanBodyBones.LeftLowerArm), animator.GetBoneTransform(HumanBodyBones.LeftHand), "LeftLowerArm", 0.30f, 0.02f, 0.08f);
            AttachBoneCapsule(animator.GetBoneTransform(HumanBodyBones.RightUpperArm), animator.GetBoneTransform(HumanBodyBones.RightLowerArm), "RightUpperArm", 0.34f, 0.025f, 0.09f);
            AttachBoneCapsule(animator.GetBoneTransform(HumanBodyBones.RightLowerArm), animator.GetBoneTransform(HumanBodyBones.RightHand), "RightLowerArm", 0.30f, 0.02f, 0.08f);
            AttachBoneCapsule(animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg), animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg), "LeftUpperLeg", 0.30f, 0.03f, 0.10f);
            AttachBoneCapsule(animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg), animator.GetBoneTransform(HumanBodyBones.LeftFoot), "LeftLowerLeg", 0.26f, 0.025f, 0.09f);
            AttachBoneCapsule(animator.GetBoneTransform(HumanBodyBones.RightUpperLeg), animator.GetBoneTransform(HumanBodyBones.RightLowerLeg), "RightUpperLeg", 0.30f, 0.03f, 0.10f);
            AttachBoneCapsule(animator.GetBoneTransform(HumanBodyBones.RightLowerLeg), animator.GetBoneTransform(HumanBodyBones.RightFoot), "RightLowerLeg", 0.26f, 0.025f, 0.09f);
        }

        private static void AttachHeadProxy(Transform proxyRoot, Animator animator, Bounds localBounds)
        {
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            var neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            if (head == null)
            {
                return;
            }

            var headObject = new GameObject("Head", typeof(SphereCollider));
            headObject.transform.SetParent(head, false);
            headObject.transform.localPosition = Vector3.zero;
            headObject.transform.localRotation = Quaternion.identity;

            var radius = neck != null
                ? Mathf.Clamp(Vector3.Distance(head.position, neck.position) * 0.55f, 0.05f, localBounds.size.x * 0.18f)
                : Mathf.Clamp(localBounds.size.x * 0.1f, 0.05f, 0.16f);
            headObject.GetComponent<SphereCollider>().radius = radius;
        }

        private static void AttachBoneCapsule(
            Transform? start,
            Transform? end,
            string name,
            float radiusFactor,
            float minRadius,
            float maxRadius)
        {
            if (start == null || end == null)
            {
                return;
            }

            var localEnd = start.InverseTransformPoint(end.position);
            var length = localEnd.magnitude;
            if (length <= 0.0001f)
            {
                return;
            }

            var capsuleObject = new GameObject(name, typeof(CapsuleCollider));
            capsuleObject.transform.SetParent(start, false);
            capsuleObject.transform.localPosition = localEnd * 0.5f;
            capsuleObject.transform.localRotation = Quaternion.FromToRotation(Vector3.up, localEnd.normalized);

            var collider = capsuleObject.GetComponent<CapsuleCollider>();
            var radius = Mathf.Clamp(length * radiusFactor, minRadius, maxRadius);
            collider.direction = 1;
            collider.radius = radius;
            collider.height = Mathf.Max(length + (radius * 2f), radius * 2.2f);
            collider.center = Vector3.zero;
        }

        private static bool TryBuildLocalBounds(Transform rootTransform, GameObject modelRoot, out Bounds localBounds)
        {
            var renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
            var hasBounds = false;
            localBounds = default;
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled)
                {
                    continue;
                }

                var worldBounds = renderer.bounds;
                foreach (var corner in EnumerateWorldCorners(worldBounds))
                {
                    var localCorner = rootTransform.InverseTransformPoint(corner);
                    if (!hasBounds)
                    {
                        localBounds = new Bounds(localCorner, Vector3.zero);
                        hasBounds = true;
                        continue;
                    }

                    localBounds.Encapsulate(localCorner);
                }
            }

            return hasBounds;
        }

        private static Vector3[] EnumerateWorldCorners(Bounds bounds)
        {
            var min = bounds.min;
            var max = bounds.max;
            return new[]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z),
            };
        }
    }
}
