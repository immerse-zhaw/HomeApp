using UnityEngine;
using UnityEngine.Video;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.UI;
using TMPro;
using System.IO;
using App;

namespace Playback
{
        public class VideoController : MonoBehaviour
        {
            [Header("MXR Panel Reference")]
            [SerializeField] private GameObject mxrPanel;
        [Header("Components")]
        [SerializeField] private VideoPlayer videoPlayer;

        [Header("Skybox Materials")]
        [SerializeField] private Material skyboxDefault;
        [SerializeField] private Material skyboxEquirect;
        [SerializeField] private Material skyboxCubemap;
        
        [Header("Floor")]
        [SerializeField] private Renderer floorRenderer;

        [Header("AR Components")]
        [SerializeField] private ARCameraManager arCameraManager;
        [SerializeField] private Camera mainCamera;

        [Header("Player Control UI")]
        [SerializeField] private GameObject controlPanel;
        [SerializeField] private Slider seekSlider;
        [SerializeField] private Button playPauseButton;
        [SerializeField] private Button forwardButton;
        [SerializeField] private Button backwardButton;
        [SerializeField] private Image playPauseIcon;
        [SerializeField] private Sprite playSprite;
        [SerializeField] private Sprite pauseSprite;
        [SerializeField] private TextMeshProUGUI timeText;

        private bool isPlaying = false;
        private bool isDragging = false;
        private bool prevLeftPrimaryPressed = false; // for edge-detecting left primary button presses

        // Injected references
        private StateMachine stateMachine;
        private GlbController glbController;

        public void Inject(StateMachine sm, GlbController gc)
        {
            stateMachine = sm;
            glbController = gc;
        }

        public void Awake()
        {
            RenderSettings.skybox = skyboxDefault; 
            SetFloorAlpha(0f);
            SetupUI();
            SetControlPanelVisibility(false);
            // Ensure pause icon is shown at start (video is playing by default)
            if (playPauseIcon != null && pauseSprite != null)
            {
                playPauseIcon.sprite = pauseSprite;
            }
        }

        private void SetupUI()
        {
            if (playPauseButton) playPauseButton.onClick.AddListener(TogglePlayPause);
            if (forwardButton) forwardButton.onClick.AddListener(Forward10s);
            if (backwardButton) backwardButton.onClick.AddListener(Backward10s);
            if (seekSlider) 
            {
                seekSlider.onValueChanged.AddListener(OnSliderValueChanged);
                seekSlider.minValue = 0f;
                seekSlider.maxValue = 1f;
            }
        }

        private void Update()
        {
            if (videoPlayer.isPrepared)
            {
                // Update slider only when not dragging
                if (!isDragging && seekSlider)
                {
                    float progress = (float)(videoPlayer.time / videoPlayer.length);
                    seekSlider.SetValueWithoutNotify(progress);
                }

                // Update time text
                UpdateTimeText();
            }

            // Left-hand primary button toggles the control panel when a video is playing
            if (controlPanel != null && isPlaying)
            {
                var leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
                if (leftHand.isValid && leftHand.TryGetFeatureValue(CommonUsages.primaryButton, out bool primaryPressed))
                {
                    if (primaryPressed && !prevLeftPrimaryPressed)
                    {
                        bool visible = controlPanel.activeSelf;
                        SetControlPanelVisibility(!visible);
                        Debug.Log($"[VideoController] Toggled control panel -> {!visible} via left primary button.");
                    }
                    prevLeftPrimaryPressed = primaryPressed;
                }
            }
        }

        private void UpdateTimeText()
        {
            if (timeText && videoPlayer.isPrepared)
            {
                string currentTime = FormatTime(videoPlayer.time);
                string totalTime = FormatTime(videoPlayer.length);
                timeText.text = $"{currentTime} / {totalTime}";
            }
        }

        private string FormatTime(double timeInSeconds)
        {
            int minutes = Mathf.FloorToInt((float)timeInSeconds / 60f);
            int seconds = Mathf.FloorToInt((float)timeInSeconds % 60f);
            return $"{minutes}:{seconds:00}";
        }

        public void PlayVideo(string url, string mapping, string projection, string stereo, string name = null, string fileId = null)
        {   
            // Ensure GLB content is closed before playing video
            if (glbController != null)
            {
                glbController.CloseModel();

                // Double-check no children remain under model root
                var root = glbController.ModelRoot;
                if (root != null && root.childCount > 0)
                {
                    for (int i = root.childCount - 1; i >= 0; i--)
                    {
                        Destroy(root.GetChild(i).gameObject);
                    }
                }
            }

            SetFloorAlpha(0.0f);
            videoPlayer.url = url;
            videoPlayer.Play();
            isPlaying = true;
            UpdatePlayPauseIcon();
            SetControlPanelVisibility(true);

            stateMachine?.SetState(AppState.PlayingVideo);
            stateMachine?.SetAction("playing");
            stateMachine?.SetContent(name, fileId);

            // Deactivate the MXR panel when video is playing
            if (mxrPanel != null)
            {
                mxrPanel.SetActive(false);
            }

            // Disable AR passthrough and set camera to render skybox
            if (arCameraManager) arCameraManager.enabled = false;
            if (mainCamera) mainCamera.clearFlags = CameraClearFlags.Skybox;

            bool useCube = mapping.ToLower().Contains("cube");
            RenderSettings.skybox = useCube ? skyboxCubemap : skyboxEquirect;

            if (videoPlayer.targetTexture)
                RenderSettings.skybox.SetTexture("_MainTex", videoPlayer.targetTexture);

            RenderSettings.skybox.SetInt("_ImageType", projection.Contains("180") ? 1 : 0);

            int layout = stereo switch
            {
                "sbs" or "lr" or "sidebyside" => 1,
                "tb" or "overunder" or "topbottom" => 2,
                _ => 0
            };
            RenderSettings.skybox.SetInt("_Layout", layout);

            Debug.Log($"[VideoController] Playing video: {url} | Mapping: {mapping} | Projection: {projection} | Stereo: {stereo}");
        }

