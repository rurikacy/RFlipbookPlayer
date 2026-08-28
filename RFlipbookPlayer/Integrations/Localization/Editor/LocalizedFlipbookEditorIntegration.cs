using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace FlipbookEditorTools
{
    [InitializeOnLoad]
    internal static class LocalizedFlipbookEditorIntegration
    {
        static LocalizedFlipbookEditorIntegration()
        {
            FlipbookEditorIntegrationRegistry.Register(Draw);
        }

        private static void Draw(FlipbookPlayer player)
        {
            if (!player) return;

            EditorGUILayout.Space(4f);
            LocalizedFlipbookBinder binder = player.GetComponent<LocalizedFlipbookBinder>();
            if (!binder)
            {
                if (GUILayout.Button(
                        new GUIContent("添加多语言绑定器", "添加 LocalizedFlipbookBinder 并自动绑定播放器"),
                        GUILayout.Height(28f)))
                {
                    AddBinder(player);
                    GUIUtility.ExitGUI();
                }

                return;
            }

            SerializedObject serializedBinder = new(binder);
            serializedBinder.Update();
            EditorGUILayout.LabelField("多语言资源", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(serializedBinder.FindProperty("localizedClip"), GUIContent.none, true);
            serializedBinder.ApplyModifiedProperties();
        }

        private static LocalizedFlipbookBinder AddBinder(FlipbookPlayer player)
        {
            LocalizedFlipbookBinder existing = player.GetComponent<LocalizedFlipbookBinder>();
            if (existing) return existing;

            LocalizedFlipbookBinder binder = Undo.AddComponent<LocalizedFlipbookBinder>(player.gameObject);
            SerializedObject serializedBinder = new(binder);
            serializedBinder.FindProperty("player").objectReferenceValue = player;
            serializedBinder.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(binder);
            return binder;
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
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("多语言 Flipbook", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("localizedClip"), GUIContent.none, true);
            serializedObject.ApplyModifiedProperties();

            FlipbookPlayer player = playerProperty.objectReferenceValue as FlipbookPlayer;
            if (!player) return;

            EditorGUILayout.Space(6f);
            if (GUILayout.Button("打开 Flipbook 工作台", GUILayout.Height(30f)))
                FlipbookWorkbenchWindow.Open(player);
        }
    }
}
