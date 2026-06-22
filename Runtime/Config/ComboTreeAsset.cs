using System;
using NiumaAction.Data;
using UnityEngine;

namespace NiumaAction.Config
{
    // ComboTreeAsset 只负责连招结构：入口节点、转换、取消窗口和输入缓冲。
    // 它不保存运行时角色状态，也不直接播放动画或产出 Combat 伤害事实。
    [CreateAssetMenu(menuName = "NiumaAction/Combo Tree", fileName = "ComboTreeAsset")]
    public sealed class ComboTreeAsset : ScriptableObject
    {
        [Tooltip("连招树稳定 ID。一个武器通常引用一个默认连招树。")]
        public string ComboTreeId;

        [Tooltip("显示名称，仅用于策划查看和调试。")]
        public string DisplayName;

        [Tooltip("起始节点 ID。为空时使用第一个有效节点。")]
        public string StartNodeId;

        [Min(0f)]
        [Tooltip("默认输入缓冲时间。Transition 的 InputBufferSecondsOverride > 0 时会覆盖它。")]
        public float InputBufferSeconds = 0.25f;

        [Tooltip("连招节点。NodeId 必须稳定且不能重复。")]
        public ComboNode[] Nodes = Array.Empty<ComboNode>();

        private void OnValidate()
        {
            InputBufferSeconds = Mathf.Max(0f, InputBufferSeconds);
            Nodes ??= Array.Empty<ComboNode>();

            for (var i = 0; i < Nodes.Length; i++)
            {
                var node = Nodes[i];
                if (node == null)
                {
                    continue;
                }

                node.CancelWindowStart01 = Mathf.Clamp01(node.CancelWindowStart01);
                node.CancelWindowEnd01 = Mathf.Clamp01(node.CancelWindowEnd01);
                node.Transitions ??= Array.Empty<ComboTransitionData>();

                for (var j = 0; j < node.Transitions.Length; j++)
                {
                    var transition = node.Transitions[j];
                    if (transition == null)
                    {
                        continue;
                    }

                    transition.InputBufferSecondsOverride = Mathf.Max(0f, transition.InputBufferSecondsOverride);
                    transition.MinBufferedSeconds = Mathf.Max(0f, transition.MinBufferedSeconds);
                    transition.Conditions ??= Array.Empty<ActionConditionData>();
                }
            }
        }
    }
}
