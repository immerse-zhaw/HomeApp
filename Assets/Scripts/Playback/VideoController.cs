using UnityEngine;
using UnityEngine.Video;
using UnityEngine.XR;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.IO;
using UnityEngine.Networking;
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

            [Header("Control Panel Placement")]
            [Tooltip("Optional. If not set, falls back to Camera.main.")]
            [SerializeField] private Transform userCameraTransform;

            [Tooltip("Offset in camera-local space (x=right, y=up, z=forward).")]
            [SerializeField] private Vector3 controlPanelOffset = new Vector3(0f, -0.2f, 0.6f);

            [Tooltip("If enabled, the panel will face the camera when shown. Use Rotation Offset to fine-tune orientation.")]
            [SerializeField] private bool faceControlPanelToCamera = true;

            [Tooltip("Applied after facing the camera (euler degrees). Useful if the UI appears mirrored/backwards.")]
            [SerializeField] private Vector3 controlPanelRotationOffsetEuler = Vector3.zero;

            [Header("Video Playback Mode")]
            [Tooltip("When enabled, the video is downloaded to persistent storage first and played from disk (reliable on slow/remote networks). When disabled, the URL is streamed directly (lower latency on a good LAN).")]
            [SerializeField] private bool useVideoCache = true;

            [Header("Control Panel Auto-Hide")]
            [SerializeField] private bool autoHideControlPanel = true;
            [SerializeField] private float controlPanelAutoHideSeconds = 5f;

            private bool isPlaying = false;
            private bool isDragging = false;
            private bool prevLeftPrimaryPressed = false; // for edge-detecting left primary button presses

            private Coroutine controlPanelAutoHideCoroutine;
            private Coroutine downloadCoroutine;

            // Cached state for throttled UI updates
            private int lastDisplayedSecond = -1;   // avoids string alloc every frame
            private int lastDownloadPct = -1;        // avoids string alloc every frame during download

            // Video cache directory inside persistentDataPath
            private static readonly string CacheFolder = "VideoCache";

            // Pending metadata stored while download is in progress
            private string pendingName;
            private string pendingFileId;

            // Current projection settings set via ChangeProjectionMapping
            private string currentMapping = "equirectangular";
            private string currentProjection = "360";
            private string currentStereo = "mono";

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
                SetupUI();
                SetControlPanelVisibility(false);
                Debug.Log($"[VideoController] PersistentDataPath: {Application.persistentDataPath}");
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

                // XR input polling is only needed while a video is actively playing
                if (!isPlaying || controlPanel == null) return;

                var leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
                if (leftHand.isValid && leftHand.TryGetFeatureValue(CommonUsages.primaryButton, out bool primaryPressed))
                {
                    if (primaryPressed && !prevLeftPrimaryPressed)
                    {
                        bool visible = controlPanel.activeSelf;
                        SetControlPanelVisibility(!visible);
                    }
                    prevLeftPrimaryPressed = primaryPressed;
                }
            }

            private void UpdateTimeText()
            {
                if (!timeText || !videoPlayer.isPrepared) return;

                // Only rebuild the string when the displayed second actually changes
                int currentSecond = (int)videoPlayer.time;
                if (currentSecond == lastDisplayedSecond) return;
                lastDisplayedSecond = currentSecond;

                timeText.text = $"{FormatTime(videoPlayer.time)} / {FormatTime(videoPlayer.length)}";
            }

            private string FormatTime(double timeInSeconds)
            {
                int minutes = Mathf.FloorToInt((float)timeInSeconds / 60f);
                int seconds = Mathf.FloorToInt((float)timeInSeconds % 60f);
                return $"{minutes}:{seconds:00}";
            }

            public void PlayVideo(string url, string name = null, string fileId = null)
            {
                // Cancel any in-progress download from a previous request
                if (downloadCoroutine != null)
                {
                    StopCoroutine(downloadCoroutine);
                    downloadCoroutine = null;
                }

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

                // Deactivate the MXR panel immediately
                if (mxrPanel != null)
                    mxrPanel.SetActive(false);

                ApplyProjectionSettings();

                if (!useVideoCache)
                {
                    // ── Stream mode: play directly from the URL ──────────────────────
                    Debug.Log($"[VideoController] Stream mode – playing URL directly: {url}");
                    StartPlaybackFromUrl(url, name, fileId);
                    return;
                }

                // ── Cache mode: download then play from disk ─────────────────────────
                // Check if the file is already cached locally
                string localPath = GetCachedPath(fileId, url);
                if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
                {
                    Debug.Log($"[VideoController] Cache hit – playing from local file: {localPath}");
                    StartPlaybackFromLocal(localPath, name, fileId);
                    return;
                }

                // File not cached — download first, then play
                Debug.Log($"[VideoController] Cache miss – downloading: {url}");
                downloadCoroutine = StartCoroutine(DownloadAndPlay(url, name, fileId));
            }

            /// <summary>
            /// Returns the expected local cache path for a video, or null if the file cannot be cached
            /// (e.g. no fileId and URL has no extension to use as filename).
            /// </summary>
            private string GetCachedPath(string fileId, string url)
            {
                string cacheDir = Path.Combine(Application.persistentDataPath, CacheFolder);
                if (!string.IsNullOrEmpty(fileId))
                {
                    // Try to preserve the original extension from the URL
                    string ext = Path.GetExtension(new Uri(url).LocalPath);
                    if (string.IsNullOrEmpty(ext)) ext = ".mp4";
                    return Path.Combine(cacheDir, fileId + ext);
                }
                return null; // no stable key – cannot cache
            }

            private IEnumerator DownloadAndPlay(string url, string name, string fileId)
            {
                // Show control panel in "loading" state so the user sees feedback
                if (timeText != null) timeText.text = "Downloading... 0%";
                SetControlPanelVisibility(true);

                string cacheDir = Path.Combine(Application.persistentDataPath, CacheFolder);
                Directory.CreateDirectory(cacheDir);

                string localPath = GetCachedPath(fileId, url);
                // If no stable key, use a temp filename derived from url hash
                if (localPath == null)
                {
                    string ext = Path.GetExtension(new Uri(url).LocalPath);
                    if (string.IsNullOrEmpty(ext)) ext = ".mp4";
                    localPath = Path.Combine(cacheDir, "tmp_" + Mathf.Abs(url.GetHashCode()) + ext);
                }

                string tempPath = localPath + ".part";

                using (var uwr = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
                {
                    uwr.downloadHandler = new DownloadHandlerFile(tempPath);
                    uwr.SendWebRequest();

                    while (!uwr.isDone)
                    {
                        // Only allocate a new string when the integer percentage changes
                        int pct = (int)(uwr.downloadProgress * 100f);
                        if (timeText != null && pct != lastDownloadPct)
                        {
                            lastDownloadPct = pct;
                            timeText.text = $"Downloading... {pct}%";
                        }
                        yield return null;
                    }

                    if (uwr.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogError($"[VideoController] Download failed: {uwr.error}");
                        if (timeText != null) timeText.text = "Download failed";
                        downloadCoroutine = null;

                        // Reactivate MXR panel so the user isn't stuck
                        if (mxrPanel != null) mxrPanel.SetActive(true);
                        SetControlPanelVisibility(false);
                        yield break;
                    }
                }

                // Rename .part -> final path (atomic-ish swap)
                if (File.Exists(localPath)) File.Delete(localPath);
                File.Move(tempPath, localPath);

                Debug.Log($"[VideoController] Download complete: {localPath}");
                downloadCoroutine = null;

                StartPlaybackFromLocal(localPath, name, fileId);
            }

            private void StartPlaybackFromUrl(string url, string name, string fileId)
            {
                lastDisplayedSecond = -1;
                videoPlayer.skipOnDrop = true;   // drop frames instead of stalling when decoder falls behind
                videoPlayer.url = url;
                videoPlayer.prepareCompleted += OnVideoPrepared;
                videoPlayer.Prepare();

                isPlaying = true;
                UpdatePlayPauseIcon();
                SetControlPanelVisibility(true);

                stateMachine?.SetState(AppState.PlayingVideo);
                stateMachine?.SetAction("playing");
                stateMachine?.SetContent(name, fileId);
            }

            private void StartPlaybackFromLocal(string localPath, string name, string fileId)
            {
                lastDisplayedSecond = -1;
                videoPlayer.skipOnDrop = true;   // drop frames instead of stalling when decoder falls behind
                // Prefix required by Unity on all platforms
                videoPlayer.url = "file://" + localPath;

                videoPlayer.prepareCompleted += OnVideoPrepared;
                videoPlayer.Prepare();

                isPlaying = true;
                UpdatePlayPauseIcon();
                SetControlPanelVisibility(true);

                stateMachine?.SetState(AppState.PlayingVideo);
                stateMachine?.SetAction("playing");
                stateMachine?.SetContent(name, fileId);

                Debug.Log($"[VideoController] Prepared: {localPath} | Mapping: {currentMapping} | Projection: {currentProjection} | Stereo: {currentStereo}");
            }

            private void OnVideoPrepared(VideoPlayer source)
            {
                source.prepareCompleted -= OnVideoPrepared;
                source.Play();
                // ensure icon reflects actual playback state
                UpdatePlayPauseIcon();
                // Refresh skybox texture now that the render texture is populated
                ApplyProjectionSettings();
            }

            public void ChangeProjectionMapping(string mapping, string projection, string stereo)
            {
                // Store the new projection settings
                currentMapping = mapping;
                currentProjection = projection;
                currentStereo = stereo;

                // Apply them immediately if video is playing
                ApplyProjectionSettings();
                Seek(0);

                Debug.Log($"[VideoController] Changed Projection: Mapping: {mapping} | Projection: {projection} | Stereo: {stereo}");
            }

            private void ApplyProjectionSettings()
            {
                bool useCube = currentMapping.ToLower().Contains("cube");
                RenderSettings.skybox = useCube ? skyboxCubemap : skyboxEquirect;

                if (videoPlayer.targetTexture)
                    RenderSettings.skybox.SetTexture("_MainTex", videoPlayer.targetTexture);

                RenderSettings.skybox.SetInt("_ImageType", currentProjection.Contains("180") ? 1 : 0);

                int layout = currentStereo switch
                {
                    "sbs" or "lr" or "sidebyside" => 1,
                    "tb" or "overunder" or "topbottom" => 2,
                    _ => 0
                };
                RenderSettings.skybox.SetInt("_Layout", layout);
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
                // Cancel any in-progress download and remove the incomplete .part file
                if (downloadCoroutine != null)
                {
                    StopCoroutine(downloadCoroutine);
                    downloadCoroutine = null;

                    // Clean up a partially downloaded file if it exists
                    try
                    {
                        string cacheDir = Path.Combine(Application.persistentDataPath, CacheFolder);
                        foreach (var part in Directory.GetFiles(cacheDir, "*.part"))
                            File.Delete(part);
                    }
                    catch { /* best-effort */ }
                }

                videoPlayer.prepareCompleted -= OnVideoPrepared; // guard against pending prepare
                videoPlayer.Stop();
                isPlaying = false;
                UpdatePlayPauseIcon();
                SetControlPanelVisibility(false);
                RenderSettings.skybox = skyboxDefault; 
                stateMachine?.SetAction("none");
                stateMachine?.ClearContent();

                // Reactivate the MXR panel when video is stopped
                if (mxrPanel != null)
                {
                    mxrPanel.SetActive(true);
                }

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
                    if (visible)
                    {
                        PlaceControlPanelInFrontOfCamera();
                        RestartControlPanelAutoHideTimer();
                    }
                    else
                    {
                        StopControlPanelAutoHideTimer();
                    }
                    controlPanel.SetActive(visible);
                }
            }

            private void RestartControlPanelAutoHideTimer()
            {
                StopControlPanelAutoHideTimer();

                if (!autoHideControlPanel)
                {
                    return;
                }

                if (controlPanelAutoHideSeconds <= 0f)
                {
                    return;
                }

                controlPanelAutoHideCoroutine = StartCoroutine(AutoHideControlPanelAfterDelay(controlPanelAutoHideSeconds));
            }

            private void StopControlPanelAutoHideTimer()
            {
                if (controlPanelAutoHideCoroutine != null)
                {
                    StopCoroutine(controlPanelAutoHideCoroutine);
                    controlPanelAutoHideCoroutine = null;
                }
            }

            private IEnumerator AutoHideControlPanelAfterDelay(float seconds)
            {
                yield return new WaitForSeconds(seconds);

                controlPanelAutoHideCoroutine = null;

                if (controlPanel != null && controlPanel.activeSelf)
                {
                    SetControlPanelVisibility(false);
                }
            }

            private Transform GetUserCameraTransform()
            {
                if (userCameraTransform != null)
                {
                    return userCameraTransform;
                }

                var mainCam = Camera.main;
                return mainCam != null ? mainCam.transform : null;
            }

            private void PlaceControlPanelInFrontOfCamera()
            {
                if (controlPanel == null)
                {
                    return;
                }

                var camTransform = GetUserCameraTransform();
                if (camTransform == null)
                {
                    Debug.LogWarning("[VideoController] Cannot place control panel: no user camera transform assigned and no Camera.main found.");
                    return;
                }

                var panelTransform = controlPanel.transform;
                panelTransform.position = camTransform.TransformPoint(controlPanelOffset);

                if (faceControlPanelToCamera)
                {
                    // Make the panel face the camera (so its forward points toward the camera).
                    var toCamera = camTransform.position - panelTransform.position;
                    if (toCamera.sqrMagnitude > 0.0001f)
                    {
                        panelTransform.rotation = Quaternion.LookRotation(toCamera.normalized, camTransform.up) * Quaternion.Euler(controlPanelRotationOffsetEuler);
                    }
                }
            }


        }
}