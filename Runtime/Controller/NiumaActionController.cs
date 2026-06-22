using System;
using NiumaAction.Config;
using NiumaAction.Data;
using NiumaAction.Service;
using NiumaAttribute.Controller;
using NiumaAttribute.Service;
using NiumaCore.Event;
using NiumaCore.Module;
using UnityEngine;

namespace NiumaAction.Controller
{
    [DisallowMultipleComponent]
    public sealed class NiumaActionController : MonoBehaviour, IGameModule
    {
        [Header("动作资产")]
        [Tooltip("可用武器动作来源。请拖入 MeleeWeaponSource；运行时可通过 EquipWeaponSource 为 Actor 装备其中一个。")]
        [SerializeField] private MeleeWeaponSource[] weaponSources = Array.Empty<MeleeWeaponSource>();

        [Tooltip("可用连招树索引。通常 MeleeWeaponSource 会直接引用默认 ComboTree；这里用于运行时按 ID 查询或调试。")]
        [SerializeField] private ComboTreeAsset[] defaultComboTrees = Array.Empty<ComboTreeAsset>();

        [Header("属性依赖")]
        [Tooltip("属性模块控制器。没有统一 GameContext 时拖核心场景里的 NiumaAttributeController，用于体力查询和扣除。")]
        [SerializeField] private NiumaAttributeController attributeController;

        [Tooltip("初始化时是否尝试从 GameContext 解析 IAttributeQuery / IAttributeCommand。核心场景已注册 Attribute 时建议开启。")]
        [SerializeField] private bool resolveAttributeFromContext = true;

        [Tooltip("动作体力资源 ID。必须与 NiumaAttribute 的 ResourceDefinition.ResourceId 一致；默认 stamina。")]
        [SerializeField] private string staminaResourceId = "stamina";

        [Header("可选扩展")]
        [Tooltip("动作条件解析器。请拖实现 IActionConditionResolver 的组件；为空时 Custom 条件会失败并返回结构化错误。")]
        [SerializeField] private MonoBehaviour conditionResolverProvider;

        [Tooltip("TPC 播放网关。正式核心场景中 NiumaActionTPCBridge 应通过 EventBus 订阅播放意图；这里仅给无 EventBus 的调试场景绑定提交口，不能同步裁决播放结果。")]
        [SerializeField] private MonoBehaviour playbackGatewayProvider;

        [Header("模块启动")]
        [Tooltip("Awake 时自动初始化。核心场景使用统一模块启动器时可以关闭。")]
        [SerializeField] private bool initializeOnAwake = true;

        [Tooltip("OnEnable 时自动启动模块。核心场景使用统一 StartModule 时可以关闭。")]
        [SerializeField] private bool startOnEnable = true;

        [Tooltip("初始化时是否把 IActionService / IActionQuery / IActionCommand 注册到 GameContext。核心场景建议开启。")]
        [SerializeField] private bool registerServiceToContext = true;

        [Tooltip("是否由本控制器 Update 自动驱动 Tick。若已有统一模块启动器调用 IGameModule.Tick，请关闭，避免动作时间推进两次。")]
        [SerializeField] private bool driveTickInUpdate = true;

        [Header("Inspector 调试")]
        [Tooltip("调试 ActorId。建议玩家使用 player；NPC 或本地多角色请使用稳定唯一 ID。")]
        [SerializeField] private string debugActorId = "player";

        [Tooltip("调试输入 ID，例如 attack_light 或 attack_heavy。")]
        [SerializeField] private string debugInputId = "attack_light";

        private NiumaActionService _actionService;
        private IActionConfigurationService _configurationService;
        private GameContext _context;
        private IAttributeQuery _attributeQuery;
        private IAttributeCommand _attributeCommand;
        private IAttributeQuery _externalAttributeQuery;
        private IAttributeCommand _externalAttributeCommand;
        private IActionConditionResolver _externalConditionResolver;
        private INiumaActionTPCPlaybackGateway _externalPlaybackGateway;
        private IEventBus _externalEventBus;
        private bool _attributeDependencyLocked;
        private bool _conditionResolverLocked;
        private bool _playbackGatewayLocked;
        private bool _eventBusLocked;

