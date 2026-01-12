using System;
using System.Linq;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

namespace MXR.SDK.Samples {
    // NOTE: A simple library example that instantiates cells for content types.
    // Every time the Device Status of the Runtime Settings Summary changes,
    // this script destroys the previously instantiated cells and instantiates 
    // them again. Not efficient, we know!. But this is just a demo.
    public class LibraryPanel : MonoBehaviour {
        [Header("Scroll View")]
        [SerializeField] ScrollRect scrollRect;
        
        [Header("Content Containers")]
        [SerializeField] GameObject appsContainer;
        [SerializeField] GameObject videosContainer;
        [SerializeField] GameObject webXRContainer;

        [Header("Buttons")]
        [SerializeField] Button appsButton;
        [SerializeField] Button videosButton;
        [SerializeField] Button webXRButton;
        [SerializeField] Button syncButton;

        [Header("Cell Templates")]
        [SerializeField] RuntimeAppCell appCellTemplate;
        [SerializeField] WebXRAppCell webXRAppCellTemplate;
        [SerializeField] VideoCell videoCellTemplate;
        
        [Header("Error UI")]
        [SerializeField] GameObject errPanel;
        [SerializeField] Text errLabel;

        List<WebXRAppCell> webXRAppCells = new List<WebXRAppCell>();
        List<VideoCell> videoCells = new List<VideoCell>();
        List<RuntimeAppCell> appCells = new List<RuntimeAppCell>();
        
        // Packages we do not want to show in the Apps list (easy to edit in one place).
        static readonly HashSet<string> HiddenPackageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "com.microsoft.windowsapp.WindowsAPP_AndroidApp",
            "com.microsoft.rdc.androidx",
            "com.immerse.HomeAppVR",
            "horizon.platform.service",
            "com.mightyimmersion.customlauncher.oculus.prod",
            "horizonos.supplement.meta.ndk.libraryapk",
            "horizon.platform.providers",
            "com.android.managedprovisioning",
            "com.mightyimmersion.mightyplatform.adminapp.prod",
            "com.oculus.firsttimenux",
            "com.oculus.q4bservice",
            "com.oculus.store",
            "com.oculus.vrshell"
        };

        static bool IsPackageHidden(string packageName) {
            return !string.IsNullOrEmpty(packageName) && HiddenPackageNames.Contains(packageName);
        }

        private float autoSyncInterval = 30f; // Auto-sync every 30 seconds
        private float timeSinceLastSync = 0f;

