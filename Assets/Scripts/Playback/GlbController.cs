using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityGLTF;
using UnityGLTF.Loader;
using System.Threading.Tasks;
using System;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using App;
using Launcher;

namespace Playback
{
        public class GlbController : MonoBehaviour
        {
        [Header("MXR Panel Reference")]
        [SerializeField] private GameObject mxrPanel;
        [SerializeField] private Transform modelRoot;
        [SerializeField] private Material pointsMaterial;

        // MXR panel position restoration
        private Vector3 mxrPanelInitialPosition;
        private Quaternion mxrPanelInitialRotation;

        [Header("Spawn Settings")]
        [Tooltip("Camera Reference for Spawn")]
        [SerializeField] private Camera spawnCamera;
        [Tooltip("Local offset from the camera")]
        [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 0f, 2f);
        [Header("Download Progress")]
        [SerializeField] private Slider downloadProgress;

        [Header("Scale Controls")]
        [SerializeField] private Slider scaleSlider;
        [SerializeField] private TMP_Text scaleValueText;
        [SerializeField] private float minScale = 0.1f;
        [SerializeField] private float maxScale = 10f;
        [SerializeField] private string scaleValueFormat = "x{0:0.00}";

        [Header("Control Mode Card")]
        [SerializeField] private GameObject modeCard;
        [SerializeField] private TMP_Text modeCardText;

        [Header("Instruction Cards")]
        [Tooltip("Root GameObjects for UI instruction cards. These will be activated when a 3D model is being shown and deactivated otherwise.")]
        [SerializeField] private System.Collections.Generic.List<GameObject> instructionCards = new System.Collections.Generic.List<GameObject>();

        [Header("Animation Controls")]
        [SerializeField] private bool enableAnimationSelector = false;
        [SerializeField] private GameObject animationControlPanel;
        [SerializeField] private TMP_Text animationNameText;
        [SerializeField] private Button nextAnimButton;
        [SerializeField] private Button prevAnimButton;
        [SerializeField] private Button playPauseButton;
        [Tooltip("Image component on the play/pause button")]
        [SerializeField] private Image playPauseImage;
        [Tooltip("Play icon (shown when paused / None)")]
        [SerializeField] private Sprite playSprite;
        [Tooltip("Pause icon (shown when playing)")]
        [SerializeField] private Sprite pauseSprite;
        // Simple paused flag — true when playback is paused (None uses currentAnimIndex == -1)
        private bool isAnimationPaused = false;

        [Header("Point Rendering")]
        [SerializeField] private float defaultPointSize = 2.0f;         // Pixel size used by point shader
        [SerializeField] private string pointSizeProperty = "_PointSize"; // Change if your shader uses a different name
        [SerializeField, Min(10000)] private int maxPointCloudPoints = 75000;
        [SerializeField] private int[] pointCloudPointLimitsByScale =
        {
            300000,
            450000,
            600000,
            800000,
            1100000,
            1500000
        };
        [SerializeField] private float pointCloudLodResizeSettleSeconds = 0.35f;

        [Header("Point Cloud Quality")]
        [SerializeField, Range(1, 8)] private int defaultMsaaSamples = 8;
        [SerializeField, Range(1, 8)] private int pointCloudMsaaSamples = 2;

        private static int _pointSizePropId = Shader.PropertyToID("_PointSize");

        private GLTFSceneImporter currentModel;
        private Animation animationPlayer; // Found on instantiated model, if any
        private List<string> availableAnimations = new List<string>();
        private int currentAnimIndex = -1;
        private bool prevLeftPrimaryPressed = false;

        private float autoScale = 1f;
        private float userScale = 1f;
        private float lastAppliedQuantizedScale = -1f; // last quantized user scale used for point LOD updates
        private float currentScaleInput = 1f; // External/user-friendly scale (0.1x - 10x)
        private bool suppressScaleUiEvents = false;
        private readonly List<PointCloudMeshInfo> pointCloudMeshes = new List<PointCloudMeshInfo>();
        // Keep the URL of the model being loaded so we can report filename when ready
        private string currentModelUrl;
        private StateMachine stateMachine;
        private Playback.VideoController videoController;
        private bool scaleUiVisible = false;
        private bool pointCloudQualityActive = false;
        private int lastPointCloudTargetCount = -1;
        private Coroutine cleanupCoroutine;
        private Coroutine pointCloudLodCoroutine;
        private float pendingPointCloudLodScale = -1f;
        private bool isDestroying = false;

        // Expose model root so other scripts (e.g., GlbMover) can manipulate the loaded model.
        public Transform ModelRoot => modelRoot;

        public bool TryGetCameraSpawnPosition(out Vector3 position)
        {
            var cam = spawnCamera != null ? spawnCamera : Camera.main;
            if (cam == null)
            {
                position = modelRoot != null ? modelRoot.position : Vector3.zero;
                return false;
            }

            position = cam.transform.TransformPoint(cameraOffset);
            return true;
        }

        public bool HasActiveModel()
        {
            if (modelRoot == null || modelRoot.childCount == 0)
                return false;

            // If state machine isn't present (e.g., in isolated testing), allow interaction
            if (stateMachine == null)
                return true;

            return stateMachine.Current == AppState.ShowingModel;
        }

