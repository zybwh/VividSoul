#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VividSoul.Runtime.AI;

namespace VividSoul.Runtime.App
{
    public sealed class DesktopPetChatOverlayPresenter
    {
        private static readonly Color PanelColor = new(0.10f, 0.14f, 0.20f, 0.98f);
        private static readonly Color PanelBorderColor = new(0.29f, 0.38f, 0.50f, 1f);
        private static readonly Color HeaderColor = new(0.12f, 0.17f, 0.24f, 1f);
        private static readonly Color ComposerColor = new(0.13f, 0.18f, 0.25f, 0.98f);
        private static readonly Color InputColor = new(0.07f, 0.10f, 0.15f, 1f);
        private static readonly Color ActionColor = new(0.24f, 0.39f, 0.59f, 1f);
        private static readonly Color ActionMutedColor = new(0.23f, 0.28f, 0.35f, 1f);
        private static readonly Color UserBubbleColor = new(0.23f, 0.35f, 0.54f, 1f);
        private static readonly Color MateBubbleColor = new(0.18f, 0.24f, 0.33f, 1f);
        private static readonly Color SystemBubbleColor = new(0.16f, 0.19f, 0.25f, 0.92f);
        private static readonly Color TextPrimaryColor = new(0.95f, 0.97f, 1f, 1f);
        private static readonly Color TextSecondaryColor = new(0.73f, 0.80f, 0.88f, 1f);
        private static readonly Color TextMutedColor = new(0.56f, 0.63f, 0.72f, 1f);

        private const float PanelWidth = 520f;
        private const float PanelHeight = 560f;
        private const float ScreenMargin = 26f;
        private const float ComposerHeight = 46f;
        private const float MessageBubbleWidth = 336f;
        private const float SystemBubbleWidth = 420f;
        private const float SettingsRefreshIntervalSeconds = 1.2f;
        private const int UiFontSize = 16;

        private readonly Action<string> onUserMessageSubmitted;
        private readonly Action? onExpanded;
        private readonly Action<string> statusReporter;
        private readonly IAiSettingsStore aiSettingsStore;
        private readonly List<ChatEntry> entries = new();

        private ChatOverlayUi? chatUi;
        private bool isExpanded;
        private bool isRequestInFlight;
        private int unreadCount;
        private float nextSettingsRefreshAt;
        private string providerSummaryBaseText = string.Empty;
        private ConversationStatusSnapshot? currentConversationStatus;

        public DesktopPetChatOverlayPresenter(Action<string> statusReporter, Action<string> onUserMessageSubmitted, Action? onExpanded = null)
        {
            this.statusReporter = statusReporter ?? throw new ArgumentNullException(nameof(statusReporter));
            this.onUserMessageSubmitted = onUserMessageSubmitted ?? throw new ArgumentNullException(nameof(onUserMessageSubmitted));
            this.onExpanded = onExpanded;
            aiSettingsStore = new AiSettingsStore();
        }

        public bool IsExpanded => isExpanded;

        public bool BlocksBackgroundInteraction => chatUi != null
            && chatUi.Root.gameObject.activeSelf
            && chatUi.PanelRoot.gameObject.activeSelf;

        public int UnreadCount => unreadCount;

        public void Attach(Canvas canvas)
        {
            if (canvas == null)
            {
                throw new ArgumentNullException(nameof(canvas));
            }

            EnsureUi(canvas);
            SetExpanded(false, focusInput: false);
        }

        public void Show(Canvas canvas)
        {
            if (canvas == null)
            {
                throw new ArgumentNullException(nameof(canvas));
            }

            EnsureUi(canvas);
            Expand();
        }

        public void Hide()
        {
            if (chatUi == null)
            {
                return;
            }

            chatUi.Root.gameObject.SetActive(false);
        }

        public void Dispose()
        {
        }

