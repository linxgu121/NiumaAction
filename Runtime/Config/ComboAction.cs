using System;
using NiumaAction.Data;
using UnityEngine;

namespace NiumaAction.Config
{
    // ComboAction 是连招节点选中的动作逻辑单元；它引用动画和调参数据，
    // 但最终命中是否合法、扣多少血，仍由 Combat 结算。
    [CreateAssetMenu(menuName = "NiumaAction/Combo Action", fileName = "ComboAction")]
    public sealed class ComboAction : ScriptableObject
    {
        [Tooltip("动作逻辑稳定 ID。用于 ComboNode、日志、调试和后续技能动作映射。")]
        public string ActionId;

        [Tooltip("显示名称，仅用于策划查看和调试。")]
        public string DisplayName;

        [Tooltip("动作表现资产。描述动画 Clip、淡入淡出、时间轴事件等。")]
        public AnimateAsset Animate;

        [Min(0f)]
        [Tooltip("体力消耗。ActionService 会先 Query 检查资源足够，再在 TPC Commit 前后按设计流程扣除。")]
        public float StaminaCost;

        [Min(0f)]
        [Tooltip("伤害倍率。会由 CombatBridge 写入 Combat 请求，最终伤害仍由 NiumaCombat 计算。")]
        public float DamageMultiplier = 1f;

        [Tooltip("默认 Hitbox 通道 ID。为空时可由 TimelineEvent.PayloadId 指定。")]
        public string HitboxId;

        [Tooltip("动作音效 CueId。核心 Runtime 不播放音频，由 AudioBridge 处理。")]
        public string AudioCueId;

        [Tooltip("动作触发条件。内置条件由 ActionService 处理，Custom 条件由 IActionConditionResolver 处理。")]
        public ActionConditionData[] Conditions = Array.Empty<ActionConditionData>();

        private void OnValidate()
        {
            StaminaCost = Mathf.Max(0f, StaminaCost);
            DamageMultiplier = Mathf.Max(0f, DamageMultiplier);
            Conditions ??= Array.Empty<ActionConditionData>();
        }
    }
}
