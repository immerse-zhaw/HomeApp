using UnityEngine;
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
    }
}