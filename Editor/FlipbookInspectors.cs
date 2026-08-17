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

        protected FlipbookEditorData Data => _data;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (targets.Length != 1 || !target) return;

            _data = new FlipbookEditorData(target);
            _session = FlipbookPreviewSessions.Acquire(target);
            _session.Changed += OnPreviewChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        protected override void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            if (_session != null) _session.Changed -= OnPreviewChanged;
            FlipbookPreviewSessions.Release(_session);
            _session = null;
            _data = null;
            base.OnDisable();
        }

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

        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying && _data?.Player && _data.Player.IsPlaying;
        }

        private void ClampPreviewFrame()
        {
            if (_session == null) return;
            int totalFrames = _data.GetTotalFrames();
            if (totalFrames > 0 && _session.CurrentFrame > totalFrames) _session.SetFrame(totalFrames);
        }

        private void OnUndoRedo()
        {
            if (_data == null) return;
            _data.Update();
            ClampPreviewFrame();
            if (!Application.isPlaying) _session?.SetFrame(_session.CurrentFrame);
            Repaint();
        }

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

    [CustomEditor(typeof(LocalizedFlipbookBinder))]
    public sealed class LocalizedFlipbookBinderOdinEditor : OdinEditor
    {
        public override void OnInspectorGUI()
        {
            LocalizedFlipbookBinder binder = target as LocalizedFlipbookBinder;
            if (!binder) return;

            serializedObject.Update();
            SerializedProperty playerProperty = serializedObject.FindProperty("player");
            EditorGUILayout.PropertyField(playerProperty, new GUIContent("播放器"));

            FlipbookEditorGUI.BeginSection("多语言 Flipbook", SdfIconType.Translate);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("localizedClip"), GUIContent.none, true);
            FlipbookEditorGUI.EndSection();
            serializedObject.ApplyModifiedProperties();

            FlipbookPlayer player = playerProperty.objectReferenceValue as FlipbookPlayer;
            if (player)
            {
                EditorGUILayout.Space(6f);
                if (GUILayout.Button("打开 Flipbook 工作台", GUILayout.Height(30f)))
                    FlipbookWorkbenchWindow.Open(player);
            }
        }
    }
}