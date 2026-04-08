#nullable enable

using System;
using System.Threading.Tasks;
using UniGLTF;
using UnityEngine;

namespace VividSoul.Runtime.Avatar
{
    public sealed class RuntimeScaledTextureDeserializer : ITextureDeserializer
    {
        private readonly int maxTextureSize;

        public RuntimeScaledTextureDeserializer(int maxTextureSize)
        {
            this.maxTextureSize = Mathf.Max(32, maxTextureSize);
        }

        public async Task<Texture2D> LoadTextureAsync(DeserializingTextureInfo textureInfo, IAwaitCaller awaitCaller)
        {
            if (textureInfo.ImageData == null)
            {
                return null!;
            }

            try
            {
                var isLinear = textureInfo.ColorSpace == UniGLTF.ColorSpace.Linear;
                var texture = new Texture2D(2, 2, TextureFormat.ARGB32, textureInfo.UseMipmap, isLinear);
                texture.LoadImage(textureInfo.ImageData);
                await awaitCaller.NextFrame();

                var resizedTexture = DownscaleIfNeeded(texture, isLinear, textureInfo.UseMipmap);
                if (!ReferenceEquals(resizedTexture, texture))
                {
                    UnityEngine.Object.Destroy(texture);
                    texture = resizedTexture;
                    await awaitCaller.NextFrame();
                }

                texture.wrapModeU = textureInfo.WrapModeU;
                texture.wrapModeV = textureInfo.WrapModeV;
                texture.filterMode = textureInfo.FilterMode;
                return texture;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return null!;
            }
        }

        private Texture2D DownscaleIfNeeded(Texture2D source, bool linear, bool useMipMap)
        {
            if (source.width <= maxTextureSize && source.height <= maxTextureSize)
            {
                return source;
            }

            var scale = Mathf.Min((float)maxTextureSize / source.width, (float)maxTextureSize / source.height);
            var targetWidth = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
            var targetHeight = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
            var renderTexture = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            var previousActive = RenderTexture.active;
            try
            {
                Graphics.Blit(source, renderTexture);
                RenderTexture.active = renderTexture;
                var resized = new Texture2D(targetWidth, targetHeight, TextureFormat.ARGB32, useMipMap, linear);
                resized.ReadPixels(new Rect(0f, 0f, targetWidth, targetHeight), 0, 0);
                resized.Apply(useMipMap, false);
                return resized;
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }
    }
}
