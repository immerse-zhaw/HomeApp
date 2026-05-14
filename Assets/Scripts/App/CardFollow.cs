using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class CardFollow : MonoBehaviour
{
    [Header("Facing")]
    [Tooltip("Optional: assign a camera transform. If left empty, Camera.main will be used.")]
    public Transform cameraTransform;
    [Tooltip("Whether the card should always face the camera.")]
    public bool faceCamera = true;
    [Tooltip("If true, the card will only rotate around the Y axis so it stays upright.")]
    public bool constrainToYAxis = true;
    [Tooltip("How fast the card rotates to face the camera. Set to 0 for instant rotation.")]
    public float smoothSpeed = 10f;

    [Header("Connection Line")]
    [Tooltip("Whether to draw a line connecting the card to a target transform.")]
    public bool drawLine = true;
    [Tooltip("The transform to connect the card to (e.g., the button).")]
    public Transform target;
    [Tooltip("Local offset from the card's transform for the line start.")]
    public Vector3 sourceOffset = Vector3.zero;
    [Tooltip("Local offset from the target transform for the line end.")]
    public Vector3 targetOffset = Vector3.zero;
    [Tooltip("Width of the connecting line (world units).")]
    public float lineWidth = 0.002f;
    [Tooltip("Color of the connecting line.")]
    public Color lineColor = Color.white;

    LineRenderer lr;

    void Awake()
    {
        // Try to assign the main camera if none provided
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        lr = GetComponent<LineRenderer>();
        SetupLineRenderer();
    }

    void OnValidate()
    {
        // Keep the LineRenderer settings in sync in the editor
        if (lr == null) lr = GetComponent<LineRenderer>();
        if (lr != null)
        {
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.startWidth = lr.endWidth = Mathf.Max(0f, lineWidth);
            lr.startColor = lr.endColor = lineColor;
            if (lr.sharedMaterial == null)
                lr.material = new Material(Shader.Find("Sprites/Default"));
        }
    }

    void SetupLineRenderer()
    {
        if (lr == null) return;
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        lr.startWidth = lr.endWidth = Mathf.Max(0f, lineWidth);
        lr.startColor = lr.endColor = lineColor;
        if (lr.material == null)
            lr.material = new Material(Shader.Find("Sprites/Default"));
    }

    void OnDisable()
    {
        if (lr == null) lr = GetComponent<LineRenderer>();
        if (lr != null)
            lr.enabled = false;
    }

    void LateUpdate()
    {
        // Update camera reference if lost at runtime
        if (faceCamera)
        {
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
            if (cameraTransform != null)
            {
                Vector3 dir = cameraTransform.position - transform.position;
                if (constrainToYAxis) dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 180f, 0f);
                    if (smoothSpeed <= 0f) transform.rotation = targetRot;
                    else transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * smoothSpeed);
                }
            }
        }

        // Draw connecting line if requested
        if (lr != null)
        {
            lr.enabled = drawLine && target != null;
            if (drawLine && target != null)
            {
                lr.startWidth = lr.endWidth = Mathf.Max(0f, lineWidth);
                lr.startColor = lr.endColor = lineColor;
                lr.SetPosition(0, transform.TransformPoint(sourceOffset));
                lr.SetPosition(1, target.TransformPoint(targetOffset));
            }
        }
    }
}