        public string ModuleName => "NiumaAction";
        public IActionService ActionService => _actionService;
        public IActionQuery ActionQuery => _actionService;
        public IActionCommand ActionCommand => _actionService;
        public bool IsInitialized { get; private set; }
        public bool IsRunning { get; private set; }
        public long ActionRevision => _actionService != null ? _actionService.Revision : 0L;

        private void Awake()
        {
            if (initializeOnAwake && !IsInitialized)
            {
                Initialize(null);
            }
        }

        private void OnEnable()
        {
            if (startOnEnable && IsInitialized && !IsRunning)
            {
                StartModule();
            }
        }

        private void Update()
        {
            if (driveTickInUpdate && IsRunning)
            {
                Tick(Time.deltaTime);
            }
        }

        private void OnDisable()
        {
            if (IsRunning)
            {
                StopModule();
            }
        }

        private void OnDestroy()
        {
            UnregisterServicesFromContext(_context);
            _configurationService?.SetEventBus(null);
            _actionService = null;
            _configurationService = null;
            IsInitialized = false;
            IsRunning = false;
        }

        private void OnValidate()
        {
            staminaResourceId = string.IsNullOrWhiteSpace(staminaResourceId) ? "stamina" : staminaResourceId.Trim();
            debugActorId = string.IsNullOrWhiteSpace(debugActorId) ? "player" : debugActorId.Trim();
            debugInputId = string.IsNullOrWhiteSpace(debugInputId) ? "attack_light" : debugInputId.Trim();

            if (conditionResolverProvider != null && !(conditionResolverProvider is IActionConditionResolver))
            {
                Debug.LogWarning("[NiumaAction] Condition Resolver Provider 未实现 IActionConditionResolver，运行时会被忽略。", this);
            }

            if (playbackGatewayProvider != null && !(playbackGatewayProvider is INiumaActionTPCPlaybackGateway))
            {
                Debug.LogWarning("[NiumaAction] Playback Gateway Provider 未实现 INiumaActionTPCPlaybackGateway，运行时会被忽略。", this);
            }
        }

        public void Initialize(GameContext context)
        {
            var previousService = _actionService;
            var previousConfiguration = _configurationService;
            var previousContext = _context;
            var previousInitialized = IsInitialized;
            var previousRunning = IsRunning;
            var targetContext = context ?? _context;
            var oldService = targetContext != null ? targetContext.GetService<IActionService>() : null;
            var oldQuery = targetContext != null ? targetContext.GetService<IActionQuery>() : null;
            var oldCommand = targetContext != null ? targetContext.GetService<IActionCommand>() : null;

            try
            {
                // 重初始化会替换 Service，先解除旧服务的 EventBus 订阅，避免同一事件被旧实例重复处理。
                previousConfiguration?.SetEventBus(null);
                _context = targetContext;
                ResolveAttributeDependencies(_context);

                var service = new NiumaActionService();
                service.SetWeaponSources(weaponSources);
                service.SetDefaultComboTrees(defaultComboTrees);
                service.SetStaminaResourceId(staminaResourceId);
                service.SetAttributeQuery(_attributeQuery);
                service.SetAttributeCommand(_attributeCommand);
                service.SetConditionResolver(ResolveConditionResolver(_context));
                service.SetPlaybackGateway(ResolvePlaybackGateway(_context));
                service.SetEventBus(ResolveEventBus(_context));
                service.SetTimeProvider(() => Time.time);

                _actionService = service;
                _configurationService = service;

                if (registerServiceToContext && _context != null)
                {
                    RegisterServicesToContext(_context);
                }

                IsInitialized = true;
                IsRunning = false;
            }
            catch (Exception ex)
            {
                _actionService = previousService;
                _configurationService = previousConfiguration;
                _context = previousContext;
                IsInitialized = previousInitialized;
                IsRunning = previousRunning;
                previousConfiguration?.SetEventBus(ResolveEventBus(previousContext));

                if (targetContext != null && registerServiceToContext)
                {
                    RestoreRegisteredServices(targetContext, oldService, oldQuery, oldCommand);
                }

                Debug.LogError($"[NiumaAction] 初始化失败，已回滚：{ex.Message}", this);
                throw;
            }
        }

