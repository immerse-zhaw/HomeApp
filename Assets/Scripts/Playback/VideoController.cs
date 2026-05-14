using UnityEngine;
using UnityEngine.Video;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.IO;
using UnityEngine.Networking;
using App;
using Launcher;

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
            [SerializeField] private Vector3 controlPanelOffset = new Vector3(0f, -0.5f, 0.6f);

            [Tooltip("If enabled, the panel will face the camera when shown. Use Rotation Offset to fine-tune orientation.")]
            [SerializeField] private bool faceControlPanelToCamera = true;

            [Tooltip("Applied after facing the camera (euler degrees). Useful if the UI appears mirrored/backwards.")]
            [SerializeField] private Vector3 controlPanelRotationOffsetEuler = Vector3.zero;

            [Header("Video Playback Mode")]
            [Tooltip("When enabled, the video is downloaded to persistent storage first and played from disk (reliable on slow/remote networks). When disabled, the URL is streamed directly (lower latency on a good LAN).")]
            [SerializeField] private bool useVideoCache = true;
            [Tooltip("Yaw correction for 360/180 skybox videos. Use this when the encoded forward direction is offset.")]
            [SerializeField] private float skyboxYawOffsetDegrees = 90f;
            [Tooltip("MSAA while video is playing. Skybox video does not benefit much from 8x MSAA, and lower values reduce decode/render pressure.")]
            [SerializeField, Range(1, 8)] private int videoMsaaSamples = 1;
            [Tooltip("MSAA restored after video closes.")]
            [SerializeField, Range(1, 8)] private int defaultMsaaSamples = 8;

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
            private bool videoQualityActive = false;
            private RenderTexture videoTargetTexture;
            private Coroutine cleanupCoroutine;
            private float playbackRequestStartedAt;
            private string playbackSourceLabel = "none";
            private bool pendingAutoPlay = true;

            // MXR panel position restoration
            private Vector3 mxrPanelInitialPosition;
            private Quaternion mxrPanelInitialRotation;

            public void Inject(StateMachine sm, GlbController gc)
            {
                stateMachine = sm;
                glbController = gc;
            }

            public void Awake()
            {
                RenderSettings.skybox = skyboxDefault; 
                videoTargetTexture = videoPlayer != null ? videoPlayer.targetTexture : null;
                SetupUI();
                SetControlPanelVisibility(false);
                Debug.Log($"[VideoController] PersistentDataPath: {Application.persistentDataPath}");
                // Ensure pause icon is shown at start (video is playing by default)
                if (playPauseIcon != null && pauseSprite != null)
                {
                    playPauseIcon.sprite = pauseSprite;
                }
                // Save initial MXR panel position for restoration
                if (mxrPanel != null)
                {
                    mxrPanelInitialPosition = mxrPanel.transform.position;
                    mxrPanelInitialRotation = mxrPanel.transform.rotation;
                }
            }

            private void OnDestroy()
            {
                if (cleanupCoroutine != null)
                    StopCoroutine(cleanupCoroutine);
                ResetCurrentVideoSurface(true);
                RestoreDefaultQuality();
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

                // Keep controls reachable while paused; a prepared video is still the active video session.
                if (!HasActiveVideoSession()) return;

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

            private bool HasActiveVideoSession()
            {
                return controlPanel != null && videoPlayer != null && videoPlayer.isPrepared;
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

            public void PlayVideo(string url, string name = null, string fileId = null, bool autoPlay = true)
            {
                FileLogger.Log($"[Video] Play requested name={name ?? "none"} fileId={fileId ?? "none"} url={ShortUrl(url)}");
                ResetCurrentVideoSurface(true);

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

                if (!useVideoCache)
                {
                    // ── Stream mode: play directly from the URL ──────────────────────
                    Debug.Log($"[VideoController] Stream mode – playing URL directly: {url}");
                    StartPlaybackFromUrl(url, name, fileId, autoPlay);
                    return;
                }

                // ── Cache mode: download then play from disk ─────────────────────────
                // Check if the file is already cached locally
                string localPath = GetCachedPath(fileId, url);
                if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
                {
                    FileLogger.Log($"[Video] Cache hit path={localPath} size={GetFileSizeLabel(localPath)}");
                    StartPlaybackFromLocal(localPath, name, fileId, autoPlay);
                    return;
                }

                // File not cached — download first, then play
                FileLogger.Log($"[Video] Cache miss; downloading url={ShortUrl(url)}");
                downloadCoroutine = StartCoroutine(DownloadAndPlay(url, name, fileId, autoPlay));
            }

            /// <summary>
            /// Returns the expected local cache path for a video, or null if the file cannot be cached
            /// (e.g. no fileId and URL has no extension to use as filename).
            /// </summary>
            private string GetCachedPath(string fileId, string url)
            {
                return ContentCache.GetCachedPath("videos", fileId, url, ".mp4");
            }

            private IEnumerator DownloadAndPlay(string url, string name, string fileId, bool autoPlay)
            {
                // Show control panel in "loading" state so the user sees feedback
                float startedAt = Time.realtimeSinceStartup;
                if (timeText != null) timeText.text = "Downloading... 0%";
                SetControlPanelVisibility(true);

                string localPath = GetCachedPath(fileId, url);
                string tempPath = ContentCache.GetTempPath(localPath);

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

                FileLogger.Log($"[Video] Download complete path={localPath} size={GetFileSizeLabel(localPath)} elapsed={(Time.realtimeSinceStartup - startedAt):0.00}s");
                downloadCoroutine = null;

                StartPlaybackFromLocal(localPath, name, fileId, autoPlay);
            }

            private void StartPlaybackFromUrl(string url, string name, string fileId, bool autoPlay)
            {
                ApplyVideoQuality();
                ResetCurrentVideoSurface(false);
                PrepareVideoPlayerForPlayback();
                playbackRequestStartedAt = Time.realtimeSinceStartup;
                playbackSourceLabel = "stream";
                pendingAutoPlay = autoPlay;
                lastDisplayedSecond = -1;
                videoPlayer.skipOnDrop = true;   // drop frames instead of stalling when decoder falls behind
                videoPlayer.url = url;
                videoPlayer.prepareCompleted += OnVideoPrepared;
                videoPlayer.Prepare();

                isPlaying = autoPlay;
                UpdatePlayPauseIcon();
                SetControlPanelVisibility(true);

                stateMachine?.SetState(AppState.PlayingVideo);
                stateMachine?.SetAction(autoPlay ? "playing" : "paused");
                stateMachine?.SetContent(name, fileId);
            }

            private void StartPlaybackFromLocal(string localPath, string name, string fileId, bool autoPlay)
            {
                ApplyVideoQuality();
                ResetCurrentVideoSurface(false);
                PrepareVideoPlayerForPlayback();
                playbackRequestStartedAt = Time.realtimeSinceStartup;
                playbackSourceLabel = "local";
                pendingAutoPlay = autoPlay;
                lastDisplayedSecond = -1;
                videoPlayer.skipOnDrop = true;   // drop frames instead of stalling when decoder falls behind
                // Prefix required by Unity on all platforms
                videoPlayer.url = "file://" + localPath;

                videoPlayer.prepareCompleted += OnVideoPrepared;
                videoPlayer.Prepare();

                isPlaying = autoPlay;
                UpdatePlayPauseIcon();
                SetControlPanelVisibility(true);

                stateMachine?.SetState(AppState.PlayingVideo);
                stateMachine?.SetAction(autoPlay ? "playing" : "paused");
                stateMachine?.SetContent(name, fileId);

                FileLogger.Log($"[Video] Prepare requested source=local path={localPath} size={GetFileSizeLabel(localPath)} mapping={currentMapping} projection={currentProjection} stereo={currentStereo} autoPlay={autoPlay}");
            }

            private void OnVideoPrepared(VideoPlayer source)
            {
                source.prepareCompleted -= OnVideoPrepared;
                FileLogger.Log($"[Video] Prepared source={playbackSourceLabel} elapsed={(Time.realtimeSinceStartup - playbackRequestStartedAt):0.00}s size={source.width}x{source.height} fps={source.frameRate:0.##} frames={source.frameCount} rt={(source.targetTexture != null ? source.targetTexture.width + "x" + source.targetTexture.height : "none")}");
                if (pendingAutoPlay)
                    source.Play();
                else
                    source.Pause();
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
                RenderSettings.skybox.SetFloat("_Rotation", skyboxYawOffsetDegrees);

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
                float stoppedAt = Time.realtimeSinceStartup;
                FileLogger.Log($"[Video] Stop requested isPrepared={videoPlayer.isPrepared} isPlaying={videoPlayer.isPlaying} time={videoPlayer.time:0.00}s");
                // Cancel any in-progress download and remove the incomplete .part file
                if (downloadCoroutine != null)
                {
                    StopCoroutine(downloadCoroutine);
                    downloadCoroutine = null;

                    // Clean up a partially downloaded file if it exists
                    try
                    {
                        string cacheDir = Path.Combine(ContentCache.Root, "videos");
                        foreach (var part in Directory.GetFiles(cacheDir, "*.part"))
                            File.Delete(part);
                    }
                    catch { /* best-effort */ }
                }

                videoPlayer.prepareCompleted -= OnVideoPrepared; // guard against pending prepare
                ResetCurrentVideoSurface(true);
                RestoreDefaultQuality();
                ScheduleUnusedAssetCleanup();
                isPlaying = false;
                UpdatePlayPauseIcon();
                SetControlPanelVisibility(false);
                stateMachine?.SetAction("none");
                stateMachine?.ClearContent();

                // Reactivate the MXR panel when video is stopped
                if (mxrPanel != null)
                {
                    mxrPanel.SetActive(true);
                    RestoreMxrPanelPosition();
                }

                if (stateMachine != null && stateMachine.Current == AppState.PlayingVideo)
                {
                    stateMachine.SetState(AppState.Idle);
                }
                FileLogger.Log($"[Video] Stop completed elapsed={(Time.realtimeSinceStartup - stoppedAt):0.00}s");
            }

            private void ResetCurrentVideoSurface(bool releaseRenderTexture)
            {
                if (videoPlayer == null) return;
                videoPlayer.prepareCompleted -= OnVideoPrepared;

                if (videoPlayer.isPlaying || videoPlayer.isPrepared)
                {
                    videoPlayer.Stop();
                }

                videoPlayer.url = string.Empty;
                videoPlayer.clip = null;
                RenderSettings.skybox = skyboxDefault;

                var target = videoPlayer.targetTexture != null ? videoPlayer.targetTexture : videoTargetTexture;
                if (target == null)
                {
                    videoPlayer.enabled = false;
                    return;
                }

                if (target.IsCreated())
                {
                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    GL.Clear(false, true, Color.black);
                    RenderTexture.active = previous;
                    target.DiscardContents();
                }

                if (releaseRenderTexture)
                {
                    videoPlayer.targetTexture = null;
                    target.Release();
                    videoPlayer.enabled = false;
                }
            }

            private void PrepareVideoPlayerForPlayback()
            {
                if (videoPlayer == null) return;

                if (videoTargetTexture != null)
                {
                    if (!videoTargetTexture.IsCreated())
                        videoTargetTexture.Create();
                    videoPlayer.targetTexture = videoTargetTexture;
                }

                videoPlayer.enabled = true;
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
                FileLogger.Log("[Video] UnloadUnusedAssets begin");
                yield return null;
                yield return Resources.UnloadUnusedAssets();
                GC.Collect();
                FileLogger.Log($"[Video] UnloadUnusedAssets complete elapsed={(Time.realtimeSinceStartup - startedAt):0.00}s");
                cleanupCoroutine = null;
            }

            private void ApplyVideoQuality()
            {
                SetMsaaSamples(videoMsaaSamples);
                videoQualityActive = true;
            }

            private void RestoreDefaultQuality()
            {
                if (!videoQualityActive) return;

                SetMsaaSamples(defaultMsaaSamples);
                videoQualityActive = false;
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
                if (!videoPlayer.isPrepared || videoPlayer.length <= 0)
                    return;

                // Playback progress is pushed with SetValueWithoutNotify(), so value-changed
                // callbacks here are user interactions from clicking/grabbing the playbar.
                double targetTime = Mathf.Clamp01(value) * videoPlayer.length;
                Seek(targetTime);
                RestartControlPanelAutoHideTimer();
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
                    RestartControlPanelAutoHideTimer();
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

            private void RestoreMxrPanelPosition()
            {
                if (mxrPanel == null) return;
                mxrPanel.transform.position = mxrPanelInitialPosition;
                mxrPanel.transform.rotation = mxrPanelInitialRotation;
            }


        }
}
