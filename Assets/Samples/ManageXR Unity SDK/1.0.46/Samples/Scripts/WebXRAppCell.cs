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
        static readonly System.Collections.Generic.Dictionary<string, Sprite> ThumbnailCache = new System.Collections.Generic.Dictionary<string, Sprite>();
        Sprite ownedSprite;
        Texture2D ownedTexture;

        [ContextMenu("Refresh")]
        public void Refresh() {
            if (serverAsset != null)
            {
                string modelPath = !string.IsNullOrEmpty(serverAsset.downloadUrl)
                    ? serverAsset.downloadUrl
                    : serverAsset.streamUrl;
                string modelUrl = ServerAssetUtils.BuildAbsoluteUrl(baseUrl, modelPath);
                bool isCached = ContentCache.IsCached("models", serverAsset.id, modelUrl, ".glb");

                if (title != null)
                    title.text = serverAsset.originalFilename + (isCached ? " <color=#00ff66>●</color>" : "");

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
                    if (isBeingDestroyed) {
                        if (result != null) Destroy(result);
                        return;
                    }

                    if (result == null) {
                        icon.sprite = defaultIcon;
                        return;
                    }

                    SetOwnedSprite(result);
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

                string path = !string.IsNullOrEmpty(serverAsset.downloadUrl)
                    ? serverAsset.downloadUrl
                    : serverAsset.streamUrl;

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
            if (ThumbnailCache.TryGetValue(url, out var cachedSprite) && cachedSprite != null)
            {
                ReleaseOwnedImage();
                icon.sprite = cachedSprite;
                icon.preserveAspect = true;
                return;
            }

            new ImageDownloader().Load(url, TextureFormat.ARGB32, true,
                result => {
                    if (isBeingDestroyed) {
                        if (result != null) Destroy(result);
                        return;
                    }

                    if (result == null) {
                        icon.sprite = defaultIcon;
                        return;
                    }

                    result.Apply(false, true);
                    var sprite = Sprite.Create(result, new Rect(0, 0, result.width, result.height), Vector2.one / 2);
                    ThumbnailCache[url] = sprite;
                    ReleaseOwnedImage();
                    icon.sprite = sprite;
                    icon.preserveAspect = true;
                },
                error => icon.sprite = defaultIcon
            );
        }

        void SetOwnedSprite(Texture2D texture)
        {
            ReleaseOwnedImage();
            ownedTexture = texture;
            ownedSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one / 2);
            icon.sprite = ownedSprite;
        }

        void ReleaseOwnedImage()
        {
            if (icon != null && icon.sprite == ownedSprite)
                icon.sprite = defaultIcon;
            if (ownedSprite != null)
                Destroy(ownedSprite);
            if (ownedTexture != null)
                Destroy(ownedTexture);
            ownedSprite = null;
            ownedTexture = null;
        }

        bool isBeingDestroyed = false;
        void OnDestroy() {
            isBeingDestroyed = true;
            ReleaseOwnedImage();
        }
    }
}
