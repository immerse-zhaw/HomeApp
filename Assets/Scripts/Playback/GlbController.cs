using UnityEngine;
using GLTFast;
using System.Threading.Tasks;
using System;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;

namespace Playback
{
    public class GlbController : MonoBehaviour
    {
        [SerializeField] private Transform modelRoot;
        [SerializeField] private Material pointsMaterial;
        [Header("Download Progress")]
        [SerializeField] private Slider downloadProgress;

        [Header("Point Rendering")]
        [SerializeField] private float defaultPointSize = 0.01f;        // Applied after load if mesh topology is Points
        [SerializeField] private string pointSizeProperty = "_PointSize"; // Change if your shader uses a different name

        private static int _pointSizePropId = Shader.PropertyToID("_PointSize");

        private GltfImport currentModel;
        private Animation animationPlayer; // Found on instantiated model, if any
        private float autoScale = 1f;
        private float userScale = 1f;

        public void LoadModel(string url)
        {
            Debug.Log($"[GlbController] Loading model from URL: {url}");
            StopAllCoroutines();
            ClearCurrentModel();
            if (downloadProgress)
            {
                StartCoroutine(DownloadThenInstantiate(url));
            }
            else
            {
                _ = LoadAsync(url, () => Debug.Log("[GlbController] Model is ready."));
            }
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

            if (pointsMaterial.HasProperty(_pointSizePropId))
            {
                pointsMaterial.SetFloat(_pointSizePropId, size);
                Debug.Log($"[GlbController] Set point size to {size}.");
            }
            else
            {
                Debug.LogWarning($"[GlbController] Points material doesn't have _PointSize property.");
            }
        }

        public void SetScale(float s)
        {
            if (modelRoot == null)
            {
                Debug.LogWarning("[GlbController] modelRoot is not assigned.");
                return;
            }
            userScale = Mathf.Max(0f, s);
            ApplyCompositeScale();
            Debug.Log($"[GlbController] Set user scale to {s}. Combined scale: {autoScale * userScale}");
        }

        private void ClearCurrentModel()
        {
            if (currentModel != null)
            {
                currentModel.Dispose();
                currentModel = null;
            }

            animationPlayer = null;

            if (modelRoot != null)
            {
                for (int i = modelRoot.childCount - 1; i >= 0; i--)
                {
                    Destroy(modelRoot.GetChild(i).gameObject);
                }

                RemovePipelineComponents();
                modelRoot.localScale = Vector3.one;
            }

            autoScale = 1f;
            userScale = 1f;

            if (downloadProgress)
            {
                downloadProgress.value = 0f;
                downloadProgress.gameObject.SetActive(false);
            }
        }

        private async Task LoadAsync(string url, Action onReady)
        {
            currentModel?.Dispose();
            currentModel = new GltfImport();
            bool success = await currentModel.Load(new Uri(url));
            if (!success)
            {
                Debug.LogError("[GlbController] Failed to load model.");
                return;
            }

            Debug.Log("[GlbController] Model loaded successfully.");
            await currentModel.InstantiateMainSceneAsync(modelRoot);
            FinalizeLoadedModel();

            onReady?.Invoke();
        }

        private void FinalizeLoadedModel()
        {
            if (modelRoot == null) return;

            ScaleModelToUnitCube();

            animationPlayer = modelRoot.GetComponentInChildren<Animation>(true);

            ApplyPointCloudMaterialIfNeeded();
            if (HasPointTopologyInChildren(modelRoot))
            {
                SetPointsSize(defaultPointSize);
            }
            else
            {
                SetupGlbPipeline(modelRoot);
            }
        }

