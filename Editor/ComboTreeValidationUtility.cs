using System.Collections.Generic;
using NiumaAction.Config;
using NiumaAction.Enum;

namespace NiumaAction.Editor
{
    internal enum ComboTreeValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    internal sealed class ComboTreeValidationResult
    {
        public ComboTreeValidationSeverity Severity;
        public string Code;
        public string Message;
        public int NodeIndex = -1;
    }

    internal static class ComboTreeValidationUtility
    {
        public static List<ComboTreeValidationResult> Validate(ComboTreeAsset asset)
        {
            var results = new List<ComboTreeValidationResult>();
            if (asset == null)
            {
                results.Add(Error("asset_missing", "未选择 ComboTreeAsset。"));
                return results;
            }

            if (string.IsNullOrWhiteSpace(asset.ComboTreeId))
            {
                results.Add(Error("combo_tree_id_empty", "ComboTreeId 为空。"));
            }

            var nodes = asset.Nodes;
            if (nodes == null || nodes.Length == 0)
            {
                results.Add(Warning("nodes_empty", "Nodes 为空。请先添加至少一个连招节点。"));
                return results;
            }

            var nodeIdToIndex = new Dictionary<string, int>();
            for (var i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (node == null)
                {
                    results.Add(Error("node_null", $"第 {i + 1} 个节点为空。", i));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(node.NodeId))
                {
                    results.Add(Error("node_id_empty", $"第 {i + 1} 个节点 NodeId 为空。", i));
                }
                else if (nodeIdToIndex.TryGetValue(node.NodeId, out var firstIndex))
                {
                    results.Add(Error("node_id_duplicate", $"NodeId '{node.NodeId}' 重复：第 {firstIndex + 1} 个节点与第 {i + 1} 个节点。", i));
                }
                else
                {
                    nodeIdToIndex.Add(node.NodeId, i);
                }

                if (node.Action == null)
                {
                    results.Add(Warning("node_action_missing", $"节点 '{GetNodeLabel(node, i)}' 未绑定 ComboAction。", i));
                }
                else
                {
                    ValidateComboAction(node.Action, GetNodeLabel(node, i), i, results);
                }

                if (node.CancelWindowEnd01 < node.CancelWindowStart01)
                {
                    results.Add(Error("cancel_window_invalid", $"节点 '{GetNodeLabel(node, i)}' 的取消窗口结束值小于开始值。", i));
                }
            }

            ValidateStartNode(asset, nodeIdToIndex, results);
            ValidateTransitions(nodes, nodeIdToIndex, results);

            results.Add(Info("summary", $"节点 {nodes.Length} 个，Transition {CountTransitions(nodes)} 条。"));
            return results;
        }

        private static void ValidateComboAction(
            ComboAction action,
            string nodeLabel,
            int nodeIndex,
            ICollection<ComboTreeValidationResult> results)
        {
            if (string.IsNullOrWhiteSpace(action.ActionId))
            {
                results.Add(Warning("action_id_empty", $"节点 '{nodeLabel}' 绑定的 ComboAction 未填写 ActionId。", nodeIndex));
            }

            if (action.StaminaCost < 0f)
            {
                results.Add(Warning("action_stamina_negative", $"节点 '{nodeLabel}' 的 ComboAction.StaminaCost 小于 0，保存后会被 OnValidate 修正。", nodeIndex));
            }

            if (action.DamageMultiplier < 0f)
            {
                results.Add(Warning("action_damage_multiplier_negative", $"节点 '{nodeLabel}' 的 ComboAction.DamageMultiplier 小于 0，保存后会被 OnValidate 修正。", nodeIndex));
            }

            if (action.Animate == null)
            {
                results.Add(Warning("action_animate_missing", $"节点 '{nodeLabel}' 的 ComboAction 未绑定 AnimateAsset。", nodeIndex));
                return;
            }

            ValidateAnimateAsset(action.Animate, nodeLabel, nodeIndex, results);
        }

        private static void ValidateAnimateAsset(
            AnimateAsset animate,
            string nodeLabel,
            int nodeIndex,
            ICollection<ComboTreeValidationResult> results)
        {
            if (string.IsNullOrWhiteSpace(animate.AnimateId))
            {
                results.Add(Warning("animate_id_empty", $"节点 '{nodeLabel}' 的 AnimateAsset 未填写 AnimateId。", nodeIndex));
            }

            if (animate.Clip == null)
            {
                results.Add(Warning("animate_clip_missing", $"节点 '{nodeLabel}' 的 AnimateAsset 未绑定 AnimationClip。", nodeIndex));
            }

            if (animate.DurationSeconds <= 0f)
            {
                var message = animate.Clip != null
                    ? $"节点 '{nodeLabel}' 的 AnimateAsset.DurationSeconds <= 0，运行时会回退使用 Clip.length。"
                    : $"节点 '{nodeLabel}' 的 AnimateAsset.DurationSeconds <= 0 且 Clip 为空，动作时长会变成 0。";
                results.Add(Warning("animate_duration_non_positive", message, nodeIndex));
            }

            var events = animate.TimelineEvents;
            if (events == null || events.Length == 0)
            {
                return;
            }

            for (var i = 0; i < events.Length; i++)
            {
                var timelineEvent = events[i];
                if (timelineEvent == null)
                {
                    results.Add(Error("timeline_event_null", $"节点 '{nodeLabel}' 的 AnimateAsset 第 {i + 1} 个 TimelineEvent 为空。", nodeIndex));
                    continue;
                }

                if (timelineEvent.NormalizedTime < 0f || timelineEvent.NormalizedTime > 1f)
                {
                    results.Add(Error("timeline_event_time_out_of_range", $"节点 '{nodeLabel}' 的 TimelineEvent '{ResolveTimelineEventLabel(timelineEvent, i)}' NormalizedTime 不在 0-1 范围内。", nodeIndex));
                }

                if ((timelineEvent.Type == ActionTimelineEventType.HitboxOpen ||
                     timelineEvent.Type == ActionTimelineEventType.HitboxClose) &&
                    string.IsNullOrWhiteSpace(timelineEvent.PayloadId))
                {
                    results.Add(Warning("timeline_event_payload_empty", $"节点 '{nodeLabel}' 的 Hitbox TimelineEvent '{ResolveTimelineEventLabel(timelineEvent, i)}' 未填写 PayloadId；多 Hitbox 通道或 CombatBridge 绑定时建议填写。", nodeIndex));
                }
            }
        }

