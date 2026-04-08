#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using VividSoul.Runtime.AI;
using VividSoul.Runtime.Animation;
using VividSoul.Runtime.Avatar;
using VividSoul.Runtime.Content;
using VividSoul.Runtime.Settings;

namespace VividSoul.Runtime.App
{
    public sealed class DesktopPetSettingsWindowPresenter
    {
        private static readonly Color BackdropColor = new(0f, 0f, 0f, 0f);
        private static readonly Color PanelColor = new(0.10f, 0.13f, 0.18f, 0.98f);
        private static readonly Color HeaderColor = new(0.13f, 0.17f, 0.24f, 1f);
        private static readonly Color SidebarColor = new(0.08f, 0.11f, 0.16f, 0.92f);
        private static readonly Color ContentHostColor = new(0.11f, 0.15f, 0.21f, 0.96f);
        private static readonly Color SectionColor = new(0.14f, 0.18f, 0.25f, 0.96f);
        private static readonly Color SectionBorderColor = new(0.27f, 0.34f, 0.45f, 1f);
        private static readonly Color InputColor = new(0.09f, 0.11f, 0.16f, 1f);
        private static readonly Color ButtonColor = new(0.23f, 0.35f, 0.54f, 1f);
        private static readonly Color ButtonDangerColor = new(0.54f, 0.23f, 0.28f, 1f);
        private static readonly Color ButtonMutedColor = new(0.22f, 0.27f, 0.34f, 1f);
        private static readonly Color ButtonActiveColor = new(0.38f, 0.56f, 0.80f, 1f);
        private static readonly Color TextPrimaryColor = new(0.95f, 0.97f, 1f, 1f);
        private static readonly Color TextSecondaryColor = new(0.73f, 0.80f, 0.88f, 1f);
        private static readonly Color TextMutedColor = new(0.56f, 0.63f, 0.72f, 1f);
        private static readonly Color ToggleOnColor = new(0.36f, 0.71f, 0.52f, 1f);
        private static readonly Color ToggleOffColor = new(0.29f, 0.32f, 0.38f, 1f);
        private const float HeaderHeight = 72f;
        private const float SidebarWidth = 136f;
        private const float BodyPadding = 18f;
        private const float ColumnGap = 18f;
        private const float StandardFieldHeight = 44f;
        private const float StandardSectionSpacing = 16f;

        private readonly IAiSecretsStore aiSecretsStore;
        private readonly IAiSettingsStore aiSettingsStore;
        private readonly ILlmUsageStatsStore llmUsageStatsStore;
        private readonly DesktopPetRuntimeController runtimeController;
        private readonly Action<string> statusReporter;

        private AiSettingsData editingSettings = default!;
        private Dictionary<string, string> editingApiKeys = new(StringComparer.OrdinalIgnoreCase);
        private GeneralTabUi? generalTabUi;
        private SettingsWindowUi? windowUi;
        private SettingsTab activeTab = SettingsTab.Llm;
        private string pendingDeleteModelPath = string.Empty;
        private string pendingDeleteAnimationPath = string.Empty;
        private string selectedProviderId = string.Empty;
        private string configurationStatusMessage = "当前配置尚未保存。";
        private float nextStatsRefreshAt;

        public DesktopPetSettingsWindowPresenter(DesktopPetRuntimeController runtimeController, Action<string> statusReporter)
        {
            this.runtimeController = runtimeController ?? throw new ArgumentNullException(nameof(runtimeController));
            this.statusReporter = statusReporter ?? throw new ArgumentNullException(nameof(statusReporter));
            aiSettingsStore = new AiSettingsStore();
            aiSecretsStore = new AiSecretsStore();
            llmUsageStatsStore = new LlmUsageStatsStore();
            this.runtimeController.ModelLoaded += HandleRuntimeModelChanged;
            this.runtimeController.ModelCleared += HandleRuntimeModelCleared;
            this.runtimeController.ManagedLocalAnimationsChanged += HandleManagedLocalAnimationsChanged;
        }

        public bool IsVisible => windowUi != null && windowUi.Root.gameObject.activeSelf;

        public void Dispose()
        {
            runtimeController.ModelLoaded -= HandleRuntimeModelChanged;
            runtimeController.ModelCleared -= HandleRuntimeModelCleared;
            runtimeController.ManagedLocalAnimationsChanged -= HandleManagedLocalAnimationsChanged;
        }

        public void Show(Canvas canvas)
        {
            if (canvas == null)
            {
                throw new ArgumentNullException(nameof(canvas));
            }

            EnsureWindowUi(canvas);
            LoadEditingState();
            RefreshManagedModelLibraryList();
            RefreshManagedAnimationLibraryList();
            ResizeWindow();
            ShowTab(activeTab);
            windowUi!.Root.gameObject.SetActive(true);
            windowUi.Root.SetAsLastSibling();
            RefreshStats();
            nextStatsRefreshAt = Time.unscaledTime + 1f;
            RebuildLayout();
            LogLayoutDiagnostics("show");
        }

        public void Hide()
        {
            if (windowUi == null)
            {
                return;
            }

            windowUi.Root.gameObject.SetActive(false);
        }

        public void Update(float deltaTime)
        {
            if (!IsVisible)
            {
                return;
            }

            ResizeWindow();
            if (Time.unscaledTime >= nextStatsRefreshAt)
            {
                RefreshStats();
                nextStatsRefreshAt = Time.unscaledTime + 1f;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Hide();
            }
        }

        private void HandleRuntimeModelChanged(ModelLoadResult _)
        {
            if (!IsVisible || activeTab != SettingsTab.General)
            {
                return;
            }

            pendingDeleteModelPath = string.Empty;
            RefreshManagedModelLibraryList();
        }

        private void HandleRuntimeModelCleared()
        {
            if (!IsVisible || activeTab != SettingsTab.General)
            {
                return;
            }

            pendingDeleteModelPath = string.Empty;
            RefreshManagedModelLibraryList();
        }

        private void HandleManagedLocalAnimationsChanged()
        {
            pendingDeleteAnimationPath = string.Empty;
            if (!IsVisible || activeTab != SettingsTab.General)
            {
                return;
            }

            RefreshManagedAnimationLibraryList();
        }

