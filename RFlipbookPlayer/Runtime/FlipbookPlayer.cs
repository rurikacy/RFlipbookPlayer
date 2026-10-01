using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
///     播放规则网格或 Sprite Multiple 切片形式的序列帧动画。
/// </summary>
[ExecuteAlways]
[AddComponentMenu("Custom/Flipbook 序列帧播放器")]
public class FlipbookPlayer : MonoBehaviour
{
    private const string BuiltInShaderResourcePath = "RFlipbookPlayer/Shaders/Flipbook_Standard_Builtin";
    private const string UniversalShaderResourcePath = "RFlipbookPlayer/Shaders/Flipbook_Standard";

    private static readonly int _currentFrameId = Shader.PropertyToID("_CurrentFrame");
    private static readonly int _mainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int _rowId = Shader.PropertyToID("_Row");
    private static readonly int _colId = Shader.PropertyToID("_Col");
    private static readonly int _totalFrameId = Shader.PropertyToID("_TotalFrame");
    private static readonly int _frameModeId = Shader.PropertyToID("_FrameMode");
    private static readonly int _frameRectId = Shader.PropertyToID("_FrameRect");
    private static Shader _builtInShader;
    private static Shader _universalShader;

    /// <summary>
    ///     按播放顺序排列的序列帧图集。
    /// </summary>
    [Tooltip("序列帧图集列表，单图集模式只需放入一张即可")]
    public List<Texture2D> textureList = new();

    /// <summary>
    ///     获取当前 Flipbook 的帧识别方式；Grid 使用规则网格，Multiple 使用由 Editor 同步的 Sprite 切片信息。
    /// </summary>
    [Tooltip("帧识别方式：固定网格或 Multiple Sprite 切片")]
    public FlipbookFrameSourceMode frameSourceMode = FlipbookFrameSourceMode.Grid;

    /// <summary>
    ///     对应每张图集的实际总帧数；Grid 模式下会受网格容量约束。
    /// </summary>
    [Tooltip("对应每张图集的实际总帧数，例如 144")]
    public List<int> frameList = new();

    /// <summary>
    ///     Multiple 模式下按分段和帧顺序保存的归一化 UV 矩形，坐标原点位于纹理左下角。
    /// </summary>
    [Tooltip("Multiple 模式下由同步切片生成的帧 UV 数据")]
    public List<Rect> multipleFrameUvList = new();

    /// <summary>
    ///     图集物理网格行数。
    /// </summary>
    [Tooltip("图集物理网格行数（Texture高度 / 单帧高度）")]
    public int row = 16;

    /// <summary>
    ///     图集物理网格列数。
    /// </summary>
    [Tooltip("图集物理网格列数（Texture宽度 / 单帧宽度）")]
    public int column = 16;

    /// <summary>
    ///     目标播放帧率。
    /// </summary>
    [Tooltip("目标播放帧率（FPS）")]
    public int frameRate = 24;

    /// <summary>
    ///     每帧上边缘向内收缩的纹理像素数。
    /// </summary>
    [Tooltip("每帧上边缘向内收缩的纹理像素数，0 表示不收缩")]
    [Min(0)] public int insetTop;

    /// <summary>
    ///     每帧下边缘向内收缩的纹理像素数。
    /// </summary>
    [Tooltip("每帧下边缘向内收缩的纹理像素数，0 表示不收缩")]
    [Min(0)] public int insetBottom;

    /// <summary>
    ///     每帧左边缘向内收缩的纹理像素数。
    /// </summary>
    [Tooltip("每帧左边缘向内收缩的纹理像素数，0 表示不收缩")]
    [Min(0)] public int insetLeft;

    /// <summary>
    ///     每帧右边缘向内收缩的纹理像素数。
    /// </summary>
    [Tooltip("每帧右边缘向内收缩的纹理像素数，0 表示不收缩")]
    [Min(0)] public int insetRight;

    /// <summary>
    ///     播放到末帧后是否从第一帧重新开始。
    /// </summary>
    [Tooltip("播放到末帧后是否从第一帧重新开始")]
    public bool loop = true;

