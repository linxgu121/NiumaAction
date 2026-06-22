using System.Collections.Generic;
using NiumaAction.Config;
using NiumaAction.Data;

namespace NiumaAction.Service
{
    internal sealed class ActionOwnerRuntimeState
    {
        public string ActorId;
        public MeleeWeaponSource CurrentWeapon;
        public ComboTreeAsset CurrentComboTree;
        public ComboNode CurrentNode;
        public ComboAction CurrentAction;
        public string RequestId;
        public float ActionElapsedSeconds;
        public float ActionDurationSeconds;
        public bool IsActionPlaying;
        public PendingActionRequest PendingRequest;

        public readonly List<BufferedActionInput> InputBuffer = new();
        public readonly HashSet<string> FiredTimelineEventKeys = new();

        public float CurrentNormalizedTime
        {
            get
            {
                if (ActionDurationSeconds <= 0f)
                {
                    return IsActionPlaying ? 1f : 0f;
                }

                return Clamp01(ActionElapsedSeconds / ActionDurationSeconds);
            }
        }

        public bool IsInCancelWindow
        {
            get
            {
                if (!IsActionPlaying || CurrentNode == null)
                {
                    return false;
                }

                var normalizedTime = CurrentNormalizedTime;
                return normalizedTime >= CurrentNode.CancelWindowStart01 && normalizedTime <= CurrentNode.CancelWindowEnd01;
            }
        }

        public ActionOwnerSnapshot ToSnapshot()
        {
            return new ActionOwnerSnapshot
            {
                ActorId = ActorId,
                CurrentWeaponSourceId = CurrentWeapon != null ? CurrentWeapon.WeaponSourceId : null,
                CurrentComboTreeId = CurrentComboTree != null ? CurrentComboTree.ComboTreeId : null,
                CurrentNodeId = CurrentNode != null ? CurrentNode.NodeId : null,
                ActionElapsedSeconds = ActionElapsedSeconds,
                ActionDurationSeconds = ActionDurationSeconds,
                CurrentNormalizedTime = CurrentNormalizedTime,
                IsActionPlaying = IsActionPlaying,
                IsInCancelWindow = IsInCancelWindow,
                InputBuffer = BufferedActionInput.CloneArray(InputBuffer.ToArray())
            };
        }

        public void ClearActionState()
        {
            CurrentNode = null;
            CurrentAction = null;
            RequestId = null;
            ActionElapsedSeconds = 0f;
            ActionDurationSeconds = 0f;
            IsActionPlaying = false;
            PendingRequest = null;
            InputBuffer.Clear();
            FiredTimelineEventKeys.Clear();
        }

        private static float Clamp01(float value)
        {
            if (value <= 0f)
            {
                return 0f;
            }

            return value >= 1f ? 1f : value;
        }
    }

    internal sealed class PendingActionRequest
    {
        public NiumaActionPlaybackRequest PlaybackRequest;
        public ComboNode Node;
        public ComboAction Action;
        public float StaminaCost;
        public bool ResourceConsumed;
    }
}