        private void EnsureWindowUi(Canvas canvas)
        {
            if (windowUi != null)
            {
                return;
            }

            var rootObject = new GameObject(
                "VividSoulSettingsWindow",
                typeof(RectTransform));
            var root = rootObject.GetComponent<RectTransform>();
            root.SetParent(canvas.transform, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var backdropObject = new GameObject(
                "Backdrop",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            var backdropRect = backdropObject.GetComponent<RectTransform>();
            backdropRect.SetParent(root, false);
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = Vector2.zero;
            backdropRect.offsetMax = Vector2.zero;

            var backdrop = backdropObject.GetComponent<Image>();
            backdrop.color = BackdropColor;
            backdrop.raycastTarget = true;

            var backdropButton = backdropObject.GetComponent<Button>();
            backdropButton.transition = Selectable.Transition.None;
            backdropButton.onClick.AddListener(Hide);

            var panelObject = new GameObject(
                "Panel",
                typeof(RectTransform),
                typeof(Image),
                typeof(Outline));
            var panel = panelObject.GetComponent<RectTransform>();
            panel.SetParent(root, false);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);

            var panelImage = panelObject.GetComponent<Image>();
            panelImage.color = PanelColor;
            panelImage.raycastTarget = true;

            var panelOutline = panelObject.GetComponent<Outline>();
            panelOutline.effectColor = SectionBorderColor;
            panelOutline.effectDistance = new Vector2(1f, -1f);

            var layoutRoot = new GameObject("LayoutRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            layoutRoot.SetParent(panel, false);
            layoutRoot.anchorMin = Vector2.zero;
            layoutRoot.anchorMax = Vector2.one;
            layoutRoot.offsetMin = Vector2.zero;
            layoutRoot.offsetMax = Vector2.zero;

            var header = CreateHeader(layoutRoot);
            var body = new GameObject("Body", typeof(RectTransform)).GetComponent<RectTransform>();
            body.SetParent(layoutRoot, false);

            var sidebar = CreateSidebar(body);
            var contentHost = CreateContentHost(body);

            var generalContent = CreateGeneralTab(contentHost);
            var llmContent = CreateLlmTab(contentHost);

            rootObject.SetActive(false);

            windowUi = new SettingsWindowUi(
                root,
                panel,
                layoutRoot,
                header.Root,
                body,
                sidebar.Root,
                contentHost,
                header.CloseButton,
                sidebar.GeneralTabButton,
                sidebar.GeneralTabButtonText,
                sidebar.LlmTabButton,
                sidebar.LlmTabButtonText,
                generalContent.Root,
                llmContent.Root,
                llmContent.ScrollRoot,
                llmContent.ScrollContent,
                llmContent.ProviderSection,
                llmContent.GlobalSection,
                llmContent.StatsSection,
                llmContent.ConfigurationStatusText,
                llmContent.ProviderListRoot,
                llmContent.ProviderDisplayNameInput,
                llmContent.ProviderTypeValueText,
                llmContent.ProviderEnabledToggle,
                llmContent.ProviderBaseUrlInput,
                llmContent.ProviderModelInput,
                llmContent.ProviderApiKeyInput,
                llmContent.SystemPromptInput,
                llmContent.TemperatureInput,
                llmContent.MaxOutputTokensInput,
                llmContent.EnableStreamingToggle,
                llmContent.EnableProactiveToggle,
                llmContent.ProactiveMinIntervalInput,
                llmContent.ProactiveMaxIntervalInput,
                llmContent.MemoryWindowInput,
                llmContent.SummaryThresholdInput,
                llmContent.EnableTtsToggle,
                llmContent.StatsText);
            generalTabUi = generalContent;

            header.CloseButton.onClick.AddListener(Hide);
            sidebar.GeneralTabButton.onClick.AddListener(() => ShowTab(SettingsTab.General));
            sidebar.LlmTabButton.onClick.AddListener(() => ShowTab(SettingsTab.Llm));
            generalContent.ImportButton.onClick.AddListener(runtimeController.OpenLocalModelDialog);
            generalContent.RefreshButton.onClick.AddListener(RefreshManagedModelLibraryList);
            generalContent.ImportActionButton.onClick.AddListener(runtimeController.OpenLocalAnimationFileDialog);
            generalContent.RefreshActionButton.onClick.AddListener(RefreshManagedAnimationLibraryList);
            llmContent.AddProviderButton.onClick.AddListener(AddProviderProfile);
            llmContent.RemoveProviderButton.onClick.AddListener(RemoveSelectedProviderProfile);
            llmContent.CycleProviderTypeButton.onClick.AddListener(CycleSelectedProviderType);
            llmContent.SaveButton.onClick.AddListener(SaveChanges);
            llmContent.ReloadButton.onClick.AddListener(LoadEditingState);
            llmContent.RefreshStatsButton.onClick.AddListener(RefreshStats);
            llmContent.ResetStatsButton.onClick.AddListener(ResetStats);
        }

        private void ResizeWindow()
        {
            if (windowUi == null)
            {
                return;
            }

            var width = Mathf.Clamp(Screen.width * 0.70f, 940f, 1120f);
            var height = Mathf.Clamp(Screen.height * 0.76f, 600f, 780f);
            windowUi.Panel.sizeDelta = new Vector2(width, height);
            ApplyChromeLayout();
        }

        private void LoadEditingState()
        {
            editingSettings = aiSettingsStore.Load();
            editingApiKeys = editingSettings.ProviderProfiles.ToDictionary(
                profile => profile.Id,
                profile => aiSecretsStore.LoadApiKey(profile.Id),
                StringComparer.OrdinalIgnoreCase);

            selectedProviderId = editingSettings.ProviderProfiles.Any(profile =>
                    string.Equals(profile.Id, editingSettings.ActiveProviderId, StringComparison.OrdinalIgnoreCase))
                ? editingSettings.ActiveProviderId
                : editingSettings.ProviderProfiles[0].Id;
            editingSettings = editingSettings with { ActiveProviderId = selectedProviderId };
            configurationStatusMessage = "当前配置已从本地加载，尚未做连通性验证。";

            ApplyEditingStateToUi();
            RefreshStats();
        }

        private void ApplyEditingStateToUi()
        {
            if (windowUi == null)
            {
                return;
            }

            RebuildProviderButtons();
            ApplySelectedProviderToUi();
            ApplyGlobalSettingsToUi();
            RebuildLayout();
        }

        private void RebuildProviderButtons()
        {
            if (windowUi == null)
            {
                return;
            }

            ClearChildren(windowUi.ProviderListRoot);
            foreach (var profile in editingSettings.ProviderProfiles)
            {
                var providerId = profile.Id;
                var isActive = string.Equals(providerId, selectedProviderId, StringComparison.OrdinalIgnoreCase);
                var label = profile.DisplayName;
                var button = CreateButton(windowUi.ProviderListRoot, label, isActive ? ButtonActiveColor : ButtonMutedColor, 40f);
                var buttonText = FindRequiredText(button.transform);
                buttonText.alignment = TextAnchor.MiddleLeft;
                button.onClick.AddListener(() => SelectProvider(providerId));
            }
        }

        private void SelectProvider(string providerId)
        {
            if (!TryCaptureUiState(out _))
            {
                return;
            }

            selectedProviderId = providerId;
            editingSettings = editingSettings with { ActiveProviderId = providerId };
            ApplyEditingStateToUi();
        }

        private void AddProviderProfile()
        {
            if (!TryCaptureUiState(out _))
            {
                return;
            }

            var providerId = $"provider-{Guid.NewGuid():N}";
            var newProfile = new LlmProviderProfile(
                Id: providerId,
                DisplayName: $"Provider {editingSettings.ProviderProfiles.Count + 1}",
                ProviderType: LlmProviderType.OpenAiCompatible,
                BaseUrl: "https://api.openai.com/v1",
                Model: "gpt-4.1-mini",
                Enabled: true);

            editingSettings = editingSettings with
            {
                ActiveProviderId = providerId,
                ProviderProfiles = editingSettings.ProviderProfiles.Append(newProfile).ToArray(),
            };
            editingApiKeys[providerId] = string.Empty;
            selectedProviderId = providerId;
            ApplyEditingStateToUi();
        }

        private void RemoveSelectedProviderProfile()
        {
            if (editingSettings.ProviderProfiles.Count <= 1)
            {
                statusReporter("至少保留一个 Provider 配置。");
                return;
            }

            if (!TryCaptureUiState(out _))
            {
                return;
            }

            var profiles = editingSettings.ProviderProfiles
                .Where(profile => !string.Equals(profile.Id, selectedProviderId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            editingApiKeys.Remove(selectedProviderId);
            selectedProviderId = profiles[0].Id;
            editingSettings = editingSettings with
            {
                ActiveProviderId = selectedProviderId,
                ProviderProfiles = profiles,
            };
            ApplyEditingStateToUi();
        }

        private void CycleSelectedProviderType()
        {
            if (!TryCaptureUiState(out _))
            {
                return;
            }

            var selectedProfile = GetSelectedProvider();
            if (selectedProfile == null)
            {
                return;
            }

            var nextType = (LlmProviderType)(((int)selectedProfile.ProviderType + 1) % Enum.GetValues(typeof(LlmProviderType)).Length);
            UpdateSelectedProvider(nextType == LlmProviderType.OpenClaw
                ? selectedProfile with
                {
                    ProviderType = nextType,
                    OpenClawGatewayWsUrl = string.IsNullOrWhiteSpace(selectedProfile.OpenClawGatewayWsUrl)
                        ? "ws://127.0.0.1:18789"
                        : selectedProfile.OpenClawGatewayWsUrl,
                    OpenClawAgentId = string.IsNullOrWhiteSpace(selectedProfile.OpenClawAgentId)
                        ? "main"
                        : selectedProfile.OpenClawAgentId,
                }
                : selectedProfile with { ProviderType = nextType });
            ApplySelectedProviderToUi();
        }

        private void SaveChanges()
        {
            if (!TryCaptureUiState(out var errorMessage))
            {
                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    statusReporter(errorMessage);
                }

                return;
            }

            aiSettingsStore.Save(editingSettings);
            foreach (var profile in editingSettings.ProviderProfiles)
            {
                editingApiKeys.TryGetValue(profile.Id, out var apiKey);
                aiSecretsStore.SaveApiKey(profile.Id, apiKey ?? string.Empty);
            }

            configurationStatusMessage = "配置已保存到本地，尚未做连通性验证。";
            statusReporter("LLM 设置已保存。");
            LoadEditingState();
        }

        private void ResetStats()
        {
            llmUsageStatsStore.Reset();
            RefreshStats();
            statusReporter("LLM 调用统计已重置。");
        }

        private void RefreshStats()
        {
            if (windowUi == null)
            {
                return;
            }

            var stats = llmUsageStatsStore.Load();
            var averageLatency = stats.TotalRequestCount > 0
                ? (double)stats.TotalLatencyMs / stats.TotalRequestCount
                : 0d;
            var successRate = stats.TotalRequestCount > 0
                ? (double)stats.SuccessfulRequestCount / stats.TotalRequestCount * 100d
                : 0d;
            var lastRequest = TryFormatTimestamp(stats.LastRequestAtUtc);
            var lines = new[]
            {
                $"总请求: {stats.TotalRequestCount}",
                $"成功 / 失败: {stats.SuccessfulRequestCount} / {stats.FailedRequestCount}",
                $"成功率: {successRate:0.0}%",
                $"平均耗时: {averageLatency:0.0} ms",
                $"输入字符: {stats.TotalPromptCharacters}",
                $"输出字符: {stats.TotalCompletionCharacters}",
                $"最近 Provider: {Fallback(stats.LastProviderId, "未记录")}",
                $"最近 Model: {Fallback(stats.LastModel, "未记录")}",
                $"最近时间: {Fallback(lastRequest, "未记录")}",
                $"最近错误: {Fallback(stats.LastErrorMessage, "无")}",
            };
            windowUi.StatsText.text = string.Join("\n", lines);
        }

        private bool TryCaptureUiState(out string errorMessage)
        {
            errorMessage = string.Empty;
            if (windowUi == null)
            {
                return false;
            }

            var selectedProfile = GetSelectedProvider();
            if (selectedProfile == null)
            {
                errorMessage = "当前没有可编辑的 Provider。";
                return false;
            }

            var updatedProfile = selectedProfile.ProviderType == LlmProviderType.OpenClaw
                ? selectedProfile with
                {
                    DisplayName = windowUi.ProviderDisplayNameInput.text.Trim(),
                    BaseUrl = string.Empty,
                    Model = string.Empty,
                    Enabled = windowUi.ProviderEnabledToggle.isOn,
                    OpenClawGatewayWsUrl = windowUi.ProviderBaseUrlInput.text.Trim(),
                    OpenClawAgentId = windowUi.ProviderModelInput.text.Trim(),
                }
                : selectedProfile with
                {
                    DisplayName = windowUi.ProviderDisplayNameInput.text.Trim(),
                    BaseUrl = windowUi.ProviderBaseUrlInput.text.Trim(),
                    Model = windowUi.ProviderModelInput.text.Trim(),
                    Enabled = windowUi.ProviderEnabledToggle.isOn,
                };
            if (string.IsNullOrWhiteSpace(updatedProfile.DisplayName))
            {
                errorMessage = "Provider 名称不能为空。";
                return false;
            }

            UpdateSelectedProvider(updatedProfile);
            editingApiKeys[selectedProviderId] = windowUi.ProviderApiKeyInput.text.Trim();

            if (!TryParseFloat(windowUi.TemperatureInput.text, "Temperature", out var temperature, out errorMessage)
                || !TryParseInt(windowUi.MaxOutputTokensInput.text, "Max Output Tokens", out var maxOutputTokens, out errorMessage)
                || !TryParseFloat(windowUi.ProactiveMinIntervalInput.text, "主动消息最短间隔", out var proactiveMinInterval, out errorMessage)
                || !TryParseFloat(windowUi.ProactiveMaxIntervalInput.text, "主动消息最长间隔", out var proactiveMaxInterval, out errorMessage)
                || !TryParseInt(windowUi.MemoryWindowInput.text, "记忆窗口轮数", out var memoryWindowTurns, out errorMessage)
                || !TryParseInt(windowUi.SummaryThresholdInput.text, "摘要阈值", out var summaryThreshold, out errorMessage))
            {
                return false;
            }

            if (proactiveMaxInterval < proactiveMinInterval)
            {
                errorMessage = "主动消息最长间隔不能小于最短间隔。";
                return false;
            }

            editingSettings = editingSettings with
            {
                ActiveProviderId = selectedProviderId,
                GlobalSystemPrompt = windowUi.SystemPromptInput.text.Trim(),
                Temperature = temperature,
                MaxOutputTokens = maxOutputTokens,
                EnableStreaming = windowUi.EnableStreamingToggle.isOn,
                EnableProactiveMessages = windowUi.EnableProactiveToggle.isOn,
                ProactiveMinIntervalMinutes = proactiveMinInterval,
                ProactiveMaxIntervalMinutes = proactiveMaxInterval,
                MemoryWindowTurns = memoryWindowTurns,
                SummaryThreshold = summaryThreshold,
                EnableTts = windowUi.EnableTtsToggle.isOn,
            };
            return true;
        }

        private void ApplySelectedProviderToUi()
        {
            if (windowUi == null)
            {
                return;
            }

            var selectedProfile = GetSelectedProvider();
            if (selectedProfile == null)
            {
                return;
            }

            windowUi.ProviderDisplayNameInput.text = selectedProfile.DisplayName;
            windowUi.ProviderTypeValueText.text = GetProviderTypeLabel(selectedProfile.ProviderType);
            windowUi.ProviderEnabledToggle.isOn = selectedProfile.Enabled;
            windowUi.ProviderBaseUrlInput.text = selectedProfile.ProviderType == LlmProviderType.OpenClaw
                ? selectedProfile.OpenClawGatewayWsUrl
                : selectedProfile.BaseUrl;
            windowUi.ProviderModelInput.text = selectedProfile.ProviderType == LlmProviderType.OpenClaw
                ? selectedProfile.OpenClawAgentId
                : selectedProfile.Model;
            windowUi.ProviderApiKeyInput.text = editingApiKeys.TryGetValue(selectedProfile.Id, out var apiKey) ? apiKey : string.Empty;
            windowUi.ConfigurationStatusText.text = BuildConfigurationStatusText(selectedProfile);
            RebuildProviderButtons();
        }

        private void ApplyGlobalSettingsToUi()
        {
            if (windowUi == null)
            {
                return;
            }

            windowUi.SystemPromptInput.text = editingSettings.GlobalSystemPrompt;
            windowUi.TemperatureInput.text = editingSettings.Temperature.ToString("0.##", CultureInfo.InvariantCulture);
            windowUi.MaxOutputTokensInput.text = editingSettings.MaxOutputTokens.ToString(CultureInfo.InvariantCulture);
            windowUi.EnableStreamingToggle.isOn = editingSettings.EnableStreaming;
            windowUi.EnableProactiveToggle.isOn = editingSettings.EnableProactiveMessages;
            windowUi.ProactiveMinIntervalInput.text = editingSettings.ProactiveMinIntervalMinutes.ToString("0.##", CultureInfo.InvariantCulture);
            windowUi.ProactiveMaxIntervalInput.text = editingSettings.ProactiveMaxIntervalMinutes.ToString("0.##", CultureInfo.InvariantCulture);
            windowUi.MemoryWindowInput.text = editingSettings.MemoryWindowTurns.ToString(CultureInfo.InvariantCulture);
            windowUi.SummaryThresholdInput.text = editingSettings.SummaryThreshold.ToString(CultureInfo.InvariantCulture);
            windowUi.EnableTtsToggle.isOn = editingSettings.EnableTts;
        }

        private void UpdateSelectedProvider(LlmProviderProfile updatedProfile)
        {
            editingSettings = editingSettings with
            {
                ProviderProfiles = editingSettings.ProviderProfiles
                    .Select(profile => string.Equals(profile.Id, updatedProfile.Id, StringComparison.OrdinalIgnoreCase)
                        ? updatedProfile
                        : profile)
                    .ToArray(),
            };
        }

        private LlmProviderProfile? GetSelectedProvider()
        {
            return editingSettings.ProviderProfiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, selectedProviderId, StringComparison.OrdinalIgnoreCase));
        }

        private void ShowTab(SettingsTab tab)
        {
            if (windowUi == null)
            {
                return;
            }

            activeTab = tab;
            var isGeneral = tab == SettingsTab.General;
            windowUi.GeneralContent.SetActive(isGeneral);
            windowUi.LlmContent.SetActive(!isGeneral);
            SetTabVisual(windowUi.GeneralTabButton, windowUi.GeneralTabButtonText, isGeneral);
            SetTabVisual(windowUi.LlmTabButton, windowUi.LlmTabButtonText, !isGeneral);
            if (isGeneral)
            {
                RefreshManagedModelLibraryList();
                RefreshManagedAnimationLibraryList();
            }

            RebuildLayout();
            LogLayoutDiagnostics($"tab:{tab}");
        }

        private static void SetTabVisual(Button button, Text label, bool isActive)
        {
            var background = button.GetComponent<Image>();
            if (background != null)
            {
                background.color = isActive ? ButtonActiveColor : ButtonMutedColor;
            }

            label.color = isActive ? TextPrimaryColor : TextSecondaryColor;
        }

        private HeaderUi CreateHeader(Transform parent)
        {
            var root = CreateLayoutContainer(parent, "Header", isHorizontal: true, 12f, new RectOffset(24, 24, 18, 18));
            var image = root.gameObject.AddComponent<Image>();
            image.color = HeaderColor;

            var title = CreateText(root, "Title", "VividSoul 设置", 26, FontStyle.Bold, TextPrimaryColor, TextAnchor.MiddleLeft);
            var titleLayout = title.gameObject.AddComponent<LayoutElement>();
            titleLayout.flexibleWidth = 1f;

            var closeButton = CreateButton(root, "关闭", ButtonMutedColor, 44f, 104f);
            return new HeaderUi(root, closeButton);
        }

        private SidebarUi CreateSidebar(Transform parent)
        {
            var root = CreateLayoutContainer(parent, "Sidebar", isHorizontal: false, 10f, new RectOffset(18, 18, 18, 18));
            var image = root.gameObject.AddComponent<Image>();
            image.color = SidebarColor;
            CreateText(root, "SidebarTitle", "分类", 18, FontStyle.Bold, TextSecondaryColor, TextAnchor.MiddleLeft);
            var generalButton = CreateButton(root, "常规", ButtonMutedColor, 44f);
            var llmButton = CreateButton(root, "LLM", ButtonActiveColor, 44f);
            var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            spacer.transform.SetParent(root, false);
            var spacerLayout = spacer.GetComponent<LayoutElement>();
            spacerLayout.flexibleHeight = 1f;

            return new SidebarUi(
                root,
                generalButton,
                FindRequiredText(generalButton.transform),
                llmButton,
                FindRequiredText(llmButton.transform));
        }

        private RectTransform CreateContentHost(Transform parent)
        {
            var hostObject = new GameObject(
                "ContentHost",
                typeof(RectTransform),
                typeof(Image),
                typeof(LayoutElement));
            var root = hostObject.GetComponent<RectTransform>();
            root.SetParent(parent, false);

            var image = hostObject.GetComponent<Image>();
            image.color = ContentHostColor;
            image.raycastTarget = true;

            var layoutElement = hostObject.GetComponent<LayoutElement>();
            layoutElement.flexibleWidth = 1f;
            layoutElement.flexibleHeight = 1f;
            return root;
        }

        private GeneralTabUi CreateGeneralTab(Transform parent)
        {
            var root = new GameObject("GeneralTab", typeof(RectTransform));
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(parent, false);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = new Vector2(18f, 18f);
            rootRect.offsetMax = new Vector2(-18f, -18f);

            var scrollRoot = CreateScrollView(root.transform, out var content);

            var card = CreateSectionCard(content, "GeneralCard", "常规");
            SetPreferredHeight(card, 152f);
            CreateBodyText(card, "这里集中放角色库与动作管理。角色仍然是当前主要内容对象，动作则以受控动作库的形式提供应用、导入和删除。");

            var librarySection = CreateSectionCard(content, "ModelLibrarySection", "角色库管理");
            CreateHintText(librarySection, "仅显示已导入到角色库的本地角色。删除会移除对应角色文件；当前角色删除后会先卸载。");
            var toolbar = CreateLayoutContainer(librarySection, "Toolbar", isHorizontal: true, 8f, new RectOffset(0, 0, 0, 0), fitToContents: true);
            var importButton = CreateButton(toolbar, "导入角色", ButtonColor, 40f, 112f);
            var toolbarSpacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            toolbarSpacer.transform.SetParent(toolbar, false);
            toolbarSpacer.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var refreshButton = CreateButton(toolbar, "刷新列表", ButtonMutedColor, 40f, 104f);
            var libraryHintText = CreateHintText(librarySection, string.Empty);
            var libraryListRoot = CreateLayoutContainer(librarySection, "LibraryList", isHorizontal: false, 10f, new RectOffset(0, 0, 0, 0));
            libraryListRoot.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var libraryListLayout = libraryListRoot.gameObject.AddComponent<LayoutElement>();
            libraryListLayout.minHeight = 40f;

            var actionSection = CreateSectionCard(content, "ActionLibrarySection", "动作管理");
            CreateHintText(actionSection, "内置动作可直接应用；导入到动作库的本地 VRMA 会长期保留，并支持再次应用或删除。");

            var builtInTitle = CreateSingleLineText(actionSection, "BuiltInActionsTitle", "内置动作", 15, FontStyle.Bold, TextSecondaryColor, TextAnchor.MiddleLeft);
            var builtInTitleLayout = builtInTitle.gameObject.AddComponent<LayoutElement>();
            builtInTitleLayout.preferredHeight = 26f;
            var builtInListRoot = CreateLayoutContainer(actionSection, "BuiltInActionList", isHorizontal: false, 10f, new RectOffset(0, 0, 0, 0));
            builtInListRoot.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var builtInListLayout = builtInListRoot.gameObject.AddComponent<LayoutElement>();
            builtInListLayout.minHeight = 40f;

            var localTitle = CreateSingleLineText(actionSection, "LocalActionsTitle", "本地动作库", 15, FontStyle.Bold, TextSecondaryColor, TextAnchor.MiddleLeft);
            var localTitleLayout = localTitle.gameObject.AddComponent<LayoutElement>();
            localTitleLayout.preferredHeight = 26f;
            var actionToolbar = CreateLayoutContainer(actionSection, "ActionToolbar", isHorizontal: true, 8f, new RectOffset(0, 0, 0, 0), fitToContents: true);
            var importActionButton = CreateButton(actionToolbar, "导入 VRMA", ButtonColor, 40f, 112f);
            var actionToolbarSpacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            actionToolbarSpacer.transform.SetParent(actionToolbar, false);
            actionToolbarSpacer.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var refreshActionButton = CreateButton(actionToolbar, "刷新动作库", ButtonMutedColor, 40f, 112f);
            var actionHintText = CreateHintText(actionSection, string.Empty);
            var actionListRoot = CreateLayoutContainer(actionSection, "ActionList", isHorizontal: false, 10f, new RectOffset(0, 0, 0, 0));
            actionListRoot.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var actionListLayout = actionListRoot.gameObject.AddComponent<LayoutElement>();
            actionListLayout.minHeight = 40f;

            return new GeneralTabUi(
                root,
                scrollRoot,
                content,
                librarySection,
                libraryHintText,
                libraryListRoot,
                importButton,
                refreshButton,
                actionSection,
                builtInListRoot,
                actionHintText,
                actionListRoot,
                importActionButton,
                refreshActionButton);
        }

        private void RefreshManagedModelLibraryList()
        {
            if (generalTabUi == null)
            {
                return;
            }

            ClearChildren(generalTabUi.LibraryListRoot);
            var managedModels = runtimeController.ManagedLocalModels
                .Where(model => runtimeController.CanDeleteManagedLocalModel(model.EntryPath))
                .ToArray();
            if (!managedModels.Any())
            {
                pendingDeleteModelPath = string.Empty;
                generalTabUi.LibraryHintText.text = "当前还没有可管理的导入角色。通过右键菜单里的“添加角色”导入后，这里会自动出现。";
                RebuildLayout();
                return;
            }

            if (!managedModels.Any(model => PathsEqual(model.EntryPath, pendingDeleteModelPath)))
            {
                pendingDeleteModelPath = string.Empty;
            }

            generalTabUi.LibraryHintText.text = "点击“删除”后需要再点一次确认，避免误删。";
            foreach (var managedModel in managedModels)
            {
                CreateManagedModelRow(managedModel);
            }

            RebuildLayout();
        }

        private void CreateManagedModelRow(ContentItem managedModel)
        {
            if (generalTabUi == null)
            {
                return;
            }

            var row = CreateLayoutContainer(
                generalTabUi.LibraryListRoot,
                $"ModelRow-{managedModel.Title}",
                isHorizontal: true,
                spacing: 12f,
                padding: new RectOffset(14, 14, 12, 12),
                fitToContents: true);
            var rowImage = row.gameObject.AddComponent<Image>();
            rowImage.color = InputColor;
            var rowOutline = row.gameObject.AddComponent<Outline>();
            rowOutline.effectColor = SectionBorderColor;
            rowOutline.effectDistance = new Vector2(1f, -1f);

            var detailsColumn = CreateLayoutContainer(row, "Details", isHorizontal: false, 4f, new RectOffset(0, 0, 0, 0), fitToContents: true);
            var detailsLayout = detailsColumn.gameObject.AddComponent<LayoutElement>();
            detailsLayout.flexibleWidth = 1f;

            var isCurrentModel = runtimeController.IsCurrentModelPath(managedModel.EntryPath);
            var modelExists = File.Exists(managedModel.EntryPath);
            var title = isCurrentModel
                ? $"{GetContentDisplayName(managedModel)}  (当前)"
                : GetContentDisplayName(managedModel);
            var titleText = CreateText(detailsColumn, "Title", title, 16, FontStyle.Bold, TextPrimaryColor, TextAnchor.UpperLeft);
            titleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            titleText.verticalOverflow = VerticalWrapMode.Overflow;

            var directoryPath = runtimeController.GetManagedLocalModelDisplayDirectory(managedModel.EntryPath);
            var directoryLabel = modelExists
                ? $"模型存放目录：{directoryPath}"
                : $"模型存放目录：{directoryPath}\n当前文件已缺失，但仍可删除这条记录。";
            CreateBodyText(detailsColumn, directoryLabel, 13, TextSecondaryColor);

            var actionColumn = CreateLayoutContainer(row, "Actions", isHorizontal: false, 8f, new RectOffset(0, 0, 0, 0), fitToContents: true);
            var actionColumnLayout = actionColumn.gameObject.AddComponent<LayoutElement>();
            actionColumnLayout.preferredWidth = 104f;
            actionColumnLayout.minWidth = 104f;
            if (actionColumn.GetComponent<VerticalLayoutGroup>() is VerticalLayoutGroup actionLayoutGroup)
            {
                actionLayoutGroup.childAlignment = TextAnchor.UpperRight;
                actionLayoutGroup.childControlWidth = false;
                actionLayoutGroup.childForceExpandWidth = false;
            }

            var applyButton = CreateButton(
                actionColumn,
                isCurrentModel ? "当前使用" : "应用",
                isCurrentModel ? ButtonActiveColor : ButtonColor,
                36f,
                96f);
            applyButton.interactable = !isCurrentModel && modelExists;
            applyButton.onClick.AddListener(() => ApplyManagedModel(managedModel));

            var isPendingDelete = PathsEqual(managedModel.EntryPath, pendingDeleteModelPath);
            var deleteButton = CreateButton(actionColumn, isPendingDelete ? "确认删除" : "删除", ButtonDangerColor, 36f, 96f);
            deleteButton.onClick.AddListener(() => DeleteManagedModel(managedModel));
        }

        private void ApplyManagedModel(ContentItem managedModel)
        {
            try
            {
                runtimeController.LoadManagedLocalModel(managedModel.EntryPath);
                pendingDeleteModelPath = string.Empty;
                statusReporter($"已切换角色：{GetContentDisplayName(managedModel)}");
                RefreshManagedModelLibraryList();
            }
            catch (UserFacingException exception)
            {
                statusReporter(exception.Message);
            }
            catch (Exception exception)
            {
                statusReporter($"切换角色失败：{exception.Message}");
            }
        }

        private void DeleteManagedModel(ContentItem managedModel)
        {
            if (!PathsEqual(managedModel.EntryPath, pendingDeleteModelPath))
            {
                pendingDeleteModelPath = managedModel.EntryPath;
                statusReporter($"再次点击“确认删除”以移除角色：{GetContentDisplayName(managedModel)}");
                RefreshManagedModelLibraryList();
                return;
            }

            try
            {
                runtimeController.DeleteManagedLocalModel(managedModel.EntryPath);
                pendingDeleteModelPath = string.Empty;
                statusReporter($"已删除角色：{GetContentDisplayName(managedModel)}");
                RefreshManagedModelLibraryList();
            }
            catch (UserFacingException exception)
            {
                statusReporter(exception.Message);
            }
            catch (Exception exception)
            {
                statusReporter($"删除角色失败：{exception.Message}");
            }
        }

        private void RefreshManagedAnimationLibraryList()
        {
            if (generalTabUi == null)
            {
                return;
            }

            ClearChildren(generalTabUi.BuiltInActionListRoot);
            foreach (var builtInPose in runtimeController.BuiltInPoses)
            {
                CreateBuiltInActionRow(builtInPose);
            }

            ClearChildren(generalTabUi.ActionListRoot);
            var managedAnimations = runtimeController.ManagedLocalAnimations
                .Where(animation => runtimeController.CanDeleteManagedLocalAnimation(animation.EntryPath))
                .ToArray();
            if (!managedAnimations.Any())
            {
                pendingDeleteAnimationPath = string.Empty;
                generalTabUi.ActionHintText.text = "当前还没有导入到动作库的本地 VRMA。点击“导入 VRMA”后，这里会自动出现。";
                RebuildLayout();
                return;
            }

            if (!managedAnimations.Any(animation => PathsEqual(animation.EntryPath, pendingDeleteAnimationPath)))
            {
                pendingDeleteAnimationPath = string.Empty;
            }

            generalTabUi.ActionHintText.text = "本地动作删除前需要再次确认；内置动作仅支持应用，不支持删除。";
            foreach (var managedAnimation in managedAnimations)
            {
                CreateManagedAnimationRow(managedAnimation);
            }

            RebuildLayout();
        }

        private void CreateBuiltInActionRow(BuiltInPoseOption builtInPose)
        {
            if (generalTabUi == null)
            {
                return;
            }

            var row = CreateLayoutContainer(
                generalTabUi.BuiltInActionListRoot,
                $"BuiltInActionRow-{builtInPose.Id}",
                isHorizontal: true,
                spacing: 12f,
                padding: new RectOffset(14, 14, 12, 12),
                fitToContents: true);
            var rowImage = row.gameObject.AddComponent<Image>();
            rowImage.color = InputColor;
            var rowOutline = row.gameObject.AddComponent<Outline>();
            rowOutline.effectColor = SectionBorderColor;
            rowOutline.effectDistance = new Vector2(1f, -1f);

            var detailsColumn = CreateLayoutContainer(row, "Details", isHorizontal: false, 4f, new RectOffset(0, 0, 0, 0), fitToContents: true);
            var detailsLayout = detailsColumn.gameObject.AddComponent<LayoutElement>();
            detailsLayout.flexibleWidth = 1f;

            var titleText = CreateText(detailsColumn, "Title", builtInPose.Label, 16, FontStyle.Bold, TextPrimaryColor, TextAnchor.UpperLeft);
            titleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            titleText.verticalOverflow = VerticalWrapMode.Overflow;
            CreateBodyText(detailsColumn, $"内置动作 ID：{builtInPose.Id}\n来源：StreamingAssets 默认动作", 13, TextSecondaryColor);

            var actionColumn = CreateLayoutContainer(row, "Actions", isHorizontal: false, 8f, new RectOffset(0, 0, 0, 0), fitToContents: true);
            var actionColumnLayout = actionColumn.gameObject.AddComponent<LayoutElement>();
            actionColumnLayout.preferredWidth = 104f;
            actionColumnLayout.minWidth = 104f;
            if (actionColumn.GetComponent<VerticalLayoutGroup>() is VerticalLayoutGroup actionLayoutGroup)
            {
                actionLayoutGroup.childAlignment = TextAnchor.UpperRight;
                actionLayoutGroup.childControlWidth = false;
                actionLayoutGroup.childForceExpandWidth = false;
            }

            var applyButton = CreateButton(actionColumn, "应用", ButtonColor, 36f, 96f);
            applyButton.onClick.AddListener(() => ApplyBuiltInAction(builtInPose));
            var builtinTagButton = CreateButton(actionColumn, "内置", ButtonMutedColor, 36f, 96f);
            builtinTagButton.interactable = false;
        }

        private void CreateManagedAnimationRow(ContentItem managedAnimation)
        {
            if (generalTabUi == null)
            {
                return;
            }

            var row = CreateLayoutContainer(
                generalTabUi.ActionListRoot,
                $"ActionRow-{managedAnimation.Title}",
                isHorizontal: true,
                spacing: 12f,
                padding: new RectOffset(14, 14, 12, 12),
                fitToContents: true);
            var rowImage = row.gameObject.AddComponent<Image>();
            rowImage.color = InputColor;
            var rowOutline = row.gameObject.AddComponent<Outline>();
            rowOutline.effectColor = SectionBorderColor;
            rowOutline.effectDistance = new Vector2(1f, -1f);

            var detailsColumn = CreateLayoutContainer(row, "Details", isHorizontal: false, 4f, new RectOffset(0, 0, 0, 0), fitToContents: true);
            var detailsLayout = detailsColumn.gameObject.AddComponent<LayoutElement>();
            detailsLayout.flexibleWidth = 1f;

            var titleText = CreateText(detailsColumn, "Title", GetContentDisplayName(managedAnimation), 16, FontStyle.Bold, TextPrimaryColor, TextAnchor.UpperLeft);
            titleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            titleText.verticalOverflow = VerticalWrapMode.Overflow;
            var directoryPath = runtimeController.GetManagedLocalAnimationDisplayDirectory(managedAnimation.EntryPath);
            CreateBodyText(detailsColumn, $"动作存放目录：{directoryPath}", 13, TextSecondaryColor);

            var actionColumn = CreateLayoutContainer(row, "Actions", isHorizontal: false, 8f, new RectOffset(0, 0, 0, 0), fitToContents: true);
            var actionColumnLayout = actionColumn.gameObject.AddComponent<LayoutElement>();
            actionColumnLayout.preferredWidth = 104f;
            actionColumnLayout.minWidth = 104f;
            if (actionColumn.GetComponent<VerticalLayoutGroup>() is VerticalLayoutGroup actionLayoutGroup)
            {
                actionLayoutGroup.childAlignment = TextAnchor.UpperRight;
                actionLayoutGroup.childControlWidth = false;
                actionLayoutGroup.childForceExpandWidth = false;
            }

            var applyButton = CreateButton(actionColumn, "应用", ButtonColor, 36f, 96f);
            applyButton.onClick.AddListener(() => ApplyManagedAnimation(managedAnimation));

            var isPendingDelete = PathsEqual(managedAnimation.EntryPath, pendingDeleteAnimationPath);
            var deleteButton = CreateButton(actionColumn, isPendingDelete ? "确认删除" : "删除", ButtonDangerColor, 36f, 96f);
            deleteButton.onClick.AddListener(() => DeleteManagedAnimation(managedAnimation));
        }

        private void ApplyBuiltInAction(BuiltInPoseOption builtInPose)
        {
            try
            {
                runtimeController.ApplyBuiltInPose(builtInPose.Id);
                statusReporter($"已应用内置动作：{builtInPose.Label}");
            }
            catch (UserFacingException exception)
            {
                statusReporter(exception.Message);
            }
            catch (Exception exception)
            {
                statusReporter($"应用动作失败：{exception.Message}");
            }
        }

        private void ApplyManagedAnimation(ContentItem managedAnimation)
        {
            try
            {
                runtimeController.LoadManagedLocalAnimation(managedAnimation.EntryPath);
                pendingDeleteAnimationPath = string.Empty;
                statusReporter($"已应用动作：{GetContentDisplayName(managedAnimation)}");
                RefreshManagedAnimationLibraryList();
            }
            catch (UserFacingException exception)
            {
                statusReporter(exception.Message);
            }
            catch (Exception exception)
            {
                statusReporter($"应用动作失败：{exception.Message}");
            }
        }

        private void DeleteManagedAnimation(ContentItem managedAnimation)
        {
            if (!PathsEqual(managedAnimation.EntryPath, pendingDeleteAnimationPath))
            {
                pendingDeleteAnimationPath = managedAnimation.EntryPath;
                statusReporter($"再次点击“确认删除”以移除动作：{GetContentDisplayName(managedAnimation)}");
                RefreshManagedAnimationLibraryList();
                return;
            }

            try
            {
                runtimeController.DeleteManagedLocalAnimation(managedAnimation.EntryPath);
                pendingDeleteAnimationPath = string.Empty;
                statusReporter($"已删除动作：{GetContentDisplayName(managedAnimation)}");
                RefreshManagedAnimationLibraryList();
            }
            catch (UserFacingException exception)
            {
                statusReporter(exception.Message);
            }
            catch (Exception exception)
            {
                statusReporter($"删除动作失败：{exception.Message}");
            }
        }

        public void ShowGeneral(Canvas canvas)
        {
            activeTab = SettingsTab.General;
            Show(canvas);
        }

        public void ShowLlm(Canvas canvas)
        {
            activeTab = SettingsTab.Llm;
            Show(canvas);
        }

        private LlmTabUi CreateLlmTab(Transform parent)
        {
            var root = new GameObject("LlmTab", typeof(RectTransform), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = new Vector2(18f, 18f);
            rootRect.offsetMax = new Vector2(-18f, -18f);
            var rootLayout = root.GetComponent<LayoutElement>();
            rootLayout.flexibleHeight = 1f;
            rootLayout.flexibleWidth = 1f;

            var scrollRoot = CreateScrollView(root.transform, out var content);

            var providerSection = CreateSectionCard(content, "ProviderSection", "Provider 配置");
            SetPreferredHeight(providerSection, 430f);
            CreateHintText(providerSection, "管理当前激活的 Provider。保存仅代表本地配置生效。");
            var providerToolbar = CreateLayoutContainer(providerSection, "ProviderToolbar", isHorizontal: true, 8f, new RectOffset(0, 0, 0, 0), fitToContents: true);
            var providerToolbarLayout = providerToolbar.gameObject.AddComponent<LayoutElement>();
            providerToolbarLayout.preferredHeight = 44f;
            var addProviderButton = CreateButton(providerToolbar, "新增 Provider", ButtonColor, 40f, 128f);
            var removeProviderButton = CreateButton(providerToolbar, "删除当前", ButtonDangerColor, 40f, 112f);
            var providerToolbarSpacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            providerToolbarSpacer.transform.SetParent(providerToolbar, false);
            providerToolbarSpacer.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var providerListRoot = CreateLayoutContainer(providerSection, "ProviderList", isHorizontal: false, 6f, new RectOffset(0, 0, 0, 0));
            providerListRoot.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var providerListLayout = providerListRoot.gameObject.AddComponent<LayoutElement>();
            providerListLayout.minHeight = 40f;

            var providerIdentityRow = CreateTwoColumnRow(providerSection, "ProviderIdentityRow");
            var providerDisplayNameInput = CreateInputFieldSection(providerIdentityRow.LeftColumn, "ProviderName", "显示名称", "例如 minimax", preferredHeight: StandardFieldHeight);
            var providerModelInput = CreateInputFieldSection(providerIdentityRow.RightColumn, "ProviderModel", "模型", "例如 gpt-4.1-mini", preferredHeight: StandardFieldHeight);

            var providerBaseUrlInput = CreateInputFieldSection(providerSection, "ProviderBaseUrl", "API URL", "例如 https://api.openai.com/v1", preferredHeight: StandardFieldHeight);
            var providerApiKeyInput = CreateInputFieldSection(providerSection, "ProviderApiKey", "API Key", "sk-...", preferredHeight: StandardFieldHeight, isPassword: true);

            var providerTypeRow = CreateLayoutContainer(providerSection, "ProviderTypeRow", isHorizontal: true, 12f, new RectOffset(0, 0, 0, 0), fitToContents: true);
            var providerTypeInfo = CreateLayoutContainer(providerTypeRow, "ProviderTypeInfo", isHorizontal: true, 10f, new RectOffset(0, 0, 0, 0), fitToContents: true);
            var providerTypeInfoLayout = providerTypeInfo.gameObject.AddComponent<LayoutElement>();
            providerTypeInfoLayout.flexibleWidth = 1f;
            var providerTypeLabel = CreateSingleLineText(providerTypeInfo, "ProviderTypeLabel", "Provider 类型", 15, FontStyle.Bold, TextSecondaryColor, TextAnchor.MiddleLeft);
            var providerTypeLabelLayout = providerTypeLabel.gameObject.AddComponent<LayoutElement>();
            providerTypeLabelLayout.preferredWidth = 112f;
            var providerTypeValue = CreateSingleLineText(providerTypeInfo, "ProviderTypeValue", string.Empty, 15, FontStyle.Normal, TextPrimaryColor, TextAnchor.MiddleLeft);
            var providerTypeValueLayout = providerTypeValue.gameObject.AddComponent<LayoutElement>();
            providerTypeValueLayout.flexibleWidth = 1f;
            var cycleProviderTypeButton = CreateButton(providerTypeRow, "切换类型", ButtonMutedColor, 40f, 96f);
            var providerEnabledToggle = CreateToggleSection(providerSection, "ProviderEnabled", "启用该 Provider");
            var configurationStatusText = CreateHintText(providerSection, string.Empty);

            var globalSection = CreateSectionCard(content, "GlobalSection", "全局对话策略");
            SetPreferredHeight(globalSection, 420f);
            CreateHintText(globalSection, "这些参数控制 prompt、记忆窗口和主动消息节奏。");
            var systemPromptInput = CreateInputFieldSection(globalSection, "SystemPrompt", "System Prompt", "定义角色对话基调与行为边界", multiline: true, preferredHeight: 120f);
            var modelParamsRow = CreateTwoColumnRow(globalSection, "ModelParamsRow");
            var temperatureInput = CreateInputFieldSection(modelParamsRow.LeftColumn, "Temperature", "Temperature", "0.0 - 2.0", preferredHeight: StandardFieldHeight);
            var maxOutputTokensInput = CreateInputFieldSection(modelParamsRow.RightColumn, "MaxOutputTokens", "Max Output Tokens", "例如 256", preferredHeight: StandardFieldHeight);
            var featureToggleRow = CreateTwoColumnRow(globalSection, "FeatureToggleRow");
            var streamingToggle = CreateToggleSection(featureToggleRow.LeftColumn, "EnableStreaming", "启用流式输出");
            var proactiveToggle = CreateToggleSection(featureToggleRow.RightColumn, "EnableProactive", "启用主动消息");
            var proactiveIntervalRow = CreateTwoColumnRow(globalSection, "ProactiveIntervalRow");
            var proactiveMinIntervalInput = CreateInputFieldSection(proactiveIntervalRow.LeftColumn, "ProactiveMinInterval", "主动消息最短间隔（分钟）", "例如 10", preferredHeight: StandardFieldHeight);
            var proactiveMaxIntervalInput = CreateInputFieldSection(proactiveIntervalRow.RightColumn, "ProactiveMaxInterval", "主动消息最长间隔（分钟）", "例如 30", preferredHeight: StandardFieldHeight);
            var memoryRow = CreateTwoColumnRow(globalSection, "MemoryRow");
            var memoryWindowInput = CreateInputFieldSection(memoryRow.LeftColumn, "MemoryWindowTurns", "Recent Turns 保留轮数", "例如 12", preferredHeight: StandardFieldHeight);
            var summaryThresholdInput = CreateInputFieldSection(memoryRow.RightColumn, "SummaryThreshold", "摘要触发阈值", "例如 24", preferredHeight: StandardFieldHeight);
            var enableTtsToggle = CreateToggleSection(globalSection, "EnableTts", "启用 TTS（当前仅框架预留）");

            var statsSection = CreateSectionCard(content, "StatsSection", "LLM 调用统计");
            SetPreferredHeight(statsSection, 230f);
            CreateHintText(statsSection, "这里显示整体调用统计。尚未真正请求模型时会保持为 0。");
            var statsText = CreateBodyText(statsSection, string.Empty, 14, TextPrimaryColor);
            statsText.alignment = TextAnchor.UpperLeft;
            statsText.horizontalOverflow = HorizontalWrapMode.Wrap;
            statsText.verticalOverflow = VerticalWrapMode.Overflow;
            var statsToolbar = CreateLayoutContainer(statsSection, "StatsToolbar", isHorizontal: true, 8f, new RectOffset(0, 0, 0, 0), fitToContents: true);
            var statsToolbarSpacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            statsToolbarSpacer.transform.SetParent(statsToolbar, false);
            statsToolbarSpacer.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var refreshStatsButton = CreateButton(statsToolbar, "刷新统计", ButtonMutedColor, 40f, 104f);
            var resetStatsButton = CreateButton(statsToolbar, "重置统计", ButtonDangerColor, 40f, 104f);

            var actionBar = CreateLayoutContainer(content, "ActionBar", isHorizontal: true, 8f, new RectOffset(0, 0, 0, 0));
            var actionBarLayout = actionBar.gameObject.AddComponent<LayoutElement>();
            actionBarLayout.preferredHeight = 52f;
            var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            spacer.transform.SetParent(actionBar, false);
            spacer.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var reloadButton = CreateButton(actionBar, "重新载入", ButtonMutedColor, 44f, 120f);
            var saveButton = CreateButton(actionBar, "保存设置", ButtonColor, 44f, 132f);

            return new LlmTabUi(
                root,
                scrollRoot,
                content,
                providerSection,
                globalSection,
                statsSection,
                configurationStatusText,
                providerListRoot,
                addProviderButton,
                removeProviderButton,
                providerDisplayNameInput,
                providerTypeValue,
                providerEnabledToggle,
                cycleProviderTypeButton,
                providerBaseUrlInput,
                providerModelInput,
                providerApiKeyInput,
                systemPromptInput,
                temperatureInput,
                maxOutputTokensInput,
                streamingToggle,
                proactiveToggle,
                proactiveMinIntervalInput,
                proactiveMaxIntervalInput,
                memoryWindowInput,
                summaryThresholdInput,
                enableTtsToggle,
                statsText,
                refreshStatsButton,
                resetStatsButton,
                reloadButton,
                saveButton);
        }

        private static RectTransform CreateScrollView(Transform parent, out RectTransform content)
        {
            var rootObject = new GameObject(
                "ScrollView",
                typeof(RectTransform),
                typeof(Image),
                typeof(ScrollRect));
            var root = rootObject.GetComponent<RectTransform>();
            root.SetParent(parent, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var rootImage = rootObject.GetComponent<Image>();
            rootImage.color = new Color(0f, 0f, 0f, 0f);

            var viewportObject = new GameObject(
                "Viewport",
                typeof(RectTransform),
                typeof(Image),
                typeof(RectMask2D));
            var viewport = viewportObject.GetComponent<RectTransform>();
            viewport.SetParent(root, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;

            var viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0f);
            viewportImage.raycastTarget = true;

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
            content.sizeDelta = new Vector2(0f, 0f);

            var layout = contentObject.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.padding = new RectOffset(0, 8, 0, 0);
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
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

            return root;
        }

        private RectTransform CreateSectionCard(Transform parent, string name, string title)
        {
            var root = CreateLayoutContainer(parent, name, isHorizontal: false, StandardSectionSpacing, new RectOffset(20, 20, 20, 20), fitToContents: true);
            var image = root.gameObject.AddComponent<Image>();
            image.color = SectionColor;
            var outline = root.gameObject.AddComponent<Outline>();
            outline.effectColor = SectionBorderColor;
            outline.effectDistance = new Vector2(1f, -1f);
            CreateSingleLineText(root, "Title", title, 18, FontStyle.Bold, TextPrimaryColor, TextAnchor.MiddleLeft);
            return root;
        }

        private Text CreateBodyText(Transform parent, string text, int fontSize = 14, Color? color = null)
        {
            return CreateText(parent, "BodyText", text, fontSize, FontStyle.Normal, color ?? TextSecondaryColor, TextAnchor.UpperLeft);
        }

        private Text CreateHintText(Transform parent, string text)
        {
            var hint = CreateText(parent, "HintText", text, 12, FontStyle.Normal, TextMutedColor, TextAnchor.UpperLeft);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            hint.verticalOverflow = VerticalWrapMode.Overflow;
            return hint;
        }

        private Text CreateSingleLineText(
            Transform parent,
            string name,
            string text,
            int fontSize,
            FontStyle fontStyle,
            Color color,
            TextAnchor alignment)
        {
            var label = CreateText(parent, name, text, fontSize, fontStyle, color, alignment);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        private TwoColumnRow CreateTwoColumnRow(Transform parent, string name)
        {
            var row = CreateLayoutContainer(parent, name, isHorizontal: true, 16f, new RectOffset(0, 0, 0, 0), fitToContents: true);
            var leftColumn = CreateLayoutContainer(row, "LeftColumn", isHorizontal: false, 6f, new RectOffset(0, 0, 0, 0), fitToContents: true);
            var leftLayout = leftColumn.gameObject.AddComponent<LayoutElement>();
            leftLayout.flexibleWidth = 1f;

            var rightColumn = CreateLayoutContainer(row, "RightColumn", isHorizontal: false, 6f, new RectOffset(0, 0, 0, 0), fitToContents: true);
            var rightLayout = rightColumn.gameObject.AddComponent<LayoutElement>();
            rightLayout.flexibleWidth = 1f;

            return new TwoColumnRow(leftColumn, rightColumn);
        }

        private InputField CreateInputFieldSection(
            Transform parent,
            string name,
            string label,
            string placeholder,
            bool multiline = false,
            float preferredHeight = 44f,
            bool isPassword = false)
        {
            var section = CreateLayoutContainer(parent, name, isHorizontal: false, 8f, new RectOffset(0, 0, 0, 0), fitToContents: true);
            CreateSingleLineText(section, "Label", label, 14, FontStyle.Bold, TextSecondaryColor, TextAnchor.MiddleLeft);
            return CreateInputField(section, placeholder, preferredHeight, multiline, isPassword);
        }

        private Toggle CreateToggleSection(Transform parent, string name, string label)
        {
            var section = CreateLayoutContainer(parent, name, isHorizontal: false, 6f, new RectOffset(0, 0, 0, 0), fitToContents: true);
            return CreateToggle(section, label);
        }

        private Button CreateButton(Transform parent, string label, Color backgroundColor, float preferredHeight, float preferredWidth = -1f)
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
            background.raycastTarget = true;

            var button = buttonObject.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = background;
            button.colors = CreateButtonColors(backgroundColor);
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            var layoutElement = buttonObject.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = preferredHeight;
            if (preferredWidth > 0f)
            {
                layoutElement.preferredWidth = preferredWidth;
            }

            var buttonLabel = CreateSingleLineText(rectTransform, "Label", label, 15, FontStyle.Bold, TextPrimaryColor, TextAnchor.MiddleCenter);
            StretchRect(buttonLabel.rectTransform);
            buttonLabel.rectTransform.offsetMin = new Vector2(12f, 0f);
            buttonLabel.rectTransform.offsetMax = new Vector2(-12f, 0f);
            return button;
        }

        private InputField CreateInputField(Transform parent, string placeholder, float preferredHeight, bool multiline, bool isPassword)
        {
            var fieldObject = new GameObject(
                "InputField",
                typeof(RectTransform),
                typeof(Image),
                typeof(InputField),
                typeof(LayoutElement));
            var rectTransform = fieldObject.GetComponent<RectTransform>();
            rectTransform.SetParent(parent, false);

            var background = fieldObject.GetComponent<Image>();
            background.color = InputColor;
            background.raycastTarget = true;

            var layoutElement = fieldObject.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = preferredHeight;

            var textViewport = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            var viewportRect = textViewport.GetComponent<RectTransform>();
            viewportRect.SetParent(fieldObject.transform, false);
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(14f, 10f);
            viewportRect.offsetMax = new Vector2(-14f, -10f);

            var placeholderText = CreateText(
                viewportRect,
                "Placeholder",
                placeholder,
                15,
                FontStyle.Normal,
                TextMutedColor,
                multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            placeholderText.fontStyle = FontStyle.Italic;
            StretchRect(placeholderText.rectTransform);

            var inputText = CreateText(
                viewportRect,
                "Text",
                string.Empty,
                15,
                FontStyle.Normal,
                TextPrimaryColor,
                multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            StretchRect(inputText.rectTransform);

            var inputField = fieldObject.GetComponent<InputField>();
            inputField.textComponent = inputText;
            inputField.placeholder = placeholderText;
            inputField.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            inputField.contentType = isPassword ? InputField.ContentType.Password : InputField.ContentType.Standard;
            inputField.transition = Selectable.Transition.ColorTint;
            inputField.colors = CreateButtonColors(InputColor);
            inputField.caretWidth = 2;
            inputField.customCaretColor = true;
            inputField.caretColor = TextPrimaryColor;

            inputText.horizontalOverflow = HorizontalWrapMode.Wrap;
            inputText.verticalOverflow = VerticalWrapMode.Truncate;
            if (multiline)
            {
                inputText.horizontalOverflow = HorizontalWrapMode.Wrap;
                inputText.alignment = TextAnchor.UpperLeft;
                inputText.resizeTextForBestFit = false;
                placeholderText.horizontalOverflow = HorizontalWrapMode.Wrap;
                placeholderText.alignment = TextAnchor.UpperLeft;
            }
            else
            {
                inputText.horizontalOverflow = HorizontalWrapMode.Overflow;
                inputText.alignment = TextAnchor.MiddleLeft;
                placeholderText.horizontalOverflow = HorizontalWrapMode.Overflow;
                placeholderText.alignment = TextAnchor.MiddleLeft;
            }

            return inputField;
        }

        private Toggle CreateToggle(Transform parent, string label)
        {
            var root = CreateLayoutContainer(parent, "ToggleRow", isHorizontal: true, 12f, new RectOffset(0, 0, 0, 0));
            var layoutElement = root.gameObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 32f;

            var toggleObject = new GameObject(
                "Toggle",
                typeof(RectTransform),
                typeof(Toggle),
                typeof(LayoutElement));
            var toggleRect = toggleObject.GetComponent<RectTransform>();
            toggleRect.SetParent(root, false);
            var toggleLayout = toggleObject.GetComponent<LayoutElement>();
            toggleLayout.preferredWidth = 26f;
            toggleLayout.preferredHeight = 26f;

            var backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
            var backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.SetParent(toggleRect, false);
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            var background = backgroundObject.GetComponent<Image>();
            background.color = ToggleOffColor;

            var checkmarkObject = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            var checkmarkRect = checkmarkObject.GetComponent<RectTransform>();
            checkmarkRect.SetParent(backgroundRect, false);
            checkmarkRect.anchorMin = new Vector2(0.2f, 0.2f);
            checkmarkRect.anchorMax = new Vector2(0.8f, 0.8f);
            checkmarkRect.offsetMin = Vector2.zero;
            checkmarkRect.offsetMax = Vector2.zero;
            var checkmark = checkmarkObject.GetComponent<Image>();
            checkmark.color = TextPrimaryColor;

            var toggle = toggleObject.GetComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = checkmark;
            toggle.navigation = new Navigation { mode = Navigation.Mode.None };
            toggle.onValueChanged.AddListener(value => background.color = value ? ToggleOnColor : ToggleOffColor);

            var labelText = CreateText(root, "Label", label, 15, FontStyle.Normal, TextPrimaryColor, TextAnchor.MiddleLeft);
            var labelLayout = labelText.gameObject.AddComponent<LayoutElement>();
            labelLayout.flexibleWidth = 1f;

            return toggle;
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
                layout.childAlignment = TextAnchor.UpperLeft;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;
            }
            else
            {
                var layout = objectRoot.GetComponent<VerticalLayoutGroup>();
                layout.spacing = spacing;
                layout.padding = padding;
                layout.childAlignment = TextAnchor.UpperLeft;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
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

        private void RebuildLayout()
        {
            if (windowUi == null)
            {
                return;
            }

            ApplyChromeLayout();
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(windowUi.Panel);
            if (windowUi.GeneralContent.activeSelf)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)windowUi.GeneralContent.transform);
            }

            if (windowUi.LlmContent.activeSelf)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)windowUi.LlmContent.transform);
            }

            Canvas.ForceUpdateCanvases();
        }