    /// <summary>
    ///     进入 Play Mode 后是否在 Start 生命周期自动播放。
    /// </summary>
    [Tooltip("进入 Play Mode 后是否在 Start 生命周期自动播放")]
    public bool autoPlayOnStart = true;

    /// <summary>
    ///     组件每次启用时是否自动播放。
    /// </summary>
    [Tooltip("组件每次启用时是否自动播放")]
    public bool autoPlayOnEnable = false;
    private readonly List<int> _segmentEndFrames = new();
    private int _appliedFrame = -1;
    private int _cachedTotalFrameCount;
    private int _currentSegIndex = -1;
    private bool _hasOriginalRawImageState;
    private bool _isInitialized;
    private bool _ownsTargetMaterial;
    private Material _originalRawImageMaterial;
    private Texture _originalRawImageTexture;
    private Material _originalRendererMaterial;
    private MaterialPropertyBlock _originalPropertyBlock;
    private MaterialPropertyBlock _propertyBlock;
    private RawImage _rawImage;
    private Rect _originalRawImageUvRect;
    private Renderer _renderer;

    private Material _targetMat;

    private float _totalTime;

    /// <summary>
    ///     获取当前全局帧号；有效帧号从 1 开始，无有效帧时为 0。
    /// </summary>
    public int CurrentFrameNumber { get; private set; }

    /// <summary>
    ///     获取播放器当前是否正在推进时间。
    /// </summary>
    public bool IsPlaying { get; private set; }

    /// <summary>
    ///     获取当前播放序列已经完成的循环次数。
    /// </summary>
    public int PlaybackLoopCount { get; private set; }

    /// <summary>
    ///     获取播放序列标识；每次成功调用 <see cref="Play" /> 后递增。
    /// </summary>
    public int PlaybackSequenceId { get; private set; }

    /// <summary>
    ///     非循环播放到达末帧时触发。
    /// </summary>
    public event Action<FlipbookPlayer> PlaybackCompleted;

    /// <summary>
    ///     获取播放器是否已找到可用的渲染目标。
    /// </summary>
    private bool HasRenderTarget => _rawImage || (_renderer && _targetMat);

    /// <summary>
    ///     启动时初始化播放器并按配置自动播放。
    /// </summary>
    private void Start()
    {
        InitPlayer();

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            RefreshPreview();
            return;
        }
#endif

        if (autoPlayOnStart && Application.isPlaying && !IsPlaying) Play();
    }

    /// <summary>
    ///     按未缩放时间推进运行时播放。
    /// </summary>
    private void Update()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && !IsPlaying) return;
#endif

        if (IsPlaying && textureList != null && _segmentEndFrames.Count != textureList.Count)
            CalculateSegmentTime();

        if (!IsPlaying || textureList == null || textureList.Count == 0 ||
            _cachedTotalFrameCount <= 0 || _segmentEndFrames.Count == 0 || !HasRenderTarget)
        {
            return;
        }

        float fullDuration = _cachedTotalFrameCount / (float)Mathf.Max(1, frameRate);
        if (fullDuration <= 0f) return;

        _totalTime += Time.unscaledDeltaTime;

        bool completed = false;
        if (_totalTime >= fullDuration)
        {
            if (loop)
            {
                PlaybackLoopCount += Mathf.Max(1, Mathf.FloorToInt(_totalTime / fullDuration));
                _totalTime = Mathf.Repeat(_totalTime, fullDuration);
            }
            else
            {
                _totalTime = fullDuration;
                IsPlaying = false;
                completed = true;
            }
        }

        UpdateAnimationState(_totalTime);

        if (completed) PlaybackCompleted?.Invoke(this);
    }

    /// <summary>
    ///     启用时初始化播放器并按配置自动播放。
    /// </summary>
    private void OnEnable()
    {
        InitPlayer();

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            RefreshPreview();
            return;
        }
