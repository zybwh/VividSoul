#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
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
        private const string ExampleDesktopMoveBehaviorRelativePath = "Defaults/Behavior/example_desktop_move/behavior.json";

        private static readonly BuiltInPoseOption[] BuiltInPoseOptions =
        {
            new("vrma_01", "VRMA_01 Show full body", "Defaults/Animations/VRMA_MotionPack/VRMA_01.vrma"),
            new("vrma_02", "VRMA_02 Greeting", "Defaults/Animations/VRMA_MotionPack/VRMA_02.vrma"),
            new("vrma_03", "VRMA_03 Peace sign", "Defaults/Animations/VRMA_MotionPack/VRMA_03.vrma"),
            new("vrma_04", "VRMA_04 Shoot", "Defaults/Animations/VRMA_MotionPack/VRMA_04.vrma"),
            new("vrma_05", "VRMA_05 Spin", "Defaults/Animations/VRMA_MotionPack/VRMA_05.vrma"),
            new("vrma_06", "VRMA_06 Model pose", "Defaults/Animations/VRMA_MotionPack/VRMA_06.vrma"),
            new("vrma_07", "VRMA_07 Squat", "Defaults/Animations/VRMA_MotionPack/VRMA_07.vrma"),
        };

        [SerializeField] private Camera? interactionCamera;
        [SerializeField] private Transform? modelAnchor;
        [SerializeField] private bool restoreSelectedModelOnStart = true;
        [SerializeField] private uint steamAppId = DefaultSteamAppId;
        [SerializeField, TextArea] private string workshopContentSummary = string.Empty;

        private CharacterRuntimeAssembler? characterRuntimeAssembler;
        private AnimationPackageInstaller? animationPackageInstaller;
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
        private CancellationTokenSource? loadCancellationTokenSource;
        private GameObject? currentModelRoot;
        private IReadOnlyList<WorkshopContentItem> workshopContent = Array.Empty<WorkshopContentItem>();
        private bool isClickThroughLocked;
        private bool isContextMenuOpen;
        private bool isTopMostEnabled;
        private int monitorIndex;

        public event Action<ModelLoadResult>? ModelLoaded;
        public event Action? ModelCleared;
        public event Action<string>? ModelLoadFailed;
        public event Action<string>? BuiltInPoseTriggered;

        public ModelLoadResult? CurrentModel { get; private set; }

        public GameObject? CurrentModelRoot => currentModelRoot;

        public Camera? InteractionCamera => interactionCamera != null ? interactionCamera : Camera.main;

        public bool IsInteractionDisabled => AllowClickThrough && isClickThroughLocked;

        public bool IsModelInteractionBlocked => IsInteractionDisabled || isContextMenuOpen;

        public bool CanUseClickThrough => AllowClickThrough;

        public bool IsTopMostEnabled => isTopMostEnabled;

        public bool IsTopMostForced => ForceTopMost;

        public int MonitorIndex => monitorIndex;

        public string BuiltInDefaultPoseId => DefaultBuiltInPoseId;

        public IReadOnlyList<BuiltInPoseOption> BuiltInPoses => BuiltInPoseOptions;

        public IReadOnlyList<CachedModelState> CachedModels => cachedModelStore != null
            ? cachedModelStore.Load()
            : Array.Empty<CachedModelState>();

        public IReadOnlyList<WorkshopContentItem> WorkshopContent => workshopContent;

        public string? LastErrorMessage { get; private set; }

        private Transform ModelAnchor => modelAnchor != null ? modelAnchor : transform;

        private void Awake()
        {
            characterRuntimeAssembler = new CharacterRuntimeAssembler();
            animationPackageInstaller = new AnimationPackageInstaller();
            behaviorPackageInstaller = new BehaviorPackageInstaller();
            boundsService = new DesktopPetBoundsService();
            contentCatalog = new FileSystemContentCatalog();
            modelLibraryPaths = new ModelLibraryPaths();
            modelFingerprintService = new ModelFingerprintService();
            modelImportService = new ModelImportService(modelLibraryPaths, contentCatalog, modelFingerprintService);
            modelLoader = new VrmModelLoaderService(characterRuntimeAssembler);
            fileDialogService = new StandaloneFileDialogService();
            settingsStore = new DesktopPetSettingsStore();
            modelLibraryMigrationService = new ModelLibraryMigrationService(settingsStore, modelLibraryPaths, modelImportService);
            steamPlatformService = new SteamworksNetPlatformService(steamAppId);
            windowService = new UniWindowWindowService(gameObject);
            workshopService = new SteamworksNetWorkshopService(steamPlatformService, contentCatalog);
            cachedModelStore = new CachedModelStore(settingsStore);
            selectedContentStore = new SelectedContentStore(settingsStore);
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
            if (!ForceTopMost || windowService == null || !isTopMostEnabled)
            {
                return;
            }

            if (!windowService.IsTopMost)
            {
                windowService.SetTopMost(true);
            }
        }

        private void OnDestroy()
        {
            CancelCurrentLoad();
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

                await ApplyLocalAnimationPackageAsync(path, preferredEntryPath: path);
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

        [ContextMenu("Apply Example Desktop Move Behavior (StreamingAssets)")]
        public async void ApplyExampleDesktopMoveBehavior()
        {
            try
            {
                EnsureServices();
                await ApplyLocalBehaviorManifestAsync(GetExampleDesktopMoveBehaviorPath());
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
                await ApplyLocalAnimationPackageAsync(path, preferredEntryPath: path);
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
                await PlayBuiltInPoseOnceAsync(pose);
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
            if (!AllowClickThrough)
            {
                return;
            }

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

        [ContextMenu("Move To Sampled Desktop Location")]
        public async void MoveToSampledDesktopLocation()
        {
            try
            {
                var movementController = GetMovementController();
                if (!movementController.HasConfiguredMovementAnimation)
                {
                    await ApplyLocalBehaviorManifestAsync(GetExampleDesktopMoveBehaviorPath());
                    movementController = GetMovementController();
                }

                if (!movementController.HasConfiguredMovementLoopAnimation)
                {
                    throw new InvalidOperationException(
                        "Movement behavior is active, but no loop VRMA was resolved for movement.");
                }

                await movementController.MoveToSampledPointAsync();
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        private static string GetExampleDesktopMoveBehaviorPath()
        {
            var path = Path.GetFullPath(
                Path.Combine(Application.streamingAssetsPath, ExampleDesktopMoveBehaviorRelativePath));
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Example behavior manifest was not found.", path);
            }

            return path;
        }

        [ContextMenu("Clear Selected Model")]
        public void ClearSelectedModel()
        {
            EnsureServices();
            CancelCurrentLoad();

            if (currentModelRoot != null)
            {
                characterRuntimeAssembler!.Destroy(currentModelRoot);
                currentModelRoot = null;
            }

            CurrentModel = null;
            LastErrorMessage = null;
            selectedContentStore!.Clear();
            ModelCleared?.Invoke();
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
                windowService.MoveToMonitor(monitorIndex);
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
                }

                ModelLoaded?.Invoke(result);
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
            behaviorPackageInstaller ??= new BehaviorPackageInstaller();
            boundsService ??= new DesktopPetBoundsService();
            contentCatalog ??= new FileSystemContentCatalog();
            modelLibraryPaths ??= new ModelLibraryPaths();
            modelFingerprintService ??= new ModelFingerprintService();
            modelImportService ??= new ModelImportService(modelLibraryPaths, contentCatalog, modelFingerprintService);
            modelLoader ??= new VrmModelLoaderService(characterRuntimeAssembler);
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
        }

        private DesktopPetAnimationController GetAnimationController()
        {
            return GetComponent<DesktopPetAnimationController>();
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
                windowService.FitToMonitor(monitorIndex);
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

        private async Task PlayBuiltInPoseOnceAsync(BuiltInPoseOption pose, CancellationToken cancellationToken = default)
        {
            var posePath = GetBuiltInPosePath(pose);
            if (!File.Exists(posePath))
            {
                throw new FileNotFoundException("The built-in pose file does not exist.", posePath);
            }

            BuiltInPoseTriggered?.Invoke(pose.Id);

            var fallbackMotionController = GetFallbackMotionController();
            if (fallbackMotionController != null)
            {
                await fallbackMotionController.PlayPoseOnceAsync(posePath, cancellationToken);
                return;
            }

            await GetAnimationController().PlayPoseOnceAsync(posePath, cancellationToken);
        }

        private static BuiltInPoseOption FindRequiredBuiltInPose(string poseId)
        {
            return BuiltInPoseOptions.FirstOrDefault(pose =>
                       string.Equals(pose.Id, poseId, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException($"Built-in pose '{poseId}' was not found.");
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
