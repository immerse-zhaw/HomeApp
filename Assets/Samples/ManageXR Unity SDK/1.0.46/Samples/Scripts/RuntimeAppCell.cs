using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using App;

using UnityEngine.UI;

namespace MXR.SDK.Samples {
    public class RuntimeAppCell : MonoBehaviour {
        public RuntimeApp runtimeApp;
        public AppInstallStatus status;

        [SerializeField] Text title;
        [SerializeField] Image icon;
        [SerializeField] Image updateIndicator;
        [SerializeField] Image readyIndicator;
        [SerializeField] Text statusLabel;
        [SerializeField] Sprite defaultIcon;
        [SerializeField] Image internetRequirement;
        [SerializeField] Image controllerRequirement;
        [SerializeField] bool loadIconImages = true;
        static readonly System.Collections.Generic.Dictionary<string, Sprite> IconCache = new System.Collections.Generic.Dictionary<string, Sprite>();
        Coroutine iconLoadCoroutine;
        StateMachine stateMachine;

        [ContextMenu("Refresh")]
        public void Refresh() {
            title.text = runtimeApp.title;

            if (!loadIconImages)
            {
                icon.sprite = defaultIcon;
                icon.preserveAspect = true;
                RefreshStatusOnly();
                return;
            }

            // Instead of MXRStorage.GetFullPath(runtimeApp.iconPath) you can also use
            // runtimeApp.iconUrl to download the icon from a URL, like this:
            //new ImageDownloader().Load(runtimeApp.iconUrl, TextureFormat.ARGB32, true, result =>{}, error =>{});
            string iconLocation = ResolveIconLocation();
            if (!string.IsNullOrEmpty(iconLocation) && IconCache.TryGetValue(iconLocation, out var cachedSprite))
            {
                icon.sprite = cachedSprite;
                icon.preserveAspect = true;
                RefreshStatusOnly();
                return;
            }

            icon.sprite = defaultIcon;
            icon.preserveAspect = true;
            RefreshStatusOnly();
            if (iconLoadCoroutine != null)
            {
                StopCoroutine(iconLoadCoroutine);
            }
            iconLoadCoroutine = StartCoroutine(LoadIconDeferred(iconLocation));
            return;
        }

        IEnumerator LoadIconDeferred(string iconLocation)
        {
            int siblingIndex = transform.GetSiblingIndex();
            if (siblingIndex > 0)
            {
                yield return new WaitForSecondsRealtime(Mathf.Min(0.5f, siblingIndex * 0.025f));
            }
            else
            {
                yield return null;
            }

            if (isBeingDestroyed) yield break;
            if (string.IsNullOrEmpty(iconLocation))
            {
                FileLogger.LogWarning($"[RuntimeAppCell] No icon path/url for {runtimeApp?.title ?? "unknown"} ({runtimeApp?.packageName ?? "unknown"})");
                yield break;
            }

            if (IsHttpUrl(iconLocation))
            {
                yield return LoadIconFromUrl(iconLocation);
            }
            else
            {
                LoadIconFromDisk(iconLocation);
            }
        }

        IEnumerator LoadIconFromUrl(string url)
        {
            using (var req = UnityWebRequestTexture.GetTexture(url, true))
            {
                yield return req.SendWebRequest();

                if (isBeingDestroyed) yield break;

                if (req.result != UnityWebRequest.Result.Success)
                {
                    FileLogger.LogWarning($"[RuntimeAppCell] Icon URL failed for {runtimeApp?.title ?? "unknown"}: {req.error}");
                    icon.sprite = defaultIcon;
                    yield break;
                }

                var texture = DownloadHandlerTexture.GetContent(req);
                SetIconTexture(url, texture);
            }
        }

        void LoadIconFromDisk(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    FileLogger.LogWarning($"[RuntimeAppCell] Icon file missing for {runtimeApp?.title ?? "unknown"}: {path}");
                    icon.sprite = defaultIcon;
                    return;
                }

