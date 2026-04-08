#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FairyGUI;
using UnityEngine;
using VividSoul.Runtime.AI;

namespace VividSoul.Runtime.App
{
    public sealed class DesktopPetFairyGuiChatPresenter : IDisposable
    {
        private const int MaxVisibleEntries = 20;
        private const float SettingsRefreshIntervalSeconds = 1.2f;

        private readonly Action<string> onUserMessageSubmitted;
        private readonly Action? onExpanded;
        private readonly Action<bool>? onVisibilityChanged;
        private readonly Action<string> statusReporter;
        private readonly IAiSettingsStore aiSettingsStore;
        private readonly List<ChatEntry> entries = new();

        private DesktopPetChatV2Window? window;
        private bool isRequestInFlight;
        private int unreadCount;
        private float nextSettingsRefreshAt;
        private string providerSummaryBaseText = string.Empty;
        private ConversationStatusSnapshot? currentConversationStatus;

        public DesktopPetFairyGuiChatPresenter(
            Action<string> statusReporter,
            Action<string> onUserMessageSubmitted,
            Action? onExpanded = null,
            Action<bool>? onVisibilityChanged = null)
        {
            this.statusReporter = statusReporter ?? throw new ArgumentNullException(nameof(statusReporter));
            this.onUserMessageSubmitted = onUserMessageSubmitted ?? throw new ArgumentNullException(nameof(onUserMessageSubmitted));
            this.onExpanded = onExpanded;
            this.onVisibilityChanged = onVisibilityChanged;
            aiSettingsStore = new AiSettingsStore();
        }

        public bool IsVisible => window is { isShowing: true };

        public bool BlocksBackgroundInteraction => IsVisible;

        public int UnreadCount => unreadCount;

        public void Show()
        {
            EnsureWindow();
            window!.Show();
        }

        public void Hide()
        {
            window?.Hide();
        }

        public void Dispose()
        {
            if (window == null)
            {
                return;
            }

            if (window.isShowing)
            {
                window.HideImmediately();
            }

            window.Dispose();
            window = null;
        }

        public void Update(float deltaTime)
        {
            _ = deltaTime;
            if (!IsVisible)
            {
                return;
            }

            if (Time.unscaledTime >= nextSettingsRefreshAt)
            {
                RefreshProviderSummary();
            }

            window!.SetRequestState(isRequestInFlight);
            window.SetUnreadCount(unreadCount);
            window.SetProviderSummaryText(BuildProviderSummaryText());

            if (Input.GetKeyDown(KeyCode.Escape) && !window.IsInputFocused)
            {
                Hide();
            }
        }

        public void AppendMateMessage(string message)
        {
            AppendMessage(MessageRole.Mate, "VividSoul", message);
        }

        public void AppendUserMessage(string message)
        {
            AppendMessage(MessageRole.User, "你", message);
        }

        public void AppendSystemMessage(string message)
        {
            AppendMessage(MessageRole.System, "系统", message);
        }

        public void SetRequestInFlight(bool value)
        {
            isRequestInFlight = value;
            window?.SetRequestState(value);
            window?.SetProviderSummaryText(BuildProviderSummaryText());
        }

        public void SetConversationStatus(ConversationStatusSnapshot status)
        {
            currentConversationStatus = status;
            unreadCount = Mathf.Max(0, status.UnreadCount);
            if (IsVisible)
            {
                unreadCount = 0;
            }

            window?.SetUnreadCount(unreadCount);
            window?.SetProviderSummaryText(BuildProviderSummaryText());
        }

        private void EnsureWindow()
        {
            if (window != null)
            {
                return;
            }

            DesktopPetFairyGuiBridge.EnsureInitialized();
            window = new DesktopPetChatV2Window(this);
            RefreshProviderSummary();
            window.SetTranscriptText(BuildTranscriptMarkup());
            window.SetProviderSummaryText(BuildProviderSummaryText());
            window.SetRequestState(isRequestInFlight);
            window.SetUnreadCount(unreadCount);
        }

        private void OnWindowShown()
        {
            unreadCount = 0;
            window?.SetUnreadCount(0);
            onVisibilityChanged?.Invoke(true);
            onExpanded?.Invoke();
            RefreshProviderSummary();
            window?.SetTranscriptText(BuildTranscriptMarkup());
            window?.FocusInput();
        }

        private void OnWindowHidden()
        {
            onVisibilityChanged?.Invoke(false);
        }

