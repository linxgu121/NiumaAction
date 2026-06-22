using System;
using System.Collections.Generic;
using NiumaAction.Config;
using NiumaAction.Data;
using NiumaAction.Enum;
using NiumaAction.Event;
using NiumaAttribute.Service;
using NiumaCore.Event;

namespace NiumaAction.Service
{
    public sealed class NiumaActionService : IActionService, IActionConfigurationService
    {
        private const string SourceModule = "NiumaAction";

        private readonly Dictionary<string, ActionOwnerRuntimeState> _states = new();
        private readonly Dictionary<string, MeleeWeaponSource> _weaponSources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ComboTreeAsset> _comboTrees = new(StringComparer.Ordinal);

        private IAttributeQuery _attributeQuery;
        private IAttributeCommand _attributeCommand;
        private IActionConditionResolver _conditionResolver;
        private INiumaActionTPCPlaybackGateway _playbackGateway;
        private IEventBus _eventBus;
        private Func<float> _timeProvider;
        private string _staminaResourceId = "stamina";

        public NiumaActionService()
        {
            ServiceInstanceId = Guid.NewGuid().ToString("N");
        }

        public long Revision { get; private set; }
        public string ServiceInstanceId { get; }

        public void SetDefaultComboTrees(ComboTreeAsset[] comboTrees)
        {
            _comboTrees.Clear();
            if (comboTrees == null)
            {
                return;
            }

            for (var i = 0; i < comboTrees.Length; i++)
            {
                var comboTree = comboTrees[i];
                if (comboTree == null || string.IsNullOrWhiteSpace(comboTree.ComboTreeId))
                {
                    continue;
                }

                _comboTrees[comboTree.ComboTreeId.Trim()] = comboTree;
            }
        }

        public void SetWeaponSources(MeleeWeaponSource[] weaponSources)
        {
            _weaponSources.Clear();
            if (weaponSources == null)
            {
                return;
            }

            for (var i = 0; i < weaponSources.Length; i++)
            {
                var weaponSource = weaponSources[i];
                if (weaponSource == null || string.IsNullOrWhiteSpace(weaponSource.WeaponSourceId))
                {
                    continue;
                }

                _weaponSources[weaponSource.WeaponSourceId.Trim()] = weaponSource;
            }
        }

        public void SetAttributeQuery(IAttributeQuery attributeQuery) => _attributeQuery = attributeQuery;

        public void SetAttributeCommand(IAttributeCommand attributeCommand) => _attributeCommand = attributeCommand;

        public void SetConditionResolver(IActionConditionResolver conditionResolver) => _conditionResolver = conditionResolver;

        public void SetPlaybackGateway(INiumaActionTPCPlaybackGateway playbackGateway) => _playbackGateway = playbackGateway;

        public void SetStaminaResourceId(string resourceId)
        {
            _staminaResourceId = string.IsNullOrWhiteSpace(resourceId) ? "stamina" : resourceId.Trim();
        }

        public void SetTimeProvider(Func<float> timeProvider)
        {
            _timeProvider = timeProvider;
        }

        public void SetEventBus(IEventBus eventBus)
        {
            if (ReferenceEquals(_eventBus, eventBus))
            {
                return;
            }

            Unsubscribe(_eventBus);
            _eventBus = eventBus;
            Subscribe(_eventBus);
        }

        public bool HasActor(string actorId)
        {
            return !string.IsNullOrWhiteSpace(actorId) && _states.ContainsKey(actorId.Trim());
        }

        public ActionOwnerSnapshot GetSnapshot(string actorId)
        {
            return TryGetState(actorId, out var state) ? state.ToSnapshot() : new ActionOwnerSnapshot { ActorId = NormalizeId(actorId) };
        }

        public MeleeWeaponSource GetWeaponSource(string actorId)
        {
            return TryGetState(actorId, out var state) ? state.CurrentWeapon : null;
        }

        public ComboTreeAsset GetComboTree(string actorId)
        {
            return TryGetState(actorId, out var state) ? state.CurrentComboTree : null;
        }

        public bool IsActionPlaying(string actorId)
        {
            return TryGetState(actorId, out var state) && state.IsActionPlaying;
        }

        public bool IsInCancelWindow(string actorId)
        {
            return TryGetState(actorId, out var state) && state.IsInCancelWindow;
        }

