using System;
using NiumaAction.Data;
using NiumaAction.Enum;
using UnityEngine;

namespace NiumaAction.Config
{
    // AnimateAsset 只描述动作表现意图；真正的播放、混合、中断和动画时间仍由 TPC 负责。
    [CreateAssetMenu(menuName = "NiumaAction/Animate Asset", fileName = "AnimateAsset")]
    public sealed class AnimateAsset : ScriptableObject
    {
        [Tooltip("动作表现稳定 ID。发布后不要随意修改，连招和调试日志会使用它。")]
        public string AnimateId;

        [Tooltip("实际播放的动画 Clip。第一版由 TPCBridge 转成 TPC ActionRequest。")]
        public AnimationClip Clip;

        [Tooltip("骨骼遮罩。第一版仅传递意图，TPC 支持动作层后再完整接入。")]
        public AvatarMask AvatarMask;

        [Tooltip("动作层模式。第一版主要用于桥接降级和调试显示。")]
        public ActionAnimationLayerMode LayerMode = ActionAnimationLayerMode.BaseLayer;

        [Min(0f)]
        [Tooltip("进入动作的淡入时间。会映射到 TPC ActionRequest.FadeDuration。")]
        public float FadeInSeconds = 0.15f;

        [Min(0f)]
        [Tooltip("退出动作的淡出时间。TPC 未支持时会被桥接层忽略或记录 Warning。")]
        public float FadeOutSeconds = 0.1f;

        [Tooltip("动作优先级。数值越大，越应优先于普通动作。")]
        public int Priority = 20;

        [Tooltip("播放动作时是否继续应用重力。会映射到 TPC ActionRequest.ApplyGravity。")]
        public bool ApplyGravity = true;

        [Min(0f)]
        [Tooltip("动作时长。<= 0 时默认使用 Clip.length；只作为 Action 兜底计时，真实播放以 TPC 为准。")]
        public float DurationSeconds;

        [Tooltip("动作时间轴事件。第一版只支持 HitboxOpen、HitboxClose、AudioCue。")]
        public ActionTimelineEventData[] TimelineEvents = Array.Empty<ActionTimelineEventData>();

        // DurationSeconds 只是 ActionService 兜底计时用；桥接层能拿到 TPC 真实进度时应以 TPC 为准。
        public float ResolveDurationSeconds()
        {
            if (DurationSeconds > 0f)
            {
                return DurationSeconds;
            }

            return Clip != null ? Mathf.Max(0f, Clip.length) : 0f;
        }

        private void OnValidate()
        {
            FadeInSeconds = Mathf.Max(0f, FadeInSeconds);
            FadeOutSeconds = Mathf.Max(0f, FadeOutSeconds);
            Priority = Mathf.Max(0, Priority);
            DurationSeconds = Mathf.Max(0f, DurationSeconds);
            TimelineEvents ??= Array.Empty<ActionTimelineEventData>();

            for (var i = 0; i < TimelineEvents.Length; i++)
            {
                if (TimelineEvents[i] == null)
                {
                    continue;
                }

                TimelineEvents[i].NormalizedTime = Mathf.Clamp01(TimelineEvents[i].NormalizedTime);
            }
        }
    }
}