        private void AppendMessage(MessageRole role, string author, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            entries.Add(new ChatEntry(role, author, text.Trim(), DateTimeOffset.Now));
            while (entries.Count > MaxVisibleEntries)
            {
                entries.RemoveAt(0);
            }

            if (!IsVisible && role == MessageRole.Mate)
            {
                unreadCount++;
            }

            window?.SetUnreadCount(unreadCount);
            window?.SetTranscriptText(BuildTranscriptMarkup());
        }

        private void ClearVisibleHistory()
        {
            entries.Clear();
            AppendSystemMessage("聊天 V2 视图已清空，底层会话数据未删除。");
            statusReporter("聊天 V2 视图已清空。");
        }

        private void TrySubmitInput()
        {
            if (window == null)
            {
                return;
            }

            var text = window.InputText.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                window.FocusInput();
                return;
            }

            window.InputText = string.Empty;
            onUserMessageSubmitted(text);
            window.FocusInput();
        }

        private void RefreshProviderSummary()
        {
            var settings = aiSettingsStore.Load();
            var activeProfile = settings.ProviderProfiles.FirstOrDefault(profile =>
                                    string.Equals(profile.Id, settings.ActiveProviderId, StringComparison.OrdinalIgnoreCase))
                                ?? settings.ProviderProfiles.FirstOrDefault();
            providerSummaryBaseText = currentConversationStatus == null
                ? activeProfile == null
                    ? "当前未配置 Provider"
                    : $"当前 Provider：{activeProfile.DisplayName} | {FormatProviderType(activeProfile.ProviderType)} | {(activeProfile.Enabled ? "已启用" : "未启用")}"
                : BuildConversationStatusSummary(currentConversationStatus);
            nextSettingsRefreshAt = Time.unscaledTime + SettingsRefreshIntervalSeconds;
            window?.SetProviderSummaryText(BuildProviderSummaryText());
        }

        private string BuildProviderSummaryText()
        {
            var suffix = isRequestInFlight
                ? " | 正在请求回复..."
                : " | FairyGUI 试点窗口";
            return $"{providerSummaryBaseText}{suffix}";
        }

        private string BuildTranscriptMarkup()
        {
            if (entries.Count == 0)
            {
                return "[color=#94A3B8]聊天 V2 已接通。这里会显示最近的对话内容。[/color]";
            }

            var builder = new StringBuilder(entries.Count * 96);
            foreach (var entry in entries)
            {
                builder.Append("[b][color=");
                builder.Append(GetRoleColorHex(entry.Role));
                builder.Append(']');
                builder.Append(EscapeMarkup(entry.Author));
                builder.Append("[/color][/b] ");
                builder.Append("[color=#94A3B8]");
                builder.Append(entry.Timestamp.ToLocalTime().ToString("HH:mm"));
                builder.Append("[/color]\n");
                builder.Append(EscapeMarkup(entry.Text));
                builder.Append("\n\n");
            }

            return builder.ToString().TrimEnd();
        }

        private static string EscapeMarkup(string text)
        {
            return text
                .Replace("[", "［", StringComparison.Ordinal)
                .Replace("]", "］", StringComparison.Ordinal);
        }

        private static string GetRoleColorHex(MessageRole role)
        {
            return role switch
            {
                MessageRole.User => "#9FD0FF",
                MessageRole.Mate => "#F4C6DD",
                _ => "#B7C4D6",
            };
        }

        private static string BuildConversationStatusSummary(ConversationStatusSnapshot status)
        {
            var baseText = $"当前 Provider：{status.ProviderDisplayName} | {FormatProviderType(status.ProviderType)} | {FormatConnectionState(status.ConnectionState)}";
            if (status.ProviderType == LlmProviderType.OpenClaw)
            {
                var sessionText = string.IsNullOrWhiteSpace(status.SessionKey) ? "未绑定 Session" : $"Session: {status.SessionKey}";
                var agentText = string.IsNullOrWhiteSpace(status.AgentId) ? "Agent: main" : $"Agent: {status.AgentId}";
                return $"{baseText} | {agentText} | {sessionText}";
            }

            return baseText;
        }

        private static string FormatConnectionState(ConversationConnectionState state)
        {
            return state switch
            {
                ConversationConnectionState.Connected => "已连接",
                ConversationConnectionState.Connecting => "连接中",
                ConversationConnectionState.Reconnecting => "重连中",
                ConversationConnectionState.AuthFailed => "鉴权失败",
                ConversationConnectionState.Faulted => "异常",
                _ => "未连接",
            };
        }