        public void ChangeProjectionMapping(string mapping, string projection, string stereo)
        {
            bool useCube = mapping.ToLower().Contains("cube");
            RenderSettings.skybox = useCube ? skyboxCubemap : skyboxEquirect;

            RenderSettings.skybox.SetInt("_ImageType", projection.Contains("180") ? 1 : 0);

            int layout = stereo switch
            {
                "sbs" or "lr" or "sidebyside" => 1,
                "tb" or "overunder" or "topbottom" => 2,
                _ => 0
            };
            RenderSettings.skybox.SetInt("_Layout", layout);
            Seek(0);

            Debug.Log($"[VideoController] Changed Projection: Mapping: {mapping} | Projection: {projection} | Stereo: {stereo}");
        }

        public void Seek(double timeCode)
        {
            if (videoPlayer.isPrepared)
            {
                videoPlayer.time = timeCode;
                Debug.Log($"[VideoController] Set timecode to {timeCode}.");
            }
        }

        public void PauseVideo()
        {
            videoPlayer.Pause();
            isPlaying = false;
            UpdatePlayPauseIcon();
            stateMachine?.SetAction("paused");
            Debug.Log("[VideoController] Pausing video.");
        }

        public void ResumeVideo()
        {
            videoPlayer.Play();
            isPlaying = true;
            UpdatePlayPauseIcon();
            stateMachine?.SetState(AppState.PlayingVideo);
            stateMachine?.SetAction("playing");
            Debug.Log("[VideoController] Resuming video.");
        }

        public void StopVideo()
        {
            videoPlayer.Stop();
            isPlaying = false;
            UpdatePlayPauseIcon();
            SetControlPanelVisibility(false);
            RenderSettings.skybox = skyboxDefault; 
            SetFloorAlpha(0f);
            stateMachine?.SetAction("none");
            stateMachine?.ClearContent();

            // Reactivate the MXR panel when video is stopped
            if (mxrPanel != null)
            {
                mxrPanel.SetActive(true);
            }

            // Re-enable AR passthrough and restore camera settings
            if (arCameraManager) arCameraManager.enabled = true;
            if (mainCamera) mainCamera.clearFlags = CameraClearFlags.SolidColor;

            if (stateMachine != null && stateMachine.Current == AppState.PlayingVideo)
            {
                stateMachine.SetState(AppState.Idle);
            }
            Debug.Log("[VideoController] Stopping video.");
        }

        // UI Control Methods
        private void TogglePlayPause()
        {
            if (videoPlayer.isPrepared)
            {
                if (videoPlayer.isPlaying)
                    PauseVideo();
                else
                    ResumeVideo();
            }
        }

        private void Forward10s()
        {
            if (videoPlayer.isPrepared)
            {
                double newTime = Mathf.Min((float)(videoPlayer.time + 10.0), (float)videoPlayer.length);
                Seek(newTime);
            }
        }

        private void Backward10s()
        {
            if (videoPlayer.isPrepared)
            {
                double newTime = Mathf.Max((float)(videoPlayer.time - 10.0), 0f);
                Seek(newTime);
            }
        }

        private void OnSliderValueChanged(float value)
        {
            // Only seek when user is actively dragging
            if (videoPlayer.isPrepared && isDragging)
            {
                double targetTime = value * videoPlayer.length;
                videoPlayer.time = targetTime;
            }
        }

        public void OnSliderPointerDown()
        {
            isDragging = true;
        }

        public void OnSliderPointerUp()
        {
            isDragging = false;
            if (videoPlayer.isPrepared && seekSlider)
            {
                double targetTime = seekSlider.value * videoPlayer.length;
                Seek(targetTime);
            }
        }

        private void UpdatePlayPauseIcon()
        {
            if (playPauseIcon)
            {
                playPauseIcon.sprite = videoPlayer.isPlaying ? pauseSprite : playSprite;
            }
        }

        private void SetControlPanelVisibility(bool visible)
        {
            if (controlPanel != null)
            {
                controlPanel.SetActive(visible);
            }
        }

        public void SetFloorAlpha(float alpha)
        {
            if (floorRenderer != null && floorRenderer.material != null)
            {
                Color color = floorRenderer.material.color;
                color.a = Mathf.Clamp01(alpha);
                floorRenderer.material.color = color;
            }
        }
    }
}