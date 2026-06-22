using System;
using System.Collections.Generic;
using System.Linq;
using NiumaAction.Config;
using NiumaAction.Data;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace NiumaAction.Editor
{
    internal sealed class ComboTreeGraphView : GraphView
    {
        private const float NodeWidth = 230f;
        private const float NodeHeight = 135f;
        private const float ColumnSpacing = 290f;
        private const float RowSpacing = 190f;
        private const int NodesPerRow = 5;

        private readonly Dictionary<int, ComboTreeGraphNode> nodesByIndex = new Dictionary<int, ComboTreeGraphNode>();
        private readonly Dictionary<string, ComboTreeGraphNode> nodesById = new Dictionary<string, ComboTreeGraphNode>();
        private int selectedNodeIndex = -1;

        public event Action<int> NodeSelected;

        public ComboTreeGraphView()
        {
            style.flexGrow = 1f;
            style.backgroundColor = new Color(0.11f, 0.11f, 0.11f);

            Insert(0, new GridBackground());
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
        }

        public void Rebuild(ComboTreeAsset asset, int selectedIndex)
        {
            selectedNodeIndex = selectedIndex;
            nodesByIndex.Clear();
            nodesById.Clear();
            DeleteElements(graphElements.ToList());

            if (asset == null)
            {
                AddPlaceholder("请选择 ComboTreeAsset。");
                return;
            }

            var nodes = asset.Nodes;
            if (nodes == null || nodes.Length == 0)
            {
                AddPlaceholder("当前连招树没有节点。请在左侧点击“添加节点”。");
                return;
            }

            BuildNodes(nodes, ResolveStartNodeId(asset));
            BuildEdges(nodes);
        }

        public void SelectNode(int index)
        {
            selectedNodeIndex = index;
            foreach (var pair in nodesByIndex)
            {
                pair.Value.SetSelectedVisual(pair.Key == selectedNodeIndex);
            }
        }

        public void FocusNode(int index)
        {
            if (!nodesByIndex.TryGetValue(index, out var node))
            {
                return;
            }

            ClearSelection();
            AddToSelection(node);
            node.BringToFront();
            FrameSelection();
        }

        private void BuildNodes(IReadOnlyList<ComboNode> nodes, string startNodeId)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                var nodeData = nodes[i];
                if (nodeData == null)
                {
                    continue;
                }

                var graphNode = new ComboTreeGraphNode(i, nodeData, IsStartNode(nodeData, i, startNodeId), OnNodeClicked);
                graphNode.SetPosition(CalculateNodeRect(i));
                graphNode.SetSelectedVisual(i == selectedNodeIndex);

                AddElement(graphNode);
                nodesByIndex[i] = graphNode;

                if (!string.IsNullOrWhiteSpace(nodeData.NodeId) && !nodesById.ContainsKey(nodeData.NodeId))
                {
                    nodesById.Add(nodeData.NodeId, graphNode);
                }
            }
        }

        private void BuildEdges(IReadOnlyList<ComboNode> nodes)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                var sourceData = nodes[i];
                if (sourceData?.Transitions == null || !nodesByIndex.TryGetValue(i, out var sourceNode))
                {
                    continue;
                }

                for (var j = 0; j < sourceData.Transitions.Length; j++)
                {
                    var transition = sourceData.Transitions[j];
                    if (transition == null ||
                        string.IsNullOrWhiteSpace(transition.TargetNodeId) ||
                        !nodesById.TryGetValue(transition.TargetNodeId, out var targetNode))
                    {
                        continue;
                    }

                    var edge = sourceNode.OutputPort.ConnectTo(targetNode.InputPort);
                    edge.tooltip = BuildTransitionTooltip(transition);
                    edge.capabilities &= ~Capabilities.Deletable;
                    AddElement(edge);
                }
            }
        }

        private static Rect CalculateNodeRect(int index)
        {
            var column = index % NodesPerRow;
            var row = index / NodesPerRow;
            return new Rect(80f + column * ColumnSpacing, 90f + row * RowSpacing, NodeWidth, NodeHeight);
        }

        private static string ResolveStartNodeId(ComboTreeAsset asset)
        {
            if (!string.IsNullOrWhiteSpace(asset.StartNodeId))
            {
                return asset.StartNodeId;
            }

            var nodes = asset.Nodes;
            if (nodes == null || nodes.Length == 0)
            {
                return string.Empty;
            }

            return nodes[0]?.NodeId ?? string.Empty;
        }

        private static bool IsStartNode(ComboNode node, int index, string startNodeId)
        {
            if (!string.IsNullOrWhiteSpace(startNodeId))
            {
                return string.Equals(node.NodeId, startNodeId, StringComparison.Ordinal);
            }

            return index == 0;
        }

        private static string BuildTransitionTooltip(ComboTransitionData transition)
        {
            return $"输入：{transition.InputId}\n目标：{transition.TargetNodeId}\n需要取消窗口：{transition.RequireCancelWindow}";
        }

        private void OnNodeClicked(int index)
        {
            NodeSelected?.Invoke(index);
        }

        private void AddPlaceholder(string message)
        {
            var placeholder = new Node
            {
                title = "ComboTree"
            };
            placeholder.capabilities &= ~Capabilities.Deletable;
            placeholder.extensionContainer.Add(new Label(message));
            placeholder.RefreshExpandedState();
            placeholder.RefreshPorts();
            placeholder.SetPosition(new Rect(120f, 120f, 340f, 100f));
            AddElement(placeholder);
        }
    }

    internal sealed class ComboTreeGraphNode : Node
    {
        public readonly int Index;
        public readonly Port InputPort;
        public readonly Port OutputPort;

        private readonly Color normalBorderColor = new Color(0.27f, 0.27f, 0.27f);
        private readonly Color selectedBorderColor = new Color(1f, 0.78f, 0.28f);

        public ComboTreeGraphNode(int index, ComboNode node, bool isStartNode, Action<int> onClicked)
        {
            Index = index;
            title = BuildTitle(index, node, isStartNode);
            capabilities &= ~Capabilities.Deletable;
            capabilities &= ~Capabilities.Movable;

            InputPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
            InputPort.portName = string.Empty;
            inputContainer.Add(InputPort);

            OutputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
            OutputPort.portName = string.Empty;
            outputContainer.Add(OutputPort);

            AddNodeSummary(node);
            RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                onClicked?.Invoke(Index);
            });

            RefreshExpandedState();
            RefreshPorts();
        }

        public void SetSelectedVisual(bool selected)
        {
            style.borderTopWidth = 2f;
            style.borderBottomWidth = 2f;
            style.borderLeftWidth = 2f;
            style.borderRightWidth = 2f;
            style.borderTopColor = selected ? selectedBorderColor : normalBorderColor;
            style.borderBottomColor = selected ? selectedBorderColor : normalBorderColor;
            style.borderLeftColor = selected ? selectedBorderColor : normalBorderColor;
            style.borderRightColor = selected ? selectedBorderColor : normalBorderColor;
        }

        private void AddNodeSummary(ComboNode node)
        {
            extensionContainer.Add(new Label($"Action：{ResolveActionLabel(node)}"));
            extensionContainer.Add(new Label($"取消窗口：{node.CancelWindowStart01:0.##} - {node.CancelWindowEnd01:0.##}"));

            var transitions = node.Transitions;
            if (transitions == null || transitions.Length == 0)
            {
                extensionContainer.Add(new Label("Transitions：无"));
                return;
            }

            for (var i = 0; i < transitions.Length; i++)
            {
                var transition = transitions[i];
                if (transition == null)
                {
                    extensionContainer.Add(new Label($"→ #{i + 1}：空 Transition"));
                    continue;
                }

                extensionContainer.Add(new Label($"→ {transition.TargetNodeId} / 输入 {transition.InputId}"));
            }
        }

        private static string BuildTitle(int index, ComboNode node, bool isStartNode)
        {
            var id = string.IsNullOrWhiteSpace(node.NodeId) ? $"未命名节点 {index + 1}" : node.NodeId;
            return isStartNode ? $"起手：{id}" : id;
        }

        private static string ResolveActionLabel(ComboNode node)
        {
            if (node.Action == null)
            {
                return "未绑定";
            }

            if (!string.IsNullOrWhiteSpace(node.Action.DisplayName))
            {
                return node.Action.DisplayName;
            }

            if (!string.IsNullOrWhiteSpace(node.Action.ActionId))
            {
                return node.Action.ActionId;
            }

            return node.Action.name;
        }
    }
}
