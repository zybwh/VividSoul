using System;
using System.Collections.Generic;
using UnityEngine;

namespace UniGLTF
{
    /// <summary>
    /// Generate the descriptor of the glTF default material.
    /// </summary>
    public static class BuiltInGltfDefaultMaterialImporter
    {
        public static MaterialDescriptor CreateParam(string materialName = null)
        {
            var shader = ResolveDefaultShader();

            // FIXME
            return new MaterialDescriptor(
                string.IsNullOrEmpty(materialName) ? "__default__" : materialName,
                shader,
                default,
                new Dictionary<string, TextureDescriptor>(),
                new Dictionary<string, float>(),
                new Dictionary<string, Color>(),
                new Dictionary<string, Vector4>(),
                new List<Action<Material>>()
            );
        }

        private static Shader ResolveDefaultShader()
        {
            return BuiltInGltfPbrMaterialImporter.Shader
                ?? Shader.Find("UniGLTF/UniUnlit")
                ?? Shader.Find("VRM10/MToon10")
                ?? Shader.Find("Sprites/Default")
                ?? throw new InvalidOperationException("No fallback shader is available for glTF default material import.");
        }
    }
}