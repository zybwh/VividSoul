#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
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
        private const float StatusMessageDurationSeconds = 3f;
        private const float StatusMessageBottomOffset = 72f;
        private static readonly Color MenuButtonColor = new(0.10f, 0.18f, 0.26f, 1f);
        private static readonly Color MenuButtonHighlightColor = new(0.21f, 0.33f, 0.47f, 1f);
        private static readonly Color MenuButtonPressedColor = new(0.29f, 0.42f, 0.58f, 1f);
        private static readonly Color MenuButtonDisabledColor = new(0.18f, 0.24f, 0.31f, 1f);
        private static readonly Color StatusMessageBackgroundColor = new(0.08f, 0.11f, 0.15f, 0.92f);
        private const int RightMouseButton = 1;

        private readonly DesktopPetBoundsService boundsService = new();

        private MenuUi? contextMenuUi;
        private ContextMenuSubmenu currentSubmenu = ContextMenuSubmenu.None;
        private Font? menuFont;
        private int menuSessionId;
        private bool ownsEventSystem;
        private DesktopPetRuntimeController? runtimeController;
        private DesktopPetSpeechBubblePresenter? speechBubblePresenter;
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

        private void Awake()
        {
            runtimeController = GetComponent<DesktopPetRuntimeController>();
            speechBubblePresenter = new DesktopPetSpeechBubblePresenter(boundsService);
        }

        private void OnEnable()
        {
            if (runtimeController != null)
            {
                runtimeController.ModelLoadFailed += HandleRuntimeFailure;
                runtimeController.BuiltInPoseTriggered += HandleBuiltInPoseTriggered;
            }
        }

        private void Update()
        {
            HandleContextMenuInput();
            UpdateStatusMessageVisibility();
            speechBubblePresenter?.Update(Time.unscaledDeltaTime);

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
            }

            CloseContextMenus();
            HideStatusMessage();
            speechBubblePresenter?.HideImmediate();
        }

        private void OnDestroy()
        {
            if (runtimeController != null)
            {
                runtimeController.ModelLoadFailed -= HandleRuntimeFailure;
                runtimeController.BuiltInPoseTriggered -= HandleBuiltInPoseTriggered;
            }

            CloseContextMenus();
            speechBubblePresenter?.HideImmediate();

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

        private void HandleBuiltInPoseTriggered(string poseId)
        {
            if (runtimeController == null
                || speechBubblePresenter == null
                || !SpeechBubbleDialogueCatalog.TryGetBuiltInPoseLine(poseId, out var line))
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
            CreateMenuButton(contextMenuUi.ItemList, "添加角色", closeMenusOnClick: true, onClick: () =>
            {
                runtimeController.OpenLocalModelDialog();
            });
            CreateSubmenuButton(contextMenuUi.ItemList, "角色库", ContextMenuSubmenu.ReplaceCharacter);
            CreateSubmenuButton(contextMenuUi.ItemList, "姿势选择", ContextMenuSubmenu.PoseSelection);
            CreateDisabledMenuButton(contextMenuUi.ItemList, "更换服装");
            CreateDisabledMenuButton(contextMenuUi.ItemList, "创意工坊");
            CreateDisabledMenuButton(contextMenuUi.ItemList, "设置");
            CreateMenuButton(contextMenuUi.ItemList, "随机走动", closeMenusOnClick: true, onClick: () =>
            {
                runtimeController.MoveToSampledDesktopLocation();
            });
            CreateMenuButton(contextMenuUi.ItemList, "应用示例移动行为", closeMenusOnClick: true, onClick: () =>
            {
                runtimeController.ApplyExampleDesktopMoveBehavior();
            });
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
            return new MenuRow(rectTransform, background);
        }

        private void EnsureContextMenusExist()
        {
            EnsureCanvasExists();

            var canvasTransform = uiCanvas!.transform;
            contextMenuUi ??= CreateMainMenuUi(canvasTransform, "VividSoulContextMenu");
            submenuUi ??= CreateMainMenuUi(canvasTransform, "VividSoulContextSubmenu");
            submenuUi.SetVisible(false);
        }

        private MenuUi CreateMainMenuUi(Transform parent, string objectName)
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
            menuFont ??= Resources.GetBuiltinResource<Font>("Arial.ttf");
            return menuFont;
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

        private static void AddEventTriggerAction(GameObject target, EventTriggerType eventType, Action action)
        {
            var eventTrigger = target.GetComponent<EventTrigger>() ?? target.AddComponent<EventTrigger>();
            eventTrigger.triggers ??= new List<EventTrigger.Entry>();
            var entry = new EventTrigger.Entry { eventID = eventType };
            entry.callback.AddListener(_ => action());
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
            var itemListRect = GetScreenRect(menu.ItemList);
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

            private CanvasGroup CanvasGroup { get; }

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
