using UnityEngine;
using UnityEngine.Video;
using UnityEngine.XR.ARFoundation;
using UnityEngine.UI;
using TMPro;
using System.IO;

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

        public void Awake()
        {
            RenderSettings.skybox = skyboxDefault; 
            SetFloorAlpha(0f);
            SetupUI();
            SetControlPanelVisibility(false);
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

        public void PlayVideo(string url, string mapping, string projection, string stereo)
        {   
            SetFloorAlpha(0.0f);
            videoPlayer.url = url;
            videoPlayer.Play();
            isPlaying = true;
            UpdatePlayPauseIcon();
            SetControlPanelVisibility(true);

            // Deactivate the MXR panel when video is playing
            if (mxrPanel != null)
            {
                mxrPanel.SetActive(false);
            }

            // Send status to websocket: "Playing video: <filename>"
            var ws = FindObjectOfType<Net.WsClient>();
            if (ws != null)
            {
                var fileName = Path.GetFileName(url);
                var status = $"Playing video: {fileName}";
                Debug.Log($"[VideoController] Sending status: {status}");
                ws.SendStatus(status);
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
            Debug.Log("[VideoController] Pausing video.");
        }

        public void ResumeVideo()
        {
            videoPlayer.Play();
            isPlaying = true;
            UpdatePlayPauseIcon();
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

            // Reactivate the MXR panel when video is stopped
            if (mxrPanel != null)
            {
                mxrPanel.SetActive(true);
            }

            // Re-enable AR passthrough and restore camera settings
            if (arCameraManager) arCameraManager.enabled = true;
            if (mainCamera) mainCamera.clearFlags = CameraClearFlags.SolidColor;

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