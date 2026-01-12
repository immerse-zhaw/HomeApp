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
        [SerializeField] private string currentContentName = null;
        [SerializeField] private string currentContentFileId = null;

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

        public void SetContent(string name, string fileId)
        {
            var newName = string.IsNullOrWhiteSpace(name) ? null : name;
            var newFileId = string.IsNullOrWhiteSpace(fileId) ? null : fileId;
            if (currentContentName == newName && currentContentFileId == newFileId) return;
            string serial = MXRManager.System?.DeviceStatus?.serial ?? "unknown";
            Debug.Log($"[StateMachine] Content → name: {newName}, fileId: {newFileId} | Serial: {serial}");
            currentContentName = newName;
            currentContentFileId = newFileId;
        }

        public void ClearContent()
        {
            if (currentContentName == null && currentContentFileId == null) return;
            string serial = MXRManager.System?.DeviceStatus?.serial ?? "unknown";
            Debug.Log($"[StateMachine] Content cleared | Serial: {serial}");
            currentContentName = null;
            currentContentFileId = null;
        }

        public AppState Current => current;
        public string CurrentAction => currentAction;
        public string CurrentContentName => currentContentName;
        public string CurrentContentFileId => currentContentFileId;
    }
}