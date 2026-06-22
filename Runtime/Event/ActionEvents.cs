using System;
using NiumaAction.Data;
using NiumaAction.Enum;

namespace NiumaAction.Event
{
    // 播放事件是 ActionService 与 TPC 之间的防腐边界。
    // Runtime 只发布中立事实，TPCBridge 决定是否以及如何转成真实动画请求。
    [Serializable]
    public sealed class ActionPlaybackPrecheckRequestedEvent
    {
        public string ServiceInstanceId;
        public NiumaActionPlaybackRequest Request;
    }

    [Serializable]
    public sealed class ActionPlaybackPrecheckAcceptedEvent
    {
        public string ServiceInstanceId;
        public string RequestId;
        public string ActorId;
        public string NodeId;
        public string Message;
    }

    [Serializable]
    public sealed class ActionPlaybackPrecheckRejectedEvent
    {
        public string ServiceInstanceId;
        public string RequestId;
        public string ActorId;
        public ActionFailureReason FailureReason = ActionFailureReason.TpcPlaybackRejected;
        public string Message;
    }

    [Serializable]
    public sealed class ActionPlaybackCommitRequestedEvent
    {
        public string ServiceInstanceId;
        public string RequestId;
        public string ActorId;
        public string NodeId;
        public string ActionId;
        public NiumaActionPlaybackRequest Request;
    }

    [Serializable]
    public sealed class ActionPlaybackCommittedEvent
    {
        public string ServiceInstanceId;
        public string RequestId;
        public string ActorId;
        public string NodeId;
        public string ActionId;
        public NiumaActionPlaybackRequest Request;
    }

    [Serializable]
    public sealed class ActionPlaybackCommitRejectedEvent
    {
        public string ServiceInstanceId;
        public string RequestId;
        public string ActorId;
        public ActionFailureReason FailureReason = ActionFailureReason.TpcPlaybackRejected;
        public string Message;
    }

    [Serializable]
    public sealed class ActionPlaybackCancelledEvent
    {
        public string ServiceInstanceId;
        public string RequestId;
        public string ActorId;
        public string Reason;
    }

    [Serializable]
    public sealed class ActionPlaybackInterruptedEvent
    {
        public string ServiceInstanceId;
        public string RequestId;
        public string ActorId;
        public string Reason;
    }

    [Serializable]
    public sealed class ActionTimelineEventRaisedEvent
    {
        public string ServiceInstanceId;
        public string RequestId;
        // CombatBridge 打开和关闭 Hitbox 时应使用这个 ID；
        // 即使动画在 Close 事件前被中断，也能按请求清理。
        public string AttackInstanceId;
        public string ActorId;
        public string NodeId;
        public string ActionId;
        // TimelineEvent.NormalizedTime 是资产配置的目标时间。
        public ActionTimelineEventData TimelineEvent;
        // NormalizedTime 是本次事件实际触发时的动画进度；Tick 兜底或 AnimationEvent 来源下可能与配置值有微小差异。
        public float NormalizedTime;
        public ActionTimelineEventSource Source = ActionTimelineEventSource.Unknown;
    }
}