        async void Start() {
            await MXRManager.InitAsync();
            
            // Log detailed MXR status
            FileLogger.Log("[LibraryPanel] ===== MXR INITIALIZATION STATUS =====");
            FileLogger.Log($"[LibraryPanel] MXRManager.System: {(MXRManager.System != null ? "OK" : "NULL")}");
            
            if (MXRManager.System != null) {
                var deviceStatus = MXRManager.System.DeviceStatus;
                var runtimeSettings = MXRManager.System.RuntimeSettingsSummary;
                
                FileLogger.Log($"[LibraryPanel] Device Serial: {deviceStatus?.serial ?? "NULL"}");
                FileLogger.Log($"[LibraryPanel] RuntimeSettingsSummary: {(runtimeSettings != null ? "OK" : "NULL")}");
                
                if (runtimeSettings != null) {
                    FileLogger.Log($"[LibraryPanel] Apps in RuntimeSettings: {runtimeSettings.apps?.Count ?? 0}");
                    FileLogger.Log($"[LibraryPanel] Videos in RuntimeSettings: {runtimeSettings.videos?.Count ?? 0}");
                    FileLogger.Log($"[LibraryPanel] WebXR in RuntimeSettings: {runtimeSettings.webXRApps?.Count ?? 0}");
                }
                
                if (deviceStatus != null) {
                    FileLogger.Log($"[LibraryPanel] App Statuses in DeviceStatus: {deviceStatus.appStatuses?.Count ?? 0}");
                    FileLogger.Log($"[LibraryPanel] Video Statuses in DeviceStatus: {deviceStatus.videoStatuses?.Count ?? 0}");
                    
                    if (deviceStatus.appStatuses != null && deviceStatus.appStatuses.Count > 0) {
                        foreach (var kvp in deviceStatus.appStatuses) {
                            FileLogger.Log($"[LibraryPanel] App Status: {kvp.Key} = {kvp.Value?.status}");
                        }
                    } else {
                        FileLogger.LogWarning("[LibraryPanel] NO APP STATUSES - Device not tracking any apps!");
                    }
                } else {
                    FileLogger.LogWarning("[LibraryPanel] DeviceStatus is NULL - MXR may not be properly initialized");
                }
            }
            FileLogger.Log("[LibraryPanel] ===========================================");
            
            // Disable the cell template gameobjects
            appCellTemplate.gameObject.SetActive(false);
            webXRAppCellTemplate.gameObject.SetActive(false);
            videoCellTemplate.gameObject.SetActive(false);

            // Setup button listeners
            if (appsButton != null) appsButton.onClick.AddListener(() => ShowContent(ContentType.Apps));
            if (videosButton != null) videosButton.onClick.AddListener(() => ShowContent(ContentType.Videos));
            if (webXRButton != null) webXRButton.onClick.AddListener(() => ShowContent(ContentType.WebXR));
            if (syncButton != null) syncButton.onClick.AddListener(TriggerSync);

            OnRuntimeSettingsSummaryChange(MXRManager.System.RuntimeSettingsSummary);
            OnDeviceStatusChange(MXRManager.System.DeviceStatus);

            MXRManager.System.OnRuntimeSettingsSummaryChange += OnRuntimeSettingsSummaryChange;
            MXRManager.System.OnDeviceStatusChange += OnDeviceStatusChange;
             
            // Show apps by default
            ShowContent(ContentType.Apps);
            
            // Trigger initial sync to check for pending installations
            FileLogger.Log("[LibraryPanel] Triggering initial sync on startup");
            TriggerSync();
            
            Debug.Log("The system infor");
        }
        
        void Update() {
            // Auto-sync periodically to check for new deployments
            timeSinceLastSync += Time.deltaTime;
            if (timeSinceLastSync >= autoSyncInterval) {
                timeSinceLastSync = 0f;
                FileLogger.Log("[LibraryPanel] Auto-sync triggered");
                TriggerSync();
            }
        }

        void OnDestroy() {
            MXRManager.System.OnRuntimeSettingsSummaryChange -= OnRuntimeSettingsSummaryChange;
            MXRManager.System.OnDeviceStatusChange -= OnDeviceStatusChange;
            
            if (appsButton != null) appsButton.onClick.RemoveAllListeners();
            if (videosButton != null) videosButton.onClick.RemoveAllListeners();
            if (webXRButton != null) webXRButton.onClick.RemoveAllListeners();
            if (syncButton != null) syncButton.onClick.RemoveAllListeners();
        }

        public void TriggerSync() {
            FileLogger.Log("[LibraryPanel] Sync button pressed - Triggering ManageXR Admin App sync");
            if (MXRManager.System != null) {
                bool adminAppInstalled = MXRAndroidUtils.IsAppInstalled("com.mightyimmersion.mightyplatform.adminapp.prod");
                FileLogger.Log($"[LibraryPanel] Admin App installed check: {adminAppInstalled}");
                
                MXRManager.System.Sync();
                FileLogger.Log("[LibraryPanel] Sync() command sent to Admin App via checkDbAsync message");
                
                // Log device status
                var deviceStatus = MXRManager.System.DeviceStatus;
                if (deviceStatus != null) {
                    FileLogger.Log($"[LibraryPanel] Device has {deviceStatus.appStatuses?.Count ?? 0} app statuses");
                } else {
                    FileLogger.LogWarning("[LibraryPanel] DeviceStatus is NULL - device may not be tracking apps");
                }
            } else {
                FileLogger.LogWarning("[LibraryPanel] Cannot sync - MXRManager.System is null");
            }
        }

        public enum ContentType {
            Apps,
            Videos,
            WebXR
        }