        private void ApplyChromeLayout()
        {
            if (windowUi == null)
            {
                return;
            }

            windowUi.HeaderRoot.anchorMin = new Vector2(0f, 1f);
            windowUi.HeaderRoot.anchorMax = new Vector2(1f, 1f);
            windowUi.HeaderRoot.pivot = new Vector2(0.5f, 1f);
            windowUi.HeaderRoot.offsetMin = new Vector2(0f, -HeaderHeight);
            windowUi.HeaderRoot.offsetMax = Vector2.zero;

            windowUi.BodyRoot.anchorMin = Vector2.zero;
            windowUi.BodyRoot.anchorMax = Vector2.one;
            windowUi.BodyRoot.offsetMin = new Vector2(0f, 0f);
            windowUi.BodyRoot.offsetMax = new Vector2(0f, -HeaderHeight);

            windowUi.SidebarRoot.anchorMin = new Vector2(0f, 0f);
            windowUi.SidebarRoot.anchorMax = new Vector2(0f, 1f);
            windowUi.SidebarRoot.pivot = new Vector2(0f, 1f);
            windowUi.SidebarRoot.offsetMin = new Vector2(BodyPadding, BodyPadding);
            windowUi.SidebarRoot.offsetMax = new Vector2(BodyPadding + SidebarWidth, -BodyPadding);

            windowUi.ContentHostRoot.anchorMin = new Vector2(0f, 0f);
            windowUi.ContentHostRoot.anchorMax = new Vector2(1f, 1f);
            windowUi.ContentHostRoot.offsetMin = new Vector2(BodyPadding + SidebarWidth + ColumnGap, BodyPadding);
            windowUi.ContentHostRoot.offsetMax = new Vector2(-BodyPadding, -BodyPadding);
        }

