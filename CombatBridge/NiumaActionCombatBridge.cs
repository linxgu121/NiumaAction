using System;
using System.Collections.Generic;
using NiumaAction.Enum;
using NiumaAction.Event;
using NiumaCombat.Data;
using NiumaCombat.Service;
using NiumaCore.Event;
using NiumaCore.Module;
using UnityEngine;

namespace NiumaAction.CombatBridge
{
    /// <summary>
    /// 将 NiumaAction 的时间轴事件转换成 Combat Hitbox 开关。
    /// Runtime 不直接引用 Combat；只有这个桥接程序集同时认识 Action 与 Combat。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NiumaActionCombatBridge : MonoBehaviour, IGameModule
    {
        [Header("Combat 服务")]
        [Tooltip("通常拖核心场景 CombatRoot 上的 NiumaCombatController。为空时会尝试从 GameContext 获取 ICombatHitboxService。")]
        [SerializeField] private MonoBehaviour combatRuntimeServiceProvider;

        [Tooltip("未手动绑定 NiumaCombatController 时，是否从 GameContext.GetService<ICombatHitboxService>() 获取 Combat Hitbox 服务。")]
        [SerializeField] private bool resolveCombatFromContext = true;

        [Header("Action 事件过滤")]
        [Tooltip("只处理指定 ActionServiceInstanceId 的事件。为空时不按 ServiceInstanceId 过滤。多 ActionService 场景建议填写。")]
        [SerializeField] private string actionServiceInstanceId;

        [Tooltip("只处理指定 ActorId 的事件。建议与 NiumaActionController.SubmitInput 使用的 ActorId 一致，例如 player。为空时不过滤 ActorId。")]
        [SerializeField] private string actorId = "player";

        [Tooltip("是否订阅 NiumaAction 时间轴和取消事件。正式核心场景应开启。")]
        [SerializeField] private bool subscribeActionEvents = true;

        [Header("Hitbox 配置")]
        [Tooltip("是否启用默认 Hitbox 定义。关闭后，TimelineEvent 必须通过 Hitbox Bindings 找到匹配配置。")]
        [SerializeField] private bool useDefaultHitboxDefinition;

        [Tooltip("默认 Hitbox 定义。TimelineEvent.PayloadId 找不到绑定时会使用它；为空且无匹配绑定时不会打开 Hitbox。")]
        [SerializeField] private CombatHitboxDefinition defaultHitboxDefinition = new CombatHitboxDefinition();

        [Tooltip("按 TimelineEvent.PayloadId 或 EventId 绑定 CombatHitboxDefinition。Key 例：sword_light / blade / hitbox_main。")]
        [SerializeField] private ActionCombatHitboxBinding[] hitboxBindings = Array.Empty<ActionCombatHitboxBinding>();

        [Header("清理策略")]
        [Tooltip("收到 ActionPlaybackCancelledEvent 时，是否关闭该 RequestId 下所有已打开 Hitbox。建议开启，避免连招切换或取消时 Hitbox 泄漏。")]
        [SerializeField] private bool closeHitboxesOnCancelled = true;

        [Tooltip("收到 ActionPlaybackInterruptedEvent 时，是否关闭该 RequestId 下所有已打开 Hitbox。建议开启，避免 TPC 覆盖动作后 Hitbox 泄漏。")]
        [SerializeField] private bool closeHitboxesOnInterrupted = true;

        [Header("调试")]
        [Tooltip("缺少 Combat 服务、Hitbox 配置或重复开关时是否输出 Warning。")]
        [SerializeField] private bool logWarnings = true;

        private readonly Dictionary<string, ActiveCombatHitbox> _activeByActionAttackId = new Dictionary<string, ActiveCombatHitbox>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _actionAttackIdsByRequestId = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        private GameContext _context;
        private IEventBus _eventBus;
        private ICombatHitboxService _combatHitboxService;
        private bool _subscribed;

        public string ModuleName => "NiumaAction.CombatBridge";

        public void Initialize(GameContext context)
        {
            _context = context;
            ResolveCombatService();

            if (subscribeActionEvents)
            {
                Subscribe(_context?.EventBus);
            }
        }

        public void StartModule()
        {
            ResolveCombatService();
        }

        public void StopModule()
        {
            CloseAllTrackedHitboxes("StopModule");
            Unsubscribe();
        }

        public void Tick(float deltaTime)
        {
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

        public void SetCombatHitboxService(ICombatHitboxService hitboxService)
        {
            _combatHitboxService = hitboxService;
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
            CloseAllTrackedHitboxes("OnDisable");
            Unsubscribe();
        }

        private void OnDestroy()
        {
            CloseAllTrackedHitboxes("OnDestroy");
            Unsubscribe();
        }

        private void OnValidate()
        {
            actionServiceInstanceId = actionServiceInstanceId != null ? actionServiceInstanceId.Trim() : string.Empty;
            actorId = actorId != null ? actorId.Trim() : string.Empty;
            hitboxBindings ??= Array.Empty<ActionCombatHitboxBinding>();

            for (var i = 0; i < hitboxBindings.Length; i++)
            {
                hitboxBindings[i]?.Normalize();
            }
        }

        private void HandleTimelineEvent(ActionTimelineEventRaisedEvent evt)
        {
            if (evt == null || !ShouldHandle(evt.ServiceInstanceId, evt.ActorId))
            {
                return;
            }

            var timelineEvent = evt.TimelineEvent;
            if (timelineEvent == null)
            {
                Warn("忽略 Action 时间轴事件：TimelineEvent 为空。");
                return;
            }

            switch (timelineEvent.Type)
            {
                case ActionTimelineEventType.HitboxOpen:
                    OpenHitbox(evt);
                    break;

                case ActionTimelineEventType.HitboxClose:
                    CloseHitbox(evt);
                    break;
            }
        }

        private void HandleCancelled(ActionPlaybackCancelledEvent evt)
        {
            if (evt == null || !closeHitboxesOnCancelled || !ShouldHandle(evt.ServiceInstanceId, evt.ActorId))
            {
                return;
            }

            CloseHitboxesForRequest(evt.RequestId, evt.Reason);
        }

        private void HandleInterrupted(ActionPlaybackInterruptedEvent evt)
        {
            if (evt == null || !closeHitboxesOnInterrupted || !ShouldHandle(evt.ServiceInstanceId, evt.ActorId))
            {
                return;
            }

            CloseHitboxesForRequest(evt.RequestId, evt.Reason);
        }

        private void OpenHitbox(ActionTimelineEventRaisedEvent evt)
        {
            if (!ResolveCombatService())
            {
                Warn("无法打开 Combat Hitbox：未找到 ICombatHitboxService。请拖入 NiumaCombatController，或确保 NiumaCombatController 已注册到 GameContext。");
                return;
            }

            var actionAttackId = ResolveActionAttackInstanceId(evt);
            if (string.IsNullOrWhiteSpace(actionAttackId))
            {
                Warn("无法打开 Combat Hitbox：AttackInstanceId 为空。");
                return;
            }

            if (_activeByActionAttackId.ContainsKey(actionAttackId))
            {
                // 同一 RequestId + PayloadId 重复 Open 时先关闭旧实例，避免 Combat 服务残留旧 Hitbox。
                CloseTrackedHitbox(actionAttackId, "DuplicateHitboxOpen");
            }

            var hitboxKey = ResolveHitboxKey(evt);
            var definition = ResolveDefinition(hitboxKey);
            if (definition == null)
            {
                Warn($"无法打开 Combat Hitbox：未找到 PayloadId/EventId={hitboxKey} 的 Hitbox 配置，也没有默认 Hitbox。");
                return;
            }

            var runtimeDefinition = definition.Clone();
            if (string.IsNullOrWhiteSpace(runtimeDefinition.HitboxId))
            {
                runtimeDefinition.HitboxId = hitboxKey;
            }

            var combatAttackId = _combatHitboxService.OpenHitbox(runtimeDefinition, evt.ActorId);
            if (string.IsNullOrWhiteSpace(combatAttackId))
            {
                Warn($"Combat Hitbox 打开失败：ActorId={evt.ActorId}, HitboxKey={hitboxKey}");
                return;
            }

            _activeByActionAttackId[actionAttackId] = new ActiveCombatHitbox(evt.RequestId, actionAttackId, combatAttackId);
            RegisterRequestMapping(evt.RequestId, actionAttackId);
        }

        private void CloseHitbox(ActionTimelineEventRaisedEvent evt)
        {
            var actionAttackId = ResolveActionAttackInstanceId(evt);
            if (string.IsNullOrWhiteSpace(actionAttackId))
            {
                Warn("无法关闭 Combat Hitbox：AttackInstanceId 为空。");
                return;
            }

            if (!CloseTrackedHitbox(actionAttackId, "HitboxClose"))
            {
                Warn($"关闭 Combat Hitbox 时未找到已打开实例：ActionAttackInstanceId={actionAttackId}");
            }
        }

        private bool CloseTrackedHitbox(string actionAttackId, string reason)
        {
            if (string.IsNullOrWhiteSpace(actionAttackId))
            {
                return false;
            }

            if (!_activeByActionAttackId.TryGetValue(actionAttackId, out var active) || active == null)
            {
                return false;
            }

            ResolveCombatService();
            _combatHitboxService?.CloseHitbox(active.CombatAttackInstanceId);
            _activeByActionAttackId.Remove(actionAttackId);
            UnregisterRequestMapping(active.RequestId, actionAttackId);
            return true;
        }

        private void CloseHitboxesForRequest(string requestId, string reason)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return;
            }

            if (!_actionAttackIdsByRequestId.TryGetValue(requestId, out var actionAttackIds) || actionAttackIds == null || actionAttackIds.Count == 0)
            {
                return;
            }

            var copy = actionAttackIds.ToArray();
            for (var i = 0; i < copy.Length; i++)
            {
                CloseTrackedHitbox(copy[i], reason);
            }

            _actionAttackIdsByRequestId.Remove(requestId);
        }

