using UnityEngine;
using UnityEngine.UI;
using Playback;
using Launcher;

namespace MXR.SDK.Samples {
    public class VideoCell : MonoBehaviour {
        public Video video;
        public FileInstallStatus status;
        public ServerAsset serverAsset;
        public VideoController videoController;
        public string baseUrl;
        [SerializeField] Text title;
        [SerializeField] Image icon;
        [SerializeField] Image updateIndicator;
        [SerializeField] Image readyIndicator;
        [SerializeField] Text statusLabel;
        [SerializeField] Sprite defaultIcon;
        [SerializeField] Image internetRequirement;
        [SerializeField] Image controllerRequirement;

        [ContextMenu("Refresh")]
        public void Refresh() {
            if (serverAsset != null)
            {
                if (title != null)
                    title.text = serverAsset.originalFilename;

                LoadServerThumbnail();

                if (controllerRequirement != null) controllerRequirement.transform.parent.gameObject.SetActive(false);
                if (internetRequirement != null) internetRequirement.transform.parent.gameObject.SetActive(false);
                if (updateIndicator != null) updateIndicator.enabled = false;
                if (readyIndicator != null) readyIndicator.enabled = false;
                SetStatus(null);
                return;
            }

            title.text = video.title;

            // Instead of MXRStorage.GetFullPath(video.iconPath) you can also use
            // video.iconUrl to download the icon from a URL, like this:
            //new ImageDownloader().Load(video.iconUrl, TextureFormat.ARGB32, true, result =>{}, error =>{});
            new ImageDownloader().Load(MXRStorage.GetFullPath(video.iconPath), TextureFormat.ARGB32, true,
                result => {
                    if (isBeingDestroyed) return;

                    if (result == null) {
                        icon.sprite = defaultIcon;
                        return;
                    }

                    icon.sprite = Sprite.Create(result, new Rect(0, 0, result.width, result.height), Vector2.one / 2);
                    icon.preserveAspect = true;
                },
                error => icon.sprite = defaultIcon
            );

            SetRequirementIcon(controllerRequirement, video.controllersRequired);
            SetRequirementIcon(internetRequirement, video.internetRequired);

            if (status != null) {
                if (status.status == FileInstallStatus.Status.COMPLETE) {
                    SetStatus(null);
                    updateIndicator.enabled = false;
                    readyIndicator.enabled = false; // Luke 6/11 - disabling all readyIndicators this for now  (value was "true")
                } else if (status.status == FileInstallStatus.Status.QUEUED) {
                    SetStatus("Queued...");
                    updateIndicator.enabled = false;
                    readyIndicator.enabled = false;
                } else if (status.status == FileInstallStatus.Status.DOWNLOADING) {
                    SetStatus("Downloading..." + status.progress + "%");
                    updateIndicator.enabled = true;
                    updateIndicator.fillAmount = status.progress / 100f;
                    readyIndicator.enabled = false;
                }
            } else {
                readyIndicator.enabled = false;
                updateIndicator.enabled = false;
            }
        }

        void SetRequirementIcon(Image icon, Content.Requirement requirement) {
            switch (requirement) {
                case Content.Requirement.UNDEFINED:
                    icon.transform.parent.gameObject.SetActive(false);
                    break;
                case Content.Requirement.OPTIONAL:
                    icon.transform.parent.gameObject.SetActive(true);
                    icon.color = Color.yellow;
                    break;
                case Content.Requirement.MANDATORY:
                    icon.transform.parent.gameObject.SetActive(true);
                    icon.color = Color.green;
                    break;
            }
        }

        void SetStatus(string text) {
            if (string.IsNullOrEmpty(text)) {
                statusLabel.transform.parent.GetComponent<Image>().enabled = false;
                statusLabel.text = "";
            } else {
                statusLabel.transform.parent.GetComponent<Image>().enabled = true;
                statusLabel.text = text;
            }
        }

        public void OnClick() {
            if (serverAsset != null)
            {
                if (videoController == null)
                    videoController = FindObjectOfType<VideoController>();

                if (videoController == null)
                {
                    Debug.LogWarning("[VideoCell] VideoController not found in scene.");
                    return;
                }

                string path = !string.IsNullOrEmpty(serverAsset.universalMp4Url)
                    ? serverAsset.universalMp4Url
                    : serverAsset.streamUrl;

                string url = ServerAssetUtils.BuildAbsoluteUrl(baseUrl, path);
                ServerAssetUtils.ParseProjection(serverAsset.videoSettings?.projection, out string projection, out string stereo);
                string mapping = "equirectangular";

                // Set projection first, then play video
                videoController.ChangeProjectionMapping(mapping, projection, stereo);
                videoController.PlayVideo(url, serverAsset.originalFilename, serverAsset.id);
                return;
            }

            Debug.Log($"Play Video titled {video.title} from {MXRStorage.GetFullPath(video.videoPath)}");
        }

        void LoadServerThumbnail()
        {
            if (icon == null) return;

            string thumbPath = (serverAsset.thumbnails != null && serverAsset.thumbnails.Length > 0)
                ? serverAsset.thumbnails[0]
                : null;

            if (string.IsNullOrWhiteSpace(thumbPath))
            {
                icon.sprite = defaultIcon;
                return;
            }

            string url = ServerAssetUtils.BuildAbsoluteUrl(baseUrl, thumbPath);
            new ImageDownloader().Load(url, TextureFormat.ARGB32, true,
                result => {
                    if (isBeingDestroyed) return;

                    if (result == null)
                    {
                        icon.sprite = defaultIcon;
                        return;
                    }

                    // Check if this is a top-bottom stereo video
                    bool isTopBottomStereo = false;
                    
                    if (serverAsset.videoSettings != null && !string.IsNullOrEmpty(serverAsset.videoSettings.projection))
                    {
                        ServerAssetUtils.ParseProjection(serverAsset.videoSettings.projection, out string projection, out string stereo);
                        
                        if (stereo != null)
                        {
                            string stereoLower = stereo.ToLower();
                            isTopBottomStereo = stereoLower == "tb" || 
                                               (stereoLower.Contains("top") && stereoLower.Contains("bottom"));
                        }
                    }

                    Texture2D finalTexture = result;
                    
                    // If top-bottom stereo, crop the upper half and stretch it vertically
                    if (isTopBottomStereo)
                    {
                        int halfHeight = result.height / 2;
                        Color[] topHalfPixels = result.GetPixels(0, halfHeight, result.width, halfHeight);
                        
                        finalTexture = new Texture2D(result.width, result.height, result.format, false);
                        
                        // Stretch the top half to fill the full height
                        for (int y = 0; y < result.height; y++)
                        {
                            for (int x = 0; x < result.width; x++)
                            {
                                int sourceY = Mathf.FloorToInt((float)y / result.height * halfHeight);
                                int sourceIndex = sourceY * result.width + x;
                                finalTexture.SetPixel(x, y, topHalfPixels[sourceIndex]);
                            }
                        }
                        
                        finalTexture.Apply();
                    }

                    icon.sprite = Sprite.Create(finalTexture, new Rect(0, 0, finalTexture.width, finalTexture.height), Vector2.one / 2);
                    icon.preserveAspect = true;
                },
                error => icon.sprite = defaultIcon
            );
        }

        bool isBeingDestroyed = false;
        void OnDestroy() {
            isBeingDestroyed = true;
        }
    }
}