        private void LogLayoutDiagnostics(string reason)
        {
            if (windowUi == null)
            {
                return;
            }

            var lines = new[]
            {
                $"reason={reason}",
                $"panel={DescribeRect(windowUi.Panel)}",
                $"generalActive={windowUi.GeneralContent.activeSelf} general={DescribeRect((RectTransform)windowUi.GeneralContent.transform)}",
                $"llmActive={windowUi.LlmContent.activeSelf} llm={DescribeRect((RectTransform)windowUi.LlmContent.transform)}",
                $"scroll={DescribeRect(windowUi.LlmScrollRoot)} scrollContent={DescribeRect(windowUi.LlmScrollContent)} childCount={windowUi.LlmScrollContent.childCount}",
                $"providerSection={DescribeRect(windowUi.ProviderSection)} providerList={DescribeRect(windowUi.ProviderListRoot)} providerButtons={windowUi.ProviderListRoot.childCount}",
                $"globalSection={DescribeRect(windowUi.GlobalSection)}",
                $"statsSection={DescribeRect(windowUi.StatsSection)} statsTextLen={windowUi.StatsText.text.Length}",
            };

            Log(string.Join(" | ", lines));
        }

        private static void SetPreferredHeight(Component component, float preferredHeight)
        {
            var layoutElement = component.GetComponent<LayoutElement>() ?? component.gameObject.AddComponent<LayoutElement>();
            layoutElement.minHeight = preferredHeight;
            layoutElement.preferredHeight = preferredHeight;
        }

