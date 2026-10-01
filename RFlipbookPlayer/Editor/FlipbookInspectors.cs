using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace FlipbookEditorTools
{
    internal abstract class FlipbookTargetEditorBase : OdinEditor
    {
        private FlipbookEditorData _data;
        private FlipbookPreviewSession _session;

        /// <summary>
        ///     获取当前 Inspector 的序列化编辑数据。
        /// </summary>
        protected FlipbookEditorData Data => _data;

        /// <summary>
        ///     启用时初始化状态和事件订阅。
        /// </summary>
        protected override void OnEnable()
        {
            base.OnEnable();
            if (targets.Length != 1 || !target) return;

            _data = new FlipbookEditorData(target);
            _session = FlipbookPreviewSessions.Acquire(target);
            _session.Changed += OnPreviewChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        /// <summary>
        ///     停用时清理状态和事件订阅。
        /// </summary>
        protected override void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            if (_session != null) _session.Changed -= OnPreviewChanged;
            FlipbookPreviewSessions.Release(_session);
            _session = null;
            _data = null;
            base.OnDisable();
        }

        /// <summary>
        ///     绘制目标对象的自定义 Inspector。
        /// </summary>
        public override void OnInspectorGUI()
        {
            if (targets.Length != 1)
            {
                EditorGUILayout.HelpBox("Flipbook 可视化工具一次只编辑一个目标。", MessageType.Info);
                return;
            }

            if (_data == null || !_data.IsValid)
            {
                EditorGUILayout.HelpBox("无法读取 Flipbook 序列化数据。", MessageType.Error);
                return;
            }

            _data.Update();
            ClampPreviewFrame();

            FlipbookEditorGUI.DrawSummary(_data, _session);

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                FlipbookEditorGUI.DrawSegmentList(_data);
                EditorGUILayout.Space(5f);
                FlipbookEditorGUI.DrawSettings(_data);
            }

            bool changed = _data.ApplyModifiedProperties();
            if (changed && !Application.isPlaying)
            {
                _data.Update();
                ClampPreviewFrame();
                _session?.SetFrame(_session.CurrentFrame);
            }

            EditorGUILayout.Space(5f);
            FlipbookEditorGUI.DrawPlayback(_data, _session, true);

            if (_data.Player)
            {
                EditorGUILayout.Space(5f);
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                    FlipbookEditorGUI.DrawDependencies(_data);
            }

            EditorGUILayout.Space(6f);
            if (GUILayout.Button(new GUIContent("打开 Flipbook 工作台", "打开可停靠的大图网格与事件编辑器"), GUILayout.Height(32f)))
                FlipbookWorkbenchWindow.Open(_data.Target);
        }

        /// <summary>
        ///     判断播放期间是否需要持续重绘 Inspector。
        /// </summary>
        /// <returns>运行时播放器正在播放时返回 true，否则返回 false。</returns>
        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying && _data?.Player && _data.Player.IsPlaying;
        }

        /// <summary>
        ///     将预览帧限制在当前有效帧范围内。
        /// </summary>
        private void ClampPreviewFrame()
        {
            if (_session == null) return;
            int totalFrames = _data.GetTotalFrames();
            if (totalFrames > 0 && _session.CurrentFrame > totalFrames) _session.SetFrame(totalFrames);
        }

        /// <summary>
        ///     在撤销或重做后刷新编辑数据和预览。
        /// </summary>
        private void OnUndoRedo()
        {
            if (_data == null) return;
            _data.Update();
            ClampPreviewFrame();
            if (!Application.isPlaying) _session?.SetFrame(_session.CurrentFrame);
            Repaint();
        }

        /// <summary>
        ///     在预览状态变化后重绘界面。
        /// </summary>
        private void OnPreviewChanged()
        {
            Repaint();
        }
    }

    [CustomEditor(typeof(FlipbookPlayer))]
    [CanEditMultipleObjects]
    internal sealed class FlipbookPlayerOdinEditor : FlipbookTargetEditorBase
    {
    }

    [CustomEditor(typeof(FlipbookClip))]
    [CanEditMultipleObjects]
    internal sealed class FlipbookClipOdinEditor : FlipbookTargetEditorBase
    {
    }

    [CustomEditor(typeof(FlipbookPlayerEventProxy))]
    public sealed class FlipbookPlayerEventProxyOdinEditor : OdinEditor
    {
        /// <summary>
        ///     绘制目标对象的自定义 Inspector。
        /// </summary>
        public override void OnInspectorGUI()
        {
            FlipbookPlayerEventProxy proxy = target as FlipbookPlayerEventProxy;
            if (!proxy) return;

            serializedObject.Update();
            SerializedProperty playerProperty = serializedObject.FindProperty("player");
            EditorGUILayout.PropertyField(playerProperty, new GUIContent("播放器"));
            serializedObject.ApplyModifiedProperties();

            FlipbookPlayer player = playerProperty.objectReferenceValue as FlipbookPlayer;
            int totalFrames = player ? player.GetTotalFrames() : 0;
            int selectedFrame = player ? Mathf.Max(1, player.CurrentFrameNumber) : 1;

            FlipbookEditorGUI.BeginSection("完成事件", SdfIconType.CheckCircleFill);
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("onCompleted"), new GUIContent("播放完成"));
            serializedObject.ApplyModifiedProperties();
            FlipbookEditorGUI.EndSection();

            EditorGUILayout.Space(5f);
            FlipbookEditorGUI.DrawEventList(proxy, totalFrames, selectedFrame);

            if (player)
            {
                EditorGUILayout.Space(6f);
                if (GUILayout.Button("打开 Flipbook 工作台", GUILayout.Height(30f)))
                    FlipbookWorkbenchWindow.Open(player);
            }
        }
    }
}
