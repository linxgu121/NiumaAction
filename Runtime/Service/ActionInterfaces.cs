using System;
using NiumaAction.Config;
using NiumaAction.Data;
using NiumaAttribute.Service;
using NiumaCore.Event;

namespace NiumaAction.Service
{
    public interface IActionQuery
    {
        long Revision { get; }
        string ServiceInstanceId { get; }
        bool HasActor(string actorId);
        ActionOwnerSnapshot GetSnapshot(string actorId);
        MeleeWeaponSource GetWeaponSource(string actorId);
        ComboTreeAsset GetComboTree(string actorId);
        bool IsActionPlaying(string actorId);
        bool IsInCancelWindow(string actorId);
        ActionOperationResult CanSubmitInput(string actorId, string inputId);
    }

    public interface IActionCommand
    {
        ActionOperationResult EquipWeaponSource(string actorId, MeleeWeaponSource weaponSource);
        ActionOperationResult SubmitInput(string actorId, string inputId);
        ActionOperationResult CancelAction(string actorId, string reason);
    }

    public interface IActionService : IActionQuery, IActionCommand
    {
        void Tick(float deltaTime);
    }

    public interface IActionConfigurationService
    {
        void SetDefaultComboTrees(ComboTreeAsset[] comboTrees);
        void SetWeaponSources(MeleeWeaponSource[] weaponSources);
        void SetAttributeQuery(IAttributeQuery attributeQuery);
        void SetAttributeCommand(IAttributeCommand attributeCommand);
        void SetConditionResolver(IActionConditionResolver conditionResolver);
        void SetPlaybackGateway(INiumaActionTPCPlaybackGateway playbackGateway);
        void SetEventBus(IEventBus eventBus);
        void SetStaminaResourceId(string resourceId);
        void SetTimeProvider(Func<float> timeProvider);
    }

    public interface IActionConditionResolver
    {
        ActionOperationResult Evaluate(ActionConditionData condition, ActionConditionContext context);
    }

    public interface INiumaActionTPCPlaybackGateway
    {
        // Gateway 只负责把意图写入 TPC 数据层 / 仲裁队列，不在这里同步裁决。
        // 仲裁结果必须通过 ActionPlaybackPrecheckAccepted / Rejected 等事件回传。
        void SubmitPrecheckPlayback(NiumaActionPlaybackRequest request);
        void SubmitCommitPlayback(NiumaActionPlaybackRequest request);
        void CancelPlayback(string requestId, string actorId, string reason);
    }
}
