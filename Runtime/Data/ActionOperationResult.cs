using System;
using NiumaAction.Enum;

namespace NiumaAction.Data
{
    // 命令方法用它返回可预期的玩法失败，例如缺少武器、取消窗口关闭或播放被拒绝；
    // 这些情况不应该通过异常表达。
    [Serializable]
    public sealed class ActionOperationResult
    {
        public bool Succeeded;
        public ActionFailureReason FailureReason = ActionFailureReason.None;
        public string Message;
        public string ActorId;
        public string NodeId;
        public string ActionId;

        public static ActionOperationResult Success(string actorId = null, string nodeId = null, string actionId = null, string message = null)
        {
            return new ActionOperationResult
            {
                Succeeded = true,
                FailureReason = ActionFailureReason.None,
                Message = message,
                ActorId = actorId,
                NodeId = nodeId,
                ActionId = actionId
            };
        }

        public static ActionOperationResult Failed(ActionFailureReason reason, string message = null, string actorId = null, string nodeId = null, string actionId = null)
        {
            return new ActionOperationResult
            {
                Succeeded = false,
                FailureReason = reason,
                Message = message,
                ActorId = actorId,
                NodeId = nodeId,
                ActionId = actionId
            };
        }
    }
}