        public ActionOperationResult CanSubmitInput(string actorId, string inputId)
        {
            var normalizedActorId = NormalizeId(actorId);
            var normalizedInputId = NormalizeId(inputId);
            if (string.IsNullOrWhiteSpace(normalizedActorId) || string.IsNullOrWhiteSpace(normalizedInputId))
            {
                return ActionOperationResult.Failed(ActionFailureReason.InvalidRequest, "ActorId 或 InputId 为空。", normalizedActorId);
            }

            var state = GetOrCreateState(normalizedActorId);
            if (state.CurrentWeapon == null)
            {
                return ActionOperationResult.Failed(ActionFailureReason.WeaponSourceMissing, "当前 Actor 未装备 MeleeWeaponSource。", normalizedActorId);
            }

            if (ResolveComboTree(state) == null)
            {
                return ActionOperationResult.Failed(ActionFailureReason.ComboTreeMissing, "当前武器没有可用 ComboTree。", normalizedActorId);
            }

            if (state.IsActionPlaying && !state.IsInCancelWindow)
            {
                return ActionOperationResult.Success(normalizedActorId, state.CurrentNode != null ? state.CurrentNode.NodeId : null, null, "输入可进入缓冲。");
            }

            return ActionOperationResult.Success(normalizedActorId, state.CurrentNode != null ? state.CurrentNode.NodeId : null);
        }

        public ActionOperationResult EquipWeaponSource(string actorId, MeleeWeaponSource weaponSource)
        {
            var normalizedActorId = NormalizeId(actorId);
            if (string.IsNullOrWhiteSpace(normalizedActorId))
            {
                return ActionOperationResult.Failed(ActionFailureReason.InvalidRequest, "ActorId 为空。", normalizedActorId);
            }

            if (weaponSource == null)
            {
                return ActionOperationResult.Failed(ActionFailureReason.WeaponSourceMissing, "MeleeWeaponSource 为空。", normalizedActorId);
            }

            var state = GetOrCreateState(normalizedActorId);
            if (state.IsActionPlaying || state.PendingRequest != null)
            {
                CancelActionInternal(state, "EquipWeaponSource", true);
            }

            state.CurrentWeapon = weaponSource;
            state.CurrentComboTree = weaponSource.DefaultComboTree != null ? weaponSource.DefaultComboTree : ResolveComboTreeById(weaponSource.WeaponSourceId);
            state.InputBuffer.Clear();
            Revision++;
            return ActionOperationResult.Success(normalizedActorId, null, null, "已装备武器动作来源。");
        }

        public ActionOperationResult SubmitInput(string actorId, string inputId)
        {
            var normalizedActorId = NormalizeId(actorId);
            var normalizedInputId = NormalizeId(inputId);
            if (string.IsNullOrWhiteSpace(normalizedActorId) || string.IsNullOrWhiteSpace(normalizedInputId))
            {
                return ActionOperationResult.Failed(ActionFailureReason.InvalidRequest, "ActorId 或 InputId 为空。", normalizedActorId);
            }

            var state = GetOrCreateState(normalizedActorId);
            var comboTree = ResolveComboTree(state);
            if (state.CurrentWeapon == null)
            {
                return ActionOperationResult.Failed(ActionFailureReason.WeaponSourceMissing, "当前 Actor 未装备 MeleeWeaponSource。", normalizedActorId);
            }

            if (comboTree == null)
            {
                return ActionOperationResult.Failed(ActionFailureReason.ComboTreeMissing, "当前武器没有可用 ComboTree。", normalizedActorId);
            }

            if (state.PendingRequest != null)
            {
                BufferInput(state, normalizedInputId, comboTree.InputBufferSeconds);
                return ActionOperationResult.Success(normalizedActorId, state.PendingRequest.Node != null ? state.PendingRequest.Node.NodeId : null, null, "已有动作等待 TPC 确认，本次输入已进入缓冲。");
            }

            if (state.IsActionPlaying)
            {
                if (TryResolveTransition(state, normalizedInputId, out var targetNode, out var failure))
                {
                    return RequestNodePlayback(state, targetNode, normalizedInputId);
                }

                if (failure != null
                    && failure.FailureReason != ActionFailureReason.CancelWindowClosed
                    && failure.FailureReason != ActionFailureReason.TransitionMissing)
                {
                    return failure;
                }

                BufferInput(state, normalizedInputId, comboTree.InputBufferSeconds);
                return failure != null && failure.FailureReason == ActionFailureReason.CancelWindowClosed
                    ? ActionOperationResult.Success(normalizedActorId, state.CurrentNode != null ? state.CurrentNode.NodeId : null, null, "取消窗口未打开，本次输入已进入缓冲。")
                    : ActionOperationResult.Success(normalizedActorId, state.CurrentNode != null ? state.CurrentNode.NodeId : null, null, "未找到立即转换，本次输入已进入缓冲。");
            }

            var startNode = ResolveStartNode(comboTree);
            return RequestNodePlayback(state, startNode, normalizedInputId);
        }

