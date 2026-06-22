using System;
using NiumaAction.Config;
using NiumaAction.Enum;

namespace NiumaAction.Data
{
    // 这是 NiumaAction 发给 TPCBridge 的中立播放请求，
    // 用来避免 NiumaAction.Runtime 直接依赖 TPC 的 ActionRequest 类型。
    [Serializable]
    public sealed class NiumaActionPlaybackRequest
    {
        public string RequestId;
        public string ActorId;
        public string NodeId;
        public string ActionId;
        public AnimateAsset Animate;
        public int Priority;
        public float FadeInSeconds;
        public bool ApplyGravity = true;

        public NiumaActionPlaybackRequest Clone()
        {
            return new NiumaActionPlaybackRequest
            {
                RequestId = RequestId,
                ActorId = ActorId,
                NodeId = NodeId,
                ActionId = ActionId,
                Animate = Animate,
                Priority = Priority,
                FadeInSeconds = FadeInSeconds,
                ApplyGravity = ApplyGravity
            };
        }
    }

    // TPC 播放分为预检查和提交两个阶段；正式仲裁结果通过事件回传。
    // 这个结构保留给 TPCBridge 内部、调试工具或轻量校验描述结果。
    [Serializable]
    public sealed class NiumaActionTPCPlaybackResult
    {
        public bool Succeeded;
        public NiumaActionTPCPlaybackStatus Status = NiumaActionTPCPlaybackStatus.None;
        public ActionFailureReason FailureReason = ActionFailureReason.None;
        public string RequestId;
        public string ActorId;
        public string Message;

        public static NiumaActionTPCPlaybackResult PrecheckAccepted(string requestId = null, string actorId = null, string message = null)
        {
            return Success(NiumaActionTPCPlaybackStatus.PrecheckAccepted, requestId, actorId, message);
        }

        public static NiumaActionTPCPlaybackResult CommitStarted(string requestId = null, string actorId = null, string message = null)
        {
            return Success(NiumaActionTPCPlaybackStatus.CommitStarted, requestId, actorId, message);
        }

        public static NiumaActionTPCPlaybackResult PrecheckRejected(string requestId = null, string actorId = null, string message = null, ActionFailureReason reason = ActionFailureReason.TpcPlaybackRejected)
        {
            return Failed(NiumaActionTPCPlaybackStatus.PrecheckRejected, reason, requestId, actorId, message);
        }

        public static NiumaActionTPCPlaybackResult CommitRejected(string requestId = null, string actorId = null, string message = null, ActionFailureReason reason = ActionFailureReason.TpcPlaybackRejected)
        {
            return Failed(NiumaActionTPCPlaybackStatus.CommitRejected, reason, requestId, actorId, message);
        }

        private static NiumaActionTPCPlaybackResult Success(NiumaActionTPCPlaybackStatus status, string requestId = null, string actorId = null, string message = null)
        {
            return new NiumaActionTPCPlaybackResult
            {
                Succeeded = true,
                Status = status,
                FailureReason = ActionFailureReason.None,
                RequestId = requestId,
                ActorId = actorId,
                Message = message
            };
        }

        private static NiumaActionTPCPlaybackResult Failed(NiumaActionTPCPlaybackStatus status, ActionFailureReason reason, string requestId = null, string actorId = null, string message = null)
        {
            return new NiumaActionTPCPlaybackResult
            {
                Succeeded = false,
                Status = status,
                FailureReason = reason,
                RequestId = requestId,
                ActorId = actorId,
                Message = message
            };
        }
    }
}
