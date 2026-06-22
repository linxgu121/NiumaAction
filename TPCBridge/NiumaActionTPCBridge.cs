using System;
using System.Collections.Generic;
using NiumaAction.Data;
using NiumaAction.Enum;
using NiumaAction.Event;
using NiumaAction.Service;
using NiumaCore.Event;
using NiumaCore.Module;
using NiumaTPC.Character;
using NiumaTPC.Character.Arbitration.ArbitrationRequest;
using UnityEngine;

namespace NiumaAction.TPCBridge
{
    /// <summary>
    /// 将 NiumaAction 的播放意图提交给 TPC ActionArbiter。
    /// 第一版不直接驱动 Animator，只写入 TPC 黑板并读取 TPC 当前 Override 结果作为确认。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NiumaActionTPCBridge : MonoBehaviour, IGameModule, INiumaActionTPCPlaybackGateway
    {
        [Header("TPC 角色")]
        [Tooltip("拖 PlayerRoot 上的 NiumaCharacterController。为空时会从当前物体和父物体查找。")]
        [SerializeField] private NiumaCharacterController characterController;

        [Tooltip("未手动绑定 NiumaCharacterController 时，是否自动从当前物体和父物体查找。")]
        [SerializeField] private bool autoResolveFromParents = true;

        [Header("Action 事件")]
        [Tooltip("当前桥接负责的 ActorId。建议与 NiumaActionController 调用 EquipWeaponSource / SubmitInput 时使用的 ActorId 一致，例如 player。为空时不过滤 ActorId。")]
        [SerializeField] private string actorId = "player";

        [Tooltip("是否订阅 NiumaAction 播放事件。正式核心场景应开启；关闭后只保留 Inspector 或外部手动调用接口。")]
        [SerializeField] private bool subscribeActionEvents = true;

        [Tooltip("提交 TPC 请求后是否本帧立即执行一次 ActionArbiter。开启后桥接可以立刻确认是否进入 Override；关闭时只能按提交成功乐观回传。")]
        [SerializeField] private bool flushImmediately = true;

        [Tooltip("当关闭“立即仲裁”时，是否在提交 TPC 黑板后直接回传 Committed。第一版没有异步仲裁回调，关闭时建议保持勾选。")]
        [SerializeField] private bool optimisticCommittedWhenNotFlushed = true;

        [Tooltip("动作已 Committed 后，若 TPC 当前 Override 被其它动作替换，是否发布 ActionPlaybackInterruptedEvent。自然播放结束不会被当成中断。")]
        [SerializeField] private bool detectOverrideInterruption = true;

        [Header("服务注册")]
        [Tooltip("是否把本脚本注册为 INiumaActionTPCPlaybackGateway。正式 EventBus 流程通常不需要；无 EventBus 调试提交时才开启。")]
        [SerializeField] private bool registerGatewayToContext;

        [Header("调试")]
        [Tooltip("缺少角色、动画 Clip、TPC 降级字段或提交失败时是否输出 Warning。")]
        [SerializeField] private bool logWarnings = true;

        private readonly Dictionary<string, TrackedPlayback> _trackedByRequestId = new(StringComparer.Ordinal);
        private readonly HashSet<string> _reportedUnsupportedFields = new(StringComparer.Ordinal);

        private GameContext _context;
        private IEventBus _eventBus;
        private bool _subscribed;
        private TrackedPlayback _currentPlayback;

        public string ModuleName => "NiumaAction.TPCBridge";

        public void Initialize(GameContext context)
        {
            _context = context;
            ResolveReferences();

            if (registerGatewayToContext && _context != null)
            {
                _context.RegisterService<INiumaActionTPCPlaybackGateway>(this);
            }

            if (subscribeActionEvents)
            {
                Subscribe(_context?.EventBus);
            }
        }

        public void StartModule()
        {
            ResolveReferences();
        }

        public void StopModule()
        {
            Unsubscribe();
            _trackedByRequestId.Clear();
            _currentPlayback = null;
        }

        public void Tick(float deltaTime)
        {
            if (!detectOverrideInterruption ||
                _currentPlayback == null ||
                !_currentPlayback.Committed ||
                _eventBus == null)
            {
                return;
            }

            if (!HasActiveOverride())
            {
                // Override 自然结束时不发布 Interrupted，正常完成由 ActionService 自己根据动作时长收口。
                return;
            }

            if (IsCurrentOverrideRequest(in _currentPlayback.TpcRequest))
            {
                return;
            }

            var playback = _currentPlayback;
            var requestId = playback.Request.RequestId;
            // TPC 没有独立中断回调时，只把“仍有 Override 但已不是本请求”视为被覆盖。
            Publish(new ActionPlaybackInterruptedEvent
            {
                ServiceInstanceId = playback.ServiceInstanceId,
                RequestId = requestId,
                ActorId = playback.Request.ActorId,
                Reason = "TPCOverrideReplaced"
            });
            ClearTrackedRequest(requestId);
        }

        public void SetEventBus(IEventBus eventBus)
        {
            if (ReferenceEquals(_eventBus, eventBus))
            {
                return;
            }

            Unsubscribe();
            if (subscribeActionEvents)
            {
                Subscribe(eventBus);
            }
        }

        public void SetCharacterController(NiumaCharacterController controller)
        {
            characterController = controller;
        }

        public void SubmitPrecheckPlayback(NiumaActionPlaybackRequest request)
        {
            // Gateway 是无 EventBus 调试提交口。没有 ServiceInstanceId 时无法完成正式回传，只做轻量校验和日志。
            if (!TryValidateRequest(request, out var message, out _))
            {
                Warn($"TPC 预检提交失败：{message}");
                return;
            }

            Warn("Gateway 预检只验证请求可提交，不会同步裁决；正式播放闭环请使用 EventBus。");
        }

        public void SubmitCommitPlayback(NiumaActionPlaybackRequest request)
        {
            if (!TrySubmitToTPC(request, out _, out var message))
            {
                Warn($"Gateway Commit 提交失败：{message}");
            }
        }

        public void CancelPlayback(string requestId, string actorId, string reason)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return;
            }

