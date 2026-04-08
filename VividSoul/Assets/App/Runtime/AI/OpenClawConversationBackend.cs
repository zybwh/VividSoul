#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VividSoul.Runtime.Content;

namespace VividSoul.Runtime.AI
{
    public sealed class OpenClawConversationBackend : IMateConversationBackend
    {
        private static readonly TimeSpan AssistantTurnTimeout = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan AssistantMessageDeduplicationWindow = TimeSpan.FromSeconds(5);
        private readonly IAiSecretsStore aiSecretsStore;
        private readonly OpenClawGatewayClient gatewayClient;
        private readonly OpenClawHttpChatClient httpChatClient;
        private readonly OpenClawTranscriptMirrorStore transcriptMirrorStore;
        private readonly ModelFingerprintService modelFingerprintService;
        private readonly object syncRoot = new();
        private readonly List<PendingOptimisticUserMessage> pendingOptimisticUserMessages = new();
        private readonly List<RecentAssistantMessage> recentAssistantMessages = new();

        private LlmProviderProfile? activeProfile;
        private string currentSessionKey = string.Empty;
        private string connectedProviderId = string.Empty;
        private string connectedGatewayUrl = string.Empty;
        private string connectedTokenSignature = string.Empty;
        private string currentCharacterSourcePath = string.Empty;
        private bool awaitingAssistantMessage;
        private int unreadCount;
        private bool isRequestInFlight;
        private bool isDisposed;
        private Task? reconnectTask;
        private TaskCompletionSource<bool>? pendingAssistantTurnCompletionSource;
        private ConversationStatusSnapshot lastStatus = new(
            ProviderId: string.Empty,
            ProviderDisplayName: "OpenClaw",
            ProviderType: LlmProviderType.OpenClaw,
            ConnectionState: ConversationConnectionState.Disconnected,
            StatusText: "未连接",
            SessionKey: string.Empty,
            AgentId: string.Empty,
            IsRequestInFlight: false,
            UnreadCount: 0);

        public OpenClawConversationBackend(
            IAiSecretsStore aiSecretsStore,
            OpenClawGatewayClient gatewayClient,
            OpenClawTranscriptMirrorStore transcriptMirrorStore,
            ModelFingerprintService modelFingerprintService)
        {
            this.aiSecretsStore = aiSecretsStore ?? throw new ArgumentNullException(nameof(aiSecretsStore));
            this.gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
            httpChatClient = new OpenClawHttpChatClient();
            this.transcriptMirrorStore = transcriptMirrorStore ?? throw new ArgumentNullException(nameof(transcriptMirrorStore));
            this.modelFingerprintService = modelFingerprintService ?? throw new ArgumentNullException(nameof(modelFingerprintService));

            this.gatewayClient.EventReceived += HandleGatewayEventReceived;
            this.gatewayClient.ConnectionFaulted += HandleGatewayConnectionFaulted;
            this.gatewayClient.ConnectionClosed += HandleGatewayConnectionClosed;
        }

        public event Action<ConversationMessageEnvelope>? MessageReceived;

        public event Action<ConversationStatusSnapshot>? StatusChanged;

        public LlmProviderType ProviderType => LlmProviderType.OpenClaw;

        public async Task ActivateAsync(
            LlmProviderProfile profile,
            string characterSourcePath,
            string characterDisplayName,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            ValidateProfile(profile);

            if (string.IsNullOrWhiteSpace(characterSourcePath))
            {
                throw new UserFacingException("当前还没有加载角色，暂时无法建立 OpenClaw 会话。");
            }

            var nextCharacterSourcePath = characterSourcePath.Trim();
            var nextSessionKey = BuildSessionKey(profile, nextCharacterSourcePath);
            var hasSessionChanged = !string.Equals(currentSessionKey, nextSessionKey, StringComparison.Ordinal);

            activeProfile = profile;
            currentCharacterSourcePath = nextCharacterSourcePath;
            currentSessionKey = nextSessionKey;
            if (hasSessionChanged)
            {
                unreadCount = 0;
                pendingOptimisticUserMessages.Clear();
                recentAssistantMessages.Clear();
                ResetPendingAssistantTurn();
            }

            if (!profile.OpenClawAutoConnect && gatewayClient.State != System.Net.WebSockets.WebSocketState.Open)
            {
                PublishStatus(ConversationConnectionState.Disconnected, "OpenClaw 未自动连接", false);
                return;
            }

            await EnsureConnectedAsync(cancellationToken);
        }

