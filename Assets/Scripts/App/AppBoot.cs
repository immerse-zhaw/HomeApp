using UnityEngine;
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

        [Header("Core refs")]
        [SerializeField] private Net.WsClient wsClient;
        [SerializeField] private Net.CommandRouter commandRouter;
        [SerializeField] private Playback.VideoController videoController;
        [SerializeField] private Playback.GlbController glbController;

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
            
            // Initialize MXR and print serial number
            await MXRManager.InitAsync();
            if (MXRManager.System == null)
            {
                Debug.LogError("[AppBoot] MXRManager.System not ready.");
                return;
            }

            UpdateDeviceStatusLabel(MXRManager.System.DeviceStatus);
            MXRManager.System.OnDeviceStatusChange += UpdateDeviceStatusLabel;

            if (MXRManager.System.DeviceStatus != null)
            {
                string serial = MXRManager.System.DeviceStatus.serial;
                Debug.Log($"[AppBoot] Device Serial Number: {serial}");
            }
            state = GetComponent<StateMachine>();

            wsClient.Init(projectSettings);
            commandRouter.Init(projectSettings, state, videoController, glbController);


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