using UnityEngine;
using UnityEngine.XR;

using UnityEngine.UI;

namespace Playback
{
    // Simple controller that moves the currently-loaded GLB (via GlbController.ModelRoot)
    // Left thumbstick: move in X/Z plane relative to camera forward/right
    // Right thumbstick Y: raise/lower object's Y position (height)
    public class GlbMover : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 1.0f;      // units per second
        [SerializeField] private float heightSpeed = 0.5f;    // units per second for height changes
        [SerializeField] private float deadzone = 0.15f;      // thumbstick deadzone
        [SerializeField] private float minHeight = -5f;
        [SerializeField] private float maxHeight = 5f;
        [SerializeField] private float maxHorizontalDistanceFromUser = 10f;
        [SerializeField] private bool enableEditorControls = true; // allow keyboard fallback for testing in Editor
        [SerializeField] private float rotationSpeed = 90f;   // degrees per second around Y when rotating
        [SerializeField] private float scaleAdjustSpeed = 0.3f; // units per second when nudging scale (0.1x - 10x)

        [Header("Reset")]
        [SerializeField] private bool enableReset = true;

        private bool prevLeftPrimaryPressed = false;

        private bool initialCaptured = false;
        private Vector3 initialPosition = Vector3.zero;
        private Quaternion initialRotation = Quaternion.identity;
        private Vector3 initialLocalScale = Vector3.one;
        private float lastScaleUiTime = -1f;
        private const float ScaleUiVisibleSeconds = 1.5f;

        private Playback.GlbController glbController;

        [Header("Scale UI Buttons")]
        [Tooltip("Background image for the scale up button.")]
        [SerializeField] private Image scaleUpImage;
        [Tooltip("Background image for the scale down button.")]
        [SerializeField] private Image scaleDownImage;
        [Tooltip("Normal color for scale buttons.")]
        [SerializeField] private Color scaleButtonNormalColor = Color.white;
        [Tooltip("Highlight color for scale buttons (when pressed).")]
        [SerializeField] private Color scaleButtonHighlightColor = new Color(0.5f, 0.8f, 1f, 1f);

        private bool wasScalingUp = false;
        private bool wasScalingDown = false;
        private Transform mainCameraTransform;

        void Start()
        {
            glbController = FindObjectOfType<Playback.GlbController>();
            var cam = Camera.main;
            mainCameraTransform = cam != null ? cam.transform : null;
            if (glbController == null)
            {
                Debug.LogWarning("[GlbMover] No GlbController found in scene.");
            }
            else
            {
                glbController.SetScaleUiVisible(false);
            }
        }

