using UnityEngine;
using UnityEngine.XR;

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

        [SerializeField] private float rotationSpeed = 90f;   // degrees per second around Y when rotating
        [SerializeField] private float scaleAdjustSpeed = 1.5f; // units per second when nudging scale (0.1x - 10x)

        private enum ControlMode
        {
            Position,
            Scale
        }

        private ControlMode controlMode = ControlMode.Position;
        private bool prevRightPrimaryPressed = false;
        private bool prevLeftPrimaryPressed = false;

        [Header("Reset")]
        [Tooltip("Press the primary button on the left controller to reset the object to its initial transform.")]
        [SerializeField] private bool enableReset = true;

        private bool initialCaptured = false;
        private Vector3 initialPosition = Vector3.zero;
        private Quaternion initialRotation = Quaternion.identity;
        private Vector3 initialLocalScale = Vector3.one;

        private Playback.GlbController glbController;
        // Diagnostics helpers
        [Header("Debug")]
        [Tooltip("Enable to get extra diagnostic logs for device mappings and state.")]
        public bool verboseDebug = true;

        private string lastModelDiag = null;
        private bool prevLeftValid = false;
        private bool prevRightValid = false;

        // Diagnostics timing
        private float nextDiagTime = 0f;
        private float diagInterval = 1f; // seconds between detailed device dumps when verboseDebug is true

        void OnEnable()
        {
            Debug.Log("[GlbMover] Enabled");
        }

        void OnDisable()
        {
            Debug.Log("[GlbMover] Disabled");
        }

        void Start()
        {
            glbController = FindObjectOfType<Playback.GlbController>();
            if (glbController == null)
            {
                Debug.LogWarning("[GlbMover] No GlbController found in scene.");
            }
            else
            {
                glbController.SetScaleUiVisible(false);
                glbController.UpdateControlModeCard(false);
            }
        }

        void Update()
        {
            if (glbController == null) return;

            if (!glbController.HasActiveModel())
            {
                glbController.SetScaleUiVisible(false);
                glbController.SetControlModeCardVisible(false);
                // Reset capture state so we will re-capture when the next model is shown
                initialCaptured = false;
                if (verboseDebug)
                {
                    var diag = glbController.GetModelStateDiagnostic();
                    if (diag != lastModelDiag)
                    {
                        Debug.Log($"[GlbMover] No active model - {diag}");
                        lastModelDiag = diag;
                    }
                }
                return;
            }
            var root = glbController.ModelRoot;
            if (root == null) return;

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

            // If either hand appears invalid (Device Simulator or other runtimes may not expose XRNode devices directly),
            // scan available devices for a controller with matching left/right characteristics as a fallback.
            if ((!leftValid || !rightValid))
            {
                var found = new System.Collections.Generic.List<InputDevice>();
                // Prefer left and right specific characteristics
                if (!leftValid)
                {
                    InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left, found);
                    if (found.Count > 0)
                    {
                        left = found[0];
                        leftValid = left.isValid;
                        if (verboseDebug) Debug.Log($"[GlbMover] Fallback: found left device: '{left.name}' characteristics={left.characteristics}");
                    }
                }

                found.Clear();
                if (!rightValid)
                {
                    InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right, found);
                    if (found.Count > 0)
                    {
                        right = found[0];
                        rightValid = right.isValid;
                        if (verboseDebug) Debug.Log($"[GlbMover] Fallback: found right device: '{right.name}' characteristics={right.characteristics}");
                    }
                }

                // If still missing, log the list of available devices once for diagnostics
                if (verboseDebug && (!leftValid || !rightValid))
                {
                    var all = new System.Collections.Generic.List<InputDevice>();
                    InputDevices.GetDevices(all);
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    sb.AppendLine("[GlbMover] Available XR devices:");
                    foreach (var d in all)
                        sb.AppendLine($"  - name='{d.name}', characteristics={d.characteristics}");
                    Debug.Log(sb.ToString());
                }
            }



            // Apply deadzone (note: we intentionally ignore rightAxis.x - right stick only controls height)
            if (Mathf.Abs(leftAxis.x) < deadzone) leftAxis.x = 0f;
            if (Mathf.Abs(leftAxis.y) < deadzone) leftAxis.y = 0f;
            if (Mathf.Abs(rightAxis.y) < deadzone) rightAxis.y = 0f;
            if (Mathf.Abs(rightAxis.x) < deadzone) rightAxis.x = 0f;

            // Read trigger inputs (some controllers map scale to triggers)
            float leftTrigger = 0f, rightTrigger = 0f;
            if (leftValid) left.TryGetFeatureValue(CommonUsages.trigger, out leftTrigger);
            if (rightValid) right.TryGetFeatureValue(CommonUsages.trigger, out rightTrigger);
            float triggerDelta = rightTrigger - leftTrigger;
            if (Mathf.Abs(triggerDelta) < deadzone) triggerDelta = 0f;

            // Log device validity changes for diagnostics
            if (verboseDebug && (leftValid != prevLeftValid || rightValid != prevRightValid))
            {
                Debug.Log($"[GlbMover] Device validity changed - LeftValid: {leftValid}, RightValid: {rightValid}");
                prevLeftValid = leftValid;
                prevRightValid = rightValid;
            }

            // Periodic detailed diagnostic dump (1 Hz) when verboseDebug is enabled. Shows raw feature values so we can see mapping differences.
            if (verboseDebug && Time.time >= nextDiagTime)
            {
                nextDiagTime = Time.time + diagInterval;
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("[GlbMover] Device diagnostic dump:");

                if (leftValid)
                {
                    sb.AppendLine($"  Left Device: name='{left.name}', characteristics={left.characteristics}");
                    if (left.TryGetFeatureValue(CommonUsages.trigger, out float lt)) sb.AppendLine($"    trigger={lt:F3}");
                    if (left.TryGetFeatureValue(CommonUsages.grip, out float lg)) sb.AppendLine($"    grip={lg:F3}");
                    if (left.TryGetFeatureValue(CommonUsages.triggerButton, out bool ltb)) sb.AppendLine($"    triggerButton={ltb}");
                    if (left.TryGetFeatureValue(CommonUsages.gripButton, out bool lgb)) sb.AppendLine($"    gripButton={lgb}");
                    if (left.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 la)) sb.AppendLine($"    primary2DAxis={la}");
                    if (left.TryGetFeatureValue(CommonUsages.secondary2DAxis, out Vector2 ls)) sb.AppendLine($"    secondary2DAxis={ls}");
                }
                else sb.AppendLine("  Left Device: INVALID");

                if (rightValid)
                {
                    sb.AppendLine($"  Right Device: name='{right.name}', characteristics={right.characteristics}");
                    if (right.TryGetFeatureValue(CommonUsages.trigger, out float rt)) sb.AppendLine($"    trigger={rt:F3}");
                    if (right.TryGetFeatureValue(CommonUsages.grip, out float rg)) sb.AppendLine($"    grip={rg:F3}");
                    if (right.TryGetFeatureValue(CommonUsages.triggerButton, out bool rtb)) sb.AppendLine($"    triggerButton={rtb}");
                    if (right.TryGetFeatureValue(CommonUsages.gripButton, out bool rgb)) sb.AppendLine($"    gripButton={rgb}");
                    if (right.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 ra)) sb.AppendLine($"    primary2DAxis={ra}");
                    if (right.TryGetFeatureValue(CommonUsages.secondary2DAxis, out Vector2 rs)) sb.AppendLine($"    secondary2DAxis={rs}");
                }
                else sb.AppendLine("  Right Device: INVALID");

                Debug.Log(sb.ToString());
            }

            // Detect right-primary button to toggle control mode
            if (rightValid && right.TryGetFeatureValue(CommonUsages.primaryButton, out bool rightPrimary))
            {
                if (rightPrimary && !prevRightPrimaryPressed)
                {
                    ToggleControlMode();
                }

                prevRightPrimaryPressed = rightPrimary;
            }

            // Detect left-primary button for reset (and provide an Editor keyboard fallback)
            bool leftPrimary = false;
            if (leftValid && left.TryGetFeatureValue(CommonUsages.primaryButton, out bool lp))
            {
                leftPrimary = lp;
            }



            // Capture the initial transform when a model becomes active so we have a reset target
            if (glbController.HasActiveModel() && !initialCaptured)
            {
                initialCaptured = true;
                initialPosition = root.position;
                initialRotation = root.rotation;
                initialLocalScale = root.localScale;
                Debug.Log("[GlbMover] Captured initial model transform for reset.");
            }

            // Trigger reset on left-primary button press (edge-triggered)
            if (leftPrimary && !prevLeftPrimaryPressed)
            {
                if (enableReset && glbController.HasActiveModel())
                {
                    ResetToInitial(root);
                }
            }

            prevLeftPrimaryPressed = leftPrimary;

            // If we're in Scale mode, block all position controls (planar + height)
            if (controlMode == ControlMode.Scale)
            {
                leftAxis = Vector2.zero;
                rightAxis.y = 0f; // block height changes
            }

            // No input? nothing to do
            bool hasPlanarInput = leftAxis != Vector2.zero;
            bool hasHeightInput = Mathf.Abs(rightAxis.y) > Mathf.Epsilon;
            bool hasRightXInput = Mathf.Abs(rightAxis.x) > Mathf.Epsilon || Mathf.Abs(triggerDelta) > Mathf.Epsilon;
            if (!hasPlanarInput && !hasHeightInput && !hasRightXInput) return;

            // Debug input for troubleshooting (include device validity flags)
            Debug.Log($"[GlbMover] Input -> Left: {leftAxis} (valid:{leftValid}), RightAxis: {rightAxis} (valid:{rightValid}), Triggers(R-L): {rightTrigger:F2}-{leftTrigger:F2}, Mode: {controlMode}");

            // Movement direction relative to camera forward/right projected to XZ plane
            Vector3 forward = Camera.main != null ? Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 rightDir = Camera.main != null ? Vector3.ProjectOnPlane(Camera.main.transform.right, Vector3.up).normalized : Vector3.right;

            // Compute planar movement (X/Z)
            Vector3 planarDelta = (forward * leftAxis.y + rightDir * leftAxis.x) * moveSpeed * Time.deltaTime;

            // Compute height change (Y)
            float newY = root.position.y + rightAxis.y * heightSpeed * Time.deltaTime;
            newY = Mathf.Clamp(newY, minHeight, maxHeight);

            // Apply position
            Vector3 newPos = root.position + planarDelta;
            newPos.y = newY;
            root.position = newPos;

            // Apply rotation or scale based on current mode. Triggers (right-left) can be used for Scale mode; right stick X still works for rotation/scale.
            float scaleOrYawInput = Mathf.Abs(triggerDelta) > 0.01f ? triggerDelta : rightAxis.x;
            if (Mathf.Abs(scaleOrYawInput) > 0f)
            {
                if (controlMode == ControlMode.Position)
                {
                    float yawDelta = scaleOrYawInput * rotationSpeed * Time.deltaTime;
                    root.Rotate(Vector3.up, yawDelta, Space.World);
                }
                else // Scale mode
                {
                    if (glbController != null)
                    {
                        float scaleDelta = scaleOrYawInput * scaleAdjustSpeed * Time.deltaTime;
                        glbController.AdjustScale(scaleDelta);
                        Debug.Log($"[GlbMover] AdjustScale called with delta={scaleDelta:F4} (input={scaleOrYawInput:F3})");
                    }
                }
            }
        }

        private void ToggleControlMode()
        {
            controlMode = controlMode == ControlMode.Position ? ControlMode.Scale : ControlMode.Position;
            Debug.Log($"[GlbMover] Switched control mode to {controlMode}.");

            if (glbController != null)
            {
                glbController.SetScaleUiVisible(controlMode == ControlMode.Scale);
                glbController.UpdateControlModeCard(controlMode == ControlMode.Scale);
            }
        }

        private void ResetToInitial(Transform root)
        {
            if (root == null) return;
            root.position = initialPosition;
            root.rotation = initialRotation;
            root.localScale = initialLocalScale;
            Debug.Log("[GlbMover] Reset model transform to initial values.");
        }


    }
}