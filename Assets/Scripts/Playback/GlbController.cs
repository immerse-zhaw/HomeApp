using UnityEngine;
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

namespace Playback
{
        public class GlbController : MonoBehaviour
        {
        [Header("MXR Panel Reference")]
        [SerializeField] private GameObject mxrPanel;
        [SerializeField] private Transform modelRoot;
        [SerializeField] private Material pointsMaterial;

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

        private static int _pointSizePropId = Shader.PropertyToID("_PointSize");

        private GLTFSceneImporter currentModel;
        private Animation animationPlayer; // Found on instantiated model, if any
        private List<string> availableAnimations = new List<string>();
        private int currentAnimIndex = -1;
        private bool prevLeftSecondaryPressed = false;

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
        }

        private void Update()
        {
            // Handle Input for Animation Control (Y Button on Left Controller)
            if (HasActiveModel() && animationPlayer != null && availableAnimations.Count > 0)
            {
                var leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
                if (leftHand.isValid && leftHand.TryGetFeatureValue(CommonUsages.secondaryButton, out bool secondaryPressed))
                {
                    if (secondaryPressed && !prevLeftSecondaryPressed)
                    {
                        if (animationPlayer.isPlaying)
                        {
                            PauseAnimation();
                        }
                        else
                        {
                            PlayCurrentAnimation();
                        }
                    }
                    prevLeftSecondaryPressed = secondaryPressed;
                }
            }
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

            if (downloadProgress)
            {
                StartCoroutine(DownloadThenInstantiate(url));
            }
            else
            {
                _ = LoadAsync(url, () => Debug.Log("[GlbController] Model is ready."));
            }
        }