        void Update()
        {
            if (glbController == null) return;

            if (!glbController.HasActiveModel())
            {
                glbController.SetScaleUiVisible(false);
                initialCaptured = false;
                return;
            }
            var root = glbController.ModelRoot;
            if (root == null) return;

            // Capture the initial transform once per active model
            if (!initialCaptured)
            {
                initialCaptured = true;
                initialPosition = root.position;
                initialRotation = root.rotation;
                initialLocalScale = root.localScale;
            }

            // Read left thumbstick (left hand) for planar movement
            var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            Vector2 leftAxis = default;
            bool leftValid = left.isValid;
            if (leftValid)
                left.TryGetFeatureValue(CommonUsages.primary2DAxis, out leftAxis);

            // Read right thumbstick (right hand) for height control (we only use Y)
            var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            Vector2 rightAxis = default;
            bool rightValid = right.isValid;
            if (rightValid)
                right.TryGetFeatureValue(CommonUsages.primary2DAxis, out rightAxis);

            // If the right primary axis doesn't contain Y input, try secondary2DAxis (XR Simulator sometimes maps here)
            if (rightValid && Mathf.Abs(rightAxis.y) < Mathf.Epsilon)
            {
                if (right.TryGetFeatureValue(CommonUsages.secondary2DAxis, out Vector2 sec))
                {
                    if (Mathf.Abs(sec.y) > Mathf.Abs(rightAxis.y)) rightAxis.y = sec.y;
                }
            }

            // Editor fallback: safely attempt to read keyboard input. Try the old Input API first in a try/catch,
            // and if it throws (Input System is active), attempt to use the new Input System via reflection.
#if UNITY_EDITOR
            if (enableEditorControls)
            {
                if ((!leftValid || leftAxis == Vector2.zero) || !rightValid)
                {
                    if (TryGetEditorInput(out Vector2 fallbackLeft, out float fallbackRightY))
                    {
                        // Only override axes that were not provided by XR device
                        if (!leftValid || leftAxis == Vector2.zero) leftAxis = fallbackLeft;
                        if (!rightValid) rightAxis.y = fallbackRightY;
                    }
                }
            }
#endif

            // Triggers (scale)
            float leftTrigger = 0f;
            float rightTrigger = 0f;
            if (leftValid) left.TryGetFeatureValue(CommonUsages.trigger, out leftTrigger);
            if (rightValid) right.TryGetFeatureValue(CommonUsages.trigger, out rightTrigger);

            // Detect scale up/down intent
            bool scalingUp = rightTrigger > leftTrigger + 0.01f;
            bool scalingDown = leftTrigger > rightTrigger + 0.01f;

            // Update scale up button color
            if (scaleUpImage != null)
            {
                if (scalingUp && !wasScalingUp)
                    scaleUpImage.color = scaleButtonHighlightColor;
                else if (!scalingUp && wasScalingUp)
                    scaleUpImage.color = scaleButtonNormalColor;
            }
            wasScalingUp = scalingUp;

            // Update scale down button color
            if (scaleDownImage != null)
            {
                if (scalingDown && !wasScalingDown)
                    scaleDownImage.color = scaleButtonHighlightColor;
                else if (!scalingDown && wasScalingDown)
                    scaleDownImage.color = scaleButtonNormalColor;
            }
            wasScalingDown = scalingDown;

            // Apply deadzone (note: right stick controls rotation + height)
            if (Mathf.Abs(leftAxis.x) < deadzone) leftAxis.x = 0f;
            if (Mathf.Abs(leftAxis.y) < deadzone) leftAxis.y = 0f;
            if (Mathf.Abs(rightAxis.y) < deadzone) rightAxis.y = 0f;
            if (Mathf.Abs(rightAxis.x) < deadzone) rightAxis.x = 0f;
            if (leftTrigger < 0.05f) leftTrigger = 0f;
            if (rightTrigger < 0.05f) rightTrigger = 0f;

            // Detect left-primary button for reset (edge-triggered)
            bool leftPrimary = false;
            if (leftValid && left.TryGetFeatureValue(CommonUsages.primaryButton, out bool lp))
            {
                leftPrimary = lp;
            }

            if (leftPrimary && !prevLeftPrimaryPressed && enableReset)
            {
                ResetToInitial(root);
                // Ensure point-cloud LOD matches the reset scale immediately
                if (glbController != null) glbController.RefreshLodFromTransform();
            }

            prevLeftPrimaryPressed = leftPrimary;

            // No input? nothing to do
            bool hasPlanarInput = leftAxis != Vector2.zero;
            bool hasHeightInput = Mathf.Abs(rightAxis.y) > Mathf.Epsilon;
            bool hasRightXInput = Mathf.Abs(rightAxis.x) > Mathf.Epsilon;
            bool hasTriggerInput = Mathf.Abs(rightTrigger - leftTrigger) > 0f;
            if (!hasPlanarInput && !hasHeightInput && !hasRightXInput && !hasTriggerInput) return;

            // Debug input for troubleshooting (include device validity flags)
            //Debug.Log($"[GlbMover] Input -> Left: {leftAxis} (valid:{leftValid}), RightAxis: {rightAxis} (valid:{rightValid}), Triggers: L{leftTrigger:0.00}/R{rightTrigger:0.00}");

            // Movement direction relative to camera forward/right projected to XZ plane
            if (mainCameraTransform == null)
            {
                var cam = Camera.main;
                mainCameraTransform = cam != null ? cam.transform : null;
            }

            Vector3 forward = mainCameraTransform != null ? Vector3.ProjectOnPlane(mainCameraTransform.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 rightDir = mainCameraTransform != null ? Vector3.ProjectOnPlane(mainCameraTransform.right, Vector3.up).normalized : Vector3.right;

            // Compute planar movement (X/Z)
            Vector3 planarDelta = (forward * leftAxis.y + rightDir * leftAxis.x) * moveSpeed * Time.deltaTime;

            // Compute height change (Y) from right stick only when vertical dominates
            float newY = root.position.y;
            if (Mathf.Abs(rightAxis.y) > Mathf.Abs(rightAxis.x))
            {
                newY = root.position.y + rightAxis.y * heightSpeed * Time.deltaTime;
            }
            newY = Mathf.Clamp(newY, minHeight, maxHeight);

            // Apply position
            Vector3 newPos = root.position + planarDelta;
            newPos.y = newY;
            root.position = ClampToUserDistance(newPos);

            // Apply rotation (right stick X) only when horizontal dominates
            if (Mathf.Abs(rightAxis.x) > Mathf.Abs(rightAxis.y))
            {
                float yawDelta = rightAxis.x * rotationSpeed * Time.deltaTime;
                root.Rotate(Vector3.up, yawDelta, Space.World);
            }

            // Apply scale (triggers)
            if (hasTriggerInput)
            {
                float scaleDelta = (rightTrigger - leftTrigger) * scaleAdjustSpeed * Time.deltaTime;
                glbController.AdjustScale(scaleDelta);
                glbController.SetScaleUiVisible(true);
                lastScaleUiTime = Time.time;
                // Keep highlight while pressed
                if (scaleUpImage != null && scalingUp)
                    scaleUpImage.color = scaleButtonHighlightColor;
                if (scaleDownImage != null && scalingDown)
                    scaleDownImage.color = scaleButtonHighlightColor;
            }

            if (lastScaleUiTime > 0f && Time.time - lastScaleUiTime > ScaleUiVisibleSeconds)
            {
                glbController.SetScaleUiVisible(false);
                lastScaleUiTime = -1f;
                // Reset button colors
                if (scaleUpImage != null) scaleUpImage.color = scaleButtonNormalColor;
                if (scaleDownImage != null) scaleDownImage.color = scaleButtonNormalColor;
            }
        }

        private void ResetToInitial(Transform root)
        {
            if (root == null) return;
            if (glbController != null && glbController.TryGetCameraSpawnPosition(out var spawnPos))
            {
                root.position = ClampToUserDistance(spawnPos);
            }
            else
            {
                root.position = ClampToUserDistance(initialPosition);
            }
            root.rotation = initialRotation;
            root.localScale = initialLocalScale;
            if (glbController != null)
            {
                glbController.SetScale(1f);
                glbController.RefreshLodFromTransform();
            }
        }

        private Vector3 ClampToUserDistance(Vector3 worldPosition)
        {
            if (maxHorizontalDistanceFromUser <= 0f)
                return worldPosition;

            if (mainCameraTransform == null)
            {
                var cam = Camera.main;
                mainCameraTransform = cam != null ? cam.transform : null;
            }

            if (mainCameraTransform == null)
                return worldPosition;

            Vector3 origin = mainCameraTransform.position;
            Vector3 offset = worldPosition - origin;
            Vector2 horizontal = new Vector2(offset.x, offset.z);
            float distance = horizontal.magnitude;
            if (distance <= maxHorizontalDistanceFromUser || distance <= 0.0001f)
                return worldPosition;

            Vector2 clamped = horizontal / distance * maxHorizontalDistanceFromUser;
            return new Vector3(origin.x + clamped.x, worldPosition.y, origin.z + clamped.y);
        }

        // Try to get keyboard fallback input in editor. Works with both the old Input API and the new Input System (via reflection).
        private bool TryGetEditorInput(out Vector2 leftAxis, out float rightY)
        {
            leftAxis = Vector2.zero;
            rightY = 0f;

            // First try the old Input API (may throw if the Input System package is active)
            try
            {
                float h = Input.GetAxis("Horizontal");
                float v = Input.GetAxis("Vertical");
                leftAxis = new Vector2(h, v);

                float r = 0f;
                if (Input.GetKey(KeyCode.E)) r += 1f;
                if (Input.GetKey(KeyCode.Q)) r -= 1f;
                rightY = r;

                return leftAxis != Vector2.zero || rightY != 0f;
            }
            catch
            {
                // Old Input API not available (Input System active). Fall back to the new Input System via reflection.
                try
                {
                    var keyboardType = System.Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem");
                    if (keyboardType == null) return false;

                    var currentProp = keyboardType.GetProperty("current", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                    var keyboard = currentProp?.GetValue(null);
                    if (keyboard == null) return false;

                    bool w = (bool)keyboard.GetType().GetProperty("wKey").GetValue(keyboard).GetType().GetProperty("isPressed").GetValue(keyboard.GetType().GetProperty("wKey").GetValue(keyboard));
                    bool s = (bool)keyboard.GetType().GetProperty("sKey").GetValue(keyboard).GetType().GetProperty("isPressed").GetValue(keyboard.GetType().GetProperty("sKey").GetValue(keyboard));
                    bool a = (bool)keyboard.GetType().GetProperty("aKey").GetValue(keyboard).GetType().GetProperty("isPressed").GetValue(keyboard.GetType().GetProperty("aKey").GetValue(keyboard));
                    bool d = (bool)keyboard.GetType().GetProperty("dKey").GetValue(keyboard).GetType().GetProperty("isPressed").GetValue(keyboard.GetType().GetProperty("dKey").GetValue(keyboard));
                    bool e = (bool)keyboard.GetType().GetProperty("eKey").GetValue(keyboard).GetType().GetProperty("isPressed").GetValue(keyboard.GetType().GetProperty("eKey").GetValue(keyboard));
                    bool q = (bool)keyboard.GetType().GetProperty("qKey").GetValue(keyboard).GetType().GetProperty("isPressed").GetValue(keyboard.GetType().GetProperty("qKey").GetValue(keyboard));

                    float hx = 0f;
                    float vy = 0f;
                    if (a) hx -= 1f;
                    if (d) hx += 1f;
                    if (w) vy += 1f;
                    if (s) vy -= 1f;
                    if (e) rightY += 1f;
                    if (q) rightY -= 1f;

                    leftAxis = new Vector2(hx, vy);
                    return leftAxis != Vector2.zero || rightY != 0f;
                }
                catch
                {
                    // Failed to get any fallback input
                    return false;
                }
            }
        }
    }
}
