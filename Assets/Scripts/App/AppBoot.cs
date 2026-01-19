using UnityEngine;
using TMPro;
using MXR.SDK;
using MXR.SDK.Samples;

namespace App
{
    [DefaultExecutionOrder(-1000)]
    [RequireComponent(typeof(StateMachine))]
    public class AppBoot : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private ProjectSettings projectSettings;

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

            if (serialText != null)
            {
                serialText.text = status.serial;
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
        }
    }
}