        public ActionOperationResult CancelAction(string actorId, string reason)
        {
            var normalizedActorId = NormalizeId(actorId);
            if (!TryGetState(normalizedActorId, out var state))
            {
                return ActionOperationResult.Success(normalizedActorId, null, null, "当前 Actor 没有动作状态。");
            }

            CancelActionInternal(state, string.IsNullOrWhiteSpace(reason) ? "CancelAction" : reason.Trim(), true);
            return ActionOperationResult.Success(normalizedActorId, null, null, "动作已取消。");
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || _states.Count == 0)
            {
                return;
            }

            foreach (var state in _states.Values)
            {
                PruneExpiredInputs(state);
                if (!state.IsActionPlaying || state.PendingRequest != null)
                {
                    continue;
                }

                var previousNormalizedTime = state.CurrentNormalizedTime;
                state.ActionElapsedSeconds += deltaTime;
                var currentNormalizedTime = state.CurrentNormalizedTime;
                RaiseDueTimelineEvents(state, previousNormalizedTime, currentNormalizedTime);
                TryConsumeBufferedTransition(state);

                if (state.PendingRequest == null && state.IsActionPlaying && currentNormalizedTime >= 1f)
                {
                    CompleteCurrentAction(state);
                }
            }
        }

        private ActionOperationResult RequestNodePlayback(ActionOwnerRuntimeState state, ComboNode node, string inputId)
        {
            if (node == null)
            {
                return ActionOperationResult.Failed(ActionFailureReason.NodeMissing, "没有可用 ComboNode。", state.ActorId);
            }

            if (node.Action == null)
            {
                return ActionOperationResult.Failed(ActionFailureReason.ActionMissing, "ComboNode 未绑定 ComboAction。", state.ActorId, node.NodeId);
            }

            var conditionResult = EvaluateConditions(state, node, inputId);
            if (!conditionResult.Succeeded)
            {
                return conditionResult;
            }

            var resourceResult = CheckResourceAvailable(state.ActorId, node.Action.StaminaCost);
            if (!resourceResult.Succeeded)
            {
                return resourceResult;
            }

            var requestId = $"act:{state.ActorId}:{Guid.NewGuid():N}";
            var animate = node.Action.Animate;
            var request = new NiumaActionPlaybackRequest
            {
                RequestId = requestId,
                ActorId = state.ActorId,
                NodeId = node.NodeId,
                ActionId = node.Action.ActionId,
                Animate = animate,
                Priority = animate != null ? animate.Priority : 0,
                FadeInSeconds = animate != null ? animate.FadeInSeconds : 0f,
                ApplyGravity = animate == null || animate.ApplyGravity
            };

            PublishTransitionCleanupIfNeeded(state);

            // 进入 Pending 后只等待 TPC 预检结果；没有桥接时不会伪造成播放成功。
            state.PendingRequest = new PendingActionRequest
            {
                PlaybackRequest = request,
                Node = node,
                Action = node.Action,
                StaminaCost = Math.Max(0f, node.Action.StaminaCost)
            };
            state.IsActionPlaying = false;
            state.CurrentNode = node;
            state.CurrentAction = node.Action;
            state.RequestId = requestId;
            Revision++;

            Publish(new ActionPlaybackPrecheckRequestedEvent
            {
                ServiceInstanceId = ServiceInstanceId,
                Request = request.Clone()
            });

            // 正式流程通过 EventBus 让 TPCBridge 写入 TPC 黑板并等待仲裁结果。
            // 没有 EventBus 时 Gateway 只能做提交测试；完整播放闭环仍需要外部回传 Accepted/Committed 事件。
            if (_eventBus == null)
            {
                _playbackGateway?.SubmitPrecheckPlayback(request.Clone());
                state.ClearActionState();
                Revision++;
                return ActionOperationResult.Failed(
                    ActionFailureReason.TpcPlaybackRejected,
                    "当前没有 EventBus，Playback Gateway 只能验证提交入口，无法完成 Accepted / Committed 回传闭环。",
                    state.ActorId,
                    node.NodeId);
            }

            return ActionOperationResult.Success(state.ActorId, node.NodeId, node.Action.ActionId, "动作播放预检已请求。");
        }

        private void OnPrecheckAccepted(ActionPlaybackPrecheckAcceptedEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            if (!IsEventForThisService(evt?.ServiceInstanceId) || !TryGetState(evt.ActorId, out var state))
            {
                return;
            }

            if (state.PendingRequest == null || !string.Equals(state.PendingRequest.PlaybackRequest.RequestId, evt.RequestId, StringComparison.Ordinal))
            {
                return;
            }

            TryCommitPending(state);
        }

        private void OnPrecheckRejected(ActionPlaybackPrecheckRejectedEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            if (!IsEventForThisService(evt?.ServiceInstanceId) || !TryGetState(evt.ActorId, out var state))
            {
                return;
            }

            if (state.PendingRequest == null || !string.Equals(state.PendingRequest.PlaybackRequest.RequestId, evt.RequestId, StringComparison.Ordinal))
            {
                return;
            }

            state.ClearActionState();
            Revision++;
        }