        public string GetModelStateDiagnostic()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"HasActiveModel={HasActiveModel()}");
            sb.Append($"; stateMachine={(stateMachine == null ? "NULL" : stateMachine.Current.ToString())}");
            sb.Append($"; modelRoot={(modelRoot == null ? "NULL" : "OK")}");
            sb.Append($"; childCount={(modelRoot == null ? 0 : modelRoot.childCount)}");
            return sb.ToString();
        }

        public void UpdateControlModeCard(bool isScaleMode)
        {
            if (modeCard == null) return;
            bool visible = HasActiveModel();
            modeCard.SetActive(visible);
            if (modeCardText != null)
            {
                // Show the current mode name: "Size" when in Scale mode, "Position" otherwise
                modeCardText.text = isScaleMode ? "Size" : "Position";
            }
        }

        public void SetControlModeCardVisible(bool visible)
        {
            if (modeCard == null) return;
            modeCard.SetActive(visible && HasActiveModel());
        }

        public void SetInstructionCardsActive(bool active)
        {
            if (instructionCards == null) return;
            bool canShow = active && HasActiveModel();
            foreach (var g in instructionCards)
            {
                if (g != null) g.SetActive(canShow);
            }
        }

        private class PointCloudMeshInfo
        {
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public Mesh OriginalMesh; // copied from loaded mesh; never mutated
            public Mesh WorkingMesh;  // assigned to filter; rebuilt per LOD
        }

        private void Awake()
        {
            InitializeScaleUi();
            SetScaleUiVisible(false);
            SetInstructionCardsActive(false);

            if (nextAnimButton) nextAnimButton.onClick.AddListener(NextAnimation);
            if (prevAnimButton) prevAnimButton.onClick.AddListener(PrevAnimation);
            if (playPauseButton) playPauseButton.onClick.AddListener(TogglePlayPause);
            // set a sane default icon in editor if the image & sprite are assigned
            if (playPauseImage != null && playSprite != null) playPauseImage.sprite = playSprite;
            if (animationControlPanel) animationControlPanel.SetActive(false);

            // Save initial MXR panel position for restoration
            if (mxrPanel != null)
            {
                mxrPanelInitialPosition = mxrPanel.transform.position;
                mxrPanelInitialRotation = mxrPanel.transform.rotation;
            }
        }

        private void Update()
        {
            if (enableAnimationSelector && HasActiveModel() && animationPlayer != null && availableAnimations.Count > 0)
            {
                var leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
                if (leftHand.isValid && leftHand.TryGetFeatureValue(CommonUsages.primaryButton, out bool primaryPressed))
                {
                    if (primaryPressed && !prevLeftPrimaryPressed)
                    {
                        ToggleAnimationPanelVisibility();
                    }
                    prevLeftPrimaryPressed = primaryPressed;
                }
                else
                {
                    prevLeftPrimaryPressed = false;
                }
            }
        }

        private void ToggleAnimationPanelVisibility()
        {
            if (animationControlPanel == null) return;
            animationControlPanel.SetActive(!animationControlPanel.activeSelf);
        }

        public void NextAnimation()
        {
            if (availableAnimations.Count == 0) return;

            // wheel: [None, anim0, anim1, ...]
            int total = availableAnimations.Count + 1;
            // map currentAnimIndex (-1..N-1) into virtual (0..N) and advance by one
            int virtualIndex = (currentAnimIndex + 2) % total; // simpler, no extra adjustments
            currentAnimIndex = (virtualIndex == 0) ? -1 : virtualIndex - 1;

            // If selection is an animation => play it immediately; if None => stop/reset
            if (currentAnimIndex >= 0)
            {
                PlayCurrentAnimation();
            }
            else
            {
                StopAnimation();
            }
        }

        public void PrevAnimation()
        {
            if (availableAnimations.Count == 0) return;

            int total = availableAnimations.Count + 1;
            int virtualIndex = (currentAnimIndex + 1) - 1;
            virtualIndex = (virtualIndex % total + total) % total;
            currentAnimIndex = (virtualIndex == 0) ? -1 : virtualIndex - 1;

            if (currentAnimIndex >= 0)
            {
                PlayCurrentAnimation();
            }
            else
            {
                StopAnimation();
            }
        }

        private void PlayCurrentAnimation()
        {
            if (animationPlayer == null || availableAnimations.Count == 0 || currentAnimIndex < -1) return;

            // default to first animation if coming from None
            if (currentAnimIndex < 0) currentAnimIndex = 0;

            string animName = availableAnimations[currentAnimIndex];
            
            // If already playing/paused on this clip, just ensure it's unpaused and playing
            if (animationPlayer.clip != null && animationPlayer.clip.name == animName)
            {
                var state = animationPlayer[animName];
                if (state != null) state.enabled = true;
                animationPlayer.Play(animName);
            }
            else
            {
                // Only reset/stop if switching to a brand new clip
                animationPlayer.Stop();
                animationPlayer.clip = animationPlayer.GetClip(animName);
                animationPlayer.Play(animName);
            }

            isAnimationPaused = false;
            stateMachine?.SetAction(animName);
            UpdateAnimationUI();
            UpdatePlayPauseUI(true);
        }

        private void UpdateAnimationUI()
        {
            if (animationNameText == null) return;

            if (availableAnimations.Count > 0 && currentAnimIndex >= 0)
            {
                var name = availableAnimations[currentAnimIndex];
                animationNameText.text = name + (isAnimationPaused ? " (Paused)" : "");
            }
            else if (availableAnimations.Count > 0 && currentAnimIndex < 0)
            {
                // 'None' state: show a neutral label
                animationNameText.text = "None";
            }
            else
            {
                animationNameText.text = "No Animation";
            }
        }

        public void LoadModel(string url, string name = null, string fileId = null)
        {
            Debug.Log($"[GlbController] Loading model from URL: {url}");
            FileLogger.Log($"[Model] Load requested name={name ?? "none"} fileId={fileId ?? "none"} url={ShortUrl(url)}");
            // Ensure video is stopped before loading a model
            videoController?.StopVideo();
            stateMachine?.SetState(AppState.Loading);
            stateMachine?.SetAction("none");
            stateMachine?.SetContent(name, fileId);
            StopAllCoroutines();
            ClearCurrentModel();
            // Keep this so we can report filename later
            currentModelUrl = url;

            // Deactivate the MXR panel when spawning a GLB object
            if (mxrPanel != null)
            {
                mxrPanel.SetActive(false);
            }

            StartCoroutine(DownloadThenInstantiate(url));
        }

        public void CloseModel()
        {
            Debug.Log("[GlbController] Closing model.");
            ClearCurrentModel();
            // Reactivate the MXR panel when GLB is closed
            if (mxrPanel != null)
            {
                mxrPanel.SetActive(true);
                RestoreMxrPanelPosition();
            }
            // Set state to Idle when model is closed
            if (stateMachine != null && stateMachine.Current == AppState.ShowingModel)
            {
                stateMachine.SetState(AppState.Idle);
            }
            stateMachine?.SetAction("none");
        }

        public void PlayAnimation(string animation)
        {
            if (animationPlayer == null)
            {
                Debug.LogWarning("[GlbController] No Animations found.");
                return;
            }
            animationPlayer.clip = animationPlayer.GetClip(animation);
            animationPlayer.Play();
            isAnimationPaused = false;
            stateMachine?.SetAction(animation);
            UpdatePlayPauseUI(true);
            Debug.Log($"[GlbController] Playing animation #{animation}.");
        }

        public void StopAnimation()
        {
            if (animationPlayer == null)
            {
                Debug.LogWarning("[GlbController] No Animations to stop.");
                return;
            }

            // Reset to first frame so the model returns to its original pose
            if (animationPlayer.clip != null)
            {
                animationPlayer[animationPlayer.clip.name].time = 0f;
                animationPlayer.Sample();
            }

            animationPlayer.Stop();
            // Enter the explicit 'None' selection state so UI and logic remain consistent
            currentAnimIndex = -1;
            isAnimationPaused = false;
            stateMachine?.SetAction("none");
            UpdateAnimationUI();
            UpdatePlayPauseUI(false);
            Debug.Log("[GlbController] Stopped animation (reset to first frame / None state).");
        }

        /// <summary>
        /// Pause playback but preserve the current animation time (do not reset to first frame).
        /// </summary>
        private void PauseAnimation()
        {
            if (animationPlayer == null || animationPlayer.clip == null) return;
            
            // Disable the state to pause exactly at current time
            var state = animationPlayer[animationPlayer.clip.name];
            if (state != null) state.enabled = false;

            isAnimationPaused = true;
            stateMachine?.SetAction(animationPlayer.clip.name);
            UpdateAnimationUI();
            UpdatePlayPauseUI(false);
            Debug.Log("[GlbController] Paused animation.");
        }

        /// <summary>
        /// Toggle play/pause from UI button.
        /// </summary>
        public void TogglePlayPause()
        {
            if (animationPlayer == null || availableAnimations.Count == 0)
            {
                // If no animation is loaded but clips exist (edge case), ensure UI shows Play
                UpdatePlayPauseUI(false);
                return;
            }

            if (animationPlayer.isPlaying)
            {
                PauseAnimation();
            }
            else
            {
                PlayCurrentAnimation();
            }
        }

        private void UpdatePlayPauseUI(bool isPlaying)
        {
            if (playPauseImage == null) return;
            if (isPlaying)
            {
                if (pauseSprite != null) playPauseImage.sprite = pauseSprite;
            }
            else
            {
                if (playSprite != null) playPauseImage.sprite = playSprite;
            }
        }


        public void SetPointsSize(float size)
        {
            if (pointsMaterial == null)
            {
                Debug.LogWarning("[GlbController] Points material not assigned.");
                return;
            }

            if (pointsMaterial.HasProperty(_pointSizePropId))
            {
                pointsMaterial.SetFloat(_pointSizePropId, size);
                Debug.Log($"[GlbController] Set point size to {size}.");
            }
            else
            {
                Debug.LogWarning($"[GlbController] Points material doesn't have _PointSize property.");
            }
        }

        private void ApplyPointCloudQuality()
        {
            SetMsaaSamples(pointCloudMsaaSamples);
            pointCloudQualityActive = true;
        }

        private void RestoreDefaultQuality()
        {
            if (!pointCloudQualityActive) return;

            SetMsaaSamples(defaultMsaaSamples);
            pointCloudQualityActive = false;
        }

        private static void SetMsaaSamples(int samples)
        {
            int normalized = NormalizeMsaaSamples(samples);
            QualitySettings.antiAliasing = normalized;

            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urpAsset)
            {
                urpAsset.msaaSampleCount = normalized;
            }
        }

        private static int NormalizeMsaaSamples(int samples)
        {
            if (samples >= 8) return 8;
            if (samples >= 4) return 4;
            if (samples >= 2) return 2;
            return 1;
        }

        public void SetScale(float s)
        {
            if (modelRoot == null)
            {
                Debug.LogWarning("[GlbController] modelRoot is not assigned.");
                return;
            }

            float clamped = Mathf.Clamp(s, minScale, maxScale);
            currentScaleInput = clamped;
            // Remap external scale: linear mapping so slider travel is symmetric and constant-speed
            float mappedScale = 1f;
            if (clamped <= 1.0f)
            {
                float t = (clamped - minScale) / (1f - minScale);
                t = Mathf.Clamp01(t);
                mappedScale = Mathf.Lerp(minScale, 1f, t);
            }
            else
            {
                float t = (clamped - 1f) / (maxScale - 1f);
                t = Mathf.Clamp01(t);
                mappedScale = Mathf.Lerp(1f, maxScale, t);
            }
            userScale = Mathf.Max(0f, mappedScale);
            ApplyCompositeScale();

            float lodScale = autoScale * userScale;
            if (lodScale <= 0f) lodScale = 1e-6f;
            SchedulePointCloudLod(lodScale);
            lastAppliedQuantizedScale = QuantizeUserScale(userScale);

            // Keep point size simple and stable; downsampling reduces overdraw instead
            if (pointsMaterial != null && pointsMaterial.HasProperty(_pointSizePropId))
            {
                pointsMaterial.SetFloat(_pointSizePropId, defaultPointSize);
            }

            UpdateScaleUi(clamped);
            SetScaleUiVisible(scaleUiVisible); // respect current toggle but re-check model presence
        }

        public void AdjustScale(float delta)
        {
            // Apply changes in normalized slider space so travel speed is symmetric around 1x
            float norm = MapScaleToNormalized(currentScaleInput);
            norm = Mathf.Clamp01(norm + delta);
            float target = MapNormalizedToScale(norm);
            SetScale(target);
        }

        public void SetScaleUiVisible(bool shouldShow)
        {
            scaleUiVisible = shouldShow;
            bool canShow = shouldShow && HasActiveModel();

            if (scaleSlider != null)
            {
                scaleSlider.gameObject.SetActive(canShow);
            }

            if (scaleValueText != null)
            {
                scaleValueText.gameObject.SetActive(canShow);
            }
        }

        private void InitializeScaleUi()
        {
            if (scaleSlider != null)
            {
                suppressScaleUiEvents = true;
                // Use a normalized 0..1 slider and map non-linearly to the 0.1..10 scale so
                // lower-than-1 changes are slightly slower and above-1 changes are slightly faster.
                scaleSlider.minValue = 0f;
                scaleSlider.maxValue = 1f;
                float norm = MapScaleToNormalized(currentScaleInput);
                scaleSlider.SetValueWithoutNotify(norm);
                scaleSlider.onValueChanged.AddListener(OnScaleSliderChanged);
                suppressScaleUiEvents = false;
            }

            UpdateScaleUiLabel(currentScaleInput);
            SetScaleUiVisible(false);
        }

        private void UpdateScaleUi(float value)
        {
            if (scaleSlider != null)
            {
                suppressScaleUiEvents = true;
                float norm = MapScaleToNormalized(value);
                scaleSlider.SetValueWithoutNotify(norm);
                suppressScaleUiEvents = false;
            }

            UpdateScaleUiLabel(value);
        }

        private void UpdateScaleUiLabel(float value)
        {
            if (scaleValueText != null)
            {
                scaleValueText.text = string.Format(scaleValueFormat, value);
            }
        }

        private void OnScaleSliderChanged(float value)
        {
            if (suppressScaleUiEvents) return;
            // Slider is normalized (0..1); map to actual scale with piecewise easing so
            // below 1x is slower and above 1x is faster.
            float mapped = MapNormalizedToScale(value);
            SetScale(mapped);
        }

        // Map normalized slider (0..1) to actual scale in [minScale,maxScale]
        private float MapNormalizedToScale(float n)
        {
            n = Mathf.Clamp01(n);
            if (n <= 0.5f)
            {
                float t = n / 0.5f; // 0..1
                return Mathf.Lerp(minScale, 1f, t);
            }
            else
            {
                float t = (n - 0.5f) / 0.5f; // 0..1
                return Mathf.Lerp(1f, maxScale, t);
            }
        }

        // Map actual scale back to normalized slider position
        private float MapScaleToNormalized(float s)
        {
            s = Mathf.Clamp(s, minScale, maxScale);
            if (s <= 1f)
            {
                float t = (s - minScale) / (1f - minScale);
                t = Mathf.Clamp01(t);
                return t * 0.5f;
            }
            else
            {
                float t = (s - 1f) / (maxScale - 1f);
                t = Mathf.Clamp01(t);
                return 0.5f + 0.5f * t;
            }
        }

        private void ClearCurrentModel()
        {
            bool hadModel = modelRoot != null && modelRoot.childCount > 0;
            float startedAt = Time.realtimeSinceStartup;
            RestoreDefaultQuality();

            if (currentModel != null)
            {
                currentModel.Dispose();
                currentModel = null;
            }

            if (pointCloudLodCoroutine != null)
            {
                StopCoroutine(pointCloudLodCoroutine);
                pointCloudLodCoroutine = null;
            }
            pendingPointCloudLodScale = -1f;
            pointCloudMeshes.Clear();

            animationPlayer = null;

            if (modelRoot != null)
            {
                for (int i = modelRoot.childCount - 1; i >= 0; i--)
                {
                    Destroy(modelRoot.GetChild(i).gameObject);
                }

                RemovePipelineComponents();
                modelRoot.localScale = Vector3.one;
            }

            autoScale = 1f;
            userScale = 1f;
            currentScaleInput = 1f;
            lastAppliedQuantizedScale = -1f;
            lastPointCloudTargetCount = -1;

            UpdateScaleUi(currentScaleInput);
            SetScaleUiVisible(false);
            SetControlModeCardVisible(false);
            if (animationControlPanel) animationControlPanel.SetActive(false);
            if (playPauseButton != null) playPauseButton.gameObject.SetActive(false);
            // Ensure instruction cards are hidden when model is cleared
            SetInstructionCardsActive(false);

            if (downloadProgress)
            {
                downloadProgress.value = 0f;
                downloadProgress.gameObject.SetActive(false);
            }

            if (hadModel)
                FileLogger.Log($"[Model] Cleared model elapsed={(Time.realtimeSinceStartup - startedAt):0.00}s");

            if (!isDestroying && hadModel)
                ScheduleUnusedAssetCleanup();
        }

        private async Task LoadAsync(string url, Action onReady)
        {
            float startedAt = Time.realtimeSinceStartup;
            currentModel?.Dispose();
            // Log the URL being streamed/loaded so we can identify the source file
            FileLogger.Log($"[Model] Import begin source={url}");
            var uriDir = url.Substring(0, url.LastIndexOf('/') + 1);
            var fileName = Path.GetFileName(url);
            var importOpt = new ImportOptions
            {
                DataLoader = new UnityWebRequestLoader(uriDir)
            };
            currentModel = new GLTFSceneImporter(fileName, importOpt);
            await currentModel.LoadSceneAsync();
            var loaded = currentModel.LastLoadedScene;
            if (loaded == null)
            {
                Debug.LogError("[GlbController] Failed to load model.");
                return;
            }

            Debug.Log("[GlbController] Model loaded successfully.");
            loaded.transform.SetParent(modelRoot, false);
            FinalizeLoadedModel();
            FileLogger.Log($"[Model] Import complete elapsed={(Time.realtimeSinceStartup - startedAt):0.00}s childCount={(modelRoot != null ? modelRoot.childCount : 0)} pointMeshes={pointCloudMeshes.Count}");

            onReady?.Invoke();
        }

        private void FinalizeLoadedModel()
        {
            if (modelRoot == null) return;

            var cam = spawnCamera != null ? spawnCamera : Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[GlbController] No spawn camera found. Using existing modelRoot position.");
            }
            else
            {
                modelRoot.position = cam.transform.TransformPoint(cameraOffset);
            }
            ScaleModelToUnitCube();

            animationPlayer = modelRoot.GetComponentInChildren<Animation>(true);

            // Populate animation list
            availableAnimations.Clear();
            currentAnimIndex = -1; // default to 'None' so model spawns un-animated (first-frame preview)
            if (animationPlayer != null)
            {
                foreach (AnimationState state in animationPlayer)
                {
                    availableAnimations.Add(state.name);
                }
            }

            if (availableAnimations.Count > 0)
            {
                // Do NOT auto-play: show first-frame preview and expose controls
                if (animationPlayer != null)
                {
                    // set preview clip to the first animation and sample its first frame
                    animationPlayer.clip = animationPlayer.GetClip(availableAnimations[0]);
                    if (animationPlayer.clip != null)
                    {
                        animationPlayer[animationPlayer.clip.name].time = 0f;
                        animationPlayer.Sample();
                    }
                }

                isAnimationPaused = false;
                if (animationControlPanel) animationControlPanel.SetActive(false);
                if (playPauseButton != null) playPauseButton.gameObject.SetActive(enableAnimationSelector);
            }
            else
            {
                if (animationControlPanel) animationControlPanel.SetActive(false);
                if (playPauseButton != null) playPauseButton.gameObject.SetActive(false);
            }

            UpdateAnimationUI();
            UpdatePlayPauseUI(false);

            // Apply point-cloud material when the mesh topology is already points
            bool hasPointTopology = HasPointTopologyInChildren(modelRoot);
            
            // Send simple status over websocket indicating what is playing (point cloud vs 3D object)


            if (hasPointTopology)
            {
                ApplyPointCloudQuality();
                CachePointCloudMeshes();
                float initialQuant = QuantizeUserScale(userScale);
                ApplyPointCloudLod(autoScale * initialQuant);
                lastAppliedQuantizedScale = initialQuant;
                ApplyPointCloudMaterialIfNeeded();
                SetPointsSize(defaultPointSize);
            }
            else
            {
                RestoreDefaultQuality();
                SetupGlbPipeline(modelRoot);
            }

            if (stateMachine != null)
            {
                Debug.Log($"[GlbController] Setting state to ShowingModel. Current state: {stateMachine.Current}");
                stateMachine.SetState(AppState.ShowingModel);
                stateMachine.SetAction("none");
                // Keep scale UI hidden until user toggles size mode, but ensure visibility checks know a model exists
                SetScaleUiVisible(scaleUiVisible);
                // Update mode card to reflect current control mode (default is Position)
                UpdateControlModeCard(false);
                // Show instruction cards when a 3D model is shown
                SetInstructionCardsActive(true);
            }
            else
            {
                Debug.LogError("[GlbController] StateMachine is NULL! Cannot set state to ShowingModel.");
            }
        }

        private void ScaleModelToUnitCube()
        {
            var renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = default;
            bool hasBounds = false;

            foreach (var r in renderers)
            {
                if (r == null || r.transform == modelRoot) continue; // Ignore helper renderer on root

                if (!hasBounds)
                {
                    bounds = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            if (!hasBounds) return;

            float maxDim = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (maxDim <= 0f) return;

            autoScale = 1f / maxDim;
            ApplyCompositeScale();
            Debug.Log($"[GlbController] Scaled model to fit inside 1x1x1 cube. Auto scale: {autoScale}");
        }

        private void RemovePipelineComponents()
        {
            var anim = modelRoot.GetComponent<Animation>();
            if (anim) Destroy(anim);

            var grab = modelRoot.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            if (grab) Destroy(grab);

            var meshRenderer = modelRoot.GetComponent<MeshRenderer>();
            if (meshRenderer) Destroy(meshRenderer);

            var meshFilter = modelRoot.GetComponent<MeshFilter>();
            if (meshFilter) Destroy(meshFilter);

            var collider = modelRoot.GetComponent<Collider>();
            if (collider) Destroy(collider);

            var rigidbody = modelRoot.GetComponent<Rigidbody>();
            if (rigidbody) Destroy(rigidbody);
        }

        private void ApplyCompositeScale()
        {
            if (modelRoot == null) return;

            modelRoot.localScale = Vector3.one * (autoScale * userScale);
        }

        private void ApplyPointCloudMaterialIfNeeded()
        {
            if (pointsMaterial == null) return;

            foreach (var r in modelRoot.GetComponentsInChildren<Renderer>(true))
            {
                var mesh = (r as MeshRenderer)?.GetComponent<MeshFilter>()?.sharedMesh
                           ?? (r as SkinnedMeshRenderer)?.sharedMesh;

                if (mesh != null && mesh.GetTopology(0) == MeshTopology.Points)
                {
                    r.sharedMaterial = pointsMaterial;
                }
            }
        }

        private void CachePointCloudMeshes()
        {
            pointCloudMeshes.Clear();
            lastPointCloudTargetCount = -1;
            foreach (var mr in modelRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh;
                if (mesh.GetTopology(0) != MeshTopology.Points) continue;

                var working = new Mesh();
                working.name = mesh.name + "_WorkingLod";
                working.MarkDynamic();

                mf.sharedMesh = working;

                pointCloudMeshes.Add(new PointCloudMeshInfo
                {
                    Filter = mf,
                    Renderer = mr,
                    OriginalMesh = mesh,
                    WorkingMesh = working
                });
            }

            // Show total original points in debug console
            int totalPoints = 0;
            foreach (var pc in pointCloudMeshes)
                if (pc.OriginalMesh != null && pc.OriginalMesh.vertexCount > 0)
                    totalPoints += pc.OriginalMesh.vertexCount;
            Debug.Log($"[GlbController] Loaded point cloud(s) with {totalPoints} points");
            FileLogger.Log($"[Model] Point cloud meshes={pointCloudMeshes.Count} originalPoints={totalPoints} limits=[{string.Join(",", pointCloudPointLimitsByScale)}]");
        }

        private void ApplyPointCloudLod(float totalScale)
        {
            if (pointCloudMeshes.Count == 0) return;
            // Compute global target across all point-cloud meshes so totals match requested budget
            int totalOriginal = 0;
            foreach (var pc in pointCloudMeshes)
            {
                if (pc.OriginalMesh != null) totalOriginal += pc.OriginalMesh.vertexCount;
            }
            if (totalOriginal == 0) return;

            int globalTarget = ComputeTargetPointCount(totalOriginal, totalScale);
            if (globalTarget == lastPointCloudTargetCount)
                return;

            lastPointCloudTargetCount = globalTarget;

            // Allocate per-mesh targets proportionally, then adjust to match globalTarget exactly
            var perMeshTargets = new int[pointCloudMeshes.Count];
            int allocated = 0;
            for (int i = 0; i < pointCloudMeshes.Count; i++)
            {
                var pc = pointCloudMeshes[i];
                int orig = pc.OriginalMesh != null ? pc.OriginalMesh.vertexCount : 0;
                if (orig == 0) { perMeshTargets[i] = 0; continue; }
                perMeshTargets[i] = Mathf.Max(256, Mathf.RoundToInt(orig / (float)totalOriginal * globalTarget));
                perMeshTargets[i] = Mathf.Min(perMeshTargets[i], orig);
                allocated += perMeshTargets[i];
            }

            // Fix allocation rounding errors: distribute remainder
            int remainder = globalTarget - allocated;
            int idx = 0;
            while (remainder > 0)
            {
                if (idx >= pointCloudMeshes.Count) idx = 0;
                var pc = pointCloudMeshes[idx];
                int orig = pc.OriginalMesh != null ? pc.OriginalMesh.vertexCount : 0;
                if (orig > perMeshTargets[idx])
                {
                    perMeshTargets[idx]++;
                    remainder--;
                }
                idx++;
            }

            int totalNewPoints = 0;
            for (int i = 0; i < pointCloudMeshes.Count; i++)
            {
                var pc = pointCloudMeshes[i];
                var src = pc.OriginalMesh;
                var dst = pc.WorkingMesh;
                if (src == null || dst == null) continue;
                if (pc.Filter != null && pc.Filter.sharedMesh != dst)
                    pc.Filter.sharedMesh = dst;

                var verts = src.vertices;
                if (verts == null || verts.Length == 0) continue;

                int targetCount = perMeshTargets[i];
                targetCount = Mathf.Clamp(targetCount, 256, verts.Length);
                // Evenly sample to get close to the requested targetCount. This produces a smooth,
                // monotonic progression up to the requested budget (or original vertex count).
                int desired = Mathf.Min(targetCount, verts.Length);

                var outVerts = new Vector3[desired];
                var srcColors = src.colors;
                var outColors = (srcColors != null && srcColors.Length > 0) ? new Color[desired] : null;
                var srcUV = src.uv;
                var outUV = (srcUV != null && srcUV.Length > 0) ? new Vector2[desired] : null;

                for (int k = 0; k < desired; k++)
                {
                    int srcIdx = Mathf.Min(verts.Length - 1, Mathf.FloorToInt(k * (verts.Length / (float)desired)));
                    outVerts[k] = verts[srcIdx];
                    if (outColors != null && srcIdx < srcColors.Length) outColors[k] = srcColors[srcIdx];
                    if (outUV != null && srcIdx < srcUV.Length) outUV[k] = srcUV[srcIdx];
                }

                dst.Clear();
                dst.vertices = outVerts;
                if (outColors != null) dst.colors = outColors;
                if (outUV != null) dst.uv = outUV;

                var indices = new int[desired];
                for (int k = 0; k < desired; k++) indices[k] = k;
                dst.SetIndices(indices, MeshTopology.Points, 0);
                dst.RecalculateBounds();

                totalNewPoints += desired;
            }

            Debug.Log($"[GlbController] Current point cloud LOD: {totalNewPoints} points");
            FileLogger.Log($"[Model] Point LOD target={totalNewPoints}/{totalOriginal} scale={userScale:0.##} mode=sampled");
        }

        private void SchedulePointCloudLod(float lodScale)
        {
            if (pointCloudMeshes.Count == 0) return;

            pendingPointCloudLodScale = lodScale;

            if (pointCloudLodCoroutine != null)
                StopCoroutine(pointCloudLodCoroutine);

            pointCloudLodCoroutine = StartCoroutine(ApplyPointCloudLodAfterResizeSettles());
        }

        private IEnumerator ApplyPointCloudLodAfterResizeSettles()
        {
            yield return new WaitForSecondsRealtime(pointCloudLodResizeSettleSeconds);

            float lodScale = pendingPointCloudLodScale;
            pointCloudLodCoroutine = null;
            pendingPointCloudLodScale = -1f;
            ApplyPointCloudLod(lodScale);
        }

        private int ComputeTargetPointCount(int originalCount, float totalScale)
        {
            float us = Mathf.Max(userScale, 0.001f);

            if (originalCount <= 0) return 0;

            int target = GetPointCloudLimitForScale(us);

            // Never exceed original count
            return Mathf.Clamp(target, 0, originalCount);
        }

        private int GetPointCloudLimitForScale(float scale)
        {
            if (pointCloudPointLimitsByScale == null || pointCloudPointLimitsByScale.Length == 0)
                return maxPointCloudPoints;

            int index = Mathf.Clamp(Mathf.FloorToInt(scale + 0.0001f) - 1, 0, pointCloudPointLimitsByScale.Length - 1);
            int configured = pointCloudPointLimitsByScale[index];
            return Mathf.Max(10000, configured);
        }

        // Quantize userScale for LOD updates to reduce frequency of heavy point-cloud rebuilds:
        // - when userScale <= 1, round to nearest 0.1
        // - when userScale > 1, quantize to integer steps (1,2,3,...)
        private float QuantizeUserScale(float us)
        {
            if (us <= 1f)
            {
                // Quantize to nearest 0.1 for values <= 1.0
                float q = Mathf.Round(us * 10f) / 10f;
                return Mathf.Clamp(q, minScale, 1f);
            }
            else
            {
                // Quantize to integer steps (floor) for values > 1.0 so e.g. 8.1..8.9 -> 8
                float q = Mathf.Floor(us);
                return Mathf.Clamp(q, 1f, maxScale);
            }
        }

        private static bool HasPointTopologyInChildren(Transform root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mesh = (r as MeshRenderer)?.GetComponent<MeshFilter>()?.sharedMesh
                           ?? (r as SkinnedMeshRenderer)?.sharedMesh;

                if (mesh != null && mesh.GetTopology(0) == MeshTopology.Points)
                    return true;
            }
            return false;
        }

        private void SetupGlbPipeline(Transform root)
        {
            // Add Rigidbody (ensure kinematic so released objects don't fly away)
            var rigidbody = root.GetComponent<Rigidbody>();
            if (rigidbody == null)
            {
                rigidbody = root.gameObject.AddComponent<Rigidbody>();
                rigidbody.useGravity = false;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
            }
            rigidbody.isKinematic = true;
            rigidbody.linearVelocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;

            // Add Collider
            if (!root.TryGetComponent<Collider>(out _))
            {
                Mesh mesh = null;
                var meshFilter = root.GetComponentInChildren<MeshFilter>();
                if (meshFilter != null)
                    mesh = meshFilter.sharedMesh;

                if (mesh != null)
                {
                    var meshCollider = root.gameObject.AddComponent<MeshCollider>();
                    meshCollider.sharedMesh = mesh;
                    meshCollider.convex = true;
                }
                else
                {
                    var boxCollider = root.gameObject.AddComponent<BoxCollider>();
                    var renderers = root.GetComponentsInChildren<Renderer>();
                    Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(root.position, Vector3.zero);
                    foreach (var rr in renderers)
                        bounds.Encapsulate(rr.bounds);
                    boxCollider.center = bounds.center;
                    boxCollider.size = bounds.size;
                }
            }

            // Add XRGrabInteractable
            var grabbable = root.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>()
                            ?? root.gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            // Use Kinematic movement so objects remain in place when released
            grabbable.movementType = UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable.MovementType.Kinematic;
            grabbable.throwOnDetach = true;

            // Add Editable tag
            root.gameObject.tag = "Editable";

            // Shader assignment happens during import via UnityGLTF.
        }

        // Handle GET. Show network download progress, then load bytes via GLTFast
        private IEnumerator DownloadThenInstantiate(string url)
        {
            float totalStartedAt = Time.realtimeSinceStartup;
            if (downloadProgress)
            {
                downloadProgress.gameObject.SetActive(true);
                downloadProgress.value = 0f;
            }

            string localPath = ContentCache.GetCachedPath("models", stateMachine?.CurrentContentFileId, url, ".glb");
            if (!File.Exists(localPath) || new FileInfo(localPath).Length <= 0)
            {
                string tempPath = ContentCache.GetTempPath(localPath);
                float downloadStartedAt = Time.realtimeSinceStartup;
                FileLogger.Log($"[Model] Cache miss; downloading url={ShortUrl(url)}");
                using (var uwr = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
                {
                    uwr.downloadHandler = new DownloadHandlerFile(tempPath);
                    uwr.SendWebRequest();
                    while (!uwr.isDone)
                    {
                        if (downloadProgress) downloadProgress.value = uwr.downloadProgress;
                        yield return null;
                    }

                    if (uwr.result != UnityWebRequest.Result.Success)
                    {
                        FileLogger.LogWarning($"[Model] Download failed error={uwr.error} url={ShortUrl(url)}");
                        if (downloadProgress)
                        {
                            downloadProgress.value = 0f;
                            downloadProgress.gameObject.SetActive(false);
                        }
                        yield break;
                    }
                }

                if (File.Exists(localPath)) File.Delete(localPath);
                File.Move(tempPath, localPath);
                FileLogger.Log($"[Model] Download complete path={localPath} size={GetFileSizeLabel(localPath)} elapsed={(Time.realtimeSinceStartup - downloadStartedAt):0.00}s");
            }
            else
            {
                FileLogger.Log($"[Model] Cache hit path={localPath} size={GetFileSizeLabel(localPath)}");
            }

            var loadTask = LoadAsync("file://" + localPath.Replace("\\", "/"), () => Debug.Log("[GlbController] Model is ready."));
            while (!loadTask.IsCompleted) yield return null;

            if (downloadProgress)
            {
                downloadProgress.value = 1f;
                downloadProgress.gameObject.SetActive(false);
            }

            FileLogger.Log($"[Model] Load flow complete elapsed={(Time.realtimeSinceStartup - totalStartedAt):0.00}s");
        }

        private void OnDestroy()
        {
            isDestroying = true;
            if (cleanupCoroutine != null)
                StopCoroutine(cleanupCoroutine);
            ClearCurrentModel();

            if (scaleSlider != null)
            {
                scaleSlider.onValueChanged.RemoveListener(OnScaleSliderChanged);
            }
            // Ensure card hidden when controller is gone
            if (modeCard != null)
            {
                modeCard.SetActive(false);
            }

            // Hide instruction cards
            SetInstructionCardsActive(false);
        }

        private void ScheduleUnusedAssetCleanup()
        {
            if (cleanupCoroutine != null)
                StopCoroutine(cleanupCoroutine);
            cleanupCoroutine = StartCoroutine(UnloadUnusedAssetsSoon());
        }

        private IEnumerator UnloadUnusedAssetsSoon()
        {
            float startedAt = Time.realtimeSinceStartup;
            FileLogger.Log("[Model] UnloadUnusedAssets begin");
            yield return null;
            yield return Resources.UnloadUnusedAssets();
            GC.Collect();
            FileLogger.Log($"[Model] UnloadUnusedAssets complete elapsed={(Time.realtimeSinceStartup - startedAt):0.00}s");
            cleanupCoroutine = null;
        }

        private static string ShortUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "none";
            return url.Length <= 96 ? url : url.Substring(0, 93) + "...";
        }

        private static string GetFileSizeLabel(string path)
        {
            try
            {
                if (!File.Exists(path)) return "missing";
                return $"{new FileInfo(path).Length / (1024f * 1024f):0.0}MB";
            }
            catch
            {
                return "unknown";
            }
        }

        public void Inject(StateMachine sm, Playback.VideoController vc)
        {
            stateMachine = sm;
            videoController = vc;
            Debug.Log($"[GlbController] Injected StateMachine: {stateMachine != null}, VideoController: {videoController != null}");
        }

        private void RestoreMxrPanelPosition()
        {
            if (mxrPanel == null) return;
            mxrPanel.transform.position = mxrPanelInitialPosition;
            mxrPanel.transform.rotation = mxrPanelInitialRotation;
        }

        // Refresh the point-cloud LOD to match the current transform scale immediately.
        // This is intended for external actions that set transform scale directly (e.g., a Reset button).
        public void RefreshLodFromTransform()
        {
            if (modelRoot == null) return;
            float compositeScale = modelRoot.localScale.x; // assume uniform scale is used
            if (autoScale <= 0f) autoScale = 1e-6f;
            float computedUserScale = compositeScale / autoScale;
            float clampedUserScale = Mathf.Clamp(computedUserScale, minScale, maxScale);
            float quant = QuantizeUserScale(clampedUserScale);
            float lodScale = autoScale * clampedUserScale;
            if (pointCloudLodCoroutine != null)
            {
                StopCoroutine(pointCloudLodCoroutine);
                pointCloudLodCoroutine = null;
                pendingPointCloudLodScale = -1f;
            }
            ApplyPointCloudLod(lodScale);
            lastAppliedQuantizedScale = quant;

            // Update internal userScale and slider UI to reflect new value
            userScale = clampedUserScale;
            currentScaleInput = clampedUserScale;
            UpdateScaleUi(currentScaleInput);

            Debug.Log($"[GlbController] RefreshLodFromTransform -> composite:{compositeScale} computedUser:{computedUserScale} appliedUser:{clampedUserScale} quant:{quant}");
        }
    }
}
