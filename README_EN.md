<p align="center">
  <img src="Documents/Icon.ico" alt="RFlipbookPlayer" width="96" height="96" />
  <h1 align="center">RFlipbookPlayer — Unity Flipbook Playback Plugin</h1>
  <p align="center">
    <img alt="Unity" src="https://img.shields.io/badge/Unity-2021.3%2B-000000" />
    <img alt="C#" src="https://img.shields.io/badge/C%23-9.0-512BD4" />
    <img alt="URP" src="https://img.shields.io/badge/URP-12.1%2B-5C2D91" />
    <img alt="License" src="https://img.shields.io/badge/License-Unlicense-0F766E" />
  </p>
  <p align="center"><code>by Rurikacy</code></p>
  <p align="center"><a href="README.md">简体中文</a></p>
</p>

---

## 📖 Introduction

**RFlipbookPlayer** is a lightweight frame-by-frame (Flipbook) player for Unity. It reads one or more texture atlases and plays them at a fixed frame rate through UV remapping on a `RawImage` or any `Renderer`. Both regular grid layouts and Sprite `Multiple` slicing are supported.

The plugin keeps the runtime player, event proxy, editor workbench, and optional Unity Localization integration separate. You can use only the runtime components or enable the Odin-powered editor tools for atlas ordering, frame-event editing, and preview.

---

## ✨ Features

- Supports `RawImage` and `Renderer` targets. The RawImage path only updates `uvRect`; the Renderer path uses `MaterialPropertyBlock`.
- Chains multiple texture atlases, with a different valid frame count for each atlas.
- Calculates UVs from rows and columns in `Grid` mode; reads Sprite slices and stores normalized UVs in `Multiple` mode.
- Provides loop, pause, resume, stop, playback-completion notifications, and one-based frame-number APIs.
- `FlipbookPlayerEventProxy` exposes both UnityEvent and C# events and reliably dispatches events when frames are crossed.
- Odin editor tools provide a custom Inspector, atlas-segment management, a frame-grid workbench, slice synchronization, and frame-event editing.
- Shaders are stored under `Resources` and the correct URP or Built-in variant is selected for the active render pipeline, reducing the risk of shader stripping in Player builds.

---

## 📥 Installation

### Import the source

Clone the repository into the project or install the latest `.unitypackage` from Releases.

### Unity Package Manager

The repository root contains `package.json`. In `Window → Package Manager → + → Add package from git URL...`, add:

```text
https://github.com/rurikacy/RFlipbookPlayer.git#main
```

UPM resolves the Unity dependencies declared in `package.json`, but it does not install Odin. Install Odin Inspector separately before using UPM.

---

## 🔗 Dependencies and optional integrations

| Dependency | Purpose | Required |
| --- | --- | --- |
| `com.unity.ugui` | Provides UI types such as `RawImage`. | Yes |
| **URP** (`com.unity.render-pipelines.universal`) | Provides the URP `ShaderLibrary` used by `Flipbook_Standard.shader` and performs atlas UV calculations in URP projects. | Required when using the URP shader |
| `com.unity.localization` | Provides `LocalizedAsset<FlipbookClip>` so a complete `FlipbookClip` can be replaced automatically when the language changes. | Only for localization |
| **Odin Inspector** | Provides the custom Inspector, workbench window, and SDF icons. Odin is a paid plugin and is not included in this repository. | Required for editor tools |

### Do you need localization?

Localization code lives in `Integrations/Localization/`, and the main runtime and editor assemblies no longer reference it directly. When importing the source, delete the entire `Integrations/Localization/` directory to isolate the Unity Localization integration, then remove `com.unity.localization` from the project's `Packages/manifest.json`. With UPM, make the same change in your own package copy because the upstream `package.json` declares this dependency by default.

### Do you need the Odin editor?