        public async Task SendUserMessageAsync(
            LlmProviderProfile profile,
            string characterSourcePath,
            string characterDisplayName,
            string userMessage,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                throw new ArgumentException("A chat message is required.", nameof(userMessage));
            }

            await ActivateAsync(profile, characterSourcePath, characterDisplayName, cancellationToken);
            await EnsureConnectedAsync(cancellationToken);
            PublishStatus(ConversationConnectionState.Connected, "发送到 OpenClaw 中...", true);

            var trimmedMessage = userMessage.Trim();
            var pendingAssistantTurnTask = BeginPendingAssistantTurn();
            lock (syncRoot)
            {
                pendingOptimisticUserMessages.Add(new PendingOptimisticUserMessage(trimmedMessage, DateTimeOffset.UtcNow));
            }

            var optimisticMessage = new ChatMessage(
                Id: Guid.NewGuid().ToString("N"),
                SessionId: currentSessionKey,
                Role: ChatRole.User,
                Text: trimmedMessage,
                CreatedAt: DateTimeOffset.UtcNow,
                Source: ChatInvocationSource.UserInput);
            MirrorMessageIfNeeded(optimisticMessage);
            MessageReceived?.Invoke(new ConversationMessageEnvelope(
                Message: optimisticMessage,
                IsProactive: false,
                ShouldDisplayBubble: false,
                ShouldSpeak: false,
                IsOptimistic: true));

            try
            {
                var response = await httpChatClient.SendAsync(
                    NormalizeGatewayUri(profile.OpenClawGatewayWsUrl),
                    LoadRequiredToken(profile),
                    BuildHttpModel(profile),
                    currentSessionKey,
                    trimmedMessage,
                    cancellationToken);
                if (IsAwaitingAssistantTurn()
                    && TryHandleSuppressedNoReplySignal(response.AssistantText, "http-fallback", completePendingTurn: true))
                {
                    return;
                }

                PublishStatus(
                    ConversationConnectionState.Connected,
                    IsAwaitingAssistantTurn() ? "等待 OpenClaw 回复..." : "已连接到 OpenClaw",
                    IsAwaitingAssistantTurn());
                await WaitForAssistantTurnAsync(pendingAssistantTurnTask, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await InvalidateConnectionAsync(CancellationToken.None);
                Debug.LogWarning($"[OpenClawBackend] send failed session={currentSessionKey} error={exception.Message}");
                var userFacingMessage = exception switch
                {
                    AssistantTurnFailedException => exception.Message,
                    TimeoutException => $"等待 OpenClaw 回复超时：{exception.Message}",
                    _ => FormatGatewayFailureMessage(exception.Message, "OpenClaw 请求失败"),
                };
                PublishStatus(ConversationConnectionState.Faulted, userFacingMessage, false);
                FailPendingAssistantTurn(new AssistantTurnFailedException(userFacingMessage));
                throw new UserFacingException(userFacingMessage);
            }
        }

        public async Task DeactivateAsync(CancellationToken cancellationToken)
        {
            activeProfile = null;
            currentSessionKey = string.Empty;
            connectedProviderId = string.Empty;
            connectedGatewayUrl = string.Empty;
            connectedTokenSignature = string.Empty;
            currentCharacterSourcePath = string.Empty;
            unreadCount = 0;
            pendingOptimisticUserMessages.Clear();
            recentAssistantMessages.Clear();
            ResetPendingAssistantTurn();
            reconnectTask = null;
            PublishStatus(ConversationConnectionState.Disconnected, "OpenClaw 未激活", false);
            await gatewayClient.CloseAsync(cancellationToken);
        }

