using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "FlipbookClip", menuName = "Custom/Flipbook/Flipbook Clip")]
public class FlipbookClip : ScriptableObject
{
    /// <summary>
    ///     获取 Flipbook 使用的图集列表。
    /// </summary>
    [Tooltip("Flipbook texture atlases in playback order.")]
    public List<Texture2D> textureList = new();

    /// <summary>
    ///     获取帧识别方式。Multiple 模式下帧数由同步的 Sprite 切片自动决定。
    /// </summary>
    [Tooltip("帧识别方式：固定网格或 Multiple Sprite 切片")]
    public FlipbookFrameSourceMode frameSourceMode = FlipbookFrameSourceMode.Grid;

    /// <summary>
    ///     获取每个图集分段的帧数。Multiple 模式下由同步切片自动生成。
    /// </summary>
    [Tooltip("Actual frame count for each texture atlas.")]
    public List<int> frameList = new();

    /// <summary>
    ///     获取 Multiple 模式下按分段和帧顺序保存的归一化 UV 矩形。
    /// </summary>
    [Tooltip("Multiple 模式下由同步切片生成的帧 UV 数据")]
    public List<Rect> multipleFrameUvList = new();

    [Tooltip("Physical row count in each texture atlas.")]
    [Min(1)]
    public int row = 16;

    [Tooltip("Physical column count in each texture atlas.")]
    [Min(1)]
    public int column = 16;

    [Tooltip("Playback frame rate for this clip.")]
    [Min(1)]
    public int frameRate = 24;

    public int GridFrameCount => Mathf.Max(1, row) * Mathf.Max(1, column);

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