#endif

        if (autoPlayOnEnable && Application.isPlaying) Play();
    }

    /// <summary>
    ///     停用时停止播放并清理编辑器预览材质。
    /// </summary>
    private void OnDisable()
    {
        IsPlaying = false;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            CleanupTargetMaterial(true);
            _isInitialized = false;
        }
#endif
    }

    /// <summary>
    ///     销毁时释放播放器持有的材质状态。
    /// </summary>
    private void OnDestroy()
    {
#if UNITY_EDITOR
        CleanupTargetMaterial(!Application.isPlaying);
#else
        CleanupTargetMaterial(false);
#endif
    }

    /// <summary>
    ///     在编辑器中校验并修正序列化配置。
    /// </summary>
    private void OnValidate()
    {
        row = Mathf.Max(1, row);
        column = Mathf.Max(1, column);
        frameRate = Mathf.Max(1, frameRate);
        insetTop = Mathf.Max(0, insetTop);
        insetBottom = Mathf.Max(0, insetBottom);
        insetLeft = Mathf.Max(0, insetLeft);
        insetRight = Mathf.Max(0, insetRight);

        textureList ??= new List<Texture2D>();
        frameList ??= new List<int>();
        multipleFrameUvList ??= new List<Rect>();

        int gridFrames = GetGridFrameCapacity();

        while (frameList.Count < textureList.Count)
            frameList.Add(frameSourceMode == FlipbookFrameSourceMode.Grid ? gridFrames : 0);

        while (frameList.Count > textureList.Count) frameList.RemoveAt(frameList.Count - 1);

        for (int i = 0; i < frameList.Count; i++)
            frameList[i] = frameSourceMode == FlipbookFrameSourceMode.Grid
                ? Mathf.Clamp(frameList[i], 1, gridFrames)
                : Mathf.Max(0, frameList[i]);

        CalculateSegmentTime();

#if UNITY_EDITOR
        if (!Application.isPlaying) RefreshPreview();
#endif
    }

    /// <summary>
    ///     查找渲染目标并初始化播放器材质和状态。
    /// </summary>
    private void InitPlayer()
    {
        if (_isInitialized) return;

        EnsureCollections();

        _rawImage = GetComponent<RawImage>();
        if (_rawImage)
        {
            _originalRawImageMaterial = _rawImage.material;
            _originalRawImageTexture = _rawImage.texture;
            _originalRawImageUvRect = _rawImage.uvRect;
            _hasOriginalRawImageState = true;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                _targetMat = CreateFlipbookMaterial(_originalRawImageMaterial);
                if (_targetMat)
                {
                    _ownsTargetMaterial = true;
                    _targetMat.hideFlags = HideFlags.HideAndDontSave;
                    _rawImage.material = _targetMat;
                }
            }
            else
#endif
            {
                // 运行时直接更新 UV，保留共享 UI 材质和 Canvas 批处理。
                if (IsFlipbookMaterial(_originalRawImageMaterial)) _rawImage.material = null;
            }
        }
        else
        {
            _renderer = GetComponent<Renderer>();
            if (_renderer)
            {
                _originalRendererMaterial = _renderer.sharedMaterial;
                _targetMat = IsFlipbookMaterial(_originalRendererMaterial)
                    ? _originalRendererMaterial
                    : CreateFlipbookMaterial(_originalRendererMaterial);

                if (_targetMat)
                {
                    _ownsTargetMaterial = _targetMat != _originalRendererMaterial;
                    if (_ownsTargetMaterial) _renderer.sharedMaterial = _targetMat;

                    _originalPropertyBlock = new MaterialPropertyBlock();
                    _renderer.GetPropertyBlock(_originalPropertyBlock);

                    _propertyBlock = new MaterialPropertyBlock();
                    _renderer.GetPropertyBlock(_propertyBlock);
                }
            }
        }

        CalculateSegmentTime();
        _isInitialized = true;
    }

    /// <summary>
    ///     恢复渲染目标并释放播放器创建的材质。
    /// </summary>
    /// <param name="immediate">是否立即销毁播放器创建的材质。</param>
    private void CleanupTargetMaterial(bool immediate)
    {
        if (_rawImage && _hasOriginalRawImageState)
        {
            _rawImage.uvRect = _originalRawImageUvRect;
            if (_rawImage.texture != _originalRawImageTexture)
                _rawImage.texture = _originalRawImageTexture;
            if (_rawImage.material != _originalRawImageMaterial)
                _rawImage.material = _originalRawImageMaterial;
        }

        if (_renderer && _propertyBlock != null) _renderer.SetPropertyBlock(_originalPropertyBlock);

        if (_renderer && _ownsTargetMaterial && _renderer.sharedMaterial == _targetMat)
            _renderer.sharedMaterial = _originalRendererMaterial;

        if (_ownsTargetMaterial && _targetMat)
        {
#if UNITY_EDITOR
            if (immediate)
                DestroyImmediate(_targetMat);
            else
#endif
                Destroy(_targetMat);
        }

        _targetMat = null;
        _originalRawImageMaterial = null;
        _originalRawImageTexture = null;
        _originalRendererMaterial = null;
        _originalPropertyBlock = null;
        _propertyBlock = null;
        _hasOriginalRawImageState = false;
        _ownsTargetMaterial = false;
        _appliedFrame = -1;
        _currentSegIndex = -1;
    }

    /// <summary>
    ///     重建各图集分段的累计帧缓存。运行时修改公开配置后应调用此方法。
    /// </summary>
    public void CalculateSegmentTime()
    {
        EnsureCollections();
        _segmentEndFrames.Clear();
        _appliedFrame = -1;
        _currentSegIndex = -1;

        long accumulatedFrames = 0;
        for (int i = 0; i < textureList.Count; i++)
        {
            int frames = GetSafeFrameCount(i);
            accumulatedFrames = Math.Min(int.MaxValue, accumulatedFrames + frames);
            _segmentEndFrames.Add((int)accumulatedFrames);
        }

        _cachedTotalFrameCount = (int)accumulatedFrames;
    }

    /// <summary>
    ///     获取指定图集分段的有效帧数。
    /// </summary>
    /// <param name="index">从 0 开始的图集分段索引。</param>
    /// <returns>指定分段经过模式约束后的有效帧数。</returns>
    private int GetSafeFrameCount(int index)
    {
        if (frameSourceMode == FlipbookFrameSourceMode.Multiple)
        {
            if (frameList != null && index >= 0 && index < frameList.Count)
                return Mathf.Max(0, frameList[index]);
            return 0;
        }

        int gridFrames = GetGridFrameCapacity();

        if (frameList != null && index >= 0 && index < frameList.Count)
            return Mathf.Clamp(frameList[index], 1, gridFrames);

        return gridFrames;
    }

    /// <summary>
    ///     根据播放时间切换到对应的全局帧。
    /// </summary>
    /// <param name="timePosition">当前播放位置，单位为秒。</param>
    private void UpdateAnimationState(float timePosition)
    {
        if (_cachedTotalFrameCount <= 0 || _segmentEndFrames.Count == 0)
        {
            CurrentFrameNumber = 0;
            return;
        }

        int globalFrameIndex = Mathf.Clamp(
            Mathf.FloorToInt(Mathf.Max(0f, timePosition) * Mathf.Max(1, frameRate)),
            0,
            _cachedTotalFrameCount - 1);
        int globalFrameNumber = globalFrameIndex + 1;
        if (_currentSegIndex >= 0 && CurrentFrameNumber == globalFrameNumber) return;

        int targetIndex = FindSegmentForFrame(globalFrameIndex);
        if (targetIndex < 0) return;

        if (targetIndex != _currentSegIndex) SwitchSegment(targetIndex);

        int segmentStartFrame = targetIndex > 0 ? _segmentEndFrames[targetIndex - 1] : 0;
        int localFrame = globalFrameIndex - segmentStartFrame;
        CurrentFrameNumber = globalFrameNumber;

        SetCurrentFrame(localFrame);
    }

    /// <summary>
    ///     查找全局帧所属的图集分段。
    /// </summary>
    /// <param name="globalFrameIndex">从 0 开始的全局帧索引。</param>
    /// <returns>全局帧所在的分段索引；未找到时返回 -1。</returns>
    private int FindSegmentForFrame(int globalFrameIndex)
    {
        int low = 0;
        int high = _segmentEndFrames.Count - 1;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (globalFrameIndex < _segmentEndFrames[middle])
                high = middle;
            else
                low = middle + 1;
        }

        return globalFrameIndex < _segmentEndFrames[low] ? low : -1;
    }

    /// <summary>
    ///     将分段内帧的 UV 应用到当前渲染目标。
    /// </summary>
    /// <param name="frame">从 0 开始的当前分段内帧索引。</param>
    private void SetCurrentFrame(int frame)
    {
        if (_appliedFrame == frame) return;

        _appliedFrame = frame;
        if (_rawImage)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (_targetMat)
                {
                    _targetMat.SetFloat(_currentFrameId, frame);
                    _targetMat.SetVector(_frameRectId, ToVector4(GetCurrentFrameRect(frame)));
                }
                return;
            }