        public void Update(float deltaTime)
        {
            _ = deltaTime;
            if (chatUi == null || !chatUi.Root.gameObject.activeSelf)
            {
                return;
            }

            if (Time.unscaledTime >= nextSettingsRefreshAt)
            {
                RefreshProviderSummary();
            }

            RefreshSendButtonState();
            if (isExpanded
                && Input.GetKeyDown(KeyCode.Escape)
                && !chatUi.InputField.isFocused)
            {
                Collapse();
            }
        }

        public void Expand()
        {
            SetExpanded(true, focusInput: true);
        }

        public void Collapse()
        {
            SetExpanded(false, focusInput: false);
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
            if (chatUi == null)
            {
                return;
            }

            chatUi.InputField.interactable = !value;
            chatUi.ClearButton.interactable = !value;
            chatUi.CollapseButton.interactable = true;
            RefreshSendButtonState();
            ApplyProviderSummaryText();
        }

        public void SetConversationStatus(ConversationStatusSnapshot status)
        {
            currentConversationStatus = status;
            unreadCount = Mathf.Max(0, status.UnreadCount);
            if (isExpanded)
            {
                unreadCount = 0;
            }

            ApplyProviderSummaryText();
        }

        private void EnsureUi(Canvas canvas)
        {
            if (chatUi != null)
            {
                if (chatUi.Root.parent != canvas.transform)
                {
                    chatUi.Root.SetParent(canvas.transform, false);
                }

                return;
            }

            var rootObject = new GameObject("VividSoulChatOverlay", typeof(RectTransform));
            var root = rootObject.GetComponent<RectTransform>();
            root.SetParent(canvas.transform, false);
            StretchRect(root);
            var panelRoot = CreatePanel(root, out var providerSummaryText, out var historyScrollRect, out var historyContent, out var inputField, out var sendButton, out var clearButton, out var collapseButton);

            chatUi = new ChatOverlayUi(
                root,
                panelRoot,
                providerSummaryText,
                historyScrollRect,
                historyContent,
                inputField,
                sendButton,
                FindRequiredText(sendButton.transform),
                clearButton,
                collapseButton);

            sendButton.onClick.AddListener(TrySubmitInput);
            clearButton.onClick.AddListener(ClearHistory);
            collapseButton.onClick.AddListener(Collapse);
            inputField.onValueChanged.AddListener(_ => RefreshSendButtonState());
            inputField.onEndEdit.AddListener(HandleInputEndEdit);

            SetExpanded(false, focusInput: false);
            AppendSystemMessage("聊天交互面板已接通。配置有效时，发送内容会直接请求当前激活的 LLM Provider。");
        }

