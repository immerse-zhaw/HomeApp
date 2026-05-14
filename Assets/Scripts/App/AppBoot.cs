using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using TMPro;
using MXR.SDK;
using Unity.Collections;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR.Features.Meta;
using UnityEngine.UI;

namespace App
{
    [DefaultExecutionOrder(-1000)]
    [RequireComponent(typeof(StateMachine))]
    public class AppBoot : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private ProjectSettings projectSettings;

        public ProjectSettings ProjectSettings => projectSettings;

        [Header("Core refs")]
        [SerializeField] private Net.WsClient wsClient;
        [SerializeField] private Net.CommandRouter commandRouter;
        [SerializeField] private Playback.VideoController videoController;
        [SerializeField] private Playback.GlbController glbController;
        [SerializeField] private PassthroughController passthroughController;

        [Header("UI")]
        [SerializeField] private TMP_Text serialText;
        [SerializeField] private TMP_Text appsTrackedText;
        [SerializeField] private TMP_Text videosTrackedText;
        [SerializeField] private TMP_Text castingText;
        [Tooltip("Root GameObject for the casting UI (e.g. 'Recording'). If assigned, this will be enabled/disabled when casting starts/stops.")]
        [SerializeField] private GameObject castingRoot;

        [Header("Device Labeling")]
        [Tooltip("If enabled, display the MXR device name when available. Otherwise show the serial or fallback device identifier.")]
        [SerializeField] private bool showDeviceNameInUi = false;
        [Tooltip("Text to display when neither device name nor serial are available.")]
        [SerializeField] private string deviceUnknownLabel = "unknown";

        [Header("Input")]
        [Tooltip("Allow the trigger button to grab/select panel handles and other grabbable objects in addition to grip.")]
        [SerializeField] private bool allowTriggerGrab = true;

        private StateMachine state;
        private bool subscribedToRuntimeSettings;

        async void Awake()
        {
            DontDestroyOnLoad(gameObject);

            if (projectSettings == null)
            {
                Debug.LogError("[AppBoot] ProjectSettings asset not assigned.");
                return;
            }

            Debug.unityLogger.filterLogType = projectSettings.VerboseLogging ? LogType.Log : LogType.Warning;
            ApplyRuntimePerformanceSettings();
            UpdateDeviceStatusLabel(null);
            GrabInputConfigurator.Configure(allowTriggerGrab);
            TryRequestManageExternalStoragePermission();

            Exception mxrInitException = null;
            try
            {
                if (projectSettings.LightweightManageXrInit)
                {
#pragma warning disable 0618
                    MXRManager.Init();
#pragma warning restore 0618
                }
                else
                {
                    await MXRManager.InitAsync();
                }

                if (MXRManager.System != null)
                {
                    MXRManager.System.LoggingEnabled = projectSettings.VerboseLogging;
                }
            }
            catch (Exception ex)
            {
                mxrInitException = ex;
            }

            if (MXRManager.System != null)
            {
                UpdateDeviceStatusLabel(MXRManager.System.DeviceStatus);
                MXRManager.System.OnDeviceStatusChange += UpdateDeviceStatusLabel;
                if (!projectSettings.LightweightManageXrInit)
                {
                    MXRManager.System.OnRuntimeSettingsSummaryChange += OnRuntimeSettingsSummaryChange;
                    subscribedToRuntimeSettings = true;
                }

                if (MXRManager.System.DeviceStatus != null)
                {
                    LogDeviceStatus(MXRManager.System.DeviceStatus);
                }
                else
                {
                    LogMxrFallback("MXR device status is not available yet.");
                }
            }
            else
            {
                string reason = mxrInitException != null
                    ? $"MXR initialization failed: {mxrInitException.Message}"
                    : "MXR system is not ready.";
                LogMxrFallback(reason);
            }

            state = GetComponent<StateMachine>();
            wsClient.Init(projectSettings, state);

            if (passthroughController == null)
            {
                passthroughController = FindObjectOfType<PassthroughController>();
                if (passthroughController == null)
                {
                    Debug.LogWarning("[AppBoot] PassthroughController not found in scene.");
                }
            }

            commandRouter.Init(projectSettings, state, videoController, glbController, passthroughController);

            // Wire state and cross-control between playback components
            if (videoController != null)
            {
                videoController.Inject(state, glbController);
            }
            if (glbController != null)
            {
                glbController.Inject(state, videoController);
            }
            if (passthroughController != null)
            {
                passthroughController.Inject(state);
                passthroughController.Configure(projectSettings.DefaultPassthroughEnabled);
            }


            wsClient.OnMessage += commandRouter.Handle;
            wsClient.Connect();

            Debug.Log("[AppBoot] Ready.");

            // Convenience: if casting UI wasn't assigned in the inspector, try to auto-find the common Recording UI created in the scene hierarchy.
            if (castingRoot == null)
            {
                var go = GameObject.Find("Recording");
                if (go != null) castingRoot = go;
            }
            if (castingText == null && castingRoot != null)
            {
                var txt = castingRoot.GetComponentInChildren<TMP_Text>();
                if (txt != null) castingText = txt;
            }
        }