        private void TryCommitPending(ActionOwnerRuntimeState state)
        {
            var pending = state.PendingRequest;
            if (pending == null)
            {
                return;
            }

            var resourceResult = CheckResourceAvailable(state.ActorId, pending.StaminaCost);
            if (!resourceResult.Succeeded)
            {
                CancelActionInternal(state, "ResourceChangedBeforeCommit", true);
                return;
            }

            var consumeResult = ConsumeResource(state.ActorId, pending.StaminaCost);
            if (!consumeResult.Succeeded)
            {
                CancelActionInternal(state, "ConsumeResourceFailed", true);
                return;
            }

            pending.ResourceConsumed = pending.StaminaCost > 0f;
            Publish(new ActionPlaybackCommitRequestedEvent
            {
                ServiceInstanceId = ServiceInstanceId,
                RequestId = pending.PlaybackRequest.RequestId,
                ActorId = state.ActorId,
                NodeId = pending.PlaybackRequest.NodeId,
                ActionId = pending.PlaybackRequest.ActionId,
                Request = pending.PlaybackRequest.Clone()
            });

            // Commit 只表示 Action 已完成资源扣除并请求正式播放，
            // 是否真正进入 TPC 动作状态仍由 TPCBridge 在仲裁后通过事件回传。
            if (_eventBus == null)
            {
                _playbackGateway?.SubmitCommitPlayback(pending.PlaybackRequest.Clone());
            }
        }

        private void PublishTransitionCleanupIfNeeded(ActionOwnerRuntimeState state)
        {
            if (state == null || !state.IsActionPlaying || string.IsNullOrWhiteSpace(state.RequestId))
            {
                return;
            }

            var oldRequestId = state.RequestId;
            // 连招切换会替换 RequestId。先发布旧请求的取消事实，确保 CombatBridge 能关闭旧 Hitbox。
            Publish(new ActionPlaybackCancelledEvent
            {
                ServiceInstanceId = ServiceInstanceId,
                RequestId = oldRequestId,
                ActorId = state.ActorId,
                Reason = "ComboTransition"
            });

            if (_eventBus == null)
            {
                _playbackGateway?.CancelPlayback(oldRequestId, state.ActorId, "ComboTransition");
            }
        }

        private void OnCommitted(ActionPlaybackCommittedEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            if (!IsEventForThisService(evt?.ServiceInstanceId) || !TryGetState(evt.ActorId, out var state))
            {
                return;
            }

            var pending = state.PendingRequest;
            if (pending == null || !string.Equals(pending.PlaybackRequest.RequestId, evt.RequestId, StringComparison.Ordinal))
            {
                return;
            }

            state.CurrentNode = pending.Node;
            state.CurrentAction = pending.Action;
            state.RequestId = pending.PlaybackRequest.RequestId;
            state.ActionElapsedSeconds = 0f;
            state.ActionDurationSeconds = ResolveDurationSeconds(pending.Action);
            state.IsActionPlaying = true;
            state.PendingRequest = null;
            state.FiredTimelineEventKeys.Clear();
            Revision++;
        }

        private void OnCommitRejected(ActionPlaybackCommitRejectedEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            if (!IsEventForThisService(evt?.ServiceInstanceId) || !TryGetState(evt.ActorId, out var state))
            {
                return;
            }

            var pending = state.PendingRequest;
            if (pending == null || !string.Equals(pending.PlaybackRequest.RequestId, evt.RequestId, StringComparison.Ordinal))
            {
                return;
            }

            if (pending.ResourceConsumed)
            {
                RecoverResource(state.ActorId, pending.StaminaCost);
            }

            state.ClearActionState();
            Revision++;
        }

        private void OnInterrupted(ActionPlaybackInterruptedEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            if (!IsEventForThisService(evt?.ServiceInstanceId) || !TryGetState(evt.ActorId, out var state))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(evt.RequestId) || !string.Equals(state.RequestId, evt.RequestId, StringComparison.Ordinal))
            {
                return;
            }

