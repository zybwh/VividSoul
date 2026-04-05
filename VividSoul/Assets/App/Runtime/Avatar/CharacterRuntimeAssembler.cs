#nullable enable

using System;
using UnityEngine;

namespace VividSoul.Runtime.Avatar
{
    public sealed class CharacterRuntimeAssembler
    {
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
                skinnedMeshRenderer.updateWhenOffscreen = true;
            }

            if (modelRoot.TryGetComponent<Animator>(out var animator))
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.updateMode = AnimatorUpdateMode.Normal;
            }

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
    }
}
