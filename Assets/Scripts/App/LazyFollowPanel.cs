using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace App
{
    /// <summary>
    /// Makes a UI panel follow the user's view with smooth movement
    /// while preserving any manual (grabbed) repositioning relative to the camera.
    /// </summary>
    public class LazyFollowPanel : MonoBehaviour
    {
        [Header("Follow Settings")]
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private float distanceFromCamera = 2f;
        [SerializeField] private float maxFollowDistance = 10f;
        [SerializeField] private float followSpeed = 2f;
        [SerializeField] private float heightOffset = 0f;
        [SerializeField] private XRGrabInteractable grabInteractable;
        // Offset in the camera's local space so the panel moves with the camera
        private Vector3 relativeOffset = Vector3.zero;
        private bool offsetInitialized;
        private bool warnedMissingGrab;
        
        [Header("Activation Settings")]
        [Tooltip("Minimum angle difference before panel starts following")]
        [SerializeField] private float activationAngle = 30f;

        private Vector3 targetPosition;

        void Start()
        {
            // Auto-find camera if not assigned
            if (cameraTransform == null)
            {
                Camera mainCam = Camera.main;
                if (mainCam != null)
                {
                    cameraTransform = mainCam.transform;
                }
                else
                {
                    Debug.LogWarning("[LazyFollowPanel] No camera assigned and Camera.main not found!");
                }
            }

            // Initialize panel in front of user
            if (cameraTransform != null)
            {
                // Capture the starting offset relative to the camera so we preserve it until the user moves it.
                relativeOffset = cameraTransform.InverseTransformPoint(transform.position);
                offsetInitialized = true;
            }
        }

        void OnEnable()
        {
            EnsureGrabCallbacks();
        }

        void OnDisable()
        {
            if (grabInteractable != null)
            {
                grabInteractable.selectExited.RemoveListener(OnSelectExited);
            }
        }

        void Update()
        {
            if (cameraTransform == null) return;

            if (ShouldFollow())
            {
                // Continuously update desired transform based on camera + stored offset
                UpdateTargetTransform();

                // Smoothly move towards target
                transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * followSpeed);
            }
        }

        private bool ShouldFollow()
        {
            if (cameraTransform == null) return false;
            Vector3 toPanel = transform.position - cameraTransform.position;
            if (toPanel.sqrMagnitude <= 0.0001f) return false;
            float angle = Vector3.Angle(cameraTransform.forward, toPanel.normalized);
            return angle > activationAngle;
        }

        private void UpdateTargetTransform()
        {
            // Lazily initialize offset from current placement if it wasn't set yet
            if (!offsetInitialized)
            {
                relativeOffset = cameraTransform.InverseTransformPoint(transform.position);
                offsetInitialized = true;
            }

            // Convert camera-local offset back to world space
            targetPosition = cameraTransform.TransformPoint(relativeOffset);

            // Constraint: Clamp target position to maxFollowDistance
            Vector3 toTarget = targetPosition - cameraTransform.position;
            if (toTarget.magnitude > maxFollowDistance)
            {
                targetPosition = cameraTransform.position + toTarget.normalized * maxFollowDistance;
            }
        }

        private void PositionPanelInFrontOfUser()
        {
            // Reset offset to default in front of user in camera-local space
            relativeOffset = new Vector3(0f, heightOffset, distanceFromCamera);
            offsetInitialized = true;
            UpdateTargetTransform();
            transform.position = targetPosition;
        }

        /// <summary>
        /// Reposition the panel immediately in front of the user
        /// </summary>
        public void ResetPosition()
        {
            if (cameraTransform != null)
            {
                PositionPanelInFrontOfUser();
            }
        }

        
        // When the panel is released after being grabbed, preserve its offset
        public void PreserveOffsetAfterGrab()
        {
            if (cameraTransform != null)
            {
                // Store the offset in camera-local space so it moves with the camera
                relativeOffset = cameraTransform.InverseTransformPoint(transform.position);
                offsetInitialized = true;
            }
        }

        private void EnsureGrabCallbacks()
        {
            if (grabInteractable == null)
            {
                grabInteractable = GetComponent<XRGrabInteractable>();
            }

            if (grabInteractable != null)
            {
                // Avoid duplicate subscriptions when toggling enabled state
                grabInteractable.selectExited.RemoveListener(OnSelectExited);
                grabInteractable.selectExited.AddListener(OnSelectExited);
            }
            else if (!warnedMissingGrab)
            {
                Debug.LogWarning("[LazyFollowPanel] No XRGrabInteractable found. Offset after grab will not be preserved.");
                warnedMissingGrab = true;
            }
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            PreserveOffsetAfterGrab();
        }
    }
}
