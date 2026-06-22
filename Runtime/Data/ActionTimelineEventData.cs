using System;
using NiumaAction.Enum;

namespace NiumaAction.Data
{
    // 时间轴事件配置在 AnimateAsset 上，但可能由不同来源抛出：
    // TPC 动画事件、ActionService 兜底 Tick 或调试工具。
    [Serializable]
    public sealed class ActionTimelineEventData
    {
        public string EventId;
        public ActionTimelineEventType Type = ActionTimelineEventType.HitboxOpen;
        public float NormalizedTime;
        public string PayloadId;
        public float FloatValue;

        public ActionTimelineEventData Clone()
        {
            return new ActionTimelineEventData
            {
                EventId = EventId,
                Type = Type,
                NormalizedTime = NormalizedTime,
                PayloadId = PayloadId,
                FloatValue = FloatValue
            };
        }

        public static ActionTimelineEventData[] CloneArray(ActionTimelineEventData[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<ActionTimelineEventData>();
            }

            var clone = new ActionTimelineEventData[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                clone[i] = source[i]?.Clone();
            }

            return clone;
        }
    }

    public static class ActionAttackInstanceId
    {
        // CombatBridge 使用这个稳定 ID 配对 HitboxOpen 和 HitboxClose。
        // 优先使用 PayloadId，让同一段动画可以打开多个 Hitbox 通道。
        public static string Resolve(string requestId, ActionTimelineEventData timelineEvent)
        {
            var normalizedRequestId = string.IsNullOrWhiteSpace(requestId) ? "unknown_request" : requestId.Trim();
            var channelId = timelineEvent != null && !string.IsNullOrWhiteSpace(timelineEvent.PayloadId)
                ? timelineEvent.PayloadId.Trim()
                : timelineEvent != null && !string.IsNullOrWhiteSpace(timelineEvent.EventId)
                    ? timelineEvent.EventId.Trim()
                    : "unknown_event";

            return $"act:{normalizedRequestId}:{channelId}";
        }
    }
}
