#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using VividSoul.Runtime.Settings;

namespace VividSoul.Runtime.AI
{
    public sealed class MateSpeechService : IDisposable
    {
        private const int AudioDownloadTimeoutSeconds = 45;
        private readonly IAiSettingsStore aiSettingsStore;
        private readonly IAiSecretsStore aiSecretsStore;
        private readonly IDesktopPetSettingsStore desktopPetSettingsStore;
        private readonly ITtsProvider miniMaxTtsProvider;
        private readonly GameObject audioHostObject;
        private readonly AudioSource audioSource;
        private CancellationTokenSource? playbackCancellationTokenSource;
        private AudioClip? currentClip;

        public MateSpeechService(
            Transform parent,
            IAiSettingsStore? aiSettingsStore = null,
            IAiSecretsStore? aiSecretsStore = null,
            IDesktopPetSettingsStore? desktopPetSettingsStore = null,
            ITtsProvider? miniMaxTtsProvider = null)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            this.aiSettingsStore = aiSettingsStore ?? new AiSettingsStore();
            this.aiSecretsStore = aiSecretsStore ?? new AiSecretsStore();
            this.desktopPetSettingsStore = desktopPetSettingsStore ?? new DesktopPetSettingsStore();
            this.miniMaxTtsProvider = miniMaxTtsProvider ?? new MiniMaxTtsProvider();
            audioHostObject = new GameObject("MateSpeechAudio", typeof(AudioSource));
            audioHostObject.transform.SetParent(parent, false);
            audioSource = audioHostObject.GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
        }

        public async Task<bool> SpeakAsync(
            string text,
            string characterSourcePath = "",
            CancellationToken cancellationToken = default)
        {
            var normalizedText = text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedText))
            {
                return false;
            }

            var settings = aiSettingsStore.Load();
            if (!settings.EnableTts)
            {
                return false;
            }

            var activeProfile = ResolveActiveProfile(settings);
            if (activeProfile == null || activeProfile.ProviderType != LlmProviderType.MiniMax || !activeProfile.Enabled)
            {
                return false;
            }

            var apiKey = aiSecretsStore.LoadApiKey(activeProfile.Id);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new UserFacingException("当前 MiniMax Provider 的 API Key 为空，无法发起 TTS。");
            }

            using var linkedCancellationTokenSource = ReplacePlaybackCancellationTokenSource(cancellationToken);

            try
            {
                var synthesis = await miniMaxTtsProvider.SynthesizeAsync(
                    new TtsRequest(
                        ProviderProfile: activeProfile,
                        ApiKey: apiKey,
                        Text: normalizedText,
                        Volume: NormalizeVolume(desktopPetSettingsStore.Load().VoiceVolume),
                        PreferredVoiceId: activeProfile.MiniMaxTtsVoiceId),
                    linkedCancellationTokenSource.Token);
                await PlayAudioAsync(synthesis, linkedCancellationTokenSource.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            finally
            {
                if (ReferenceEquals(playbackCancellationTokenSource, linkedCancellationTokenSource))
                {
                    playbackCancellationTokenSource = null;
                }
            }
        }

        public void Stop()
        {
            CancelPlayback();
            audioSource.Stop();
            ClearCurrentClip();
        }

        public void Dispose()
        {
            Stop();
            playbackCancellationTokenSource?.Dispose();
            playbackCancellationTokenSource = null;
            if (audioHostObject != null)
            {
                UnityEngine.Object.Destroy(audioHostObject);
            }
        }

        private static LlmProviderProfile? ResolveActiveProfile(AiSettingsData settings)
        {
            return settings.ProviderProfiles.FirstOrDefault(profile =>
                       string.Equals(profile.Id, settings.ActiveProviderId, StringComparison.OrdinalIgnoreCase))
                   ?? settings.ProviderProfiles.FirstOrDefault();
        }

        private static float NormalizeVolume(float volume)
        {
            return Mathf.Clamp(volume, 0.1f, 1f);
        }

        private CancellationTokenSource ReplacePlaybackCancellationTokenSource(CancellationToken externalCancellationToken)
        {
            CancelPlayback();
            audioSource.Stop();
            ClearCurrentClip();
            playbackCancellationTokenSource?.Dispose();
            playbackCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken);
            return playbackCancellationTokenSource;
        }

        private void CancelPlayback()
        {
            if (playbackCancellationTokenSource == null)
            {
                return;
            }

            playbackCancellationTokenSource.Cancel();
        }

        private async Task PlayAudioAsync(TtsSynthesisResult synthesisResult, CancellationToken cancellationToken)
        {
            using var unityWebRequest = UnityWebRequestMultimedia.GetAudioClip(
                synthesisResult.AudioUrl,
                ResolveAudioType(synthesisResult.AudioFormat));
            unityWebRequest.timeout = AudioDownloadTimeoutSeconds;

            using var cancellationRegistration = cancellationToken.Register(unityWebRequest.Abort);
            var operation = unityWebRequest.SendWebRequest();
            while (!operation.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (unityWebRequest.result != UnityWebRequest.Result.Success)
            {
                throw new UserFacingException(string.IsNullOrWhiteSpace(unityWebRequest.error)
                    ? "TTS 音频下载失败。"
                    : $"TTS 音频下载失败：{unityWebRequest.error.Trim()}");
            }

            var clip = DownloadHandlerAudioClip.GetContent(unityWebRequest);
            if (clip == null)
            {
                throw new UserFacingException("TTS 音频下载成功，但无法解码播放。");
            }

            cancellationToken.ThrowIfCancellationRequested();
            ClearCurrentClip();
            currentClip = clip;
            audioSource.clip = currentClip;
            audioSource.volume = NormalizeVolume(desktopPetSettingsStore.Load().VoiceVolume);
            audioSource.Play();

            while (audioSource.isPlaying)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }

        private void ClearCurrentClip()
        {
            audioSource.clip = null;
            if (currentClip != null)
            {
                UnityEngine.Object.Destroy(currentClip);
                currentClip = null;
            }
        }

        private static AudioType ResolveAudioType(string audioFormat)
        {
            return string.Equals(audioFormat, "mp3", StringComparison.OrdinalIgnoreCase)
                ? AudioType.MPEG
                : AudioType.WAV;
        }
    }
}
