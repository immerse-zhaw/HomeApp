using UnityEngine;

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

        [ContextMenu("Refresh")]
        public void Refresh() {
            title.text = runtimeApp.title;

            // Instead of MXRStorage.GetFullPath(runtimeApp.iconPath) you can also use
            // runtimeApp.iconUrl to download the icon from a URL, like this:
            //new ImageDownloader().Load(runtimeApp.iconUrl, TextureFormat.ARGB32, true, result =>{}, error =>{});
            new ImageDownloader().Load(MXRStorage.GetFullPath(runtimeApp.iconPath), TextureFormat.ARGB32, true,
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

            SetRequirementIcon(controllerRequirement, runtimeApp.controllersRequired);
            SetRequirementIcon(internetRequirement, runtimeApp.internetRequired);            

            if (status != null) {
                if (status.IsNotUpdating()) {
                    SetStatus(null);
                    updateIndicator.enabled = false;
                    var isComplete = status.status == AppInstallStatus.Status.COMPLETE;
                    readyIndicator.enabled = false;
                } else if (status.UpdateIsQueued()) {
                    SetStatus("Queued...");
                    updateIndicator.enabled = false;
                    readyIndicator.enabled = false;
                } else if (status.IsUpdating()) {
                    switch (status.status) {
                        case AppInstallStatus.Status.DOWNLOADING:
                            SetStatus("Downloading..." + status.progress + "%");
                            break;
                        case AppInstallStatus.Status.PATCHING:
                            SetStatus("Patching..." + status.progress + "%");
                            break;
                        case AppInstallStatus.Status.INSTALLING:
                            SetStatus("Installing..." + status.progress + "%");
                            break;
                        case AppInstallStatus.Status.CLEANUP:
                            SetStatus("Cleaning up...");
                            break;
                        case AppInstallStatus.Status.SETUP:
                            SetStatus("Setup...");
                            break;
                        case AppInstallStatus.Status.READY_TO_INSTALL:
                            SetStatus("Ready to install...");
                            break;
                    }
                    updateIndicator.enabled = true;
                    updateIndicator.fillAmount = status.progress / 100f;
                    readyIndicator.enabled = false;
                }
            } else {
                // No status from MXR - check if actually installed on Android
                if (Application.platform == RuntimePlatform.Android) {
                    bool androidInstalled = MXRAndroidUtils.IsAppInstalled(runtimeApp.packageName);
                    if (!androidInstalled) {
                        SetStatus("Not Installed");
                    }
                }
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
        }
    }
}
