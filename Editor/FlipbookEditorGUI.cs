using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FlipbookEditorTools
{
    internal static class FlipbookEditorGUI
    {
        private const float IconButtonSize = 26f;
        private const float MinimumGridCellSize = 28f;
        private static GUIStyle _buttonLabel;
        private static GUIStyle _centeredMiniLabel;
        private static GUIStyle _dropAreaStyle;

        public static void BeginSection(string title, SdfIconType icon = SdfIconType.None)
        {
            SirenixEditorGUI.BeginBox();
            SirenixEditorGUI.BeginBoxHeader();
            Rect headerRect = EditorGUILayout.GetControlRect(false, 20f);
            if (icon != SdfIconType.None)
            {
                Rect iconRect = new(headerRect.x, headerRect.y + 2f, 16f, 16f);
                SdfIcons.DrawIcon(iconRect, icon, EditorStyles.label.normal.textColor);
                headerRect.xMin += 22f;
            }

            EditorGUI.LabelField(headerRect, title, EditorStyles.boldLabel);
            SirenixEditorGUI.EndBoxHeader();
        }

        public static void EndSection()
        {
            SirenixEditorGUI.EndBox();
        }

        public static void DrawSummary(FlipbookEditorData data, FlipbookPreviewSession session)
        {
            int totalFrames = data.GetTotalFrames();
            string targetKind;
            if (data.Player)
            {
                bool rawImage = data.Player.GetComponent<RawImage>();
                targetKind = rawImage ? "UI / RawImage" : data.Player.GetComponent<Renderer>() ? "Renderer" : "缺少渲染目标";
            }
            else
            {
                targetKind = "Flipbook Clip";
            }

            BeginSection(data.Target.name, SdfIconType.CollectionPlayFill);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawStat("类型", targetKind);
                DrawStat("识别", data.IsMultiple ? "Multiple" : "网格");
                DrawStat("图集", data.TextureCount.ToString());
                DrawStat("总帧", totalFrames.ToString());
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawStat("时长", $"{data.GetDuration():0.###}s");
                DrawStat("FPS", data.SafeFrameRate.ToString());
                string state = Application.isPlaying && data.Player
                    ? data.Player.IsPlaying ? "播放中" : "已停止"
                    : session is { IsPlaying: true }
                        ? "预览中"
                        : "已暂停";
                DrawStat("状态", state);
            }

            if (session != null)
                EditorGUILayout.LabelField(
                    session.IsPlaying ? $"编辑器预览中 · 第 {session.CurrentFrame} 帧" : $"当前第 {session.CurrentFrame} 帧",
                    EditorStyles.miniLabel);
            EndSection();
        }

        public static void DrawSegmentList(FlipbookEditorData data)
        {
            BeginSection("图集分段", SdfIconType.Images);
            EditorGUILayout.LabelField("图集", "实际帧", EditorStyles.miniBoldLabel);

            if (data.TextureCount == 0)
                EditorGUILayout.HelpBox("拖入一张或多张 Texture2D，或使用下方添加按钮。", MessageType.Info);

            for (int i = 0; i < data.TextureCount; i++)
            {
                SerializedProperty textureProperty = data.Textures.GetArrayElementAtIndex(i);
                Texture2D texture = textureProperty.objectReferenceValue as Texture2D;
                bool hasSerializedFrame = i < data.Frames.arraySize;
                int serializedFrameCount = hasSerializedFrame
                    ? data.Frames.GetArrayElementAtIndex(i).intValue
                    : data.IsMultiple
                        ? 0
                        : data.GridFrameCount;

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label((i + 1).ToString(), EditorStyles.miniLabel, GUILayout.Width(20f));
                    EditorGUI.BeginChangeCheck();
                    Texture2D selectedTexture = EditorGUILayout.ObjectField(
                        texture,
                        typeof(Texture2D),
                        false,
                        GUILayout.MinWidth(100f)) as Texture2D;
                    if (EditorGUI.EndChangeCheck())
                    {
                        FlipbookEventEditorUtility.SetTexture(data, i, selectedTexture);
                        GUIUtility.ExitGUI();
                    }

                    if (data.IsMultiple)
                    {
                        using (new EditorGUI.DisabledScope(true))
                            EditorGUILayout.IntField(serializedFrameCount, GUILayout.Width(54f));
                    }
                    else
                    {
                        EditorGUI.BeginChangeCheck();
                        int newFrameCount = EditorGUILayout.IntField(serializedFrameCount, GUILayout.Width(54f));
                        if (EditorGUI.EndChangeCheck())
                        {
                            FlipbookEventEditorUtility.SetFrameCount(data, i, newFrameCount);
                            GUIUtility.ExitGUI();
                        }
                    }

                    using (new EditorGUI.DisabledScope(i == 0))
                        if (IconButton(SdfIconType.ChevronUp, "上移分段", 22f))
                        {
                            FlipbookEventEditorUtility.MoveSegment(data, i, i - 1);
                            GUIUtility.ExitGUI();
                        }

                    using (new EditorGUI.DisabledScope(i >= data.TextureCount - 1))
                        if (IconButton(SdfIconType.ChevronDown, "下移分段", 22f))
                        {
                            FlipbookEventEditorUtility.MoveSegment(data, i, i + 1);
                            GUIUtility.ExitGUI();
                        }

                    if (IconButton(SdfIconType.Trash, "删除分段", 22f))
                    {
                        if (FlipbookEventEditorUtility.RemoveSegment(data, i)) GUIUtility.ExitGUI();
                    }
                }

                if (data.IsMultiple && texture && !FlipbookEventEditorUtility.IsMultipleTexture(texture, out _))
                    EditorGUILayout.HelpBox(
                        $"{texture.name} 不是 Sprite Mode = Multiple，或尚未创建有效切片。",
                        MessageType.Error);
                else if (data.IsMultiple && texture && serializedFrameCount <= 0)
                    EditorGUILayout.HelpBox($"{texture.name} 没有已同步的切片帧。", MessageType.Warning);
                else if (!hasSerializedFrame)
                    EditorGUILayout.HelpBox($"第 {i + 1} 个分段缺少帧数记录，编辑该值后会自动补齐。", MessageType.Warning);
                else if (!data.IsMultiple && (serializedFrameCount < 1 || serializedFrameCount > data.GridFrameCount))
                    EditorGUILayout.HelpBox(
                        $"第 {i + 1} 个分段的帧数必须在 1 到 {data.GridFrameCount} 之间。",
                        MessageType.Warning);

                if (!texture)
                {
                    EditorGUILayout.HelpBox($"第 {i + 1} 个分段没有贴图。", MessageType.Warning);
                }
                else if (!data.IsMultiple &&
                         (texture.width % data.SafeColumns != 0 || texture.height % data.SafeRows != 0))
                {
                    EditorGUILayout.HelpBox(
                        $"{texture.name} 的尺寸 {texture.width}×{texture.height} 不能被 {data.SafeColumns}×{data.SafeRows} 网格整除，可能出现采样边缘。",
                        MessageType.Warning);
                }
            }

            Rect dropRect = GUILayoutUtility.GetRect(0f, 38f, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "拖放 Texture2D 到这里批量添加", DropAreaStyle);
            HandleTextureDrop(dropRect, data);

            if (GUILayout.Button(new GUIContent(" 添加空分段", "在列表末尾添加一个空图集分段"), GUILayout.Height(26f)))
            {
                FlipbookEventEditorUtility.AddSegment(data, null);
                GUIUtility.ExitGUI();
            }

            EndSection();
        }

        public static void DrawSettings(FlipbookEditorData data)
        {
            BeginSection("帧识别与播放", data.IsMultiple ? SdfIconType.Images : SdfIconType.Grid3x3Gap);

            EditorGUI.BeginChangeCheck();
            FlipbookFrameSourceMode sourceMode = (FlipbookFrameSourceMode)EditorGUILayout.EnumPopup(
                new GUIContent("识别模式", "Grid 使用固定行列；Multiple 读取 Texture 的 Sprite 切片"),
                data.FrameSourceMode);
            if (EditorGUI.EndChangeCheck())
            {
                FlipbookEventEditorUtility.SetSourceMode(data, sourceMode);
                GUIUtility.ExitGUI();
            }

            if (data.IsMultiple)
            {
                int synchronizedFrames = data.MultipleFrameUvs.arraySize;
                EditorGUILayout.LabelField("已同步切片", $"{synchronizedFrames} 帧");
                if (IconTextButton(
                        SdfIconType.ArrowDownUp,
                        "同步切片",
                        "重新读取所有 Texture 的 Multiple Sprite 切片，并更新帧数、UV 与帧事件"))
                {
                    FlipbookEventEditorUtility.SyncMultipleSlices(data);
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.HelpBox("重新切片或调整切片顺序后，请执行同步切片。", MessageType.Info);
            }
            else
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel("网格");
                    int currentRows = data.Rows.intValue;
                    int currentColumns = data.Columns.intValue;
                    GUILayout.Label(new GUIContent("行", "图集物理行数"), GUILayout.Width(18f));
                    int rows = EditorGUILayout.IntField(currentRows, GUILayout.Width(52f));
                    GUILayout.Space(8f);
                    GUILayout.Label(new GUIContent("列", "图集物理列数"), GUILayout.Width(18f));
                    int columns = EditorGUILayout.IntField(currentColumns, GUILayout.Width(52f));
                    if (rows != currentRows || columns != currentColumns)
                    {
                        FlipbookEventEditorUtility.SetGridSize(data, rows, columns);
                        GUIUtility.ExitGUI();
                    }
                }

                EditorGUILayout.LabelField("单张容量", $"{data.GridFrameCount} 帧");

                if (data.Rows.intValue < 1 || data.Columns.intValue < 1)
                    EditorGUILayout.HelpBox("网格行列数必须大于 0。", MessageType.Warning);
            }

            int currentFrameRate = data.FrameRate.intValue;
            EditorGUI.BeginChangeCheck();
            int frameRate = EditorGUILayout.IntField(new GUIContent("帧率", "每秒播放帧数"), currentFrameRate);
            if (EditorGUI.EndChangeCheck()) data.FrameRate.intValue = Mathf.Max(1, frameRate);
            if (currentFrameRate < 1)
                EditorGUILayout.HelpBox("帧率必须大于 0。", MessageType.Warning);

            if (data.IsPlayer)
            {
                data.Loop.boolValue = EditorGUILayout.Toggle(new GUIContent("循环播放"), data.Loop.boolValue);
                data.AutoPlayOnStart.boolValue = EditorGUILayout.Toggle(
                    new GUIContent("Start 时播放", "在 Start 生命周期首次执行时自动播放"),
                    data.AutoPlayOnStart.boolValue);
                data.AutoPlayOnEnable.boolValue = EditorGUILayout.Toggle(
                    new GUIContent("OnEnable 时播放", "每次组件进入 OnEnable 生命周期时自动播放"),
                    data.AutoPlayOnEnable.boolValue);
            }

            EndSection();
        }

        public static void DrawPlayback(FlipbookEditorData data, FlipbookPreviewSession session, bool drawPreview)
        {
            BeginSection(Application.isPlaying ? "运行时控制" : "编辑器预览", SdfIconType.PlayCircleFill);

            int totalFrames = data.GetTotalFrames();
            int currentFrame = GetDisplayedFrame(data, session);
            if (totalFrames <= 0)
            {
                EditorGUILayout.HelpBox("没有可预览帧。", MessageType.Warning);
                EndSection();
                return;
            }

            if (drawPreview) DrawFramePreview(data, currentFrame, 150f);

            if (Application.isPlaying && !data.Player)
            {
                EditorGUILayout.HelpBox("Play Mode 下 FlipbookClip 仅供查看；请选择场景中的 FlipbookPlayer 进行运行时控制。", MessageType.Info);
                EndSection();
                return;
            }

            if (Application.isPlaying && data.Player)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.IntSlider("当前帧", Mathf.Clamp(currentFrame, 1, totalFrames), 1, totalFrames);
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                int selectedFrame = EditorGUILayout.IntSlider("当前帧", Mathf.Clamp(currentFrame, 1, totalFrames), 1, totalFrames);
                if (EditorGUI.EndChangeCheck()) session?.SetFrame(selectedFrame);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(Application.isPlaying || session == null))
                    if (IconButton(SdfIconType.SkipBackwardFill, "上一帧"))
                        session.Step(-1);

                bool isPlaying = Application.isPlaying && data.Player ? data.Player.IsPlaying : session is { IsPlaying: true };
                if (IconButton(isPlaying ? SdfIconType.PauseFill : SdfIconType.PlayFill, isPlaying ? "暂停" : "播放", 30f, isPlaying))
                {
                    if (Application.isPlaying && data.Player)
                    {
                        if (data.Player.IsPlaying)
                            data.Player.Pause();
                        else if (data.Player.CurrentFrameNumber > 1 && data.Player.CurrentFrameNumber < totalFrames)
                            data.Player.Resume();
                        else
                            data.Player.Play();
                    }
                    else if (session != null)
                    {
                        if (session.IsPlaying) session.Pause();
                        else session.Play();
                    }
                }

                if (IconButton(SdfIconType.StopFill, "停止"))
                {
                    if (Application.isPlaying && data.Player)
                        data.Player.Stop();
                    else
                        session?.Stop();
                }

                using (new EditorGUI.DisabledScope(Application.isPlaying || session == null))
                    if (IconButton(SdfIconType.SkipForwardFill, "下一帧"))
                        session.Step(1);
            }

            if (!data.IsPlayer && session != null && !Application.isPlaying)
                session.PreviewLoop = EditorGUILayout.ToggleLeft("预览循环", session.PreviewLoop);
            else if (data.IsPlayer && session != null)
                session.PreviewLoop = data.Loop.boolValue;

            EndSection();
        }

        public static void DrawDependencies(FlipbookEditorData data)
        {
            if (!data.Player) return;

            BeginSection("扩展功能", SdfIconType.PlusCircle);
            FlipbookPlayerEventProxy proxy = FlipbookEventEditorUtility.GetProxy(data);

            if (!proxy)
            {
                if (IconTextButton(SdfIconType.CalendarEventFill, "添加事件代理", "添加 FlipbookPlayerEventProxy 并自动绑定播放器"))
                {
                    FlipbookEventEditorUtility.AddProxy(data.Player);
                    GUIUtility.ExitGUI();
                }
            }
            else
            {
                SerializedObject serializedProxy = new(proxy);
                serializedProxy.Update();
                SerializedProperty events = serializedProxy.FindProperty("frameEvents");
                EditorGUILayout.LabelField("事件代理", $"{events.arraySize} 个帧事件", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(serializedProxy.FindProperty("onCompleted"), new GUIContent("播放完成"));
                serializedProxy.ApplyModifiedProperties();
            }

            FlipbookEditorIntegrationRegistry.Draw(data.Player);

            EndSection();
        }

        public static void DrawEventList(FlipbookPlayerEventProxy proxy, int totalFrames, int selectedFrame)
        {
            if (!proxy) return;

            SerializedObject serializedProxy = new(proxy);
            serializedProxy.Update();
            SerializedProperty events = serializedProxy.FindProperty("frameEvents");

            BeginSection($"帧事件 · {events.arraySize}", SdfIconType.CalendarEventFill);
            if (events.arraySize == 0)
                EditorGUILayout.HelpBox("在工作台开启事件编辑模式后，点击网格即可添加帧事件。", MessageType.Info);

            for (int i = 0; i < events.arraySize; i++)
            {
                SerializedProperty eventProperty = events.GetArrayElementAtIndex(i);
                int frame = eventProperty.FindPropertyRelative("frameNumber").intValue;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField($"第 {frame} 帧", frame == selectedFrame ? EditorStyles.boldLabel : EditorStyles.label);
                        if (IconButton(SdfIconType.Trash, "删除事件", 22f))
                        {
                            if (FlipbookEventEditorUtility.RemoveEventAtIndex(proxy, i)) GUIUtility.ExitGUI();
                        }
                    }

                    if (frame < 1 || frame > totalFrames)
                        EditorGUILayout.HelpBox("该事件超出当前动画帧范围，已保留但不会在网格中显示。", MessageType.Warning);

                    EditorGUI.BeginChangeCheck();
                    int newFrame = EditorGUILayout.IntField("帧号", frame);
                    if (EditorGUI.EndChangeCheck())
                    {
                        FlipbookEventEditorUtility.ChangeEventFrame(proxy, i, newFrame, totalFrames);
                        GUIUtility.ExitGUI();
                    }

                    EditorGUILayout.PropertyField(eventProperty.FindPropertyRelative("onReached"), new GUIContent("到达时"));
                }
            }

            serializedProxy.ApplyModifiedProperties();
            EndSection();
        }

        public static void DrawSelectedEvent(FlipbookPlayerEventProxy proxy, int selectedFrame)
        {
            if (!proxy) return;

            SerializedObject serializedProxy = new(proxy);
            serializedProxy.Update();
            SerializedProperty events = serializedProxy.FindProperty("frameEvents");
            int index = FlipbookEventEditorUtility.FindEventIndex(events, selectedFrame);
            if (index < 0) return;

            SerializedProperty selectedEvent = events.GetArrayElementAtIndex(index);
            EditorGUILayout.PropertyField(selectedEvent.FindPropertyRelative("onReached"), new GUIContent($"第 {selectedFrame} 帧事件"));
            serializedProxy.ApplyModifiedProperties();
        }

        public static void DrawFramePreview(FlipbookEditorData data, int globalFrame, float height)
        {
            FlipbookFrameLocation location = data.LocateFrame(globalFrame);
            Rect previewRect = GUILayoutUtility.GetRect(0f, height, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(previewRect, EditorGUIUtility.isProSkin ? new Color(0.11f, 0.11f, 0.11f) : new Color(0.72f, 0.72f, 0.72f));

            if (!location.IsValid) return;
            Texture2D texture = data.GetTexture(location.SegmentIndex);
            if (!texture)
            {
                GUI.Label(previewRect, "当前分段没有贴图", CenteredMiniLabel);
                return;
            }

            if (!data.TryGetFrameUv(location.SegmentIndex, location.LocalFrame, out Rect uv))
            {
                GUI.Label(previewRect, "当前帧缺少切片 UV，请同步切片", CenteredMiniLabel);
                return;
            }

            float cellAspect = texture.width * uv.width / Mathf.Max(1f, texture.height * uv.height);
            Rect fittedRect = FitRect(previewRect, cellAspect, 8f);
            GUI.DrawTextureWithTexCoords(fittedRect, texture, uv, true);
            GUI.Label(new Rect(previewRect.x + 6f, previewRect.y + 4f, previewRect.width - 12f, 18f), $"#{location.GlobalFrame}", EditorStyles.miniBoldLabel);
        }

        public static int DrawAtlasGrid(
            FlipbookEditorData data,
            int segmentIndex,
            int currentFrame,
            ISet<int> eventFrames,
            float availableWidth,
            float zoom)
        {
            Texture2D texture = data.GetTexture(segmentIndex);
            if (!texture)
            {
                EditorGUILayout.HelpBox("当前分段没有贴图。", MessageType.Warning);
                return 0;
            }

            if (data.IsMultiple)
                return DrawMultipleAtlas(data, segmentIndex, currentFrame, eventFrames, texture, availableWidth, zoom);

            int rows = data.SafeRows;
            int columns = data.SafeColumns;
            float aspect = texture.width / (float)Mathf.Max(1, texture.height);
            float minimumWidth = Mathf.Max(columns * MinimumGridCellSize, rows * MinimumGridCellSize * aspect);
            float width = Mathf.Max(availableWidth, minimumWidth) * Mathf.Clamp(zoom, 0.5f, 4f);
            float height = width / aspect;
            Rect atlasRect = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
            GUI.DrawTexture(atlasRect, texture, ScaleMode.StretchToFill, true);

            float cellWidth = atlasRect.width / columns;
            float cellHeight = atlasRect.height / rows;
            int validFrameCount = data.GetFrameCount(segmentIndex);
            int globalStart = data.GetSegmentStartFrame(segmentIndex);
            int clickedFrame = 0;
            Color gridColor = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.36f) : new Color(0f, 0f, 0f, 0.42f);

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int localFrame = row * columns + column;
                    Rect cellRect = new(
                        atlasRect.x + column * cellWidth,
                        atlasRect.y + row * cellHeight,
                        cellWidth,
                        cellHeight);

                    DrawBorder(cellRect, gridColor, 1f);
                    if (localFrame >= validFrameCount)
                    {
                        EditorGUI.DrawRect(cellRect, new Color(0f, 0f, 0f, 0.62f));
                        continue;
                    }

                    int globalFrame = globalStart + localFrame;
                    if (globalFrame == currentFrame)
                        DrawBorder(new Rect(cellRect.x + 1f, cellRect.y + 1f, cellRect.width - 2f, cellRect.height - 2f), new Color(0.2f, 0.72f, 1f), 3f);

                    GUI.Label(cellRect, globalFrame.ToString(), CenteredMiniLabel);
                    if (eventFrames != null && eventFrames.Contains(globalFrame))
                    {
                        Rect eventRect = new(cellRect.xMax - 16f, cellRect.y + 3f, 13f, 13f);
                        SdfIcons.DrawIcon(eventRect, SdfIconType.CalendarEventFill, new Color(1f, 0.67f, 0.2f));
                    }

                    GUI.Label(cellRect, new GUIContent(string.Empty,
                        $"全局帧 {globalFrame} · 分段 {segmentIndex + 1} · 局部帧 {localFrame + 1} · 行 {row + 1} 列 {column + 1}"));

                    Event currentEvent = Event.current;
                    if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && cellRect.Contains(currentEvent.mousePosition))
                    {
                        clickedFrame = globalFrame;
                        currentEvent.Use();
                    }
                }
            }

            return clickedFrame;
        }

        private static int DrawMultipleAtlas(
            FlipbookEditorData data,
            int segmentIndex,
            int currentFrame,
            ISet<int> eventFrames,
            Texture2D texture,
            float availableWidth,
            float zoom)
        {
            float aspect = texture.width / (float)Mathf.Max(1, texture.height);
            float width = Mathf.Max(availableWidth, 420f) * Mathf.Clamp(zoom, 0.5f, 4f);
            float height = width / aspect;
            Rect atlasRect = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
            GUI.DrawTexture(atlasRect, texture, ScaleMode.StretchToFill, true);

            int frameCount = data.GetFrameCount(segmentIndex);
            int globalStart = data.GetSegmentStartFrame(segmentIndex);
            int clickedFrame = 0;
            Color sliceColor = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.56f) : new Color(0f, 0f, 0f, 0.58f);

            for (int localFrame = 0; localFrame < frameCount; localFrame++)
            {
                if (!data.TryGetFrameUv(segmentIndex, localFrame, out Rect uv)) continue;

                Rect sliceRect = new(
                    atlasRect.x + uv.x * atlasRect.width,
                    atlasRect.y + (1f - uv.y - uv.height) * atlasRect.height,
                    uv.width * atlasRect.width,
                    uv.height * atlasRect.height);
                int globalFrame = globalStart + localFrame;

                DrawBorder(sliceRect, sliceColor, 1f);
                if (globalFrame == currentFrame)
                    DrawBorder(
                        new Rect(sliceRect.x + 1f, sliceRect.y + 1f, sliceRect.width - 2f, sliceRect.height - 2f),
                        new Color(0.2f, 0.72f, 1f),
                        3f);

                GUI.Label(sliceRect, globalFrame.ToString(), CenteredMiniLabel);
                if (eventFrames != null && eventFrames.Contains(globalFrame))
                {
                    Rect eventRect = new(sliceRect.xMax - 16f, sliceRect.y + 3f, 13f, 13f);
                    SdfIcons.DrawIcon(eventRect, SdfIconType.CalendarEventFill, new Color(1f, 0.67f, 0.2f));
                }

                GUI.Label(
                    sliceRect,
                    new GUIContent(string.Empty, $"全局帧 {globalFrame} · 分段 {segmentIndex + 1} · 切片 {localFrame + 1}"));

                Event currentEvent = Event.current;
                if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && sliceRect.Contains(currentEvent.mousePosition))
                {
                    clickedFrame = globalFrame;
                    currentEvent.Use();
                }
            }

            return clickedFrame;
        }

        public static bool IconButton(SdfIconType icon, string tooltip, float size = IconButtonSize, bool selected = false)
        {
            Rect rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            GUIStyle style = selected ? EditorStyles.toolbarButton : GUI.skin.button;
            bool clicked = GUI.Button(rect, new GUIContent(string.Empty, tooltip), style);
            if (Event.current.type == EventType.Repaint)
            {
                float padding = Mathf.Max(3f, size * 0.22f);
                Rect iconRect = new(rect.x + padding, rect.y + padding, rect.width - padding * 2f, rect.height - padding * 2f);
                SdfIcons.DrawIcon(iconRect, icon, GUI.enabled ? EditorStyles.label.normal.textColor : new Color(0.5f, 0.5f, 0.5f));
            }

            return clicked;
        }

        public static bool IconTextButton(SdfIconType icon, string text, string tooltip, float height = 28f)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, height, GUILayout.ExpandWidth(true));
            bool clicked = GUI.Button(rect, new GUIContent(string.Empty, tooltip));
            if (Event.current.type != EventType.Repaint) return clicked;

            GUIContent label = new(text);
            float textWidth = ButtonLabel.CalcSize(label).x;
            float contentWidth = 18f + 5f + textWidth;
            float contentX = rect.center.x - contentWidth * 0.5f;
            Color color = GUI.enabled ? EditorStyles.label.normal.textColor : new Color(0.5f, 0.5f, 0.5f);
            SdfIcons.DrawIcon(new Rect(contentX, rect.y + (rect.height - 16f) * 0.5f, 16f, 16f), icon, color);
            GUI.Label(new Rect(contentX + 23f, rect.y, textWidth, rect.height), label, ButtonLabel);
            return clicked;
        }

        public static int GetDisplayedFrame(FlipbookEditorData data, FlipbookPreviewSession session)
        {
            if (Application.isPlaying && data.Player)
                return Mathf.Max(1, data.Player.CurrentFrameNumber);
            return session?.CurrentFrame ?? 1;
        }

        private static Rect FitRect(Rect bounds, float aspect, float padding)
        {
            Rect result = new(bounds.x + padding, bounds.y + padding, bounds.width - padding * 2f, bounds.height - padding * 2f);
            if (result.width / result.height > aspect)
            {
                float width = result.height * aspect;
                result.x += (result.width - width) * 0.5f;
                result.width = width;
            }
            else
            {
                float height = result.width / aspect;
                result.y += (result.height - height) * 0.5f;
                result.height = height;
            }

            return result;
        }

        private static void DrawStat(string label, string value)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(54f)))
            {
                GUILayout.Label(label, EditorStyles.centeredGreyMiniLabel);
                GUILayout.Label(value, EditorStyles.centeredGreyMiniLabel);
            }
        }

        private static void DrawBorder(Rect rect, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private static void HandleTextureDrop(Rect dropRect, FlipbookEditorData data)
        {
            Event currentEvent = Event.current;
            if (!dropRect.Contains(currentEvent.mousePosition) ||
                (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform))
                return;

            bool hasTexture = false;
            bool hasInvalidTexture = false;
            for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
                if (DragAndDrop.objectReferences[i] is Texture2D texture)
                {
                    hasTexture = true;
                    if (data.IsMultiple && !FlipbookEventEditorUtility.IsMultipleTexture(texture, out _))
                        hasInvalidTexture = true;
                }

            if (!hasTexture) return;
            DragAndDrop.visualMode = hasInvalidTexture ? DragAndDropVisualMode.Rejected : DragAndDropVisualMode.Copy;
            if (currentEvent.type == EventType.DragPerform)
            {
                if (hasInvalidTexture)
                {
                    EditorUtility.DisplayDialog(
                        "无法添加贴图",
                        "当前 Flipbook 使用 Multiple 模式，只能拖入 Sprite Mode = Multiple 且包含有效切片的 Texture。",
                        "确定");
                    currentEvent.Use();
                    return;
                }

                DragAndDrop.AcceptDrag();
                for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
                    if (DragAndDrop.objectReferences[i] is Texture2D texture)
                        FlipbookEventEditorUtility.AddSegment(data, texture);
                currentEvent.Use();
                GUIUtility.ExitGUI();
            }

            currentEvent.Use();
        }

        private static GUIStyle CenteredMiniLabel => _centeredMiniLabel ??= new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            clipping = TextClipping.Clip,
            normal = { textColor = Color.white }
        };

        private static GUIStyle ButtonLabel => _buttonLabel ??= new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft
        };

        private static GUIStyle DropAreaStyle => _dropAreaStyle ??= new GUIStyle(EditorStyles.helpBox)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 11
        };
    }
}
