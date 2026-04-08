#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VividSoul.Runtime.AI;
using VividSoul.Runtime;
using VividSoul.Runtime.Avatar;
using VividSoul.Runtime.Animation;
using VividSoul.Runtime.Behavior;
using VividSoul.Runtime.Content;
using VividSoul.Runtime.Interaction;
using VividSoul.Runtime.Movement;
using VividSoul.Runtime.Platform;
using VividSoul.Runtime.Settings;
using VividSoul.Runtime.Workshop;

namespace VividSoul.Runtime.App
{
    [RequireComponent(typeof(DesktopPetAnimationController))]
    [RequireComponent(typeof(DesktopPetFallbackMotionController))]
    [RequireComponent(typeof(DesktopPetMovementController))]
    [RequireComponent(typeof(DesktopPetDragController))]
    [RequireComponent(typeof(DesktopPetScaleController))]
    [RequireComponent(typeof(DesktopPetRotationController))]
    [RequireComponent(typeof(DesktopPetClickInteractionController))]
    [RequireComponent(typeof(DesktopPetRuntimeHud))]
    public sealed class DesktopPetRuntimeController : MonoBehaviour
    {
        private const uint DefaultSteamAppId = 3625270;
        private const float DefaultFacingRotationY = 180f;
        private const bool AllowClickThrough = false;
        private const bool ForceTopMost = true;
        private const string DefaultBuiltInPoseId = "vrma_01";
        private const string StartupAnimationDirectoryEnvironmentVariable = "VIVIDSOUL_LOCAL_VRMA_DIR";
        private const string StartupAnimationFileEnvironmentVariable = "VIVIDSOUL_LOCAL_VRMA_PATH";
        private const float ConversationAmbientPoseSuppressionSeconds = 18f;
        private const float CompactWindowHorizontalPadding = 56f;
        private const float CompactWindowTopPadding = 72f;
        private const float CompactWindowBottomPadding = 44f;
        private const float CompactWindowMinimumWidth = 280f;
        private const float CompactWindowMinimumHeight = 320f;
        private const float CompactWindowRectChangeThreshold = 2f;
        private const int IdleTargetFrameRate = 30;
        private const int ActiveTargetFrameRate = 60;

        [SerializeField] private Camera? interactionCamera;
        [SerializeField] private Transform? modelAnchor;
        [SerializeField] private bool restoreSelectedModelOnStart = true;
        [SerializeField] private uint steamAppId = DefaultSteamAppId;
        [SerializeField, TextArea] private string workshopContentSummary = string.Empty;

        private CharacterRuntimeAssembler? characterRuntimeAssembler;
        private AnimationPackageInstaller? animationPackageInstaller;
        private AnimationLibraryPaths? animationLibraryPaths;
        private AnimationImportService? animationImportService;
        private BehaviorPackageInstaller? behaviorPackageInstaller;
        private DesktopPetBoundsService? boundsService;
        private FileSystemContentCatalog? contentCatalog;
        private ModelFingerprintService? modelFingerprintService;
        private ModelImportService? modelImportService;
        private ModelLibraryMigrationService? modelLibraryMigrationService;
        private ModelLibraryPaths? modelLibraryPaths;
        private IModelLoader? modelLoader;
        private IFileDialogService? fileDialogService;
        private IDesktopPetSettingsStore? settingsStore;
        private ISteamPlatformService? steamPlatformService;
        private IWindowService? windowService;
        private IWorkshopService? workshopService;
        private CachedModelStore? cachedModelStore;
        private SelectedContentStore? selectedContentStore;
        private MateConversationOrchestrator? mateConversationOrchestrator;
        private MateConversationService? mateConversationService;
        private CancellationTokenSource? loadCancellationTokenSource;
        private GameObject? currentModelRoot;
        private IReadOnlyList<WorkshopContentItem> workshopContent = Array.Empty<WorkshopContentItem>();
        private float suppressAmbientPoseUntilTime;
        private bool isClickThroughLocked;
        private bool isContextMenuOpen;
        private bool isExpandedWindowMode;
        private bool isTopMostEnabled;
        private bool compactWindowEnabled;
        private int monitorIndex;
        private int currentTargetFrameRate = ActiveTargetFrameRate;
        private Vector2 savedWindowPosition;
        private VrmImportPerformanceMode vrmImportPerformanceMode = VrmImportPerformanceMode.Balanced;

        public event Action<ModelLoadResult>? ModelLoaded;
        public event Action? ModelCleared;
        public event Action<string>? ModelLoadFailed;
        public event Action? ManagedLocalAnimationsChanged;
        public event Action<BuiltInPosePlaybackEvent>? BuiltInPoseTriggered;
        public event Action<ConversationMessageEnvelope>? ConversationMessageReceived;
        public event Action<ConversationStatusSnapshot>? ConversationStatusChanged;

        public ModelLoadResult? CurrentModel { get; private set; }

        public GameObject? CurrentModelRoot => currentModelRoot;

        public Camera? InteractionCamera => interactionCamera != null ? interactionCamera : Camera.main;

        public bool IsInteractionDisabled => AllowClickThrough && isClickThroughLocked;

        public bool IsModelInteractionBlocked => IsInteractionDisabled || isContextMenuOpen;

        public bool IsAmbientPoseRotationSuppressed => Time.unscaledTime < suppressAmbientPoseUntilTime;

        public bool CanUseClickThrough => AllowClickThrough;

        public bool IsTopMostEnabled => isTopMostEnabled;

        public bool IsTopMostForced => ForceTopMost;

        public bool UsesCompactWindow => false;

        public int MonitorIndex => monitorIndex;

        public string BuiltInDefaultPoseId => DefaultBuiltInPoseId;

        public IReadOnlyList<BuiltInPoseOption> BuiltInPoses => BuiltInPoseCatalog.All;

        public IReadOnlyList<CachedModelState> CachedModels => cachedModelStore != null
            ? cachedModelStore.Load()
            : Array.Empty<CachedModelState>();

        public IReadOnlyList<ContentItem> ManagedLocalModels => GetManagedLocalModels();

        public IReadOnlyList<ContentItem> ManagedLocalAnimations => GetManagedLocalAnimations();

        public IReadOnlyList<WorkshopContentItem> WorkshopContent => workshopContent;

        public string? LastErrorMessage { get; private set; }

        private Transform ModelAnchor => modelAnchor != null ? modelAnchor : transform;

