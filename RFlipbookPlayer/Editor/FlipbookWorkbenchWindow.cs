using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace FlipbookEditorTools
{
    public sealed class FlipbookWorkbenchWindow : OdinEditorWindow
    {
        private const float SidebarWidth = 310f;

        [SerializeField] private Object _target;
        [SerializeField] private int _segmentIndex;
        [SerializeField] private float _zoom = 1f;
        [SerializeField] private bool _eventEditMode;
        [SerializeField] private Vector2 _gridScroll;
        [SerializeField] private Vector2 _sidebarScroll;

        private FlipbookEditorData _data;
        private FlipbookPreviewSession _session;

        /// <summary>
        ///     从菜单打开当前选择对象的工作台。
        /// </summary>
        [MenuItem("Tools/Flipbook Workbench")]
        private static void OpenFromMenu()
        {
            Open(ResolveTarget(Selection.activeObject));
        }

        /// <summary>
        ///     打开并定位到指定目标的 Flipbook 工作台。
        /// </summary>
        /// <param name="target">要编辑或预览的播放器或 Clip。</param>
        public static void Open(Object target)
        {
            FlipbookWorkbenchWindow window = GetWindow<FlipbookWorkbenchWindow>();
            window.titleContent = new GUIContent("Flipbook", EditorGUIUtility.IconContent("Animation.Play").image);
            window.minSize = new Vector2(620f, 480f);
            window.SetTarget(ResolveTarget(target));
            window.Show();
            window.Focus();
        }

        /// <summary>
        ///     启用工作台时恢复目标和撤销监听。
        /// </summary>
        protected override void OnEnable()
        {
            base.OnEnable();
            Undo.undoRedoPerformed += OnUndoRedo;
            SetTarget(ResolveTarget(_target));
        }

        /// <summary>
        ///     关闭工作台时释放预览会话和监听。
        /// </summary>
        protected override void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            ReleaseSession();
            base.OnDisable();
        }

        /// <summary>
        ///     绘制 Flipbook 工作台界面。
        /// </summary>
        protected override void OnImGUI()
        {
            DrawTargetToolbar();
            if (_data == null || !_data.IsValid)
            {
                DrawEmptyState();
                return;
            }

            _data.Update();
            int totalFrames = _data.GetTotalFrames();
            if (totalFrames > 0 && _session.CurrentFrame > totalFrames) _session.SetFrame(totalFrames);

            int displayedFrame = FlipbookEditorGUI.GetDisplayedFrame(_data, _session);
            FlipbookFrameLocation currentLocation = _data.LocateFrame(displayedFrame);
            if ((_session.IsPlaying || Application.isPlaying) && currentLocation.IsValid)
                _segmentIndex = currentLocation.SegmentIndex;
            _segmentIndex = Mathf.Clamp(_segmentIndex, 0, Mathf.Max(0, _data.TextureCount - 1));

            FlipbookEditorGUI.DrawSummary(_data, _session);
            EditorGUILayout.Space(4f);

            if (position.width >= 760f)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawMainGrid(Mathf.Max(280f, position.width - SidebarWidth - 32f), displayedFrame);
                    DrawSidebar(displayedFrame, SidebarWidth);
                }
            }
            else
            {
                DrawMainGrid(Mathf.Max(280f, position.width - 24f), displayedFrame);
                EditorGUILayout.Space(5f);
                DrawSidebar(displayedFrame, position.width - 24f);
            }

            bool changed = _data.ApplyModifiedProperties();
            if (changed && !Application.isPlaying) _session.SetFrame(_session.CurrentFrame);
        }

        /// <summary>
        ///     播放器运行时持续刷新工作台。
        /// </summary>
        private void Update()
        {
            if (Application.isPlaying && _data?.Player && _data.Player.IsPlaying) Repaint();
        }

        /// <summary>
        ///     绘制工作台顶部的目标选择栏。
        /// </summary>
        private void DrawTargetToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("目标", GUILayout.Width(30f));
                EditorGUI.BeginChangeCheck();
                Object selectedTarget = EditorGUILayout.ObjectField(_target, typeof(Object), true);
                if (EditorGUI.EndChangeCheck()) SetTarget(ResolveTarget(selectedTarget));

                if (GUILayout.Button(new GUIContent("使用当前选择", "从当前 Selection 解析 FlipbookPlayer 或 FlipbookClip"), EditorStyles.toolbarButton, GUILayout.Width(86f)))
                    SetTarget(ResolveTarget(Selection.activeObject));
            }
        }

        /// <summary>
        ///     绘制未选择有效目标时的提示。
        /// </summary>
        private void DrawEmptyState()
        {
            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.HelpBox("请选择 FlipbookPlayer、包含播放器的 GameObject，或 FlipbookClip。", MessageType.Info);
                GUILayout.FlexibleSpace();
            }
            GUILayout.FlexibleSpace();
        }

        /// <summary>
        ///     绘制当前图集和帧网格。
        /// </summary>
        /// <param name="width">绘制区域的宽度，单位为编辑器像素。</param>
        /// <param name="displayedFrame">从 1 开始的当前显示全局帧号。</param>
        private void DrawMainGrid(float width, int displayedFrame)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(width), GUILayout.ExpandHeight(true)))
            {
                DrawAtlasTabs();

                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    GUILayout.Label("缩放", GUILayout.Width(30f));
                    _zoom = GUILayout.HorizontalSlider(_zoom, 0.5f, 4f, GUILayout.Width(120f));
                    GUILayout.Label($"{_zoom:0.0}×", EditorStyles.miniLabel, GUILayout.Width(36f));
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("全局帧号", EditorStyles.miniLabel);
                }

                HashSet<int> eventFrames = FlipbookEventEditorUtility.GetEventFrames(FlipbookEventEditorUtility.GetProxy(_data));
                _gridScroll = EditorGUILayout.BeginScrollView(_gridScroll, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                int clickedFrame = FlipbookEditorGUI.DrawAtlasGrid(
                    _data,
                    _segmentIndex,
                    displayedFrame,
                    eventFrames,
                    Mathf.Max(200f, width - 22f),
                    _zoom);
                EditorGUILayout.EndScrollView();

                if (clickedFrame > 0 && !Application.isPlaying)
                {
                    _session.SetFrame(clickedFrame);
                    if (_eventEditMode)
                    {
                        FlipbookPlayerEventProxy proxy = FlipbookEventEditorUtility.GetProxy(_data);
                        if (proxy) FlipbookEventEditorUtility.ToggleEvent(proxy, clickedFrame);
                    }
                    Repaint();
                }
            }
        }

        /// <summary>
        ///     绘制图集分段切换控件。
        /// </summary>
        private void DrawAtlasTabs()
        {
            if (_data.TextureCount <= 0) return;

            string[] labels = new string[_data.TextureCount];
            for (int i = 0; i < labels.Length; i++)
            {
                Texture2D texture = _data.GetTexture(i);
                labels[i] = texture ? $"{i + 1} · {texture.name}" : $"{i + 1} · 空";
            }

            _segmentIndex = labels.Length <= 4
                ? GUILayout.Toolbar(_segmentIndex, labels, GUILayout.Height(24f))
                : EditorGUILayout.Popup("当前图集", _segmentIndex, labels);
        }

        /// <summary>
        ///     绘制播放控制、内收设置和帧信息侧栏。
        /// </summary>
        /// <param name="displayedFrame">从 1 开始的当前显示全局帧号。</param>
        /// <param name="width">绘制区域的宽度，单位为编辑器像素。</param>
        private void DrawSidebar(int displayedFrame, float width)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(width), GUILayout.ExpandHeight(true)))
            {
                _sidebarScroll = EditorGUILayout.BeginScrollView(_sidebarScroll);
                FlipbookEditorGUI.DrawPlayback(_data, _session, true);

                EditorGUILayout.Space(5f);
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                    FlipbookEditorGUI.DrawInsetSettings(_data);

                FlipbookFrameLocation location = _data.LocateFrame(displayedFrame);
                if (location.IsValid)
                {
                    FlipbookEditorGUI.BeginSection("帧信息", SdfIconType.InfoCircleFill);
                    EditorGUILayout.LabelField("全局帧", location.GlobalFrame.ToString());
                    EditorGUILayout.LabelField("图集分段", (location.SegmentIndex + 1).ToString());
                    EditorGUILayout.LabelField("局部帧", (location.LocalFrame + 1).ToString());
                    if (_data.IsMultiple)
                    {
                        if (_data.TryGetFrameUv(location.SegmentIndex, location.LocalFrame, out Rect uv))
                            EditorGUILayout.LabelField("切片 UV", $"({uv.x:0.###}, {uv.y:0.###}, {uv.width:0.###}, {uv.height:0.###})");
                    }
                    else
                    {
                        int row = location.LocalFrame / _data.SafeColumns;
                        int column = location.LocalFrame % _data.SafeColumns;
                        EditorGUILayout.LabelField("网格坐标", $"行 {row + 1} · 列 {column + 1}");
                    }
                    FlipbookEditorGUI.EndSection();
                }

                if (_data.Player)
                {
                    EditorGUILayout.Space(5f);
                    DrawEventTools(displayedFrame);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        /// <summary>
        ///     绘制当前帧的事件编辑工具。
        /// </summary>
        /// <param name="displayedFrame">从 1 开始的当前显示全局帧号。</param>
        private void DrawEventTools(int displayedFrame)
        {
            FlipbookPlayerEventProxy proxy = FlipbookEventEditorUtility.GetProxy(_data);
            FlipbookEditorGUI.BeginSection("帧事件", SdfIconType.CalendarEventFill);

            if (!proxy)
            {
                _eventEditMode = false;
                EditorGUILayout.HelpBox("添加事件代理后才能在网格中编辑帧事件。", MessageType.Info);
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                    if (FlipbookEditorGUI.IconTextButton(
                            SdfIconType.CalendarEventFill,
                            "添加事件代理",
                            "添加 FlipbookPlayerEventProxy 并自动绑定当前播放器"))
                    {
                        FlipbookEventEditorUtility.AddProxy(_data.Player);
                        GUIUtility.ExitGUI();
                    }
            }
            else
            {
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                    _eventEditMode = GUILayout.Toggle(
                        _eventEditMode,
                        new GUIContent("事件编辑模式", "开启后，单击网格可添加或移除该帧事件"),
                        EditorStyles.toolbarButton,
                        GUILayout.Height(28f));

                EditorGUILayout.LabelField(
                    _eventEditMode ? "单击格子：添加或移除事件" : "单击格子：仅选择帧",
                    EditorStyles.centeredGreyMiniLabel);
                FlipbookEditorGUI.DrawSelectedEvent(proxy, displayedFrame);

                SerializedObject serializedProxy = new(proxy);
                serializedProxy.Update();
                EditorGUILayout.PropertyField(serializedProxy.FindProperty("onCompleted"), new GUIContent("播放完成"));
                serializedProxy.ApplyModifiedProperties();
            }

            FlipbookEditorGUI.EndSection();
        }

        /// <summary>
        ///     切换工作台的编辑目标和预览会话。
        /// </summary>
        /// <param name="target">要编辑或预览的播放器或 Clip。</param>
        private void SetTarget(Object target)
        {
            Object resolvedTarget = ResolveTarget(target);
            if (_target == resolvedTarget && _data != null) return;

            ReleaseSession();
            _target = resolvedTarget;
            _segmentIndex = 0;
            _eventEditMode = false;
            _data = _target ? new FlipbookEditorData(_target) : null;
            _session = _target ? FlipbookPreviewSessions.Acquire(_target) : null;
            if (_session != null) _session.Changed += OnPreviewChanged;
            Repaint();
        }

        /// <summary>
        ///     释放工作台持有的预览会话。
        /// </summary>
        private void ReleaseSession()
        {
            if (_session != null) _session.Changed -= OnPreviewChanged;
            FlipbookPreviewSessions.Release(_session);
            _session = null;
            _data = null;
        }

        /// <summary>
        ///     在撤销或重做后刷新编辑数据和预览。
        /// </summary>
        private void OnUndoRedo()
        {
            if (_data == null) return;
            _data.Update();
            int totalFrames = _data.GetTotalFrames();
            if (totalFrames > 0) _session.SetFrame(Mathf.Min(_session.CurrentFrame, totalFrames));
            Repaint();
        }

        /// <summary>
        ///     在预览状态变化后重绘界面。
        /// </summary>
        private void OnPreviewChanged()
        {
            Repaint();
        }

        /// <summary>
        ///     从选择对象中解析播放器或 Clip。
        /// </summary>
        /// <param name="candidate">待解析的 Unity 对象。</param>
        /// <returns>解析出的播放器或 Clip；无法解析时返回 null。</returns>
        private static Object ResolveTarget(Object candidate)
        {
            if (candidate is FlipbookPlayer or FlipbookClip) return candidate;
            if (candidate is GameObject gameObject) return gameObject.GetComponent<FlipbookPlayer>();
            if (candidate is Component component) return component.GetComponent<FlipbookPlayer>();
            return null;
        }
    }
}
