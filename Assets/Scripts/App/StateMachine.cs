using UnityEngine;

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
            Debug.Log($"[StateMachine] {current} → {next}");
            current = next;
        }

        public AppState Current => current;
    }
}