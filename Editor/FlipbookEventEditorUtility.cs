using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlipbookEditorTools
{
    internal static class FlipbookEventEditorUtility
    {
        private readonly struct EventAnchor
        {
            public readonly int ArrayIndex;
            public readonly int SegmentIndex;
            public readonly int LocalFrame;

            public EventAnchor(int arrayIndex, int segmentIndex, int localFrame)
            {
                ArrayIndex = arrayIndex;
                SegmentIndex = segmentIndex;
                LocalFrame = localFrame;
            }
        }

        public static FlipbookPlayerEventProxy GetProxy(FlipbookEditorData data)
        {
            return data?.Player ? data.Player.GetComponent<FlipbookPlayerEventProxy>() : null;
        }

        public static FlipbookPlayerEventProxy AddProxy(FlipbookPlayer player)
        {
            if (!player) return null;
            FlipbookPlayerEventProxy existing = player.GetComponent<FlipbookPlayerEventProxy>();
            if (existing) return existing;

            FlipbookPlayerEventProxy proxy = Undo.AddComponent<FlipbookPlayerEventProxy>(player.gameObject);
            SerializedObject serializedProxy = new(proxy);
            serializedProxy.FindProperty("player").objectReferenceValue = player;
            serializedProxy.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(proxy);
            return proxy;
        }

        public static HashSet<int> GetEventFrames(FlipbookPlayerEventProxy proxy)
        {
            HashSet<int> eventFrames = new();
            if (!proxy) return eventFrames;

            SerializedObject serializedProxy = new(proxy);
            SerializedProperty events = serializedProxy.FindProperty("frameEvents");
            for (int i = 0; i < events.arraySize; i++)
                eventFrames.Add(events.GetArrayElementAtIndex(i).FindPropertyRelative("frameNumber").intValue);

            return eventFrames;
        }

        public static int FindEventIndex(SerializedProperty events, int globalFrame)
        {
            if (events == null) return -1;
            for (int i = 0; i < events.arraySize; i++)
                if (events.GetArrayElementAtIndex(i).FindPropertyRelative("frameNumber").intValue == globalFrame)
                    return i;

            return -1;
        }

        public static bool ToggleEvent(FlipbookPlayerEventProxy proxy, int globalFrame)
        {
            if (!proxy || globalFrame <= 0) return false;

            SerializedObject serializedProxy = new(proxy);
            serializedProxy.Update();
            SerializedProperty events = serializedProxy.FindProperty("frameEvents");
            int existingIndex = FindEventIndex(events, globalFrame);

            if (existingIndex >= 0)
            {
                SerializedProperty existingEvent = events.GetArrayElementAtIndex(existingIndex);
                if (GetPersistentCallCount(existingEvent.FindPropertyRelative("onReached")) > 0 &&
                    !EditorUtility.DisplayDialog(
                        "删除帧事件",
                        $"第 {globalFrame} 帧包含持久化监听器。确定删除该事件吗？",
                        "删除",
                        "取消"))
                {
                    return false;
                }

                Undo.RecordObject(proxy, "删除 Flipbook 帧事件");
                DeleteArrayElement(events, existingIndex);
            }
            else
            {
                Undo.RecordObject(proxy, "添加 Flipbook 帧事件");
                int insertIndex = events.arraySize;
                for (int i = 0; i < events.arraySize; i++)
                    if (events.GetArrayElementAtIndex(i).FindPropertyRelative("frameNumber").intValue > globalFrame)
                    {
                        insertIndex = i;
                        break;
                    }

                if (insertIndex >= events.arraySize)
                    events.arraySize++;
                else
                    events.InsertArrayElementAtIndex(insertIndex);
                SerializedProperty newEvent = events.GetArrayElementAtIndex(insertIndex);
                newEvent.FindPropertyRelative("frameNumber").intValue = globalFrame;
                ClearUnityEvent(newEvent.FindPropertyRelative("onReached"));
            }

            bool changed = serializedProxy.ApplyModifiedProperties();
            if (changed) PrefabUtility.RecordPrefabInstancePropertyModifications(proxy);
            return changed;
        }

        public static bool ChangeEventFrame(FlipbookPlayerEventProxy proxy, int arrayIndex, int newFrame, int totalFrames)
        {
            if (!proxy) return false;

            SerializedObject serializedProxy = new(proxy);
            serializedProxy.Update();
            SerializedProperty events = serializedProxy.FindProperty("frameEvents");
            if (arrayIndex < 0 || arrayIndex >= events.arraySize) return false;

            int clampedFrame = Mathf.Clamp(newFrame, 1, Mathf.Max(1, totalFrames));
            int duplicateIndex = FindEventIndex(events, clampedFrame);
            if (duplicateIndex >= 0 && duplicateIndex != arrayIndex)
            {
                EditorUtility.DisplayDialog("重复帧事件", $"第 {clampedFrame} 帧已经存在事件。", "确定");
                return false;
            }

            Undo.RecordObject(proxy, "修改 Flipbook 帧事件");
            events.GetArrayElementAtIndex(arrayIndex).FindPropertyRelative("frameNumber").intValue = clampedFrame;
            SortEvents(events);
            bool changed = serializedProxy.ApplyModifiedProperties();
            if (changed) PrefabUtility.RecordPrefabInstancePropertyModifications(proxy);
            return changed;
        }

        public static bool RemoveEventAtIndex(FlipbookPlayerEventProxy proxy, int arrayIndex)
        {
            if (!proxy) return false;

            SerializedObject serializedProxy = new(proxy);
            serializedProxy.Update();
            SerializedProperty events = serializedProxy.FindProperty("frameEvents");
            if (arrayIndex < 0 || arrayIndex >= events.arraySize) return false;

            SerializedProperty eventProperty = events.GetArrayElementAtIndex(arrayIndex);
            int frame = eventProperty.FindPropertyRelative("frameNumber").intValue;
            if (GetPersistentCallCount(eventProperty.FindPropertyRelative("onReached")) > 0 &&
                !EditorUtility.DisplayDialog(
                    "删除帧事件",
                    $"第 {frame} 帧包含持久化监听器。确定删除该事件吗？",
                    "删除",
                    "取消"))
            {
                return false;
            }

            Undo.RecordObject(proxy, "删除 Flipbook 帧事件");
            DeleteArrayElement(events, arrayIndex);
            bool changed = serializedProxy.ApplyModifiedProperties();
            if (changed) PrefabUtility.RecordPrefabInstancePropertyModifications(proxy);
            return changed;
        }

        /// <summary>
        ///     切换 Flipbook 帧识别方式，并根据新模式更新帧数、切片 UV 和已有帧事件映射。
        /// </summary>
        /// <param name="data">要修改的 Flipbook 序列化数据。</param>
        /// <param name="sourceMode">目标帧识别方式。</param>
        /// <returns>模式和关联数据成功发生变化时返回 true；验证失败或没有变化时返回 false。</returns>
        public static bool SetSourceMode(FlipbookEditorData data, FlipbookFrameSourceMode sourceMode)
        {
            data.Update();
            if (data.FrameSourceMode == sourceMode) return false;

            if (sourceMode == FlipbookFrameSourceMode.Multiple && !ValidateMultipleTextures(data))
                return false;

            FlipbookPlayerEventProxy proxy = GetProxy(data);
            int[] oldCounts = data.GetFrameCounts();
            List<EventAnchor> anchors = CaptureEventAnchors(proxy, oldCounts);
            int[] newCounts;
            List<Rect> multipleFrameUvs = null;
            if (sourceMode == FlipbookFrameSourceMode.Multiple)
            {
                ReadMultipleSliceData(data, out newCounts, out multipleFrameUvs);
            }
            else
            {
                newCounts = new int[data.TextureCount];
                for (int i = 0; i < newCounts.Length; i++)
                {
                    int oldCount = i < oldCounts.Length ? oldCounts[i] : data.GridFrameCount;
                    newCounts[i] = Mathf.Clamp(oldCount, 1, data.GridFrameCount);
                }
            }

            int removedEventCount = CountRemovedEvents(anchors, newCounts);
            if (removedEventCount > 0 &&
                !EditorUtility.DisplayDialog(
                    sourceMode == FlipbookFrameSourceMode.Multiple ? "切换到 Multiple 模式" : "切换到 Grid 模式",
                    sourceMode == FlipbookFrameSourceMode.Multiple
                        ? $"切片数量会移除 {removedEventCount} 个超出帧范围的事件。"
                        : $"网格容量会移除 {removedEventCount} 个超出帧范围的事件。",
                    "切换并删除事件",
                    "取消"))
            {
                return false;
            }

            List<Object> undoTargets = new() { data.Target };
            if (proxy) undoTargets.Add(proxy);
            Undo.RecordObjects(undoTargets.ToArray(), "修改 Flipbook 帧识别模式");

            data.SourceMode.enumValueIndex = (int)sourceMode;
            if (sourceMode == FlipbookFrameSourceMode.Multiple)
            {
                SetMultipleFrameData(data, newCounts, multipleFrameUvs);
            }
            else
            {
                EnsureFrameArraySize(data);
                for (int i = 0; i < newCounts.Length; i++)
                    data.Frames.GetArrayElementAtIndex(i).intValue = newCounts[i];

                data.MultipleFrameUvs.arraySize = 0;
            }

            RemapEvents(proxy, anchors, newCounts, oldSegment => oldSegment, anchor =>
                anchor.SegmentIndex < newCounts.Length && anchor.LocalFrame < newCounts[anchor.SegmentIndex]);
            bool changed = data.ApplyModifiedProperties();
            if (changed) PrefabUtility.RecordPrefabInstancePropertyModifications(data.Target);
            return changed;
        }

        /// <summary>
        ///     替换指定图集分段的 Texture；Multiple 模式下会验证导入设置并自动同步切片。
        /// </summary>
        /// <param name="data">要修改的 Flipbook 序列化数据。</param>
        /// <param name="segmentIndex">从零开始的图集分段索引。</param>
        /// <param name="texture">新的 Texture；传入 null 表示清空当前分段。</param>
        /// <returns>Texture 或同步数据成功发生变化时返回 true；验证失败或没有变化时返回 false。</returns>
        public static bool SetTexture(FlipbookEditorData data, int segmentIndex, Texture2D texture)
        {
            data.Update();
            if (segmentIndex < 0 || segmentIndex >= data.TextureCount) return false;
            if (data.IsMultiple && texture && !IsMultipleTexture(texture, out _))
            {
                EditorUtility.DisplayDialog(
                    "无法使用该贴图",
                    $"{texture.name} 不是 Sprite Mode = Multiple，不能用于当前 Flipbook 的 Multiple 模式。",
                    "确定");
                return false;
            }

            SerializedProperty textureProperty = data.Textures.GetArrayElementAtIndex(segmentIndex);
            if (textureProperty.objectReferenceValue == texture) return false;

            FlipbookPlayerEventProxy proxy = GetProxy(data);
            int[] oldCounts = data.GetFrameCounts();
            List<EventAnchor> anchors = CaptureEventAnchors(proxy, oldCounts);
            List<Object> undoTargets = new() { data.Target };
            if (proxy) undoTargets.Add(proxy);
            Undo.RecordObjects(undoTargets.ToArray(), "修改 Flipbook 图集");

            textureProperty.objectReferenceValue = texture;
            int[] newCounts = null;
            List<Rect> frameUvs = null;
            if (data.IsMultiple)
            {
                ReadMultipleSliceData(data, out newCounts, out frameUvs);
                int removedEventCount = CountRemovedEvents(anchors, newCounts);
                if (removedEventCount > 0 &&
                    !EditorUtility.DisplayDialog(
                        "替换 Multiple 图集",
                        $"新切片数量会移除 {removedEventCount} 个超出帧范围的事件。",
                        "替换并删除事件",
                        "取消"))
                {
                    data.SerializedObject.Update();
                    return false;
                }
            }

            if (data.IsMultiple)
            {
                SetMultipleFrameData(data, newCounts, frameUvs);
                RemapEvents(proxy, anchors, newCounts, oldSegment => oldSegment, anchor =>
                    anchor.SegmentIndex < newCounts.Length && anchor.LocalFrame < newCounts[anchor.SegmentIndex]);
            }

            bool changed = data.ApplyModifiedProperties();
            if (changed) PrefabUtility.RecordPrefabInstancePropertyModifications(data.Target);
            return changed;
        }

        /// <summary>
        ///     重新读取所有图集的 Multiple Sprite 切片，生成帧数和归一化 UV，并重映射帧事件。
        /// </summary>
        /// <param name="data">要同步的 Flipbook 序列化数据。</param>
        /// <returns>同步数据成功写入时返回 true；模式或纹理验证失败、用户取消或数据未变化时返回 false。</returns>
        public static bool SyncMultipleSlices(FlipbookEditorData data)
        {
            data.Update();
            if (!data.IsMultiple || !ValidateMultipleTextures(data)) return false;

            int[] oldCounts = data.GetFrameCounts();
            FlipbookPlayerEventProxy proxy = GetProxy(data);
            List<EventAnchor> anchors = CaptureEventAnchors(proxy, oldCounts);
            ReadMultipleSliceData(data, out int[] newCounts, out List<Rect> frameUvs);

            int removedEventCount = CountRemovedEvents(anchors, newCounts);
            if (removedEventCount > 0 &&
                !EditorUtility.DisplayDialog(
                    "同步 Multiple 切片",
                    $"切片数量变化会移除 {removedEventCount} 个超出新帧范围的事件。",
                    "同步并删除事件",
                    "取消"))
            {
                return false;
            }

            List<Object> undoTargets = new() { data.Target };
            if (proxy) undoTargets.Add(proxy);
            Undo.RecordObjects(undoTargets.ToArray(), "同步 Flipbook Multiple 切片");

            SetMultipleFrameData(data, newCounts, frameUvs);
            RemapEvents(proxy, anchors, newCounts, oldSegment => oldSegment, anchor =>
                anchor.SegmentIndex < newCounts.Length && anchor.LocalFrame < newCounts[anchor.SegmentIndex]);
            bool changed = data.ApplyModifiedProperties();
            if (changed) PrefabUtility.RecordPrefabInstancePropertyModifications(data.Target);
            return changed;
        }

        /// <summary>
        ///     检查 Texture 是否以 Sprite/Multiple 模式导入且至少包含一个有效切片。
        /// </summary>
        /// <param name="texture">要检查的 Texture。</param>
        /// <param name="sliceCount">验证成功时返回导入器记录的切片数量；失败时返回零。</param>
        /// <returns>Texture 是包含有效切片的 Multiple Sprite 时返回 true；否则返回 false。</returns>
        public static bool IsMultipleTexture(Texture2D texture, out int sliceCount)
        {
            sliceCount = 0;
            if (!texture) return false;

            string path = AssetDatabase.GetAssetPath(texture);
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer ||
                importer.textureType != TextureImporterType.Sprite ||
                importer.spriteImportMode != SpriteImportMode.Multiple)
            {
                return false;
            }

            SpriteMetaData[] sprites = importer.spritesheet;
            sliceCount = sprites?.Length ?? 0;
            return sliceCount > 0;
        }

        public static bool MoveSegment(FlipbookEditorData data, int fromIndex, int toIndex)
        {
            data.Update();
            if (fromIndex < 0 || fromIndex >= data.TextureCount || toIndex < 0 || toIndex >= data.TextureCount || fromIndex == toIndex)
                return false;

            FlipbookPlayerEventProxy proxy = GetProxy(data);
            int[] oldCounts = data.GetFrameCounts();
            List<EventAnchor> anchors = CaptureEventAnchors(proxy, oldCounts);

            List<Object> undoTargets = new() { data.Target };
            if (proxy) undoTargets.Add(proxy);
            Undo.RecordObjects(undoTargets.ToArray(), "重排 Flipbook 图集");

            EnsureFrameArraySize(data);
            data.Textures.MoveArrayElement(fromIndex, toIndex);
            int[] newCounts;
            if (data.IsMultiple)
            {
                ReadMultipleSliceData(data, out newCounts, out List<Rect> frameUvs);
                SetMultipleFrameData(data, newCounts, frameUvs);
            }
            else
            {
                data.Frames.MoveArrayElement(fromIndex, toIndex);
                newCounts = (int[])oldCounts.Clone();
                int movedCount = newCounts[fromIndex];
                if (fromIndex < toIndex)
                    Array.Copy(newCounts, fromIndex + 1, newCounts, fromIndex, toIndex - fromIndex);
                else
                    Array.Copy(newCounts, toIndex, newCounts, toIndex + 1, fromIndex - toIndex);
                newCounts[toIndex] = movedCount;
            }

            RemapEvents(proxy, anchors, newCounts, oldSegment => MapMovedSegment(oldSegment, fromIndex, toIndex));
            bool changed = data.ApplyModifiedProperties();
            if (changed) PrefabUtility.RecordPrefabInstancePropertyModifications(data.Target);
            return changed;
        }

        public static bool RemoveSegment(FlipbookEditorData data, int segmentIndex)
        {
            data.Update();
            if (segmentIndex < 0 || segmentIndex >= data.TextureCount) return false;

            FlipbookPlayerEventProxy proxy = GetProxy(data);
            int[] oldCounts = data.GetFrameCounts();
            List<EventAnchor> anchors = CaptureEventAnchors(proxy, oldCounts);
            int removedEventCount = anchors.FindAll(anchor => anchor.SegmentIndex == segmentIndex).Count;
            if (removedEventCount > 0 &&
                !EditorUtility.DisplayDialog(
                    "删除图集分段",
                    $"该分段包含 {removedEventCount} 个帧事件。删除分段将同时删除这些事件，其他事件会自动重映射。",
                    "删除分段",
                    "取消"))
            {
                return false;
            }

            List<Object> undoTargets = new() { data.Target };
            if (proxy) undoTargets.Add(proxy);
            Undo.RecordObjects(undoTargets.ToArray(), "删除 Flipbook 图集");

            EnsureFrameArraySize(data);
            DeleteArrayElement(data.Textures, segmentIndex);
            DeleteArrayElement(data.Frames, segmentIndex);

            IReadOnlyList<int> newCounts;
            if (data.IsMultiple)
            {
                ReadMultipleSliceData(data, out int[] syncedCounts, out List<Rect> frameUvs);
                SetMultipleFrameData(data, syncedCounts, frameUvs);
                newCounts = syncedCounts;
            }
            else
            {
                List<int> gridCounts = new(oldCounts);
                gridCounts.RemoveAt(segmentIndex);
                newCounts = gridCounts;
            }
            RemapEvents(
                proxy,
                anchors,
                newCounts,
                oldSegment => oldSegment == segmentIndex ? -1 : oldSegment > segmentIndex ? oldSegment - 1 : oldSegment);

            bool changed = data.ApplyModifiedProperties();
            if (changed) PrefabUtility.RecordPrefabInstancePropertyModifications(data.Target);
            return changed;
        }

        public static bool AddSegment(FlipbookEditorData data, Texture2D texture)
        {
            data.Update();
            if (data.IsMultiple && texture && !IsMultipleTexture(texture, out _))
            {
                EditorUtility.DisplayDialog(
                    "无法添加该贴图",
                    $"{texture.name} 不是 Sprite Mode = Multiple，或尚未创建有效切片。",
                    "确定");
                return false;
            }

            Undo.RecordObject(data.Target, "添加 Flipbook 图集");

            EnsureFrameArraySize(data);
            int index = data.Textures.arraySize;
            data.Textures.arraySize++;
            data.Textures.GetArrayElementAtIndex(index).objectReferenceValue = texture;
            data.Frames.arraySize++;
            data.Frames.GetArrayElementAtIndex(index).intValue = data.IsMultiple ? 0 : data.GridFrameCount;

            bool changed = data.ApplyModifiedProperties();
            if (changed) PrefabUtility.RecordPrefabInstancePropertyModifications(data.Target);
            return data.IsMultiple && texture ? SyncMultipleSlices(data) : changed;
        }

        public static bool SetFrameCount(FlipbookEditorData data, int segmentIndex, int requestedFrameCount)
        {
            data.Update();
            if (data.IsMultiple) return false;
            if (segmentIndex < 0 || segmentIndex >= data.TextureCount) return false;

            int[] oldCounts = data.GetFrameCounts();
            int newFrameCount = Mathf.Clamp(requestedFrameCount, 1, data.GridFrameCount);
            bool hasSerializedFrame = segmentIndex < data.Frames.arraySize;
            int serializedFrameCount = hasSerializedFrame
                ? data.Frames.GetArrayElementAtIndex(segmentIndex).intValue
                : data.GridFrameCount;
            if (hasSerializedFrame && serializedFrameCount == newFrameCount) return false;

            FlipbookPlayerEventProxy proxy = GetProxy(data);
            List<EventAnchor> anchors = CaptureEventAnchors(proxy, oldCounts);
            int removedEventCount = anchors.FindAll(anchor => anchor.SegmentIndex == segmentIndex && anchor.LocalFrame >= newFrameCount).Count;
            if (removedEventCount > 0 &&
                !EditorUtility.DisplayDialog(
                    "缩短图集帧数",
                    $"此次修改会移除 {removedEventCount} 个超出新帧数的事件，其他事件会自动重映射。",
                    "修改并删除事件",
                    "取消"))
            {
                return false;
            }

            List<Object> undoTargets = new() { data.Target };
            if (proxy) undoTargets.Add(proxy);
            Undo.RecordObjects(undoTargets.ToArray(), "修改 Flipbook 分段帧数");

            EnsureFrameArraySize(data);
            data.Frames.GetArrayElementAtIndex(segmentIndex).intValue = newFrameCount;
            int[] newCounts = (int[])oldCounts.Clone();
            newCounts[segmentIndex] = newFrameCount;
            RemapEvents(proxy, anchors, newCounts, oldSegment => oldSegment, anchor =>
                anchor.SegmentIndex != segmentIndex || anchor.LocalFrame < newFrameCount);

            bool changed = data.ApplyModifiedProperties();
            if (changed) PrefabUtility.RecordPrefabInstancePropertyModifications(data.Target);
            return changed;
        }

        public static bool SetGridSize(FlipbookEditorData data, int requestedRows, int requestedColumns)
        {
            data.Update();
            if (data.IsMultiple) return false;
            int newRows = Mathf.Max(1, requestedRows);
            int newColumns = Mathf.Max(1, requestedColumns);
            if (newRows == data.Rows.intValue && newColumns == data.Columns.intValue) return false;

            int[] oldCounts = data.GetFrameCounts();
            int newCapacity = newRows * newColumns;
            int[] newCounts = new int[oldCounts.Length];
            for (int i = 0; i < oldCounts.Length; i++) newCounts[i] = Mathf.Clamp(oldCounts[i], 1, newCapacity);

            FlipbookPlayerEventProxy proxy = GetProxy(data);
            List<EventAnchor> anchors = CaptureEventAnchors(proxy, oldCounts);
            int removedEventCount = anchors.FindAll(anchor => anchor.LocalFrame >= newCounts[anchor.SegmentIndex]).Count;
            if (removedEventCount > 0 &&
                !EditorUtility.DisplayDialog(
                    "修改图集网格",
                    $"新网格容量不足，将移除 {removedEventCount} 个超出容量的帧事件。",
                    "修改并删除事件",
                    "取消"))
            {
                return false;
            }

            List<Object> undoTargets = new() { data.Target };
            if (proxy) undoTargets.Add(proxy);
            Undo.RecordObjects(undoTargets.ToArray(), "修改 Flipbook 网格");

            data.Rows.intValue = newRows;
            data.Columns.intValue = newColumns;
            EnsureFrameArraySize(data);
            for (int i = 0; i < newCounts.Length; i++) data.Frames.GetArrayElementAtIndex(i).intValue = newCounts[i];

            RemapEvents(proxy, anchors, newCounts, oldSegment => oldSegment, anchor =>
                anchor.LocalFrame < newCounts[anchor.SegmentIndex]);

            bool changed = data.ApplyModifiedProperties();
            if (changed) PrefabUtility.RecordPrefabInstancePropertyModifications(data.Target);
            return changed;
        }

        public static int GetPersistentCallCount(SerializedProperty unityEvent)
        {
            SerializedProperty calls = GetPersistentCalls(unityEvent);
            return calls?.arraySize ?? 0;
        }

        private static List<EventAnchor> CaptureEventAnchors(FlipbookPlayerEventProxy proxy, IReadOnlyList<int> frameCounts)
        {
            List<EventAnchor> anchors = new();
            if (!proxy) return anchors;

            SerializedObject serializedProxy = new(proxy);
            SerializedProperty events = serializedProxy.FindProperty("frameEvents");
            int totalFrames = FlipbookEditorData.GetTotalFrames(frameCounts);
            for (int i = 0; i < events.arraySize; i++)
            {
                int frame = events.GetArrayElementAtIndex(i).FindPropertyRelative("frameNumber").intValue;
                if (frame < 1 || frame > totalFrames) continue;
                FlipbookFrameLocation location = FlipbookEditorData.LocateFrame(frame, frameCounts);
                if (location.IsValid) anchors.Add(new EventAnchor(i, location.SegmentIndex, location.LocalFrame));
            }

            return anchors;
        }

        private static void RemapEvents(
            FlipbookPlayerEventProxy proxy,
            IReadOnlyList<EventAnchor> anchors,
            IReadOnlyList<int> newCounts,
            Func<int, int> mapSegment,
            Predicate<EventAnchor> keepEvent = null)
        {
            if (!proxy) return;

            SerializedObject serializedProxy = new(proxy);
            serializedProxy.Update();
            SerializedProperty events = serializedProxy.FindProperty("frameEvents");
            List<int> removedIndices = new();

            for (int i = 0; i < anchors.Count; i++)
            {
                EventAnchor anchor = anchors[i];
                int newSegment = mapSegment(anchor.SegmentIndex);
                bool keep = newSegment >= 0 && newSegment < newCounts.Count &&
                            anchor.LocalFrame < newCounts[newSegment] &&
                            (keepEvent == null || keepEvent(anchor));
                if (!keep)
                {
                    removedIndices.Add(anchor.ArrayIndex);
                    continue;
                }

                if (anchor.ArrayIndex >= events.arraySize) continue;
                int globalFrame = FlipbookEditorData.ToGlobalFrame(newSegment, anchor.LocalFrame, newCounts);
                events.GetArrayElementAtIndex(anchor.ArrayIndex).FindPropertyRelative("frameNumber").intValue = globalFrame;
            }

            removedIndices.Sort((a, b) => b.CompareTo(a));
            for (int i = 0; i < removedIndices.Count; i++)
                if (removedIndices[i] < events.arraySize)
                    DeleteArrayElement(events, removedIndices[i]);

            SortEvents(events);
            serializedProxy.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(proxy);
        }

        private static int MapMovedSegment(int segmentIndex, int fromIndex, int toIndex)
        {
            if (segmentIndex == fromIndex) return toIndex;
            if (fromIndex < toIndex && segmentIndex > fromIndex && segmentIndex <= toIndex) return segmentIndex - 1;
            if (toIndex < fromIndex && segmentIndex >= toIndex && segmentIndex < fromIndex) return segmentIndex + 1;
            return segmentIndex;
        }

        private static int CountRemovedEvents(IReadOnlyList<EventAnchor> anchors, IReadOnlyList<int> frameCounts)
        {
            int removedCount = 0;
            for (int i = 0; i < anchors.Count; i++)
            {
                EventAnchor anchor = anchors[i];
                if (anchor.SegmentIndex >= frameCounts.Count || anchor.LocalFrame >= frameCounts[anchor.SegmentIndex])
                    removedCount++;
            }

            return removedCount;
        }

        private static bool ValidateMultipleTextures(FlipbookEditorData data)
        {
            for (int i = 0; i < data.TextureCount; i++)
            {
                Texture2D texture = data.GetTexture(i);
                if (!texture || IsMultipleTexture(texture, out _)) continue;

                EditorUtility.DisplayDialog(
                    "无法使用 Multiple 模式",
                    $"第 {i + 1} 个图集 {texture.name} 不是 Sprite Mode = Multiple，或尚未创建有效切片。",
                    "确定");
                return false;
            }

            return true;
        }

        private static void ReadMultipleSliceData(FlipbookEditorData data, out int[] frameCounts, out List<Rect> frameUvs)
        {
            frameCounts = new int[data.TextureCount];
            frameUvs = new List<Rect>();
            for (int i = 0; i < data.TextureCount; i++)
            {
                Texture2D texture = data.GetTexture(i);
                if (!texture)
                {
                    frameCounts[i] = 0;
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(texture);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                SpriteMetaData[] sprites = importer?.spritesheet;
                if (sprites == null || sprites.Length == 0)
                {
                    frameCounts[i] = 0;
                    continue;
                }

                SortMultipleSprites(sprites);
                frameCounts[i] = sprites.Length;
                importer.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
                float textureWidth = Mathf.Max(1, sourceWidth);
                float textureHeight = Mathf.Max(1, sourceHeight);
                for (int j = 0; j < sprites.Length; j++)
                {
                    Rect rect = sprites[j].rect;
                    frameUvs.Add(new Rect(
                        rect.x / textureWidth,
                        rect.y / textureHeight,
                        rect.width / textureWidth,
                        rect.height / textureHeight));
                }
            }
        }

        internal static void SortMultipleSprites(SpriteMetaData[] sprites)
        {
            if (sprites == null || sprites.Length < 2 ||
                !TryGetNumericSuffixPrefix(sprites[0].name, out string prefix))
            {
                return;
            }

            for (int i = 1; i < sprites.Length; i++)
                if (!TryGetNumericSuffixPrefix(sprites[i].name, out string candidatePrefix) ||
                    !string.Equals(prefix, candidatePrefix, StringComparison.Ordinal))
                {
                    return;
                }

            Array.Sort(sprites, (left, right) => EditorUtility.NaturalCompare(left.name, right.name));
        }

        private static bool TryGetNumericSuffixPrefix(string spriteName, out string prefix)
        {
            prefix = null;
            if (string.IsNullOrEmpty(spriteName)) return false;

            int suffixStart = spriteName.Length;
            while (suffixStart > 0 && char.IsDigit(spriteName[suffixStart - 1])) suffixStart--;
            if (suffixStart == spriteName.Length) return false;

            prefix = spriteName.Substring(0, suffixStart);
            return true;
        }

        private static void SetMultipleFrameData(FlipbookEditorData data, IReadOnlyList<int> frameCounts, IReadOnlyList<Rect> frameUvs)
        {
            data.Frames.arraySize = frameCounts.Count;
            for (int i = 0; i < frameCounts.Count; i++)
                data.Frames.GetArrayElementAtIndex(i).intValue = Mathf.Max(0, frameCounts[i]);

            data.MultipleFrameUvs.arraySize = frameUvs.Count;
            for (int i = 0; i < frameUvs.Count; i++)
                data.MultipleFrameUvs.GetArrayElementAtIndex(i).rectValue = frameUvs[i];
        }

        private static void EnsureFrameArraySize(FlipbookEditorData data)
        {
            while (data.Frames.arraySize < data.Textures.arraySize)
            {
                int index = data.Frames.arraySize;
                data.Frames.arraySize++;
                data.Frames.GetArrayElementAtIndex(index).intValue = data.GridFrameCount;
            }

            while (data.Frames.arraySize > data.Textures.arraySize)
                DeleteArrayElement(data.Frames, data.Frames.arraySize - 1);
        }

        private static void ClearUnityEvent(SerializedProperty unityEvent)
        {
            SerializedProperty calls = GetPersistentCalls(unityEvent);
            if (calls != null) calls.arraySize = 0;
        }

        private static SerializedProperty GetPersistentCalls(SerializedProperty unityEvent)
        {
            SerializedProperty persistentCalls = unityEvent?.FindPropertyRelative("m_PersistentCalls");
            return persistentCalls?.FindPropertyRelative("m_Calls");
        }

        private static void SortEvents(SerializedProperty events)
        {
            for (int i = 0; i < events.arraySize - 1; i++)
            {
                int smallestIndex = i;
                int smallestFrame = events.GetArrayElementAtIndex(i).FindPropertyRelative("frameNumber").intValue;
                for (int j = i + 1; j < events.arraySize; j++)
                {
                    int candidateFrame = events.GetArrayElementAtIndex(j).FindPropertyRelative("frameNumber").intValue;
                    if (candidateFrame >= smallestFrame) continue;
                    smallestFrame = candidateFrame;
                    smallestIndex = j;
                }

                if (smallestIndex != i) events.MoveArrayElement(smallestIndex, i);
            }
        }

        private static void DeleteArrayElement(SerializedProperty array, int index)
        {
            int oldSize = array.arraySize;
            array.DeleteArrayElementAtIndex(index);
            if (array.arraySize == oldSize) array.DeleteArrayElementAtIndex(index);
        }
    }
}
