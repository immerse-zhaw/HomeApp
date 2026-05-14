using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using TMPro;

using UnityEngine.UI;

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

        [Header("Passthrough Icon")]
        [Tooltip("Image component to update when passthrough is toggled.")]
        [SerializeField] private Image passthroughImage;
        [Tooltip("Sprite to show when passthrough is ON.")]
        [SerializeField] private Sprite passthroughOnSprite;
        [Tooltip("Sprite to show when passthrough is OFF.")]
        [SerializeField] private Sprite passthroughOffSprite;

        [Header("Controller Visuals")]
        [Tooltip("Left Controller Visual")]
        [SerializeField] private GameObject leftControllerModel;
        [Tooltip("Right Controller Visual")]
        [SerializeField] private GameObject rightControllerModel;

        [Header("Startup Settings")]
        [SerializeField] private bool enablePassthroughOnStart = true;
        [SerializeField] private float watchdogIntervalSeconds = 0.5f;

        private bool prevLeftMenuPressed = false;
        private bool passthroughEnabled = false;
        private bool desiredPassthrough = true;
        private bool wasEnabledBeforeVideo = true;
        private bool lastInVideoMode = false;
        private float nextWatchdogTime;

        private StateMachine stateMachine;

        public void Inject(StateMachine sm)
        {
            stateMachine = sm;
        }

        public void Configure(bool defaultEnabled)
        {
            desiredPassthrough = defaultEnabled;
            enablePassthroughOnStart = defaultEnabled;
            ApplyStateControlledPassthrough("config");
        }

        private void Awake()
        {
            if (passthroughCard == null)
            {
                var card = GameObject.Find("PassthroughCard");
                if (card != null) passthroughCard = card;
            }

            desiredPassthrough = enablePassthroughOnStart;
            SetPassthrough(desiredPassthrough, "startup");
        }

        private void UpdateStatusText()
        {
            if (passthroughStatusText == null) return;
            passthroughStatusText.text = passthroughEnabled ? "Passthrough: ON" : "Passthrough: OFF";
        }

        private void Update()
        {
            bool inVideoMode = stateMachine != null && stateMachine.Current == AppState.PlayingVideo;

            if (inVideoMode)
            {
                if (!lastInVideoMode)
                {
                    wasEnabledBeforeVideo = desiredPassthrough;
                    lastInVideoMode = true;
                }
                EnsurePassthroughOff("video mode");
                SetPassthroughCardVisible(false);
                prevLeftMenuPressed = false;
                return;
            }

            if (lastInVideoMode)
            {
                desiredPassthrough = wasEnabledBeforeVideo;
                lastInVideoMode = false;
                SetPassthroughCardVisible(true);
                SetPassthrough(desiredPassthrough, "video ended");
            }

            if (desiredPassthrough && Time.unscaledTime >= nextWatchdogTime)
            {
                nextWatchdogTime = Time.unscaledTime + Mathf.Max(0.1f, watchdogIntervalSeconds);
                EnsurePassthroughOn("watchdog");
            }

            var leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

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
                prevLeftMenuPressed = false;
            }
        }

        private void TogglePassthrough()
        {
            desiredPassthrough = !desiredPassthrough;
            ApplyStateControlledPassthrough("left menu button");
        }

        public void EnablePassthrough(string reason = "external")
        {
            desiredPassthrough = true;
            ApplyStateControlledPassthrough(reason);
        }

        public void DisablePassthrough(string reason = "external")
        {
            desiredPassthrough = false;
            ApplyStateControlledPassthrough(reason);
        }

        private void EnsurePassthroughOn(string reason)
        {
            if (!passthroughEnabled)
            {
                SetPassthrough(true, reason);
            }
        }

        private void EnsurePassthroughOff(string reason)
        {
            if (passthroughEnabled)
            {
                SetPassthrough(false, reason);
            }
            else if (mainCamera != null && mainCamera.clearFlags != CameraClearFlags.Skybox)
            {
                mainCamera.clearFlags = CameraClearFlags.Skybox;
            }
        }

        private void ApplyStateControlledPassthrough(string reason)
        {
            bool inVideoMode = stateMachine != null && stateMachine.Current == AppState.PlayingVideo;
            if (inVideoMode)
            {
                EnsurePassthroughOff(reason);
                return;
            }

            SetPassthrough(desiredPassthrough, reason);
        }

        private void SetPassthrough(bool enable, string reason)
        {
            passthroughEnabled = enable;

            if (mainCamera != null)
            {
                mainCamera.clearFlags = enable ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
                if (enable) mainCamera.backgroundColor = Color.clear;
            }
            Debug.Log($"[PassthroughController] Passthrough {(enable ? "ENABLED" : "DISABLED")} ({reason}).");
            UpdateStatusText();

            // Update passthrough image sprite
            if (passthroughImage != null)
            {
                passthroughImage.sprite = enable ? passthroughOnSprite : passthroughOffSprite;
            }
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
