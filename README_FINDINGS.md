# Performance Audit & Optimization Recommendations

This document summarizes the audit of the HomeApp VR project for Meta Quest. Issues that may cause lag, freezes, or out-of-memory conditions are listed, along with their locations and suggested fixes.

---

## 🔍 Critical Issues (High Priority)

### 1. FileLogger Synchronous I/O
**Location:** `Assets/Scripts/App/FileLogger.cs`

```csharp
string logDirectory = Application.persistentDataPath;
logFilePath = Path.Combine(logDirectory, "app_debug.log");

// Create initial log entry
try
{
    string header = $"\n\n===== NEW SESSION: {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====\n";
    File.AppendAllText(logFilePath, header);
    Debug.Log($"[FileLogger] Log file created at: {logFilePath}");
}
catch (Exception e)
{
    Debug.LogError($"[FileLogger] Failed to initialize log file: {e.Message}");
}
```

- Every log call uses `File.AppendAllText`, performing blocking disk writes.
- Also calls `Debug.Log` on each entry, doubling overhead.

**Impact:** Main thread stalls on I/O, especially during startup or when many logs are generated.

**Fix:** Buffer log entries with a persistent `StreamWriter` and flush periodically, remove redundant `Debug.Log` inside logger, and flush on quit.

```csharp
private static StreamWriter writer;
private static readonly object lockObj = new object();

private static void Initialize()
{
    if (initialized) return;
    string logDirectory = Application.persistentDataPath;
    logFilePath = Path.Combine(logDirectory, "app_debug.log");
    writer = new StreamWriter(logFilePath, append: true) { AutoFlush = false };
    writer.WriteLine($"\n===== NEW SESSION: {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
    initialized = true;
}

public static void Log(string message)
{
    Initialize();
    string entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
    lock (lockObj) { writer.WriteLine(entry); }
    // No Debug.Log here; callers log separately if needed
}

public static void Flush()
{
    lock (lockObj) { writer?.Flush(); }
}
```

---

### 2. WsClient Debug Logging Spam
**Location:** `Assets/Scripts/Net/WsClient.cs`

```csharp
private string GetStatusString()
{
    var current = state != null ? state.Current : AppState.Idle;
    string statusString = current switch
    {
        AppState.PlayingVideo => "video",
        AppState.ShowingModel => "model",
        _ => "home"
    };
    Debug.Log($"[WsClient] GetStatusString: Current state = {current}, Returning: {statusString}");
    return statusString;
}

private string GetActionString()
{
    if (state == null) return null;
    string action = state.CurrentAction;
    bool isNone = string.IsNullOrWhiteSpace(action) || action == "none";
    if (isNone || GetStatusString() == "home") return null;
    Debug.Log($"[WsClient] GetActionString: Current action = {action}");
    return action;
}

public void SafeSend(string text)
{
    if (shuttingDown || !IsOpen) return;
    _ = ws.SendText(text);
    Debug.Log($"[WsClient] >> {text}");
}

ws.OnMessage += (data) =>
{
    string text = Encoding.UTF8.GetString(data);
    Debug.Log($"[WsClient] << {text}");
    OnMessage?.Invoke(text);
};
```

- `GetStatusString()` and `GetActionString()` each log every time they're called (every ping).
- `SafeSend()` and message callbacks log every send/receive.

**Impact:** 4-5 `Debug.Log` calls per 1.5s heartbeat; expensive on Quest as it writes to logcat, causing frame hiccups even at idle.

**Fix:** Remove or guard most logs behind `#if UNITY_EDITOR` or a debug flag; avoid logging every ping.

```csharp
private string GetStatusString()
{
    var current = state != null ? state.Current : AppState.Idle;
    string statusString = current switch
    {
        AppState.PlayingVideo => "video",
        AppState.ShowingModel => "model",
        _ => "home"
    };
#if UNITY_EDITOR
    Debug.Log($"[WsClient] GetStatusString: {statusString}");
#endif
    return statusString;
}

public void SafeSend(string text)
{
    if (shuttingDown || !IsOpen) return;
    _ = ws.SendText(text);
#if UNITY_EDITOR
    Debug.Log($"[WsClient] >> {text}");
#endif
}

ws.OnMessage += (data) =>
{
    string text = Encoding.UTF8.GetString(data);
#if UNITY_EDITOR
    Debug.Log($"[WsClient] << {text}");
#endif
    OnMessage?.Invoke(text);
};
```

---

### 3. GlbMover Frame‑rate Logging
**Location:** `Assets/Scripts/Playback/GlbMover.cs`, line ~176

```csharp
Debug.Log($"[GlbMover] Input -> Left: {leftAxis} (valid:{leftValid}), RightAxis: {rightAxis} (valid:{rightValid}), Triggers: L{leftTrigger:0.00}/R{rightTrigger:0.00}");
```

- Logs controller input every frame when thumbstick touched.

**Impact:** 72‑90 logs per second during interaction → severe GC and performance hits.

**Fix:** Remove or guard with editor-only compile directives.

```csharp
#if UNITY_EDITOR
Debug.Log($"[GlbMover] Input -> Left: {leftAxis} ...");
#endif
```

---

### 4. Large GLB Download Memory Spike
**Location:** `Assets/Scripts/Playback/GlbController.cs`

```csharp
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
        ...
        yield break;
    }
    var data = uwr.downloadHandler.data;
    currentModel?.Dispose();
    var importOpt = new ImportOptions();
    var stream = new MemoryStream(data);
    currentModel = new GLTFSceneImporter(stream, importOpt);
    ...
}
```

- Download coroutine buffers entire file in `uwr.downloadHandler.data` then copies it to a `MemoryStream`.

**Impact:** Creates two full copies of the model in RAM; OOM risk for big files.

