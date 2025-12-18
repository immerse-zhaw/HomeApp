using UnityEngine;

namespace App
{
    /// <summary>
    /// Makes a UI panel lazily follow the user's view with smooth movement
    /// </summary>
    public class LazyFollowPanel : MonoBehaviour
    {
        [Header("Follow Settings")]
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private float distanceFromCamera = 2f;
        [SerializeField] private float followSpeed = 2f;
        [SerializeField] private float rotationSpeed = 3f;
        [SerializeField] private float heightOffset = 0f;
        
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

            // Check if user has turned away from panel
            Vector3 directionToPanel = transform.position - cameraTransform.position;
            directionToPanel.y = 0; // Only consider horizontal rotation
            float angleToPanel = Vector3.Angle(cameraTransform.forward, directionToPanel.normalized);

            // If user has turned beyond activation angle, update target position
            if (angleToPanel > activationAngle)
            {
                UpdateTargetTransform();
            }

            // Smoothly move towards target
            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * followSpeed);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
        }

        private void UpdateTargetTransform()
        {
            Vector3 forward = cameraTransform.forward;
            forward.y = 0; // Keep panel at consistent height
            forward.Normalize();

            targetPosition = cameraTransform.position + forward * distanceFromCamera;
            targetPosition.y = cameraTransform.position.y + heightOffset;

            targetRotation = Quaternion.LookRotation(forward);
        }

        private void PositionPanelInFrontOfUser()
        {
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
    }
}