        private RectTransform CreatePanel(
            Transform parent,
            out Text providerSummaryText,
            out ScrollRect historyScrollRect,
            out RectTransform historyContent,
            out InputField inputField,
            out Button sendButton,
            out Button clearButton,
            out Button collapseButton)
        {
            var panelObject = new GameObject(
                "Panel",
                typeof(RectTransform),
                typeof(Image),
                typeof(VerticalLayoutGroup));
            var panel = panelObject.GetComponent<RectTransform>();
            panel.SetParent(parent, false);
            panel.anchorMin = new Vector2(1f, 0f);
            panel.anchorMax = new Vector2(1f, 0f);
            panel.pivot = new Vector2(1f, 0f);
            panel.anchoredPosition = new Vector2(-ScreenMargin, ScreenMargin);
            panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            var panelImage = panelObject.GetComponent<Image>();
            panelImage.color = PanelColor;
            panelImage.raycastTarget = true;

            var panelOutline = panelObject.AddComponent<Outline>();
            panelOutline.effectColor = PanelBorderColor;
            panelOutline.effectDistance = new Vector2(1f, -1f);

            var panelLayout = panelObject.GetComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(18, 18, 18, 18);
            panelLayout.spacing = 14f;
            panelLayout.childControlWidth = true;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childControlHeight = false;
            panelLayout.childForceExpandHeight = false;

            var header = CreateLayoutContainer(panel, "Header", isHorizontal: true, 12f, new RectOffset(14, 14, 14, 14));
            header.gameObject.AddComponent<Image>().color = HeaderColor;
            var headerLayout = header.gameObject.AddComponent<LayoutElement>();
            headerLayout.preferredHeight = 74f;

            var headerTextBlock = CreateLayoutContainer(header, "HeaderTextBlock", isHorizontal: false, 4f, new RectOffset(0, 0, 0, 0), fitToContents: true);
            var headerTextLayout = headerTextBlock.gameObject.AddComponent<LayoutElement>();
            headerTextLayout.flexibleWidth = 1f;
            CreateText(headerTextBlock, "Title", "聊天", 22, FontStyle.Bold, TextPrimaryColor, TextAnchor.MiddleLeft);
            providerSummaryText = CreateText(headerTextBlock, "ProviderSummary", string.Empty, 13, FontStyle.Normal, TextSecondaryColor, TextAnchor.UpperLeft);
            providerSummaryText.horizontalOverflow = HorizontalWrapMode.Wrap;
            providerSummaryText.verticalOverflow = VerticalWrapMode.Overflow;

            collapseButton = CreateButton(header, "收起", ActionMutedColor, 40f, 88f);

            historyScrollRect = CreateHistoryScrollView(panel, out historyContent);
            var historyScrollLayout = historyScrollRect.gameObject.AddComponent<LayoutElement>();
            historyScrollLayout.flexibleHeight = 1f;
            historyScrollLayout.minHeight = 260f;

            var composerCard = CreateLayoutContainer(panel, "ComposerCard", isHorizontal: false, 10f, new RectOffset(14, 14, 14, 14));
            composerCard.gameObject.AddComponent<Image>().color = ComposerColor;

            var composerTitle = CreateText(composerCard, "ComposerTitle", "输入你想对 VividSoul 说的话", 14, FontStyle.Bold, TextSecondaryColor, TextAnchor.MiddleLeft);
            composerTitle.horizontalOverflow = HorizontalWrapMode.Wrap;
            composerTitle.verticalOverflow = VerticalWrapMode.Overflow;

            var composerRow = CreateLayoutContainer(composerCard, "ComposerRow", isHorizontal: true, 10f, new RectOffset(0, 0, 0, 0), fitToContents: true);
            inputField = CreateInputField(composerRow, "输入消息，回车发送", ComposerHeight);
            var inputLayout = inputField.gameObject.GetComponent<LayoutElement>();
            inputLayout.flexibleWidth = 1f;
            sendButton = CreateButton(composerRow, "发送", ActionColor, ComposerHeight, 104f);

            var footerRow = CreateLayoutContainer(composerCard, "FooterRow", isHorizontal: true, 10f, new RectOffset(0, 0, 0, 0), fitToContents: true);
            var footerHint = CreateText(footerRow, "FooterHint", "发送后会直接请求当前激活的 Provider；失败信息会回到这里和状态栏。", 12, FontStyle.Normal, TextMutedColor, TextAnchor.MiddleLeft);
            footerHint.horizontalOverflow = HorizontalWrapMode.Wrap;
            footerHint.verticalOverflow = VerticalWrapMode.Overflow;
            var footerHintLayout = footerHint.gameObject.AddComponent<LayoutElement>();
            footerHintLayout.flexibleWidth = 1f;
            clearButton = CreateButton(footerRow, "清空", ActionMutedColor, 36f, 88f);

            return panel;
        }

        private ScrollRect CreateHistoryScrollView(Transform parent, out RectTransform content)
        {
            var rootObject = new GameObject(
                "HistoryScrollView",
                typeof(RectTransform),
                typeof(Image),
                typeof(ScrollRect));
            var root = rootObject.GetComponent<RectTransform>();
            root.SetParent(parent, false);
            rootObject.GetComponent<Image>().color = new Color(0.07f, 0.10f, 0.15f, 0.92f);

            var viewportObject = new GameObject(
                "Viewport",
                typeof(RectTransform),
                typeof(Image),
                typeof(RectMask2D));
            var viewport = viewportObject.GetComponent<RectTransform>();
            viewport.SetParent(root, false);
            StretchRect(viewport);
            viewportObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

            var contentObject = new GameObject(
                "Content",
                typeof(RectTransform),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            content = contentObject.GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            var layout = contentObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 14, 14);
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = contentObject.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var scrollRect = rootObject.GetComponent<ScrollRect>();
            scrollRect.viewport = viewport;
            scrollRect.content = content;
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 24f;
            return scrollRect;
        }