            // 已经播放成功后的中断属于正常战斗消耗，不回滚资源。
            state.ClearActionState();
            Revision++;
        }

        private bool TryResolveTransition(ActionOwnerRuntimeState state, string inputId, out ComboNode targetNode, out ActionOperationResult failure)
        {
            targetNode = null;
            failure = null;

            if (state.CurrentNode == null || state.CurrentNode.Transitions == null)
            {
                failure = ActionOperationResult.Failed(ActionFailureReason.TransitionMissing, "当前节点没有可用转换。", state.ActorId);
                return false;
            }

            for (var i = 0; i < state.CurrentNode.Transitions.Length; i++)
            {
                var transition = state.CurrentNode.Transitions[i];
                if (transition == null || !string.Equals(transition.InputId, inputId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (transition.RequireCancelWindow && !state.IsInCancelWindow)
                {
                    failure = ActionOperationResult.Failed(ActionFailureReason.CancelWindowClosed, "当前不在取消窗口内。", state.ActorId, state.CurrentNode.NodeId);
                    return false;
                }

                targetNode = FindNode(state.CurrentComboTree, transition.TargetNodeId);
                if (targetNode == null)
                {
                    failure = ActionOperationResult.Failed(ActionFailureReason.NodeMissing, "转换目标节点不存在。", state.ActorId, transition.TargetNodeId);
                    return false;
                }

                var conditionResult = EvaluateTransitionConditions(state, transition, targetNode, inputId);
                if (!conditionResult.Succeeded)
                {
                    failure = conditionResult;
                    return false;
                }

                return true;
            }

            failure = ActionOperationResult.Failed(ActionFailureReason.TransitionMissing, "没有匹配当前输入的转换。", state.ActorId, state.CurrentNode.NodeId);
            return false;
        }

        private void BufferInput(ActionOwnerRuntimeState state, string inputId, float defaultBufferSeconds)
        {
            var now = GetTime();
            var bufferSeconds = Math.Max(0f, defaultBufferSeconds);
            state.InputBuffer.RemoveAll(input => string.Equals(input.InputId, inputId, StringComparison.Ordinal));
            state.InputBuffer.Add(new BufferedActionInput
            {
                InputId = inputId,
                BufferedAtTime = now,
                ExpiresAtTime = now + bufferSeconds
            });
            Revision++;
        }

        private void TryConsumeBufferedTransition(ActionOwnerRuntimeState state)
        {
            if (!state.IsInCancelWindow || state.InputBuffer.Count == 0)
            {
                return;
            }

            for (var i = 0; i < state.InputBuffer.Count; i++)
            {
                var buffered = state.InputBuffer[i];
                if (!TryResolveTransition(state, buffered.InputId, out var targetNode, out _))
                {
                    continue;
                }

                state.InputBuffer.RemoveAt(i);
                RequestNodePlayback(state, targetNode, buffered.InputId);
                return;
            }
        }

        private void PruneExpiredInputs(ActionOwnerRuntimeState state)
        {
            var now = GetTime();
            state.InputBuffer.RemoveAll(input => input == null || input.ExpiresAtTime <= now);
        }

        private void RaiseDueTimelineEvents(ActionOwnerRuntimeState state, float previousNormalizedTime, float currentNormalizedTime)
        {
            var events = state.CurrentAction != null && state.CurrentAction.Animate != null
                ? state.CurrentAction.Animate.TimelineEvents
                : null;
            if (events == null || events.Length == 0)
            {
                return;
            }

            for (var i = 0; i < events.Length; i++)
            {
                var timelineEvent = events[i];
                if (timelineEvent == null)
                {
                    continue;
                }

                var key = BuildTimelineEventKey(timelineEvent, i);
                if (state.FiredTimelineEventKeys.Contains(key))
                {
                    continue;
                }

                if (timelineEvent.NormalizedTime > currentNormalizedTime || timelineEvent.NormalizedTime < previousNormalizedTime)
                {
                    continue;
                }

                state.FiredTimelineEventKeys.Add(key);
                Publish(new ActionTimelineEventRaisedEvent
                {
                    ServiceInstanceId = ServiceInstanceId,
                    RequestId = state.RequestId,
                    AttackInstanceId = ActionAttackInstanceId.Resolve(state.RequestId, timelineEvent),
                    ActorId = state.ActorId,
                    NodeId = state.CurrentNode != null ? state.CurrentNode.NodeId : null,
                    ActionId = state.CurrentAction != null ? state.CurrentAction.ActionId : null,
                    TimelineEventKey = key,
                    TimelineEvent = timelineEvent.Clone(),
                    NormalizedTime = currentNormalizedTime,
                    Source = ActionTimelineEventSource.ActionServiceTickFallback
                });
            }
        }

        private void CompleteCurrentAction(ActionOwnerRuntimeState state)
        {
            var requestId = state.RequestId;
            var actorId = state.ActorId;
            state.ClearActionState();
            Publish(new ActionPlaybackCancelledEvent
            {
                ServiceInstanceId = ServiceInstanceId,
                RequestId = requestId,
                ActorId = actorId,
                Reason = "Completed"
            });
            Revision++;
        }

        private void CancelActionInternal(ActionOwnerRuntimeState state, string reason, bool notifyGateway)
        {
            var requestId = state.RequestId;
            var pending = state.PendingRequest;
            if (pending != null && pending.ResourceConsumed)
            {
                RecoverResource(state.ActorId, pending.StaminaCost);
            }

            if (notifyGateway && _eventBus == null && !string.IsNullOrWhiteSpace(requestId))
            {
                // 正式 EventBus 流程由 CancelledEvent 通知 TPCBridge，避免 Gateway 和事件订阅双路取消。
                _playbackGateway?.CancelPlayback(requestId, state.ActorId, reason);
            }

            state.ClearActionState();
            Publish(new ActionPlaybackCancelledEvent
            {
                ServiceInstanceId = ServiceInstanceId,
                RequestId = requestId,
                ActorId = state.ActorId,
                Reason = reason
            });
            Revision++;
        }

        private ActionOperationResult EvaluateConditions(ActionOwnerRuntimeState state, ComboNode node, string inputId)
        {
            var action = node.Action;
            var conditions = action != null ? action.Conditions : null;
            return EvaluateConditionArray(state, node, null, inputId, conditions);
        }

        private ActionOperationResult EvaluateTransitionConditions(ActionOwnerRuntimeState state, ComboTransitionData transition, ComboNode targetNode, string inputId)
        {
            return EvaluateConditionArray(state, targetNode, transition, inputId, transition.Conditions);
        }

        private ActionOperationResult EvaluateConditionArray(ActionOwnerRuntimeState state, ComboNode candidateNode, ComboTransitionData transition, string inputId, ActionConditionData[] conditions)
        {
            if (conditions == null || conditions.Length == 0)
            {
                return ActionOperationResult.Success(state.ActorId, candidateNode != null ? candidateNode.NodeId : null);
            }

            var context = new ActionConditionContext
            {
                ActorId = state.ActorId,
                InputId = inputId,
                WeaponSource = state.CurrentWeapon,
                ComboTree = state.CurrentComboTree,
                CurrentNodeId = state.CurrentNode != null ? state.CurrentNode.NodeId : null,
                CandidateNodeId = candidateNode != null ? candidateNode.NodeId : null,
                Owner = state.ToSnapshot(),
                CurrentNormalizedTime = state.CurrentNormalizedTime,
                IsInCancelWindow = state.IsInCancelWindow
            };

            for (var i = 0; i < conditions.Length; i++)
            {
                var condition = conditions[i];
                if (condition == null)
                {
                    continue;
                }

                var result = EvaluateCondition(condition, context);
                if (!result.Succeeded)
                {
                    return result;
                }
            }

            return ActionOperationResult.Success(state.ActorId, candidateNode != null ? candidateNode.NodeId : null);
        }

        private ActionOperationResult EvaluateCondition(ActionConditionData condition, ActionConditionContext context)
        {
            switch (condition.Type)
            {
                case ActionConditionType.None:
                    return ActionOperationResult.Success(context.ActorId, context.CandidateNodeId);

                case ActionConditionType.ResourceAtLeast:
                    return EvaluateResourceCondition(condition, context);

                case ActionConditionType.CurrentNodeEquals:
                    return EvaluateCurrentNodeCondition(condition, context);

                case ActionConditionType.HasWeaponSource:
                    return EvaluateWeaponCondition(condition, context);

                case ActionConditionType.Custom:
                    return _conditionResolver != null
                        ? _conditionResolver.Evaluate(condition, context)
                        : ActionOperationResult.Failed(ActionFailureReason.ConditionFailed, "Custom 条件缺少 IActionConditionResolver。", context.ActorId, context.CandidateNodeId);

                default:
                    return ActionOperationResult.Failed(ActionFailureReason.ConditionFailed, "未知动作条件类型。", context.ActorId, context.CandidateNodeId);
            }
        }

        private ActionOperationResult EvaluateResourceCondition(ActionConditionData condition, ActionConditionContext context)
        {
            if (_attributeQuery == null)
            {
                return ActionOperationResult.Failed(ActionFailureReason.AttributeServiceMissing, "ResourceAtLeast 条件缺少 IAttributeQuery。", context.ActorId, context.CandidateNodeId);
            }

            var resourceId = string.IsNullOrWhiteSpace(condition.TargetId) ? _staminaResourceId : condition.TargetId.Trim();
            var current = _attributeQuery.GetResourceCurrent(context.ActorId, resourceId, 0f);
            return current >= condition.FloatValue
                ? ActionOperationResult.Success(context.ActorId, context.CandidateNodeId)
                : ActionOperationResult.Failed(ActionFailureReason.ConditionFailed, "资源不足，动作条件不满足。", context.ActorId, context.CandidateNodeId);
        }

        private static ActionOperationResult EvaluateCurrentNodeCondition(ActionConditionData condition, ActionConditionContext context)
        {
            var expected = !string.IsNullOrWhiteSpace(condition.TargetId) ? condition.TargetId.Trim() : NormalizeId(condition.StringValue);
            return string.Equals(context.CurrentNodeId, expected, StringComparison.Ordinal)
                ? ActionOperationResult.Success(context.ActorId, context.CandidateNodeId)
                : ActionOperationResult.Failed(ActionFailureReason.ConditionFailed, "当前节点不匹配。", context.ActorId, context.CandidateNodeId);
        }

        private static ActionOperationResult EvaluateWeaponCondition(ActionConditionData condition, ActionConditionContext context)
        {
            if (context.WeaponSource == null)
            {
                return ActionOperationResult.Failed(ActionFailureReason.WeaponSourceMissing, "当前没有武器动作来源。", context.ActorId, context.CandidateNodeId);
            }

            var expected = !string.IsNullOrWhiteSpace(condition.TargetId) ? condition.TargetId.Trim() : NormalizeId(condition.StringValue);
            if (string.IsNullOrWhiteSpace(expected))
            {
                return ActionOperationResult.Success(context.ActorId, context.CandidateNodeId);
            }

            return string.Equals(context.WeaponSource.WeaponSourceId, expected, StringComparison.Ordinal)
                ? ActionOperationResult.Success(context.ActorId, context.CandidateNodeId)
                : ActionOperationResult.Failed(ActionFailureReason.ConditionFailed, "武器动作来源不匹配。", context.ActorId, context.CandidateNodeId);
        }

        private ActionOperationResult CheckResourceAvailable(string actorId, float amount)
        {
            if (amount <= 0f)
            {
                return ActionOperationResult.Success(actorId);
            }

            if (_attributeQuery == null)
            {
                return ActionOperationResult.Failed(ActionFailureReason.AttributeServiceMissing, "动作需要消耗体力，但未注入 IAttributeQuery。", actorId);
            }

            var current = _attributeQuery.GetResourceCurrent(actorId, _staminaResourceId, 0f);
            return current >= amount
                ? ActionOperationResult.Success(actorId)
                : ActionOperationResult.Failed(ActionFailureReason.ResourceInsufficient, "体力不足，动作无法启动。", actorId);
        }

        private ActionOperationResult ConsumeResource(string actorId, float amount)
        {
            if (amount <= 0f)
            {
                return ActionOperationResult.Success(actorId);
            }

            if (_attributeCommand == null)
            {
                return ActionOperationResult.Failed(ActionFailureReason.AttributeServiceMissing, "动作需要扣除体力，但未注入 IAttributeCommand。", actorId);
            }

            var result = _attributeCommand.ConsumeResource(actorId, _staminaResourceId, amount, SourceModule);
            return result != null && result.Succeeded
                ? ActionOperationResult.Success(actorId)
                : ActionOperationResult.Failed(ActionFailureReason.AttributeServiceMissing, result != null ? result.Message : "扣除体力失败。", actorId);
        }

        private void RecoverResource(string actorId, float amount)
        {
            if (amount <= 0f || _attributeCommand == null)
            {
                return;
            }

            _attributeCommand.RecoverResource(actorId, _staminaResourceId, amount, SourceModule);
        }

        private ComboTreeAsset ResolveComboTree(ActionOwnerRuntimeState state)
        {
            if (state.CurrentComboTree != null)
            {
                return state.CurrentComboTree;
            }

            if (state.CurrentWeapon != null && state.CurrentWeapon.DefaultComboTree != null)
            {
                state.CurrentComboTree = state.CurrentWeapon.DefaultComboTree;
                return state.CurrentComboTree;
            }

            return null;
        }

        private ComboTreeAsset ResolveComboTreeById(string comboTreeId)
        {
            return !string.IsNullOrWhiteSpace(comboTreeId) && _comboTrees.TryGetValue(comboTreeId.Trim(), out var comboTree)
                ? comboTree
                : null;
        }

        private static ComboNode ResolveStartNode(ComboTreeAsset comboTree)
        {
            if (comboTree == null || comboTree.Nodes == null || comboTree.Nodes.Length == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(comboTree.StartNodeId))
            {
                var startNode = FindNode(comboTree, comboTree.StartNodeId);
                if (startNode != null)
                {
                    return startNode;
                }
            }

            for (var i = 0; i < comboTree.Nodes.Length; i++)
            {
                if (comboTree.Nodes[i] != null)
                {
                    return comboTree.Nodes[i];
                }
            }

            return null;
        }

        private static ComboNode FindNode(ComboTreeAsset comboTree, string nodeId)
        {
            if (comboTree == null || comboTree.Nodes == null || string.IsNullOrWhiteSpace(nodeId))
            {
                return null;
            }

            var normalizedNodeId = nodeId.Trim();
            for (var i = 0; i < comboTree.Nodes.Length; i++)
            {
                var node = comboTree.Nodes[i];
                if (node != null && string.Equals(node.NodeId, normalizedNodeId, StringComparison.Ordinal))
                {
                    return node;
                }
            }

            return null;
        }

        private ActionOwnerRuntimeState GetOrCreateState(string actorId)
        {
            var normalizedActorId = NormalizeId(actorId);
            if (!_states.TryGetValue(normalizedActorId, out var state))
            {
                state = new ActionOwnerRuntimeState { ActorId = normalizedActorId };
                _states[normalizedActorId] = state;
                Revision++;
            }

            return state;
        }

        private bool TryGetState(string actorId, out ActionOwnerRuntimeState state)
        {
            state = null;
            var normalizedActorId = NormalizeId(actorId);
            return !string.IsNullOrWhiteSpace(normalizedActorId) && _states.TryGetValue(normalizedActorId, out state);
        }

        private static string NormalizeId(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private float GetTime()
        {
            return _timeProvider != null ? _timeProvider() : 0f;
        }

        private static float ResolveDurationSeconds(ComboAction action)
        {
            var duration = action != null && action.Animate != null ? action.Animate.ResolveDurationSeconds() : 0f;
            return duration > 0f ? duration : 0.01f;
        }

        private static string BuildTimelineEventKey(ActionTimelineEventData timelineEvent, int index)
        {
            if (timelineEvent == null)
            {
                return $"null:{index}";
            }

            if (!string.IsNullOrWhiteSpace(timelineEvent.EventId))
            {
                return timelineEvent.EventId.Trim();
            }

            // 未填写 EventId 时使用稳定字段去重，确保 TPC AnimationEvent 与 Tick 兜底能对齐。
            // 如果同一动作内存在同时间同类型的多个事件，请给它们配置不同 EventId 或 PayloadId。
            var payloadId = string.IsNullOrWhiteSpace(timelineEvent.PayloadId) ? "none" : timelineEvent.PayloadId.Trim();
            return $"{timelineEvent.Type}:{timelineEvent.NormalizedTime:0.###}:{payloadId}";
        }

        private static string ResolveTimelineEventKey(string explicitKey, ActionTimelineEventData timelineEvent)
        {
            return !string.IsNullOrWhiteSpace(explicitKey)
                ? explicitKey.Trim()
                : BuildTimelineEventKey(timelineEvent, -1);
        }

        private bool IsEventForThisService(string serviceInstanceId)
        {
            return !string.IsNullOrWhiteSpace(serviceInstanceId)
                && string.Equals(serviceInstanceId, ServiceInstanceId, StringComparison.Ordinal);
        }

        private void Publish<T>(T evt)
        {
            _eventBus?.Publish(evt, EventChannel.Immediate);
        }

        private void Subscribe(IEventBus eventBus)
        {
            if (eventBus == null)
            {
                return;
            }

            eventBus.Subscribe<ActionPlaybackPrecheckAcceptedEvent>(OnPrecheckAccepted);
            eventBus.Subscribe<ActionPlaybackPrecheckRejectedEvent>(OnPrecheckRejected);
            eventBus.Subscribe<ActionPlaybackCommittedEvent>(OnCommitted);
            eventBus.Subscribe<ActionPlaybackCommitRejectedEvent>(OnCommitRejected);
            eventBus.Subscribe<ActionPlaybackInterruptedEvent>(OnInterrupted);
            eventBus.Subscribe<ActionTimelineEventRaisedEvent>(OnTimelineEventRaised);
        }

        private void Unsubscribe(IEventBus eventBus)
        {
            if (eventBus == null)
            {
                return;
            }

            eventBus.Unsubscribe<ActionPlaybackPrecheckAcceptedEvent>(OnPrecheckAccepted);
            eventBus.Unsubscribe<ActionPlaybackPrecheckRejectedEvent>(OnPrecheckRejected);
            eventBus.Unsubscribe<ActionPlaybackCommittedEvent>(OnCommitted);
            eventBus.Unsubscribe<ActionPlaybackCommitRejectedEvent>(OnCommitRejected);
            eventBus.Unsubscribe<ActionPlaybackInterruptedEvent>(OnInterrupted);
            eventBus.Unsubscribe<ActionTimelineEventRaisedEvent>(OnTimelineEventRaised);
        }

        private void OnTimelineEventRaised(ActionTimelineEventRaisedEvent evt)
        {
            if (evt == null || evt.TimelineEvent == null || !IsEventForThisService(evt.ServiceInstanceId) || !TryGetState(evt.ActorId, out var state))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(evt.RequestId) || !string.Equals(state.RequestId, evt.RequestId, StringComparison.Ordinal))
            {
                return;
            }

            // 外部 AnimationEvent / 调试事件先触发时，也要写入已触发表，避免 Tick 兜底下一帧重复补发。
            state.FiredTimelineEventKeys.Add(ResolveTimelineEventKey(evt.TimelineEventKey, evt.TimelineEvent));
        }
    }
}
