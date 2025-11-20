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
        [SerializeField] private Material floor;

        private Camera mainCamera;
        private CameraClearFlags originalClearFlags;
        private Color originalBackgroundColor;

        public void Awake()
        {
            // Find main camera (could be XR camera)
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                mainCamera = FindObjectOfType<Camera>();
            }

            if (mainCamera != null)
            {
                originalClearFlags = mainCamera.clearFlags;
                originalBackgroundColor = mainCamera.backgroundColor;
            }

            RenderSettings.skybox = skyboxDefault; 
            SetFloorAlpha(1f);
        }

        public void PlayVideo(string url, string mapping, string projection, string stereo)
        {   
            SetFloorAlpha(0.1f);
            videoPlayer.url = url;
            videoPlayer.Play();

            // Set camera to render skybox for 360 video
            if (mainCamera != null)
            {
                mainCamera.clearFlags = CameraClearFlags.Skybox;
            }

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

            // Restore original camera settings for passthrough
            if (mainCamera != null)
            {
                mainCamera.clearFlags = originalClearFlags;
                mainCamera.backgroundColor = originalBackgroundColor;
            }

            SetFloorAlpha(1f);
            Debug.Log("[VideoController] Stopping video.");
        }
        public void SetFloorAlpha(float alpha)
        {
            if (floor != null)
            {
                Color color = floor.color;
                color.a = Mathf.Clamp01(alpha);
                floor.color = color;
            }
        }
    }
}