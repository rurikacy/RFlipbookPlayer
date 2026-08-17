using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[AddComponentMenu("Custom/Flipbook 序列帧播放器")]
public class FlipbookPlayer : MonoBehaviour
{
    private static readonly int _currentFrameId = Shader.PropertyToID("_CurrentFrame");
    private static readonly int _mainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int _rowId = Shader.PropertyToID("_Row");
    private static readonly int _colId = Shader.PropertyToID("_Col");
    private static readonly int _totalFrameId = Shader.PropertyToID("_TotalFrame");
    private static readonly int _frameModeId = Shader.PropertyToID("_FrameMode");
    private static readonly int _frameRectId = Shader.PropertyToID("_FrameRect");

    // ==========================================
    // 基础配置
    // ==========================================
    [Tooltip("序列帧图集列表，单图集模式只需放入一张即可")]
    public List<Texture2D> textureList = new();

    /// <summary>
    ///     获取当前 Flipbook 的帧识别方式；Grid 使用规则网格，Multiple 使用由 Editor 同步的 Sprite 切片信息。
    /// </summary>
    [Tooltip("帧识别方式：固定网格或 Multiple Sprite 切片")]
    public FlipbookFrameSourceMode frameSourceMode = FlipbookFrameSourceMode.Grid;

    [Tooltip("对应每张图集的实际总帧数，例如 144")]
    public List<int> frameList = new();

    /// <summary>
    ///     Multiple 模式下按分段和帧顺序保存的归一化 UV 矩形，坐标原点位于纹理左下角。
    /// </summary>
    [Tooltip("Multiple 模式下由同步切片生成的帧 UV 数据")]
    public List<Rect> multipleFrameUvList = new();

    [Tooltip("图集物理网格行数（Texture高度 / 单帧高度）")]
    public int row = 16;

    [Tooltip("图集物理网格列数（Texture宽度 / 单帧宽度）")]
    public int column = 16;

    // ==========================================
    // 播放设置
    // ==========================================
    [Tooltip("目标播放帧率（FPS）")]
    public int frameRate = 24;

    public bool loop = true;
    public bool autoPlayOnStart = true;
    public bool autoPlayOnEnable = false;
    private readonly List<float> _segDuration = new();
    private int _appliedFrame = -1;
    private int _currentSegIndex = -1;
    private bool _hasOriginalRawImageState;
    private bool _isInitialized;
    private bool _ownsTargetMaterial;
    private Material _originalRawImageMaterial;
    private Material _originalRendererMaterial;
    private MaterialPropertyBlock _originalPropertyBlock;
    private MaterialPropertyBlock _propertyBlock;
    private RawImage _rawImage;
    private Rect _originalRawImageUvRect;
    private Renderer _renderer;

    // ==========================================
    // 运行时私有变量
    // ==========================================
    private Material _targetMat;

    private float _totalTime;

    public int CurrentFrameNumber { get; private set; }
    public bool IsPlaying { get; private set; }
    public int PlaybackLoopCount { get; private set; }
    public int PlaybackSequenceId { get; private set; }
    public event Action<FlipbookPlayer> PlaybackCompleted;

    private bool HasRenderTarget => _rawImage || (_renderer && _targetMat);

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

        if (autoPlayOnStart && Application.isPlaying) Play();
    }

    private void Update()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && !IsPlaying) return;
