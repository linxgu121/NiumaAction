namespace NiumaAction.Enum
{
    // 这些枚举放在 Runtime 中，因为资产和桥接载荷都需要稳定的序列化值。
    // 资产发布后不要随意重命名枚举成员。
    public enum ActionAnimationLayerMode
    {
        BaseLayer = 0,
        UpperBody = 1,
        FullBodyOverride = 2
    }

    public enum ActionConditionType
    {
        None = 0,
        ResourceAtLeast = 1,
        CurrentNodeEquals = 2,
        HasWeaponSource = 3,
        Custom = 100
    }

    public enum ActionTimelineEventType
    {
        // 第一阶段有意不加入 DamagePoint；近战伤害优先通过
        // HitboxOpen / HitboxClose 和 CombatBridge 流转。
        HitboxOpen = 1,
        HitboxClose = 2,
        AudioCue = 3
    }

    public enum ActionTimelineEventSource
    {
        Unknown = 0,
        TpcAnimationEvent = 1,
        ActionServiceTickFallback = 2,
        DebugManual = 3
    }

    public enum ActionFailureReason
    {
        None = 0,
        InvalidRequest = 1,
        ActorMissing = 2,
        WeaponSourceMissing = 3,
        ComboTreeMissing = 4,
        NodeMissing = 5,
        ActionMissing = 6,
        ConditionFailed = 7,
        ResourceInsufficient = 8,
        AttributeServiceMissing = 9,
        TpcPlaybackRejected = 10,
        AlreadyPlaying = 11,
        CancelWindowClosed = 12,
        TransitionMissing = 13,
        InternalError = 99
    }

    public enum NiumaActionTPCPlaybackStatus
    {
        None = 0,
        PrecheckAccepted = 1,
        PrecheckRejected = 2,
        CommitStarted = 3,
        CommitRejected = 4
    }
}
