using UnityEngine;

namespace NiumaAction.Config
{
    // WeaponSource 是 ActionService 使用的武器动作来源资产。
    // Equipment / Inventory 后续可以把自己的物品实例映射到这里。
    public abstract class WeaponSource : ScriptableObject
    {
        [Tooltip("武器动作来源稳定 ID。后续可由 ItemId / EquipmentInstanceSnapshot 映射到这里。")]
        public string WeaponSourceId;

        [Tooltip("显示名称，仅用于策划查看和调试。")]
        public string DisplayName;
    }

    [CreateAssetMenu(menuName = "NiumaAction/Melee Weapon Source", fileName = "MeleeWeaponSource")]
    public sealed class MeleeWeaponSource : WeaponSource
    {
        [Min(0f)]
        [Tooltip("近战武器基础伤害。CombatBridge 会把它作为 Combat 请求基础值来源之一。")]
        public float BaseDamage = 10f;

        [Tooltip("持握 / 架势 ID。第一版只作为 TPC 姿态切换预留，不直接执行。")]
        public string HoldStyleId;

        [Tooltip("默认连招树。普通攻击输入会从这棵树选择起始或后续节点。")]
        public ComboTreeAsset DefaultComboTree;

        private void OnValidate()
        {
            BaseDamage = Mathf.Max(0f, BaseDamage);
        }
    }
}
