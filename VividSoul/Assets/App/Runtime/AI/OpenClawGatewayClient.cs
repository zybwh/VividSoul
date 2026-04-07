#nullable enable

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace VividSoul.Runtime.AI
{
    public sealed class OpenClawGatewayClient : IDisposable
    {
        private const int ProtocolVersion = 3;
        private const string CompatibleClientId = "cli";
        private const string CompatibleClientMode = "cli";
        private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(15);

        private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> pendingRequests = new();
        private readonly SemaphoreSlim sendLock = new(1, 1);

        private ClientWebSocket? socket;
        private CancellationTokenSource? receiveLoopCancellationTokenSource;
        private TaskCompletionSource<string>? challengeCompletionSource;
        private bool isDisposed;

        public event Action<string, string>? EventReceived;

        public event Action<Exception>? ConnectionFaulted;

        public event Action<WebSocketCloseStatus?, string>? ConnectionClosed;

        public WebSocketState State => socket?.State ?? WebSocketState.None;

        public async Task ConnectAsync(Uri gatewayUri, string token, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (gatewayUri == null)
            {
                throw new ArgumentNullException(nameof(gatewayUri));
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentException("A gateway token is required.", nameof(token));
            }

            await CloseAsync(CancellationToken.None);
            var normalizedToken = NormalizeToken(token);

            socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            challengeCompletionSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            receiveLoopCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            await socket.ConnectAsync(gatewayUri, cancellationToken);
            _ = Task.Run(() => ReceiveLoopAsync(socket, receiveLoopCancellationTokenSource.Token), CancellationToken.None);

            _ = await WaitForChallengeAsync(cancellationToken);
            await SendRequestAsync(
                "connect",
                new Dictionary<string, object?>
                {
                    ["minProtocol"] = ProtocolVersion,
                    ["maxProtocol"] = ProtocolVersion,
                    ["client"] = new Dictionary<string, object?>
                    {
                        ["id"] = CompatibleClientId,
                        ["version"] = Application.version,
                        ["platform"] = Application.platform.ToString(),
                        ["mode"] = CompatibleClientMode,
                    },
                    ["role"] = "operator",
                    ["scopes"] = new[] { "operator.read", "operator.write" },
                    ["caps"] = Array.Empty<string>(),
                    ["commands"] = Array.Empty<string>(),
                    ["permissions"] = new Dictionary<string, object?>(),
                    ["auth"] = new Dictionary<string, object?>
                    {
                        ["token"] = normalizedToken,
                    },
                    ["locale"] = Application.systemLanguage.ToString(),
                    ["userAgent"] = $"vividsoul/{Application.version}",
                },
                cancellationToken);
        }

        public async Task<string> SendRequestAsync(string method, object parameters, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(method))
            {
                throw new ArgumentException("A method is required.", nameof(method));
            }

            var activeSocket = socket;
            if (activeSocket == null || activeSocket.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("OpenClaw gateway WebSocket is not connected.");
            }

            var id = Guid.NewGuid().ToString("N");
            var requestCompletionSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingRequests[id] = requestCompletionSource;

            try
            {
                var requestJson = MiniJson.Serialize(new Dictionary<string, object?>
                {
                    ["type"] = "req",
                    ["id"] = id,
                    ["method"] = method,
                    ["params"] = parameters,
                });
                var requestBytes = Encoding.UTF8.GetBytes(requestJson);

                await sendLock.WaitAsync(cancellationToken);
                try
                {
                    await activeSocket.SendAsync(
                        new ArraySegment<byte>(requestBytes),
                        WebSocketMessageType.Text,
                        true,
                        cancellationToken);
                }
                finally
                {
                    sendLock.Release();
                }

                using var timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCancellationTokenSource.CancelAfter(ReceiveTimeout);
                using var registration = timeoutCancellationTokenSource.Token.Register(
                    () => requestCompletionSource.TrySetCanceled(timeoutCancellationTokenSource.Token));
                return await requestCompletionSource.Task;
            }
            finally
            {
                pendingRequests.TryRemove(id, out _);
            }
        }

        public async Task CloseAsync(CancellationToken cancellationToken)
        {
            var activeSocket = socket;
            socket = null;

            if (receiveLoopCancellationTokenSource != null)
            {
                receiveLoopCancellationTokenSource.Cancel();
                receiveLoopCancellationTokenSource.Dispose();
                receiveLoopCancellationTokenSource = null;
            }

            foreach (var pendingRequest in pendingRequests.Values)
            {
                pendingRequest.TrySetCanceled(cancellationToken);
            }

            pendingRequests.Clear();
            challengeCompletionSource = null;

            if (activeSocket == null)
            {
                return;
            }

            try
            {
                if (activeSocket.State == WebSocketState.Open || activeSocket.State == WebSocketState.CloseReceived)
                {
                    await activeSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "client-close", cancellationToken);
                }
            }
            catch
            {
            }
            finally
            {
                activeSocket.Dispose();
            }
        }

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            sendLock.Dispose();
            receiveLoopCancellationTokenSource?.Cancel();
            receiveLoopCancellationTokenSource?.Dispose();
            socket?.Dispose();
        }

        private async Task<string> WaitForChallengeAsync(CancellationToken cancellationToken)
        {
            var challengeSource = challengeCompletionSource ?? throw new InvalidOperationException("Challenge wait was not initialized.");
            using var timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellationTokenSource.CancelAfter(ReceiveTimeout);
            using var registration = timeoutCancellationTokenSource.Token.Register(
                () => challengeSource.TrySetCanceled(timeoutCancellationTokenSource.Token));
            return await challengeSource.Task;
        }

        private async Task ReceiveLoopAsync(ClientWebSocket activeSocket, CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            using var stream = new MemoryStream();

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    stream.SetLength(0);
                    WebSocketReceiveResult receiveResult;
                    do
                    {
                        receiveResult = await activeSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                        if (receiveResult.MessageType == WebSocketMessageType.Close)
                        {
                            ConnectionClosed?.Invoke(activeSocket.CloseStatus, activeSocket.CloseStatusDescription ?? string.Empty);
                            return;
                        }

                        stream.Write(buffer, 0, receiveResult.Count);
                    }
                    while (!receiveResult.EndOfMessage);

                    ProcessIncomingFrame(Encoding.UTF8.GetString(stream.ToArray()));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[OpenClawWs] receive loop fault error={exception.Message}");
                ConnectionFaulted?.Invoke(exception);
            }
        }

        private void ProcessIncomingFrame(string payload)
        {
            if (MiniJson.Deserialize(payload) is not Dictionary<string, object?> frame)
            {
                return;
            }

            var frameType = GetString(frame, "type");
            if (string.Equals(frameType, "event", StringComparison.OrdinalIgnoreCase))
            {
                var eventName = GetString(frame, "event");
                var payloadJson = frame.TryGetValue("payload", out var eventPayload)
                    ? MiniJson.Serialize(eventPayload)
                    : "{}";
                if (string.Equals(eventName, "connect.challenge", StringComparison.OrdinalIgnoreCase))
                {
                    challengeCompletionSource?.TrySetResult(payloadJson);
                }

                EventReceived?.Invoke(eventName, payloadJson);
                return;
            }

            if (!string.Equals(frameType, "res", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var id = GetString(frame, "id");
            if (string.IsNullOrWhiteSpace(id) || !pendingRequests.TryGetValue(id, out var requestCompletionSource))
            {
                return;
            }

            var isOk = frame.TryGetValue("ok", out var okValue) && okValue is bool ok && ok;
            if (isOk)
            {
                var responsePayload = frame.TryGetValue("payload", out var payloadValue)
                    ? MiniJson.Serialize(payloadValue)
                    : string.Empty;
                requestCompletionSource.TrySetResult(responsePayload);
                return;
            }

            var errorPreview = BuildPreview(MiniJson.Serialize(frame.TryGetValue("error", out var errorPayload) ? errorPayload : null));
            Debug.LogWarning($"[OpenClawWs] response failed requestId={id} error={errorPreview}");
            requestCompletionSource.TrySetException(
                new InvalidOperationException(FormatErrorMessage(frame.TryGetValue("error", out var errorValue) ? errorValue : null)));
        }

        private static string GetString(IReadOnlyDictionary<string, object?> values, string key)
        {
            return values.TryGetValue(key, out var value) && value is string text
                ? text
                : string.Empty;
        }

        private static string FormatErrorMessage(object? errorValue)
        {
            const string defaultMessage = "OpenClaw gateway request failed.";
            if (errorValue is Dictionary<string, object?> errorMap)
            {
                var message = GetOptionalString(errorMap, "message") ?? defaultMessage;
                if (errorMap.TryGetValue("details", out var detailsValue)
                    && detailsValue is Dictionary<string, object?> detailsMap)
                {
                    var code = GetOptionalString(detailsMap, "code");
                    var reason = GetOptionalString(detailsMap, "reason");
                    var detailSuffix = string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(reason)
                        ? string.Empty
                        : $" ({code ?? "unknown-code"} / {reason ?? "unknown-reason"})";
                    return $"{message}{detailSuffix}";
                }

                return message;
            }

            return errorValue is string errorText && !string.IsNullOrWhiteSpace(errorText)
                ? errorText
                : defaultMessage;
        }

        private static string? GetOptionalString(IReadOnlyDictionary<string, object?> values, string key)
        {
            return values.TryGetValue(key, out var value) && value is string text && !string.IsNullOrWhiteSpace(text)
                ? text
                : null;
        }

        private void ThrowIfDisposed()
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(OpenClawGatewayClient));
            }
        }

        private static string NormalizeToken(string token)
        {
            var normalized = token.Trim().Trim('"', '\'');
            if (normalized.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[7..].Trim();
            }

            return normalized
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal)
                .Replace("\t", string.Empty, StringComparison.Ordinal)
                .Trim();
        }

        private static string BuildPreview(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "<empty>";
            }

            var normalized = value
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();
            return normalized.Length <= 220
                ? normalized
                : $"{normalized[..220]}...";
        }
    }
}