        public void StartModule()
        {
            if (!IsInitialized)
            {
                Initialize(_context);
            }

            IsRunning = true;
        }

        public void StopModule()
        {
            IsRunning = false;
        }

        public void Tick(float deltaTime)
        {
            if (!IsInitialized)
            {
                return;
            }

            _actionService?.Tick(deltaTime);
        }

        public void SetWeaponSources(MeleeWeaponSource[] sources)
        {
            weaponSources = sources ?? Array.Empty<MeleeWeaponSource>();
            _configurationService?.SetWeaponSources(weaponSources);
        }

        public void SetDefaultComboTrees(ComboTreeAsset[] comboTrees)
        {
            defaultComboTrees = comboTrees ?? Array.Empty<ComboTreeAsset>();
            _configurationService?.SetDefaultComboTrees(defaultComboTrees);
        }

        public void SetAttributeDependencies(IAttributeQuery query, IAttributeCommand command)
        {
            _externalAttributeQuery = query;
            _externalAttributeCommand = command;
            _attributeDependencyLocked = query != null || command != null;
            _attributeQuery = query;
            _attributeCommand = command;
            _configurationService?.SetAttributeQuery(_attributeQuery);
            _configurationService?.SetAttributeCommand(_attributeCommand);
        }

        public void SetConditionResolver(IActionConditionResolver resolver)
        {
            _externalConditionResolver = resolver;
            _conditionResolverLocked = resolver != null;
            _configurationService?.SetConditionResolver(resolver);
        }

        public void SetPlaybackGateway(INiumaActionTPCPlaybackGateway gateway)
        {
            _externalPlaybackGateway = gateway;
            _playbackGatewayLocked = gateway != null;
            _configurationService?.SetPlaybackGateway(gateway);
        }

        public void SetEventBus(IEventBus eventBus)
        {
            _externalEventBus = eventBus;
            _eventBusLocked = eventBus != null;
            _configurationService?.SetEventBus(eventBus);
        }

        [ContextMenu("NiumaAction/调试/装备第一把武器")]
        private void DebugEquipFirstWeapon()
        {
            EnsureInitializedForDebug();
            var weapon = weaponSources != null && weaponSources.Length > 0 ? weaponSources[0] : null;
            var result = _actionService != null ? _actionService.EquipWeaponSource(debugActorId, weapon) : ActionOperationResult.Failed(NiumaAction.Enum.ActionFailureReason.InternalError, "ActionService 未初始化。", debugActorId);
            Debug.Log($"[NiumaAction] 调试装备武器：Succeeded={result.Succeeded}, Reason={result.FailureReason}, Message={result.Message}", this);
        }

        [ContextMenu("NiumaAction/调试/提交输入")]
        private void DebugSubmitInput()
        {
            EnsureInitializedForDebug();
            var result = _actionService != null ? _actionService.SubmitInput(debugActorId, debugInputId) : ActionOperationResult.Failed(NiumaAction.Enum.ActionFailureReason.InternalError, "ActionService 未初始化。", debugActorId);
            Debug.Log($"[NiumaAction] 调试提交输入：Succeeded={result.Succeeded}, Reason={result.FailureReason}, Message={result.Message}", this);
        }

        [ContextMenu("NiumaAction/调试/取消动作")]
        private void DebugCancelAction()
        {
            EnsureInitializedForDebug();
            var result = _actionService != null ? _actionService.CancelAction(debugActorId, "InspectorCancel") : ActionOperationResult.Failed(NiumaAction.Enum.ActionFailureReason.InternalError, "ActionService 未初始化。", debugActorId);
            Debug.Log($"[NiumaAction] 调试取消动作：Succeeded={result.Succeeded}, Reason={result.FailureReason}, Message={result.Message}", this);
        }