        private static string FormatProviderType(LlmProviderType providerType)
        {
            return providerType switch
            {
                LlmProviderType.OpenAiCompatible => "OpenAI Compatible",
                LlmProviderType.MiniMax => "MiniMax",
                LlmProviderType.Anthropic => "Anthropic",
                LlmProviderType.Gemini => "Gemini",
                LlmProviderType.Ollama => "Ollama",
                LlmProviderType.OpenClaw => "OpenClaw",
                _ => providerType.ToString(),
            };
        }

        private sealed record ChatEntry(
            MessageRole Role,
            string Author,
            string Text,
            DateTimeOffset Timestamp);

        private enum MessageRole
        {
            User = 0,
            Mate = 1,
            System = 2,
        }

        private sealed class DesktopPetChatV2Window : Window
        {
            private const float WindowWidth = 620f;
            private const float WindowHeight = 700f;
            private const float HorizontalPadding = 18f;
            private const float HeaderHeight = 88f;
            private const float TranscriptHeight = 420f;
            private const float InputHeight = 42f;
            private const float ButtonHeight = 40f;
            private const float ButtonWidth = 112f;

            private static readonly Color PanelColor = new(0.08f, 0.12f, 0.18f, 0.98f);
            private static readonly Color PanelBorderColor = new(0.30f, 0.38f, 0.52f, 1f);
            private static readonly Color HeaderColor = new(0.11f, 0.16f, 0.24f, 1f);
            private static readonly Color CardColor = new(0.10f, 0.14f, 0.20f, 0.96f);
            private static readonly Color InputColor = new(0.07f, 0.10f, 0.16f, 1f);
            private static readonly Color PrimaryActionColor = new(0.22f, 0.42f, 0.75f, 1f);
            private static readonly Color SecondaryActionColor = new(0.21f, 0.27f, 0.37f, 1f);
            private static readonly Color DisabledActionColor = new(0.16f, 0.19f, 0.25f, 1f);
            private static readonly Color TitleColor = new(0.97f, 0.98f, 1f, 1f);
            private static readonly Color PrimaryTextColor = new(0.92f, 0.96f, 1f, 1f);
            private static readonly Color SecondaryTextColor = new(0.71f, 0.79f, 0.89f, 1f);
            private static readonly Color BadgeColor = new(0.84f, 0.31f, 0.49f, 1f);

            private readonly DesktopPetFairyGuiChatPresenter owner;

            private GRichTextField? transcriptText;
            private GTextInput? inputField;
            private GTextField? providerSummaryText;
            private GComponent? unreadBadgeRoot;
            private GTextField? unreadBadgeText;
            private ChatButton? sendButton;

            public DesktopPetChatV2Window(DesktopPetFairyGuiChatPresenter owner)
            {
                this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
                bringToFontOnClick = true;
            }

            public bool IsInputFocused => inputField is { focused: true };

            public string InputText
            {
                get => inputField?.text ?? string.Empty;
                set
                {
                    if (inputField != null)
                    {
                        inputField.text = value ?? string.Empty;
                    }
                }
            }

            public void SetTranscriptText(string markup)
            {
                if (transcriptText != null)
                {
                    transcriptText.text = markup ?? string.Empty;
                }
            }

            public void SetProviderSummaryText(string text)
            {
                if (providerSummaryText != null)
                {
                    providerSummaryText.text = text ?? string.Empty;
                }
            }

            public void SetUnreadCount(int count)
            {
                if (unreadBadgeRoot == null || unreadBadgeText == null)
                {
                    return;
                }

                var hasUnread = count > 0;
                unreadBadgeRoot.visible = hasUnread;
                unreadBadgeText.text = hasUnread ? $"未读 {count}" : string.Empty;
            }

            public void SetRequestState(bool isRequestInFlight)
            {
                if (inputField != null)
                {
                    inputField.editable = !isRequestInFlight;
                }

                sendButton?.SetEnabled(!isRequestInFlight && !string.IsNullOrWhiteSpace(InputText), isRequestInFlight ? "发送中" : "发送");
            }

            public void FocusInput()
            {
                inputField?.RequestFocus();
            }

            protected override void OnInit()
            {
                contentPane = BuildContentPane();
                CenterOn(GRoot.inst, true);
            }

