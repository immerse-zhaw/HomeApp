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

        public string WebsocketUrl => websocketUrl;
        public string WebsiteUrl => websiteUrl;
        public int PingIntervalMs => pingIntervalMs;
        public Vector2 ReconnectBackoff => reconnectBackoff;
        public bool EnableAppLauncher => enableAppLauncher;
        public bool EnableVideoControls => enableVideoControls;
        public bool EnableGlbControls => enableGlbControls;

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
