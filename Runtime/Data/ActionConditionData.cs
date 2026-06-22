using System;
using NiumaAction.Config;
using NiumaAction.Enum;

namespace NiumaAction.Data
{
    // 内置条件保持轻量；复杂项目规则应在服务阶段交给 IActionConditionResolver，
    // 避免这个可序列化数据结构持续膨胀。
    [Serializable]
    public sealed class ActionConditionData
    {
        public string ConditionId;
        public ActionConditionType Type;
        public string TargetId;
        public string StringValue;
        public float FloatValue;
        public bool BoolValue;

        public ActionConditionData Clone()
        {
            return new ActionConditionData
            {
                ConditionId = ConditionId,
                Type = Type,
                TargetId = TargetId,
                StringValue = StringValue,
                FloatValue = FloatValue,
                BoolValue = BoolValue
            };
        }

        public static ActionConditionData[] CloneArray(ActionConditionData[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<ActionConditionData>();
            }

            var clone = new ActionConditionData[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                clone[i] = source[i]?.Clone();
            }

            return clone;
        }
    }

    // 运行时条件判断上下文，不要序列化；它临时组合当前角色状态、
    // 候选转换状态和输入信息。
    public sealed class ActionConditionContext
    {
        public string ActorId;
        public string InputId;
        public MeleeWeaponSource WeaponSource;
        public ComboTreeAsset ComboTree;
        public string CurrentNodeId;
        public string CandidateNodeId;
        public ActionOwnerSnapshot Owner;
        public float CurrentNormalizedTime;
        public bool IsInCancelWindow;
    }
}
