using NiumaAction.Config;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace NiumaAction.Editor
{
    [CustomEditor(typeof(ComboTreeAsset))]
    public sealed class ComboTreeAssetInspector : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            var openButton = new Button(() => ComboTreeAssetEditorWindow.Open((ComboTreeAsset)target))
            {
                text = "打开连招树可视化编辑器"
            };
            root.Add(openButton);

            root.Add(new HelpBox(
                "可视化编辑器用于查看 ComboNode、Transition 连线和配置校验；字段仍通过 Unity SerializedProperty 写入，支持 Undo / Dirty。",
                HelpBoxMessageType.Info));

            AddProperty(root, "ComboTreeId", "ComboTreeId");
            AddProperty(root, "DisplayName", "显示名称");
            AddProperty(root, "StartNodeId", "起手节点 ID");
            AddProperty(root, "InputBufferSeconds", "输入缓冲时间");
            AddProperty(root, "Nodes", "连招节点");
            return root;
        }

        private void AddProperty(VisualElement root, string propertyName, string label)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                root.Add(new HelpBox($"找不到字段：{propertyName}", HelpBoxMessageType.Warning));
                return;
            }

            var field = new PropertyField(property, label);
            field.BindProperty(property);
            root.Add(field);
        }
    }
}
