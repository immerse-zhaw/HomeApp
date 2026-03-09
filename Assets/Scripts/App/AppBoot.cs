using System;
using UnityEngine;
using System.Linq;
using TMPro;
using MXR.SDK;

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

        async void Awake()
        {
            DontDestroyOnLoad(gameObject);

            if (projectSettings == null)
            {
                Debug.LogError("[AppBoot] ProjectSettings asset not assigned.");
                return;
            }

            UpdateDeviceStatusLabel(null);
            GrabInputConfigurator.Configure(allowTriggerGrab);
            TryRequestManageExternalStoragePermission();

            Exception mxrInitException = null;
            try
            {
                await MXRManager.InitAsync();
            }
            catch (Exception ex)
            {
                mxrInitException = ex;
            }

            if (MXRManager.System != null)
            {
                UpdateDeviceStatusLabel(MXRManager.System.DeviceStatus);
                MXRManager.System.OnDeviceStatusChange += UpdateDeviceStatusLabel;
                MXRManager.System.OnRuntimeSettingsSummaryChange += (_) => UpdateDeviceStatusLabel(MXRManager.System.DeviceStatus);

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
            }
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