        private InputField CreateInputField(Transform parent, string placeholder, float preferredHeight)
        {
            var fieldObject = new GameObject(
                "InputField",
                typeof(RectTransform),
                typeof(Image),
                typeof(InputField),
                typeof(LayoutElement));
            var rect = fieldObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);

            var background = fieldObject.GetComponent<Image>();
            background.color = InputColor;

            var layout = fieldObject.GetComponent<LayoutElement>();
            layout.preferredHeight = preferredHeight;

            var textViewportObject = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            var textViewport = textViewportObject.GetComponent<RectTransform>();
            textViewport.SetParent(fieldObject.transform, false);
            StretchRect(textViewport);
            textViewport.offsetMin = new Vector2(14f, 10f);
            textViewport.offsetMax = new Vector2(-14f, -10f);

            var placeholderText = CreateText(textViewport, "Placeholder", placeholder, 15, FontStyle.Normal, TextMutedColor, TextAnchor.MiddleLeft);
            placeholderText.fontStyle = FontStyle.Italic;
            StretchRect(placeholderText.rectTransform);
            placeholderText.horizontalOverflow = HorizontalWrapMode.Overflow;
            placeholderText.verticalOverflow = VerticalWrapMode.Truncate;

            var inputText = CreateText(textViewport, "Text", string.Empty, 15, FontStyle.Normal, TextPrimaryColor, TextAnchor.MiddleLeft);
            StretchRect(inputText.rectTransform);
            inputText.horizontalOverflow = HorizontalWrapMode.Overflow;
            inputText.verticalOverflow = VerticalWrapMode.Truncate;

            var inputField = fieldObject.GetComponent<InputField>();
            inputField.lineType = InputField.LineType.SingleLine;
            inputField.contentType = InputField.ContentType.Standard;
            inputField.textComponent = inputText;
            inputField.placeholder = placeholderText;
            inputField.transition = Selectable.Transition.ColorTint;
            inputField.colors = CreateButtonColors(InputColor);
            inputField.caretWidth = 2;
            inputField.customCaretColor = true;
            inputField.caretColor = TextPrimaryColor;
            inputField.navigation = new Navigation { mode = Navigation.Mode.None };
            return inputField;
        }

        private void SetExpanded(bool expanded, bool focusInput)
        {
            var wasExpanded = isExpanded;
            isExpanded = expanded;
            if (expanded)
            {
                unreadCount = 0;
            }

            if (chatUi == null)
            {
                return;
            }

            chatUi.Root.gameObject.SetActive(expanded);
            chatUi.PanelRoot.gameObject.SetActive(expanded);
            chatUi.Root.SetAsLastSibling();
            if (expanded)
            {
                if (!wasExpanded)
                {
                    onExpanded?.Invoke();
                }

                ScrollHistoryToBottom();
                RefreshProviderSummary();
                RefreshSendButtonState();
                if (focusInput)
                {
                    FocusInputField();
                }
            }
        }

