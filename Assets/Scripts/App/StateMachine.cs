using UnityEngine;
using MXR.SDK;

namespace App
{
    public enum AppState
    {
        Idle,
        Loading,
        PlayingVideo,
        ShowingModel,
        Error,
    }

    public class StateMachine : MonoBehaviour
    {
        [SerializeField] private AppState current = AppState.Idle;

        public void SetState(AppState next)
        {
            if (current == next) return;
            string serial = MXRManager.System?.DeviceStatus?.serial ?? "unknown";
            Debug.Log($"[StateMachine] {current} → {next} | Serial: {serial}");
            current = next;
        }

        public AppState Current => current;
    }
}