        private static void StretchRect(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
        }

        private void Log(string message)
        {
            Debug.Log($"[DesktopPetSettingsWindow] {message}");
        }

        private static string DescribeRect(RectTransform rectTransform)
        {
            var worldRect = GetScreenRect(rectTransform);
            return $"{rectTransform.gameObject.activeSelf}:{rectTransform.rect.width:0.0}x{rectTransform.rect.height:0.0}@{FormatRect(worldRect)}";
        }

        private static Rect GetScreenRect(RectTransform rectTransform)
        {
            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private static string FormatRect(Rect rect)
        {
            return $"({rect.xMin:0.0},{rect.yMin:0.0})-({rect.xMax:0.0},{rect.yMax:0.0})";
        }

        private Text CreateText(
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

            var label = textObject.GetComponent<Text>();
            label.font = GetUiFont();
            label.fontSize = fontSize;
            label.fontStyle = fontStyle;
            label.color = color;
            label.alignment = alignment;
            label.supportRichText = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.text = text;
            label.raycastTarget = false;
            return label;
        }

        private static ColorBlock CreateButtonColors(Color baseColor)
        {
            return new ColorBlock
            {
                normalColor = baseColor,
                highlightedColor = Tint(baseColor, 1.12f),
                pressedColor = Tint(baseColor, 0.9f),
                selectedColor = Tint(baseColor, 1.08f),
                disabledColor = new Color(baseColor.r * 0.5f, baseColor.g * 0.5f, baseColor.b * 0.5f, 0.6f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };
        }

        private static Color Tint(Color color, float multiplier)
        {
            return new Color(
                Mathf.Clamp01(color.r * multiplier),
                Mathf.Clamp01(color.g * multiplier),
                Mathf.Clamp01(color.b * multiplier),
                color.a);
        }

        private static string GetProviderTypeLabel(LlmProviderType providerType)
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

        private static string Fallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetContentDisplayName(ContentItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.Title))
            {
                return item.Title.Trim();
            }

            return Path.GetFileNameWithoutExtension(item.EntryPath);
        }

