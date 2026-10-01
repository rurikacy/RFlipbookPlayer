using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlipbookEditorTools
{
    internal readonly struct FlipbookFrameLocation
    {
        /// <summary>
        ///     从 1 开始的全局帧号。
        /// </summary>
        public readonly int GlobalFrame;

        /// <summary>
        ///     从 0 开始的图集分段索引。
        /// </summary>
        public readonly int SegmentIndex;

        /// <summary>
        ///     从 0 开始的分段内帧索引。
        /// </summary>
        public readonly int LocalFrame;

        /// <summary>
        ///     创建全局帧在图集中的位置记录。
        /// </summary>
        /// <param name="globalFrame">从 1 开始的全局帧号。</param>
        /// <param name="segmentIndex">从 0 开始的图集分段索引。</param>
        /// <param name="localFrame">从 0 开始的分段内帧索引。</param>
        public FlipbookFrameLocation(int globalFrame, int segmentIndex, int localFrame)
        {
            GlobalFrame = globalFrame;
            SegmentIndex = segmentIndex;
            LocalFrame = localFrame;
        }

        /// <summary>
        ///     获取当前数据是否有效。
        /// </summary>
        public bool IsValid => SegmentIndex >= 0 && LocalFrame >= 0;
    }

    internal sealed class FlipbookEditorData
    {
        /// <summary>
        ///     当前目标的序列化对象。
        /// </summary>
        public readonly SerializedObject SerializedObject;

        /// <summary>
        ///     图集列表的序列化属性。
        /// </summary>
        public readonly SerializedProperty Textures;

        /// <summary>
        ///     获取目标对象的帧识别模式序列化属性。
        /// </summary>
        public readonly SerializedProperty SourceMode;

        /// <summary>
        ///     分段帧数列表的序列化属性。
        /// </summary>
        public readonly SerializedProperty Frames;

        /// <summary>
        ///     获取目标对象的 Multiple 切片 UV 序列化属性。
        /// </summary>
        public readonly SerializedProperty MultipleFrameUvs;

        /// <summary>
        ///     网格行数的序列化属性。
        /// </summary>
        public readonly SerializedProperty Rows;

        /// <summary>
        ///     网格列数的序列化属性。
        /// </summary>
        public readonly SerializedProperty Columns;

        /// <summary>
        ///     播放帧率的序列化属性。
        /// </summary>
        public readonly SerializedProperty FrameRate;

        /// <summary>
        ///     上边缘像素内收值的序列化属性。
        /// </summary>
        public readonly SerializedProperty InsetTop;

        /// <summary>
        ///     下边缘像素内收值的序列化属性。
        /// </summary>
        public readonly SerializedProperty InsetBottom;

        /// <summary>
        ///     左边缘像素内收值的序列化属性。
        /// </summary>
        public readonly SerializedProperty InsetLeft;

        /// <summary>
        ///     右边缘像素内收值的序列化属性。
        /// </summary>
        public readonly SerializedProperty InsetRight;

        /// <summary>
        ///     循环播放开关的序列化属性。
        /// </summary>
        public readonly SerializedProperty Loop;

        /// <summary>
        ///     在 Start 时自动播放开关的序列化属性。
        /// </summary>
        public readonly SerializedProperty AutoPlayOnStart;

        /// <summary>
        ///     在 OnEnable 时自动播放开关的序列化属性。
        /// </summary>
        public readonly SerializedProperty AutoPlayOnEnable;

        /// <summary>
        ///     为播放器或 Clip 创建序列化编辑数据。
        /// </summary>
        /// <param name="target">要编辑或预览的播放器或 Clip。</param>
        public FlipbookEditorData(Object target)
        {
            if (target is not FlipbookPlayer && target is not FlipbookClip)
            {
                throw new ArgumentException("Flipbook editor data only supports FlipbookPlayer and FlipbookClip.", nameof(target));
            }

            SerializedObject = new SerializedObject(target);
            Textures = SerializedObject.FindProperty("textureList");
            SourceMode = SerializedObject.FindProperty("frameSourceMode");
            Frames = SerializedObject.FindProperty("frameList");
            MultipleFrameUvs = SerializedObject.FindProperty("multipleFrameUvList");
            Rows = SerializedObject.FindProperty("row");
            Columns = SerializedObject.FindProperty("column");
            FrameRate = SerializedObject.FindProperty("frameRate");
            InsetTop = SerializedObject.FindProperty("insetTop");
            InsetBottom = SerializedObject.FindProperty("insetBottom");
            InsetLeft = SerializedObject.FindProperty("insetLeft");
            InsetRight = SerializedObject.FindProperty("insetRight");
            Loop = SerializedObject.FindProperty("loop");
            AutoPlayOnStart = SerializedObject.FindProperty("autoPlayOnStart");
            AutoPlayOnEnable = SerializedObject.FindProperty("autoPlayOnEnable");
        }

        /// <summary>
        ///     获取当前编辑目标。
        /// </summary>
        public Object Target => SerializedObject.targetObject;

        /// <summary>
        ///     获取当前目标对应的播放器。
        /// </summary>
        public FlipbookPlayer Player => Target as FlipbookPlayer;

        /// <summary>
        ///     获取当前目标对应的 Clip。
        /// </summary>
        public FlipbookClip Clip => Target as FlipbookClip;

        /// <summary>
        ///     获取当前目标是否为播放器。
        /// </summary>
        public bool IsPlayer => Player;

        /// <summary>
        ///     获取当前数据是否有效。
        /// </summary>
        public bool IsValid => Target && Textures != null && SourceMode != null && Frames != null && MultipleFrameUvs != null
                               && InsetTop != null && InsetBottom != null && InsetLeft != null && InsetRight != null;

        /// <summary>
        ///     获取当前目标配置的帧识别方式。
        /// </summary>
        public FlipbookFrameSourceMode FrameSourceMode => (FlipbookFrameSourceMode)SourceMode.enumValueIndex;

        /// <summary>
        ///     获取当前目标是否使用 Multiple Sprite 切片识别模式。
        /// </summary>
        public bool IsMultiple => FrameSourceMode == FlipbookFrameSourceMode.Multiple;

        /// <summary>
        ///     获取图集分段数量。
        /// </summary>
        public int TextureCount => Textures?.arraySize ?? 0;

        /// <summary>
        ///     获取至少为 1 的网格行数。
        /// </summary>
        public int SafeRows => Mathf.Max(1, Rows?.intValue ?? 1);

        /// <summary>
        ///     获取至少为 1 的网格列数。
        /// </summary>
        public int SafeColumns => Mathf.Max(1, Columns?.intValue ?? 1);

        /// <summary>
        ///     获取单张网格图集的帧容量。
        /// </summary>
        public int GridFrameCount => SafeRows * SafeColumns;

        /// <summary>
        ///     获取至少为 1 的播放帧率。
        /// </summary>
        public int SafeFrameRate => Mathf.Max(1, FrameRate?.intValue ?? 1);

        /// <summary>
        ///     从目标对象刷新序列化编辑数据。
        /// </summary>
        public void Update()
        {
            if (SerializedObject.targetObject) SerializedObject.UpdateIfRequiredOrScript();
        }

        /// <summary>
        ///     提交序列化属性修改，并刷新播放器的分段时间。
        /// </summary>
        /// <returns>属性发生变化时返回 true，否则返回 false。</returns>
        public bool ApplyModifiedProperties()
        {
            if (!SerializedObject.targetObject) return false;

            bool changed = SerializedObject.ApplyModifiedProperties();
            if (changed && Player) Player.CalculateSegmentTime();
            return changed;
        }

        /// <summary>
        ///     获取指定分段的图集纹理。
        /// </summary>
        /// <param name="segmentIndex">从 0 开始的图集分段索引。</param>
        /// <returns>指定分段的图集纹理；索引无效时返回 null。</returns>
        public Texture2D GetTexture(int segmentIndex)
        {
            if (segmentIndex < 0 || segmentIndex >= TextureCount) return null;
            return Textures.GetArrayElementAtIndex(segmentIndex).objectReferenceValue as Texture2D;
        }

        /// <summary>
        ///     获取指定分段的有效帧数。
        /// </summary>
        /// <param name="segmentIndex">从 0 开始的图集分段索引。</param>
        /// <returns>指定分段的有效帧数。</returns>
        public int GetFrameCount(int segmentIndex)
        {
            if (segmentIndex < 0 || segmentIndex >= TextureCount) return 0;
            if (segmentIndex >= Frames.arraySize) return IsMultiple ? 0 : GridFrameCount;

            int frameCount = Frames.GetArrayElementAtIndex(segmentIndex).intValue;
            return IsMultiple ? Mathf.Max(0, frameCount) : Mathf.Clamp(frameCount, 1, GridFrameCount);
        }

        /// <summary>
        ///     获取指定分段和局部帧的归一化 UV。Grid 模式实时计算，Multiple 模式读取已同步数据。
        /// </summary>
        /// <param name="segmentIndex">从零开始的图集分段索引。</param>
        /// <param name="localFrame">从零开始的分段内帧索引。</param>
        /// <param name="frameUv">成功时返回以纹理左下角为原点的归一化 UV 矩形。</param>
        /// <returns>帧索引有效且存在可用 UV 时返回 true；否则返回 false。</returns>
        public bool TryGetFrameUv(int segmentIndex, int localFrame, out Rect frameUv)
        {
            frameUv = default;
            if (segmentIndex < 0 || localFrame < 0 || localFrame >= GetFrameCount(segmentIndex)) return false;

            if (!IsMultiple)
            {
                int column = localFrame % SafeColumns;
                int rowFromTop = localFrame / SafeColumns;
                float width = 1f / SafeColumns;
                float height = 1f / SafeRows;
                frameUv = new Rect(column * width, 1f - (rowFromTop + 1) * height, width, height);
                return true;
            }

            int frameIndex = localFrame;
            for (int i = 0; i < segmentIndex; i++) frameIndex += GetFrameCount(i);
            if (frameIndex < 0 || frameIndex >= MultipleFrameUvs.arraySize) return false;

            frameUv = MultipleFrameUvs.GetArrayElementAtIndex(frameIndex).rectValue;
            return frameUv is { width: > 0f, height: > 0f };
        }

        /// <summary>
        ///     获取所有分段的有效帧数。
        /// </summary>
        /// <returns>按分段顺序排列的有效帧数数组。</returns>
        public int[] GetFrameCounts()
        {
            int[] counts = new int[TextureCount];
            for (int i = 0; i < counts.Length; i++) counts[i] = GetFrameCount(i);
            return counts;
        }

        /// <summary>
        ///     获取所有分段的总帧数。
        /// </summary>
        /// <returns>所有图集分段的有效总帧数。</returns>
        public int GetTotalFrames()
        {
            int total = 0;
            for (int i = 0; i < TextureCount; i++) total += GetFrameCount(i);
            return total;
        }

        /// <summary>
        ///     获取按当前帧率计算的播放时长。
        /// </summary>
        /// <returns>按当前帧率计算的播放时长，单位为秒。</returns>
        public float GetDuration()
        {
            return GetTotalFrames() / (float)SafeFrameRate;
        }

        /// <summary>
        ///     获取指定分段从 1 开始的首帧号。
        /// </summary>
        /// <param name="segmentIndex">从 0 开始的图集分段索引。</param>
        /// <returns>指定分段从 1 开始的首帧号。</returns>
        public int GetSegmentStartFrame(int segmentIndex)
        {
            int startFrame = 1;
            for (int i = 0; i < segmentIndex && i < TextureCount; i++) startFrame += GetFrameCount(i);
            return startFrame;
        }

        /// <summary>
        ///     定位全局帧所在的分段及局部帧。
        /// </summary>
        /// <param name="globalFrame">从 1 开始的全局帧号。</param>
        /// <returns>全局帧对应的分段和局部帧位置。</returns>
        public FlipbookFrameLocation LocateFrame(int globalFrame)
        {
            int totalFrames = GetTotalFrames();
            if (totalFrames <= 0) return new FlipbookFrameLocation(0, -1, -1);

            int clampedFrame = Mathf.Clamp(globalFrame, 1, totalFrames);
            int remaining = clampedFrame;

            for (int i = 0; i < TextureCount; i++)
            {
                int frameCount = GetFrameCount(i);
                if (remaining <= frameCount)
                    return new FlipbookFrameLocation(clampedFrame, i, remaining - 1);

                remaining -= frameCount;
            }

            return new FlipbookFrameLocation(clampedFrame, TextureCount - 1, GetFrameCount(TextureCount - 1) - 1);
        }

        /// <summary>
        ///     将分段内帧号转换为全局帧号。
        /// </summary>
        /// <param name="segmentIndex">从 0 开始的图集分段索引。</param>
        /// <param name="localFrame">从 0 开始的分段内帧索引。</param>
        /// <returns>从 1 开始的全局帧号。</returns>
        public int ToGlobalFrame(int segmentIndex, int localFrame)
        {
            if (segmentIndex < 0 || segmentIndex >= TextureCount) return 0;
            int safeLocalFrame = Mathf.Clamp(localFrame, 0, GetFrameCount(segmentIndex) - 1);
            return GetSegmentStartFrame(segmentIndex) + safeLocalFrame;
        }

        /// <summary>
        ///     获取所有分段的总帧数。
        /// </summary>
        /// <param name="frameCounts">各图集分段的有效帧数。</param>
        /// <returns>所有图集分段的有效总帧数。</returns>
        public static int GetTotalFrames(IReadOnlyList<int> frameCounts)
        {
            int total = 0;
            for (int i = 0; i < frameCounts.Count; i++) total += Mathf.Max(0, frameCounts[i]);
            return total;
        }

        /// <summary>
        ///     定位全局帧所在的分段及局部帧。
        /// </summary>
        /// <param name="globalFrame">从 1 开始的全局帧号。</param>
        /// <param name="frameCounts">各图集分段的有效帧数。</param>
        /// <returns>全局帧对应的分段和局部帧位置。</returns>
        public static FlipbookFrameLocation LocateFrame(int globalFrame, IReadOnlyList<int> frameCounts)
        {
            int totalFrames = GetTotalFrames(frameCounts);
            if (totalFrames <= 0) return new FlipbookFrameLocation(0, -1, -1);

            int clampedFrame = Mathf.Clamp(globalFrame, 1, totalFrames);
            int remaining = clampedFrame;
            for (int i = 0; i < frameCounts.Count; i++)
            {
                int frameCount = Mathf.Max(0, frameCounts[i]);
                if (remaining <= frameCount)
                    return new FlipbookFrameLocation(clampedFrame, i, remaining - 1);

                remaining -= frameCount;
            }

            return new FlipbookFrameLocation(0, -1, -1);
        }

        /// <summary>
        ///     将分段内帧号转换为全局帧号。
        /// </summary>
        /// <param name="segmentIndex">从 0 开始的图集分段索引。</param>
        /// <param name="localFrame">从 0 开始的分段内帧索引。</param>
        /// <param name="frameCounts">各图集分段的有效帧数。</param>
        /// <returns>从 1 开始的全局帧号。</returns>
        public static int ToGlobalFrame(int segmentIndex, int localFrame, IReadOnlyList<int> frameCounts)
        {
            if (segmentIndex < 0 || segmentIndex >= frameCounts.Count) return 0;

            int globalFrame = 1 + Mathf.Clamp(localFrame, 0, Mathf.Max(0, frameCounts[segmentIndex] - 1));
            for (int i = 0; i < segmentIndex; i++) globalFrame += Mathf.Max(0, frameCounts[i]);
            return globalFrame;
        }
    }
}
