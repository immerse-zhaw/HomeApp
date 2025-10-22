using UnityEngine;
using GLTFast;
using System.Threading.Tasks;
using System;

namespace Playback
{
    public class GlbController : MonoBehaviour
    {
        [SerializeField] private Transform modelRoot;

        private GltfImport currentModel;

        [SerializeField] private Material pointsMaterial;

        public void LoadModel(string url)
        {
            Debug.Log($"[GlbController] Loading model from URL: {url}");
            ClearCurrentModel();
            _ = LoadAsync(url, () => Debug.Log("[GlbController] Model is ready."));
        }

        public void CloseModel()
        {
            Debug.Log("[GlbController] Closing model.");
            ClearCurrentModel();
        }

        private void ClearCurrentModel()
        {
            if (currentModel != null)
            {
                currentModel = null;
                foreach (Transform child in modelRoot)
                {
                    Destroy(child.gameObject);
                }
            }
        }

        private async Task LoadAsync(string url, Action onReady)
        {
            currentModel = new GltfImport();
            bool success = await currentModel.Load(new Uri(url));
            if (!success)
            {
                Debug.LogError("[GlbController] Failed to load model.");

            }
            Debug.Log("[GlbController] Model loaded successfully.");
            await currentModel.InstantiateMainSceneAsync(modelRoot);
            ApplyPointCloudMaterialIfNeeded();
            Animation anim = modelRoot.GetComponentInChildren<Animation>();
            if (anim != null)
            {
                anim.Play();
            }
            onReady?.Invoke();
        }

        void ApplyPointCloudMaterialIfNeeded()
        {
            foreach (var r in modelRoot.GetComponentsInChildren<Renderer>(true))
            {
                var mesh = (r as MeshRenderer)?.GetComponent<MeshFilter>()?.sharedMesh ??
                           (r as SkinnedMeshRenderer)?.sharedMesh;
                if (mesh != null && mesh.GetTopology(0) == MeshTopology.Points)
                {
                    r.sharedMaterial = pointsMaterial;
                }
            }
        }

    }
}