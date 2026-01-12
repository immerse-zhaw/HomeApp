using NativeWebSocket;
using App;
using UnityEngine;
using System;
using System.Text;
using Net.Messages;
using MXR.SDK;

namespace Net
{
    public class WsClient : MonoBehaviour
    {
        private ProjectSettings settings;
        private WebSocket ws;
        private StateMachine state;

        private float heartbeatAccumMs;
        private int reconnecAttempt;
        private bool shuttingDown;

        public bool IsOpen => ws != null && ws.State == WebSocketState.Open;

        public event Action<string> OnMessage;

        public void Init(ProjectSettings s, StateMachine sm)
        {
            settings = s;
            state = sm;
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
                string serial = MXRManager.System?.DeviceStatus?.serial ?? "unknown";
                Debug.Log($"[WsClient] OPEN | Serial: {serial}");
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
                Debug.Log($"[WsClient] << {text}");
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
                // Use the same serial logic as HelloMsg
                var deviceStatus = MXRManager.System?.DeviceStatus;
                string serial = deviceStatus?.serial ?? SystemInfo.deviceUniqueIdentifier;
                string status = GetStatusString();
                string action = GetActionString();
                var (contentName, contentFileId) = GetContentInfo(status);

                var sb = new System.Text.StringBuilder();
                sb.Append("{\"type\":\"ping\"");
                sb.Append($",\"serial\":\"{EscapeJson(serial)}\"");
                sb.Append($",\"status\":\"{EscapeJson(status)}\"");
                if (!string.IsNullOrEmpty(action))
                    sb.Append($",\"action\":\"{EscapeJson(action)}\"");
                if (!string.IsNullOrEmpty(contentName))
                    sb.Append($",\"name\":\"{EscapeJson(contentName)}\"");
                if (!string.IsNullOrEmpty(contentFileId))
                    sb.Append($",\"fileId\":\"{EscapeJson(contentFileId)}\"");
                sb.Append("}");

                string pingJson = sb.ToString();
                SafeSend(pingJson);
            }
        }

        private string GetStatusString()
        {
            var current = state != null ? state.Current : AppState.Idle;
            string statusString = current switch
            {
                AppState.PlayingVideo => "video",
                AppState.ShowingModel => "model",
                _ => "home"
            };
            Debug.Log($"[WsClient] GetStatusString: Current state = {current}, Returning: {statusString}");
            return statusString;
        }

        private string GetActionString()
        {
            if (state == null) return null;
            string action = state.CurrentAction;
            // Treat empty/none or when status is home as no action
            bool isNone = string.IsNullOrWhiteSpace(action) || action == "none";
            if (isNone || GetStatusString() == "home") return null;
            Debug.Log($"[WsClient] GetActionString: Current action = {action}");
            return action;
        }

        private (string name, string fileId) GetContentInfo(string status)
        {
            if (state == null) return (null, null);
            // Only include content info when not in home
            if (status == "home") return (null, null);
            return (state.CurrentContentName, state.CurrentContentFileId);
        }

        private static string EscapeJson(string value)
        {
            return value?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? string.Empty;
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
            var deviceStatus = MXRManager.System?.DeviceStatus;

            // Backend expects the field name androidId; populate it with the MXR serial when available.
            msg.device.androidId    = deviceStatus?.serial ?? SystemInfo.deviceUniqueIdentifier;
            msg.device.serial       = msg.device.androidId;
            msg.device.model        = SystemInfo.deviceModel;
            msg.device.systemStatus = deviceStatus?.deviceSystemVersionStatus != null
                ? deviceStatus.deviceSystemVersionStatus.status.ToString()
                : "unknown";

            msg.app.name    = Application.identifier;
            msg.app.version = Application.version;
            var json = JsonUtility.ToJson(msg);
            
            // Log serial number when sending hello
            Debug.Log($"[WsClient] Sending Hello | Serial: {msg.device.serial}");

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