        void OnDestroy()
        {
            if (MXRManager.System != null)
            {
                MXRManager.System.OnDeviceStatusChange -= UpdateDeviceStatusLabel;
                if (subscribedToRuntimeSettings)
                {
                    MXRManager.System.OnRuntimeSettingsSummaryChange -= OnRuntimeSettingsSummaryChange;
                }
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                ReturnHomeFromExternalAppIfNeeded("focus restored");
            }
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (!isPaused)
            {
                ReturnHomeFromExternalAppIfNeeded("resume");
            }
        }

        private void ReturnHomeFromExternalAppIfNeeded(string reason)
        {
            if (state == null || state.Current != AppState.PlayingApp)
                return;

            FileLogger.Log($"[AppBoot] Returned from launched app ({reason}); reporting home.");
            state.SetState(AppState.Idle);
            state.SetAction("none");
            state.ClearContent();
        }

        private void ApplyRuntimePerformanceSettings()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Mathf.Max(60, projectSettings.TargetFrameRate);
            XRSettings.eyeTextureResolutionScale = Mathf.Clamp(projectSettings.XrRenderScale, 1f, 1.75f);
            StartCoroutine(RequestXrDisplayRefreshRate());

            float dynamicPixels = Mathf.Clamp(projectSettings.WorldCanvasDynamicPixelsPerUnit, 1f, 16f);
            float geometryDensity = Mathf.Clamp(projectSettings.WorldCanvasGeometryDensity, 1f, 4f);
            foreach (var scaler in FindObjectsOfType<CanvasScaler>(true))
            {
                var canvas = scaler.GetComponent<Canvas>();
                if (canvas != null && canvas.renderMode == RenderMode.WorldSpace)
                {
                    scaler.dynamicPixelsPerUnit = Mathf.Max(scaler.dynamicPixelsPerUnit, dynamicPixels);
                    ApplyWorldCanvasGeometryDensity(canvas, geometryDensity);
                }
            }
        }

        private IEnumerator RequestXrDisplayRefreshRate()
        {
            int requestedTarget = Mathf.Max(60, projectSettings.TargetFrameRate);

            for (int attempt = 0; attempt < 30; attempt++)
            {
                var display = GetLoadedDisplaySubsystem();
                if (display != null && display.running)
                {
                    ApplyXrDisplayRefreshRate(display, requestedTarget);
                    yield break;
                }

                yield return null;
            }

            FileLogger.LogWarning("[AppBoot] XR display subsystem not ready; could not request display refresh rate.");
        }

        private void ApplyXrDisplayRefreshRate(XRDisplaySubsystem display, int requestedTarget)
        {
            try
            {
                float current = 0f;
                bool hasCurrent = display.TryGetDisplayRefreshRate(out current);

                if (!display.TryGetSupportedDisplayRefreshRates(Allocator.Temp, out var rates))
                {
                    FileLogger.LogWarning($"[AppBoot] Could not query supported XR refresh rates. current={(hasCurrent ? current.ToString("0.##") : "unknown")}Hz target={requestedTarget}Hz");
                    return;
                }

                using (rates)
                {
                    if (!rates.IsCreated || rates.Length == 0)
                    {
                        FileLogger.LogWarning("[AppBoot] XR refresh rate list is empty.");
                        return;
                    }

                    float selected = rates[0];
                    for (int i = 0; i < rates.Length; i++)
                    {
                        float rate = rates[i];
                        if (rate <= requestedTarget + 0.1f && rate > selected)
                            selected = rate;
                    }

                    var supported = new List<string>(rates.Length);
                    for (int i = 0; i < rates.Length; i++)
                        supported.Add(rates[i].ToString("0.##"));

                    bool requestOk = display.TryRequestDisplayRefreshRate(selected);
                    FileLogger.Log($"[AppBoot] XR refresh request target={requestedTarget}Hz selected={selected:0.##}Hz currentBefore={(hasCurrent ? current.ToString("0.##") : "unknown")}Hz supported=[{string.Join(", ", supported)}] requestOk={requestOk}");
                    StartCoroutine(LogXrDisplayRefreshRateAfterDelay(display, selected));
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogWarning($"[AppBoot] XR refresh rate request failed: {ex.Message}");
            }
        }

        private IEnumerator LogXrDisplayRefreshRateAfterDelay(XRDisplaySubsystem display, float requested)
        {
            yield return new WaitForSecondsRealtime(1f);

            if (display != null && display.TryGetDisplayRefreshRate(out float current))
                FileLogger.Log($"[AppBoot] XR refresh after request={current:0.##}Hz requested={requested:0.##}Hz");
            else
                FileLogger.LogWarning($"[AppBoot] Could not read XR refresh after requesting {requested:0.##}Hz.");
        }

        private static XRDisplaySubsystem GetLoadedDisplaySubsystem()
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            return displays.FirstOrDefault(display => display != null);
        }

        private static void ApplyWorldCanvasGeometryDensity(Canvas canvas, float density)
        {
            if (density <= 1.001f) return;

            var root = canvas.transform as RectTransform;
            if (root == null || root.childCount == 0) return;

            const string WrapperName = "__CanvasDensityWrapper";
            if (root.Find(WrapperName) != null) return;

            var wrapperObject = new GameObject(WrapperName, typeof(RectTransform));
            wrapperObject.hideFlags = HideFlags.HideAndDontSave;
            var wrapper = (RectTransform)wrapperObject.transform;
            wrapper.SetParent(root, false);
            wrapper.anchorMin = Vector2.zero;
            wrapper.anchorMax = Vector2.one;
            wrapper.pivot = root.pivot;
            wrapper.anchoredPosition = Vector2.zero;
            wrapper.sizeDelta = Vector2.zero;

            int childCount = root.childCount;
            for (int i = childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (child == wrapper) continue;
                child.SetParent(wrapper, false);
            }

            root.localScale /= density;
            root.sizeDelta *= density;
            wrapper.localScale = Vector3.one * density;
        }

        private void OnRuntimeSettingsSummaryChange(RuntimeSettingsSummary _)
        {
            UpdateDeviceStatusLabel(MXRManager.System?.DeviceStatus);
        }

        private void UpdateDeviceStatusLabel(DeviceStatus status)
        {
            if (serialText != null)
            {
                serialText.text = DeviceIdentity.GetDisplayLabel(showDeviceNameInUi, deviceUnknownLabel);
            }

            if (appsTrackedText != null)
            {
                int appCount = status?.appStatuses?.Count ?? 0;
                appsTrackedText.text = appCount.ToString();
            }

            if (videosTrackedText != null)
            {
                int videoCount = status?.videoStatuses?.Count ?? 0;
                videosTrackedText.text = videoCount.ToString();
            }

            // Casting / streaming indicator: Meta-specific flag OR any ScreenCast in ACTIVE state
            bool metaScreencast = status?.oculusScreencastActive == true;
            bool anyActiveScreenCast = status?.screenCasts?.Values?.Any(sc => sc != null && sc.state == MXR.SDK.ScreenCast.State.ACTIVE) == true;
            bool isCasting = metaScreencast || anyActiveScreenCast;

            // Prefer enabling the provided root (so your Recording object is shown). Fall back to the text GameObject if no root assigned.
            if (castingRoot != null)
            {
                castingRoot.SetActive(isCasting);
            }

            if (castingText != null)
            {
                castingText.gameObject.SetActive(isCasting);
                castingText.text = isCasting ? "Casting" : "Casting: OFF";
            }
        }

        private void TryRequestManageExternalStoragePermission()
        {
            try
            {
                if (!MXRAndroidUtils.NeedsManageExternalStoragePermission)
                {
                    return;
                }

                if (!MXRAndroidUtils.IsExternalStorageManager)
                {
                    Debug.Log("[AppBoot] Requesting MANAGE_EXTERNAL_STORAGE permission...");
                    FileLogger.Log("[AppBoot] Requesting MANAGE_EXTERNAL_STORAGE permission...");
                    MXRAndroidUtils.RequestManageAppAllFilesAccessPermission();

                    Debug.LogWarning("[AppBoot] Please grant 'All files access' permission in the system dialog, then restart the app.");
                    FileLogger.LogWarning("[AppBoot] Please grant 'All files access' permission in the system dialog, then restart the app.");
                    return;
                }

                Debug.Log("[AppBoot] MANAGE_EXTERNAL_STORAGE permission already granted.");
                FileLogger.Log("[AppBoot] MANAGE_EXTERNAL_STORAGE permission already granted.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AppBoot] MXR permission check failed. Continuing without MXR permission flow. {ex.Message}");
                FileLogger.LogWarning($"[AppBoot] MXR permission check failed. Continuing without MXR permission flow. {ex.Message}");
            }
        }

        private void LogDeviceStatus(DeviceStatus status)
        {
            string serial = DeviceIdentity.GetMxrSerial() ?? DeviceIdentity.GetStableIdentifier();
            int appStatusCount = status.appStatuses?.Count ?? 0;
            int videoStatusCount = status.videoStatuses?.Count ?? 0;

            Debug.Log($"[AppBoot] Device Serial: {serial}");
            Debug.Log($"[AppBoot] App Statuses Count: {appStatusCount}");
            Debug.Log($"[AppBoot] Video Statuses Count: {videoStatusCount}");

            FileLogger.Log($"[AppBoot] Device Serial: {serial}");
            FileLogger.Log($"[AppBoot] App Statuses Count: {appStatusCount}");
            FileLogger.Log($"[AppBoot] Video Statuses Count: {videoStatusCount}");
        }

        private void LogMxrFallback(string reason)
        {
            string serial = DeviceIdentity.GetStableIdentifier();
            Debug.LogWarning($"[AppBoot] {reason} Continuing with fallback device identifier: {serial}");
            FileLogger.LogWarning($"[AppBoot] {reason} Continuing with fallback device identifier: {serial}");
        }
    }
}
