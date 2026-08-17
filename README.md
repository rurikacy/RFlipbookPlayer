<p align="center">
  <img src="Documents/Icon.ico" alt="RFlipbookPlayer" width="96" height="96" />
  <h1 align="center">RFlipbookPlayer — Unity 序列帧播放插件</h1>
  <p align="center">
    <img alt="Unity" src="https://img.shields.io/badge/Unity-2021.3%2B-000000" />
    <img alt="C#" src="https://img.shields.io/badge/C%23-9.0-512BD4" />
    <img alt="URP" src="https://img.shields.io/badge/URP-12.1%2B-5C2D91" />
    <img alt="License" src="https://img.shields.io/badge/License-Unlicense-0F766E" />
  </p>
  <p align="center"><code>by Rurikacy</code></p>
  <p align="center"><a href="README_EN.md">English</a></p>
</p>

---

## 📖 简介

**RFlipbookPlayer** 是一个面向 Unity 的轻量级序列帧（Flipbook）播放器。它直接读取一张或多张图集，通过 UV 转移在 `RawImage` 或任意 `Renderer` 上按固定帧率播放，支持规则网格和 Sprite `Multiple` 切片两种帧识别方式。

插件将运行时播放器、事件代理、编辑器工作台和可选的 Localization 集成分开组织。你可以只使用运行时组件，也可以启用 Odin 驱动的可视化编辑器来完成图集排序、帧事件编辑和预览。

---

## ✨ 功能特性

- 支持 `RawImage` 与 `Renderer` 两类渲染目标；RawImage 运行时只修改 `uvRect`，Renderer 使用 `MaterialPropertyBlock`。
- 支持多张图集串联播放，每张图集可以有不同的有效帧数。
- `Grid` 模式按行列计算 UV；`Multiple` 模式读取 Sprite 切片并保存归一化 UV。
- 支持循环、暂停、恢复、停止、播放完成通知和一基帧号 API。
- `FlipbookPlayerEventProxy` 提供 UnityEvent 和 C# 事件，可在帧跨越时可靠触发。
- Odin 编辑器提供 Inspector、图集分段管理、帧网格工作台、切片同步和帧事件编辑。
- Shader 放在 `Resources` 中，并根据当前渲染管线选择 URP 或 Built-in 版本，降低 Player 构建中的 Shader 剥离风险。

---

## 📥 安装

### 源码导入

从仓库克隆源码导入项目或从 Releases 选择最新版本 unitypackage 包安装。
### Unity Package Manager

仓库根目录包含 `package.json`，可以在 `Window → Package Manager → + → Add package from git URL...` 中添加：

```text
https://github.com/rurikacy/RFlipbookPlayer.git#main
```

UPM 会解析 `package.json` 中的 Unity 依赖，但不会安装 Odin。使用 UPM 前请先安装 Odin Inspector。

---

## 🔗 依赖与可选集成

| 依赖 | 用途 | 是否必须 |
| --- | --- | --- |
| `com.unity.ugui` | 提供 `RawImage` 等 UI 类型。 | 必须 |
| **URP** (`com.unity.render-pipelines.universal`) | 提供 `Flipbook_Standard.shader` 使用的 URP ShaderLibrary，并在 URP 项目中执行图集 UV 计算。 | 使用 URP Shader 时必须 |
| `com.unity.localization` | 提供 `LocalizedAsset<FlipbookClip>`，让语言切换时自动替换整段 FlipbookClip。 | 仅使用多语言功能时 |
| **Odin Inspector** | 提供自定义 Inspector、工作台窗口和 SDF 图标。Odin 是付费插件，不包含在本仓库中。 | 使用编辑器工具时必须 |

### 不需要多语言？

多语言代码位于 `Integrations/Localization/`，主运行时和主编辑器程序集不再直接引用它。复制源码安装时，删除整个 `Integrations/Localization/` 目录即可隔离 Localization 集成；随后可以从项目 `Packages/manifest.json` 移除 `com.unity.localization`。使用 UPM 时请在自己的包副本中执行同样的裁剪，因为上游 `package.json` 默认声明了该依赖。