For runtime playback only, delete `Editor/`, `Tests/`, and `Integrations/Localization/Editor/`. The runtime player, event proxy, shaders, and `FlipbookClip` do not depend on Odin, but these deletions also remove the custom Inspector, workbench, and regression tests.

### Are you using URP?

The plugin also includes a Built-in Render Pipeline shader. For a Built-in project, keep `Runtime/Resources/RFlipbookPlayer/Shaders/Flipbook_Standard_Builtin.shader`, delete `Flipbook_Standard.shader`, and remove URP from the project dependencies. HDRP is outside the current support scope.

---

## 🧭 Usage

### 1. Create a player

1. Create a GameObject with a `RawImage` or a `MeshRenderer`/other `Renderer`.
2. Add `FlipbookPlayer`.
3. Add one or more Texture2D assets under **Atlas Segments**.
4. Select the detection mode:
   - **Grid**: enter the atlas row count, column count, and the actual frame count for each segment. Frames are ordered from left to right, then top to bottom.
   - **Multiple**: import the texture as a `Sprite` with `Sprite Mode = Multiple`, slice it, then click **Sync Slices**. Slice names with the same numeric suffix are sorted in natural numeric order.
5. Set the frame rate, loop option, and automatic playback options (`Start`/`OnEnable`).

`RawImage` keeps its original material and UV settings and updates only the texture and `uvRect` at runtime. `Renderer` creates a player material and updates the current frame through a property block. When using a Renderer for the first time, verify that its material queue and transparency settings match the project.

### 2. Control playback at runtime

```csharp
using UnityEngine;

public sealed class FlipbookExample : MonoBehaviour
{
    [SerializeField] private FlipbookPlayer player;

    private void OnEnable()
    {
        player.PlaybackCompleted += OnPlaybackCompleted;
        player.Play();
    }

    private void OnDisable()
    {
        player.PlaybackCompleted -= OnPlaybackCompleted;
    }

    private void OnPlaybackCompleted(FlipbookPlayer completedPlayer)
    {
        Debug.Log($"Playback completed: {completedPlayer.GetTotalFrames()} frames");
    }
}
```

Common APIs:

| API | Description |
| --- | --- |
| `Play()` | Starts a new playback sequence from the first frame; remains stopped when no valid frames exist. |
| `Pause()` / `Resume()` | Pauses playback or resumes it from the current position. |
| `Stop()` | Stops playback and returns to the first frame. |
| `PreviewFrame(int)` | Displays a specified one-based global frame number in the Editor or at runtime. |
| `RefreshPreview()` | Rebuilds the configuration and displays the first frame. |
| `CalculateSegmentTime()` | Rebuilds the cumulative frame cache after changing the public atlas/frame lists at runtime. |
| `CurrentFrameNumber` | Current one-based global frame number; `0` when no valid frames exist. |
| `PlaybackCompleted` | Raised when non-looping playback reaches the last frame. |

### 3. Frame events

Add `FlipbookPlayerEventProxy` to the player, then add frame events in the Inspector or workbench. Each event contains a global frame number and a `UnityEvent`. You can also subscribe in code:

```csharp
proxy.FrameReached += frame => Debug.Log($"Reached frame {frame}");
proxy.Completed += () => Debug.Log("Playback completed");
```

Unsubscribe in `OnDisable` after subscribing. The event proxy handles multi-frame jumps and loop boundaries; multiple events configured for the same frame are dispatched in order.

### 4. Reusable clips and localization

Create a `FlipbookClip` through `Assets → Create → Custom → Flipbook → Flipbook Clip` and reuse it across multiple players. After installing Unity Localization, add `LocalizedFlipbookBinder` to a player and assign `LocalizedFlipbookClip` from an Asset Table. When the language changes, the binder copies the atlas list, frame counts, mode, and UV data, then refreshes the player. If playback was active before the change, it resumes from the first frame of the new clip.

### 5. Editor workbench

