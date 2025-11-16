using System.Net.WebSockets;
using App;
using UnityEngine;
using System;
using System.Text;
using Net.Messages;
using System.Threading;
using System.Threading.Tasks;

namespace Net
{
    public class WsClient : MonoBehaviour
    {
        private ProjectSettings settings;
        private ClientWebSocket ws;

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
            ws = new ClientWebSocket();
            try
            {
                var uri = new Uri(settings.WebsocketUrl.Replace("ws://", "wss://"));
                await ws.ConnectAsync(uri, CancellationToken.None);
                Debug.Log("[WsClient] OPEN");
                reconnecAttempt = 0;
                SendHello();
                _ = ReceiveLoop();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WsClient] Connect exception: {ex.Message}");
                TryScheduleReconnect();
            }
        }

        void Update()
        {
            if (shuttingDown || !IsOpen) return;

            heartbeatAccumMs += Time.deltaTime * 1000f;
            if (heartbeatAccumMs >= settings.PingIntervalMs)
            {
                heartbeatAccumMs = 0f;
                SafeSend("{\"type\":\"ping\"}");
            }
            async void Update()
            {
    #if !UNITY_WEBGL || UNITY_EDITOR
                // ws?.DispatchMessageQueue(); // Not needed for ClientWebSocket
    #endif
                if (shuttingDown || !IsOpen) return;

                heartbeatAccumMs += Time.deltaTime * 1000f;
                if (heartbeatAccumMs >= settings.PingIntervalMs)
                {
                    heartbeatAccumMs = 0f;
                    await SafeSend("{\"type\":\"ping\"}");
                }
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
                ws?.Abort();
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

        private async void SendHello()
        {
            var msg = new HelloMsg();
            msg.device.androidId = SystemInfo.deviceUniqueIdentifier;
            msg.device.model     = SystemInfo.deviceModel;
            msg.app.name         = Application.identifier;
            msg.app.version      = Application.version;
            var json = JsonUtility.ToJson(msg);

            await SafeSend(json);
        }

        public async Task SafeSend(string text)
        {
            if (shuttingDown || !IsOpen) return;
            try
            {
                var buffer = Encoding.UTF8.GetBytes(text);
                await ws.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                Debug.Log($"[WsClient] >> {text}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WsClient] Send exception: {ex.Message}");
            }
        }
        private async Task ReceiveLoop()
        {
            var buffer = new byte[4096];
            while (ws != null && ws.State == WebSocketState.Open && !shuttingDown)
            {
                try
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Debug.Log("[WsClient] CLOSE (remote)");
                        TryScheduleReconnect();
                        break;
                    }
                    else if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        OnMessage?.Invoke(text);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[WsClient] Receive exception: {ex.Message}");
                    TryScheduleReconnect();
                    break;
                }
            }
        }
    }
}