### 不需要 Odin 编辑器？

如果只需要运行时播放，可以删除 `Editor/`、`Tests/` 和 `Integrations/Localization/Editor/`。运行时播放器、事件代理、Shader 与 `FlipbookClip` 不依赖 Odin，但删除这些目录后将不再有自定义 Inspector、工作台和回归测试。

### 不使用 URP？

插件同时提供 Built-in Shader。若项目使用 Built-in Render Pipeline，可保留 `Runtime/Resources/RFlipbookPlayer/Shaders/Flipbook_Standard_Builtin.shader`，删除 `Flipbook_Standard.shader`，并从项目依赖中移除 URP。HDRP 不在当前支持范围内。

---

## 🧭 使用方法

### 1. 创建播放器

1. 创建一个带 `RawImage` 或 `MeshRenderer`/其他 `Renderer` 的 GameObject。
2. 添加 `FlipbookPlayer`。
3. 在 **图集分段** 中添加一张或多张 Texture2D。
4. 选择识别模式：
   - **Grid**：填写图集行数、列数和每段实际帧数。帧按从左到右、从上到下排列。
   - **Multiple**：将 Texture 导入为 `Sprite`、`Sprite Mode = Multiple`，完成切片后点击 **同步切片**。切片名称带相同数字后缀时，编辑器会按自然数字顺序排序。
5. 设置帧率、是否循环，以及 `Start`/`OnEnable` 自动播放选项。

`RawImage` 会保留原始材质和 UV，在运行时只更新纹理与 `uvRect`；`Renderer` 会创建一个播放器材质并通过属性块更新当前帧。首次使用 Renderer 时请确认材质的渲染队列和透明设置符合项目需求。

