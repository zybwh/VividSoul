#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VividSoul.Runtime;
using VividSoul.Runtime.AI;
using VividSoul.Runtime.Animation;
using VividSoul.Runtime.Interaction;

namespace VividSoul.Runtime.App
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DesktopPetRuntimeController))]
    public sealed class DesktopPetRuntimeHud : MonoBehaviour
    {
        private const string ContextMenuPrefabPath = "Modern UI Pack/Prefabs/Context Menu/Context Menu";
        private const float ContextMenuGap = 8f;
        private const float ContextMenuHitPadding = 96f;
        private const float SubmenuCloseDelaySeconds = 0.15f;
        private const float SubmenuCloseHitPadding = 20f;
        private const float SubmenuOpenSettleSeconds = 0.05f;
        private const float ContextMenuLeaveCloseDelaySeconds = 0.2f;
        private const float MenuStayOpenClusterPadding = 32f;
        private const float SubmenuMaxViewportHeight = 520f;
        private const float MinimumScrollableViewportHeight = 120f;
        private const float ScrollViewportScreenMargin = 24f;
        private const float MenuScrollSensitivity = 36f;
        private const float MenuScrollbarWidth = 10f;
        private const float MenuScrollbarInset = 6f;
        private const float StatusMessageDurationSeconds = 3f;
        private const float StatusMessageBottomOffset = 72f;
        private static readonly Color MenuButtonColor = new(0.10f, 0.18f, 0.26f, 1f);
        private static readonly Color MenuButtonHighlightColor = new(0.21f, 0.33f, 0.47f, 1f);
        private static readonly Color MenuButtonPressedColor = new(0.29f, 0.42f, 0.58f, 1f);
        private static readonly Color MenuButtonDisabledColor = new(0.18f, 0.24f, 0.31f, 1f);
        private static readonly Color MenuScrollbarTrackColor = new(0.05f, 0.08f, 0.11f, 0.70f);
        private static readonly Color MenuScrollbarHandleColor = new(0.54f, 0.68f, 0.82f, 0.95f);
        private static readonly Color StatusMessageBackgroundColor = new(0.08f, 0.11f, 0.15f, 0.92f);
        private const int RightMouseButton = 1;

        private readonly DesktopPetBoundsService boundsService = new();

        private MenuUi? contextMenuUi;
        private ContextMenuSubmenu currentSubmenu = ContextMenuSubmenu.None;
        private int menuSessionId;
        private bool ownsEventSystem;
        private DesktopPetRuntimeController? runtimeController;
        private DesktopPetSpeechBubblePresenter? speechBubblePresenter;
        private DesktopPetChatOverlayPresenter? chatOverlayPresenter;
        private DesktopPetFairyGuiChatPresenter? fairyGuiChatPresenter;
        private DesktopPetSettingsWindowPresenter? settingsWindowPresenter;
        private MateActionDispatcher? actionDispatcher;
        private float scheduledSubmenuCloseTime = float.PositiveInfinity;
        private float statusMessageHideAtTime = float.PositiveInfinity;
        private CanvasGroup? statusMessageCanvasGroup;
        private RectTransform? statusMessageRoot;
        private Text? statusMessageText;
        private float submenuCloseAllowedAtTime = float.PositiveInfinity;
        private float contextMenuLeaveAllowedAtTime = float.PositiveInfinity;
        private float scheduledContextMenuLeaveCloseTime = float.PositiveInfinity;
        private MenuUi? submenuUi;
        private Canvas? uiCanvas;
        private CancellationTokenSource? chatRequestCancellationTokenSource;
        private bool isChatRequestPending;
        private readonly ConcurrentQueue<Action> pendingConversationUiActions = new();

        private void Awake()
        {
            runtimeController = GetComponent<DesktopPetRuntimeController>();
            speechBubblePresenter = new DesktopPetSpeechBubblePresenter(boundsService);
            actionDispatcher = new MateActionDispatcher(runtimeController);
            chatOverlayPresenter = new DesktopPetChatOverlayPresenter(
                ShowStatusMessage,
                HandleChatMessageSubmitted,
                () => runtimeController?.MarkConversationMessagesRead());
            fairyGuiChatPresenter = new DesktopPetFairyGuiChatPresenter(
                ShowStatusMessage,
                HandleChatMessageSubmitted,
                () => runtimeController?.MarkConversationMessagesRead());
            settingsWindowPresenter = new DesktopPetSettingsWindowPresenter(runtimeController!, ShowStatusMessage);
            EnsureCanvasExists();
            chatOverlayPresenter.Attach(uiCanvas!);
        }

        private void OnEnable()
        {
            if (runtimeController != null)
            {
                runtimeController.ModelLoadFailed += HandleRuntimeFailure;
                runtimeController.BuiltInPoseTriggered += HandleBuiltInPoseTriggered;
                runtimeController.ConversationMessageReceived += HandleConversationMessageReceived;
                runtimeController.ConversationStatusChanged += HandleConversationStatusChanged;
            }

            EnsureCanvasExists();
            chatOverlayPresenter?.Attach(uiCanvas!);
        }

        private void Update()
        {
            HandleContextMenuInput();
            UpdateStatusMessageVisibility();
            speechBubblePresenter?.Update(Time.unscaledDeltaTime);
            chatOverlayPresenter?.Update(Time.unscaledDeltaTime);
            fairyGuiChatPresenter?.Update(Time.unscaledDeltaTime);
            settingsWindowPresenter?.Update(Time.unscaledDeltaTime);
            DrainPendingConversationUiActions();
            runtimeController?.SetExpandedWindowMode(ShouldUseExpandedWindowMode());

            if (runtimeController != null
                && AreContextMenusVisible()
                && !Application.isFocused)
            {
                CloseContextMenus("application-unfocused");
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus || !AreContextMenusVisible())
            {
                return;
            }

            Log($"close menus reason=application-focus-lost");
            CloseContextMenus("application-focus-lost");
        }

        private void OnDisable()
        {
            if (runtimeController != null)
            {
                runtimeController.ModelLoadFailed -= HandleRuntimeFailure;
                runtimeController.BuiltInPoseTriggered -= HandleBuiltInPoseTriggered;
                runtimeController.ConversationMessageReceived -= HandleConversationMessageReceived;
                runtimeController.ConversationStatusChanged -= HandleConversationStatusChanged;
            }

            CloseContextMenus();
            HideStatusMessage();
            CancelChatRequest();
            speechBubblePresenter?.HideImmediate();
            chatOverlayPresenter?.Hide();
            fairyGuiChatPresenter?.Hide();
            settingsWindowPresenter?.Hide();
            runtimeController?.SetExpandedWindowMode(false);
        }

        private void OnDestroy()
        {
            if (runtimeController != null)
            {
                runtimeController.ModelLoadFailed -= HandleRuntimeFailure;
                runtimeController.BuiltInPoseTriggered -= HandleBuiltInPoseTriggered;
                runtimeController.ConversationMessageReceived -= HandleConversationMessageReceived;
                runtimeController.ConversationStatusChanged -= HandleConversationStatusChanged;
            }

            CloseContextMenus();
            CancelChatRequest();
            speechBubblePresenter?.HideImmediate();
            chatOverlayPresenter?.Hide();
            fairyGuiChatPresenter?.Hide();
            settingsWindowPresenter?.Hide();
            runtimeController?.SetExpandedWindowMode(false);
            chatOverlayPresenter?.Dispose();
            fairyGuiChatPresenter?.Dispose();
            settingsWindowPresenter?.Dispose();

            if (uiCanvas != null)
            {
                Destroy(uiCanvas.gameObject);
            }

            if (ownsEventSystem && EventSystem.current != null)
            {
                Destroy(EventSystem.current.gameObject);
            }
        }

        private void HandleRuntimeFailure(string message)
        {
            ShowStatusMessage(message);
        }

        private void HandleBuiltInPoseTriggered(BuiltInPosePlaybackEvent playbackEvent)
        {
            if (runtimeController == null
                || speechBubblePresenter == null
                || !playbackEvent.UseCatalogBubble
                || !SpeechBubbleDialogueCatalog.TryGetBuiltInPoseLine(playbackEvent.PoseId, out var line))
            {
                return;
            }

            EnsureCanvasExists();
            speechBubblePresenter.Show(uiCanvas!, runtimeController, line);
        }

        private void HandleContextMenuInput()
        {
            if (runtimeController == null)
            {
                return;
            }

            if (settingsWindowPresenter != null && settingsWindowPresenter.IsVisible)
            {
                return;
            }

            if (chatOverlayPresenter != null && chatOverlayPresenter.BlocksBackgroundInteraction)
            {
                return;
            }

            if (fairyGuiChatPresenter != null && fairyGuiChatPresenter.BlocksBackgroundInteraction)
            {
                return;
            }

            var mousePosition = Input.mousePosition;
            if (Input.GetMouseButtonDown(RightMouseButton))
            {
                if (ShouldOpenContextMenu(mousePosition))
                {
                    runtimeController.RequestApplicationFocus();
                    OpenContextMenu(mousePosition);
                }
                else if (!IsPointInsideOpenMenus(mousePosition))
                {
                    CloseContextMenus();
                }

                return;
            }

            if ((contextMenuUi == null || !contextMenuUi.Root.gameObject.activeSelf)
                && (submenuUi == null || !submenuUi.Root.gameObject.activeSelf))
            {
                return;
            }

            UpdateContextMenuLeaveTimer(mousePosition);
            UpdateSubmenuCloseTimer(mousePosition);

            if (Input.GetMouseButtonDown(0)
                && !IsPointInsideOpenMenus(mousePosition))
            {
                Log($"close menus by outside left click mouse={FormatVector(mousePosition)}");
                CloseContextMenus("outside-left-click");
            }
        }

        private bool ShouldOpenContextMenu(Vector2 screenMousePosition)
        {
            if (runtimeController == null || runtimeController.IsInteractionDisabled)
            {
                return false;
            }

            var currentModelRoot = runtimeController.CurrentModelRoot;
            var interactionCamera = runtimeController.InteractionCamera;
            if (currentModelRoot == null || interactionCamera == null)
            {
                return false;
            }

            if (!boundsService.TryGetScreenRect(interactionCamera, currentModelRoot, out var screenRect))
            {
                return true;
            }

            var expandedRect = ExpandRect(screenRect, ContextMenuHitPadding);
            return expandedRect.Contains(screenMousePosition);
        }

        private void OpenContextMenu(Vector2 screenPosition)
        {
            EnsureContextMenusExist();
            contextMenuUi!.SetVisible(true);
            PopulateMainContextMenu();
            CloseSubmenu("open-main-menu");

            ForceMenuLayout(contextMenuUi);
            contextMenuUi.Root.SetAsLastSibling();
            var resolvedScreenPoint = ResolveMainMenuScreenPoint(screenPosition, contextMenuUi.Root);
            PositionMenu(contextMenuUi.Root, resolvedScreenPoint);
            menuSessionId++;
            currentSubmenu = ContextMenuSubmenu.None;
            scheduledSubmenuCloseTime = float.PositiveInfinity;
            submenuCloseAllowedAtTime = float.PositiveInfinity;
            ClearSelectedUiObject();
            Log($"open main menu session={menuSessionId} click={FormatVector(screenPosition)} resolved={FormatVector(resolvedScreenPoint)} rect={FormatRect(GetMenuRectOrDefault(contextMenuUi))}");
            runtimeController?.SetContextMenuOpen(true);
            contextMenuLeaveAllowedAtTime = Time.unscaledTime + SubmenuOpenSettleSeconds;
            scheduledContextMenuLeaveCloseTime = float.PositiveInfinity;
        }

        private void PopulateMainContextMenu()
        {
            if (contextMenuUi == null || runtimeController == null)
            {
                return;
            }

            ClearChildren(contextMenuUi.ItemList);
            var chatLabel = chatOverlayPresenter != null && chatOverlayPresenter.UnreadCount > 0
                ? $"聊天 ({chatOverlayPresenter.UnreadCount})"
                : "聊天";
            var chatV2Label = fairyGuiChatPresenter != null && fairyGuiChatPresenter.UnreadCount > 0
                ? $"聊天 V2 ({fairyGuiChatPresenter.UnreadCount})"
                : "聊天 V2";
            CreateMenuButton(contextMenuUi.ItemList, chatLabel, closeMenusOnClick: true, onClick: OpenChatOverlay);
            CreateMenuButton(contextMenuUi.ItemList, chatV2Label, closeMenusOnClick: true, onClick: OpenChatOverlayV2);
            CreateMenuButton(contextMenuUi.ItemList, "角色库", closeMenusOnClick: true, onClick: OpenRoleLibrary);
            CreateMenuButton(contextMenuUi.ItemList, "添加角色", closeMenusOnClick: true, onClick: () =>
            {
                runtimeController.OpenLocalModelDialog();
            });
            CreateDisabledMenuButton(contextMenuUi.ItemList, "更换服装");
            CreateDisabledMenuButton(contextMenuUi.ItemList, "创意工坊");
            CreateMenuButton(contextMenuUi.ItemList, "设置", closeMenusOnClick: true, onClick: OpenSettingsWindow);
            var exitButton = CreateMenuButton(contextMenuUi.ItemList, "退出", closeMenusOnClick: false, onClick: () =>
            {
                runtimeController.QuitApplication();
                CloseContextMenus("quit");
            });
            LayoutRebuilder.ForceRebuildLayoutImmediate(contextMenuUi.ItemList);
            LayoutRebuilder.ForceRebuildLayoutImmediate(contextMenuUi.Root);
        }

        private void OpenSubmenu(ContextMenuSubmenu submenu, RectTransform anchor, string reason)
        {
            if (submenuUi == null || runtimeController == null)
            {
                return;
            }

            ClearChildren(submenuUi.ItemList);

            switch (submenu)
            {
                case ContextMenuSubmenu.PoseSelection:
                    foreach (var pose in runtimeController.BuiltInPoses)
                    {
                        CreateMenuButton(submenuUi.ItemList, pose.Label, closeMenusOnClick: true, onClick: () =>
                        {
                            runtimeController.ApplyBuiltInPose(pose.Id);
                        });
                    }

                    break;
                case ContextMenuSubmenu.ReplaceCharacter:
                    var cachedModels = runtimeController.CachedModels;
                    if (cachedModels.Count == 0)
                    {
                        CreateDisabledMenuButton(submenuUi.ItemList, "角色库中暂无角色");
                    }
                    else
                    {
                        var currentPath = runtimeController.CurrentModel != null
                            ? runtimeController.CurrentModel.SourcePath
                            : string.Empty;
                        foreach (var cachedModel in cachedModels)
                        {
                            var isCurrent = string.Equals(cachedModel.Path, currentPath, StringComparison.OrdinalIgnoreCase);
                            var label = isCurrent
                                ? $"当前: {cachedModel.DisplayName}"
                                : cachedModel.DisplayName;
                            CreateMenuButton(submenuUi.ItemList, label, closeMenusOnClick: true, onClick: () =>
                            {
                                runtimeController.LoadCachedModel(cachedModel.Path);
                            });
                        }
                    }

                    break;
                default:
                    CloseSubmenu("unknown-submenu");
                    return;
            }

            submenuUi.SetVisible(true);
            EnsureMenuBackgroundVisible(submenuUi);
            ForceMenuLayout(submenuUi);
            submenuUi.Root.SetAsLastSibling();
            PositionSubmenu(submenuUi, anchor);
            currentSubmenu = submenu;
            scheduledSubmenuCloseTime = float.PositiveInfinity;
            submenuCloseAllowedAtTime = Time.unscaledTime + SubmenuOpenSettleSeconds;
            ClearSelectedUiObject();
            Log($"open submenu session={menuSessionId} submenu={submenu} reason={reason} rect={FormatRect(GetMenuRectOrDefault(submenuUi))}");
            scheduledContextMenuLeaveCloseTime = float.PositiveInfinity;
        }

        private void CreateSubmenuButton(RectTransform parent, string label, ContextMenuSubmenu submenu)
        {
            var row = CreateMenuRow(parent, $"{label} >");
            SetMenuRowColor(row.Background, MenuButtonColor);
            AddInteractiveRowState(row.Root.gameObject, row.Background);
            AddPointerEnterAction(row.Root.gameObject, () =>
            {
                Log($"hover submenu trigger session={menuSessionId} label={label} mouse={FormatVector(Input.mousePosition)}");
                OpenSubmenu(submenu, row.Root, $"hover:{label}");
            });
            AddPointerClickAction(row.Root.gameObject, () =>
            {
                Log($"click submenu trigger session={menuSessionId} label={label} mouse={FormatVector(Input.mousePosition)}");
                OpenSubmenu(submenu, row.Root, $"click:{label}");
            });
        }

        private void CreateDisabledMenuButton(RectTransform parent, string label)
        {
            var row = CreateMenuRow(parent, label);
            SetMenuRowColor(row.Background, MenuButtonDisabledColor);
        }

        private Button CreateMenuButton(RectTransform parent, string label, bool closeMenusOnClick, Action? onClick)
        {
            var row = CreateMenuRow(parent, label);
            SetMenuRowColor(row.Background, MenuButtonColor);
            AddInteractiveRowState(row.Root.gameObject, row.Background);

            var button = row.Root.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                Log($"click menu item session={menuSessionId} label={label} submenu={currentSubmenu}");
                ClearSelectedUiObject();
            });

            if (onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }

            if (closeMenusOnClick)
            {
                button.onClick.AddListener(() => CloseContextMenus($"click:{label}"));
            }

            return button;
        }

        private MenuRow CreateMenuRow(RectTransform parent, string label)
        {
            var rowObject = new GameObject(
                label,
                typeof(RectTransform),
                typeof(Image),
                typeof(LayoutElement));
            var rectTransform = rowObject.GetComponent<RectTransform>();
            rectTransform.SetParent(parent, false);
            rectTransform.anchorMin = new Vector2(0f, 1f);
            rectTransform.anchorMax = new Vector2(1f, 1f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.sizeDelta = new Vector2(0f, 52f);

            var background = rowObject.GetComponent<Image>();
            background.raycastTarget = true;

            var layoutElement = rowObject.GetComponent<LayoutElement>();
            layoutElement.minHeight = 52f;
            layoutElement.preferredHeight = 52f;
            layoutElement.flexibleWidth = 1f;

            var textObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(rowObject.transform, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(18f, 6f);
            textRect.offsetMax = new Vector2(-18f, -6f);

            var text = textObject.GetComponent<Text>();
            text.text = label;
            text.font = GetMenuFont();
            text.fontSize = 18;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.raycastTarget = false;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            TryAttachScrollRelay(parent, rowObject);
            return new MenuRow(rectTransform, background);
        }

        private void EnsureContextMenusExist()
        {
            EnsureCanvasExists();

            var canvasTransform = uiCanvas!.transform;
            contextMenuUi ??= CreateMainMenuUi(canvasTransform, "VividSoulContextMenu");
            submenuUi ??= CreateMainMenuUi(canvasTransform, "VividSoulContextSubmenu", enableVerticalScroll: true);
            submenuUi.SetVisible(false);
        }

        private MenuUi CreateMainMenuUi(Transform parent, string objectName, bool enableVerticalScroll = false)
        {
            var menuObject = InstantiateRequiredPrefab(ContextMenuPrefabPath, parent);
            menuObject.name = objectName;
            menuObject.SetActive(false);

            var root = menuObject.GetComponent<RectTransform>();
            if (root == null)
            {
                throw new InvalidOperationException("Modern UI Pack context menu prefab is missing a RectTransform.");
            }

            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.zero;
            root.pivot = new Vector2(0f, 1f);

            var contentRoot = FindRequiredRectTransform(root, "Content");
            var itemList = FindRequiredRectTransform(contentRoot, "Item List");
            var canvasGroup = menuObject.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                throw new InvalidOperationException("Modern UI Pack context menu prefab is missing a CanvasGroup.");
            }

            RemoveRuntimeTrigger(root);
            DisableUnsupportedBehaviours(menuObject);
            var animator = menuObject.GetComponent<Animator>();
            if (animator != null)
            {
                animator.enabled = false;
            }

            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
            var menuUi = new MenuUi(root, contentRoot, itemList, canvasGroup);
            if (enableVerticalScroll)
            {
                ConfigureScrollableMenu(menuObject, menuUi);
            }

            EnsureMenuBackgroundVisible(menuUi);
            return menuUi;
        }

        private void EnsureCanvasExists()
        {
            if (uiCanvas != null)
            {
                return;
            }

            EnsureEventSystemExists();

            var canvasObject = new GameObject(
                "VividSoulContextCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            uiCanvas = canvasObject.GetComponent<Canvas>();
            uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            uiCanvas.sortingOrder = 5000;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        private void EnsureStatusMessageExists()
        {
            EnsureCanvasExists();
            if (statusMessageRoot != null)
            {
                return;
            }

            var panelObject = new GameObject(
                "VividSoulStatusMessage",
                typeof(RectTransform),
                typeof(Image),
                typeof(CanvasGroup));
            statusMessageRoot = panelObject.GetComponent<RectTransform>();
            statusMessageRoot.SetParent(uiCanvas!.transform, false);
            statusMessageRoot.anchorMin = new Vector2(0.5f, 0f);
            statusMessageRoot.anchorMax = new Vector2(0.5f, 0f);
            statusMessageRoot.pivot = new Vector2(0.5f, 0f);
            statusMessageRoot.anchoredPosition = new Vector2(0f, StatusMessageBottomOffset);
            statusMessageRoot.sizeDelta = new Vector2(520f, 64f);

            var background = panelObject.GetComponent<Image>();
            background.color = StatusMessageBackgroundColor;
            background.raycastTarget = false;

            statusMessageCanvasGroup = panelObject.GetComponent<CanvasGroup>();
            statusMessageCanvasGroup.alpha = 0f;
            statusMessageCanvasGroup.interactable = false;
            statusMessageCanvasGroup.blocksRaycasts = false;

            var textObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(panelObject.transform, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(18f, 10f);
            textRect.offsetMax = new Vector2(-18f, -10f);

            statusMessageText = textObject.GetComponent<Text>();
            statusMessageText.font = GetMenuFont();
            statusMessageText.fontSize = 18;
            statusMessageText.alignment = TextAnchor.MiddleCenter;
            statusMessageText.color = Color.white;
            statusMessageText.raycastTarget = false;
            statusMessageText.supportRichText = false;
            statusMessageText.horizontalOverflow = HorizontalWrapMode.Wrap;
            statusMessageText.verticalOverflow = VerticalWrapMode.Truncate;
            statusMessageRoot.gameObject.SetActive(false);
        }

        private void ShowStatusMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            EnsureStatusMessageExists();
            statusMessageText!.text = message;
            statusMessageRoot!.gameObject.SetActive(true);
            statusMessageRoot.SetAsLastSibling();
            statusMessageCanvasGroup!.alpha = 1f;
            statusMessageHideAtTime = Time.unscaledTime + StatusMessageDurationSeconds;
        }

        private void OpenSettingsWindow()
        {
            if (settingsWindowPresenter == null || runtimeController == null)
            {
                return;
            }

            EnsureCanvasExists();
            runtimeController.RequestApplicationFocus();
            chatOverlayPresenter?.Collapse();
            fairyGuiChatPresenter?.Hide();
            settingsWindowPresenter.ShowLlm(uiCanvas!);
        }

        private void OpenRoleLibrary()
        {
            if (settingsWindowPresenter == null || runtimeController == null)
            {
                return;
            }

            EnsureCanvasExists();
            runtimeController.RequestApplicationFocus();
            chatOverlayPresenter?.Collapse();
            fairyGuiChatPresenter?.Hide();
            settingsWindowPresenter.ShowGeneral(uiCanvas!);
        }

        private void OpenChatOverlay()
        {
            if (chatOverlayPresenter == null || runtimeController == null)
            {
                return;
            }

            EnsureCanvasExists();
            runtimeController.RequestApplicationFocus();
            settingsWindowPresenter?.Hide();
            fairyGuiChatPresenter?.Hide();
            chatOverlayPresenter.Show(uiCanvas!);
            runtimeController.MarkConversationMessagesRead();
            _ = runtimeController.RefreshConversationBackendAsync();
        }

        private void OpenChatOverlayV2()
        {
            if (fairyGuiChatPresenter == null || runtimeController == null)
            {
                return;
            }

            runtimeController.RequestApplicationFocus();
            settingsWindowPresenter?.Hide();
            chatOverlayPresenter?.Collapse();
            fairyGuiChatPresenter.Show();
            runtimeController.MarkConversationMessagesRead();
            _ = runtimeController.RefreshConversationBackendAsync();
        }

        private async void HandleChatMessageSubmitted(string message)
        {
            if (runtimeController == null || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (isChatRequestPending)
            {
                AppendSystemMessageToChatPresenters("上一条消息还在处理中，请等当前回复完成。");
                return;
            }

            CancelChatRequest();
            chatRequestCancellationTokenSource = new CancellationTokenSource();
            isChatRequestPending = true;
            SetChatRequestInFlight(true);
            runtimeController.NotifyConversationActivity();
            try
            {
                await runtimeController.SendChatMessageAsync(message, chatRequestCancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                AppendSystemMessageToChatPresenters("当前请求已取消。");
            }
            catch (UserFacingException exception)
            {
                runtimeController.NotifyConversationActivity(8f);
                AppendSystemMessageToChatPresenters(exception.Message);
                ShowStatusMessage(exception.Message);
            }
            catch (Exception exception)
            {
                runtimeController.NotifyConversationActivity(8f);
                AppendSystemMessageToChatPresenters($"LLM 请求失败：{exception.Message}");
                ShowStatusMessage($"LLM 请求失败：{exception.Message}");
            }
            finally
            {
                chatRequestCancellationTokenSource?.Dispose();
                chatRequestCancellationTokenSource = null;
                isChatRequestPending = false;
                SetChatRequestInFlight(false);
            }
        }

        private void CancelChatRequest()
        {
            if (chatRequestCancellationTokenSource == null)
            {
                return;
            }

            chatRequestCancellationTokenSource.Cancel();
            chatRequestCancellationTokenSource.Dispose();
            chatRequestCancellationTokenSource = null;
            isChatRequestPending = false;
            SetChatRequestInFlight(false);
        }

        private void HandleConversationMessageReceived(ConversationMessageEnvelope envelope)
        {
            pendingConversationUiActions.Enqueue(() =>
            {
                if (chatOverlayPresenter == null || runtimeController == null)
                {
                    return;
                }

                switch (envelope.Message.Role)
                {
                    case ChatRole.User:
                        chatOverlayPresenter.AppendUserMessage(envelope.Message.Text);
                        fairyGuiChatPresenter?.AppendUserMessage(envelope.Message.Text);
                        break;
                    case ChatRole.Assistant:
                        chatOverlayPresenter.AppendMateMessage(envelope.Message.Text);
                        fairyGuiChatPresenter?.AppendMateMessage(envelope.Message.Text);
                        if (envelope.IsProactive)
                        {
                            ShowStatusMessage(envelope.Message.Text);
                        }

                        runtimeController.NotifyConversationActivity();
                        actionDispatcher?.Dispatch(envelope.ActionRequest);
                        if (envelope.ShouldDisplayBubble)
                        {
                            EnsureCanvasExists();
                            speechBubblePresenter?.Show(uiCanvas!, runtimeController, envelope.Message.Text);
                        }

                        if (chatOverlayPresenter.IsExpanded)
                        {
                            runtimeController.MarkConversationMessagesRead();
                        }

                        if (fairyGuiChatPresenter?.IsVisible == true)
                        {
                            runtimeController.MarkConversationMessagesRead();
                        }

                        break;
                    case ChatRole.System:
                        chatOverlayPresenter.AppendSystemMessage(envelope.Message.Text);
                        fairyGuiChatPresenter?.AppendSystemMessage(envelope.Message.Text);
                        if (envelope.IsProactive)
                        {
                            ShowStatusMessage(envelope.Message.Text);
                        }

                        break;
                }
            });
        }

        private void HandleConversationStatusChanged(ConversationStatusSnapshot status)
        {
            pendingConversationUiActions.Enqueue(() =>
            {
                chatOverlayPresenter?.SetRequestInFlight(status.IsRequestInFlight);
                chatOverlayPresenter?.SetConversationStatus(status);
                fairyGuiChatPresenter?.SetRequestInFlight(status.IsRequestInFlight);
                fairyGuiChatPresenter?.SetConversationStatus(status);
            });
        }

        private void DrainPendingConversationUiActions()
        {
            while (pendingConversationUiActions.TryDequeue(out var action))
            {
                action();
            }
        }

        private void HideStatusMessage()
        {
            statusMessageHideAtTime = float.PositiveInfinity;
            if (statusMessageRoot == null || statusMessageCanvasGroup == null)
            {
                return;
            }

            statusMessageCanvasGroup.alpha = 0f;
            statusMessageRoot.gameObject.SetActive(false);
        }

        private void UpdateStatusMessageVisibility()
        {
            if (statusMessageRoot == null || !statusMessageRoot.gameObject.activeSelf)
            {
                return;
            }

            if (Time.unscaledTime >= statusMessageHideAtTime)
            {
                HideStatusMessage();
            }
        }

        private bool ShouldUseExpandedWindowMode()
        {
            return (settingsWindowPresenter != null && settingsWindowPresenter.IsVisible)
                   || (chatOverlayPresenter != null && chatOverlayPresenter.IsExpanded)
                   || (fairyGuiChatPresenter?.IsVisible == true);
        }

        private void AppendSystemMessageToChatPresenters(string message)
        {
            chatOverlayPresenter?.AppendSystemMessage(message);
            fairyGuiChatPresenter?.AppendSystemMessage(message);
        }

        private void SetChatRequestInFlight(bool value)
        {
            chatOverlayPresenter?.SetRequestInFlight(value);
            fairyGuiChatPresenter?.SetRequestInFlight(value);
        }

        private void EnsureEventSystemExists()
        {
            if (EventSystem.current != null)
            {
                ownsEventSystem = false;
                return;
            }

            var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            eventSystemObject.transform.SetAsLastSibling();
            ownsEventSystem = true;
        }

        private void CloseContextMenus(string reason = "manual")
        {
            CloseSubmenu($"close-all:{reason}");

            if (contextMenuUi != null)
            {
                contextMenuUi.SetVisible(false);
            }

            Log($"close main menu session={menuSessionId} reason={reason}");
            ClearSelectedUiObject();
            runtimeController?.SetContextMenuOpen(false);
            contextMenuLeaveAllowedAtTime = float.PositiveInfinity;
            scheduledContextMenuLeaveCloseTime = float.PositiveInfinity;
        }

        private void CloseSubmenu(string reason = "manual")
        {
            if (submenuUi != null)
            {
                submenuUi.SetVisible(false);
            }

            if (currentSubmenu != ContextMenuSubmenu.None)
            {
                Log($"close submenu session={menuSessionId} submenu={currentSubmenu} reason={reason}");
            }

            currentSubmenu = ContextMenuSubmenu.None;
            scheduledSubmenuCloseTime = float.PositiveInfinity;
            submenuCloseAllowedAtTime = float.PositiveInfinity;
            ClearSelectedUiObject();
        }

        private bool AreContextMenusVisible()
        {
            return (contextMenuUi != null && contextMenuUi.Root.gameObject.activeSelf)
                   || (submenuUi != null && submenuUi.Root.gameObject.activeSelf);
        }

        private void UpdateContextMenuLeaveTimer(Vector2 mousePosition)
        {
            if (!AreContextMenusVisible())
            {
                return;
            }

            if (Time.unscaledTime < contextMenuLeaveAllowedAtTime)
            {
                return;
            }

            if (!TryGetStayOpenScreenRect(out var stayRegion))
            {
                return;
            }

            if (stayRegion.Contains(mousePosition))
            {
                if (!float.IsPositiveInfinity(scheduledContextMenuLeaveCloseTime))
                {
                    Log($"cancel context menu leave-close session={menuSessionId} mouse={FormatVector(mousePosition)}");
                }

                scheduledContextMenuLeaveCloseTime = float.PositiveInfinity;
                return;
            }

            if (float.IsPositiveInfinity(scheduledContextMenuLeaveCloseTime))
            {
                scheduledContextMenuLeaveCloseTime = Time.unscaledTime + ContextMenuLeaveCloseDelaySeconds;
                Log($"schedule context menu leave-close session={menuSessionId} mouse={FormatVector(mousePosition)} stayRect={FormatRect(stayRegion)}");
                return;
            }

            if (Time.unscaledTime >= scheduledContextMenuLeaveCloseTime)
            {
                CloseContextMenus("pointer-left-stay-region");
            }
        }

        private bool TryGetStayOpenScreenRect(out Rect union)
        {
            union = default;
            var has = false;

            if (runtimeController != null)
            {
                var modelRoot = runtimeController.CurrentModelRoot;
                var cam = runtimeController.InteractionCamera;
                if (modelRoot != null
                    && cam != null
                    && boundsService.TryGetScreenRect(cam, modelRoot, out var modelRect))
                {
                    var r = ExpandRect(modelRect, ContextMenuHitPadding);
                    union = has ? UnionRects(union, r) : r;
                    has = true;
                }
            }

            if (contextMenuUi != null && contextMenuUi.Root.gameObject.activeSelf)
            {
                var r = ExpandRect(GetMenuInteractiveRect(contextMenuUi, 0f), MenuStayOpenClusterPadding);
                union = has ? UnionRects(union, r) : r;
                has = true;
            }

            if (submenuUi != null && submenuUi.Root.gameObject.activeSelf)
            {
                var r = ExpandRect(GetMenuInteractiveRect(submenuUi, 0f), MenuStayOpenClusterPadding);
                union = has ? UnionRects(union, r) : r;
                has = true;
            }

            return has;
        }

        private static Rect UnionRects(Rect a, Rect b)
        {
            return Rect.MinMaxRect(
                Mathf.Min(a.xMin, b.xMin),
                Mathf.Min(a.yMin, b.yMin),
                Mathf.Max(a.xMax, b.xMax),
                Mathf.Max(a.yMax, b.yMax));
        }

        private bool IsPointInsideOpenMenus(Vector2 screenPoint)
        {
            return IsPointInsideMenu(contextMenuUi, screenPoint)
                   || IsPointInsideMenu(submenuUi, screenPoint);
        }

        private static bool IsPointInsideMenu(MenuUi? menu, Vector2 screenPoint)
        {
            return IsPointInsideMenu(menu, screenPoint, 0f);
        }

        private static bool IsPointInsideMenu(MenuUi? menu, Vector2 screenPoint, float padding)
        {
            if (menu == null || menu.Root == null || !menu.Root.gameObject.activeInHierarchy)
            {
                return false;
            }

            var rect = GetMenuInteractiveRect(menu, padding);
            return rect.Contains(screenPoint);
        }

        private static GameObject InstantiateRequiredPrefab(string resourcePath, Transform parent)
        {
            var prefab = Resources.Load<GameObject>(resourcePath)
                         ?? throw new InvalidOperationException($"Required UI resource was not found at '{resourcePath}'.");
            return Instantiate(prefab, parent, false);
        }

        private static RectTransform FindRequiredRectTransform(Transform parent, string childName)
        {
            var child = parent.Find(childName);
            if (child == null)
            {
                throw new InvalidOperationException($"Required child '{childName}' was not found under '{parent.name}'.");
            }

            var rectTransform = child as RectTransform;
            if (rectTransform == null)
            {
                throw new InvalidOperationException($"Child '{childName}' under '{parent.name}' is missing a RectTransform.");
            }

            return rectTransform;
        }

        private static void ClearChildren(RectTransform parent)
        {
            while (parent.childCount > 0)
            {
                var child = parent.GetChild(0);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
        }

        private Font GetMenuFont()
        {
            return RuntimeUiFontResolver.GetFont();
        }

        private void UpdateSubmenuCloseTimer(Vector2 mousePosition)
        {
            if (submenuUi == null || !submenuUi.Root.gameObject.activeSelf)
            {
                return;
            }

            if (Time.unscaledTime < submenuCloseAllowedAtTime)
            {
                return;
            }

            var isInsideMainMenu = IsPointInsideMenu(contextMenuUi, mousePosition, SubmenuCloseHitPadding);
            var isInsideSubmenu = IsPointInsideMenu(submenuUi, mousePosition, SubmenuCloseHitPadding);
            if (isInsideMainMenu || isInsideSubmenu)
            {
                if (!float.IsPositiveInfinity(scheduledSubmenuCloseTime))
                {
                    Log($"cancel submenu close session={menuSessionId} mouse={FormatVector(mousePosition)}");
                }

                scheduledSubmenuCloseTime = float.PositiveInfinity;
                return;
            }

            if (float.IsPositiveInfinity(scheduledSubmenuCloseTime))
            {
                scheduledSubmenuCloseTime = Time.unscaledTime + SubmenuCloseDelaySeconds;
                Log($"schedule submenu close session={menuSessionId} mouse={FormatVector(mousePosition)} mainRect={FormatRect(GetMenuRectOrDefault(contextMenuUi))} subRect={FormatRect(GetMenuRectOrDefault(submenuUi))}");
                return;
            }

            if (Time.unscaledTime >= scheduledSubmenuCloseTime)
            {
                CloseSubmenu("pointer-left-both-menus");
            }
        }

        private static void RemoveRuntimeTrigger(RectTransform root)
        {
            var trigger = root.Find("Trigger");
            if (trigger != null)
            {
                Destroy(trigger.gameObject);
            }
        }

        private static void EnsureMenuBackgroundVisible(MenuUi menu)
        {
            var backgroundTransform = menu.Content.Find("Background");
            var background = backgroundTransform != null
                ? backgroundTransform.GetComponent<Image>()
                : menu.Root.GetComponent<Image>();
            if (background != null)
            {
                var color = background.color;
                background.color = new Color(color.r, color.g, color.b, 1f);
            }
        }

        private static void AddInteractiveRowState(GameObject target, Image background)
        {
            AddPointerEnterAction(target, () => SetMenuRowColor(background, MenuButtonHighlightColor));
            AddPointerExitAction(target, () => SetMenuRowColor(background, MenuButtonColor));
            AddPointerDownAction(target, () => SetMenuRowColor(background, MenuButtonPressedColor));
            AddPointerUpAction(target, () => SetMenuRowColor(background, MenuButtonHighlightColor));
        }

        private static void AddPointerEnterAction(GameObject target, Action action)
        {
            AddEventTriggerAction(target, EventTriggerType.PointerEnter, action);
        }

        private static void AddPointerExitAction(GameObject target, Action action)
        {
            AddEventTriggerAction(target, EventTriggerType.PointerExit, action);
        }

        private static void AddPointerDownAction(GameObject target, Action action)
        {
            AddEventTriggerAction(target, EventTriggerType.PointerDown, action);
        }

        private static void AddPointerUpAction(GameObject target, Action action)
        {
            AddEventTriggerAction(target, EventTriggerType.PointerUp, action);
        }

        private static void AddPointerClickAction(GameObject target, Action action)
        {
            AddEventTriggerAction(target, EventTriggerType.PointerClick, action);
        }

        private static void AddScrollAction(GameObject target, Action<PointerEventData> action)
        {
            AddEventTriggerAction(target, EventTriggerType.Scroll, eventData =>
            {
                if (eventData is PointerEventData pointerEventData)
                {
                    action(pointerEventData);
                }
            });
        }

        private static void AddEventTriggerAction(GameObject target, EventTriggerType eventType, Action action)
        {
            var eventTrigger = target.GetComponent<EventTrigger>() ?? target.AddComponent<EventTrigger>();
            eventTrigger.triggers ??= new List<EventTrigger.Entry>();
            var entry = new EventTrigger.Entry { eventID = eventType };
            entry.callback.AddListener(_ => action());
            eventTrigger.triggers.Add(entry);
        }

        private static void AddEventTriggerAction(GameObject target, EventTriggerType eventType, Action<BaseEventData> action)
        {
            var eventTrigger = target.GetComponent<EventTrigger>() ?? target.AddComponent<EventTrigger>();
            eventTrigger.triggers ??= new List<EventTrigger.Entry>();
            var entry = new EventTrigger.Entry { eventID = eventType };
            entry.callback.AddListener(action.Invoke);
            eventTrigger.triggers.Add(entry);
        }

        private static void SetMenuRowColor(Image background, Color color)
        {
            background.color = color;
        }

        private static void ForceMenuLayout(MenuUi menu)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(menu.ItemList);
            UpdateMenuScrollLayout(menu);
            RefreshContentSizeFitter(menu.Content);
            LayoutRebuilder.ForceRebuildLayoutImmediate(menu.Content);
            Canvas.ForceUpdateCanvases();
            SyncMenuRootSizeFromContent(menu);
            LayoutRebuilder.ForceRebuildLayoutImmediate(menu.Root);
            LayoutRebuilder.ForceRebuildLayoutImmediate(menu.Content);
            Canvas.ForceUpdateCanvases();
            SyncMenuRootSizeFromContent(menu);
        }

        private static void RefreshContentSizeFitter(RectTransform content)
        {
            var fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter != null)
            {
                fitter.enabled = false;
                fitter.enabled = true;
            }
        }

        private static void ConfigureScrollableMenu(GameObject menuObject, MenuUi menu)
        {
            var viewportObject = new GameObject(
                "Scroll Viewport",
                typeof(RectTransform),
                typeof(Image),
                typeof(RectMask2D),
                typeof(LayoutElement));
            var viewport = viewportObject.GetComponent<RectTransform>();
            var siblingIndex = menu.ItemList.GetSiblingIndex();
            viewport.SetParent(menu.Content, false);
            viewport.SetSiblingIndex(siblingIndex);
            viewport.anchorMin = new Vector2(0f, 1f);
            viewport.anchorMax = new Vector2(1f, 1f);
            viewport.pivot = new Vector2(0.5f, 1f);
            viewport.sizeDelta = Vector2.zero;

            var viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
            viewportImage.raycastTarget = true;

            var viewportLayout = viewportObject.GetComponent<LayoutElement>();
            viewportLayout.minHeight = MinimumScrollableViewportHeight;
            viewportLayout.preferredHeight = MinimumScrollableViewportHeight;
            viewportLayout.flexibleWidth = 1f;

            menu.ItemList.SetParent(viewport, false);
            menu.ItemList.anchorMin = new Vector2(0f, 1f);
            menu.ItemList.anchorMax = new Vector2(1f, 1f);
            menu.ItemList.pivot = new Vector2(0.5f, 1f);
            menu.ItemList.anchoredPosition = Vector2.zero;
            menu.ItemList.sizeDelta = new Vector2(0f, menu.ItemList.sizeDelta.y);

            var scrollbar = CreateMenuScrollbar(viewport);
            var scrollRect = menuObject.AddComponent<ScrollRect>();
            scrollRect.viewport = viewport;
            scrollRect.content = menu.ItemList;
            scrollRect.horizontal = false;
            scrollRect.vertical = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = true;
            scrollRect.scrollSensitivity = MenuScrollSensitivity;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            AddScrollAction(viewportObject, menu.HandleScroll);
            menu.AttachScroll(scrollRect, viewport, viewportLayout, scrollbar);
        }

        private static Scrollbar CreateMenuScrollbar(RectTransform parent)
        {
            var scrollbarObject = new GameObject(
                "Scrollbar",
                typeof(RectTransform),
                typeof(Image),
                typeof(Scrollbar));
            var scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
            scrollbarRect.SetParent(parent, false);
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 1f);
            scrollbarRect.offsetMin = new Vector2(-MenuScrollbarInset - MenuScrollbarWidth, MenuScrollbarInset);
            scrollbarRect.offsetMax = new Vector2(-MenuScrollbarInset, -MenuScrollbarInset);

            var trackImage = scrollbarObject.GetComponent<Image>();
            trackImage.color = MenuScrollbarTrackColor;
            trackImage.raycastTarget = true;

            var slidingAreaObject = new GameObject("Sliding Area", typeof(RectTransform));
            var slidingArea = slidingAreaObject.GetComponent<RectTransform>();
            slidingArea.SetParent(scrollbarRect, false);
            slidingArea.anchorMin = Vector2.zero;
            slidingArea.anchorMax = Vector2.one;
            slidingArea.offsetMin = new Vector2(1f, 1f);
            slidingArea.offsetMax = new Vector2(-1f, -1f);

            var handleObject = new GameObject(
                "Handle",
                typeof(RectTransform),
                typeof(Image));
            var handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.SetParent(slidingArea, false);
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;

            var handleImage = handleObject.GetComponent<Image>();
            handleImage.color = MenuScrollbarHandleColor;
            handleImage.raycastTarget = true;

            var scrollbar = scrollbarObject.GetComponent<Scrollbar>();
            scrollbar.targetGraphic = handleImage;
            scrollbar.handleRect = handleRect;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            scrollbar.transition = Selectable.Transition.ColorTint;
            scrollbar.colors = ColorBlock.defaultColorBlock;
            scrollbar.value = 1f;
            return scrollbar;
        }

        private static void UpdateMenuScrollLayout(MenuUi menu)
        {
            if (!menu.HasScrollSupport)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(menu.ItemList);
            var preferredHeight = LayoutUtility.GetPreferredHeight(menu.ItemList);
            if (float.IsNaN(preferredHeight) || preferredHeight < 1f)
            {
                preferredHeight = menu.ItemList.rect.height;
            }

            preferredHeight = Mathf.Max(1f, preferredHeight);
            var maxViewportHeight = GetScrollableViewportHeightCap();
            var viewportHeight = Mathf.Min(preferredHeight, maxViewportHeight);
            menu.SetScrollViewportHeight(viewportHeight);
            menu.ItemList.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, preferredHeight);
            menu.SetScrollEnabled(preferredHeight > maxViewportHeight + 0.5f);
            LayoutRebuilder.ForceRebuildLayoutImmediate(menu.VisibleContentRoot);
            Canvas.ForceUpdateCanvases();
        }

        private static float GetScrollableViewportHeightCap()
        {
            var screenLimitedHeight = Screen.height - (ContextMenuGap * 2f) - ScrollViewportScreenMargin;
            return Mathf.Max(
                MinimumScrollableViewportHeight,
                Mathf.Min(SubmenuMaxViewportHeight, screenLimitedHeight));
        }

        private static void SyncMenuRootSizeFromContent(MenuUi menu)
        {
            Canvas.ForceUpdateCanvases();
            var content = menu.Content;
            var root = menu.Root;
            var w = LayoutUtility.GetPreferredWidth(content);
            var h = LayoutUtility.GetPreferredHeight(content);
            if (float.IsNaN(w) || w < 1f)
            {
                w = content.rect.width;
            }

            if (float.IsNaN(h) || h < 1f)
            {
                h = content.rect.height;
            }

            w = Mathf.Max(1f, w);
            h = Mathf.Max(1f, h);

            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
        }

        private static void DisableUnsupportedBehaviours(GameObject root)
        {
            foreach (var behaviour in root.GetComponentsInChildren<Behaviour>(true))
            {
                if (!behaviour.enabled)
                {
                    continue;
                }

                var namespaceName = behaviour.GetType().Namespace ?? string.Empty;
                if (namespaceName.StartsWith("UnityEngine", StringComparison.Ordinal)
                    || namespaceName.StartsWith("UnityEngine.UI", StringComparison.Ordinal))
                {
                    continue;
                }

                behaviour.enabled = false;
            }
        }

        private static void PositionMenu(RectTransform rectTransform, Vector2 screenPoint)
        {
            Canvas.ForceUpdateCanvases();
            var offsets = GetMenuVisualOffsets(rectTransform);
            rectTransform.position = new Vector2(
                screenPoint.x + offsets.Left,
                screenPoint.y - offsets.Top);
            LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
            Canvas.ForceUpdateCanvases();
            ClampToScreen(rectTransform);
        }

        private static void PositionSubmenu(MenuUi submenu, RectTransform anchorRow)
        {
            var submenuRoot = submenu.Root;
            var panel = submenu.Content;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(submenuRoot);
            Canvas.ForceUpdateCanvases();

            var anchorCorners = new Vector3[4];
            anchorRow.GetWorldCorners(anchorCorners);
            var subCorners = new Vector3[4];
            panel.GetWorldCorners(subCorners);

            var anchorLeft = anchorCorners[0].x;
            var anchorRight = anchorCorners[2].x;
            var anchorTop = anchorCorners[1].y;

            var subLeft = subCorners[0].x;
            var subRight = subCorners[2].x;
            var subTop = subCorners[1].y;
            var subW = subRight - subLeft;

            var spaceRight = Screen.width - ContextMenuGap - anchorRight;
            var spaceLeft = anchorLeft - ContextMenuGap;
            var openToRight = spaceRight >= subW || spaceRight >= spaceLeft;

            if (openToRight)
            {
                var dx = anchorRight + ContextMenuGap - subLeft;
                var dy = anchorTop - subTop;
                submenuRoot.position += new Vector3(dx, dy, 0f);
            }
            else
            {
                var dx = anchorLeft - ContextMenuGap - subW - subLeft;
                var dy = anchorTop - subTop;
                submenuRoot.position += new Vector3(dx, dy, 0f);
            }

            Canvas.ForceUpdateCanvases();
            ClampToScreen(submenuRoot, panel);
        }

        private static void ClampToScreen(RectTransform root, RectTransform? measureVisual = null)
        {
            var target = measureVisual != null ? measureVisual : root;
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);

            var offset = Vector3.zero;
            if (corners[0].x < ContextMenuGap)
            {
                offset.x = ContextMenuGap - corners[0].x;
            }
            else if (corners[2].x > Screen.width - ContextMenuGap)
            {
                offset.x = (Screen.width - ContextMenuGap) - corners[2].x;
            }

            if (corners[0].y < ContextMenuGap)
            {
                offset.y = ContextMenuGap - corners[0].y;
            }
            else if (corners[1].y > Screen.height - ContextMenuGap)
            {
                offset.y = (Screen.height - ContextMenuGap) - corners[1].y;
            }

            root.position += offset;
        }

        private static void ClearSelectedUiObject()
        {
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }

        private void Log(string message)
        {
            Debug.Log($"[DesktopPetRuntimeHud] {message}");
        }

        private static Rect GetMenuRectOrDefault(MenuUi? menu)
        {
            return menu != null && menu.Root != null && menu.Root.gameObject.activeInHierarchy
                ? GetMenuInteractiveRect(menu, 0f)
                : default;
        }

        private static Rect GetMenuInteractiveRect(MenuUi menu, float padding)
        {
            var rootRect = GetScreenRect(menu.Root);
            var itemListRect = GetScreenRect(menu.VisibleContentRoot);
            var unionRect = Rect.MinMaxRect(
                Mathf.Min(rootRect.xMin, itemListRect.xMin) - padding,
                Mathf.Min(rootRect.yMin, itemListRect.yMin) - padding,
                Mathf.Max(rootRect.xMax, itemListRect.xMax) + padding,
                Mathf.Max(rootRect.yMax, itemListRect.yMax) + padding);
            return unionRect;
        }

        private static Rect GetScreenRect(RectTransform rectTransform)
        {
            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private void TryAttachScrollRelay(RectTransform parent, GameObject rowObject)
        {
            var menu = ResolveMenuUiByItemList(parent);
            if (menu == null || !menu.HasScrollSupport)
            {
                return;
            }

            AddScrollAction(rowObject, menu.HandleScroll);
        }

        private MenuUi? ResolveMenuUiByItemList(RectTransform itemList)
        {
            if (submenuUi != null && submenuUi.ItemList == itemList)
            {
                return submenuUi;
            }

            if (contextMenuUi != null && contextMenuUi.ItemList == itemList)
            {
                return contextMenuUi;
            }

            return null;
        }

        private static string FormatRect(Rect rect)
        {
            return $"({rect.xMin:0.0},{rect.yMin:0.0})-({rect.xMax:0.0},{rect.yMax:0.0})";
        }

        private static string FormatVector(Vector2 vector)
        {
            return $"({vector.x:0.0},{vector.y:0.0})";
        }

        private static MenuVisualOffsets GetMenuVisualOffsets(RectTransform rectTransform)
        {
            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            return new MenuVisualOffsets(
                rectTransform.position.x - corners[0].x,
                corners[2].x - rectTransform.position.x,
                corners[1].y - rectTransform.position.y);
        }

        private Vector2 ResolveMainMenuScreenPoint(Vector2 clickPosition, RectTransform menuRectTransform)
        {
            if (runtimeController == null)
            {
                return clickPosition;
            }

            var currentModelRoot = runtimeController.CurrentModelRoot;
            var interactionCamera = runtimeController.InteractionCamera;
            if (currentModelRoot == null
                || interactionCamera == null
                || !boundsService.TryGetScreenRect(interactionCamera, currentModelRoot, out var modelScreenRect))
            {
                return clickPosition;
            }

            var menuSize = GetMenuVisualSize(menuRectTransform);
            var availableRightWidth = Screen.width - modelScreenRect.xMax - ContextMenuGap;
            var availableLeftWidth = modelScreenRect.xMin - ContextMenuGap;
            var shouldOpenRight = availableRightWidth >= menuSize.x || availableRightWidth >= availableLeftWidth;

            var horizontalPosition = shouldOpenRight
                ? modelScreenRect.xMax + ContextMenuGap
                : modelScreenRect.xMin - menuSize.x - ContextMenuGap;
            var verticalPosition = Mathf.Clamp(
                clickPosition.y,
                menuSize.y + ContextMenuGap,
                Screen.height - ContextMenuGap);
            return new Vector2(horizontalPosition, verticalPosition);
        }

        private static Vector2 GetMenuVisualSize(RectTransform rectTransform)
        {
            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            return new Vector2(corners[2].x - corners[0].x, corners[1].y - corners[0].y);
        }

        private static Rect ExpandRect(Rect rect, float padding)
        {
            return Rect.MinMaxRect(
                rect.xMin - padding,
                rect.yMin - padding,
                rect.xMax + padding,
                rect.yMax + padding);
        }

        private sealed class MenuUi
        {
            public MenuUi(RectTransform root, RectTransform content, RectTransform itemList, CanvasGroup canvasGroup)
            {
                Root = root;
                Content = content;
                ItemList = itemList;
                CanvasGroup = canvasGroup;
            }

            public RectTransform Root { get; }

            public RectTransform Content { get; }

            public RectTransform ItemList { get; }

            public RectTransform VisibleContentRoot => ScrollViewport != null ? ScrollViewport : ItemList;

            public bool HasScrollSupport => ScrollRect != null && ScrollViewport != null && ScrollViewportLayout != null;

            private CanvasGroup CanvasGroup { get; }

            private ScrollRect? ScrollRect { get; set; }

            private RectTransform? ScrollViewport { get; set; }

            private LayoutElement? ScrollViewportLayout { get; set; }

            private Scrollbar? Scrollbar { get; set; }

            public void SetVisible(bool isVisible)
            {
                if (Root == null || CanvasGroup == null)
                {
                    return;
                }

                Root.gameObject.SetActive(isVisible);
                CanvasGroup.alpha = isVisible ? 1f : 0f;
                CanvasGroup.interactable = isVisible;
                CanvasGroup.blocksRaycasts = isVisible;
            }

            public void AttachScroll(ScrollRect scrollRect, RectTransform scrollViewport, LayoutElement scrollViewportLayout, Scrollbar scrollbar)
            {
                ScrollRect = scrollRect;
                ScrollViewport = scrollViewport;
                ScrollViewportLayout = scrollViewportLayout;
                Scrollbar = scrollbar;
            }

            public void SetScrollViewportHeight(float height)
            {
                if (ScrollViewport == null || ScrollViewportLayout == null)
                {
                    return;
                }

                var clampedHeight = Mathf.Max(1f, height);
                ScrollViewportLayout.minHeight = clampedHeight;
                ScrollViewportLayout.preferredHeight = clampedHeight;
                ScrollViewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, clampedHeight);
            }

            public void SetScrollEnabled(bool isEnabled)
            {
                if (ScrollRect == null)
                {
                    return;
                }

                ScrollRect.enabled = isEnabled;
                ScrollRect.vertical = isEnabled;
                ScrollRect.verticalNormalizedPosition = 1f;
                if (Scrollbar != null)
                {
                    Scrollbar.gameObject.SetActive(isEnabled);
                }

                if (!isEnabled)
                {
                    ItemList.anchoredPosition = Vector2.zero;
                }
            }

            public void HandleScroll(PointerEventData eventData)
            {
                if (ScrollRect == null || !ScrollRect.enabled || !ScrollRect.vertical)
                {
                    return;
                }

                ScrollRect.OnScroll(eventData);
            }
        }

        private sealed class MenuRow
        {
            public MenuRow(RectTransform root, Image background)
            {
                Root = root;
                Background = background;
            }

            public RectTransform Root { get; }

            public Image Background { get; }
        }

        private readonly struct MenuVisualOffsets
        {
            public MenuVisualOffsets(float left, float right, float top)
            {
                Left = left;
                Right = right;
                Top = top;
            }

            public float Left { get; }

            public float Right { get; }

            public float Top { get; }
        }

        private enum ContextMenuSubmenu
        {
            None = 0,
            PoseSelection = 1,
            ReplaceCharacter = 2,
        }
    }
}