        private void CloseAllTrackedHitboxes(string reason)
        {
            if (_activeByActionAttackId.Count == 0)
            {
                return;
            }

            var keys = new string[_activeByActionAttackId.Count];
            _activeByActionAttackId.Keys.CopyTo(keys, 0);
            for (var i = 0; i < keys.Length; i++)
            {
                CloseTrackedHitbox(keys[i], reason);
            }

            _activeByActionAttackId.Clear();
            _actionAttackIdsByRequestId.Clear();
        }

        private CombatHitboxDefinition ResolveDefinition(string hitboxKey)
        {
            if (!string.IsNullOrWhiteSpace(hitboxKey) && hitboxBindings != null)
            {
                for (var i = 0; i < hitboxBindings.Length; i++)
                {
                    var binding = hitboxBindings[i];
                    if (binding == null || !binding.Matches(hitboxKey))
                    {
                        continue;
                    }

                    return binding.Definition;
                }
            }

            return useDefaultHitboxDefinition ? defaultHitboxDefinition : null;
        }

        private static string ResolveActionAttackInstanceId(ActionTimelineEventRaisedEvent evt)
        {
            if (!string.IsNullOrWhiteSpace(evt.AttackInstanceId))
            {
                return evt.AttackInstanceId.Trim();
            }

            return NiumaAction.Data.ActionAttackInstanceId.Resolve(evt.RequestId, evt.TimelineEvent);
        }