                var texture = ImageDownloader.LoadFromDisk(path, TextureFormat.RGBA32, true);
                SetIconTexture(path, texture);
            }
            catch (System.Exception ex)
            {
                FileLogger.LogWarning($"[RuntimeAppCell] Icon disk load failed for {runtimeApp?.title ?? "unknown"}: {ex.Message}");
                icon.sprite = defaultIcon;
            }
        }

        void SetIconTexture(string cacheKey, Texture2D texture)
        {
            if (isBeingDestroyed)
            {
                if (texture != null) Destroy(texture);
                return;
            }

            if (texture == null)
            {
                icon.sprite = defaultIcon;
                return;
            }

            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one / 2);
            IconCache[cacheKey] = sprite;
            icon.sprite = sprite;
            icon.preserveAspect = true;
            FileLogger.Log($"[RuntimeAppCell] Icon loaded for {runtimeApp?.title ?? "unknown"} from {(IsHttpUrl(cacheKey) ? "url" : "disk")} {texture.width}x{texture.height}");
        }

        string ResolveIconLocation()
        {
            if (runtimeApp == null) return null;

            if (!string.IsNullOrWhiteSpace(runtimeApp.iconPath))
            {
                string localPath = MXRStorage.GetFullPath(runtimeApp.iconPath);
                if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
                    return localPath;
            }

            if (!string.IsNullOrWhiteSpace(runtimeApp.iconUrl))
                return runtimeApp.iconUrl;

            if (!string.IsNullOrWhiteSpace(runtimeApp.iconPath))
                return MXRStorage.GetFullPath(runtimeApp.iconPath);

            return null;
        }

        static bool IsHttpUrl(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && (value.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase));
        }

        void RefreshStatusOnly()
        {
            SetRequirementIcon(controllerRequirement, runtimeApp.controllersRequired);
            SetRequirementIcon(internetRequirement, runtimeApp.internetRequired);

            if (status != null && status.IsUpdating())
            {
                updateIndicator.enabled = true;
                updateIndicator.fillAmount = status.progress / 100f;
                SetStatus(status.status + "..." + status.progress + "%");
            }
            else
            {
                updateIndicator.enabled = false;
                readyIndicator.enabled = false;
                SetStatus(null);
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
            if (runtimeApp == null) {
                FileLogger.LogError("[RuntimeAppCell] Cannot launch: runtimeApp is null");
                return;
            }

            FileLogger.Log($"[RuntimeAppCell] ===== APP CLICK =====");
            FileLogger.Log($"[RuntimeAppCell] App Title: {runtimeApp.title}");
            FileLogger.Log($"[RuntimeAppCell] Package Name: {runtimeApp.packageName}");
            FileLogger.Log($"[RuntimeAppCell] Class Name: {runtimeApp.className}");
            FileLogger.Log($"[RuntimeAppCell] Platform: {Application.platform}");
            
            if (status != null) {
                FileLogger.Log($"[RuntimeAppCell] MXR Status: {status.status}");
                FileLogger.Log($"[RuntimeAppCell] Current Version: {status.currentVersion}");
                FileLogger.Log($"[RuntimeAppCell] Version Name: {status.currentVersionName}");
            } else {
                FileLogger.LogWarning("[RuntimeAppCell] Status is null - cannot verify installation state");
            }

            if (Application.platform == RuntimePlatform.Android) {
                bool isInstalled = MXRAndroidUtils.IsAppInstalled(runtimeApp.packageName);
                FileLogger.Log($"[RuntimeAppCell] Android IsAppInstalled check: {isInstalled}");
                
                if (isInstalled) {
                    if (stateMachine == null)
                        stateMachine = FindObjectOfType<StateMachine>();
                    stateMachine?.SetState(AppState.PlayingApp);
                    stateMachine?.SetAction("launched");
                    stateMachine?.SetContent(runtimeApp.title, runtimeApp.packageName);
                    FileLogger.Log($"[RuntimeAppCell] Attempting to launch {runtimeApp.title}...");
                    MXRAndroidUtils.LaunchRuntimeApp(runtimeApp);
                    FileLogger.Log($"[RuntimeAppCell] Launch command sent for {runtimeApp.title}");
                } else {
                    FileLogger.LogWarning($"[RuntimeAppCell] FAILED: App {runtimeApp.title} is NOT installed according to Android!");
                }
            } else {
                FileLogger.LogWarning("[RuntimeAppCell] App launching only works on Android platform");
            }
            
            FileLogger.Log($"[RuntimeAppCell] Log file location: {FileLogger.GetLogPath()}");
        }

        bool isBeingDestroyed = false;
        void OnDestroy() {
            isBeingDestroyed = true;
            if (iconLoadCoroutine != null)
            {
                StopCoroutine(iconLoadCoroutine);
            }
        }
    }
}
