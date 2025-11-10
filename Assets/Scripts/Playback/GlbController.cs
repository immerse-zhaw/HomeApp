using UnityEngine;
using GLTFast;
using System.Threading.Tasks;
using System;

namespace Playback
{
    public class GlbController : MonoBehaviour
    {
        [SerializeField] private Transform modelRoot;
        [SerializeField] private Material pointsMaterial;

        [Header("Point Rendering")]
        [SerializeField] private float defaultPointSize = 0.01f;        // Applied after load if mesh topology is Points
        [SerializeField] private string pointSizeProperty = "_PointSize"; // Change if your shader uses a different name

        private static int _pointSizePropId = Shader.PropertyToID("_PointSize");

        private GltfImport currentModel;
        private Animation animationPlayer; // Found on instantiated model, if any

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

        public void PlayAnimation(string animation)
        {
            if (animationPlayer == null)
            {
                Debug.LogWarning("[GlbController] No Animations found.");
                return;
            }
            
            if (animation == "")
            {
                animationPlayer[animationPlayer.clip.name].time = 0f;
                animationPlayer.Sample();
                animationPlayer.Stop();
            }

            animationPlayer.clip = animationPlayer.GetClip(animation);

            animationPlayer.Play();
            Debug.Log($"[GlbController] Playing animation #{animation}.");
        }

        public void SetPointsSize(float size)
        {
            if (pointsMaterial == null)
            {
                Debug.LogWarning("[GlbController] Points material not assigned.");
                return;
            }

            // Support either the configured name or the default _PointSize id
            if (!string.IsNullOrEmpty(pointSizeProperty) && pointsMaterial.HasProperty(pointSizeProperty))
            {
                pointsMaterial.SetFloat(pointSizeProperty, size);
                Debug.Log($"[GlbController] Set '{pointSizeProperty}' to {size}.");
            }
            else if (pointsMaterial.HasProperty(_pointSizePropId))
            {
                pointsMaterial.SetFloat(_pointSizePropId, size);
                Debug.Log($"[GlbController] Set _PointSize to {size}.");
            }
            else
            {
                Debug.LogWarning($"[GlbController] Points material doesn't have '{pointSizeProperty}' or _PointSize.");
            }
        }

        public void SetScale(float s)
        {
            if (modelRoot == null)
            {
                Debug.LogWarning("[GlbController] modelRoot is not assigned.");
                return;
            }
            modelRoot.localScale = Vector3.one * s;
            Debug.Log($"[GlbController] Set modelRoot scale to {s}.");
        }

        private void ClearCurrentModel()
        {
            if (currentModel != null)
            {
                currentModel = null;
                animationPlayer = null;
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
                return;
            }

            Debug.Log("[GlbController] Model loaded successfully.");
            await currentModel.InstantiateMainSceneAsync(modelRoot);

            animationPlayer = modelRoot.GetComponentInChildren<Animation>(true);

            ApplyPointCloudMaterialIfNeeded();
            if (HasPointTopologyInChildren(modelRoot))
            {
                SetPointsSize(defaultPointSize);
            }

            onReady?.Invoke();
        }

        private void ApplyPointCloudMaterialIfNeeded()
        {
            if (pointsMaterial == null) return;

            foreach (var r in modelRoot.GetComponentsInChildren<Renderer>(true))
            {
                var mesh = (r as MeshRenderer)?.GetComponent<MeshFilter>()?.sharedMesh
                           ?? (r as SkinnedMeshRenderer)?.sharedMesh;

                if (mesh != null && mesh.GetTopology(0) == MeshTopology.Points)
                {
                    r.sharedMaterial = pointsMaterial;
                }
            }
        }

        private static bool HasPointTopologyInChildren(Transform root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mesh = (r as MeshRenderer)?.GetComponent<MeshFilter>()?.sharedMesh
                           ?? (r as SkinnedMeshRenderer)?.sharedMesh;

                if (mesh != null && mesh.GetTopology(0) == MeshTopology.Points)
                    return true;
            }
            return false;
        }
    }
}