        private static string ResolveHitboxKey(ActionTimelineEventRaisedEvent evt)
        {
            var timelineEvent = evt.TimelineEvent;
            if (timelineEvent != null && !string.IsNullOrWhiteSpace(timelineEvent.PayloadId))
            {
                return timelineEvent.PayloadId.Trim();
            }

            if (timelineEvent != null && !string.IsNullOrWhiteSpace(timelineEvent.EventId))
            {
                return timelineEvent.EventId.Trim();
            }

            return !string.IsNullOrWhiteSpace(evt.TimelineEventKey) ? evt.TimelineEventKey.Trim() : "default";
        }

        private void RegisterRequestMapping(string requestId, string actionAttackId)
        {
            if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(actionAttackId))
            {
                return;
            }

            if (!_actionAttackIdsByRequestId.TryGetValue(requestId, out var list) || list == null)
            {
                list = new List<string>();
                _actionAttackIdsByRequestId[requestId] = list;
            }

            if (!list.Contains(actionAttackId))
            {
                list.Add(actionAttackId);
            }
        }

        private void UnregisterRequestMapping(string requestId, string actionAttackId)
        {
            if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(actionAttackId))
            {
                return;
            }

            if (!_actionAttackIdsByRequestId.TryGetValue(requestId, out var list) || list == null)
            {
                return;
            }

            list.Remove(actionAttackId);
            if (list.Count == 0)
            {
                _actionAttackIdsByRequestId.Remove(requestId);
            }
        }

        private bool ResolveCombatService()
        {
            if (_combatHitboxService != null)
            {
                return true;
            }

            if (combatRuntimeServiceProvider is ICombatRuntimeServiceProvider provider)
            {
                _combatHitboxService = provider.CombatHitboxService;
                if (_combatHitboxService != null)
                {
                    return true;
                }
            }

            if (resolveCombatFromContext && _context != null)
            {
                _combatHitboxService = _context.GetService<ICombatHitboxService>();
            }

            return _combatHitboxService != null;
        }

        private void Subscribe(IEventBus eventBus)
        {
            if (_subscribed || eventBus == null)
            {
                return;
            }

            _eventBus = eventBus;
            _eventBus.Subscribe<ActionTimelineEventRaisedEvent>(HandleTimelineEvent);
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

            _eventBus.Unsubscribe<ActionTimelineEventRaisedEvent>(HandleTimelineEvent);
            _eventBus.Unsubscribe<ActionPlaybackCancelledEvent>(HandleCancelled);
            _eventBus.Unsubscribe<ActionPlaybackInterruptedEvent>(HandleInterrupted);
            _subscribed = false;
            _eventBus = null;
        }

        private bool ShouldHandle(string serviceInstanceId, string eventActorId)
        {
            if (!string.IsNullOrWhiteSpace(actionServiceInstanceId) &&
                !string.Equals(actionServiceInstanceId, serviceInstanceId, StringComparison.Ordinal))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(actorId) ||
                   string.Equals(actorId, eventActorId, StringComparison.Ordinal);
        }

        private void Warn(string message)
        {
            if (logWarnings)
            {
                Debug.LogWarning($"[NiumaActionCombatBridge] {message}", this);
            }
        }

        [Serializable]
        private sealed class ActionCombatHitboxBinding
        {
            [Tooltip("匹配 TimelineEvent.PayloadId。PayloadId 为空时也可匹配 EventId。例：blade / sword_light / hitbox_main。")]
            public string Key;

            [Tooltip("打开 Hitbox 时传给 Combat 的定义。DamageTemplate 中 SourceActorId、TargetActorId 等运行时字段不要预填。")]
            public CombatHitboxDefinition Definition = new CombatHitboxDefinition();

            public void Normalize()
            {
                Key = Key != null ? Key.Trim() : string.Empty;
            }

            public bool Matches(string key)
            {
                return !string.IsNullOrWhiteSpace(Key) &&
                       string.Equals(Key, key, StringComparison.Ordinal);
            }
        }

        private sealed class ActiveCombatHitbox
        {
            public readonly string RequestId;
            public readonly string ActionAttackInstanceId;
            public readonly string CombatAttackInstanceId;

            public ActiveCombatHitbox(string requestId, string actionAttackInstanceId, string combatAttackInstanceId)
            {
                RequestId = requestId;
                ActionAttackInstanceId = actionAttackInstanceId;
                CombatAttackInstanceId = combatAttackInstanceId;
            }
        }
    }
}