            _trackedByRequestId.TryGetValue(requestId, out var playback);
            if (playback == null &&
                _currentPlayback != null &&
                string.Equals(_currentPlayback.RequestId, requestId, StringComparison.Ordinal))
            {
                playback = _currentPlayback;
            }

            if (playback != null && playback.Committed)
            {
                // 正式 EventBus 流程也会进入这里：Cancelled / Interrupted 事件必须同步停止 TPC Override。
                CancelCurrentTPCOverride(playback, reason);
            }

            ClearTrackedRequest(requestId);
        }

        /// <summary>
        /// 给 Unity AnimationEvent 调用：参数填写 AnimateAsset.TimelineEvents 中的 EventId。
        /// </summary>
        public void RaiseTimelineEvent(string eventId)
        {
            if (!TryGetCurrentPlayback(out var playback))
            {
                Warn("无法抛出 Action 时间轴事件：当前没有已提交的动作请求。");
                return;
            }

            if (!TryFindTimelineEvent(playback.Request, eventId, out var timelineEvent, out var index))
            {
                Warn($"无法抛出 Action 时间轴事件：未找到 EventId={eventId}。");
                return;
            }

            PublishTimelineEvent(playback, timelineEvent, index, ResolveCurrentNormalizedTime(playback, timelineEvent));
        }

        /// <summary>
        /// 给 Unity AnimationEvent 调用：参数填写 TimelineEvents 数组下标。
        /// </summary>
        public void RaiseTimelineEventByIndex(int eventIndex)
        {
            if (!TryGetCurrentPlayback(out var playback))
            {
                Warn("无法抛出 Action 时间轴事件：当前没有已提交的动作请求。");
                return;
            }

            var events = playback.Request != null && playback.Request.Animate != null
                ? playback.Request.Animate.TimelineEvents
                : null;

            if (events == null || eventIndex < 0 || eventIndex >= events.Length || events[eventIndex] == null)
            {
                Warn($"无法抛出 Action 时间轴事件：TimelineEvents 下标无效。Index={eventIndex}");
                return;
            }

            PublishTimelineEvent(playback, events[eventIndex], eventIndex, ResolveCurrentNormalizedTime(playback, events[eventIndex]));
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            if (_context != null && subscribeActionEvents)
            {
                Subscribe(_context.EventBus);
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (registerGatewayToContext && _context != null)
            {
                var current = _context.GetService<INiumaActionTPCPlaybackGateway>();
                if (ReferenceEquals(current, this))
                {
                    _context.UnregisterService<INiumaActionTPCPlaybackGateway>();
                }
            }
        }

        private void OnValidate()
        {
            actorId = actorId != null ? actorId.Trim() : string.Empty;
        }

        private void HandlePrecheckRequested(ActionPlaybackPrecheckRequestedEvent evt)
        {
            if (evt == null || !ShouldHandle(evt.Request))
            {
                return;
            }

            var request = evt.Request;
            if (!TryValidateRequest(request, out var message, out _))
            {
                Publish(new ActionPlaybackPrecheckRejectedEvent
                {
                    ServiceInstanceId = evt.ServiceInstanceId,
                    RequestId = request != null ? request.RequestId : null,
                    ActorId = request != null ? request.ActorId : null,
                    FailureReason = ActionFailureReason.TpcPlaybackRejected,
                    Message = message
                });
                return;
            }

            _trackedByRequestId[request.RequestId] = new TrackedPlayback(evt.ServiceInstanceId, request.Clone());
            Publish(new ActionPlaybackPrecheckAcceptedEvent
            {
                ServiceInstanceId = evt.ServiceInstanceId,
                RequestId = request.RequestId,
                ActorId = request.ActorId,
                NodeId = request.NodeId,
                Message = "TPCBridge 预检通过，等待 ActionService 扣除资源后提交 Commit。"
            });
        }

        private void HandleCommitRequested(ActionPlaybackCommitRequestedEvent evt)
        {
            if (evt == null || !ShouldHandle(evt.Request))
            {
                return;
            }

            var request = evt.Request;
            if (!TrySubmitToTPC(request, out var tpcRequest, out var message))
            {
                PublishCommitRejected(evt, ActionFailureReason.TpcPlaybackRejected, message);
                return;
            }

            if (flushImmediately && !IsCurrentOverrideRequest(in tpcRequest))
            {
                PublishCommitRejected(evt, ActionFailureReason.TpcPlaybackRejected, "TPC ActionArbiter 未接受本次 ActionRequest。");
                return;
            }

            if (!flushImmediately && !optimisticCommittedWhenNotFlushed)
            {
                PublishCommitRejected(evt, ActionFailureReason.TpcPlaybackRejected, "未立即仲裁且未开启乐观 Committed 回传。");
                return;
            }

            var playback = _trackedByRequestId.TryGetValue(request.RequestId, out var tracked)
                ? tracked
                : new TrackedPlayback(evt.ServiceInstanceId, request.Clone());

            playback.MarkCommitted(tpcRequest);
            _trackedByRequestId[request.RequestId] = playback;
            _currentPlayback = playback;

            Publish(new ActionPlaybackCommittedEvent
            {
                ServiceInstanceId = evt.ServiceInstanceId,
                RequestId = request.RequestId,
                ActorId = request.ActorId,
                NodeId = request.NodeId,
                ActionId = request.ActionId,
                Request = request.Clone()
            });
        }

        private void HandleCancelled(ActionPlaybackCancelledEvent evt)
        {
            if (evt == null || !ShouldHandle(evt.ActorId))
            {
                return;
            }

            CancelPlayback(evt.RequestId, evt.ActorId, evt.Reason);
        }

        private void HandleInterrupted(ActionPlaybackInterruptedEvent evt)
        {
            if (evt == null || !ShouldHandle(evt.ActorId))
            {
                return;
            }

            CancelPlayback(evt.RequestId, evt.ActorId, evt.Reason);
        }

        private void PublishCommitRejected(ActionPlaybackCommitRequestedEvent evt, ActionFailureReason reason, string message)
        {
            Publish(new ActionPlaybackCommitRejectedEvent
            {
                ServiceInstanceId = evt.ServiceInstanceId,
                RequestId = evt.RequestId,
                ActorId = evt.ActorId,
                FailureReason = reason,
                Message = message
            });
        }

        private bool TryValidateRequest(NiumaActionPlaybackRequest request, out string message, out ActionRequest tpcRequest)
        {
            tpcRequest = default;
            if (request == null)
            {
                message = "NiumaActionPlaybackRequest 为空。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.RequestId))
            {
                message = "RequestId 为空。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.ActorId))
            {
                message = "ActorId 为空。";
                return false;
            }

            if (!ResolveReferences())
            {
                message = "未找到 NiumaCharacterController。请把 PlayerRoot 上的 NiumaCharacterController 拖到本脚本。";
                return false;
            }

            if (request.Animate == null)
            {
                message = "AnimateAsset 为空。请在 ComboAction.Animate 中绑定动作资产。";
                return false;
            }

            if (request.Animate.Clip == null)
            {
                message = "AnimateAsset.Clip 为空，TPC 无法播放动作。";
                return false;
            }

            var priority = request.Priority >= 0 ? request.Priority : request.Animate.Priority;
            var fadeIn = request.FadeInSeconds >= 0f ? request.FadeInSeconds : request.Animate.FadeInSeconds;
            tpcRequest = new ActionRequest(request.Animate.Clip, priority, Mathf.Max(0f, fadeIn), request.ApplyGravity);
            message = null;
            return true;
        }

        private bool TrySubmitToTPC(NiumaActionPlaybackRequest request, out ActionRequest tpcRequest, out string message)
        {
            if (!TryValidateRequest(request, out message, out tpcRequest))
            {
                return false;
            }

            WarnUnsupportedAnimateFields(request);
            characterController.RequestOverride(in tpcRequest, flushImmediately);
            message = "已提交到 TPC ActionArbitration。";
            return true;
        }

        private bool IsCurrentOverrideRequest(in ActionRequest request)
        {
            return characterController != null && characterController.IsCurrentOverride(in request);
        }

        private bool HasActiveOverride()
        {
            var data = characterController != null ? characterController.RuntimeData : null;
            return data != null && data.Override.IsActive;
        }

        private bool ResolveReferences()
        {
            if (characterController != null)
            {
                return true;
            }

            if (!autoResolveFromParents)
            {
                return false;
            }

            characterController = GetComponent<NiumaCharacterController>();
            if (characterController != null)
            {
                return true;
            }

            characterController = GetComponentInParent<NiumaCharacterController>();
            return characterController != null;
        }

        private void Subscribe(IEventBus eventBus)
        {
            if (_subscribed || eventBus == null)
            {
                return;
            }

            _eventBus = eventBus;
            _eventBus.Subscribe<ActionPlaybackPrecheckRequestedEvent>(HandlePrecheckRequested);
            _eventBus.Subscribe<ActionPlaybackCommitRequestedEvent>(HandleCommitRequested);
            _eventBus.Subscribe<ActionPlaybackCancelledEvent>(HandleCancelled);
            _eventBus.Subscribe<ActionPlaybackInterruptedEvent>(HandleInterrupted);
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _eventBus == null)
            {
                _subscribed = false;
                _eventBus = null;
                return;
            }

            _eventBus.Unsubscribe<ActionPlaybackPrecheckRequestedEvent>(HandlePrecheckRequested);
            _eventBus.Unsubscribe<ActionPlaybackCommitRequestedEvent>(HandleCommitRequested);
            _eventBus.Unsubscribe<ActionPlaybackCancelledEvent>(HandleCancelled);
            _eventBus.Unsubscribe<ActionPlaybackInterruptedEvent>(HandleInterrupted);
            _subscribed = false;
            _eventBus = null;
        }

        private bool ShouldHandle(NiumaActionPlaybackRequest request)
        {
            return request != null && ShouldHandle(request.ActorId);
        }

        private bool ShouldHandle(string eventActorId)
        {
            if (string.IsNullOrWhiteSpace(actorId))
            {
                return true;
            }

            return string.Equals(actorId, eventActorId, StringComparison.Ordinal);
        }

        private void ClearTrackedRequest(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return;
            }

            _trackedByRequestId.Remove(requestId);
            if (_currentPlayback != null && string.Equals(_currentPlayback.RequestId, requestId, StringComparison.Ordinal))
            {
                _currentPlayback = null;
            }
        }

        private bool TryGetCurrentPlayback(out TrackedPlayback playback)
        {
            playback = _currentPlayback;
            return playback != null && playback.Request != null;
        }

        private static bool TryFindTimelineEvent(NiumaActionPlaybackRequest request, string eventId, out ActionTimelineEventData timelineEvent, out int index)
        {
            timelineEvent = null;
            index = -1;
            var events = request != null && request.Animate != null ? request.Animate.TimelineEvents : null;
            if (events == null || events.Length == 0 || string.IsNullOrWhiteSpace(eventId))
            {
                return false;
            }

            var normalizedEventId = eventId.Trim();
            for (var i = 0; i < events.Length; i++)
            {
                var candidate = events[i];
                if (candidate == null)
                {
                    continue;
                }

                if (!string.Equals(candidate.EventId, normalizedEventId, StringComparison.Ordinal))
                {
                    continue;
                }

                timelineEvent = candidate;
                index = i;
                return true;
            }

            return false;
        }

        private void PublishTimelineEvent(TrackedPlayback playback, ActionTimelineEventData timelineEvent, int index, float normalizedTime)
        {
            if (_eventBus == null)
            {
                Warn("无法发布 ActionTimelineEventRaisedEvent：未绑定 EventBus。");
                return;
            }

            var request = playback.Request;
            _eventBus.Publish(new ActionTimelineEventRaisedEvent
            {
                ServiceInstanceId = playback.ServiceInstanceId,
                RequestId = request.RequestId,
                AttackInstanceId = ActionAttackInstanceId.Resolve(request.RequestId, timelineEvent),
                ActorId = request.ActorId,
                NodeId = request.NodeId,
                ActionId = request.ActionId,
                TimelineEventKey = BuildTimelineEventKey(timelineEvent, index),
                TimelineEvent = timelineEvent.Clone(),
                NormalizedTime = normalizedTime,
                Source = ActionTimelineEventSource.TpcAnimationEvent
            }, EventChannel.Immediate);
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

            var payloadId = string.IsNullOrWhiteSpace(timelineEvent.PayloadId) ? "none" : timelineEvent.PayloadId.Trim();
            return $"{timelineEvent.Type}:{timelineEvent.NormalizedTime:0.###}:{payloadId}";
        }

        private float ResolveCurrentNormalizedTime(TrackedPlayback playback, ActionTimelineEventData timelineEvent)
        {
            var configuredTime = timelineEvent != null ? Mathf.Clamp01(timelineEvent.NormalizedTime) : 0f;
            var animate = playback != null && playback.Request != null ? playback.Request.Animate : null;
            var duration = animate != null ? animate.ResolveDurationSeconds() : 0f;

            if (duration <= 0f || characterController == null || characterController.AnimationFacade == null)
            {
                return configuredTime;
            }

            // AnimationEvent 来源优先使用 TPC 当前动画时间；拿不到有效时才回退资产配置时间。
            return Mathf.Clamp01(characterController.AnimationFacade.CurrentTime / duration);
        }

        private void CancelCurrentTPCOverride(TrackedPlayback playback, string reason)
        {
            if (playback == null || characterController == null)
            {
                return;
            }

            if (!characterController.IsCurrentOverride(in playback.TpcRequest))
            {
                return;
            }

            if (!characterController.TryCancelCurrentOverride(in playback.TpcRequest, reason))
            {
                Warn($"TPC Override 取消失败：RequestId={playback.RequestId}，Reason={reason}");
            }
        }

        private void Publish<T>(T evt)
        {
            _eventBus?.Publish(evt, EventChannel.Immediate);
        }

        private void WarnUnsupportedAnimateFields(NiumaActionPlaybackRequest request)
        {
            var animate = request != null ? request.Animate : null;
            if (animate == null || !logWarnings)
            {
                return;
            }

            if (animate.AvatarMask != null)
            {
                WarnUnsupportedOnce(animate, "AvatarMask", $"AnimateAsset={animate.AnimateId} 配置了 AvatarMask；TPC 当前 ActionRequest 暂未接收该字段，本次按全身 Override 降级。");
            }

            if (animate.LayerMode != ActionAnimationLayerMode.BaseLayer && animate.LayerMode != ActionAnimationLayerMode.FullBodyOverride)
            {
                WarnUnsupportedOnce(animate, "LayerMode", $"AnimateAsset={animate.AnimateId} LayerMode={animate.LayerMode} 暂未由 TPCBridge 精确映射，本次按全身 Override 降级。");
            }

            if (animate.FadeOutSeconds > 0f)
            {
                WarnUnsupportedOnce(animate, "FadeOutSeconds", $"AnimateAsset={animate.AnimateId} FadeOutSeconds 暂未由 TPC ActionRequest 接收；退出淡出以 TPC OverrideState 为准。");
            }
        }

        private void WarnUnsupportedOnce(NiumaAction.Config.AnimateAsset animate, string fieldName, string message)
        {
            var animateId = !string.IsNullOrWhiteSpace(animate.AnimateId) ? animate.AnimateId.Trim() : animate.GetInstanceID().ToString();
            var key = $"{animateId}:{fieldName}";
            if (_reportedUnsupportedFields.Add(key))
            {
                Warn(message);
            }
        }

        private void Warn(string message)
        {
            if (logWarnings)
            {
                Debug.LogWarning($"[NiumaActionTPCBridge] {message}", this);
            }
        }

        private sealed class TrackedPlayback
        {
            public readonly string ServiceInstanceId;
            public readonly NiumaActionPlaybackRequest Request;
            public ActionRequest TpcRequest;
            public bool Committed;

            public TrackedPlayback(string serviceInstanceId, NiumaActionPlaybackRequest request)
            {
                ServiceInstanceId = serviceInstanceId;
                Request = request;
            }

            public string RequestId => Request != null ? Request.RequestId : null;

            public void MarkCommitted(ActionRequest request)
            {
                TpcRequest = request;
                Committed = true;
            }
        }
    }
}
