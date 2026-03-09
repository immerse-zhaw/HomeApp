using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using Playback;

namespace App
{
    public class HomeReturnHold : MonoBehaviour
    {
        [Header("Hold Settings")]
        [SerializeField] private float holdSeconds = 2.5f;
        [SerializeField] private bool requireReleaseToRetrigger = true;

        [Header("UI")]
        [Tooltip("Image with Fill Method set to Radial360 for hold progress.")]
        [SerializeField] private Image holdProgressImage;
        [Tooltip("Optional root object to show/hide while holding.")]
        [SerializeField] private GameObject holdProgressRoot;

        [Header("References")]
        [SerializeField] private StateMachine stateMachine;
        [SerializeField] private VideoController videoController;
        [SerializeField] private GlbController glbController;

        private float holdTimer = 0f;
        private bool triggeredThisHold = false;

        private void Awake()
        {
            if (stateMachine == null)
            {
                stateMachine = GetComponent<StateMachine>();
            }
            SetProgress(0f);
            SetProgressVisible(false);
        }

        private void Update()
        {
            var rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            bool pressed = rightHand.isValid && rightHand.TryGetFeatureValue(CommonUsages.primaryButton, out bool primaryPressed) && primaryPressed;

            if (pressed)
            {
                if (!triggeredThisHold)
                {
                    holdTimer += Time.deltaTime;
                    float progress = holdSeconds > 0f ? Mathf.Clamp01(holdTimer / holdSeconds) : 1f;
                    SetProgress(progress);
                    SetProgressVisible(true);

                    if (holdTimer >= holdSeconds)
                    {
                        TriggerHomeReturn();
                        triggeredThisHold = true;
                    }
                }
                else
                {
                    SetProgress(1f);
                    SetProgressVisible(true);
                }
            }
            else
            {
                holdTimer = 0f;
                SetProgress(0f);
                SetProgressVisible(false);
                if (requireReleaseToRetrigger)
                {
                    triggeredThisHold = false;
                }
            }
        }

        private void TriggerHomeReturn()
        {
            if (videoController != null)
            {
                videoController.StopVideo();
            }

            if (glbController != null)
            {
                glbController.CloseModel();
            }

            if (stateMachine != null)
            {
                stateMachine.SetAction("none");
                stateMachine.ClearContent();
                stateMachine.SetState(AppState.Idle);
            }

            Debug.Log("[HomeReturnHold] Returning to Home (Idle) state after hold.");
        }

        private void SetProgress(float progress)
        {
            if (holdProgressImage != null)
            {
                holdProgressImage.fillAmount = progress;
            }
        }

        private void SetProgressVisible(bool visible)
        {
            if (holdProgressRoot != null)
            {
                holdProgressRoot.SetActive(visible);
            }
            else if (holdProgressImage != null)
            {
                holdProgressImage.gameObject.SetActive(visible);
            }
        }
    }
}