            protected override void OnShown()
            {
                CenterOn(GRoot.inst, false);
                owner.OnWindowShown();
            }

            protected override void OnHide()
            {
                owner.OnWindowHidden();
            }

            private GComponent BuildContentPane()
            {
                var root = new GComponent();
                root.SetSize(WindowWidth, WindowHeight);

                var panelBackground = CreatePanelBackground(WindowWidth, WindowHeight, PanelColor, PanelBorderColor);
                root.AddChild(panelBackground);

                var header = CreatePanelContainer(WindowWidth - (HorizontalPadding * 2f), HeaderHeight, HeaderColor, PanelBorderColor);
                header.SetXY(HorizontalPadding, HorizontalPadding);
                root.AddChild(header);

                var title = CreateText("聊天 V2", 26, TitleColor);
                title.SetXY(20f, 16f);
                title.SetSize(220f, 32f);
                header.AddChild(title);

                providerSummaryText = CreateText(string.Empty, 14, SecondaryTextColor);
                providerSummaryText.SetXY(20f, 50f);
                providerSummaryText.SetSize(header.width - 160f, 24f);
                header.AddChild(providerSummaryText);

                var badge = CreateBadge();
                unreadBadgeRoot = badge.Root;
                unreadBadgeText = badge.Label;
                unreadBadgeRoot.SetXY(header.width - 122f, 20f);
                header.AddChild(unreadBadgeRoot);

                var transcriptCard = CreatePanelContainer(WindowWidth - (HorizontalPadding * 2f), TranscriptHeight, CardColor, PanelBorderColor);
                transcriptCard.SetXY(HorizontalPadding, header.y + header.height + 14f);
                root.AddChild(transcriptCard);

                transcriptText = new GRichTextField
                {
                    UBBEnabled = true,
                };
                transcriptText.SetXY(16f, 14f);
                transcriptText.SetSize(transcriptCard.width - 32f, transcriptCard.height - 28f);
                transcriptText.touchable = false;
                transcriptText.textFormat = CreateTextFormat(16, PrimaryTextColor);
                transcriptCard.AddChild(transcriptText);

                var footerHint = CreateText("FairyGUI 试点窗口，当前保留最近的聊天内容。", 13, SecondaryTextColor);
                footerHint.SetXY(HorizontalPadding + 4f, transcriptCard.y + transcriptCard.height + 8f);
                footerHint.SetSize(WindowWidth - (HorizontalPadding * 2f), 20f);
                root.AddChild(footerHint);

                var inputBackground = CreatePanelContainer(WindowWidth - (HorizontalPadding * 2f), 78f, CardColor, PanelBorderColor);
                inputBackground.SetXY(HorizontalPadding, WindowHeight - 144f);
                root.AddChild(inputBackground);

                inputField = new GTextInput
                {
                    promptText = "输入你想对 VividSoul 说的话",
                    editable = true,
                };
                inputField.SetXY(16f, 18f);
                inputField.SetSize(inputBackground.width - 32f, InputHeight);
                inputField.touchable = true;
                inputField.textFormat = CreateTextFormat(16, PrimaryTextColor);
                inputField.backgroundColor = InputColor;
                inputField.borderColor = PanelBorderColor;
                inputField.border = 1;
                inputField.corner = 8;
                inputField.onChanged.Add(HandleInputChanged);
                inputField.onSubmit.Add(owner.TrySubmitInput);
                inputBackground.AddChild(inputField);

                var clearButtonComponent = CreateButton("清空", SecondaryActionColor);
                clearButtonComponent.Root.SetXY(WindowWidth - 372f, WindowHeight - 54f);
                clearButtonComponent.Background.onClick.Add(owner.ClearVisibleHistory);
                root.AddChild(clearButtonComponent.Root);

                var closeButtonComponent = CreateButton("关闭", SecondaryActionColor);
                closeButtonComponent.Root.SetXY(WindowWidth - 248f, WindowHeight - 54f);
                closeButton = closeButtonComponent.Root;
                closeButtonComponent.Background.onClick.Add(Hide);
                root.AddChild(closeButtonComponent.Root);

                sendButton = CreateButton("发送", PrimaryActionColor);
                sendButton.Root.SetXY(WindowWidth - 124f, WindowHeight - 54f);
                sendButton.Background.onClick.Add(owner.TrySubmitInput);
                root.AddChild(sendButton.Root);

                return root;
            }