        private static string TryFormatTimestamp(string rawTimestamp)
        {
            if (string.IsNullOrWhiteSpace(rawTimestamp))
            {
                return string.Empty;
            }

            return DateTimeOffset.TryParse(rawTimestamp, out var timestamp)
                ? timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                : rawTimestamp;
        }

        private string BuildConfigurationStatusText(LlmProviderProfile profile)
        {
            var hasApiUrl = profile.ProviderType == LlmProviderType.OpenClaw
                ? !string.IsNullOrWhiteSpace(profile.OpenClawGatewayWsUrl)
                : !string.IsNullOrWhiteSpace(profile.BaseUrl);
            var hasModel = profile.ProviderType == LlmProviderType.OpenClaw
                ? !string.IsNullOrWhiteSpace(profile.OpenClawAgentId)
                : !string.IsNullOrWhiteSpace(profile.Model);
            var hasApiKey = editingApiKeys.TryGetValue(profile.Id, out var apiKey) && !string.IsNullOrWhiteSpace(apiKey);
            var completeness = hasApiUrl && hasModel && hasApiKey
                ? "字段完整"
                : "字段未完整";
            return $"保存状态：{configurationStatusMessage} | 当前 Provider：{profile.DisplayName} | {completeness}";
        }

        private static bool TryParseFloat(string value, string fieldName, out float result, out string errorMessage)
        {
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            {
                errorMessage = string.Empty;
                return true;
            }

            errorMessage = $"{fieldName} 不是合法数字。";
            return false;
        }