        public void MarkMessagesRead()
        {
            unreadCount = 0;
            if (activeProfile == null)
            {
                return;
            }

            PublishStatus(ResolveConnectionState(), "已连接到 OpenClaw", isRequestInFlight);
        }

        public void Tick(float unscaledTime)
        {
            _ = unscaledTime;
        }

        public void NotifyApplicationFocus(bool hasFocus)
        {
            _ = hasFocus;
        }

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            gatewayClient.EventReceived -= HandleGatewayEventReceived;
            gatewayClient.ConnectionFaulted -= HandleGatewayConnectionFaulted;
            gatewayClient.ConnectionClosed -= HandleGatewayConnectionClosed;
            gatewayClient.Dispose();
        }

        private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
        {
            var profile = activeProfile ?? throw new InvalidOperationException("OpenClaw profile is not active.");
            var token = LoadRequiredToken(profile);
            var normalizedToken = token.Trim();
            var shouldReuseOpenConnection = gatewayClient.State == System.Net.WebSockets.WebSocketState.Open
                && string.Equals(connectedProviderId, profile.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(connectedGatewayUrl, profile.OpenClawGatewayWsUrl, StringComparison.Ordinal)
                && string.Equals(connectedTokenSignature, normalizedToken, StringComparison.Ordinal);

            PublishStatus(ConversationConnectionState.Connecting, "正在连接 OpenClaw...", false);
            try
            {
                if (!shouldReuseOpenConnection)
                {
                    await gatewayClient.ConnectAsync(NormalizeGatewayUri(profile.OpenClawGatewayWsUrl), token, cancellationToken);
                    connectedProviderId = profile.Id;
                    connectedGatewayUrl = profile.OpenClawGatewayWsUrl;
                    connectedTokenSignature = normalizedToken;
                }
                PublishStatus(ConversationConnectionState.Connected, "已连接到 OpenClaw", false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await InvalidateConnectionAsync(CancellationToken.None);
                Debug.LogWarning($"[OpenClawBackend] ensure connected failed session={currentSessionKey} error={exception.Message}");
                var nextState = IsAuthenticationFailure(exception.Message)
                    ? ConversationConnectionState.AuthFailed
                    : ConversationConnectionState.Faulted;
                var userFacingMessage = FormatGatewayFailureMessage(exception.Message, "OpenClaw 连接失败");
                PublishStatus(nextState, userFacingMessage, false);
                throw new UserFacingException(userFacingMessage);
            }
        }

        private void HandleGatewayEventReceived(string eventName, string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(eventName) || string.IsNullOrWhiteSpace(currentSessionKey))
            {
                return;
            }

            try
            {
                if (MiniJson.Deserialize(payloadJson) is not Dictionary<string, object?> root)
                {
                    return;
                }

                if (string.Equals(eventName, "chat", StringComparison.OrdinalIgnoreCase))
                {
                    HandleChatEvent(root);
                    return;
                }

                if (!string.Equals(eventName, "session.message", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                HandleSessionMessageEvent(root);
            }
            catch (Exception exception)
            {
                var message = $"OpenClaw 消息解析失败：{exception.Message}";
                Debug.LogWarning($"[OpenClawBackend] ws event parse failed session={currentSessionKey} error={exception.Message}");
                PublishStatus(ConversationConnectionState.Faulted, message, IsAwaitingAssistantTurn());
                FailPendingAssistantTurn(new AssistantTurnFailedException(message));
            }
        }

        private void HandleGatewayConnectionFaulted(Exception exception)
        {
            if (activeProfile == null)
            {
                return;
            }

            ResetConnectionTracking();
            var message = $"OpenClaw 连接异常：{exception.Message}";
            Debug.LogWarning($"[OpenClawBackend] ws fault session={currentSessionKey} error={exception.Message}");
            PublishStatus(ConversationConnectionState.Faulted, message, false);
            FailPendingAssistantTurn(new AssistantTurnFailedException(message));
            BeginReconnectLoop();
        }

        private void HandleGatewayConnectionClosed(System.Net.WebSockets.WebSocketCloseStatus? closeStatus, string description)
        {
            if (activeProfile == null)
            {
                return;
            }

            ResetConnectionTracking();
            var suffix = string.IsNullOrWhiteSpace(description) ? closeStatus?.ToString() ?? "连接已关闭" : description;
            var message = $"OpenClaw 连接已关闭：{suffix}";
            Debug.LogWarning($"[OpenClawBackend] ws closed session={currentSessionKey} status={closeStatus} description={description}");
            PublishStatus(ConversationConnectionState.Disconnected, message, false);
            FailPendingAssistantTurn(new AssistantTurnFailedException(message));
            BeginReconnectLoop();
        }

        private void BeginReconnectLoop()
        {
            var profile = activeProfile;
            if (profile == null || !profile.OpenClawAutoReconnect || reconnectTask != null || string.IsNullOrWhiteSpace(currentCharacterSourcePath))
            {
                return;
            }

            reconnectTask = Task.Run(async () =>
            {
                while (!isDisposed && activeProfile != null)
                {
                    PublishStatus(ConversationConnectionState.Reconnecting, "正在重连 OpenClaw...", false);
                    try
                    {
                        await Task.Delay(2000);
                        await EnsureConnectedAsync(CancellationToken.None);
                        reconnectTask = null;
                        return;
                    }
                    catch
                    {
                        await Task.Delay(3000);
                    }
                }

                reconnectTask = null;
            });
        }

        private void MirrorMessageIfNeeded(ChatMessage message)
        {
            var profile = activeProfile;
            if (profile == null || !profile.OpenClawMirrorTranscriptLocally)
            {
                return;
            }

            transcriptMirrorStore.Append(message.SessionId, message);
        }

        private bool TryConsumeOptimisticMessage(string text)
        {
            lock (syncRoot)
            {
                var match = pendingOptimisticUserMessages.FirstOrDefault(item =>
                    string.Equals(item.Text, text.Trim(), StringComparison.Ordinal)
                    && DateTimeOffset.UtcNow - item.CreatedAt <= TimeSpan.FromSeconds(20));
                if (match == null)
                {
                    return false;
                }

                pendingOptimisticUserMessages.Remove(match);
                return true;
            }
        }

        private void HandleChatEvent(IReadOnlyDictionary<string, object?> payload)
        {
            var sessionKey = ResolveSessionKey(payload);
            if (!string.Equals(sessionKey, currentSessionKey, StringComparison.Ordinal))
            {
                return;
            }

            var state = GetString(payload, "state")?.Trim().ToLowerInvariant() ?? string.Empty;
            var messageElement = GetMap(payload, "message");
            var text = messageElement == null ? string.Empty : ExtractMessageText(messageElement);
            if (string.Equals(state, "delta", StringComparison.Ordinal))
            {
                if (IsAwaitingAssistantTurn())
                {
                    PublishStatus(ConversationConnectionState.Connected, "正在接收 OpenClaw 回复...", true);
                }

                return;
            }

            if (string.Equals(state, "error", StringComparison.Ordinal))
            {
                if (!IsAwaitingAssistantTurn())
                {
                    return;
                }

                var errorMessage = GetString(payload, "errorMessage") ?? "OpenClaw 未返回具体错误。";
                var message = $"OpenClaw 回复失败：{errorMessage}";
                PublishStatus(ConversationConnectionState.Faulted, message, false);
                FailPendingAssistantTurn(new AssistantTurnFailedException(message));
                return;
            }

            if (!string.Equals(state, "final", StringComparison.Ordinal))
            {
                return;
            }

            if (TryHandleSuppressedNoReplySignal(text, "ws-chat-final", completePendingTurn: true))
            {
                return;
            }

            var isProactive = !CompletePendingAssistantTurn();
            if (string.IsNullOrWhiteSpace(text))
            {
                PublishStatus(ConversationConnectionState.Connected, "已连接到 OpenClaw", false);
                return;
            }

            EmitIncomingMessage(
                sessionKey,
                messageElement ?? payload,
                ChatRole.Assistant,
                text,
                isProactive,
                isOptimistic: false);
            PublishStatus(ConversationConnectionState.Connected, "已连接到 OpenClaw", false);
        }

        private void HandleSessionMessageEvent(IReadOnlyDictionary<string, object?> payload)
        {
            var sessionKey = ResolveSessionKey(payload);
            if (!string.Equals(sessionKey, currentSessionKey, StringComparison.Ordinal))
            {
                return;
            }

            var messageElement = GetMap(payload, "message") ?? payload;
            var role = ParseChatRole(GetString(messageElement, "role"));
            var text = ExtractMessageText(messageElement);
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (role == ChatRole.Assistant && TryHandleSuppressedNoReplySignal(text, "ws-session-message", completePendingTurn: false))
            {
                return;
            }

            if (role == ChatRole.User && TryConsumeOptimisticMessage(text))
            {
                return;
            }

            if (role == ChatRole.Assistant && IsAwaitingAssistantTurn())
            {
                return;
            }

            var isProactive = role == ChatRole.Assistant;
            EmitIncomingMessage(
                sessionKey,
                messageElement,
                role,
                text,
                isProactive,
                isOptimistic: false);
            PublishStatus(ConversationConnectionState.Connected, "已连接到 OpenClaw", IsAwaitingAssistantTurn());
        }

        private void EmitIncomingMessage(
            string sessionKey,
            IReadOnlyDictionary<string, object?> messageElement,
            ChatRole role,
            string text,
            bool isProactive,
            bool isOptimistic)
        {
            var normalizedText = text.Trim();
            if (string.IsNullOrWhiteSpace(normalizedText))
            {
                return;
            }

            if (role == ChatRole.Assistant)
            {
                if (isProactive && activeProfile != null && !activeProfile.OpenClawReceiveProactiveMessages)
                {
                    return;
                }

                if (!TryRegisterAssistantMessage(normalizedText))
                {
                    return;
                }
            }

            var message = new ChatMessage(
                Id: GetString(messageElement, "id") ?? Guid.NewGuid().ToString("N"),
                SessionId: sessionKey,
                Role: role,
                Text: normalizedText,
                CreatedAt: ParseTimestamp(messageElement),
                Source: isProactive ? ChatInvocationSource.ProactiveTick : ChatInvocationSource.UserInput);
            MirrorMessageIfNeeded(message);

            if (role == ChatRole.Assistant)
            {
                unreadCount++;
            }

            var profile = activeProfile;
            var shouldDisplayBubble = role == ChatRole.Assistant && (profile?.OpenClawEnableBubbleForIncoming ?? true);
            var shouldSpeak = role == ChatRole.Assistant && (profile?.OpenClawEnableTtsForIncoming ?? false);
            MessageReceived?.Invoke(new ConversationMessageEnvelope(
                Message: message,
                IsProactive: isProactive,
                ShouldDisplayBubble: shouldDisplayBubble,
                ShouldSpeak: shouldSpeak,
                IsOptimistic: isOptimistic));
        }

        private Task<bool> BeginPendingAssistantTurn()
        {
            lock (syncRoot)
            {
                pendingAssistantTurnCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                awaitingAssistantMessage = true;
                return pendingAssistantTurnCompletionSource.Task;
            }
        }

        private static async Task WaitForAssistantTurnAsync(Task<bool> pendingTask, CancellationToken cancellationToken)
        {
            var timeoutTask = Task.Delay(AssistantTurnTimeout, cancellationToken);
            var completedTask = await Task.WhenAny(pendingTask, timeoutTask);
            if (!ReferenceEquals(completedTask, pendingTask))
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException($"超过 {AssistantTurnTimeout.TotalSeconds:0} 秒仍未收到最终回复。");
            }

            await pendingTask;
        }

        private bool CompletePendingAssistantTurn()
        {
            lock (syncRoot)
            {
                if (!awaitingAssistantMessage)
                {
                    return false;
                }

                var wasAwaiting = awaitingAssistantMessage;
                awaitingAssistantMessage = false;
                pendingAssistantTurnCompletionSource?.TrySetResult(true);
                pendingAssistantTurnCompletionSource = null;
                return wasAwaiting;
            }
        }

        private void FailPendingAssistantTurn(Exception exception)
        {
            lock (syncRoot)
            {
                awaitingAssistantMessage = false;
                pendingAssistantTurnCompletionSource?.TrySetException(exception);
                pendingAssistantTurnCompletionSource = null;
                Debug.LogWarning($"[OpenClawBackend] pending turn fail session={currentSessionKey} error={exception.Message}");
            }
        }

        private void ResetPendingAssistantTurn()
        {
            lock (syncRoot)
            {
                awaitingAssistantMessage = false;
                pendingAssistantTurnCompletionSource = null;
            }
        }

        private bool IsAwaitingAssistantTurn()
        {
            lock (syncRoot)
            {
                return awaitingAssistantMessage;
            }
        }

        private async Task InvalidateConnectionAsync(CancellationToken cancellationToken)
        {
            ResetConnectionTracking();
            await gatewayClient.CloseAsync(cancellationToken);
        }

        private void ResetConnectionTracking()
        {
            connectedProviderId = string.Empty;
            connectedGatewayUrl = string.Empty;
            connectedTokenSignature = string.Empty;
        }

        private bool TryRegisterAssistantMessage(string text)
        {
            lock (syncRoot)
            {
                var cutoff = DateTimeOffset.UtcNow - AssistantMessageDeduplicationWindow;
                recentAssistantMessages.RemoveAll(item => item.CreatedAt < cutoff);
                if (recentAssistantMessages.Any(item => string.Equals(item.Text, text, StringComparison.Ordinal)))
                {
                    return false;
                }

                recentAssistantMessages.Add(new RecentAssistantMessage(text, DateTimeOffset.UtcNow));
                return true;
            }
        }

        private bool TryHandleSuppressedNoReplySignal(string text, string source, bool completePendingTurn)
        {
            if (!IsSuppressedNoReplyText(text))
            {
                return false;
            }

            if (completePendingTurn)
            {
                CompletePendingAssistantTurn();
            }

            Debug.Log($"[OpenClawBackend] suppressed no-reply source={source} session={currentSessionKey} text={BuildPreview(text)}");
            PublishStatus(
                ConversationConnectionState.Connected,
                completePendingTurn ? "OpenClaw 本轮未回复" : "已连接到 OpenClaw",
                false);
            return true;
        }

        private static bool IsSuppressedNoReplyText(string text)
        {
            var normalized = text?.Trim() ?? string.Empty;
            return string.Equals(normalized, "NO", StringComparison.Ordinal)
                   || string.Equals(normalized, "NO_REPLY", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(normalized, "No response from OpenClaw.", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(normalized, "No reply from agent.", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveSessionKey(IReadOnlyDictionary<string, object?> payload)
        {
            return GetString(payload, "sessionKey")
                   ?? GetString(payload, "key")
                   ?? GetNestedString(payload, "session", "key")
                   ?? string.Empty;
        }

        private static bool IsAuthenticationFailure(string message)
        {
            return message.Contains("auth", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("token", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("missing scope", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatGatewayFailureMessage(string rawMessage, string prefix)
        {
            if (rawMessage.Contains("missing scope: operator.write", StringComparison.OrdinalIgnoreCase))
            {
                return $"{prefix}：当前 OpenClaw Token 缺少 `operator.write` 权限。请在 Gateway 侧改用包含 `operator.read` 和 `operator.write` 的 operator token。";
            }

            if (rawMessage.Contains("missing scope: operator.read", StringComparison.OrdinalIgnoreCase))
            {
                return $"{prefix}：当前 OpenClaw Token 缺少 `operator.read` 权限。请在 Gateway 侧改用包含 `operator.read` 的 operator token。";
            }

            return $"{prefix}：{rawMessage}";
        }

        private void PublishStatus(ConversationConnectionState connectionState, string statusText, bool requestInFlight)
        {
            var profile = activeProfile;
            lastStatus = new ConversationStatusSnapshot(
                ProviderId: profile?.Id ?? string.Empty,
                ProviderDisplayName: profile?.DisplayName ?? "OpenClaw",
                ProviderType: LlmProviderType.OpenClaw,
                ConnectionState: connectionState,
                StatusText: statusText,
                SessionKey: currentSessionKey,
                AgentId: ResolveAgentId(profile),
                IsRequestInFlight: requestInFlight,
                UnreadCount: unreadCount);
            isRequestInFlight = requestInFlight;
            StatusChanged?.Invoke(lastStatus);
        }

        private ConversationConnectionState ResolveConnectionState()
        {
            return gatewayClient.State == System.Net.WebSockets.WebSocketState.Open
                ? ConversationConnectionState.Connected
                : ConversationConnectionState.Disconnected;
        }

        private static void ValidateProfile(LlmProviderProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (profile.ProviderType != LlmProviderType.OpenClaw)
            {
                throw new InvalidOperationException($"Expected OpenClaw provider type, got {profile.ProviderType}.");
            }

            if (string.IsNullOrWhiteSpace(profile.OpenClawGatewayWsUrl))
            {
                throw new UserFacingException("当前 OpenClaw Provider 缺少 Gateway WS URL。");
            }
        }

        private string BuildSessionKey(LlmProviderProfile profile, string characterSourcePath)
        {
            return profile.OpenClawSessionMode switch
            {
                OpenClawSessionMode.Global => $"vividsoul:global:{ResolveAgentId(profile)}",
                OpenClawSessionMode.Custom when !string.IsNullOrWhiteSpace(profile.OpenClawSessionKeyTemplate) => profile.OpenClawSessionKeyTemplate
                    .Replace("{agent}", ResolveAgentId(profile), StringComparison.Ordinal)
                    .Replace("{character}", modelFingerprintService.ComputeSha256(characterSourcePath), StringComparison.Ordinal),
                _ => $"vividsoul:{ResolveAgentId(profile)}:{modelFingerprintService.ComputeSha256(characterSourcePath)}",
            };
        }

        private string LoadRequiredToken(LlmProviderProfile profile)
        {
            var token = aiSecretsStore.LoadApiKey(profile.Id)?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(token))
            {
                PublishStatus(ConversationConnectionState.AuthFailed, "OpenClaw Token 为空", false);
                throw new UserFacingException("当前 OpenClaw Provider 的 Gateway Token 为空。请先在设置中保存。");
            }

            return token;
        }

        private static string BuildHttpModel(LlmProviderProfile profile)
        {
            return $"openclaw/{ResolveAgentId(profile)}";
        }

        private static string ResolveAgentId(LlmProviderProfile? profile)
        {
            return string.IsNullOrWhiteSpace(profile?.OpenClawAgentId)
                ? "main"
                : profile!.OpenClawAgentId.Trim();
        }

        private static Uri NormalizeGatewayUri(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                throw new ArgumentException("A gateway URL is required.", nameof(rawValue));
            }

            var normalized = rawValue.Trim();
            if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                normalized = $"ws://{normalized[7..]}";
            }
            else if (normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                normalized = $"wss://{normalized[8..]}";
            }
            else if (!normalized.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)
                     && !normalized.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            {
                normalized = $"ws://{normalized}";
            }

            return Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
                ? uri
                : throw new UserFacingException("OpenClaw Gateway WS URL 不是合法地址。");
        }

        private static ChatRole ParseChatRole(string? rawRole)
        {
            return rawRole?.Trim().ToLowerInvariant() switch
            {
                "assistant" => ChatRole.Assistant,
                "system" => ChatRole.System,
                _ => ChatRole.User,
            };
        }

        private static string ExtractMessageText(IReadOnlyDictionary<string, object?> messageElement)
        {
            var directText = GetString(messageElement, "text");
            if (!string.IsNullOrWhiteSpace(directText))
            {
                return directText;
            }

            if (messageElement.TryGetValue("content", out var contentElement))
            {
                return ExtractTextFromContent(contentElement);
            }

            if (messageElement.TryGetValue("body", out var bodyElement))
            {
                return ExtractTextFromContent(bodyElement);
            }

            return string.Empty;
        }

        private static string ExtractTextFromContent(object? contentElement)
        {
            return contentElement switch
            {
                null => string.Empty,
                string text => text,
                IList list => string.Join(
                    string.Empty,
                    list.Cast<object?>()
                        .Select(ExtractTextFromContent)
                        .Where(static item => !string.IsNullOrWhiteSpace(item))),
                IReadOnlyDictionary<string, object?> map => GetString(map, "text")
                                                            ?? GetString(map, "value")
                                                            ?? ExtractTextFromContent(GetValue(map, "content"))
                                                            ?? ExtractFirstNestedText(map),
                IDictionary dictionary => ExtractTextFromContent(ToDictionary(dictionary)),
                _ => string.Empty,
            };
        }

        private static string ExtractFirstNestedText(IReadOnlyDictionary<string, object?> contentElement)
        {
            foreach (var property in contentElement)
            {
                var nested = ExtractTextFromContent(property.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }

            return string.Empty;
        }

        private static DateTimeOffset ParseTimestamp(IReadOnlyDictionary<string, object?> messageElement)
        {
            var rawTimestamp = GetString(messageElement, "createdAt")
                               ?? GetString(messageElement, "createdAtUtc")
                               ?? GetString(messageElement, "timestamp");
            return DateTimeOffset.TryParse(rawTimestamp, out var parsedTimestamp)
                ? parsedTimestamp
                : DateTimeOffset.UtcNow;
        }

        private static string? GetString(IReadOnlyDictionary<string, object?> element, string propertyName)
        {
            return element.TryGetValue(propertyName, out var value) && value is string text
                ? text
                : null;
        }

        private static string? GetNestedString(IReadOnlyDictionary<string, object?> element, string parentName, string propertyName)
        {
            return GetMap(element, parentName) is { } parent
                ? GetString(parent, propertyName)
                : null;
        }

        private static IReadOnlyDictionary<string, object?>? GetMap(IReadOnlyDictionary<string, object?> element, string propertyName)
        {
            if (!element.TryGetValue(propertyName, out var value))
            {
                return null;
            }

            return value switch
            {
                IReadOnlyDictionary<string, object?> map => map,
                IDictionary dictionary => ToDictionary(dictionary),
                _ => null,
            };
        }

        private static object? GetValue(IReadOnlyDictionary<string, object?> element, string propertyName)
        {
            return element.TryGetValue(propertyName, out var value)
                ? value
                : null;
        }

        private static Dictionary<string, object?> ToDictionary(IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in dictionary)
            {
                result[entry.Key.ToString() ?? string.Empty] = entry.Value;
            }

            return result;
        }

        private static string BuildPreview(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "<empty>";
            }

            var normalized = value
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();
            return normalized.Length <= 180
                ? normalized
                : $"{normalized[..180]}...";
        }

        private void ThrowIfDisposed()
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(OpenClawConversationBackend));
            }
        }

        private sealed class PendingOptimisticUserMessage
        {
            public PendingOptimisticUserMessage(string text, DateTimeOffset createdAt)
            {
                Text = text;
                CreatedAt = createdAt;
            }

            public string Text { get; }

            public DateTimeOffset CreatedAt { get; }
        }

        private sealed class RecentAssistantMessage
        {
            public RecentAssistantMessage(string text, DateTimeOffset createdAt)
            {
                Text = text;
                CreatedAt = createdAt;
            }

            public string Text { get; }

            public DateTimeOffset CreatedAt { get; }
        }

        private sealed class AssistantTurnFailedException : Exception
        {
            public AssistantTurnFailedException(string message)
                : base(message)
            {
            }
        }
    }
}
