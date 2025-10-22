using UnityEngine;

namespace Playback
{
    public class VideoController : MonoBehaviour
    {
        public void PlayVideo(string url, string projection, string stereo, float startTime)
        {
            Debug.Log($"[VideoController] Playing video: {url} | Projection: {projection} | Stereo: {stereo} | StartTime: {startTime}");
            // TODO: Implement
        }

        public void PauseVideo()
        {
            Debug.Log("[VideoController] Pausing video.");
            // TODO: Implement
        }

        public void ResumeVideo()
        {
            Debug.Log("[VideoController] Resuming video.");
            // TODO: Implement
        }

        public void StopVideo()
        {
            Debug.Log("[VideoController] Stopping video.");
            // TODO: Implement
        }

    }
}