        private void ScaleModelToUnitCube()
        {
            var renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = default;
            bool hasBounds = false;

            foreach (var r in renderers)
            {
                if (r == null || r.transform == modelRoot) continue; // Ignore helper renderer on root

                if (!hasBounds)
                {
                    bounds = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            if (!hasBounds) return;

            float maxDim = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (maxDim <= 0f) return;

            autoScale = 1f / maxDim;
            ApplyCompositeScale();
            Debug.Log($"[GlbController] Scaled model to fit inside 1x1x1 cube. Auto scale: {autoScale}");
        }

        private void RemovePipelineComponents()
        {
            var anim = modelRoot.GetComponent<Animation>();
            if (anim) Destroy(anim);

            var grab = modelRoot.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            if (grab) Destroy(grab);

            var meshRenderer = modelRoot.GetComponent<MeshRenderer>();
            if (meshRenderer) Destroy(meshRenderer);

            var meshFilter = modelRoot.GetComponent<MeshFilter>();
            if (meshFilter) Destroy(meshFilter);

            var collider = modelRoot.GetComponent<Collider>();
            if (collider) Destroy(collider);

            var rigidbody = modelRoot.GetComponent<Rigidbody>();
            if (rigidbody) Destroy(rigidbody);
        }

        private void ApplyCompositeScale()
        {
            if (modelRoot == null) return;

            modelRoot.localScale = Vector3.one * (autoScale * userScale);
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

        private void SetupGlbPipeline(Transform root)
        {
            // Add Rigidbody
            var rigidbody = root.GetComponent<Rigidbody>();
            if (rigidbody == null)
            {
                rigidbody = root.gameObject.AddComponent<Rigidbody>();
                rigidbody.useGravity = false;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
            }
            rigidbody.linearVelocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;

            // Add Collider
            if (!root.TryGetComponent<Collider>(out _))
            {
                Mesh mesh = null;
                var meshFilter = root.GetComponentInChildren<MeshFilter>();
                if (meshFilter != null)
                    mesh = meshFilter.sharedMesh;

                if (mesh != null)
                {
                    var meshCollider = root.gameObject.AddComponent<MeshCollider>();
                    meshCollider.sharedMesh = mesh;
                    meshCollider.convex = true;
                }
                else
                {
                    var boxCollider = root.gameObject.AddComponent<BoxCollider>();
                    var renderers = root.GetComponentsInChildren<Renderer>();
                    Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(root.position, Vector3.zero);
                    foreach (var rr in renderers)
                        bounds.Encapsulate(rr.bounds);
                    boxCollider.center = bounds.center;
                    boxCollider.size = bounds.size;
                }
            }

            // Add XRGrabInteractable
            var grabbable = root.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>()
                            ?? root.gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            grabbable.movementType = UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable.MovementType.VelocityTracking;
            grabbable.throwOnDetach = true;

            // Add Editable tag
            root.gameObject.tag = "Editable";

            // Add MeshRenderer and MeshFilter to root if needed
            var childMeshRenderer = root.GetComponentInChildren<MeshRenderer>();
            var childMeshFilter = root.GetComponentInChildren<MeshFilter>();
            if (childMeshRenderer != null && childMeshFilter != null)
            {
                var meshFilter = root.GetComponent<MeshFilter>();
                if (meshFilter == null)
                {
                    meshFilter = root.gameObject.AddComponent<MeshFilter>();
                }

                var meshRenderer = root.GetComponent<MeshRenderer>();
                if (meshRenderer == null)
                {
                    meshRenderer = root.gameObject.AddComponent<MeshRenderer>();
                }

                meshFilter.sharedMesh = childMeshFilter.sharedMesh;
                meshRenderer.sharedMaterials = childMeshRenderer.sharedMaterials;
            }

            // Set glTF-unlit shader for all MeshRenderers
            var meshRenderers = root.GetComponentsInChildren<MeshRenderer>();
            Shader gltfUnlitShader = Shader.Find("Universal Render Pipeline/Lit");
            if (gltfUnlitShader != null)
            {
                foreach (var mr in meshRenderers)
                {
                    foreach (var mat in mr.materials)
                    {
                        mat.shader = gltfUnlitShader;
                    }
                }
            }
        }

        // Handle GET. Show network download progress, then load bytes via GLTFast
        private IEnumerator DownloadThenInstantiate(string url)
        {
            if (downloadProgress)
            {
                downloadProgress.gameObject.SetActive(true);
                downloadProgress.value = 0f;
            }

            using (var uwr = UnityWebRequest.Get(url))
            {
                uwr.SendWebRequest();
                while (!uwr.isDone)
                {
                    if (downloadProgress) downloadProgress.value = uwr.downloadProgress;
                    yield return null;
                }

                if (uwr.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[GlbController] Download error: {uwr.error}");
                    if (downloadProgress)
                    {
                        downloadProgress.value = 0f;
                        downloadProgress.gameObject.SetActive(false);
                    }
                    yield break;
                }

                var data = uwr.downloadHandler.data;
                currentModel?.Dispose();
                currentModel = new GltfImport();
                var parseTask = currentModel.LoadGltfBinary(data);
                while (!parseTask.IsCompleted) yield return null;
                if (!parseTask.Result)
                {
                    Debug.LogError("[GlbController] Failed to parse GLB.");
                    currentModel?.Dispose();
                    yield break;
                }

                var instTask = currentModel.InstantiateMainSceneAsync(modelRoot);
                while (!instTask.IsCompleted) yield return null;

                FinalizeLoadedModel();
            }

            if (downloadProgress)
            {
                downloadProgress.value = 1f;
                downloadProgress.gameObject.SetActive(false);
            }

            Debug.Log("[GlbController] Model is ready.");
        }
    }
}
