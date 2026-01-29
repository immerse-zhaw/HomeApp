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
        [Tooltip("If enabled, display the device name (from RuntimeSettingsSummary) in the device label when available. Otherwise show the serial number.")]
        [SerializeField] private bool showDeviceNameInUi = false;
        [Tooltip("Text to display when neither device name nor serial are available.")]
        [SerializeField] private string deviceUnknownLabel = "unknown";

        private StateMachine state;

        async void Awake()
        {
            DontDestroyOnLoad(gameObject);

            if (projectSettings == null)
            {
                Debug.LogError("[AppBoot] ProjectSettings asset not assigned.");
                return;
            }
            
            // Request MANAGE_EXTERNAL_STORAGE permission BEFORE initializing MXR SDK
            // This is critical for Android 11+ (Quest 3) to allow Admin App to download/install apps
            if (MXRAndroidUtils.NeedsManageExternalStoragePermission)
            {
                if (!MXRAndroidUtils.IsExternalStorageManager)
                {
                    Debug.Log("[AppBoot] Requesting MANAGE_EXTERNAL_STORAGE permission...");
                    FileLogger.Log("[AppBoot] Requesting MANAGE_EXTERNAL_STORAGE permission...");
                    MXRAndroidUtils.RequestManageAppAllFilesAccessPermission();
                    
                    // Note: User must grant this permission in Android Settings.
                    // The app may need to restart after granting the permission.
                    Debug.LogWarning("[AppBoot] Please grant 'All files access' permission in the system dialog, then restart the app.");
                    FileLogger.LogWarning("[AppBoot] Please grant 'All files access' permission in the system dialog, then restart the app.");
                }
                else
                {
                    Debug.Log("[AppBoot] MANAGE_EXTERNAL_STORAGE permission already granted.");
                    FileLogger.Log("[AppBoot] MANAGE_EXTERNAL_STORAGE permission already granted.");
                }
            }
            
            // Initialize MXR and print serial number
            await MXRManager.InitAsync();
            if (MXRManager.System == null)
            {
                Debug.LogError("[AppBoot] MXRManager.System not ready.");
                FileLogger.LogError("[AppBoot] MXRManager.System not ready.");
                return;
            }

            UpdateDeviceStatusLabel(MXRManager.System.DeviceStatus);
            MXRManager.System.OnDeviceStatusChange += UpdateDeviceStatusLabel;
            // Also update when runtime settings summary changes (deviceName can arrive here)
            MXRManager.System.OnRuntimeSettingsSummaryChange += (_) => UpdateDeviceStatusLabel(MXRManager.System.DeviceStatus);

            if (MXRManager.System.DeviceStatus != null)
            {
                string serial = MXRManager.System.DeviceStatus.serial;
                int appStatusCount = MXRManager.System.DeviceStatus.appStatuses?.Count ?? 0;
                int videoStatusCount = MXRManager.System.DeviceStatus.videoStatuses?.Count ?? 0;
                
                Debug.Log($"[AppBoot] Device Serial: {serial}");
                Debug.Log($"[AppBoot] App Statuses Count: {appStatusCount}");
                Debug.Log($"[AppBoot] Video Statuses Count: {videoStatusCount}");
                
                FileLogger.Log($"[AppBoot] Device Serial: {serial}");
                FileLogger.Log($"[AppBoot] App Statuses Count: {appStatusCount}");
                FileLogger.Log($"[AppBoot] Video Statuses Count: {videoStatusCount}");
            }
            else
            {
                Debug.LogError("[AppBoot] DeviceStatus is NULL - MXR not initialized properly!");
                FileLogger.LogError("[AppBoot] DeviceStatus is NULL - MXR not initialized properly!");
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
            if (status == null)
            {
                if (serialText != null) serialText.text = "N/A";
                if (appsTrackedText != null) appsTrackedText.text = "0";
                if (videosTrackedText != null) videosTrackedText.text = "0";
                return;
            }

            // Device label: either deviceName (from RuntimeSettingsSummary) or serial depending on inspector toggle
            if (serialText != null)
            {
                string label = deviceUnknownLabel;

                if (showDeviceNameInUi)
                {
                    label = MXRManager.System?.RuntimeSettingsSummary?.deviceName ?? status.serial ?? deviceUnknownLabel;
                }
                else
                {
                    label = status.serial ?? MXRManager.System?.RuntimeSettingsSummary?.deviceName ?? deviceUnknownLabel;
                }

                serialText.text = label;
            }

            if (appsTrackedText != null)
            {
                int appCount = status.appStatuses?.Count ?? 0;
                appsTrackedText.text = appCount.ToString();
            }

            if (videosTrackedText != null)
            {
                int videoCount = status.videoStatuses?.Count ?? 0;
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
                castingText.text = isCasting ? "Casting: ON" : "Casting: OFF";
            }
        }
    }
}