        private void HandleInputEndEdit(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                RefreshSendButtonState();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                TrySubmitInput();
            }
        }

        private void TrySubmitInput()
        {
            if (chatUi == null)
            {
                return;
            }

            var text = chatUi.InputField.text.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                RefreshSendButtonState();
                FocusInputField();
                return;
            }

            chatUi.InputField.text = string.Empty;
            RefreshSendButtonState();
            onUserMessageSubmitted(text);
            FocusInputField();
        }

        private void ClearHistory()
        {
            if (chatUi == null)
            {
                return;
            }

            entries.Clear();
            ClearChildren(chatUi.HistoryContent);
            AppendSystemMessage("聊天记录已清空。");
            statusReporter("聊天记录已清空。");
        }

        private void AppendMessage(MessageRole role, string author, string text)
        {
            if (chatUi == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var entry = new ChatEntry(role, author, text.Trim(), DateTimeOffset.Now);
            entries.Add(entry);
            CreateMessageBubble(chatUi.HistoryContent, entry);
            ScrollHistoryToBottom();
            chatUi.Root.SetAsLastSibling();
            if (!isExpanded && role == MessageRole.Mate)
            {
                unreadCount++;
            }
        }

        private void CreateMessageBubble(RectTransform parent, ChatEntry entry)
        {
            var row = CreateLayoutContainer(parent, $"MessageRow_{entries.Count}", isHorizontal: true, 0f, new RectOffset(0, 0, 0, 0), fitToContents: true);
            var rowLayout = row.gameObject.AddComponent<LayoutElement>();
            rowLayout.flexibleWidth = 1f;
            rowLayout.minHeight = 1f;

            var rowGroup = row.GetComponent<HorizontalLayoutGroup>();
            rowGroup.childAlignment = entry.Role switch
            {
                MessageRole.User => TextAnchor.UpperRight,
                MessageRole.Mate => TextAnchor.UpperLeft,
                _ => TextAnchor.UpperCenter,
            };
            rowGroup.childControlWidth = false;
            rowGroup.childForceExpandWidth = false;

            var bubbleObject = new GameObject(
                "Bubble",
                typeof(RectTransform),
                typeof(Image),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter),
                typeof(LayoutElement));
            var bubble = bubbleObject.GetComponent<RectTransform>();
            bubble.SetParent(row, false);

            var bubbleImage = bubbleObject.GetComponent<Image>();
            bubbleImage.color = entry.Role switch
            {
                MessageRole.User => UserBubbleColor,
                MessageRole.Mate => MateBubbleColor,
                _ => SystemBubbleColor,
            };
            bubbleImage.raycastTarget = false;

            var bubbleOutline = bubbleObject.AddComponent<Outline>();
            bubbleOutline.effectColor = PanelBorderColor;
            bubbleOutline.effectDistance = new Vector2(1f, -1f);

            var bubbleLayout = bubbleObject.GetComponent<VerticalLayoutGroup>();
            bubbleLayout.padding = new RectOffset(12, 12, 10, 10);
            bubbleLayout.spacing = 6f;
            bubbleLayout.childControlWidth = true;
            bubbleLayout.childControlHeight = false;
            bubbleLayout.childForceExpandWidth = true;
            bubbleLayout.childForceExpandHeight = false;

            var bubbleFitter = bubbleObject.GetComponent<ContentSizeFitter>();
            bubbleFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            bubbleFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var bubbleElement = bubbleObject.GetComponent<LayoutElement>();
            bubbleElement.preferredWidth = entry.Role == MessageRole.System ? SystemBubbleWidth : MessageBubbleWidth;

            var authorText = CreateText(bubble, "Author", entry.Author, 12, FontStyle.Bold, TextSecondaryColor, TextAnchor.MiddleLeft);
            authorText.horizontalOverflow = HorizontalWrapMode.Overflow;
            authorText.verticalOverflow = VerticalWrapMode.Truncate;

            var bodyText = CreateText(bubble, "Body", entry.Text, 15, FontStyle.Normal, TextPrimaryColor, TextAnchor.UpperLeft);
            bodyText.supportRichText = entry.Role == MessageRole.Mate;
            bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bodyText.verticalOverflow = VerticalWrapMode.Overflow;

            var metaText = CreateText(
                bubble,
                "Meta",
                entry.Timestamp.ToLocalTime().ToString("HH:mm"),
                11,
                FontStyle.Normal,
                TextMutedColor,
                entry.Role == MessageRole.User ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
            metaText.horizontalOverflow = HorizontalWrapMode.Overflow;
            metaText.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private void ScrollHistoryToBottom()
        {
            if (chatUi == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(chatUi.HistoryContent);
            LayoutRebuilder.ForceRebuildLayoutImmediate(chatUi.HistoryScrollRect.content);
            chatUi.HistoryScrollRect.verticalNormalizedPosition = 0f;
            Canvas.ForceUpdateCanvases();
        }

        private void RefreshProviderSummary()
        {
            if (chatUi == null)
            {
                return;
            }

            var settings = aiSettingsStore.Load();
            var activeProfile = settings.ProviderProfiles.FirstOrDefault(profile =>
                                    string.Equals(profile.Id, settings.ActiveProviderId, StringComparison.OrdinalIgnoreCase))
                                ?? settings.ProviderProfiles.FirstOrDefault();
            var providerText = currentConversationStatus == null
                ? activeProfile == null
                    ? "当前未配置 Provider"
                    : $"当前 Provider：{activeProfile.DisplayName} | {FormatProviderType(activeProfile.ProviderType)} | {(activeProfile.Enabled ? "已启用" : "未启用")}"
                : BuildConversationStatusSummary(currentConversationStatus);
            providerSummaryBaseText = providerText;
            ApplyProviderSummaryText();
            nextSettingsRefreshAt = Time.unscaledTime + SettingsRefreshIntervalSeconds;
        }

        private void ApplyProviderSummaryText()
        {
            if (chatUi == null)
            {
                return;
            }

            var suffix = isRequestInFlight
                ? " | 正在请求回复..."
                : " | 已接通真实聊天调用";
            chatUi.ProviderSummaryText.text = $"{providerSummaryBaseText}{suffix}";
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

        private void RefreshSendButtonState()
        {
            if (chatUi == null)
            {
                return;
            }

            var canSend = !isRequestInFlight && !string.IsNullOrWhiteSpace(chatUi.InputField.text);
            chatUi.SendButton.interactable = canSend;
            var graphic = chatUi.SendButton.targetGraphic as Image;
            if (graphic != null)
            {
                graphic.color = canSend ? ActionColor : ActionMutedColor;
            }

            chatUi.SendButtonLabel.text = isRequestInFlight ? "发送中" : "发送";
        }

        private void FocusInputField()
        {
            if (chatUi == null || EventSystem.current == null)
            {
                return;
            }

            EventSystem.current.SetSelectedGameObject(chatUi.InputField.gameObject);
            chatUi.InputField.ActivateInputField();
            chatUi.InputField.Select();
        }

        private static RectTransform CreateLayoutContainer(
            Transform parent,
            string name,
            bool isHorizontal,
            float spacing,
            RectOffset padding,
            bool fitToContents = false)
        {
            var objectRoot = new GameObject(
                name,
                typeof(RectTransform),
                isHorizontal ? typeof(HorizontalLayoutGroup) : typeof(VerticalLayoutGroup));
            var rectTransform = objectRoot.GetComponent<RectTransform>();
            rectTransform.SetParent(parent, false);
            rectTransform.localScale = Vector3.one;

            if (isHorizontal)
            {
                var layout = objectRoot.GetComponent<HorizontalLayoutGroup>();
                layout.spacing = spacing;
                layout.padding = padding;
                layout.childAlignment = TextAnchor.MiddleLeft;
                layout.childControlWidth = true;
                layout.childForceExpandWidth = false;
                layout.childControlHeight = true;
                layout.childForceExpandHeight = false;
            }
            else
            {
                var layout = objectRoot.GetComponent<VerticalLayoutGroup>();
                layout.spacing = spacing;
                layout.padding = padding;
                layout.childAlignment = TextAnchor.UpperLeft;
                layout.childControlWidth = true;
                layout.childForceExpandWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandHeight = false;
            }

            if (fitToContents)
            {
                var fitter = objectRoot.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            return rectTransform;
        }

        private static Button CreateButton(Transform parent, string label, Color backgroundColor, float preferredHeight, float preferredWidth)
        {
            var buttonObject = new GameObject(
                label,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button),
                typeof(LayoutElement));
            var rectTransform = buttonObject.GetComponent<RectTransform>();
            rectTransform.SetParent(parent, false);

            var background = buttonObject.GetComponent<Image>();
            background.color = backgroundColor;

            var button = buttonObject.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = background;
            button.colors = CreateButtonColors(backgroundColor);
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            var layoutElement = buttonObject.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = preferredHeight;
            layoutElement.preferredWidth = preferredWidth;

            var buttonLabel = CreateText(rectTransform, "Label", label, 15, FontStyle.Bold, TextPrimaryColor, TextAnchor.MiddleCenter);
            StretchRect(buttonLabel.rectTransform);
            buttonLabel.rectTransform.offsetMin = new Vector2(12f, 0f);
            buttonLabel.rectTransform.offsetMax = new Vector2(-12f, 0f);
            buttonLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            buttonLabel.verticalOverflow = VerticalWrapMode.Truncate;
            return button;
        }

        private static ColorBlock CreateButtonColors(Color normalColor)
        {
            var highlighted = new Color(
                Mathf.Clamp01(normalColor.r + 0.08f),
                Mathf.Clamp01(normalColor.g + 0.08f),
                Mathf.Clamp01(normalColor.b + 0.08f),
                normalColor.a);
            var pressed = new Color(
                Mathf.Clamp01(normalColor.r - 0.05f),
                Mathf.Clamp01(normalColor.g - 0.05f),
                Mathf.Clamp01(normalColor.b - 0.05f),
                normalColor.a);
            return new ColorBlock
            {
                normalColor = normalColor,
                highlightedColor = highlighted,
                pressedColor = pressed,
                selectedColor = highlighted,
                disabledColor = ActionMutedColor,
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };
        }

        private static Text CreateText(
            Transform parent,
            string name,
            string text,
            int fontSize,
            FontStyle fontStyle,
            Color color,
            TextAnchor alignment)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rectTransform = textObject.GetComponent<RectTransform>();
            rectTransform.SetParent(parent, false);
            rectTransform.localScale = Vector3.one;

            var label = textObject.GetComponent<Text>();
            label.text = text;
            label.font = GetUiFont();
            label.fontSize = fontSize;
            label.fontStyle = fontStyle;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.supportRichText = false;
            return label;
        }

        private static Font GetUiFont()
        {
            return RuntimeUiFontResolver.GetFont(UiFontSize);
        }

        private static void ClearChildren(RectTransform parent)
        {
            while (parent.childCount > 0)
            {
                var child = parent.GetChild(0);
                child.SetParent(null, false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        private static Text FindRequiredText(Transform parent)
        {
            var text = parent.GetComponentInChildren<Text>(true);
            return text != null
                ? text
                : throw new InvalidOperationException($"No Text component was found under '{parent.name}'.");
        }

        private static void StretchRect(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        private sealed record ChatEntry(
            MessageRole Role,
            string Author,
            string Text,
            DateTimeOffset Timestamp);

        private sealed class ChatOverlayUi
        {
            public ChatOverlayUi(
                RectTransform root,
                RectTransform panelRoot,
                Text providerSummaryText,
                ScrollRect historyScrollRect,
                RectTransform historyContent,
                InputField inputField,
                Button sendButton,
                Text sendButtonLabel,
                Button clearButton,
                Button collapseButton)
            {
                Root = root;
                PanelRoot = panelRoot;
                ProviderSummaryText = providerSummaryText;
                HistoryScrollRect = historyScrollRect;
                HistoryContent = historyContent;
                InputField = inputField;
                SendButton = sendButton;
                SendButtonLabel = sendButtonLabel;
                ClearButton = clearButton;
                CollapseButton = collapseButton;
            }

            public RectTransform Root { get; }

            public RectTransform PanelRoot { get; }

            public Text ProviderSummaryText { get; }

            public ScrollRect HistoryScrollRect { get; }

            public RectTransform HistoryContent { get; }

            public InputField InputField { get; }

            public Button SendButton { get; }

            public Text SendButtonLabel { get; }

            public Button ClearButton { get; }

            public Button CollapseButton { get; }
        }

        private enum MessageRole
        {
            User = 0,
            Mate = 1,
            System = 2,
        }
    }
}