#endif

        if (!IsPlaying || textureList.Count == 0 || _segDuration.Count == 0 || !HasRenderTarget) return;

        float fullDuration = _segDuration[^1];
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

    private void OnDestroy()
    {
#if UNITY_EDITOR
        CleanupTargetMaterial(!Application.isPlaying);
#else
        CleanupTargetMaterial(false);
#endif
    }

    private void OnValidate()
    {
        row = Mathf.Max(1, row);
        column = Mathf.Max(1, column);
        frameRate = Mathf.Max(1, frameRate);

        textureList ??= new List<Texture2D>();
        frameList ??= new List<int>();
        multipleFrameUvList ??= new List<Rect>();

        int gridFrames = row * column;

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

    private void InitPlayer()
    {
        if (_isInitialized) return;

        _rawImage = GetComponent<RawImage>();
        if (_rawImage)
        {
            _originalRawImageMaterial = _rawImage.material;
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
                // Runtime 下直接更新 UV，保留共享 UI 材质和 Canvas batching。
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

    private void CleanupTargetMaterial(bool immediate)
    {
        if (_rawImage && _hasOriginalRawImageState)
        {
            _rawImage.uvRect = _originalRawImageUvRect;
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
        _originalPropertyBlock = null;
        _propertyBlock = null;
        _hasOriginalRawImageState = false;
        _ownsTargetMaterial = false;
        _appliedFrame = -1;
        _currentSegIndex = -1;
    }

    /// <summary>
    ///     计算各段图集的累计结束时间点
    /// </summary>
    public void CalculateSegmentTime()
    {
        _segDuration.Clear();
        _appliedFrame = -1;

        float accumulatedTime = 0f;
        for (int i = 0; i < textureList.Count; i++)
        {
            int frames = GetSafeFrameCount(i);
            accumulatedTime += frames / (float)frameRate;
            _segDuration.Add(accumulatedTime);
        }
    }

    private int GetSafeFrameCount(int index)
    {
        if (frameSourceMode == FlipbookFrameSourceMode.Multiple)
        {
            if (index >= 0 && index < frameList.Count) return Mathf.Max(0, frameList[index]);
            return 0;
        }

        int gridFrames = Mathf.Max(1, row * column);

        if (index >= 0 && index < frameList.Count) return Mathf.Clamp(frameList[index], 1, gridFrames);

        return gridFrames;
    }

    private void UpdateAnimationState(float timePosition)
    {
        if (_segDuration.Count == 0) return;

        int targetIndex = -1;
        for (int i = 0; i < _segDuration.Count; i++)
            if (GetSafeFrameCount(i) > 0 && timePosition < _segDuration[i])
            {
                targetIndex = i;
                break;
            }

        if (targetIndex < 0)
        {
            for (int i = _segDuration.Count - 1; i >= 0; i--)
                if (GetSafeFrameCount(i) > 0)
                {
                    targetIndex = i;
                    break;
                }
        }

        if (targetIndex < 0) return;

        if (targetIndex != _currentSegIndex) SwitchSegment(targetIndex);

        float prevSegDuration = targetIndex > 0 ? _segDuration[targetIndex - 1] : 0f;
        float localTime = Mathf.Max(0f, timePosition - prevSegDuration);

        int totalFrame = GetSafeFrameCount(targetIndex);
        int localFrame = Mathf.Min(Mathf.FloorToInt(localTime * frameRate), totalFrame - 1);

        int globalFrame = localFrame + 1;
        for (int i = 0; i < targetIndex; i++) globalFrame += GetSafeFrameCount(i);
        CurrentFrameNumber = globalFrame;

        SetCurrentFrame(localFrame);
    }

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

    private static Vector4 ToVector4(Rect rect)
    {
        return new Vector4(rect.x, rect.y, rect.width, rect.height);
    }

    private Rect GetCurrentFrameRect(int frame)
    {
        if (frameSourceMode == FlipbookFrameSourceMode.Multiple && TryGetMultipleFrameUv(_currentSegIndex, frame, out Rect multipleRect))
            return multipleRect;

        int safeRow = Mathf.Max(1, row);
        int safeColumn = Mathf.Max(1, column);
        int colIndex = frame % safeColumn;
        int rowIndex = safeRow - 1 - frame / safeColumn;
        return new Rect(
            colIndex / (float)safeColumn,
            rowIndex / (float)safeRow,
            1f / safeColumn,
            1f / safeRow);
    }

    private bool TryGetMultipleFrameUv(int segmentIndex, int localFrame, out Rect frameUv)
    {
        frameUv = default;
        if (multipleFrameUvList == null || segmentIndex < 0 || localFrame < 0 || localFrame >= GetSafeFrameCount(segmentIndex))
            return false;

        int frameIndex = localFrame;
        for (int i = 0; i < segmentIndex; i++) frameIndex += GetSafeFrameCount(i);
        if (frameIndex < 0 || frameIndex >= multipleFrameUvList.Count) return false;

        frameUv = multipleFrameUvList[frameIndex];
        return frameUv.width > 0f && frameUv.height > 0f;
    }

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

    private Material CreateFlipbookMaterial(Material source)
    {
        Shader shader = Shader.Find("Custom/Flipbook_Standard");
        if (!shader) shader = Shader.Find("Custom/Flipbook_Standard_Builtin");

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

        return source ? new Material(source) : null;
    }

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

    #region 外部控制接口

    /// <summary>
    ///     获取所有图集的总帧数
    /// </summary>
    public int GetTotalFrames()
    {
        int total = 0;
        for (int i = 0; i < textureList.Count; i++) total += GetSafeFrameCount(i);

        return total;
    }

    /// <summary>
    ///     在 Edit Mode 下预览指定帧（1-based 全局帧号）
    /// </summary>
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
    ///     在 Edit Mode 下刷新显示第一帧预览
    /// </summary>
    public void RefreshPreview()
    {
        InitPlayer();

        if (textureList.Count == 0 || !HasRenderTarget) return;

        CalculateSegmentTime();

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

    private int FindFirstSegmentWithFrames()
    {
        for (int i = 0; i < textureList.Count; i++)
            if (GetSafeFrameCount(i) > 0)
                return i;

        return -1;
    }

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

        PlaybackSequenceId++;
        PlaybackLoopCount = 0;
        IsPlaying = true;
        _totalTime = 0f;
        _currentSegIndex = -1;

        UpdateAnimationState(0f);
    }

    public void Pause()
    {
        IsPlaying = false;
    }

    public void Resume()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            RefreshPreview();
            return;
        }
#endif

        if (textureList.Count > 0 && HasRenderTarget) IsPlaying = true;
    }

    public void Stop()
    {
        IsPlaying = false;
        PlaybackLoopCount = 0;
        _totalTime = 0f;
        _currentSegIndex = -1;

        if (_isInitialized && HasRenderTarget && textureList.Count > 0) UpdateAnimationState(0f);

#if UNITY_EDITOR
        if (!Application.isPlaying) ApplyRawImageEditorPreview();
#endif
    }

#if UNITY_EDITOR
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

    #endregion
}

// =========================================================================
// 编辑器自定义面板逻辑
// =========================================================================