        private void Awake()
        {
            characterRuntimeAssembler = new CharacterRuntimeAssembler();
            animationPackageInstaller = new AnimationPackageInstaller();
            animationLibraryPaths = new AnimationLibraryPaths();
            behaviorPackageInstaller = new BehaviorPackageInstaller();
            boundsService = new DesktopPetBoundsService();
            contentCatalog = new FileSystemContentCatalog();
            modelLibraryPaths = new ModelLibraryPaths();
            modelFingerprintService = new ModelFingerprintService();
            modelImportService = new ModelImportService(modelLibraryPaths, contentCatalog, modelFingerprintService);
            animationImportService = new AnimationImportService(animationLibraryPaths, contentCatalog, modelFingerprintService);
            fileDialogService = new StandaloneFileDialogService();
            settingsStore = new DesktopPetSettingsStore();
            modelLoader = new VrmModelLoaderService(characterRuntimeAssembler, settingsStore);
            modelLibraryMigrationService = new ModelLibraryMigrationService(settingsStore, modelLibraryPaths, modelImportService);
            steamPlatformService = new SteamworksNetPlatformService(steamAppId);
            windowService = new UniWindowWindowService(gameObject);
            workshopService = new SteamworksNetWorkshopService(steamPlatformService, contentCatalog);
            cachedModelStore = new CachedModelStore(settingsStore);
            selectedContentStore = new SelectedContentStore(settingsStore);
            mateConversationOrchestrator = new MateConversationOrchestrator(
                new AiSettingsStore(),
                new AiSecretsStore(),
                new ChatSessionStore(),
                new LlmUsageStatsStore(),
                modelFingerprintService);
            mateConversationService = new MateConversationService(
                new AiSettingsStore(),
                new LocalLlmConversationBackend(
                    mateConversationOrchestrator,
                    new AiSecretsStore(),
                    modelFingerprintService),
                new OpenClawConversationBackend(
                    new AiSecretsStore(),
                    new OpenClawGatewayClient(),
                    new OpenClawTranscriptMirrorStore(),
                    modelFingerprintService));
            mateConversationService.MessageReceived += HandleConversationMessageReceived;
            mateConversationService.StatusChanged += HandleConversationStatusChanged;
            LoadWindowSettings();
        }