        private static void ValidateStartNode(
            ComboTreeAsset asset,
            IReadOnlyDictionary<string, int> nodeIdToIndex,
            ICollection<ComboTreeValidationResult> results)
        {
            if (string.IsNullOrWhiteSpace(asset.StartNodeId))
            {
                results.Add(Info("start_node_empty", "StartNodeId 为空，运行时会使用第一个有效节点作为起手节点。"));
                return;
            }

            if (!nodeIdToIndex.ContainsKey(asset.StartNodeId))
            {
                results.Add(Error("start_node_missing", $"StartNodeId '{asset.StartNodeId}' 找不到对应节点。"));
            }
        }

        private static void ValidateTransitions(
            IReadOnlyList<NiumaAction.Data.ComboNode> nodes,
            IReadOnlyDictionary<string, int> nodeIdToIndex,
            ICollection<ComboTreeValidationResult> results)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null || node.Transitions == null)
                {
                    continue;
                }

                for (var j = 0; j < node.Transitions.Length; j++)
                {
                    var transition = node.Transitions[j];
                    if (transition == null)
                    {
                        results.Add(Error("transition_null", $"节点 '{GetNodeLabel(node, i)}' 的第 {j + 1} 条 Transition 为空。", i));
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(transition.InputId))
                    {
                        results.Add(Warning("transition_input_empty", $"节点 '{GetNodeLabel(node, i)}' 的第 {j + 1} 条 Transition 未填写 InputId。", i));
                    }

                    if (string.IsNullOrWhiteSpace(transition.TargetNodeId))
                    {
                        results.Add(Error("transition_target_empty", $"节点 '{GetNodeLabel(node, i)}' 的第 {j + 1} 条 Transition 未填写 TargetNodeId。", i));
                    }
                    else if (!nodeIdToIndex.ContainsKey(transition.TargetNodeId))
                    {
                        results.Add(Error("transition_target_missing", $"节点 '{GetNodeLabel(node, i)}' 的 Transition 指向不存在的 TargetNodeId '{transition.TargetNodeId}'。", i));
                    }

                    if (transition.MinBufferedSeconds > 0f && transition.InputBufferSecondsOverride > 0f &&
                        transition.MinBufferedSeconds > transition.InputBufferSecondsOverride)
                    {
                        results.Add(Warning("transition_buffer_window", $"节点 '{GetNodeLabel(node, i)}' 的第 {j + 1} 条 Transition：MinBufferedSeconds 大于 InputBufferSecondsOverride。", i));
                    }
                }
            }
        }

        private static int CountTransitions(IReadOnlyList<NiumaAction.Data.ComboNode> nodes)
        {
            var count = 0;
            for (var i = 0; i < nodes.Count; i++)
            {
                count += nodes[i]?.Transitions?.Length ?? 0;
            }

            return count;
        }

        private static ComboTreeValidationResult Info(string code, string message, int nodeIndex = -1)
        {
            return Result(ComboTreeValidationSeverity.Info, code, message, nodeIndex);
        }

        private static ComboTreeValidationResult Warning(string code, string message, int nodeIndex = -1)
        {
            return Result(ComboTreeValidationSeverity.Warning, code, message, nodeIndex);
        }

        private static ComboTreeValidationResult Error(string code, string message, int nodeIndex = -1)
        {
            return Result(ComboTreeValidationSeverity.Error, code, message, nodeIndex);
        }

        private static ComboTreeValidationResult Result(ComboTreeValidationSeverity severity, string code, string message, int nodeIndex)
        {
            return new ComboTreeValidationResult
            {
                Severity = severity,
                Code = code,
                Message = message,
                NodeIndex = nodeIndex
            };
        }

        private static string GetNodeLabel(NiumaAction.Data.ComboNode node, int index)
        {
            return string.IsNullOrWhiteSpace(node?.NodeId) ? $"#{index}" : node.NodeId;
        }

        private static string ResolveTimelineEventLabel(NiumaAction.Data.ActionTimelineEventData timelineEvent, int index)
        {
            if (!string.IsNullOrWhiteSpace(timelineEvent.EventId))
            {
                return timelineEvent.EventId;
            }

            return $"#{index + 1}";
        }
    }
}