        private static bool TryParseInt(string value, string fieldName, out int result, out string errorMessage)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            {
                errorMessage = string.Empty;
                return true;
            }

            errorMessage = $"{fieldName} 不是合法整数。";
            return false;
        }

        private static void ClearChildren(Transform parent)
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
            var text = parent.GetComponentInChildren<Text>();
            return text != null
                ? text
                : throw new InvalidOperationException($"No Text component was found under '{parent.name}'.");
        }

        private static Font GetUiFont()
        {
            return RuntimeUiFontResolver.GetFont();
        }

        private enum SettingsTab
        {
            General,
            Llm,
        }

        private sealed class HeaderUi
        {
            public HeaderUi(RectTransform root, Button closeButton)
            {
                Root = root;
                CloseButton = closeButton;
            }

            public RectTransform Root { get; }

            public Button CloseButton { get; }
        }

        private sealed class SidebarUi
        {
            public SidebarUi(RectTransform root, Button generalTabButton, Text generalTabButtonText, Button llmTabButton, Text llmTabButtonText)
            {
                Root = root;
                GeneralTabButton = generalTabButton;
                GeneralTabButtonText = generalTabButtonText;
                LlmTabButton = llmTabButton;
                LlmTabButtonText = llmTabButtonText;
            }

            public RectTransform Root { get; }

            public Button GeneralTabButton { get; }

            public Text GeneralTabButtonText { get; }

            public Button LlmTabButton { get; }

            public Text LlmTabButtonText { get; }
        }

        private sealed class GeneralTabUi
        {
            public GeneralTabUi(
                GameObject root,
                RectTransform scrollRoot,
                RectTransform scrollContent,
                RectTransform librarySection,
                Text libraryHintText,
                RectTransform libraryListRoot,
                Button importButton,
                Button refreshButton,
                RectTransform actionSection,
                RectTransform builtInActionListRoot,
                Text actionHintText,
                RectTransform actionListRoot,
                Button importActionButton,
                Button refreshActionButton)
            {
                Root = root;
                ScrollRoot = scrollRoot;
                ScrollContent = scrollContent;
                LibrarySection = librarySection;
                LibraryHintText = libraryHintText;
                LibraryListRoot = libraryListRoot;
                ImportButton = importButton;
                RefreshButton = refreshButton;
                ActionSection = actionSection;
                BuiltInActionListRoot = builtInActionListRoot;
                ActionHintText = actionHintText;
                ActionListRoot = actionListRoot;
                ImportActionButton = importActionButton;
                RefreshActionButton = refreshActionButton;
            }

            public GameObject Root { get; }

            public RectTransform ScrollRoot { get; }

            public RectTransform ScrollContent { get; }

            public RectTransform LibrarySection { get; }

            public Text LibraryHintText { get; }

            public RectTransform LibraryListRoot { get; }

            public Button ImportButton { get; }

            public Button RefreshButton { get; }

            public RectTransform ActionSection { get; }

            public RectTransform BuiltInActionListRoot { get; }

            public Text ActionHintText { get; }

            public RectTransform ActionListRoot { get; }

            public Button ImportActionButton { get; }

            public Button RefreshActionButton { get; }
        }

        private sealed class LlmTabUi
        {
            public LlmTabUi(
                GameObject root,
                RectTransform scrollRoot,
                RectTransform scrollContent,
                RectTransform providerSection,
                RectTransform globalSection,
                RectTransform statsSection,
                Text configurationStatusText,
                RectTransform providerListRoot,
                Button addProviderButton,
                Button removeProviderButton,
                InputField providerDisplayNameInput,
                Text providerTypeValueText,
                Toggle providerEnabledToggle,
                Button cycleProviderTypeButton,
                InputField providerBaseUrlInput,
                InputField providerModelInput,
                InputField providerApiKeyInput,
                InputField systemPromptInput,
                InputField temperatureInput,
                InputField maxOutputTokensInput,
                Toggle enableStreamingToggle,
                Toggle enableProactiveToggle,
                InputField proactiveMinIntervalInput,
                InputField proactiveMaxIntervalInput,
                InputField memoryWindowInput,
                InputField summaryThresholdInput,
                Toggle enableTtsToggle,
                Text statsText,
                Button refreshStatsButton,
                Button resetStatsButton,
                Button reloadButton,
                Button saveButton)
            {
                Root = root;
                ScrollRoot = scrollRoot;
                ScrollContent = scrollContent;
                ProviderSection = providerSection;
                GlobalSection = globalSection;
                StatsSection = statsSection;
                ConfigurationStatusText = configurationStatusText;
                ProviderListRoot = providerListRoot;
                AddProviderButton = addProviderButton;
                RemoveProviderButton = removeProviderButton;
                ProviderDisplayNameInput = providerDisplayNameInput;
                ProviderTypeValueText = providerTypeValueText;
                ProviderEnabledToggle = providerEnabledToggle;
                CycleProviderTypeButton = cycleProviderTypeButton;
                ProviderBaseUrlInput = providerBaseUrlInput;
                ProviderModelInput = providerModelInput;
                ProviderApiKeyInput = providerApiKeyInput;
                SystemPromptInput = systemPromptInput;
                TemperatureInput = temperatureInput;
                MaxOutputTokensInput = maxOutputTokensInput;
                EnableStreamingToggle = enableStreamingToggle;
                EnableProactiveToggle = enableProactiveToggle;
                ProactiveMinIntervalInput = proactiveMinIntervalInput;
                ProactiveMaxIntervalInput = proactiveMaxIntervalInput;
                MemoryWindowInput = memoryWindowInput;
                SummaryThresholdInput = summaryThresholdInput;
                EnableTtsToggle = enableTtsToggle;
                StatsText = statsText;
                RefreshStatsButton = refreshStatsButton;
                ResetStatsButton = resetStatsButton;
                ReloadButton = reloadButton;
                SaveButton = saveButton;
            }

            public GameObject Root { get; }

            public RectTransform ScrollRoot { get; }

            public RectTransform ScrollContent { get; }

            public RectTransform ProviderSection { get; }

            public RectTransform GlobalSection { get; }

            public RectTransform StatsSection { get; }

            public Text ConfigurationStatusText { get; }

            public RectTransform ProviderListRoot { get; }

            public Button AddProviderButton { get; }

            public Button RemoveProviderButton { get; }

            public InputField ProviderDisplayNameInput { get; }

            public Text ProviderTypeValueText { get; }

            public Toggle ProviderEnabledToggle { get; }

            public Button CycleProviderTypeButton { get; }

            public InputField ProviderBaseUrlInput { get; }

            public InputField ProviderModelInput { get; }

            public InputField ProviderApiKeyInput { get; }

            public InputField SystemPromptInput { get; }

            public InputField TemperatureInput { get; }

            public InputField MaxOutputTokensInput { get; }

            public Toggle EnableStreamingToggle { get; }

            public Toggle EnableProactiveToggle { get; }

            public InputField ProactiveMinIntervalInput { get; }

            public InputField ProactiveMaxIntervalInput { get; }

            public InputField MemoryWindowInput { get; }

            public InputField SummaryThresholdInput { get; }

            public Toggle EnableTtsToggle { get; }

            public Text StatsText { get; }

            public Button RefreshStatsButton { get; }

            public Button ResetStatsButton { get; }

            public Button ReloadButton { get; }

            public Button SaveButton { get; }
        }

        private sealed class SettingsWindowUi
        {
            public SettingsWindowUi(
                RectTransform root,
                RectTransform panel,
                RectTransform layoutRoot,
                RectTransform headerRoot,
                RectTransform bodyRoot,
                RectTransform sidebarRoot,
                RectTransform contentHostRoot,
                Button closeButton,
                Button generalTabButton,
                Text generalTabButtonText,
                Button llmTabButton,
                Text llmTabButtonText,
                GameObject generalContent,
                GameObject llmContent,
                RectTransform llmScrollRoot,
                RectTransform llmScrollContent,
                RectTransform providerSection,
                RectTransform globalSection,
                RectTransform statsSection,
                Text configurationStatusText,
                RectTransform providerListRoot,
                InputField providerDisplayNameInput,
                Text providerTypeValueText,
                Toggle providerEnabledToggle,
                InputField providerBaseUrlInput,
                InputField providerModelInput,
                InputField providerApiKeyInput,
                InputField systemPromptInput,
                InputField temperatureInput,
                InputField maxOutputTokensInput,
                Toggle enableStreamingToggle,
                Toggle enableProactiveToggle,
                InputField proactiveMinIntervalInput,
                InputField proactiveMaxIntervalInput,
                InputField memoryWindowInput,
                InputField summaryThresholdInput,
                Toggle enableTtsToggle,
                Text statsText)
            {
                Root = root;
                Panel = panel;
                LayoutRoot = layoutRoot;
                HeaderRoot = headerRoot;
                BodyRoot = bodyRoot;
                SidebarRoot = sidebarRoot;
                ContentHostRoot = contentHostRoot;
                CloseButton = closeButton;
                GeneralTabButton = generalTabButton;
                GeneralTabButtonText = generalTabButtonText;
                LlmTabButton = llmTabButton;
                LlmTabButtonText = llmTabButtonText;
                GeneralContent = generalContent;
                LlmContent = llmContent;
                LlmScrollRoot = llmScrollRoot;
                LlmScrollContent = llmScrollContent;
                ProviderSection = providerSection;
                GlobalSection = globalSection;
                StatsSection = statsSection;
                ConfigurationStatusText = configurationStatusText;
                ProviderListRoot = providerListRoot;
                ProviderDisplayNameInput = providerDisplayNameInput;
                ProviderTypeValueText = providerTypeValueText;
                ProviderEnabledToggle = providerEnabledToggle;
                ProviderBaseUrlInput = providerBaseUrlInput;
                ProviderModelInput = providerModelInput;
                ProviderApiKeyInput = providerApiKeyInput;
                SystemPromptInput = systemPromptInput;
                TemperatureInput = temperatureInput;
                MaxOutputTokensInput = maxOutputTokensInput;
                EnableStreamingToggle = enableStreamingToggle;
                EnableProactiveToggle = enableProactiveToggle;
                ProactiveMinIntervalInput = proactiveMinIntervalInput;
                ProactiveMaxIntervalInput = proactiveMaxIntervalInput;
                MemoryWindowInput = memoryWindowInput;
                SummaryThresholdInput = summaryThresholdInput;
                EnableTtsToggle = enableTtsToggle;
                StatsText = statsText;
            }

            public RectTransform Root { get; }

            public RectTransform Panel { get; }

            public RectTransform LayoutRoot { get; }

            public RectTransform HeaderRoot { get; }

            public RectTransform BodyRoot { get; }

            public RectTransform SidebarRoot { get; }

            public RectTransform ContentHostRoot { get; }

            public Button CloseButton { get; }

            public Button GeneralTabButton { get; }

            public Text GeneralTabButtonText { get; }

            public Button LlmTabButton { get; }

            public Text LlmTabButtonText { get; }

            public GameObject GeneralContent { get; }

            public GameObject LlmContent { get; }

            public RectTransform LlmScrollRoot { get; }

            public RectTransform LlmScrollContent { get; }

            public RectTransform ProviderSection { get; }

            public RectTransform GlobalSection { get; }

            public RectTransform StatsSection { get; }

            public Text ConfigurationStatusText { get; }

            public RectTransform ProviderListRoot { get; }

            public InputField ProviderDisplayNameInput { get; }

            public Text ProviderTypeValueText { get; }

            public Toggle ProviderEnabledToggle { get; }

            public InputField ProviderBaseUrlInput { get; }

            public InputField ProviderModelInput { get; }

            public InputField ProviderApiKeyInput { get; }

            public InputField SystemPromptInput { get; }

            public InputField TemperatureInput { get; }

            public InputField MaxOutputTokensInput { get; }

            public Toggle EnableStreamingToggle { get; }

            public Toggle EnableProactiveToggle { get; }

            public InputField ProactiveMinIntervalInput { get; }

            public InputField ProactiveMaxIntervalInput { get; }

            public InputField MemoryWindowInput { get; }

            public InputField SummaryThresholdInput { get; }

            public Toggle EnableTtsToggle { get; }

            public Text StatsText { get; }
        }

        private sealed class TwoColumnRow
        {
            public TwoColumnRow(RectTransform leftColumn, RectTransform rightColumn)
            {
                LeftColumn = leftColumn;
                RightColumn = rightColumn;
            }

            public RectTransform LeftColumn { get; }

            public RectTransform RightColumn { get; }
        }
    }
}
