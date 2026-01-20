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
        [SerializeField] private GameObject passthroughCard;

        private bool prevLeftMenuPressed = false;
        private bool passthroughEnabled = false;
        private bool wasEnabledBeforeVideo = false;
        private bool lastInVideoMode = false;

        private StateMachine stateMachine;

        public void Inject(StateMachine sm)
        {
            stateMachine = sm;
        }

        private void Awake()
        {
            passthroughEnabled = arCameraManager != null && arCameraManager.enabled;
            if (passthroughCard == null)
            {
                var card = GameObject.Find("PassthroughCard");
                if (card != null) passthroughCard = card;
            }
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
            bool inVideoMode = stateMachine != null && stateMachine.Current == AppState.PlayingVideo;

            if (inVideoMode)
            {
                if (!lastInVideoMode)
                {
                    wasEnabledBeforeVideo = passthroughEnabled || (arCameraManager != null && arCameraManager.enabled);
                }
                EnsurePassthroughOffForVideo();
                SetPassthroughCardVisible(false);
                prevLeftMenuPressed = false;
                lastInVideoMode = true;
                return;
            }
            else if (lastInVideoMode)
            {
                if (wasEnabledBeforeVideo)
                {
                    SetPassthrough(true, "video ended");
                }
                SetPassthroughCardVisible(true);
                wasEnabledBeforeVideo = false;
                lastInVideoMode = false;
            }

            if (leftHand.isValid && leftHand.TryGetFeatureValue(CommonUsages.menuButton, out bool menuPressed))
            {
                if (menuPressed && !prevLeftMenuPressed)
                {
                    TogglePassthrough();
                }

                prevLeftMenuPressed = menuPressed;
            }
            else
            {
                // Still enforce video-mode passthrough OFF even if input isn't available
                EnsurePassthroughOffForVideo();
            }
        }

        private void TogglePassthrough()
        {
            if (stateMachine != null && stateMachine.Current == AppState.PlayingVideo)
            {
                EnsurePassthroughOffForVideo();
                return;
            }
            SetPassthrough(!passthroughEnabled, "left menu button");
        }

        public void EnablePassthrough(string reason = "external")
        {
            SetPassthrough(true, reason);
        }

        public void DisablePassthrough(string reason = "external")
        {
            SetPassthrough(false, reason);
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

        private void SetPassthroughCardVisible(bool visible)
        {
            if (passthroughCard != null)
            {
                passthroughCard.SetActive(visible);
            }
        }
    }
}
