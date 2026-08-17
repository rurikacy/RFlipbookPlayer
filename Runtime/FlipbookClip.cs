using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>保存可复用的 Flipbook 图集、帧布局与播放速率配置。</summary>
[CreateAssetMenu(fileName = "FlipbookClip", menuName = "Custom/Flipbook/Flipbook Clip")]
public class FlipbookClip : ScriptableObject
{
    /// <summary>
    ///     获取 Flipbook 使用的图集列表。
    /// </summary>
    [Tooltip("按播放顺序排列的序列帧图集")]
    public List<Texture2D> textureList = new();

    /// <summary>
    ///     获取帧识别方式。Multiple 模式下帧数由同步的 Sprite 切片自动决定。
    /// </summary>
    [Tooltip("帧识别方式：固定网格或 Multiple Sprite 切片")]
    public FlipbookFrameSourceMode frameSourceMode = FlipbookFrameSourceMode.Grid;

    /// <summary>
    ///     获取每个图集分段的帧数。Multiple 模式下由同步切片自动生成。
    /// </summary>
    [Tooltip("每张图集实际使用的帧数")]
    public List<int> frameList = new();

    /// <summary>
    ///     获取 Multiple 模式下按分段和帧顺序保存的归一化 UV 矩形。
    /// </summary>
    [Tooltip("Multiple 模式下由同步切片生成的帧 UV 数据")]
    public List<Rect> multipleFrameUvList = new();

    /// <summary>每张图集的物理网格行数。</summary>
    [Tooltip("每张图集的物理网格行数")]
    [Min(1)]
    public int row = 16;

    /// <summary>每张图集的物理网格列数。</summary>
    [Tooltip("每张图集的物理网格列数")]
    [Min(1)]
    public int column = 16;

    /// <summary>该 Clip 的播放帧率。</summary>
    [Tooltip("每秒播放帧数")]
    [Min(1)]
    public int frameRate = 24;

    /// <summary>获取单张规则网格图集可容纳的最大帧数。</summary>
    public int GridFrameCount
    {
        get
        {
            long capacity = (long)Mathf.Max(1, row) * Mathf.Max(1, column);
            return (int)Math.Min(int.MaxValue, capacity);
        }
    }

    private void OnValidate()
    {
        row = Mathf.Max(1, row);
        column = Mathf.Max(1, column);
        frameRate = Mathf.Max(1, frameRate);

        textureList ??= new List<Texture2D>();
        frameList ??= new List<int>();
        multipleFrameUvList ??= new List<Rect>();

        int gridFrames = GridFrameCount;
        while (frameList.Count < textureList.Count)
            frameList.Add(frameSourceMode == FlipbookFrameSourceMode.Grid ? gridFrames : 0);
        while (frameList.Count > textureList.Count) frameList.RemoveAt(frameList.Count - 1);

        for (int i = 0; i < frameList.Count; i++)
            frameList[i] = frameSourceMode == FlipbookFrameSourceMode.Grid
                ? Mathf.Clamp(frameList[i], 1, gridFrames)
                : Mathf.Max(0, frameList[i]);
    }

    /// <summary>获取指定图集分段经过模式约束后的有效帧数。</summary>
    /// <param name="index">从零开始的图集分段索引。</param>
    public int GetSafeFrameCount(int index)
    {
        if (frameSourceMode == FlipbookFrameSourceMode.Multiple)
        {
            if (frameList != null && index >= 0 && index < frameList.Count)
                return Mathf.Max(0, frameList[index]);

            return 0;
        }

        int gridFrames = GridFrameCount;
        if (frameList != null && index >= 0 && index < frameList.Count)
            return Mathf.Clamp(frameList[index], 1, gridFrames);

        return gridFrames;
    }
}
