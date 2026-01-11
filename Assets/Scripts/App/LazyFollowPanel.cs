using UnityEngine;

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
        [SerializeField] private float followSpeed = 2f;
        [SerializeField] private float rotationSpeed = 3f;
        [SerializeField] private float heightOffset = 0f;
        // Offset in the camera's local space so the panel moves with the camera
        private Vector3 relativeOffset = Vector3.zero;
        
        [Header("Activation Settings")]
        [Tooltip("Minimum angle difference before panel starts following")]
        [SerializeField] private float activationAngle = 30f;

        private Vector3 targetPosition;
        private Quaternion targetRotation;

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
                PositionPanelInFrontOfUser();
            }
        }

        void Update()
        {
            if (cameraTransform == null) return;

            // Continuously update desired transform based on camera + stored offset
            UpdateTargetTransform();

            // Smoothly move towards target
            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * followSpeed);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
        }

        private void UpdateTargetTransform()
        {
            // If no custom offset yet, use default in-front offset in camera-local space
            if (relativeOffset == Vector3.zero)
            {
                relativeOffset = new Vector3(0f, heightOffset, distanceFromCamera);
            }

            // Convert camera-local offset back to world space
            targetPosition = cameraTransform.TransformPoint(relativeOffset);

            // Always face the camera horizontally
            Vector3 toCamera = cameraTransform.position - targetPosition;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude < 0.0001f)
            {
                toCamera = cameraTransform.forward;
                toCamera.y = 0f;
            }
            targetRotation = Quaternion.LookRotation(-toCamera.normalized);
        }

        private void PositionPanelInFrontOfUser()
        {
            // Reset offset to default in front of user in camera-local space
            relativeOffset = new Vector3(0f, heightOffset, distanceFromCamera);
            UpdateTargetTransform();
            transform.position = targetPosition;
            transform.rotation = targetRotation;
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
            }
        }
    }
}
