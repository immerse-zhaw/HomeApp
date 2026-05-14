using UnityEngine;

namespace App
{
    [CreateAssetMenu(fileName = "ProjectSettings", menuName = "App/Project Settings")]
    public class ProjectSettings : ScriptableObject
    {
        [Header("Network")]
        [SerializeField] private string websocketUrl = "wss://example.com/ws";
        [SerializeField] private string websiteUrl = "http://example.com";
        [SerializeField, Min(1000)] private int pingIntervalMs = 1500;
        [Tooltip("Min-Max seconds between reconnect attempts (exponential backoff).")]
        [SerializeField] private Vector2 reconnectBackoff = new Vector2(1f, 20f);

        [Header("Features")]
        [SerializeField] private bool enableAppLauncher = true;
        [SerializeField] private bool enableVideoControls = true;
        [SerializeField] private bool enableGlbControls = true;

        [Header("Performance")]
        [SerializeField] private bool verboseLogging = false;
        [SerializeField] private bool lightweightManageXrInit = true;
        [SerializeField] private bool defaultPassthroughEnabled = true;
        [SerializeField, Range(1f, 1.75f)] private float xrRenderScale = 1.1f;
        [SerializeField, Range(1f, 16f)] private float worldCanvasDynamicPixelsPerUnit = 2f;
        [SerializeField, Range(1f, 4f)] private float worldCanvasGeometryDensity = 2f;
        [SerializeField, Min(60)] private int targetFrameRate = 90;

        public string WebsocketUrl => websocketUrl;
        public string WebsiteUrl => websiteUrl;
        public int PingIntervalMs => pingIntervalMs;
        public Vector2 ReconnectBackoff => reconnectBackoff;
        public bool EnableAppLauncher => enableAppLauncher;
        public bool EnableVideoControls => enableVideoControls;
        public bool EnableGlbControls => enableGlbControls;
        public bool VerboseLogging => verboseLogging;
        public bool LightweightManageXrInit => lightweightManageXrInit;
        public bool DefaultPassthroughEnabled => defaultPassthroughEnabled;
        public float XrRenderScale => xrRenderScale;
        public float WorldCanvasDynamicPixelsPerUnit => worldCanvasDynamicPixelsPerUnit;
        public float WorldCanvasGeometryDensity => worldCanvasGeometryDensity;
        public int TargetFrameRate => targetFrameRate;

        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(websocketUrl))
            {
                Debug.LogWarning("[ProjectSettings] websocketUrl is empty.");
            }
            if (reconnectBackoff.y < reconnectBackoff.x)
            {
                reconnectBackoff.y = reconnectBackoff.x;
            }
        } 
    }
}
