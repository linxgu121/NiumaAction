using System;
using System.Collections.Generic;
using System.Linq;
using NiumaAction.Config;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NiumaAction.Editor
{
    public sealed class ComboTreeAssetEditorWindow : EditorWindow
    {
        private const double GraphRefreshDelaySeconds = 0.3d;

        [SerializeField] private ComboTreeAsset currentAsset;

        private SerializedObject serializedAsset;
        private readonly List<ComboTreeNodeListItem> nodeItems = new List<ComboTreeNodeListItem>();
        private ComboTreeGraphView graphView;
        private ListView nodeListView;
        private ScrollView detailPanel;
        private VisualElement validationContainer;
        private int selectedNodeIndex = -1;
        private bool graphRefreshPending;
        private double graphRefreshAt;

        [MenuItem("Tools/NiumaAction/Combo Tree Editor")]
        public static void ShowWindow()
        {
            var window = GetWindow<ComboTreeAssetEditorWindow>("Combo Tree Editor");
            window.minSize = new Vector2(980f, 620f);
            window.Show();
        }

        public static void Open(ComboTreeAsset asset)
        {
            var window = GetWindow<ComboTreeAssetEditorWindow>("Combo Tree Editor");
            window.minSize = new Vector2(980f, 620f);
            window.SetAsset(asset);
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed -= HandleUndoRedo;
            Undo.undoRedoPerformed += HandleUndoRedo;

            if (currentAsset != null && serializedAsset == null)
            {
                serializedAsset = new SerializedObject(currentAsset);
            }
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= HandleUndoRedo;
        }

        private void Update()
        {
            if (!graphRefreshPending || EditorApplication.timeSinceStartup < graphRefreshAt)
            {
                return;
            }

            graphRefreshPending = false;
            RebuildDataViews();
        }

        public void CreateGUI()
        {
            BuildRoot();
        }

        private void SetAsset(ComboTreeAsset asset)
        {
            currentAsset = asset;
            serializedAsset = currentAsset != null ? new SerializedObject(currentAsset) : null;
            selectedNodeIndex = -1;
            BuildRoot();
        }

        private void BuildRoot()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.flexDirection = FlexDirection.Column;
            root.style.flexGrow = 1f;

            BuildToolbar(root);

            if (currentAsset == null)
            {
                root.Add(new HelpBox("请选择一个 ComboTreeAsset，或在 Project 面板选中资产后点击 Inspector 里的“打开连招树可视化编辑器”。", HelpBoxMessageType.Info));
                return;
            }

            serializedAsset ??= new SerializedObject(currentAsset);
            serializedAsset.Update();

            var main = new VisualElement
            {
                name = "ComboTreeMainLayout"
            };
            main.style.flexDirection = FlexDirection.Row;
            main.style.flexGrow = 1f;
            main.style.minHeight = 360f;
            root.Add(main);

            BuildLeftPanel(main);
            BuildGraphPanel(main);
            BuildDetailPanel(main);
            BuildValidationPanel(root);
            RebuildDataViews();
        }

        private void BuildToolbar(VisualElement root)
        {
            var toolbar = new Toolbar();
            root.Add(toolbar);

            var assetField = new ObjectField("ComboTreeAsset")
            {
                objectType = typeof(ComboTreeAsset),
                allowSceneObjects = false,
                value = currentAsset
            };
            assetField.style.minWidth = 260f;
            assetField.RegisterValueChangedCallback(evt => SetAsset(evt.newValue as ComboTreeAsset));
            toolbar.Add(assetField);

            toolbar.Add(new ToolbarButton(AddNode) { text = "添加节点" });
            toolbar.Add(new ToolbarButton(DuplicateSelectedNode) { text = "复制节点" });
            toolbar.Add(new ToolbarButton(DeleteSelectedNode) { text = "删除节点" });
            toolbar.Add(new ToolbarButton(MoveSelectedNodeUp) { text = "上移" });
            toolbar.Add(new ToolbarButton(MoveSelectedNodeDown) { text = "下移" });
            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(new ToolbarButton(FocusStartNode) { text = "聚焦起手" });
            toolbar.Add(new ToolbarButton(RebuildDataViews) { text = "重建图" });
            toolbar.Add(new ToolbarButton(() => graphView?.FrameAll()) { text = "Fit All" });
        }

        private void BuildLeftPanel(VisualElement parent)
        {
            var left = new VisualElement
            {
                name = "ComboTreeLeftPanel"
            };
            left.style.width = 280f;
            left.style.minWidth = 240f;
            left.style.flexShrink = 0f;
            left.style.paddingLeft = 6f;
            left.style.paddingRight = 6f;
            left.style.paddingBottom = 6f;
            parent.Add(left);

            var assetFoldout = new Foldout
            {
                text = "资产信息",
                value = true
            };
            left.Add(assetFoldout);
            AddSerializedProperty(assetFoldout, "ComboTreeId", "ComboTreeId");
            AddSerializedProperty(assetFoldout, "DisplayName", "显示名称");
            AddSerializedProperty(assetFoldout, "StartNodeId", "起手节点 ID");
            AddSerializedProperty(assetFoldout, "InputBufferSeconds", "默认输入缓冲时间");

            left.Add(new Label("节点列表"));
            nodeListView = new ListView
            {
                fixedItemHeight = 42f,
                selectionType = SelectionType.Single,
                itemsSource = nodeItems,
                makeItem = MakeNodeListItem,
                bindItem = BindNodeListItemInstance
            };
            nodeListView.style.flexGrow = 1f;
            nodeListView.selectionChanged += OnNodeListSelectionChanged;
            left.Add(nodeListView);
        }

        private void BuildGraphPanel(VisualElement parent)
        {
            var center = new VisualElement
            {
                name = "ComboTreeGraphPanel"
            };
            center.style.flexGrow = 1f;
            center.style.minWidth = 420f;
            parent.Add(center);

            graphView = new ComboTreeGraphView();
            graphView.NodeSelected += index => SelectNode(index, true);
            center.Add(graphView);
        }

        private void BuildDetailPanel(VisualElement parent)
        {
            detailPanel = new ScrollView
            {
                name = "ComboTreeDetailPanel"
            };
            detailPanel.style.width = 390f;
            detailPanel.style.minWidth = 320f;
            detailPanel.style.flexShrink = 0f;
            detailPanel.style.paddingLeft = 8f;
            detailPanel.style.paddingRight = 8f;
            parent.Add(detailPanel);
        }

        private void BuildValidationPanel(VisualElement root)
        {
            validationContainer = new VisualElement
            {
                name = "ComboTreeValidationPanel"
            };
            validationContainer.style.maxHeight = 180f;
            validationContainer.style.flexShrink = 0f;
            root.Add(validationContainer);
            RefreshValidationPanel();
        }

        private void RebuildDataViews()
        {
            if (serializedAsset == null)
            {
                return;
            }

            serializedAsset.Update();
            RefreshNodeItems();
            graphView?.Rebuild(currentAsset, selectedNodeIndex);
            graphView?.SelectNode(selectedNodeIndex);
            RebuildDetails();
            RefreshValidationPanel();
        }

        private void RefreshNodeItems()
        {
            nodeItems.Clear();
            var nodes = serializedAsset.FindProperty("Nodes");
            if (nodes != null && nodes.isArray)
            {
                for (var i = 0; i < nodes.arraySize; i++)
                {
                    nodeItems.Add(BuildNodeListItem(nodes.GetArrayElementAtIndex(i), i));
                }
            }

            nodeListView?.Rebuild();
            ClampSelectedNodeIndex();
            if (nodeListView != null)
            {
                nodeListView.selectedIndex = selectedNodeIndex;
            }
        }

        private void RebuildDetails()
        {
            detailPanel.Clear();
            if (serializedAsset == null)
            {
                detailPanel.Add(new HelpBox("未选择资产。", HelpBoxMessageType.Info));
                return;
            }

            var nodes = serializedAsset.FindProperty("Nodes");
            if (nodes == null || !nodes.isArray || nodes.arraySize == 0)
            {
                detailPanel.Add(new HelpBox("当前连招树没有节点。点击左上角“添加节点”创建第一段动作。", HelpBoxMessageType.Info));
                return;
            }

            if (selectedNodeIndex < 0 || selectedNodeIndex >= nodes.arraySize)
            {
                detailPanel.Add(new HelpBox("请选择一个节点以编辑 Action、取消窗口和 Transition。", HelpBoxMessageType.Info));
                return;
            }

            var node = nodes.GetArrayElementAtIndex(selectedNodeIndex);
            detailPanel.Add(new Label($"节点详情 #{selectedNodeIndex + 1}"));
            AddRelativeProperty(detailPanel, node, "NodeId", "NodeId");
            AddRelativeProperty(detailPanel, node, "Action", "ComboAction");
            AddRelativeProperty(detailPanel, node, "CancelWindowStart01", "取消窗口开始 0-1");
            AddRelativeProperty(detailPanel, node, "CancelWindowEnd01", "取消窗口结束 0-1");
            detailPanel.Add(new HelpBox("Transitions 表示当前节点可以接到哪些下一段。TargetNodeId 必须填写目标节点的 NodeId。", HelpBoxMessageType.Info));
            AddRelativeProperty(detailPanel, node, "Transitions", "Transitions");
        }

        private void RefreshValidationPanel()
        {
            if (validationContainer == null)
            {
                return;
            }

            validationContainer.Clear();
            var results = ComboTreeValidationUtility.Validate(currentAsset);
            var hasProblem = results.Any(result => result.Severity != ComboTreeValidationSeverity.Info);
            var foldout = new Foldout
            {
                text = BuildValidationTitle(results),
                value = hasProblem
            };
            validationContainer.Add(foldout);

            var scroll = new ScrollView();
            scroll.style.maxHeight = 145f;
            foldout.Add(scroll);

            foreach (var result in results)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;

                var label = new Label($"[{result.Severity}] {result.Code}: {result.Message}");
                label.style.flexGrow = 1f;
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.color = ResolveValidationColor(result.Severity);
                row.Add(label);

                if (result.NodeIndex >= 0)
                {
                    var focusButton = new Button(() => SelectNode(result.NodeIndex, true))
                    {
                        text = $"聚焦 #{result.NodeIndex + 1}"
                    };
                    row.Add(focusButton);
                }

                scroll.Add(row);
            }
        }

        private void AddSerializedProperty(VisualElement parent, string propertyName, string label)
        {
            var property = serializedAsset.FindProperty(propertyName);
            if (property == null)
            {
                parent.Add(new HelpBox($"找不到字段：{propertyName}", HelpBoxMessageType.Warning));
                return;
            }

            AddPropertyField(parent, property, label);
        }

        private void AddRelativeProperty(VisualElement parent, SerializedProperty owner, string propertyName, string label)
        {
            var property = owner.FindPropertyRelative(propertyName);
            if (property == null)
            {
                parent.Add(new HelpBox($"找不到字段：{propertyName}", HelpBoxMessageType.Warning));
                return;
            }

            AddPropertyField(parent, property, label);
        }

        private void AddPropertyField(VisualElement parent, SerializedProperty property, string label)
        {
            var field = new PropertyField(property, label);
            field.BindProperty(property);
            field.RegisterCallback<SerializedPropertyChangeEvent>(_ => ScheduleDataRefresh());
            parent.Add(field);
        }

        private static VisualElement MakeNodeListItem()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            root.style.justifyContent = Justify.Center;
            root.style.paddingLeft = 4f;
            root.Add(new Label { name = "Title" });
            root.Add(new Label { name = "Subtitle" });
            return root;
        }

        private void BindNodeListItemInstance(VisualElement element, int index)
        {
            if (index < 0 || index >= nodeItems.Count)
            {
                return;
            }

            var item = nodeItems[index];
            var title = element.Q<Label>("Title");
            var subtitle = element.Q<Label>("Subtitle");
            title.text = item.Title;
            subtitle.text = item.Subtitle;
        }

        private ComboTreeNodeListItem BuildNodeListItem(SerializedProperty node, int index)
        {
            var nodeId = node.FindPropertyRelative("NodeId")?.stringValue;
            var action = node.FindPropertyRelative("Action")?.objectReferenceValue as ComboAction;
            var transitions = node.FindPropertyRelative("Transitions");
            var title = string.IsNullOrWhiteSpace(nodeId) ? $"#{index + 1} 未命名节点" : $"#{index + 1} {nodeId}";
            var actionName = ResolveActionLabel(action);
            return new ComboTreeNodeListItem
            {
                Index = index,
                Title = title,
                Subtitle = $"Action: {actionName} | Transitions: {transitions?.arraySize ?? 0}"
            };
        }

        private void OnNodeListSelectionChanged(IEnumerable<object> selected)
        {
            var item = selected.OfType<ComboTreeNodeListItem>().FirstOrDefault();
            if (item == null)
            {
                return;
            }

            SelectNode(item.Index, false);
        }

        private void SelectNode(int index, bool focusGraph)
        {
            selectedNodeIndex = index;
            ClampSelectedNodeIndex();

            if (nodeListView != null && nodeListView.selectedIndex != selectedNodeIndex)
            {
                nodeListView.selectedIndex = selectedNodeIndex;
            }

            graphView?.SelectNode(selectedNodeIndex);
            if (focusGraph)
            {
                graphView?.FocusNode(selectedNodeIndex);
            }

            RebuildDetails();
        }

        private void AddNode()
        {
            if (!EnsureAssetReady())
            {
                return;
            }

            var nodes = serializedAsset.FindProperty("Nodes");
            var index = nodes.arraySize;
            nodes.InsertArrayElementAtIndex(index);
            InitializeNode(nodes.GetArrayElementAtIndex(index), MakeUniqueNodeId("node"));
            serializedAsset.ApplyModifiedProperties();
            selectedNodeIndex = index;
            RebuildDataViews();
        }

        private void DuplicateSelectedNode()
        {
            if (!EnsureAssetReady())
            {
                return;
            }

            var nodes = serializedAsset.FindProperty("Nodes");
            if (selectedNodeIndex < 0 || selectedNodeIndex >= nodes.arraySize)
            {
                return;
            }

            var insertIndex = selectedNodeIndex + 1;
            nodes.InsertArrayElementAtIndex(insertIndex);
            CopyNode(nodes.GetArrayElementAtIndex(selectedNodeIndex), nodes.GetArrayElementAtIndex(insertIndex));
            var duplicate = nodes.GetArrayElementAtIndex(insertIndex);
            var originalId = duplicate.FindPropertyRelative("NodeId")?.stringValue;
            SetString(duplicate, "NodeId", MakeUniqueNodeId(string.IsNullOrWhiteSpace(originalId) ? "node" : $"{originalId}_copy"));
            serializedAsset.ApplyModifiedProperties();
            selectedNodeIndex = insertIndex;
            RebuildDataViews();
        }

        private void DeleteSelectedNode()
        {
            if (!EnsureAssetReady())
            {
                return;
            }

            var nodes = serializedAsset.FindProperty("Nodes");
            if (selectedNodeIndex < 0 || selectedNodeIndex >= nodes.arraySize)
            {
                return;
            }

            nodes.DeleteArrayElementAtIndex(selectedNodeIndex);
            serializedAsset.ApplyModifiedProperties();
            selectedNodeIndex = Mathf.Min(selectedNodeIndex, nodes.arraySize - 1);
            RebuildDataViews();
        }

        private void MoveSelectedNodeUp()
        {
            MoveSelectedNode(-1);
        }

        private void MoveSelectedNodeDown()
        {
            MoveSelectedNode(1);
        }

        private void MoveSelectedNode(int direction)
        {
            if (!EnsureAssetReady())
            {
                return;
            }

            var nodes = serializedAsset.FindProperty("Nodes");
            var nextIndex = selectedNodeIndex + direction;
            if (selectedNodeIndex < 0 || selectedNodeIndex >= nodes.arraySize || nextIndex < 0 || nextIndex >= nodes.arraySize)
            {
                return;
            }

            nodes.MoveArrayElement(selectedNodeIndex, nextIndex);
            serializedAsset.ApplyModifiedProperties();
            selectedNodeIndex = nextIndex;
            RebuildDataViews();
        }

        private void FocusStartNode()
        {
            if (currentAsset == null)
            {
                return;
            }

            var nodes = currentAsset.Nodes;
            if (nodes == null || nodes.Length == 0)
            {
                return;
            }

            var startNodeId = string.IsNullOrWhiteSpace(currentAsset.StartNodeId) ? nodes[0]?.NodeId : currentAsset.StartNodeId;
            var index = Array.FindIndex(nodes, node => node != null && string.Equals(node.NodeId, startNodeId, StringComparison.Ordinal));
            SelectNode(index >= 0 ? index : 0, true);
        }

        private void ScheduleDataRefresh()
        {
            serializedAsset?.ApplyModifiedProperties();
            graphRefreshPending = true;
            graphRefreshAt = EditorApplication.timeSinceStartup + GraphRefreshDelaySeconds;
            RefreshNodeItems();
            RefreshValidationPanel();
        }

        private void HandleUndoRedo()
        {
            if (currentAsset == null)
            {
                return;
            }

            // Undo/Redo 不一定触发 PropertyField 的变更回调，因此这里主动重建窗口数据。
            serializedAsset = new SerializedObject(currentAsset);
            BuildRoot();
        }

        private bool EnsureAssetReady()
        {
            if (currentAsset == null)
            {
                return false;
            }

            serializedAsset ??= new SerializedObject(currentAsset);
            serializedAsset.Update();
            return true;
        }

        private void ClampSelectedNodeIndex()
        {
            var count = nodeItems.Count;
            if (count <= 0)
            {
                selectedNodeIndex = -1;
                return;
            }

            if (selectedNodeIndex < 0)
            {
                selectedNodeIndex = 0;
            }
            else if (selectedNodeIndex >= count)
            {
                selectedNodeIndex = count - 1;
            }
        }

        private void InitializeNode(SerializedProperty node, string nodeId)
        {
            SetString(node, "NodeId", nodeId);
            SetObject(node, "Action", null);
            SetFloat(node, "CancelWindowStart01", 0.55f);
            SetFloat(node, "CancelWindowEnd01", 0.9f);

            var transitions = node.FindPropertyRelative("Transitions");
            if (transitions != null && transitions.isArray)
            {
                transitions.arraySize = 0;
            }
        }

        private static void CopyNode(SerializedProperty source, SerializedProperty target)
        {
            SetString(target, "NodeId", source.FindPropertyRelative("NodeId")?.stringValue);
            SetObject(target, "Action", source.FindPropertyRelative("Action")?.objectReferenceValue);
            SetFloat(target, "CancelWindowStart01", source.FindPropertyRelative("CancelWindowStart01")?.floatValue ?? 0f);
            SetFloat(target, "CancelWindowEnd01", source.FindPropertyRelative("CancelWindowEnd01")?.floatValue ?? 0f);
            CopyTransitions(source.FindPropertyRelative("Transitions"), target.FindPropertyRelative("Transitions"));
        }

        private static void CopyTransitions(SerializedProperty source, SerializedProperty target)
        {
            if (source == null || target == null || !source.isArray || !target.isArray)
            {
                return;
            }

            target.arraySize = source.arraySize;
            for (var i = 0; i < source.arraySize; i++)
            {
                var sourceTransition = source.GetArrayElementAtIndex(i);
                var targetTransition = target.GetArrayElementAtIndex(i);
                SetString(targetTransition, "InputId", sourceTransition.FindPropertyRelative("InputId")?.stringValue);
                SetString(targetTransition, "TargetNodeId", sourceTransition.FindPropertyRelative("TargetNodeId")?.stringValue);
                SetBool(targetTransition, "RequireCancelWindow", sourceTransition.FindPropertyRelative("RequireCancelWindow")?.boolValue ?? true);
                SetFloat(targetTransition, "InputBufferSecondsOverride", sourceTransition.FindPropertyRelative("InputBufferSecondsOverride")?.floatValue ?? 0f);
                SetFloat(targetTransition, "MinBufferedSeconds", sourceTransition.FindPropertyRelative("MinBufferedSeconds")?.floatValue ?? 0f);
                CopyConditions(sourceTransition.FindPropertyRelative("Conditions"), targetTransition.FindPropertyRelative("Conditions"));
            }
        }

        private static void CopyConditions(SerializedProperty source, SerializedProperty target)
        {
            if (source == null || target == null || !source.isArray || !target.isArray)
            {
                return;
            }

            target.arraySize = source.arraySize;
            for (var i = 0; i < source.arraySize; i++)
            {
                var sourceCondition = source.GetArrayElementAtIndex(i);
                var targetCondition = target.GetArrayElementAtIndex(i);
                SetString(targetCondition, "ConditionId", sourceCondition.FindPropertyRelative("ConditionId")?.stringValue);
                SetEnum(targetCondition, "Type", sourceCondition.FindPropertyRelative("Type")?.enumValueIndex ?? 0);
                SetString(targetCondition, "TargetId", sourceCondition.FindPropertyRelative("TargetId")?.stringValue);
                SetString(targetCondition, "StringValue", sourceCondition.FindPropertyRelative("StringValue")?.stringValue);
                SetFloat(targetCondition, "FloatValue", sourceCondition.FindPropertyRelative("FloatValue")?.floatValue ?? 0f);
                SetBool(targetCondition, "BoolValue", sourceCondition.FindPropertyRelative("BoolValue")?.boolValue ?? false);
            }
        }

        private string MakeUniqueNodeId(string seed)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            var nodes = serializedAsset?.FindProperty("Nodes");
            if (nodes != null && nodes.isArray)
            {
                for (var i = 0; i < nodes.arraySize; i++)
                {
                    var id = nodes.GetArrayElementAtIndex(i).FindPropertyRelative("NodeId")?.stringValue;
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        used.Add(id);
                    }
                }
            }

            var normalizedSeed = string.IsNullOrWhiteSpace(seed) ? "node" : seed.Trim();
            if (!used.Contains(normalizedSeed))
            {
                return normalizedSeed;
            }

            for (var i = 1; i < 1000; i++)
            {
                var candidate = $"{normalizedSeed}_{i:00}";
                if (!used.Contains(candidate))
                {
                    return candidate;
                }
            }

            return $"{normalizedSeed}_{Guid.NewGuid():N}";
        }

        private static void SetString(SerializedProperty owner, string name, string value)
        {
            var property = owner.FindPropertyRelative(name);
            if (property != null)
            {
                property.stringValue = value;
            }
        }

        private static void SetFloat(SerializedProperty owner, string name, float value)
        {
            var property = owner.FindPropertyRelative(name);
            if (property != null)
            {
                property.floatValue = value;
            }
        }

        private static void SetBool(SerializedProperty owner, string name, bool value)
        {
            var property = owner.FindPropertyRelative(name);
            if (property != null)
            {
                property.boolValue = value;
            }
        }

        private static void SetEnum(SerializedProperty owner, string name, int value)
        {
            var property = owner.FindPropertyRelative(name);
            if (property != null)
            {
                property.enumValueIndex = value;
            }
        }

        private static void SetObject(SerializedProperty owner, string name, UnityEngine.Object value)
        {
            var property = owner.FindPropertyRelative(name);
            if (property != null)
            {
                property.objectReferenceValue = value;
            }
        }

        private static string BuildValidationTitle(IReadOnlyCollection<ComboTreeValidationResult> results)
        {
            var errors = results.Count(result => result.Severity == ComboTreeValidationSeverity.Error);
            var warnings = results.Count(result => result.Severity == ComboTreeValidationSeverity.Warning);
            var infos = results.Count(result => result.Severity == ComboTreeValidationSeverity.Info);
            return $"校验结果：Error {errors} / Warning {warnings} / Info {infos}";
        }

        private static Color ResolveValidationColor(ComboTreeValidationSeverity severity)
        {
            switch (severity)
            {
                case ComboTreeValidationSeverity.Error:
                    return new Color(1f, 0.36f, 0.32f);
                case ComboTreeValidationSeverity.Warning:
                    return new Color(1f, 0.7f, 0.22f);
                default:
                    return new Color(0.62f, 0.78f, 1f);
            }
        }

        private static string ResolveActionLabel(ComboAction action)
        {
            if (action == null)
            {
                return "未绑定";
            }

            if (!string.IsNullOrWhiteSpace(action.DisplayName))
            {
                return action.DisplayName;
            }

            if (!string.IsNullOrWhiteSpace(action.ActionId))
            {
                return action.ActionId;
            }

            return action.name;
        }

        private sealed class ComboTreeNodeListItem
        {
            public int Index;
            public string Title;
            public string Subtitle;
        }
    }
}
