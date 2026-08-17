using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlipbookEditorTools
{
    internal readonly struct FlipbookFrameLocation
    {
        public readonly int GlobalFrame;
        public readonly int SegmentIndex;
        public readonly int LocalFrame;

        public FlipbookFrameLocation(int globalFrame, int segmentIndex, int localFrame)
        {
            GlobalFrame = globalFrame;
            SegmentIndex = segmentIndex;
            LocalFrame = localFrame;
        }

        public bool IsValid => SegmentIndex >= 0 && LocalFrame >= 0;
    }

    internal sealed class FlipbookEditorData
    {
        public readonly SerializedObject SerializedObject;
        public readonly SerializedProperty Textures;

        /// <summary>
        ///     获取目标对象的帧识别模式序列化属性。
        /// </summary>
        public readonly SerializedProperty SourceMode;
        public readonly SerializedProperty Frames;

        /// <summary>
        ///     获取目标对象的 Multiple 切片 UV 序列化属性。
        /// </summary>
        public readonly SerializedProperty MultipleFrameUvs;
        public readonly SerializedProperty Rows;
        public readonly SerializedProperty Columns;
        public readonly SerializedProperty FrameRate;
        public readonly SerializedProperty Loop;
        public readonly SerializedProperty AutoPlayOnStart;
        public readonly SerializedProperty AutoPlayOnEnable;

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
            Loop = SerializedObject.FindProperty("loop");
            AutoPlayOnStart = SerializedObject.FindProperty("autoPlayOnStart");
            AutoPlayOnEnable = SerializedObject.FindProperty("autoPlayOnEnable");
        }

        public Object Target => SerializedObject.targetObject;
        public FlipbookPlayer Player => Target as FlipbookPlayer;
        public FlipbookClip Clip => Target as FlipbookClip;
        public bool IsPlayer => Player;
        public bool IsValid => Target && Textures != null && SourceMode != null && Frames != null && MultipleFrameUvs != null;

        /// <summary>
        ///     获取当前目标配置的帧识别方式。
        /// </summary>
        public FlipbookFrameSourceMode FrameSourceMode => (FlipbookFrameSourceMode)SourceMode.enumValueIndex;

        /// <summary>
        ///     获取当前目标是否使用 Multiple Sprite 切片识别模式。
        /// </summary>
        public bool IsMultiple => FrameSourceMode == FlipbookFrameSourceMode.Multiple;
        public int TextureCount => Textures?.arraySize ?? 0;
        public int SafeRows => Mathf.Max(1, Rows?.intValue ?? 1);
        public int SafeColumns => Mathf.Max(1, Columns?.intValue ?? 1);
        public int GridFrameCount => SafeRows * SafeColumns;
        public int SafeFrameRate => Mathf.Max(1, FrameRate?.intValue ?? 1);

        public void Update()
        {
            if (SerializedObject.targetObject) SerializedObject.UpdateIfRequiredOrScript();
        }

        public bool ApplyModifiedProperties()
        {
            if (!SerializedObject.targetObject) return false;

            bool changed = SerializedObject.ApplyModifiedProperties();
            if (changed && Player) Player.CalculateSegmentTime();
            return changed;
        }

        public Texture2D GetTexture(int segmentIndex)
        {
            if (segmentIndex < 0 || segmentIndex >= TextureCount) return null;
            return Textures.GetArrayElementAtIndex(segmentIndex).objectReferenceValue as Texture2D;
        }

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
            return frameUv.width > 0f && frameUv.height > 0f;
        }

        public int[] GetFrameCounts()
        {
            int[] counts = new int[TextureCount];
            for (int i = 0; i < counts.Length; i++) counts[i] = GetFrameCount(i);
            return counts;
        }

        public int GetTotalFrames()
        {
            int total = 0;
            for (int i = 0; i < TextureCount; i++) total += GetFrameCount(i);
            return total;
        }

        public float GetDuration()
        {
            return GetTotalFrames() / (float)SafeFrameRate;
        }

        public int GetSegmentStartFrame(int segmentIndex)
        {
            int startFrame = 1;
            for (int i = 0; i < segmentIndex && i < TextureCount; i++) startFrame += GetFrameCount(i);
            return startFrame;
        }

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

        public int ToGlobalFrame(int segmentIndex, int localFrame)
        {
            if (segmentIndex < 0 || segmentIndex >= TextureCount) return 0;
            int safeLocalFrame = Mathf.Clamp(localFrame, 0, GetFrameCount(segmentIndex) - 1);
            return GetSegmentStartFrame(segmentIndex) + safeLocalFrame;
        }

        public static int GetTotalFrames(IReadOnlyList<int> frameCounts)
        {
            int total = 0;
            for (int i = 0; i < frameCounts.Count; i++) total += Mathf.Max(0, frameCounts[i]);
            return total;
        }

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

        public static int ToGlobalFrame(int segmentIndex, int localFrame, IReadOnlyList<int> frameCounts)
        {
            if (segmentIndex < 0 || segmentIndex >= frameCounts.Count) return 0;

            int globalFrame = 1 + Mathf.Clamp(localFrame, 0, Mathf.Max(0, frameCounts[segmentIndex] - 1));
            for (int i = 0; i < segmentIndex; i++) globalFrame += Mathf.Max(0, frameCounts[i]);
            return globalFrame;
        }
    }
}