        [ContextMenu("NiumaAction/调试/打印快照")]
        private void DebugLogSnapshot()
        {
            EnsureInitializedForDebug();
            var snapshot = _actionService != null ? _actionService.GetSnapshot(debugActorId) : null;
            if (snapshot == null)
            {
                Debug.LogWarning("[NiumaAction] ActionService 未初始化，无法打印快照。", this);
                return;
            }

            Debug.Log($"[NiumaAction] Snapshot Actor={snapshot.ActorId}, Weapon={snapshot.CurrentWeaponSourceId}, Tree={snapshot.CurrentComboTreeId}, Node={snapshot.CurrentNodeId}, Playing={snapshot.IsActionPlaying}, Normalized={snapshot.CurrentNormalizedTime:0.###}, Buffer={snapshot.InputBuffer?.Length ?? 0}", this);
        }

        private void EnsureInitializedForDebug()
        {
            if (!IsInitialized)
            {
                Initialize(_context);
            }
        }

        private void ResolveAttributeDependencies(GameContext context)
        {
            if (_attributeDependencyLocked)
            {
                _attributeQuery = _externalAttributeQuery;
                _attributeCommand = _externalAttributeCommand;
                return;
            }

            _attributeQuery = resolveAttributeFromContext && context != null && context.TryGetService<IAttributeQuery>(out var query)
                ? query
                : attributeController != null
                    ? attributeController.AttributeQuery
                    : null;

            _attributeCommand = resolveAttributeFromContext && context != null && context.TryGetService<IAttributeCommand>(out var command)
                ? command
                : attributeController != null
                    ? attributeController.AttributeCommand
                    : null;
        }

        private IActionConditionResolver ResolveConditionResolver(GameContext context)
        {
            if (_conditionResolverLocked)
            {
                return _externalConditionResolver;
            }

            if (conditionResolverProvider is IActionConditionResolver inspectorResolver)
            {
                return inspectorResolver;
            }

            return context != null && context.TryGetService<IActionConditionResolver>(out var contextResolver)
                ? contextResolver
                : null;
        }

        private INiumaActionTPCPlaybackGateway ResolvePlaybackGateway(GameContext context)
        {
            if (_playbackGatewayLocked)
            {
                return _externalPlaybackGateway;
            }

            if (playbackGatewayProvider is INiumaActionTPCPlaybackGateway inspectorGateway)
            {
                return inspectorGateway;
            }

            return context != null && context.TryGetService<INiumaActionTPCPlaybackGateway>(out var contextGateway)
                ? contextGateway
                : null;
        }

        private IEventBus ResolveEventBus(GameContext context)
        {
            if (_eventBusLocked)
            {
                return _externalEventBus;
            }

            return context?.EventBus;
        }

        private void RegisterServicesToContext(GameContext context)
        {
            if (context == null || _actionService == null)
            {
                return;
            }

            context.RegisterService<IActionService>(_actionService);
            context.RegisterService<IActionQuery>(_actionService);
            context.RegisterService<IActionCommand>(_actionService);
        }

        private void UnregisterServicesFromContext(GameContext context)
        {
            if (context == null || _actionService == null || !registerServiceToContext)
            {
                return;
            }

            ClearRegisteredServiceIfCurrent<IActionService>(context, _actionService);
            ClearRegisteredServiceIfCurrent<IActionQuery>(context, _actionService);
            ClearRegisteredServiceIfCurrent<IActionCommand>(context, _actionService);
        }

        private static void RestoreRegisteredServices(GameContext context, IActionService service, IActionQuery query, IActionCommand command)
        {
            RestoreRegisteredService(context, service);
            RestoreRegisteredService(context, query);
            RestoreRegisteredService(context, command);
        }

        private static void RestoreRegisteredService<T>(GameContext context, T service) where T : class
        {
            if (context == null)
            {
                return;
            }

            if (service != null)
            {
                context.RegisterService(service);
            }
            else
            {
                context.UnregisterService<T>();
            }
        }

        private static void ClearRegisteredServiceIfCurrent<T>(GameContext context, object currentService) where T : class
        {
            if (context != null && ReferenceEquals(context.GetService<T>(), currentService))
            {
                context.UnregisterService<T>();
            }
        }
    }
}
