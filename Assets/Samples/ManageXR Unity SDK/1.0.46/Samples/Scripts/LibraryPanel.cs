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

        async void Start() {
            await MXRManager.InitAsync();
            // Disable the cell template gameobjects
            appCellTemplate.gameObject.SetActive(false);
            webXRAppCellTemplate.gameObject.SetActive(false);
            videoCellTemplate.gameObject.SetActive(false);

            // Setup button listeners
            if (appsButton != null) appsButton.onClick.AddListener(() => ShowContent(ContentType.Apps));
            if (videosButton != null) videosButton.onClick.AddListener(() => ShowContent(ContentType.Videos));
            if (webXRButton != null) webXRButton.onClick.AddListener(() => ShowContent(ContentType.WebXR));

            OnRuntimeSettingsSummaryChange(MXRManager.System.RuntimeSettingsSummary);
            OnDeviceStatusChange(MXRManager.System.DeviceStatus);

            MXRManager.System.OnRuntimeSettingsSummaryChange += OnRuntimeSettingsSummaryChange;
            MXRManager.System.OnDeviceStatusChange += OnDeviceStatusChange;
             
            // Show apps by default
            ShowContent(ContentType.Apps);
            
            Debug.Log("The system infor");
        }

        void OnDestroy() {
            MXRManager.System.OnRuntimeSettingsSummaryChange -= OnRuntimeSettingsSummaryChange;
            MXRManager.System.OnDeviceStatusChange -= OnDeviceStatusChange;
            
            if (appsButton != null) appsButton.onClick.RemoveAllListeners();
            if (videosButton != null) videosButton.onClick.RemoveAllListeners();
            if (webXRButton != null) webXRButton.onClick.RemoveAllListeners();
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
                });
        }

        void InstantiateAppCells() {
            if (appsContainer == null) return;
            
            MXRManager.System.RuntimeSettingsSummary.apps.Values.ToList()
                .ForEach(x => {
                    var instance = Instantiate(appCellTemplate, appsContainer.transform);
                    instance.gameObject.SetActive(true);
                    instance.gameObject.name = x.title;
                    instance.runtimeApp = x;
                    instance.status = MXRManager.System.DeviceStatus.AppInstallStatusForRuntimeApp(x);
                    instance.Refresh();
                    appCells.Add(instance);
                });
        }
    }
}