        public void CloseModel()
        {
            Debug.Log("[GlbController] Closing model.");
            ClearCurrentModel();
            // Reactivate the MXR panel when GLB is closed
            if (mxrPanel != null)
            {
                mxrPanel.SetActive(true);
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

            // Use exact combined scale for LOD computation (no high floor) so LOD matches user intent
            // Quantize userScale so LOD only updates on coarse steps to avoid continuous rebuilds
            float quantizedUserScale = QuantizeUserScale(userScale);
            float lodScale = autoScale * quantizedUserScale;
            if (lodScale <= 0f) lodScale = 1e-6f;
            if (Mathf.Abs(quantizedUserScale - lastAppliedQuantizedScale) > 1e-6f)
            {
                ApplyPointCloudLod(lodScale);
                lastAppliedQuantizedScale = quantizedUserScale;
            }

            // Keep point size simple and stable; downsampling reduces overdraw instead
            if (pointsMaterial != null && pointsMaterial.HasProperty(_pointSizePropId))
            {
                pointsMaterial.SetFloat(_pointSizePropId, defaultPointSize);
            }

            UpdateScaleUi(clamped);
            SetScaleUiVisible(scaleUiVisible); // respect current toggle but re-check model presence

            Debug.Log($"[GlbController] Set user scale to {clamped} (mapped: {mappedScale}). Combined scale: {autoScale * userScale}");
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
            if (currentModel != null)
            {
                currentModel.Dispose();
                currentModel = null;
            }

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

            UpdateScaleUi(currentScaleInput);
            SetScaleUiVisible(false);
            SetControlModeCardVisible(false);
            // Ensure instruction cards are hidden when model is cleared
            SetInstructionCardsActive(false);

            if (downloadProgress)
            {
                downloadProgress.value = 0f;
                downloadProgress.gameObject.SetActive(false);
            }
        }

        private async Task LoadAsync(string url, Action onReady)
        {
            currentModel?.Dispose();
            // Log the URL being streamed/loaded so we can identify the source file
            Debug.Log($"[GlbController] Streaming/Loading model from URL: {url}");
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
                if (animationControlPanel) animationControlPanel.SetActive(true);
                if (playPauseButton != null) playPauseButton.gameObject.SetActive(true);
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
                CachePointCloudMeshes();
                float initialQuant = QuantizeUserScale(userScale);
                ApplyPointCloudLod(autoScale * initialQuant);
                lastAppliedQuantizedScale = initialQuant;
                ApplyPointCloudMaterialIfNeeded();
                SetPointsSize(defaultPointSize);
            }
            else
            {
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
            foreach (var mr in modelRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh;
                if (mesh.GetTopology(0) != MeshTopology.Points) continue;

                // Make a copy so we never mutate the imported mesh asset
                var originalCopy = Instantiate(mesh);
                originalCopy.name = mesh.name + "_OriginalCopy";

                var working = new Mesh();
                working.name = mesh.name + "_WorkingLod";

                mf.sharedMesh = working;

                pointCloudMeshes.Add(new PointCloudMeshInfo
                {
                    Filter = mf,
                    Renderer = mr,
                    OriginalMesh = originalCopy,
                    WorkingMesh = working
                });
            }

            // Show total original points in debug console
            int totalPoints = 0;
            foreach (var pc in pointCloudMeshes)
                if (pc.OriginalMesh != null && pc.OriginalMesh.vertexCount > 0)
                    totalPoints += pc.OriginalMesh.vertexCount;
            Debug.Log($"[GlbController] Loaded point cloud(s) with {totalPoints} points");
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
        }

        private int ComputeTargetPointCount(int originalCount, float totalScale)
        {
            // Map userScale to a target point count with these rules:
            // - <=0.5x -> 50k (do not reduce further)
            // - 1x -> 100k
            // - 5x -> full originalCount
            // Interpolate smoothly between these breakpoints.
            float us = Mathf.Max(userScale, 0.001f);

            if (originalCount <= 0) return 0;

            int target;
            if (us <= 0.5f)
            {
                target = 50000;
            }
            else if (us <= 1f)
            {
                // Interpolate from 50k at 0.5x to 100k at 1x
                float t = (us - 0.5f) / 0.5f;
                target = Mathf.RoundToInt(Mathf.Lerp(50000f, 100000f, t));
            }
            else if (us <= 5f)
            {
                // Interpolate from 100k at 1x to full at 5x
                float t = (us - 1f) / 4f;
                target = Mathf.RoundToInt(Mathf.Lerp(100000f, (float)originalCount, t));
            }
            else
            {
                // At and above 5x: use full original count
                target = originalCount;
            }

            // Never exceed original count
            return Mathf.Clamp(target, 0, originalCount);
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
            if (downloadProgress)
            {
                downloadProgress.gameObject.SetActive(true);
                downloadProgress.value = 0f;
            }

            // Log the URL being downloaded/streamed so we can inspect the source
            Debug.Log($"[GlbController] Downloading model from URL: {url}");
            using (var uwr = UnityWebRequest.Get(url))
            {
                uwr.SendWebRequest();
                while (!uwr.isDone)
                {
                    if (downloadProgress) downloadProgress.value = uwr.downloadProgress;
                    yield return null;
                }

                if (uwr.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[GlbController] Download error: {uwr.error}");
                    if (downloadProgress)
                    {
                        downloadProgress.value = 0f;
                        downloadProgress.gameObject.SetActive(false);
                    }
                    yield break;
                }

                var data = uwr.downloadHandler.data;
                currentModel?.Dispose();
                var importOpt = new ImportOptions();
                var stream = new MemoryStream(data);
                currentModel = new GLTFSceneImporter(stream, importOpt);
                var loadTask = currentModel.LoadSceneAsync();
                while (!loadTask.IsCompleted) yield return null;
                stream.Dispose();
                var loaded = currentModel.LastLoadedScene;
                if (loaded == null)
                {
                    Debug.LogError("[GlbController] Failed to parse GLB.");
                    currentModel?.Dispose();
                    yield break;
                }

                loaded.transform.SetParent(modelRoot, false);
                FinalizeLoadedModel();
            }

            if (downloadProgress)
            {
                downloadProgress.value = 1f;
                downloadProgress.gameObject.SetActive(false);
            }

            Debug.Log("[GlbController] Model download complete. Model is ready.");
        }

        private void OnDestroy()
        {
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

        public void Inject(StateMachine sm, Playback.VideoController vc)
        {
            stateMachine = sm;
            videoController = vc;
            Debug.Log($"[GlbController] Injected StateMachine: {stateMachine != null}, VideoController: {videoController != null}");
        }

        // Refresh the point-cloud LOD to match the current transform scale immediately.
        // This is intended for external actions that set transform scale directly (e.g., a Reset button).
        public void RefreshLodFromTransform()
        {
            if (modelRoot == null) return;
            float compositeScale = modelRoot.localScale.x; // assume uniform scale is used
            if (autoScale <= 0f) autoScale = 1e-6f;
            float computedUserScale = compositeScale / autoScale;
            // Quantize according to rules and force-apply LOD
            float quant = QuantizeUserScale(computedUserScale);
            float lodScale = autoScale * quant;
            ApplyPointCloudLod(lodScale);
            lastAppliedQuantizedScale = quant;

            // Update internal userScale and slider UI to reflect new value
            userScale = quant;
            currentScaleInput = Mathf.Clamp(quant, minScale, maxScale);
            UpdateScaleUi(currentScaleInput);

            Debug.Log($"[GlbController] RefreshLodFromTransform -> composite:{compositeScale} computedUser:{computedUserScale} quant:{quant}");
        }
    }
}