Select a `FlipbookPlayer`, `FlipbookClip`, or a GameObject containing a player, then click **Open Flipbook Workbench** in the Inspector or use `Tools → Flipbook Workbench`. The workbench provides atlas zoom, grid/slice preview, frame selection, loop preview, and frame-event editing. Structural changes to atlases and events support Unity Undo and record Prefab instance modifications.

---

## ⚙️ Implementation principles

1. During initialization or configuration changes, the player caches each atlas's cumulative end frame. During playback, it converts time to a global frame number, locates the atlas segment with binary search, and resolves the local frame and UV.
2. In `Grid` mode, normalized UVs are calculated from rows and columns with the Y axis flipped. In `Multiple` mode, the editor uses UV data read from `TextureImporter.spritesheet`.
3. The RawImage path assigns the current atlas to `RawImage.texture` and updates `uvRect`, preserving Canvas batching where possible. The Renderer path writes the atlas, frame number, mode, and UV to a `MaterialPropertyBlock`.
4. The shader calculates UVs in the vertex stage, avoiding repeated grid addressing per fragment. Renderers that share a material and atlas can use GPU Instancing.

---

## 📊 Performance analysis

The following describes algorithmic costs and does not replace profiling on the target platform:

| Path | Cost and allocations |
| --- | --- |
| Playback update | When the frame does not change, only time and boundary checks run. On a frame change, binary search locates the segment in `O(log S)`, where `S` is the number of atlas segments. The hot path allocates no managed objects. |
| RawImage | Writes `uvRect` only when the frame changes and does not duplicate a material per player, making it suitable for shared UI Canvases. The texture reference is updated when the atlas changes. |
| Renderer | Initialization creates at most one player material and two `MaterialPropertyBlock` instances; each frame change calls `SetPropertyBlock` once. Property blocks may reduce SRP Batcher compatibility, and different atlases also reduce batching efficiency. |
| Multiple UV | Stores one `Rect` per frame, approximately 16 bytes per frame plus `List` overhead. Large sets of irregular slices should be monitored for asset memory usage. |
| Frame events | A jump traverses the event list. After a long pause, crossing multiple loops dispatches callbacks once per loop, so cost grows with both event count and skipped loops. |
| Editor workbench | Grid and slice previews redraw in `O(F)` for the visible atlas frames. This cost is confined to Editor GUI and does not enter Player runtime. |

---

## 🗂️ Directory structure

```text
RFlipbookPlayer/
├── Runtime/                         # Player, clip, event proxy, and runtime assets
│   └── Resources/RFlipbookPlayer/   # Addressable URP/Built-in shaders for builds
├── Editor/                          # Odin Inspector, workbench, and slice tools
├── Integrations/Localization/       # Optional Unity Localization integration
├── Tests/Editor/                    # EditMode regression tests
├── Tests/Runtime/                   # PlayMode regression tests
├── Documents/Icon.ico               # README logo
├── LICENSE
├── package.json
├── README.md
└── README_EN.md
```

---

## ⚠️ Compatibility and limitations

- Verified with Unity 2021.3 LTS; the URP shader targets the 12.1 series.
- Supports the Built-in Render Pipeline and URP. HDRP-specific shaders are not supported.
- Playback uses unscaled time. For animations that should pause with the game, call `Pause`/`Resume` from the application layer.
- Public lists are Unity-serialized data. After changing them directly at runtime, call `CalculateSegmentTime()` and keep frame counts, UV lists, and atlas order consistent.
- The Renderer player material is managed by the component. Do not overwrite its `sharedMaterial` or property block from another script during playback.

---

## 📄 License

This project is released under [The Unlicense](LICENSE), allowing copying, modification, distribution, and commercial use to the extent permitted by law. Third-party dependencies retain their own licenses. Odin Inspector is a paid commercial plugin and its license is handled separately between the user and Sirenix.

---

<p align="center"><em>RFlipbookPlayer · Lightweight Unity Flipbook Playback Plugin</em></p>