        public void ShowContent(ContentType type) {
            if (appsContainer != null) appsContainer.SetActive(type == ContentType.Apps);
            if (videosContainer != null) videosContainer.SetActive(type == ContentType.Videos);
            if (webXRContainer != null) webXRContainer.SetActive(type == ContentType.WebXR);
            
            // Update ScrollRect content reference
            if (scrollRect != null) {
                switch (type) {
                    case ContentType.Apps:
                        if (appsContainer != null) scrollRect.content = appsContainer.GetComponent<RectTransform>();
                        break;
                    case ContentType.Videos:
                        if (videosContainer != null) scrollRect.content = videosContainer.GetComponent<RectTransform>();
                        break;
                    case ContentType.WebXR:
                        if (webXRContainer != null) scrollRect.content = webXRContainer.GetComponent<RectTransform>();
                        break;
                }
            }
            
            Debug.Log($"[LibraryPanel] Showing {type}");
        }

        void OnRuntimeSettingsSummaryChange(RuntimeSettingsSummary obj) {
            if (obj == null) return;
            Debug.Log("Runtime Settings Summary changed, destroy and instantiate cells");
            DestroyContentCells();
            InstantiateContentCells();
        }

        void OnDeviceStatusChange(DeviceStatus obj) {
            if (obj == null) return;
            Debug.Log("Device Status changed, destroy and instantiate cells");
            DestroyContentCells();
            InstantiateContentCells();
        }

        // Destroy all the cell instances of each content type 
        // that have been created.
        void DestroyContentCells() {
            foreach (var instance in webXRAppCells)
                Destroy(instance.gameObject);
            webXRAppCells.Clear();

            foreach (var instance in videoCells)
                Destroy(instance.gameObject);
            videoCells.Clear();

            foreach (var cell in appCells)
                Destroy(cell.gameObject);
            appCells.Clear();
        }

        void InstantiateContentCells() {
            InstantiateAppCells();
            InstantiateWebXRCells();
            InstantaiteVideoCells();
        }

        void InstantiateWebXRCells() {
            if (webXRContainer == null) return;
            
            MXRManager.System.RuntimeSettingsSummary.webXRApps.Values.ToList()
                .ForEach(x => {
                    var instance = Instantiate(webXRAppCellTemplate, webXRContainer.transform);
                    instance.gameObject.SetActive(true);
                    instance.gameObject.name = x.title;
                    instance.webXRApp = x;
                    instance.Refresh();
                    webXRAppCells.Add(instance);
                    instance.gameObject.AddComponent<ForwardScrollToParent>();
                });
        }

        void InstantaiteVideoCells() {
            if (videosContainer == null) return;
            
            MXRManager.System.RuntimeSettingsSummary.videos.Values.ToList()
                .ForEach(x => {
                    var instance = Instantiate(videoCellTemplate, videosContainer.transform);
                    instance.gameObject.SetActive(true);
                    instance.gameObject.name = x.title;
                    instance.video = x;
                    instance.status = MXRManager.System.DeviceStatus.FileInstallStatusForVideo(x);
                    instance.Refresh();
                    videoCells.Add(instance);
                    instance.gameObject.AddComponent<ForwardScrollToParent>();
                });
        }

        void InstantiateAppCells() {
            if (appsContainer == null) return;

            var allApps = MXRManager.System.RuntimeSettingsSummary.apps.Values;
            var visibleApps = allApps.Where(app => !IsPackageHidden(app.packageName)).ToList();

            if (visibleApps.Count != allApps.Count)
            {
                var filtered = allApps.Select(app => app.packageName)
                                      .Where(IsPackageHidden)
                                      .Distinct(StringComparer.OrdinalIgnoreCase)
                                      .ToList();
                FileLogger.Log($"[LibraryPanel] Filtered {filtered.Count} app(s) by package: {string.Join(", ", filtered)}");
            }

            visibleApps.ForEach(x => {
                var instance = Instantiate(appCellTemplate, appsContainer.transform);
                instance.gameObject.SetActive(true);
                instance.gameObject.name = x.title;
                instance.runtimeApp = x;
                instance.status = MXRManager.System.DeviceStatus?.AppInstallStatusForRuntimeApp(x);
                // Log to help debug
                bool androidInstalled = MXRAndroidUtils.IsAppInstalled(x.packageName);
                FileLogger.Log($"[LibraryPanel] App: {x.title} | Package: {x.packageName} | AndroidInstalled: {androidInstalled} | HasStatus: {instance.status != null}");
                instance.Refresh();
                appCells.Add(instance);
                instance.gameObject.AddComponent<ForwardScrollToParent>();
            });
        }
    }
}
