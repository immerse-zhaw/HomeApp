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
        [SerializeField] private string currentAction = "none";

        public void SetState(AppState next)
        {
            if (current == next) return;
            string serial = MXRManager.System?.DeviceStatus?.serial ?? "unknown";
            Debug.Log($"[StateMachine] {current} → {next} | Action: {currentAction} | Serial: {serial}");
            current = next;
        }

        public void SetAction(string nextAction)
        {
            var normalized = string.IsNullOrWhiteSpace(nextAction) ? "none" : nextAction;
            if (currentAction == normalized) return;
            string serial = MXRManager.System?.DeviceStatus?.serial ?? "unknown";
            Debug.Log($"[StateMachine] Action {currentAction} → {normalized} | Serial: {serial}");
            currentAction = normalized;
        }

        public AppState Current => current;
        public string CurrentAction => currentAction;
    }
}