        private async void Start()
        {
            try
            {
                await Task.Yield();
                ApplyWindowSettings();
                await TryConfigureStartupAnimationPackageAsync();
                MigrateLegacySelectedLocalModelIfNeeded();
                modelLibraryMigrationService!.MigrateManagedLocalModelDirectoriesIfNeeded();

                if (!restoreSelectedModelOnStart)
                {
                    return;
                }

                await RestoreLastSelectedModelAsync();
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        private void Update()
        {
            mateConversationService?.Tick(Time.unscaledTime);
            UpdateTargetFrameRate();

            if (!ForceTopMost || windowService == null || !isTopMostEnabled)
            {
                return;
            }

            if (!windowService.IsTopMost)
            {
                windowService.SetTopMost(true);
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            mateConversationService?.NotifyApplicationFocus(hasFocus);
            if (!hasFocus && windowService != null)
            {
                SaveCurrentWindowPosition();
            }
        }

        private void OnDestroy()
        {
            CancelCurrentLoad();
            if (windowService != null)
            {
                SaveWindowSettings();
            }

            if (mateConversationService != null)
            {
                mateConversationService.MessageReceived -= HandleConversationMessageReceived;
                mateConversationService.StatusChanged -= HandleConversationStatusChanged;
                mateConversationService.Dispose();
                mateConversationService = null;
            }
        }

        [ContextMenu("Open Local Model Dialog")]
        public async void OpenLocalModelDialog()
        {
            try
            {
                EnsureServices();
                var path = windowService!.RunWithTopMostDisabled(
                    () => fileDialogService!.OpenModelFile(GetInitialDirectory()));
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                await LoadModelFromPathAsync(
                    ImportLocalModelIntoLibraryIfNeeded(path),
                    ContentSource.Local,
                    persistSelection: true);
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        [ContextMenu("Open Local VRMA File")]
        public async void OpenLocalAnimationFileDialog()
        {
            try
            {
                EnsureServices();
                var path = windowService!.RunWithTopMostDisabled(
                    () => fileDialogService!.OpenAnimationFile(GetInitialDirectory()));
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                var managedPath = ImportLocalAnimationIntoLibraryIfNeeded(path);
                await ApplyLocalAnimationPackageAsync(managedPath, preferredEntryPath: managedPath);
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        [ContextMenu("Open Local VRMA Folder")]
        public async void OpenLocalAnimationFolderDialog()
        {
            try
            {
                EnsureServices();
                var path = windowService!.RunWithTopMostDisabled(
                    () => fileDialogService!.OpenAnimationFolder(GetInitialDirectory()));
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                await ApplyLocalAnimationPackageAsync(path);
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        [ContextMenu("Open Local Behavior Manifest")]
        public async void OpenLocalBehaviorManifestDialog()
        {
            try
            {
                EnsureServices();
                var path = windowService!.RunWithTopMostDisabled(
                    () => fileDialogService!.OpenBehaviorManifestFile(GetInitialDirectory()));
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                await ApplyLocalBehaviorManifestAsync(path);
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        [ContextMenu("Restore Last Selected Model")]
        public async void RestoreLastSelectedModel()
        {
            try
            {
                await RestoreLastSelectedModelAsync();
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        public async Task RestoreLastSelectedModelAsync()
        {
            EnsureServices();

            var selectedContent = selectedContentStore!.Load();
            if (selectedContent == null)
            {
                return;
            }

            if (selectedContent.Type != ContentType.Model || selectedContent.Source == SelectedContentSource.None)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedContent.Data))
            {
                return;
            }

            if (!File.Exists(selectedContent.Data))
            {
                selectedContentStore.Clear();
                return;
            }

            await LoadModelFromPathAsync(
                selectedContent.Data,
                ToContentSource(selectedContent.Source),
                persistSelection: false);
        }

        public async void LoadLocalModelFromPath(string path)
        {
            try
            {
                await LoadModelFromPathAsync(
                    ImportLocalModelIntoLibraryIfNeeded(path),
                    ContentSource.Local,
                    persistSelection: true);
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        public async void LoadLocalAnimationFromPath(string path)
        {
            try
            {
                var managedPath = ImportLocalAnimationIntoLibraryIfNeeded(path);
                await ApplyLocalAnimationPackageAsync(managedPath, preferredEntryPath: managedPath);
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        public async void ApplyBuiltInPose(string poseId)
        {
            try
            {
                var pose = FindRequiredBuiltInPose(poseId);
                await PlayBuiltInPoseOnceAsync(pose, useCatalogBubble: true);
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        public async void PlayConversationBuiltInPose(string poseId)
        {
            try
            {
                var pose = FindRequiredBuiltInPose(poseId);
                await PlayBuiltInPoseOnceAsync(pose, useCatalogBubble: false);
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        public async void LoadCachedModel(string path)
        {
            try
            {
                var resolvedPath = ImportLocalModelIntoLibraryIfNeeded(path);
                await LoadModelFromPathAsync(resolvedPath, InferContentSourceFromPath(resolvedPath), persistSelection: true);
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        public void LoadManagedLocalModel(string path)
        {
            LoadCachedModel(path);
        }

        public void LoadManagedLocalAnimation(string path)
        {
            LoadLocalAnimationFromPath(path);
        }

        public IReadOnlyList<ContentItem> GetManagedLocalModels()
        {
            EnsureServices();

            return contentCatalog!
                .Scan(modelLibraryPaths!.RootPath, ContentSource.Local)
                .Where(item => item.Type == ContentType.Model)
                .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public IReadOnlyList<ContentItem> GetManagedLocalAnimations()
        {
            EnsureServices();

            return contentCatalog!
                .Scan(animationLibraryPaths!.RootPath, ContentSource.Local)
                .Where(item => item.Type == ContentType.Animation)
                .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public bool CanDeleteManagedLocalModel(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || modelLibraryPaths == null)
            {
                return false;
            }

            return modelLibraryPaths.ContainsPath(Path.GetFullPath(path));
        }

        public bool CanDeleteManagedLocalAnimation(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || animationLibraryPaths == null)
            {
                return false;
            }

            return animationLibraryPaths.ContainsPath(Path.GetFullPath(path));
        }

        public bool IsCurrentModelPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                   && CurrentModel != null
                   && string.Equals(
                       CurrentModel.SourcePath,
                       Path.GetFullPath(path),
                       StringComparison.OrdinalIgnoreCase);
        }

        public string GetManagedLocalModelDisplayDirectory(string path)
        {
            EnsureServices();
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A model path is required.", nameof(path));
            }

            var normalizedPath = Path.GetFullPath(path);
            var itemDirectory = Path.GetDirectoryName(normalizedPath);
            if (string.IsNullOrWhiteSpace(itemDirectory) || !modelLibraryPaths!.ContainsPath(itemDirectory))
            {
                return normalizedPath;
            }

            return modelLibraryPaths.GetDisplayRelativeItemDirectory(itemDirectory);
        }

        public string GetManagedLocalAnimationDisplayDirectory(string path)
        {
            EnsureServices();
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("An animation path is required.", nameof(path));
            }

            var normalizedPath = Path.GetFullPath(path);
            var itemDirectory = Path.GetDirectoryName(normalizedPath);
            if (string.IsNullOrWhiteSpace(itemDirectory) || !animationLibraryPaths!.ContainsPath(itemDirectory))
            {
                return normalizedPath;
            }

            return animationLibraryPaths.GetDisplayRelativeItemDirectory(itemDirectory);
        }

        public void DeleteManagedLocalModel(string path)
        {
            EnsureServices();
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A model path is required.", nameof(path));
            }

            var normalizedPath = Path.GetFullPath(path);
            if (!modelLibraryPaths!.ContainsPath(normalizedPath))
            {
                throw new UserFacingException("当前只支持删除已导入角色库的本地角色。");
            }

            var itemDirectory = Path.GetDirectoryName(normalizedPath);
            if (string.IsNullOrWhiteSpace(itemDirectory))
            {
                throw new InvalidOperationException("The model library item directory could not be resolved.");
            }

            var normalizedItemDirectory = Path.GetFullPath(itemDirectory);
            if (!modelLibraryPaths.ContainsPath(normalizedItemDirectory))
            {
                throw new InvalidOperationException("The target path is outside of the managed model library.");
            }

            if (IsCurrentModelPath(normalizedPath))
            {
                ClearSelectedModel();
            }
            else if (selectedContentStore!.Load() is { Type: ContentType.Model } selectedContent
                     && string.Equals(selectedContent.Data, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                selectedContentStore.Clear();
            }

            if (Directory.Exists(normalizedItemDirectory))
            {
                Directory.Delete(normalizedItemDirectory, recursive: true);
            }

            cachedModelStore!.Forget(normalizedPath);
        }

        public void DeleteManagedLocalAnimation(string path)
        {
            EnsureServices();
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("An animation path is required.", nameof(path));
            }

            var normalizedPath = Path.GetFullPath(path);
            if (!animationLibraryPaths!.ContainsPath(normalizedPath))
            {
                throw new UserFacingException("当前只支持删除已导入动作库的本地 VRMA 动作。");
            }

            var itemDirectory = Path.GetDirectoryName(normalizedPath);
            if (string.IsNullOrWhiteSpace(itemDirectory))
            {
                throw new InvalidOperationException("The animation library item directory could not be resolved.");
            }

            var normalizedItemDirectory = Path.GetFullPath(itemDirectory);
            if (!animationLibraryPaths.ContainsPath(normalizedItemDirectory))
            {
                throw new InvalidOperationException("The target path is outside of the managed animation library.");
            }

            if (Directory.Exists(normalizedItemDirectory))
            {
                Directory.Delete(normalizedItemDirectory, recursive: true);
            }

            ManagedLocalAnimationsChanged?.Invoke();
        }

        public void QuitApplication()
        {
            Application.Quit();
        }

        public void SetContextMenuOpen(bool isOpen)
        {
            isContextMenuOpen = isOpen;
        }

        public void RequestApplicationFocus()
        {
            EnsureServices();
            windowService?.RequestApplicationFocus();
        }

        public Task SendChatMessageAsync(string userMessage, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                throw new ArgumentException("A chat message is required.", nameof(userMessage));
            }

            EnsureServices();
            if (CurrentModel == null)
            {
                throw new UserFacingException("当前还没有加载角色，暂时无法发起对话。");
            }

            return mateConversationService!.SendUserMessageAsync(
                CurrentModel.SourcePath,
                CurrentModel.DisplayName,
                userMessage,
                cancellationToken);
        }

        public Task RefreshConversationBackendAsync(CancellationToken cancellationToken = default)
        {
            EnsureServices();
            if (CurrentModel == null)
            {
                return mateConversationService!.ClearCharacterContextAsync(cancellationToken);
            }

            return mateConversationService!.SynchronizeAsync(
                CurrentModel.SourcePath,
                CurrentModel.DisplayName,
                cancellationToken);
        }

        public void MarkConversationMessagesRead()
        {
            EnsureServices();
            mateConversationService?.MarkMessagesRead();
        }

        public void SetExpandedWindowMode(bool expanded)
        {
            if (isExpandedWindowMode == expanded)
            {
                return;
            }

            isExpandedWindowMode = expanded;
            if (expanded)
            {
                ApplyExpandedWindowRect();
                return;
            }

            if (!compactWindowEnabled)
            {
                ApplyWindowSettings();
                return;
            }

            SyncCompactWindow(forceApply: true);
        }

        public void RefreshCompactWindowLayout()
        {
            if (isExpandedWindowMode || !compactWindowEnabled)
            {
                return;
            }

            SyncCompactWindow(forceApply: true);
        }

        public void NotifyConversationActivity(float durationSeconds = ConversationAmbientPoseSuppressionSeconds)
        {
            suppressAmbientPoseUntilTime = Mathf.Max(
                suppressAmbientPoseUntilTime,
                Time.unscaledTime + Mathf.Max(0f, durationSeconds));
        }

        public string GetBuiltInPosePath(string poseId)
        {
            return GetBuiltInPosePath(FindRequiredBuiltInPose(poseId));
        }

        [ContextMenu("Toggle TopMost")]
        public void ToggleTopMost()
        {
            SetTopMost(ForceTopMost || !isTopMostEnabled);
        }

        [ContextMenu("Toggle ClickThrough")]
        public void ToggleClickThrough()
        {
            SetClickThrough(!isClickThroughLocked);
        }

        [ContextMenu("Move To Next Monitor")]
        public void MoveToNextMonitor()
        {
            EnsureServices();

            var monitorCount = windowService!.MonitorCount;
            if (monitorCount <= 0)
            {
                return;
            }

            MoveToMonitor((monitorIndex + 1) % monitorCount);
        }

        [ContextMenu("Apply Window Settings")]
        public void ReapplyWindowSettings()
        {
            ApplyWindowSettings();
        }

        [ContextMenu("Refresh Workshop Content")]
        public async void RefreshWorkshopContent()
        {
            try
            {
                await RefreshWorkshopContentAsync();
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        [ContextMenu("Load First Workshop Model")]
        public async void LoadFirstWorkshopModel()
        {
            try
            {
                var items = await RefreshWorkshopContentAsync();
                var workshopModel = items.FirstOrDefault(item => item.ContentItem.Type == ContentType.Model);
                if (workshopModel == null)
                {
                    throw new InvalidOperationException("No workshop model content is currently available.");
                }

                await LoadWorkshopContentAsync(workshopModel);
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        [ContextMenu("Apply First Workshop Animation Package")]
        public async void ApplyFirstWorkshopAnimationPackage()
        {
            try
            {
                var items = await RefreshWorkshopContentAsync();
                var animationContent = items.FirstOrDefault(item => item.ContentItem.Type == ContentType.Animation);
                if (animationContent == null)
                {
                    throw new InvalidOperationException("No workshop animation package is currently available.");
                }

                await ApplyWorkshopAnimationPackageAsync(animationContent);
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        [ContextMenu("Apply First Workshop Behavior Package")]
        public async void ApplyFirstWorkshopBehaviorPackage()
        {
            try
            {
                var items = await RefreshWorkshopContentAsync();
                var behaviorContent = items.FirstOrDefault(item => item.ContentItem.Type == ContentType.Behavior);
                if (behaviorContent == null)
                {
                    throw new InvalidOperationException("No workshop behavior package is currently available.");
                }

                await ApplyWorkshopBehaviorPackageAsync(behaviorContent);
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        [ContextMenu("Play Idle Animation")]
        public async void PlayIdleAnimation()
        {
            try
            {
                await GetAnimationController().PlayIdleAsync();
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        [ContextMenu("Play Click Animation")]
        public async void PlayClickAnimation()
        {
            try
            {
                await GetAnimationController().PlayClickAsync();
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        [ContextMenu("Play Pose Animation")]
        public async void PlayPoseAnimation()
        {
            try
            {
                await GetAnimationController().PlayPoseAsync();
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        [ContextMenu("Clear Selected Model")]
        public async void ClearSelectedModel()
        {
            EnsureServices();
            CancelCurrentLoad();

            if (currentModelRoot != null)
            {
                characterRuntimeAssembler!.Destroy(currentModelRoot);
                currentModelRoot = null;
                await ReclaimUnusedResourcesAsync();
            }

            CurrentModel = null;
            LastErrorMessage = null;
            selectedContentStore!.Clear();
            ModelCleared?.Invoke();
            _ = RefreshConversationBackendAsync();
        }

        [ContextMenu("Save Current Transform State")]
        public void SaveCurrentTransformState()
        {
            EnsureServices();

            if (currentModelRoot == null)
            {
                return;
            }

            var settings = settingsStore!.Load() with
            {
                PositionX = currentModelRoot.transform.localPosition.x,
                PositionY = currentModelRoot.transform.localPosition.y,
                Scale = currentModelRoot.transform.localScale.x,
                RotationY = 0f,
            };

            settingsStore.Save(settings);
        }

        public void SaveCurrentWindowPosition()
        {
            EnsureServices();
            if (windowService == null)
            {
                return;
            }

            var position = ClampWindowPositionToMonitor(windowService.WindowPosition);
            if ((position - windowService.WindowPosition).sqrMagnitude > 0.0001f)
            {
                windowService.SetWindowPosition(position);
            }

            savedWindowPosition = position;
            var settings = settingsStore!.Load() with
            {
                HasWindowPosition = true,
                WindowPositionX = position.x,
                WindowPositionY = position.y,
            };
            settingsStore.Save(settings);
        }

        public Vector2 GetGlobalCursorPosition()
        {
            EnsureServices();
            return windowService!.CursorPosition;
        }

        public Vector2 GetWindowPosition()
        {
            EnsureServices();
            return windowService!.WindowPosition;
        }

        public Vector2 GetWindowClientSize()
        {
            EnsureServices();
            return windowService!.ClientSize;
        }

        public void SetWindowPosition(Vector2 position)
        {
            EnsureServices();
            windowService!.SetWindowPosition(position);
        }

        public Vector2 ClampCurrentWindowPositionToMonitor()
        {
            EnsureServices();
            if (windowService == null)
            {
                return Vector2.zero;
            }

            var clampedPosition = ClampWindowPositionToMonitor(windowService.WindowPosition);
            if ((clampedPosition - windowService.WindowPosition).sqrMagnitude > 0.0001f)
            {
                windowService.SetWindowPosition(clampedPosition);
            }

            return clampedPosition;
        }

        public Vector2 ClampWindowPositionToMonitor(Vector2 desiredPosition)
        {
            EnsureServices();
            if (windowService == null || windowService.MonitorCount <= 0)
            {
                return desiredPosition;
            }

            return windowService.ClampWindowPositionToMonitor(desiredPosition, monitorIndex);
        }

        public void SetTopMost(bool enabled)
        {
            EnsureServices();

            isTopMostEnabled = ForceTopMost || enabled;
            windowService!.SetTopMost(isTopMostEnabled);
            SaveWindowSettings();
        }

        public void SetClickThrough(bool enabled)
        {
            EnsureServices();

            var resolvedEnabled = AllowClickThrough && enabled;
            isClickThroughLocked = resolvedEnabled;
            windowService!.SetClickThrough(resolvedEnabled);
            SaveWindowSettings();
        }

        public void MoveToMonitor(int index)
        {
            EnsureServices();

            var monitorCount = windowService!.MonitorCount;
            monitorIndex = monitorCount > 0
                ? Mathf.Clamp(index, 0, monitorCount - 1)
                : 0;

            if (monitorCount > 0)
            {
                ApplyMonitorWindowRect();
                windowService.EnsureVisible();
            }

            SaveWindowSettings();
        }

        public async Task<IReadOnlyList<WorkshopContentItem>> RefreshWorkshopContentAsync()
        {
            EnsureServices();
            workshopContent = await workshopService!.GetSubscribedContentAsync();
            workshopContentSummary = workshopContent.Count == 0
                ? "No workshop content detected."
                : string.Join(
                    Environment.NewLine,
                    workshopContent.Select(item =>
                        $"{item.PublishedFileId} | {item.ContentItem.Type} | {item.ContentItem.Title} | {item.ContentItem.EntryPath}"));
            return workshopContent;
        }

        public Task LoadWorkshopContentAsync(WorkshopContentItem workshopItem)
        {
            if (workshopItem == null)
            {
                throw new ArgumentNullException(nameof(workshopItem));
            }

            return LoadModelFromPathAsync(
                workshopItem.ContentItem.EntryPath,
                ContentSource.Workshop,
                persistSelection: true);
        }

        public Task<AnimationPackage> ApplyWorkshopAnimationPackageAsync(WorkshopContentItem workshopItem)
        {
            if (workshopItem == null)
            {
                throw new ArgumentNullException(nameof(workshopItem));
            }

            return animationPackageInstaller!.InstallAsync(workshopItem.ContentItem, GetAnimationController());
        }

        public async Task<AnimationPackage> ApplyLocalAnimationPackageAsync(
            string rootPath,
            string preferredEntryPath = "",
            bool playAsPose = false,
            CancellationToken cancellationToken = default)
        {
            EnsureServices();

            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new ArgumentException("An animation package path is required.", nameof(rootPath));
            }

            var package = CreateLocalAnimationPackage(rootPath, preferredEntryPath, playAsPose);
            await GetAnimationController().ApplyAnimationPackageAsync(package, cancellationToken);

            if (playAsPose && currentModelRoot != null)
            {
                await GetAnimationController().PlayPoseAsync(cancellationToken);
            }

            return package;
        }

        public Task<IBehaviorPreset> ApplyLocalBehaviorManifestAsync(
            string manifestPath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                throw new ArgumentException("A behavior manifest path is required.", nameof(manifestPath));
            }

            EnsureServices();
            var preset = behaviorPackageInstaller!.CreatePreset(manifestPath);
            return ApplyBehaviorPresetToRuntimeAsync(preset, cancellationToken);
        }

        public async Task<IBehaviorPreset> ApplyWorkshopBehaviorPackageAsync(WorkshopContentItem workshopItem)
        {
            if (workshopItem == null)
            {
                throw new ArgumentNullException(nameof(workshopItem));
            }

            var preset = await behaviorPackageInstaller!.InstallAsync(workshopItem.ContentItem, GetAnimationController());
            GetMovementController().ApplyBehaviorPreset(preset);
            return preset;
        }

        private async Task<IBehaviorPreset> ApplyBehaviorPresetToRuntimeAsync(
            IBehaviorPreset preset,
            CancellationToken cancellationToken)
        {
            await GetAnimationController().ApplyBehaviorPresetAsync(preset, cancellationToken);
            GetMovementController().ApplyBehaviorPreset(preset);
            return preset;
        }

        public Task MoveCurrentPetToWorldPositionAsync(
            Vector3 worldPosition,
            CancellationToken cancellationToken = default)
        {
            return GetMovementController().MoveToWorldPositionAsync(worldPosition, cancellationToken);
        }

        public Task MoveCurrentPetToViewportPointAsync(
            Vector2 viewportPoint,
            CancellationToken cancellationToken = default)
        {
            return GetMovementController().MoveToViewportPointAsync(viewportPoint, cancellationToken);
        }

        private async Task LoadModelFromPathAsync(string path, ContentSource source, bool persistSelection)
        {
            EnsureServices();

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A model path is required.", nameof(path));
            }

            LastErrorMessage = null;
            var previousModelRoot = currentModelRoot;
            var cancellationToken = BeginLoad();

            try
            {
                var result = await modelLoader!.LoadAsync(path, ModelAnchor, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (source == ContentSource.Local && modelLibraryPaths!.ContainsPath(result.SourcePath))
                {
                    var normalizedPath = modelLibraryMigrationService!.NormalizeManagedLocalModelPath(
                        result.SourcePath,
                        result.DisplayName);
                    result = result with { SourcePath = normalizedPath };
                }

                if (result.Root.GetComponentInChildren<UniVRM10.Vrm10Instance>() is UniVRM10.Vrm10Instance vrmInstance)
                {
                    _ = vrmInstance.Runtime;
                }

                currentModelRoot = result.Root;
                CurrentModel = result;
                ApplySavedTransform(result.Root.transform);
                var scaleController = GetComponent<DesktopPetScaleController>();
                if (scaleController != null)
                {
                    scaleController.ConstrainCurrentModelTransform();
                }

                EnsureModelIsVisible(result.Root);
                if (scaleController != null)
                {
                    scaleController.ConstrainCurrentModelTransform(persistState: true);
                }

                if (persistSelection)
                {
                    selectedContentStore!.Save(ToSelectedContentSource(source), ContentType.Model, result.SourcePath);
                }

                cachedModelStore!.Remember(result.DisplayName, result.SourcePath);

                if (previousModelRoot != null && previousModelRoot != currentModelRoot)
                {
                    characterRuntimeAssembler!.Destroy(previousModelRoot);
                    await ReclaimUnusedResourcesAsync();
                }

                if (compactWindowEnabled)
                {
                    SyncCompactWindow(forceApply: true);
                }
                ModelLoaded?.Invoke(result);
                _ = RefreshConversationBackendAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        private CancellationToken BeginLoad()
        {
            CancelCurrentLoad();
            loadCancellationTokenSource = new CancellationTokenSource();
            return loadCancellationTokenSource.Token;
        }

        private void CancelCurrentLoad()
        {
            if (loadCancellationTokenSource == null)
            {
                return;
            }

            loadCancellationTokenSource.Cancel();
            loadCancellationTokenSource.Dispose();
            loadCancellationTokenSource = null;
        }

        private void EnsureServices()
        {
            characterRuntimeAssembler ??= new CharacterRuntimeAssembler();
            animationPackageInstaller ??= new AnimationPackageInstaller();
            animationLibraryPaths ??= new AnimationLibraryPaths();
            behaviorPackageInstaller ??= new BehaviorPackageInstaller();
            boundsService ??= new DesktopPetBoundsService();
            contentCatalog ??= new FileSystemContentCatalog();
            modelLibraryPaths ??= new ModelLibraryPaths();
            modelFingerprintService ??= new ModelFingerprintService();
            modelImportService ??= new ModelImportService(modelLibraryPaths, contentCatalog, modelFingerprintService);
            animationImportService ??= new AnimationImportService(animationLibraryPaths, contentCatalog, modelFingerprintService);
            modelLoader ??= new VrmModelLoaderService(characterRuntimeAssembler, settingsStore!);
            fileDialogService ??= new StandaloneFileDialogService();
            if (settingsStore == null)
            {
                settingsStore = new DesktopPetSettingsStore();
                LoadWindowSettings();
            }

            modelLibraryMigrationService ??= new ModelLibraryMigrationService(settingsStore, modelLibraryPaths, modelImportService);
            steamPlatformService ??= new SteamworksNetPlatformService(steamAppId);
            windowService ??= new UniWindowWindowService(gameObject);
            workshopService ??= new SteamworksNetWorkshopService(steamPlatformService, contentCatalog);
            cachedModelStore ??= new CachedModelStore(settingsStore);
            selectedContentStore ??= new SelectedContentStore(settingsStore);
            mateConversationOrchestrator ??= new MateConversationOrchestrator(
                new AiSettingsStore(),
                new AiSecretsStore(),
                new ChatSessionStore(),
                new LlmUsageStatsStore(),
                modelFingerprintService);
            if (mateConversationService == null)
            {
                mateConversationService = new MateConversationService(
                    new AiSettingsStore(),
                    new LocalLlmConversationBackend(
                        mateConversationOrchestrator,
                        new AiSecretsStore(),
                        modelFingerprintService),
                    new OpenClawConversationBackend(
                        new AiSecretsStore(),
                        new OpenClawGatewayClient(),
                        new OpenClawTranscriptMirrorStore(),
                        modelFingerprintService));
                mateConversationService.MessageReceived += HandleConversationMessageReceived;
                mateConversationService.StatusChanged += HandleConversationStatusChanged;
            }
        }

        private DesktopPetAnimationController GetAnimationController()
        {
            return GetComponent<DesktopPetAnimationController>();
        }

        private void HandleConversationMessageReceived(ConversationMessageEnvelope envelope)
        {
            ConversationMessageReceived?.Invoke(envelope);
        }

        private void HandleConversationStatusChanged(ConversationStatusSnapshot status)
        {
            ConversationStatusChanged?.Invoke(status);
        }

        private DesktopPetFallbackMotionController? GetFallbackMotionController()
        {
            return GetComponent<DesktopPetFallbackMotionController>();
        }

        private DesktopPetMovementController GetMovementController()
        {
            return GetComponent<DesktopPetMovementController>();
        }

        private void ApplySavedTransform(Transform modelTransform)
        {
            var settings = settingsStore!.Load();
            var scale = settings.Scale > 0f ? settings.Scale : 1f;

            modelTransform.localPosition = new Vector3(settings.PositionX, settings.PositionY, 0f);
            modelTransform.localRotation = Quaternion.Euler(0f, DefaultFacingRotationY, 0f);
            modelTransform.localScale = Vector3.one * scale;
        }

        private void EnsureModelIsVisible(GameObject modelRoot)
        {
            var camera = InteractionCamera;
            if (camera == null || boundsService == null)
            {
                return;
            }

            if (IsModelVisible(camera, modelRoot))
            {
                return;
            }

            FrameModelInCamera(camera, modelRoot);
        }

        private bool IsModelVisible(Camera camera, GameObject modelRoot)
        {
            if (!boundsService!.TryGetScreenRect(camera, modelRoot, out var screenRect))
            {
                return false;
            }

            if (screenRect.width < 48f || screenRect.height < 48f)
            {
                return false;
            }

            var expandedViewport = Rect.MinMaxRect(
                -Screen.width * 0.25f,
                -Screen.height * 0.25f,
                Screen.width * 1.25f,
                Screen.height * 1.25f);
            return expandedViewport.Overlaps(screenRect);
        }

        private void FrameModelInCamera(Camera camera, GameObject modelRoot)
        {
            if (!boundsService!.TryGetWorldBounds(modelRoot, out var bounds))
            {
                return;
            }

            var center = bounds.center;
            var extents = bounds.extents;
            var aspect = camera.aspect > 0f ? camera.aspect : 1f;
            var orthographicSize = Mathf.Max(extents.y, extents.x / aspect) + 0.2f;
            var distance = Mathf.Max(extents.z + 5f, 10f);

            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(orthographicSize, 0.5f);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = Mathf.Max(camera.farClipPlane, distance + bounds.size.magnitude + 10f);
            camera.transform.position = new Vector3(center.x, center.y, center.z - distance);
            camera.transform.rotation = Quaternion.identity;
        }

        private string GetInitialDirectory()
        {
            EnsureServices();

            var selectedContent = selectedContentStore!.Load();
            if (selectedContent != null && !string.IsNullOrWhiteSpace(selectedContent.Data))
            {
                var candidatePath = Path.GetFullPath(selectedContent.Data);
                if (modelLibraryPaths != null && modelLibraryPaths.ContainsPath(candidatePath))
                {
                    return Application.dataPath;
                }

                var directory = Path.GetDirectoryName(candidatePath);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                {
                    return directory;
                }
            }

            return Application.dataPath;
        }

        private void MigrateLegacySelectedLocalModelIfNeeded()
        {
            EnsureServices();
            modelLibraryMigrationService!.MigrateSelectedLocalModelIfNeeded();
        }

        private string ImportLocalModelIntoLibraryIfNeeded(string path)
        {
            EnsureServices();

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A model path is required.", nameof(path));
            }

            var normalizedPath = Path.GetFullPath(path);
            if (!File.Exists(normalizedPath))
            {
                throw new FileNotFoundException("The model file does not exist.", normalizedPath);
            }

            if (!string.Equals(Path.GetExtension(normalizedPath), ".vrm", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedPath;
            }

            if (modelLibraryPaths!.ContainsPath(normalizedPath)
                || InferContentSourceFromPath(normalizedPath) == ContentSource.BuiltIn)
            {
                return normalizedPath;
            }

            return modelImportService!.Import(normalizedPath).Item.EntryPath;
        }

        private string ImportLocalAnimationIntoLibraryIfNeeded(string path)
        {
            EnsureServices();

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("An animation path is required.", nameof(path));
            }

            var normalizedPath = Path.GetFullPath(path);
            if (!File.Exists(normalizedPath))
            {
                throw new FileNotFoundException("The animation file does not exist.", normalizedPath);
            }

            if (!string.Equals(Path.GetExtension(normalizedPath), ".vrma", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedPath;
            }

            if (animationLibraryPaths!.ContainsPath(normalizedPath)
                || InferContentSourceFromPath(normalizedPath) == ContentSource.BuiltIn)
            {
                return normalizedPath;
            }

            var importResult = animationImportService!.ImportFile(normalizedPath);
            if (importResult.ImportedNewItem)
            {
                ManagedLocalAnimationsChanged?.Invoke();
            }

            return importResult.Item.EntryPath;
        }

        private async Task TryConfigureStartupAnimationPackageAsync()
        {
            var animationFilePath = Environment.GetEnvironmentVariable(StartupAnimationFileEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(animationFilePath))
            {
                await ApplyLocalAnimationPackageAsync(animationFilePath, preferredEntryPath: animationFilePath);
                return;
            }

            var animationDirectoryPath = Environment.GetEnvironmentVariable(StartupAnimationDirectoryEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(animationDirectoryPath))
            {
                await ApplyLocalAnimationPackageAsync(animationDirectoryPath);
            }
        }

        private void LoadWindowSettings()
        {
            var settings = settingsStore!.Load();
            isTopMostEnabled = ForceTopMost || settings.IsTopMost;
            isClickThroughLocked = AllowClickThrough && settings.IsClickThrough;
            monitorIndex = settings.MonitorIndex;
            compactWindowEnabled = false;
            savedWindowPosition = settings.HasWindowPosition
                ? new Vector2(settings.WindowPositionX, settings.WindowPositionY)
                : Vector2.zero;
            vrmImportPerformanceMode = settings.VrmImportPerformanceMode;
        }

        private void ApplyWindowSettings()
        {
            EnsureServices();

            isTopMostEnabled = ForceTopMost || isTopMostEnabled;
            windowService!.Configure(InteractionCamera);
            windowService.SetTopMost(isTopMostEnabled);
            windowService.SetClickThrough(AllowClickThrough && isClickThroughLocked);

            if (windowService.MonitorCount > 0)
            {
                ApplyMonitorWindowRect();
            }

            if (!isExpandedWindowMode && compactWindowEnabled)
            {
                if (settingsStore!.Load().HasWindowPosition)
                {
                    var clampedSavedWindowPosition = ClampWindowPositionToMonitor(savedWindowPosition);
                    savedWindowPosition = clampedSavedWindowPosition;
                    windowService.SetWindowPosition(clampedSavedWindowPosition);
                }

                SyncCompactWindow(forceApply: true);
            }

            windowService.EnsureVisible();
        }

        private void SaveWindowSettings()
        {
            var settings = settingsStore!.Load() with
            {
                IsTopMost = ForceTopMost || isTopMostEnabled,
                IsClickThrough = AllowClickThrough && isClickThroughLocked,
                MonitorIndex = monitorIndex,
                CompactWindowEnabled = compactWindowEnabled,
                HasWindowPosition = true,
                WindowPositionX = windowService?.WindowPosition.x ?? savedWindowPosition.x,
                WindowPositionY = windowService?.WindowPosition.y ?? savedWindowPosition.y,
                VrmImportPerformanceMode = vrmImportPerformanceMode,
            };

            settingsStore!.Save(settings);
        }

        private AnimationPackage CreateLocalAnimationPackage(
            string rootPath,
            string preferredEntryPath = "",
            bool playAsPose = false)
        {
            var resolvedRootPath = ResolveAnimationRootPath(rootPath);
            var resolvedEntryPath = ResolveAnimationEntryPath(resolvedRootPath, preferredEntryPath);
            var package = animationPackageInstaller!.CreatePackage(resolvedRootPath, resolvedEntryPath);

            return playAsPose
                ? package with
                {
                    IdleAnimationPath = string.Empty,
                    PoseAnimationPath = package.EntryPath,
                }
                : package;
        }

        private async Task ApplyBuiltInPoseAsync(BuiltInPoseOption pose, CancellationToken cancellationToken = default)
        {
            var posePath = GetBuiltInPosePath(pose);
            if (!File.Exists(posePath))
            {
                throw new FileNotFoundException("The built-in pose file does not exist.", posePath);
            }

            await ApplyLocalAnimationPackageAsync(
                posePath,
                preferredEntryPath: posePath,
                playAsPose: true,
                cancellationToken: cancellationToken);
        }

        private async Task PlayBuiltInPoseOnceAsync(
            BuiltInPoseOption pose,
            bool useCatalogBubble,
            CancellationToken cancellationToken = default)
        {
            var posePath = GetBuiltInPosePath(pose);
            if (!File.Exists(posePath))
            {
                throw new FileNotFoundException("The built-in pose file does not exist.", posePath);
            }

            BuiltInPoseTriggered?.Invoke(new BuiltInPosePlaybackEvent(pose.Id, useCatalogBubble));

            var fallbackMotionController = GetFallbackMotionController();
            if (fallbackMotionController != null)
            {
                await fallbackMotionController.PlayPoseOnceAsync(posePath, cancellationToken);
                return;
            }

            await GetAnimationController().PlayOneShotPathAsync(
                posePath,
                returnToIdle: true,
                cancellationToken);
        }

        private static BuiltInPoseOption FindRequiredBuiltInPose(string poseId)
        {
            return BuiltInPoseCatalog.FindRequired(poseId);
        }

        private static string GetBuiltInPosePath(BuiltInPoseOption pose)
        {
            return Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, pose.RelativePath));
        }

        private static string ResolveAnimationEntryPath(string resolvedRootPath, string preferredEntryPath)
        {
            if (string.IsNullOrWhiteSpace(preferredEntryPath))
            {
                return string.Empty;
            }

            return File.Exists(preferredEntryPath)
                ? Path.GetFullPath(preferredEntryPath)
                : Path.GetFullPath(Path.Combine(resolvedRootPath, preferredEntryPath));
        }

        private static string ResolveAnimationRootPath(string path)
        {
            if (Directory.Exists(path))
            {
                return Path.GetFullPath(path);
            }

            if (File.Exists(path))
            {
                var directory = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    throw new InvalidOperationException("Animation root path could not be resolved.");
                }

                return Path.GetFullPath(directory);
            }

            throw new DirectoryNotFoundException($"Animation package root was not found: {path}");
        }

        private static ContentSource InferContentSourceFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A model path is required.", nameof(path));
            }

            var normalizedPath = Path.GetFullPath(path);
            var streamingAssetsPath = Path.GetFullPath(Application.streamingAssetsPath);
            return normalizedPath.StartsWith(streamingAssetsPath, StringComparison.OrdinalIgnoreCase)
                ? ContentSource.BuiltIn
                : ContentSource.Local;
        }

        private void ReportFailure(Exception exception)
        {
            LastErrorMessage = exception.Message;
            if (exception is UserFacingException)
            {
                Debug.LogWarning(exception.Message);
            }
            else
            {
                Debug.LogException(exception);
            }

            ModelLoadFailed?.Invoke(exception.Message);
        }

        private void SyncCompactWindow(bool forceApply)
        {
            if (windowService == null || currentModelRoot == null || InteractionCamera == null || boundsService == null)
            {
                return;
            }

            if (!boundsService.TryGetScreenRect(InteractionCamera, currentModelRoot, out var screenRect))
            {
                return;
            }

            if (screenRect.width < 8f || screenRect.height < 8f)
            {
                return;
            }

            var desiredRect = BuildCompactWindowRect(screenRect);
            var currentRect = new Rect(windowService.WindowPosition, windowService.WindowSize);
            if (!forceApply && AreRectsApproximatelyEqual(currentRect, desiredRect))
            {
                return;
            }

            windowService.SetWindowRect(desiredRect);
            windowService.EnsureVisible();
            savedWindowPosition = desiredRect.position;
        }

        private Rect BuildCompactWindowRect(Rect modelScreenRect)
        {
            var desiredMin = new Vector2(
                modelScreenRect.xMin - CompactWindowHorizontalPadding,
                modelScreenRect.yMin - CompactWindowBottomPadding);
            var desiredMax = new Vector2(
                modelScreenRect.xMax + CompactWindowHorizontalPadding,
                modelScreenRect.yMax + CompactWindowTopPadding);
            var desiredSize = new Vector2(
                Mathf.Max(CompactWindowMinimumWidth, desiredMax.x - desiredMin.x),
                Mathf.Max(CompactWindowMinimumHeight, desiredMax.y - desiredMin.y));
            var desiredCenter = modelScreenRect.center + new Vector2(0f, CompactWindowTopPadding * 0.15f);
            var localOrigin = desiredCenter - (desiredSize * 0.5f);
            var globalOrigin = windowService!.WindowPosition + localOrigin;
            return new Rect(globalOrigin, desiredSize);
        }

        private void ApplyExpandedWindowRect()
        {
            if (windowService == null)
            {
                return;
            }

            if (windowService.MonitorCount > 0)
            {
                ApplyMonitorWindowRect();
                windowService.EnsureVisible();
            }
        }

        private void ApplyMonitorWindowRect()
        {
            if (windowService == null || windowService.MonitorCount <= 0)
            {
                return;
            }

            windowService.FitToMonitor(monitorIndex);
            var fittedRect = new Rect(windowService.WindowPosition, windowService.WindowSize);
            if (fittedRect == Rect.zero)
            {
                return;
            }

            if (monitorIndex == 0)
            {
                fittedRect.position = Vector2.zero;
            }

            windowService.SetWindowRect(fittedRect);
        }

        private static bool AreRectsApproximatelyEqual(Rect left, Rect right)
        {
            return Mathf.Abs(left.x - right.x) <= CompactWindowRectChangeThreshold
                   && Mathf.Abs(left.y - right.y) <= CompactWindowRectChangeThreshold
                   && Mathf.Abs(left.width - right.width) <= CompactWindowRectChangeThreshold
                   && Mathf.Abs(left.height - right.height) <= CompactWindowRectChangeThreshold;
        }

        private void UpdateTargetFrameRate()
        {
            var targetFrameRate = ShouldUseActiveFrameRate()
                ? ActiveTargetFrameRate
                : IdleTargetFrameRate;
            if (currentTargetFrameRate == targetFrameRate)
            {
                return;
            }

            Application.targetFrameRate = targetFrameRate;
            currentTargetFrameRate = targetFrameRate;
        }

        private bool ShouldUseActiveFrameRate()
        {
            var dragController = GetComponent<DesktopPetDragController>();
            if (dragController != null && dragController.IsDragging)
            {
                return true;
            }

            var rotationController = GetComponent<DesktopPetRotationController>();
            if (rotationController != null && rotationController.IsRotating)
            {
                return true;
            }

            var movementController = GetComponent<DesktopPetMovementController>();
            if (movementController != null && movementController.IsMoving)
            {
                return true;
            }

            var animationController = GetComponent<DesktopPetAnimationController>();
            return isExpandedWindowMode
                   || isContextMenuOpen
                   || (animationController != null && animationController.HasActivePlayback);
        }

        private static async Task ReclaimUnusedResourcesAsync()
        {
            await Task.Yield();
            await Resources.UnloadUnusedAssets();
            GC.Collect();
        }

        private static SelectedContentSource ToSelectedContentSource(ContentSource source)
        {
            return source switch
            {
                ContentSource.BuiltIn => SelectedContentSource.BuiltIn,
                ContentSource.Local => SelectedContentSource.Local,
                ContentSource.Workshop => SelectedContentSource.Workshop,
                _ => SelectedContentSource.None,
            };
        }

        private static ContentSource ToContentSource(SelectedContentSource source)
        {
            return source switch
            {
                SelectedContentSource.BuiltIn => ContentSource.BuiltIn,
                SelectedContentSource.Local => ContentSource.Local,
                SelectedContentSource.Workshop => ContentSource.Workshop,
                _ => ContentSource.Local,
            };
        }
    }
}
