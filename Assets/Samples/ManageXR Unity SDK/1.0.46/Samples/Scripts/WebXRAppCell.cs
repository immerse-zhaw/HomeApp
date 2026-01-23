using UnityEngine;
using UnityEngine.UI;
using Playback;
using Launcher;

namespace MXR.SDK.Samples {
    public class WebXRAppCell : MonoBehaviour {
        public WebXRApp webXRApp;
        public ServerAsset serverAsset;
        public GlbController glbController;
        public string baseUrl;
        [SerializeField] Text title;
        [SerializeField] Image icon;
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
                return;
            }

            title.text = webXRApp.title;

            // Instead of MXRStorage.GetFullPath(webXRApp.iconPath) you can also use
            // webXRApp.iconUrl to download the icon from a URL, like this:
            //new ImageDownloader().Load(webXRApp.iconUrl, TextureFormat.ARGB32, true, result =>{}, error =>{});

            new ImageDownloader().Load(MXRStorage.GetFullPath(webXRApp.iconPath), TextureFormat.ARGB32, true,
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

            SetRequirementIcon(controllerRequirement, webXRApp.controllersRequired);
            SetRequirementIcon(internetRequirement, webXRApp.internetRequired);
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

        public void OnClick() {
            if (serverAsset != null)
            {
                if (glbController == null)
                    glbController = FindObjectOfType<GlbController>();

                if (glbController == null)
                {
                    Debug.LogWarning("[WebXRAppCell] GlbController not found in scene.");
                    return;
                }

                string path = !string.IsNullOrEmpty(serverAsset.streamUrl)
                    ? serverAsset.streamUrl
                    : serverAsset.downloadUrl;

                string url = ServerAssetUtils.BuildAbsoluteUrl(baseUrl, path);
                glbController.LoadModel(url, serverAsset.originalFilename, serverAsset.id);
                return;
            }

            if (!string.IsNullOrEmpty(webXRApp.url)) {
                Debug.Log("Open URL " + webXRApp.url);
                Application.OpenURL(webXRApp.url);
            }
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

                    if (result == null) {
                        icon.sprite = defaultIcon;
                        return;
                    }

                    icon.sprite = Sprite.Create(result, new Rect(0, 0, result.width, result.height), Vector2.one / 2);
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
