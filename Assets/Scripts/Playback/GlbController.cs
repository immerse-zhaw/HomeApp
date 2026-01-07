using UnityEngine;
using GLTFast;
using System.Threading.Tasks;
using System;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace Playback
{
        public class GlbController : MonoBehaviour
        {
        [Header("MXR Panel Reference")]
        [SerializeField] private GameObject mxrPanel;
        [SerializeField] private Transform modelRoot;
        [SerializeField] private Material pointsMaterial;
        [Header("Download Progress")]
        [SerializeField] private Slider downloadProgress;

        [Header("Point Rendering")]
        [SerializeField] private float defaultPointSize = 2.0f;         // Pixel size used by point shader
        [SerializeField] private string pointSizeProperty = "_PointSize"; // Change if your shader uses a different name

        private static int _pointSizePropId = Shader.PropertyToID("_PointSize");

        private GltfImport currentModel;
        private Animation animationPlayer; // Found on instantiated model, if any
        private float autoScale = 1f;
        private float userScale = 1f;
        private readonly List<PointCloudMeshInfo> pointCloudMeshes = new List<PointCloudMeshInfo>();
        // Keep the URL of the model being loaded so we can report filename when ready
        private string currentModelUrl;

        // Expose model root so other scripts (e.g., GlbMover) can manipulate the loaded model.
        public Transform ModelRoot => modelRoot;

        private class PointCloudMeshInfo
        {
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public Mesh OriginalMesh; // copied from loaded mesh; never mutated
            public Mesh WorkingMesh;  // assigned to filter; rebuilt per LOD
        }

        public void LoadModel(string url)
        {
            Debug.Log($"[GlbController] Loading model from URL: {url}");
            StopAllCoroutines();
            ClearCurrentModel();
            // Keep this so we can report filename later
            currentModelUrl = url;
            
            // Deactivate the MXR panel when spawning a GLB object
            if (mxrPanel != null)
            {
                mxrPanel.SetActive(false);
            }
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
            // Reactivate the MXR panel when GLB is closed
            if (mxrPanel != null)
            {
                mxrPanel.SetActive(true);
            }
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
            // Remap external scale: 1x means fit in 1x1x1, 10x means 10x bigger than that
            float mappedScale = 1f;
            if (s <= 1.0f)
            {
                // 0.1x to 1x: map linearly (0.1 to 1)
                mappedScale = Mathf.Lerp(0.1f, 1f, (s - 0.1f) / 0.9f);
            }
            else
            {
                // 1x to 10x: map linearly (1 to 10)
                mappedScale = Mathf.Lerp(1f, 10f, (s - 1f) / 9f);
            }
            userScale = Mathf.Max(0f, mappedScale);
            ApplyCompositeScale();

            // Use exact combined scale for LOD computation (no high floor) so LOD matches user intent
            float totalScale = autoScale * userScale;
            if (totalScale <= 0f) totalScale = 1e-6f;
            ApplyPointCloudLod(totalScale);

            // Keep point size simple and stable; downsampling reduces overdraw instead
            if (pointsMaterial != null && pointsMaterial.HasProperty(_pointSizePropId))
            {
                pointsMaterial.SetFloat(_pointSizePropId, defaultPointSize);
            }

            Debug.Log($"[GlbController] Set user scale to {s} (mapped: {mappedScale}). Combined scale: {autoScale * userScale}");
        }

        private void ClearCurrentModel()
        {
            if (currentModel != null)
            {
                currentModel.Dispose();
                currentModel = null;
            }

            pointCloudMeshes.Clear();

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
            // Log the URL being streamed/loaded so we can identify the source file
            Debug.Log($"[GlbController] Streaming/Loading model from URL: {url}");
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

            // Apply point-cloud material when the mesh topology is already points
            bool hasPointTopology = HasPointTopologyInChildren(modelRoot);
            
            // Send simple status over websocket indicating what is playing (point cloud vs 3D object)
            var ws = FindObjectOfType<Net.WsClient>();
            if (ws != null && !string.IsNullOrEmpty(currentModelUrl))
            {
                var fileName = Path.GetFileName(currentModelUrl);
                var status = hasPointTopology ? $"Playing point cloud: {fileName}" : $"Playing 3D object: {fileName}";
                Debug.Log($"[GlbController] Sending status: {status}");
                ws.SendStatus(status);
            }

            if (hasPointTopology)
            {
                CachePointCloudMeshes();
                ApplyPointCloudLod(autoScale * userScale);
                ApplyPointCloudMaterialIfNeeded();
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

        private void CachePointCloudMeshes()
        {
            pointCloudMeshes.Clear();
            foreach (var mr in modelRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh;
                if (mesh.GetTopology(0) != MeshTopology.Points) continue;

                // Make a copy so we never mutate the imported mesh asset
                var originalCopy = Instantiate(mesh);
                originalCopy.name = mesh.name + "_OriginalCopy";

                var working = new Mesh();
                working.name = mesh.name + "_WorkingLod";

                mf.sharedMesh = working;

                pointCloudMeshes.Add(new PointCloudMeshInfo
                {
                    Filter = mf,
                    Renderer = mr,
                    OriginalMesh = originalCopy,
                    WorkingMesh = working
                });
            }

            // Show total original points in debug console
            int totalPoints = 0;
            foreach (var pc in pointCloudMeshes)
                if (pc.OriginalMesh != null && pc.OriginalMesh.vertexCount > 0)
                    totalPoints += pc.OriginalMesh.vertexCount;
            Debug.Log($"[GlbController] Loaded point cloud(s) with {totalPoints} points");
        }

        private void ApplyPointCloudLod(float totalScale)
        {
            if (pointCloudMeshes.Count == 0) return;
            // Compute global target across all point-cloud meshes so totals match requested budget
            int totalOriginal = 0;
            foreach (var pc in pointCloudMeshes)
            {
                if (pc.OriginalMesh != null) totalOriginal += pc.OriginalMesh.vertexCount;
            }
            if (totalOriginal == 0) return;

            int globalTarget = ComputeTargetPointCount(totalOriginal, totalScale);

            // Allocate per-mesh targets proportionally, then adjust to match globalTarget exactly
            var perMeshTargets = new int[pointCloudMeshes.Count];
            int allocated = 0;
            for (int i = 0; i < pointCloudMeshes.Count; i++)
            {
                var pc = pointCloudMeshes[i];
                int orig = pc.OriginalMesh != null ? pc.OriginalMesh.vertexCount : 0;
                if (orig == 0) { perMeshTargets[i] = 0; continue; }
                perMeshTargets[i] = Mathf.Max(256, Mathf.RoundToInt(orig / (float)totalOriginal * globalTarget));
                perMeshTargets[i] = Mathf.Min(perMeshTargets[i], orig);
                allocated += perMeshTargets[i];
            }

            // Fix allocation rounding errors: distribute remainder
            int remainder = globalTarget - allocated;
            int idx = 0;
            while (remainder > 0)
            {
                if (idx >= pointCloudMeshes.Count) idx = 0;
                var pc = pointCloudMeshes[idx];
                int orig = pc.OriginalMesh != null ? pc.OriginalMesh.vertexCount : 0;
                if (orig > perMeshTargets[idx])
                {
                    perMeshTargets[idx]++;
                    remainder--;
                }
                idx++;
            }

            int totalNewPoints = 0;
            for (int i = 0; i < pointCloudMeshes.Count; i++)
            {
                var pc = pointCloudMeshes[i];
                var src = pc.OriginalMesh;
                var dst = pc.WorkingMesh;
                if (src == null || dst == null) continue;

                var verts = src.vertices;
                if (verts == null || verts.Length == 0) continue;

                int targetCount = perMeshTargets[i];
                targetCount = Mathf.Clamp(targetCount, 256, verts.Length);

                // Evenly sample to get close to the requested targetCount. This produces a smooth,
                // monotonic progression up to the requested budget (or original vertex count).
                int desired = Mathf.Min(targetCount, verts.Length);

                var outVerts = new Vector3[desired];
                var srcColors = src.colors;
                var outColors = (srcColors != null && srcColors.Length > 0) ? new Color[desired] : null;
                var srcUV = src.uv;
                var outUV = (srcUV != null && srcUV.Length > 0) ? new Vector2[desired] : null;

                for (int k = 0; k < desired; k++)
                {
                    int srcIdx = Mathf.Min(verts.Length - 1, Mathf.FloorToInt(k * (verts.Length / (float)desired)));
                    outVerts[k] = verts[srcIdx];
                    if (outColors != null && srcIdx < srcColors.Length) outColors[k] = srcColors[srcIdx];
                    if (outUV != null && srcIdx < srcUV.Length) outUV[k] = srcUV[srcIdx];
                }

                dst.Clear();
                dst.vertices = outVerts;
                if (outColors != null) dst.colors = outColors;
                if (outUV != null) dst.uv = outUV;

                var indices = new int[desired];
                for (int k = 0; k < desired; k++) indices[k] = k;
                dst.SetIndices(indices, MeshTopology.Points, 0);
                dst.RecalculateBounds();

                totalNewPoints += desired;
            }

            Debug.Log($"[GlbController] Current point cloud LOD: {totalNewPoints} points");
        }

        private int ComputeTargetPointCount(int originalCount, float totalScale)
        {
            // Map directly from the user scale: 1x -> 30k, 5x -> 500k, 10x -> 1.5M
            float us = Mathf.Max(userScale, 0.001f);

            int target;
            if (us <= 1f)
            {
                target = 30000;
            }
            else if (us <= 5f)
            {
                float t = (us - 1f) / 4f;
                target = Mathf.RoundToInt(Mathf.Lerp(30000, 500000, t));
            }
            else if (us <= 10f)
            {
                float t = (us - 5f) / 5f;
                target = Mathf.RoundToInt(Mathf.Lerp(500000, 1500000, t));
            }
            else
            {
                target = 1500000;
            }

            // Never exceed original count
            return Mathf.Min(target, originalCount);
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

            // Log the URL being downloaded/streamed so we can inspect the source
            Debug.Log($"[GlbController] Downloading model from URL: {url}");
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