### 2. 运行时控制

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
        Debug.Log($"播放完成：{completedPlayer.GetTotalFrames()} 帧");
    }
}
```

常用 API：

| API | 说明 |
| --- | --- |
| `Play()` | 从第一帧开始新的播放序列；无有效帧时保持停止。 |
| `Pause()` / `Resume()` | 暂停或从当前位置恢复。 |
| `Stop()` | 停止并回到第一帧。 |
| `PreviewFrame(int)` | 在编辑器或运行时显示指定的一基全局帧号。 |
| `RefreshPreview()` | 重新计算配置并显示第一帧。 |
| `CalculateSegmentTime()` | 运行时修改公开图集/帧列表后重建累计帧缓存。 |
| `CurrentFrameNumber` | 当前一基全局帧号；无有效帧时为 `0`。 |
| `PlaybackCompleted` | 非循环播放到达末帧时触发。 |

### 3. 帧事件

在播放器上添加 `FlipbookPlayerEventProxy`，然后在 Inspector 或工作台中添加帧事件。每个事件包含一个全局帧号和 `UnityEvent`。也可以使用：

```csharp
proxy.FrameReached += frame => Debug.Log($"到达第 {frame} 帧");
proxy.Completed += () => Debug.Log("播放完成");
```

订阅后应在 `OnDisable` 中取消订阅。事件代理会处理单次跨越多帧和循环边界；同一帧配置的多个事件都会按序触发。

### 4. 可复用 Clip 与多语言

通过 `Assets → Create → Custom → Flipbook → Flipbook Clip` 创建 `FlipbookClip`，它可以被多个播放器复用。安装 Unity Localization 后，在播放器上添加 `LocalizedFlipbookBinder`，将 `LocalizedFlipbookClip` 指向 Asset Table 中的 `FlipbookClip`。语言切换完成后，绑定器会复制图集、帧数、模式和 UV 数据并刷新当前播放器；如果切换前正在播放，会从新 Clip 的第一帧继续播放。

### 5. 编辑器工作台

选择 `FlipbookPlayer`、`FlipbookClip` 或包含播放器的 GameObject，点击 Inspector 的 **打开 Flipbook 工作台**，或使用 `Tools → Flipbook Workbench`。工作台提供图集缩放、网格/切片预览、帧选择、循环预览和帧事件编辑。所有图集和事件的结构性修改都支持 Unity Undo，并会记录 Prefab 实例修改。

---

## ⚙️ 实现原理

1. 初始化或配置变化时，播放器缓存每个图集的累计结束帧。播放过程中先将时间转换为全局帧号，再用二分查找定位图集分段，得到局部帧和 UV。
2. Grid 模式根据行列反转 Y 轴计算归一化 UV；Multiple 模式使用编辑器从 `TextureImporter.spritesheet` 读取的 UV 列表。
3. RawImage 路径把当前图集设为 `RawImage.texture` 并修改 `uvRect`，尽量保留 Canvas batching。Renderer 路径把图集、帧号、模式和 UV 写入 `MaterialPropertyBlock`。
4. Shader 在顶点阶段计算 UV，避免在每个片元重复进行网格寻址；同一材质和同一图集的 Renderer 可以配合 GPU Instancing 使用。

---

## 📊 性能分析

以下结论描述算法成本，不替代目标平台上的 Profiler 测量：

| 路径 | 成本与分配 |
| --- | --- |
| 播放更新 | 没有帧变化时只做时间和边界判断；发生帧变化时按分段数 `O(log S)` 二分定位，`S` 为图集段数。播放热路径不创建托管对象。 |
| RawImage | 仅在帧变化时写入 `uvRect`；不为每个播放器复制材质，适合 UI 中共享 Canvas。切换图集时会更新纹理引用。 |
| Renderer | 初始化时最多创建一个播放器材质和两个 `MaterialPropertyBlock`；每次帧变化调用一次 `SetPropertyBlock`。属性块可能影响 SRP Batcher，多个不同图集也会降低批处理收益。 |
| Multiple UV | 每帧保存一个 `Rect`，序列化数据约为每帧 16 字节加 List 开销；大量不规则切片应关注资源内存。 |
| 帧事件 | 一次跨越会遍历事件列表；长时间暂停后跨越多个循环时，回调会按循环次数逐次派发，事件数量和跳过的循环数都会增加成本。 |
| 编辑器工作台 | 网格和切片预览按可见图集帧数 `O(F)` 重绘，主要成本发生在编辑器 GUI，不进入 Player 运行时。 |

---

## 🗂️ 目录结构

```text
RFlipbookPlayer/
├── Runtime/                         # 播放器、Clip、事件代理与资源
│   └── Resources/RFlipbookPlayer/   # 构建可寻址的 URP/Built-in Shader
├── Editor/                          # Odin Inspector、工作台与切片工具
├── Integrations/Localization/       # 可删除的 Unity Localization 集成
├── Tests/Editor/                    # EditMode 回归测试
├── Tests/Runtime/                   # PlayMode 回归测试
├── Documents/Icon.ico               # README Logo
├── LICENSE
├── package.json
├── README.md
└── README_EN.md
```

---

## ⚠️ 兼容性与限制

- 已按 Unity 2021.3 LTS 验证；URP Shader 按 12.1 系列编写。
- 当前支持 Built-in Render Pipeline 和 URP，不支持 HDRP 专用 Shader。
- 播放时间使用未缩放时间；需要受暂停控制的动画请在业务层调用 `Pause`/`Resume`。
- 公开列表是 Unity 序列化数据。运行时直接修改列表后，请调用 `CalculateSegmentTime()`，并保证帧数、UV 列表与图集顺序一致。
- Renderer 的播放器材质由组件管理；不要在播放期间从其他脚本覆盖其 `sharedMaterial` 或属性块。

---

## 📄 许可证

本项目使用 [The Unlicense](LICENSE)，允许在法律允许范围内自由复制、修改、发布和商业使用。第三方依赖遵循各自许可证；Odin Inspector 为付费商业插件，许可证由用户与 Sirenix 之间单独负责。

---

<p align="center"><em>RFlipbookPlayer · 轻量级 Unity 序列帧播放插件</em></p>