#endif

            Rect frameRect = GetCurrentFrameRect(frame);
            _rawImage.uvRect = new Rect(
                _originalRawImageUvRect.x + frameRect.x * _originalRawImageUvRect.width,
                _originalRawImageUvRect.y + frameRect.y * _originalRawImageUvRect.height,
                frameRect.width * _originalRawImageUvRect.width,
                frameRect.height * _originalRawImageUvRect.height);
        }
        else if (_renderer && _propertyBlock != null)
        {
            _propertyBlock.SetFloat(_currentFrameId, frame);
            _propertyBlock.SetVector(_frameRectId, ToVector4(GetCurrentFrameRect(frame)));
            _renderer.SetPropertyBlock(_propertyBlock);
        }
    }

    /// <summary>
    ///     将 UV 矩形转换为材质属性向量。
    /// </summary>
    /// <param name="rect">要转换的帧 UV 矩形。</param>
    /// <returns>依次包含 UV 起点和宽高的向量。</returns>
    private static Vector4 ToVector4(Rect rect)
    {
        return new Vector4(rect.x, rect.y, rect.width, rect.height);
    }

    /// <summary>
    ///     获取当前分段内帧经过边缘内收的 UV 矩形。
    /// </summary>
    /// <param name="frame">从 0 开始的当前分段内帧索引。</param>
    /// <returns>已按像素内收的归一化帧 UV 矩形。</returns>
    private Rect GetCurrentFrameRect(int frame)
    {
        Rect frameRect;
        if (frameSourceMode == FlipbookFrameSourceMode.Multiple && TryGetMultipleFrameUv(_currentSegIndex, frame, out Rect multipleRect))
            frameRect = multipleRect;
        else
        {
            int safeRow = Mathf.Max(1, row);
            int safeColumn = Mathf.Max(1, column);
            int colIndex = frame % safeColumn;
            int rowIndex = safeRow - 1 - frame / safeColumn;
            frameRect = new Rect(
                colIndex / (float)safeColumn,
                rowIndex / (float)safeRow,
                1f / safeColumn,
                1f / safeRow);
        }

        Texture2D texture = _currentSegIndex >= 0 && _currentSegIndex < textureList.Count
            ? textureList[_currentSegIndex]
            : null;
        return InsetFrameRect(frameRect, texture, insetTop, insetBottom, insetLeft, insetRight);
    }

    /// <summary>
    ///     按纹理像素收缩帧 UV，且至少保留一个像素的采样范围。
    /// </summary>
    /// <param name="frameRect">原始帧 UV。</param>
    /// <param name="texture">帧所在图集。</param>
    /// <param name="top">上边缘内收像素数。</param>
    /// <param name="bottom">下边缘内收像素数。</param>
    /// <param name="left">左边缘内收像素数。</param>
    /// <param name="right">右边缘内收像素数。</param>
    /// <returns>收缩后的帧 UV。</returns>
    internal static Rect InsetFrameRect(Rect frameRect, Texture2D texture, int top, int bottom, int left, int right)
    {
        if (!texture) return frameRect;

        float leftPixels = Mathf.Min(Mathf.Max(0, left), Mathf.Max(0f, frameRect.width * texture.width - 1f));
        float rightPixels = Mathf.Min(Mathf.Max(0, right), Mathf.Max(0f, frameRect.width * texture.width - leftPixels - 1f));
        float bottomPixels = Mathf.Min(Mathf.Max(0, bottom), Mathf.Max(0f, frameRect.height * texture.height - 1f));
        float topPixels = Mathf.Min(Mathf.Max(0, top), Mathf.Max(0f, frameRect.height * texture.height - bottomPixels - 1f));
        return new Rect(
            frameRect.x + leftPixels / texture.width,
            frameRect.y + bottomPixels / texture.height,
            frameRect.width - (leftPixels + rightPixels) / texture.width,
            frameRect.height - (bottomPixels + topPixels) / texture.height);
    }

    /// <summary>
    ///     尝试读取指定 Multiple 切片帧的 UV。
    /// </summary>
    /// <param name="segmentIndex">从 0 开始的图集分段索引。</param>
    /// <param name="localFrame">从 0 开始的分段内帧索引。</param>
    /// <param name="frameUv">成功时返回归一化的帧 UV 矩形。</param>
    /// <returns>找到有效切片 UV 时返回 true，否则返回 false。</returns>
    private bool TryGetMultipleFrameUv(int segmentIndex, int localFrame, out Rect frameUv)
    {
        frameUv = default;
        if (multipleFrameUvList == null || segmentIndex < 0 || localFrame < 0 || localFrame >= GetSafeFrameCount(segmentIndex))
            return false;

        int frameIndex = localFrame + (segmentIndex > 0 && segmentIndex <= _segmentEndFrames.Count
            ? _segmentEndFrames[segmentIndex - 1]
            : 0);
        if (frameIndex < 0 || frameIndex >= multipleFrameUvList.Count) return false;

        frameUv = multipleFrameUvList[frameIndex];
        return frameUv is { width: > 0f, height: > 0f };
    }

    /// <summary>
    ///     切换图集分段并更新渲染材质属性。
    /// </summary>
    /// <param name="index">从 0 开始的目标图集分段索引。</param>
    private void SwitchSegment(int index)
    {
        if (!HasRenderTarget || index < 0 || index >= textureList.Count) return;

        _currentSegIndex = index;
        _appliedFrame = -1;

        Texture2D texture = textureList[index];
        int totalFrame = GetSafeFrameCount(index);

        // RawImage 必须同步 texture，否则 UI 会继续使用默认白贴图
        if (_rawImage) _rawImage.texture = texture;

#if UNITY_EDITOR
        if (_rawImage && !Application.isPlaying && _targetMat)
        {
            _targetMat.SetTexture(_mainTexId, texture);
            _targetMat.SetFloat(_rowId, row);
            _targetMat.SetFloat(_colId, column);
            _targetMat.SetFloat(_totalFrameId, totalFrame);
            _targetMat.SetFloat(_frameModeId, frameSourceMode == FlipbookFrameSourceMode.Multiple ? 1f : 0f);
        }
#endif

        if (_renderer && _propertyBlock != null)
        {
            _propertyBlock.SetTexture(_mainTexId, texture);
            _propertyBlock.SetFloat(_rowId, row);
            _propertyBlock.SetFloat(_colId, column);
            _propertyBlock.SetFloat(_totalFrameId, totalFrame);
            _propertyBlock.SetFloat(_frameModeId, frameSourceMode == FlipbookFrameSourceMode.Multiple ? 1f : 0f);
            _renderer.SetPropertyBlock(_propertyBlock);
        }
    }

    /// <summary>
    ///     创建适配当前渲染管线的 Flipbook 材质。
    /// </summary>
    /// <param name="source">用于复制渲染设置的原始材质。</param>
    /// <returns>创建的 Flipbook 材质；找不到 Shader 时返回 null。</returns>
    private Material CreateFlipbookMaterial(Material source)
    {
        Shader shader = GetFlipbookShader();

        if (shader)
        {
            Material material = new(shader);
            if (source)
            {
                material.CopyMatchingPropertiesFromMaterial(source);
                material.renderQueue = source.renderQueue;
            }

            material.enableInstancing = true;
            return material;
        }

        Debug.LogError(
            "RFlipbookPlayer 找不到当前渲染管线对应的 Shader。请确认 Runtime/Resources/RFlipbookPlayer/Shaders 目录完整。",
            this);
        return null;
    }

    /// <summary>
    ///     获取当前渲染管线对应的 Flipbook Shader。
    /// </summary>
    /// <returns>当前渲染管线适用的 Shader；未找到时返回 null。</returns>
    private static Shader GetFlipbookShader()
    {
        if (GraphicsSettings.currentRenderPipeline == null)
        {
            if (!_builtInShader)
                _builtInShader = Resources.Load<Shader>(BuiltInShaderResourcePath) ??
                                 Shader.Find("Custom/Flipbook_Standard_Builtin");
            return _builtInShader;
        }

        if (!_universalShader)
            _universalShader = Resources.Load<Shader>(UniversalShaderResourcePath) ??
                               Shader.Find("Custom/Flipbook_Standard");
        return _universalShader;
    }

    /// <summary>
    ///     获取网格图集可容纳的最大帧数。
    /// </summary>
    /// <param name="rows">网格行数。</param>
    /// <param name="columns">网格列数。</param>
    /// <returns>网格图集可容纳的最大帧数。</returns>
    private static int GetGridFrameCapacity(int rows, int columns)
    {
        long capacity = (long)Mathf.Max(1, rows) * Mathf.Max(1, columns);
        return (int)Math.Min(int.MaxValue, capacity);
    }

    /// <summary>
    ///     获取网格图集可容纳的最大帧数。
    /// </summary>
    /// <returns>网格图集可容纳的最大帧数。</returns>
    private int GetGridFrameCapacity()
    {
        return GetGridFrameCapacity(row, column);
    }

    /// <summary>
    ///     确保序列帧配置列表已初始化。
    /// </summary>
    private void EnsureCollections()
    {
        textureList ??= new List<Texture2D>();
        frameList ??= new List<int>();
        multipleFrameUvList ??= new List<Rect>();
    }

    /// <summary>
    ///     判断材质是否包含播放器需要的 Shader 属性。
    /// </summary>
    /// <param name="material">待检查的材质。</param>
    /// <returns>材质包含全部必要 Shader 属性时返回 true，否则返回 false。</returns>
    private bool IsFlipbookMaterial(Material material)
    {
        return material
               && material.HasProperty(_rowId)
               && material.HasProperty(_colId)
               && material.HasProperty(_totalFrameId)
               && material.HasProperty(_frameModeId)
               && material.HasProperty(_frameRectId)
               && material.HasProperty(_currentFrameId);
    }

    /// <summary>
    ///     获取所有图集的有效总帧数。
    /// </summary>
    /// <returns>所有图集分段的有效总帧数。</returns>
    public int GetTotalFrames()
    {
        if (textureList == null) return 0;

        long total = 0;
        for (int i = 0; i < textureList.Count; i++) total += GetSafeFrameCount(i);

        return (int)Math.Min(int.MaxValue, total);
    }

    /// <summary>
    ///     在 Edit Mode 下预览指定帧（1-based 全局帧号）。
    /// </summary>
    /// <param name="globalFrame">从 1 开始的全局帧号。</param>
    public void PreviewFrame(int globalFrame)
    {
        InitPlayer();

        int totalFrames = GetTotalFrames();
        if (textureList.Count == 0 || !HasRenderTarget || totalFrames <= 0) return;

        int clampedGlobalFrame = Mathf.Clamp(globalFrame, 1, totalFrames);
        int remaining = clampedGlobalFrame;
        int segIndex = 0;

        for (int i = 0; i < textureList.Count; i++)
        {
            int segFrames = GetSafeFrameCount(i);
            if (segFrames <= 0) continue;
            if (remaining <= segFrames)
            {
                segIndex = i;
                break;
            }

            remaining -= segFrames;
        }

        int localFrame = remaining - 1;

        SwitchSegment(segIndex);
        SetCurrentFrame(localFrame);
        CurrentFrameNumber = clampedGlobalFrame;

#if UNITY_EDITOR
        if (!Application.isPlaying) ApplyRawImageEditorPreview();
#endif
    }

    /// <summary>
    ///     在 Edit Mode 下刷新显示第一帧预览。
    /// </summary>
    public void RefreshPreview()
    {
        InitPlayer();
        CalculateSegmentTime();

        if (textureList.Count == 0 || !HasRenderTarget || _cachedTotalFrameCount <= 0)
        {
            IsPlaying = false;
            CurrentFrameNumber = 0;
            return;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            IsPlaying = false;
            _totalTime = 0f;
        }
#endif

        int firstSegment = FindFirstSegmentWithFrames();
        if (firstSegment < 0) return;

        SwitchSegment(firstSegment);
        SetCurrentFrame(0);
        CurrentFrameNumber = 1;

#if UNITY_EDITOR
        if (!Application.isPlaying) ApplyRawImageEditorPreview();
#endif
    }

    /// <summary>
    ///     查找第一个包含有效帧的图集分段。
    /// </summary>
    /// <returns>首个包含有效帧的分段索引；未找到时返回 -1。</returns>
    private int FindFirstSegmentWithFrames()
    {
        for (int i = 0; i < textureList.Count; i++)
            if (GetSafeFrameCount(i) > 0)
                return i;

        return -1;
    }

    /// <summary>
    ///     从第一帧开始播放；没有有效帧或渲染目标时不会进入播放状态。
    /// </summary>
    public void Play()
    {
        InitPlayer();

        if (textureList.Count == 0 || !HasRenderTarget) return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            RefreshPreview();
            return;
        }