            private void HandleInputChanged()
            {
                sendButton?.SetEnabled(!string.IsNullOrWhiteSpace(InputText), "发送");
            }

            private static GGraph CreatePanelBackground(float width, float height, Color fillColor, Color borderColor)
            {
                var panel = new GGraph();
                panel.DrawRect(width, height, 1, borderColor, fillColor);
                panel.SetSize(width, height);
                panel.touchable = false;
                return panel;
            }

            private static GComponent CreatePanelContainer(float width, float height, Color fillColor, Color borderColor)
            {
                var container = new GComponent();
                container.SetSize(width, height);
                container.AddChild(CreatePanelBackground(width, height, fillColor, borderColor));
                return container;
            }

            private static GTextField CreateText(string text, int fontSize, Color color)
            {
                var field = new GTextField
                {
                    text = text,
                    touchable = false,
                };
                field.SetSize(200f, fontSize + 10f);
                field.textFormat = CreateTextFormat(fontSize, color);
                return field;
            }

            private static BadgeUi CreateBadge()
            {
                var root = new GComponent();
                root.visible = false;
                root.SetSize(102f, 26f);
                var background = new GGraph();
                background.DrawRoundRect(root.width, root.height, BadgeColor, new[] { 12f, 12f, 12f, 12f });
                background.SetSize(root.width, root.height);
                background.touchable = false;
                root.AddChild(background);

                var label = CreateText(string.Empty, 13, TitleColor);
                label.SetSize(root.width, root.height);
                label.textFormat = CreateTextFormat(13, TitleColor, AlignType.Center);
                label.verticalAlign = VertAlignType.Middle;
                root.AddChild(label);

                return new BadgeUi(root, label);
            }

            private static TextFormat CreateTextFormat(int size, Color color, AlignType align = AlignType.Left)
            {
                return new TextFormat
                {
                    font = UIConfig.defaultFont,
                    size = size,
                    color = color,
                    align = align,
                    lineSpacing = 4,
                };
            }

            private static ChatButton CreateButton(string label, Color backgroundColor)
            {
                var root = new GComponent();
                root.SetSize(ButtonWidth, ButtonHeight);

                var background = new GGraph();
                background.DrawRoundRect(ButtonWidth, ButtonHeight, backgroundColor, new[] { 10f, 10f, 10f, 10f });
                background.SetSize(ButtonWidth, ButtonHeight);
                background.touchable = true;
                root.AddChild(background);

                var title = CreateText(label, 15, TitleColor);
                title.SetSize(ButtonWidth, ButtonHeight);
                title.textFormat = CreateTextFormat(15, TitleColor, AlignType.Center);
                title.verticalAlign = VertAlignType.Middle;
                root.AddChild(title);

                return new ChatButton(root, background, title);
            }

            private sealed class ChatButton
            {
                private readonly Color enabledColor;

                public ChatButton(GComponent root, GGraph background, GTextField title)
                {
                    Root = root;
                    Background = background;
                    Title = title;
                    enabledColor = background.color;
                }

                public GComponent Root { get; }

                public GGraph Background { get; }

                public GTextField Title { get; }

                public void SetEnabled(bool enabled, string label)
                {
                    Title.text = label;
                    Background.color = enabled ? enabledColor : DisabledActionColor;
                    Background.touchable = enabled;
                }
            }

            private sealed class BadgeUi
            {
                public BadgeUi(GComponent root, GTextField label)
                {
                    Root = root;
                    Label = label;
                }

                public GComponent Root { get; }

                public GTextField Label { get; }
            }
        }

        private static class DesktopPetFairyGuiBridge
        {
            private const int DesignResolutionX = 1920;
            private const int DesignResolutionY = 1080;
            private const float StageCameraDepth = 10f;

            private static bool initialized;

            public static void EnsureInitialized()
            {
                var root = GRoot.inst;
                if (initialized)
                {
                    return;
                }

                UIConfig.defaultFont = RuntimeUiFontResolver.GetFairyGuiFontFamilyChain();
                root.SetContentScaleFactor(DesignResolutionX, DesignResolutionY, UIContentScaler.ScreenMatchMode.MatchWidthOrHeight);
                Stage.inst.gameObject.name = "VividSoulFairyGuiStage";
                if (StageCamera.main != null)
                {
                    StageCamera.main.depth = Mathf.Max(StageCamera.main.depth, StageCameraDepth);
                }

                initialized = true;
            }
        }
    }
}
