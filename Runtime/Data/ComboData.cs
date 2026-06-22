using System;
using NiumaAction.Config;

namespace NiumaAction.Data
{
    // ComboNode 内嵌在 ComboTreeAsset 中，而不是每个节点做一个 ScriptableObject，
    // 这样策划只需要维护一份紧凑的连招树资产。
    [Serializable]
    public sealed class ComboNode
    {
        public string NodeId;
        public ComboAction Action;
        public float CancelWindowStart01;
        public float CancelWindowEnd01;
        public ComboTransitionData[] Transitions = Array.Empty<ComboTransitionData>();

        public ComboNode Clone()
        {
            return new ComboNode
            {
                NodeId = NodeId,
                // ComboAction 是共享 ScriptableObject 资产，运行时只读引用，不做深拷贝。
                Action = Action,
                CancelWindowStart01 = CancelWindowStart01,
                CancelWindowEnd01 = CancelWindowEnd01,
                Transitions = CloneTransitions(Transitions)
            };
        }

        public static ComboNode[] CloneArray(ComboNode[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<ComboNode>();
            }

            var clone = new ComboNode[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                clone[i] = source[i]?.Clone();
            }

            return clone;
        }

        private static ComboTransitionData[] CloneTransitions(ComboTransitionData[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<ComboTransitionData>();
            }

            var clone = new ComboTransitionData[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                clone[i] = source[i]?.Clone();
            }

            return clone;
        }
    }

    [Serializable]
    public sealed class ComboTransitionData
    {
        public string InputId;
        public string TargetNodeId;
        // 为 true 时，只有在来源节点的取消窗口内才能消费该转换；
        // 调试输入或特殊立即分支可以设为 false。
        public bool RequireCancelWindow = true;
        public float InputBufferSecondsOverride;
        public float MinBufferedSeconds;
        public ActionConditionData[] Conditions = Array.Empty<ActionConditionData>();

        public ComboTransitionData Clone()
        {
            return new ComboTransitionData
            {
                InputId = InputId,
                TargetNodeId = TargetNodeId,
                RequireCancelWindow = RequireCancelWindow,
                InputBufferSecondsOverride = InputBufferSecondsOverride,
                MinBufferedSeconds = MinBufferedSeconds,
                Conditions = ActionConditionData.CloneArray(Conditions)
            };
        }
    }
}