#endif

        CalculateSegmentTime();
        if (_cachedTotalFrameCount <= 0)
        {
            IsPlaying = false;
            CurrentFrameNumber = 0;
            return;
        }

        PlaybackSequenceId++;
        PlaybackLoopCount = 0;
        IsPlaying = true;
        _totalTime = 0f;
        _currentSegIndex = -1;

        UpdateAnimationState(0f);
    }

    /// <summary>
    ///     暂停播放并保留当前位置。
    /// </summary>
    public void Pause()
    {
        IsPlaying = false;
    }

    /// <summary>
    ///     从当前位置恢复播放。
    /// </summary>
    public void Resume()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            RefreshPreview();
            return;
        }
#endif

        if (textureList is { Count: > 0 } && _cachedTotalFrameCount > 0 && HasRenderTarget)
            IsPlaying = true;
    }

    /// <summary>
    ///     停止播放并回到第一帧。
    /// </summary>
    public void Stop()
    {
        IsPlaying = false;
        PlaybackLoopCount = 0;
        _totalTime = 0f;
        _currentSegIndex = -1;

        if (_isInitialized && HasRenderTarget && textureList is { Count: > 0 } && _cachedTotalFrameCount > 0)
            UpdateAnimationState(0f);
        else
            CurrentFrameNumber = 0;

#if UNITY_EDITOR
        if (!Application.isPlaying) ApplyRawImageEditorPreview();
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    ///     应用编辑器预览图像。
    /// </summary>
    private void ApplyRawImageEditorPreview()
    {
        if (!_rawImage || !_targetMat || textureList.Count == 0) return;

        int index = Mathf.Clamp(_currentSegIndex, 0, textureList.Count - 1);
        Texture2D texture = textureList[index];

        _rawImage.texture = texture;
        _rawImage.material = _targetMat;
        _rawImage.canvasRenderer.materialCount = 1;
        _rawImage.canvasRenderer.SetMaterial(_targetMat, 0);
        _rawImage.canvasRenderer.SetTexture(texture);
        _rawImage.SetMaterialDirty();

        SceneView.RepaintAll();
    }
#endif
}