**Fix:** Prefer streaming loader (`LoadAsync`) or dispose byte arrays immediately; avoid full buffering.

```csharp
// Always use LoadAsync when possible:
_ = LoadAsync(url, () => Debug.Log("[GlbController] Model is ready."));

// Or after obtaining data:
var stream = new MemoryStream(data);
Array.Clear(data, 0, data.Length); // free the backing array
currentModel = new GLTFSceneImporter(stream, importOpt);
```

---

### 5. Point‑Cloud LOD Allocations
**Location:** `Assets/Scripts/Playback/GlbController.cs` (`ApplyPointCloudLod`)

```csharp
var outVerts = new Vector3[desired];
var srcColors = src.colors;
var outColors = (srcColors != null && srcColors.Length > 0) ? new Color[desired] : null;
var srcUV = src.uv;
var outUV = (srcUV != null && srcUV.Length > 0) ? new Vector2[desired] : null;
```

- Allocates new large arrays for vertices/colors/indices each LOD update.

**Impact:** Generates hundreds of KBs per call, causing garbage collections and frame hitches.

**Fix:** Reuse preallocated buffers; only resize when necessary.

```csharp
private Vector3[] lodVertBuffer;
private Color[] lodColorBuffer;
private Vector2[] lodUvBuffer;
private int[] lodIndexBuffer;

// inside ApplyPointCloudLod(...):
if (lodVertBuffer == null || lodVertBuffer.Length < desired)
    lodVertBuffer = new Vector3[desired];
if (lodColorBuffer == null || lodColorBuffer.Length < desired)
    lodColorBuffer = new Color[desired];
if (lodUvBuffer == null || lodUvBuffer.Length < desired)
    lodUvBuffer = new Vector2[desired];
if (lodIndexBuffer == null || lodIndexBuffer.Length < desired)
    lodIndexBuffer = new int[desired];

// Copy vertex/color/uv data into these buffers instead of allocating new arrays
```

---

### 6. Convex MeshCollider Creation
**Location:** `GlbController.SetupGlbPipeline()`

```csharp
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
```

- Adds `MeshCollider` with `convex = true` on imported models.

**Impact:** Runtime convex hull computation can freeze the app for several seconds.

**Fix:** Use `BoxCollider` based on bounds or precompute collider offline.

```csharp
if (mesh != null)
{
    // switch to box collider to avoid convex hull cost
    Destroy(meshCollider);
    var box = root.gameObject.AddComponent<BoxCollider>();
    box.center = bounds.center;
    box.size = bounds.size;
}
```

---

### 7. Material Instances Leaking
- `CardFollow` and `VideoController` create new materials in update or setters.

```csharp
if (lr.sharedMaterial == null)
    lr.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
```

and

```csharp
floorRenderer.material.color = color; // .material creates an instance!
```

**Impact:** Unbounded memory growth and GPU waste.

**Fix:** Use shared materials or `MaterialPropertyBlock`; avoid `.material` property.

```csharp
private MaterialPropertyBlock floorMPB;
private static readonly int ColorProp = Shader.PropertyToID("_Color");

public void SetFloorAlpha(float alpha)
{
    if (floorRenderer == null) return;
    if (floorMPB == null) floorMPB = new MaterialPropertyBlock();
    floorRenderer.GetPropertyBlock(floorMPB);
    Color c = floorRenderer.sharedMaterial.color;
    c.a = Mathf.Clamp01(alpha);
    floorMPB.SetColor(ColorProp, c);
    floorRenderer.SetPropertyBlock(floorMPB);
}
```
---

### 8. Mesh.Instantiate Triples Mesh Memory
**Location:** `GlbController.CachePointCloudMeshes()`

```csharp
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
```

- Instantiating mesh creates full copy; plus working mesh and original asset exist simultaneously.

**Impact:** 3× memory usage for point cloud models.

**Fix:** Read vertex data and copy on demand; discard original after LOD conversion.

```csharp
// Instead of keeping originalCopy, store only verts/colors in a small buffer
if (mesh.GetTopology(0) == MeshTopology.Points)
{
    var verts = mesh.vertices;
    var colors = mesh.colors;
    pointCloudMeshes.Add(new PointCloudMeshInfo
    {
        Filter = mf,
        Renderer = mr,
        OriginalVertices = verts, // no full duplicate mesh
        OriginalColors  = colors,
        WorkingMesh = working
    });
}
```

---

## ⚙️ Moderate Issues (Optimizations)

- Frequent `FindObjectOfType` calls (AppBoot) – minor startup cost.
- `PassthroughController.Update()` always runs even without state change – add guard.
- `WsClient` StringBuilder allocs every ping – reuse one instance.
- `CardFollow` uses `Shader.Find` in `OnValidate` – cache or use serialized material.

---

## ✅ Summary of Worst Offenders
| # | Problem | Impact | File |
|---|---------|--------|------|
|1|GlbMover per‑frame logging|72‑90 logs/sec|`GlbMover.cs`|
|2|WsClient ping logs|4‑5 logs every 1.5s|`WsClient.cs`|
|3|FileLogger blocking writes |Main-thread I/O stalls|`FileLogger.cs`|
|4|GLB download double buffer |OOM risk|`GlbController.cs`|
|5|LOD allocation GC |Hitches during scale|`GlbController.cs`|
|6|Convex collider freeze |Seconds-long freeze|`GlbController.cs`|
|7|Material leaks |GPU/memory waste|`VideoController`, `CardFollow`|
|8|Mesh copies |High memory footprint|`GlbController.cs`|

---

## 🚀 Next Steps
Implement fixes for the critical issues first—logging reductions and memory management—and then address the remaining optimizations to ensure smooth, lag-free VR performance. Feel free to ask for patches or assistance applying the changes directly.