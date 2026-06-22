using System;

namespace NiumaAction.Data
{
    // Snapshot 是给外部读取的快照数据；Service 应复制运行时状态到这里，
    // 不要把可变内部状态直接暴露出去。
    [Serializable]
    public sealed class ActionOwnerSnapshot
    {
        public string ActorId;
        public string CurrentWeaponSourceId;
        public string CurrentComboTreeId;
        public string CurrentNodeId;
        public float ActionElapsedSeconds;
        public float ActionDurationSeconds;
        public float CurrentNormalizedTime;
        public bool IsActionPlaying;
        public bool IsInCancelWindow;
        public BufferedActionInput[] InputBuffer = Array.Empty<BufferedActionInput>();

        public ActionOwnerSnapshot Clone()
        {
            return new ActionOwnerSnapshot
            {
                ActorId = ActorId,
                CurrentWeaponSourceId = CurrentWeaponSourceId,
                CurrentComboTreeId = CurrentComboTreeId,
                CurrentNodeId = CurrentNodeId,
                ActionElapsedSeconds = ActionElapsedSeconds,
                ActionDurationSeconds = ActionDurationSeconds,
                CurrentNormalizedTime = CurrentNormalizedTime,
                IsActionPlaying = IsActionPlaying,
                IsInCancelWindow = IsInCancelWindow,
                InputBuffer = BufferedActionInput.CloneArray(InputBuffer)
            };
        }
    }
}
