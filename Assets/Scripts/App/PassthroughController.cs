using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using TMPro;

namespace App
{
    public class PassthroughController : MonoBehaviour
    {
        [Header("AR Components")]
        [SerializeField] private ARCameraManager arCameraManager;
        [SerializeField] private Camera mainCamera;
        [Header("UI")]
        [SerializeField] private TMP_Text passthroughStatusText;

        private bool prevLeftSecondaryPressed = false;
        private bool passthroughEnabled = false;

        private StateMachine stateMachine;

        public void Inject(StateMachine sm)
        {
            stateMachine = sm;
        }

        private void Awake()
        {
            passthroughEnabled = arCameraManager != null && arCameraManager.enabled;
            UpdateStatusText();
        }

        private void UpdateStatusText()
        {
            if (passthroughStatusText == null) return;
            passthroughStatusText.text = passthroughEnabled ? "Passthrough: ON" : "Passthrough: OFF";
        }

        private void Update()
        {
            var leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (leftHand.isValid && leftHand.TryGetFeatureValue(CommonUsages.secondaryButton, out bool secondaryPressed))
            {
                bool inVideoMode = stateMachine != null && stateMachine.Current == AppState.PlayingVideo;

                if (secondaryPressed && !prevLeftSecondaryPressed)
                {
                    if (inVideoMode)
                    {
                        // Force passthrough off in video mode
                        EnsurePassthroughOffForVideo();
                    }
                    else
                    {
                        TogglePassthrough();
                    }
                }

                prevLeftSecondaryPressed = secondaryPressed;

                if (inVideoMode)
                {
                    EnsurePassthroughOffForVideo();
                }
            }
            else
            {
                // Still enforce video-mode passthrough OFF even if input isn't available
                if (stateMachine != null && stateMachine.Current == AppState.PlayingVideo)
                {
                    EnsurePassthroughOffForVideo();
                }
            }
        }

        private void TogglePassthrough()
        {
            SetPassthrough(!passthroughEnabled, "left secondary button");
        }

        private void EnsurePassthroughOffForVideo()
        {
            if (passthroughEnabled || (arCameraManager != null && arCameraManager.enabled))
            {
                SetPassthrough(false, "video mode");
            }
            else
            {
                if (mainCamera != null && mainCamera.clearFlags != CameraClearFlags.Skybox)
                {
                    mainCamera.clearFlags = CameraClearFlags.Skybox;
                }
            }
            UpdateStatusText();
        }

        private void SetPassthrough(bool enable, string reason)
        {
            passthroughEnabled = enable;
            if (arCameraManager != null) arCameraManager.enabled = enable;
            if (mainCamera != null) mainCamera.clearFlags = enable ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
            Debug.Log($"[PassthroughController] Passthrough {(enable ? "ENABLED" : "DISABLED")} ({reason}).");
            UpdateStatusText();
        }
    }
}
