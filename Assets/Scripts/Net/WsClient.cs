using NativeWebSocket;
using App;
using UnityEngine;
using System;
using System.Text;
using Net.Messages;

namespace Net
{
    public class WsClient : MonoBehaviour
    {
        private ProjectSettings settings;
        private WebSocket ws;

        private float heartbeatAccumMs;
        private int reconnecAttempt;
        private bool shuttingDown;

        public bool IsOpen => ws != null && ws.State == WebSocketState.Open;

        public event Action<string> OnMessage;

        public void Init(ProjectSettings s)
        {
            settings = s;
            Debug.Log("[WsClient] Initialized.");
        }

        public async void Connect()
        {
            if (settings == null)
            {
                Debug.LogError("[WsClient] Settings not set. Call Init() first.");
                return;
            }

            if (shuttingDown || this == null) return;

            if (ws != null && (ws.State == WebSocketState.Connecting || ws.State == WebSocketState.Open))
            {
                Debug.Log("[WsClient] Already connecting/open.");
                return;
            }

            Debug.Log($"[WsClient] Connecting → {settings.WebsocketUrl}");
            ws = new WebSocket(settings.WebsocketUrl);

            ws.OnOpen += () =>
            {
                SendHello();
                Debug.Log("[WsClient] OPEN");
                reconnecAttempt = 0;
            };

            ws.OnError += (e) =>
            {
                Debug.LogWarning($"[WsClient] ERROR: {e}");
            };

            ws.OnClose += (code) =>
            {
                Debug.Log($"[WsClient] CLOSE ({code})");
                TryScheduleReconnect();
            };

            ws.OnMessage += (data) =>
            {
                string text = Encoding.UTF8.GetString(data);
                OnMessage?.Invoke(text);
            };

            try
            {
                await ws.Connect();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WsClient] Connect exception: {ex.Message}");
                TryScheduleReconnect();
            }
        }

        void Update()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            ws?.DispatchMessageQueue();
#endif
            if (shuttingDown || !IsOpen) return;

            heartbeatAccumMs += Time.deltaTime * 1000f;
            if (heartbeatAccumMs >= settings.PingIntervalMs)
            {
                heartbeatAccumMs = 0f;
                SafeSend("{\"type\":\"ping\"}");
            }
        }

        void OnApplicationQuit()
        {
            shuttingDown = true;
        }

        void OnDestroy()
        {
            shuttingDown = true;
            try
            {
                ws?.Close();
            }
            catch { }
        }

        private void TryScheduleReconnect()
        {
            if (shuttingDown) return;
            if (ws != null && ws.State == WebSocketState.Open) return;

            float min = settings.ReconnectBackoff.x;
            float max = settings.ReconnectBackoff.y;
            float delay = Mathf.Min(max, min * Mathf.Pow(2f, reconnecAttempt));
            reconnecAttempt++;

            Debug.Log($"[WsClient] Reconnect in {delay:0.0}s (attempt {reconnecAttempt})");
            CancelInvoke(nameof(Connect));
            Invoke(nameof(Connect), delay);
        }

        private void SendHello()
        {
            var msg = new HelloMsg();
            msg.device.androidId = SystemInfo.deviceUniqueIdentifier;
            msg.device.model     = SystemInfo.deviceModel;
            msg.app.name         = Application.identifier;
            msg.app.version      = Application.version;
            var json = JsonUtility.ToJson(msg);

            SafeSend(json);
        }

        public void SafeSend(string text)
        {
            if (shuttingDown || !IsOpen) return;
            _ = ws.SendText(text);
            Debug.Log($"[WsClient] >> {text}");
        }
    }
}
