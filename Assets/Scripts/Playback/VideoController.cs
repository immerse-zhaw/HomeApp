using UnityEngine;
using UnityEngine.Video;

namespace Playback
{
    public class VideoController : MonoBehaviour
    {
        [Header("Components")]
        [SerializeField] private VideoPlayer videoPlayer;

        [Header("Skybox Materials")]
        [SerializeField] private Material skyboxDefault;
        [SerializeField] private Material skyboxEquirect;
        [SerializeField] private Material skyboxCubemap;

        public void Awake()
        {
            RenderSettings.skybox = skyboxDefault; 
        }

        public void PlayVideo(string url, string mapping, string projection, string stereo)
        {
            videoPlayer.url = url;
            videoPlayer.Play();

            bool useCube = mapping.ToLower().Contains("cube");
            RenderSettings.skybox = useCube ? skyboxCubemap : skyboxEquirect;

            if (videoPlayer.targetTexture)
                RenderSettings.skybox.SetTexture("_MainTex", videoPlayer.targetTexture);

            RenderSettings.skybox.SetInt("_ImageType", projection.Contains("180") ? 1 : 0);

            int layout = stereo switch
            {
                "sbs" or "lr" or "sidebyside" => 1,
                "tb" or "overunder" or "topbottom" => 2,
                _ => 0
            };
            RenderSettings.skybox.SetInt("_Layout", layout);

            Debug.Log($"[VideoController] Playing video: {url} | Mapping: {mapping} | Projection: {projection} | Stereo: {stereo}");
        }

        public void ChangeProjectionMapping(string mapping, string projection, string stereo)
        {
            bool useCube = mapping.ToLower().Contains("cube");
            RenderSettings.skybox = useCube ? skyboxCubemap : skyboxEquirect;

            RenderSettings.skybox.SetInt("_ImageType", projection.Contains("180") ? 1 : 0);

            int layout = stereo switch
            {
                "sbs" or "lr" or "sidebyside" => 1,
                "tb" or "overunder" or "topbottom" => 2,
                _ => 0
            };
            RenderSettings.skybox.SetInt("_Layout", layout);
            Seek(0);

            Debug.Log($"[VideoController] Changed Projection: Mapping: {mapping} | Projection: {projection} | Stereo: {stereo}");
        }

        public void Seek(double timeCode)
        {
            if (videoPlayer.isPrepared)
            {
                videoPlayer.time = timeCode;
                Debug.Log($"[VideoController] Set timecode to {timeCode}.");
            }
        }

        public void PauseVideo()
        {
            videoPlayer.Pause();
            Debug.Log("[VideoController] Pausing video.");
        }

        public void ResumeVideo()
        {
            videoPlayer.Play();
            Debug.Log("[VideoController] Resuming video.");
        }

        public void StopVideo()
        {
            videoPlayer.Stop();
            RenderSettings.skybox = skyboxDefault; 
            Debug.Log("[VideoController] Stopping video.");
        }
    }
}