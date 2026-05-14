using UnityEngine;
using MXR.SDK;
using System;

namespace App
{
    public enum AppState
    {
        Idle,
        Loading,
        PlayingVideo,
        ShowingModel,
        Error,
        PlayingApp,
    }

    public class StateMachine : MonoBehaviour
    {
        [SerializeField] private AppState current = AppState.Idle;
        [SerializeField] private string currentAction = "none";
        [SerializeField] private string currentContentName = null;
        [SerializeField] private string currentContentFileId = null;

        public event Action Changed;

        public void SetState(AppState next)
        {
            if (current == next) return;
            string serial = DeviceIdentity.GetStableIdentifier();
            Debug.Log($"[StateMachine] {current} → {next} | Action: {currentAction} | Serial: {serial}");
            FileLogger.Log($"[State] {current} -> {next} action={currentAction} content={currentContentName ?? "none"} fileId={currentContentFileId ?? "none"}");
            current = next;
            Changed?.Invoke();
        }

        public void SetAction(string nextAction)
        {
            var normalized = string.IsNullOrWhiteSpace(nextAction) ? "none" : nextAction;
            if (currentAction == normalized) return;
            string serial = DeviceIdentity.GetStableIdentifier();
            Debug.Log($"[StateMachine] Action {currentAction} → {normalized} | Serial: {serial}");
            FileLogger.Log($"[State] action {currentAction} -> {normalized} state={current} content={currentContentName ?? "none"}");
            currentAction = normalized;
            Changed?.Invoke();
        }

        public void SetContent(string name, string fileId)
        {
            var newName = string.IsNullOrWhiteSpace(name) ? null : name;
            var newFileId = string.IsNullOrWhiteSpace(fileId) ? null : fileId;
            if (currentContentName == newName && currentContentFileId == newFileId) return;
            string serial = DeviceIdentity.GetStableIdentifier();
            Debug.Log($"[StateMachine] Content → name: {newName}, fileId: {newFileId} | Serial: {serial}");
            FileLogger.Log($"[State] content name={newName ?? "none"} fileId={newFileId ?? "none"} state={current}");
            currentContentName = newName;
            currentContentFileId = newFileId;
            Changed?.Invoke();
        }

        public void ClearContent()
        {
            if (currentContentName == null && currentContentFileId == null) return;
            string serial = DeviceIdentity.GetStableIdentifier();
            Debug.Log($"[StateMachine] Content cleared | Serial: {serial}");
            FileLogger.Log($"[State] content cleared previous={currentContentName ?? "none"} fileId={currentContentFileId ?? "none"} state={current}");
            currentContentName = null;
            currentContentFileId = null;
            Changed?.Invoke();
        }

        public AppState Current => current;
        public string CurrentAction => currentAction;
        public string CurrentContentName => currentContentName;
        public string CurrentContentFileId => currentContentFileId;
    }
}
