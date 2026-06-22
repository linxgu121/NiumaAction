# NiumaAction

## 模块定位

`NiumaAction` 是近战动作和连招编排模块，负责“下一招是什么、什么时候能取消、什么时候抛出动作时间轴事件”。

它不直接播放动画、不直接打开 Combat Hitbox、不直接播放音效。动画播放交给后续 `NiumaAction.TPCBridge`，命中结算交给 `NiumaAction.CombatBridge`，音效交给 `NiumaAction.AudioBridge`。

## 依赖模块

| 依赖 | 用途 |
| --- | --- |
| `NiumaCore.Runtime` | `GameContext`、`IGameModule`、事件总线 |
| `NiumaAttribute.Runtime` | 查询和消耗体力等动作资源 |

程序集文件位于：

```text
D:\zhizuo\sava\NiumaM\Assets\Game\Moudle\NiumaAction\Runtime\NiumaAction.Runtime.asmdef
```

当前 `NiumaAction.Runtime` 只引用 `NiumaCore.Runtime` 和 `NiumaAttribute.Runtime`，不会反向引用 `NiumaTPC`、`NiumaCombat`、`NiumaAudio` 或 UI 模块。

## 核心场景挂载

推荐在核心场景中创建：

```text
CoreScene
└── BootstrapRoot
    └── GameplayServicesRoot
        └── ActionRoot
```

`ActionRoot` 挂载 `NiumaActionController`。

### NiumaActionController

| 字段 | 建议填写 | 可留空 | 说明 |
| --- | --- | --- | --- |
| `Weapon Sources` | 拖入可用的 `MeleeWeaponSource` 资产 | 可以 | 运行时可通过 `EquipWeaponSource(actorId, weaponSource)` 给角色装备动作来源 |
| `Default Combo Trees` | 拖入常用 `ComboTreeAsset` | 可以 | 用于按 `ComboTreeId` 查询或调试；武器资产通常会直接引用默认连招树 |
| `Attribute Controller` | 拖核心场景里的 `NiumaAttributeController` | 可以 | 留空时会尝试从 `GameContext` 解析 `IAttributeQuery / IAttributeCommand` |
| `Resolve Attribute From Context` | 核心场景已注册 Attribute 时勾选 | 可以 | 关闭后只使用 Inspector 或代码注入的 Attribute 依赖 |
| `Stamina Resource Id` | 默认 `stamina` | 不建议空 | 必须和 Attribute 资源 ID 一致 |
| `Condition Resolver Provider` | 拖实现 `IActionConditionResolver` 的组件 | 可以 | 留空时 `Custom` 条件会失败并返回结构化错误 |
| `Playback Gateway Provider` | 正式核心场景通常不填；无 `EventBus` 的桥接测试时才拖实现 `INiumaActionTPCPlaybackGateway` 的组件 | 可以 | 只负责校验或提交请求，不能单独完成 Accepted / Committed 回传闭环 |
| `Initialize On Awake` | 单模块调试可开启 | 可以 | 核心场景统一启动器存在时可以关闭 |
| `Start On Enable` | 单模块调试可开启 | 可以 | 核心场景统一启动器存在时可以关闭 |
| `Register Service To Context` | 建议开启 | 可以 | 注册 `IActionService / IActionQuery / IActionCommand` |
| `Drive Tick In Update` | 没有统一模块 Tick 时开启 | 可以 | 如果外部已经调用 `IGameModule.Tick`，必须关闭，避免动作时间推进两次 |
| `Debug Actor Id` | 如 `player` | 可以 | Inspector 调试用 ActorId |
| `Debug Input Id` | 如 `attack_light` | 可以 | Inspector 调试用输入 ID |

### NiumaActionTPCBridge

第三阶段已加入 `NiumaAction.TPCBridge` 程序集。推荐在玩家对象下创建：

```text
PlayerRoot
└── ActionBridge
    └── NiumaActionTPCBridge
```

`NiumaActionTPCBridge` 只负责把 `ActionService` 发出的播放意图提交给 TPC，不决定下一段连招，也不计算伤害。

| 字段 | 建议填写 | 可留空 | 说明 |
| --- | --- | --- | --- |
| `Character Controller` | 拖 `PlayerRoot` 上的 `NiumaCharacterController` | 可以 | 留空且开启自动查找时，会从当前物体和父物体查找 |
| `Auto Resolve From Parents` | 建议开启 | 可以 | 适合脚本挂在 `PlayerRoot/ActionBridge` 子物体上 |
| `Actor Id` | 例如 `player` | 可以 | 必须与 `EquipWeaponSource(actorId, ...)`、`SubmitInput(actorId, ...)` 使用的 ActorId 一致；为空时不过滤 ActorId |
| `Subscribe Action Events` | 正式流程开启 | 可以 | 开启后订阅 `ActionPlaybackPrecheckRequested / CommitRequested / Cancelled / Interrupted` |
| `Flush Immediately` | 建议开启 | 可以 | 开启后提交 TPC 黑板并本帧执行一次 `ActionArbiter`，桥接可立刻确认是否进入 Override |
| `Optimistic Committed When Not Flushed` | 只有关闭立即仲裁时才需要 | 可以 | 关闭立即仲裁时，第一版没有异步仲裁回调，只能乐观回传 Committed |
| `Detect Override Interruption` | 建议开启 | 可以 | 动作已 Committed 后，如果 TPC 仍处于 Override 但已被其它请求替换，会发布 `ActionPlaybackInterruptedEvent` 清理 Action 状态；自然播放结束不会误报中断 |
| `Register Gateway To Context` | 正式核心场景通常关闭 | 可以 | 只给无 EventBus 的提交测试用；正式播放闭环依赖 EventBus |
| `Log Warnings` | 建议开启 | 可以 | 缺少角色、Clip、TPC 暂不支持字段时输出 Warning |

第三阶段第一版使用 TPC 现有 `NiumaCharacterController.RequestOverride(in ActionRequest, flushImmediately)`。`Precheck` 阶段只校验 `ActorId / RequestId / NiumaCharacterController / AnimateAsset / Clip` 是否可提交，不真正写入 TPC 仲裁器；真正的 HFSM / 优先级仲裁发生在 `Commit` 阶段。当 `Flush Immediately` 开启时，桥接会在提交后读取 `RuntimeData.Override.Request`，只有当前 Override 请求与本次提交的 `Clip / MotionData / Priority / FadeDuration / ApplyGravity` 匹配时才发布 `ActionPlaybackCommittedEvent`。如果 TPC 仲裁器拒绝，例如当前翻滚或更高优先级 Override 正在播放，则发布 `ActionPlaybackCommitRejectedEvent`，`ActionService` 会回滚本次资源消耗。

当 `ActionService` 在正式 EventBus 流程中发布 `ActionPlaybackCancelledEvent`，或外部直接调用 `CancelPlayback` 时，`NiumaActionTPCBridge` 都会调用 `NiumaCharacterController.TryCancelCurrentOverride(...)`，让 TPC 停止当前全身 Override 并回到原来的移动状态；它不只是清理桥接层内部记录。

`AnimateAsset` 中以下字段当前会降级：
- `AvatarMask`：TPC 当前 `ActionRequest` 不接收，按全身 Override 处理。
- `LayerMode`：除 `BaseLayer / FullBodyOverride` 外，第一版只输出 Warning。
- `FadeOutSeconds`：TPC 当前 `ActionRequest` 不接收，退出淡出以 TPC `OverrideState` 为准。

#### AnimationEvent 调用

如果动画 Clip 上使用 Unity AnimationEvent 主动抛时间轴事件，函数名填写：

```text
RaiseTimelineEvent
```

参数填写 `AnimateAsset.TimelineEvents` 中的 `EventId`。也可以调用：

```text
RaiseTimelineEventByIndex
```

参数填写 `TimelineEvents` 数组下标。桥接会发布 `ActionTimelineEventRaisedEvent`，并使用与 `ActionService` 相同的 `TimelineEventKey` 规则，避免 TPC AnimationEvent 和 Tick 兜底重复触发同一个 HitboxOpen / HitboxClose。

### NiumaActionCombatBridge

第四阶段已加入 `NiumaAction.CombatBridge` 程序集。推荐在玩家对象下创建：

```text
PlayerRoot
└── ActionBridge
    ├── NiumaActionTPCBridge
    └── NiumaActionCombatBridge
```

`NiumaActionCombatBridge` 只负责把 `ActionTimelineEventRaisedEvent` 里的 `HitboxOpen / HitboxClose` 转成 Combat Hitbox 开关，不计算伤害、不直接查找目标。真正的命中检测和伤害结算仍由 `NiumaCombat` 的 Hitbox / Hurtbox / CombatService 负责。

| 字段 | 建议填写 | 可留空 | 说明 |
| --- | --- | --- | --- |
| `Combat Runtime Service Provider` | 拖核心场景 `CombatRoot` 上的 `NiumaCombatController` | 可以 | 留空且开启 Context 解析时，会从 `GameContext` 获取 `ICombatHitboxService` |
| `Resolve Combat From Context` | 核心场景已注册 Combat 时开启 | 可以 | 关闭后只使用上方手动绑定或代码注入的 Combat 服务 |
| `Action Service Instance Id` | 多个 ActionService 时填写目标 `ServiceInstanceId` | 可以 | 为空时不按 ServiceInstanceId 过滤 |
| `Actor Id` | 例如 `player` | 可以 | 必须与 Action 输入使用的 ActorId 一致；为空时不过滤 ActorId |
| `Subscribe Action Events` | 正式流程开启 | 可以 | 开启后订阅时间轴、取消和中断事件 |
| `Use Default Hitbox Definition` | 需要兜底 Hitbox 时开启 | 可以 | 关闭后必须通过 `Hitbox Bindings` 匹配，否则不会打开 Hitbox |
| `Default Hitbox Definition` | 开启默认 Hitbox 后填写 Combat Hitbox 定义 | 可以 | PayloadId 找不到绑定时使用；未开启默认时该字段不生效 |
| `Hitbox Bindings` | Key 填 `TimelineEvent.PayloadId` 或 `EventId`，Definition 填对应 CombatHitboxDefinition | 可以 | 用于一段动画内多 Hitbox 通道，如 blade、kick、heavy |
| `Close Hitboxes On Cancelled` | 建议开启 | 可以 | 动作正常结束、外部取消、连招切换时关闭该 RequestId 下所有已打开 Hitbox |
| `Close Hitboxes On Interrupted` | 建议开启 | 可以 | TPC 动作被覆盖或打断时关闭该 RequestId 下所有已打开 Hitbox |
| `Log Warnings` | 建议开启 | 可以 | 缺少 Combat 服务、Hitbox 配置或重复开关时输出 Warning |

`AnimateAsset.TimelineEvents` 中：
- `Type = HitboxOpen`：打开 Combat Hitbox。
- `Type = HitboxClose`：关闭同一个 Hitbox。
- `PayloadId`：推荐填写稳定通道 ID，例如 `blade`、`hitbox_main`。Open 和 Close 使用同一个 `PayloadId` 才能配对。

桥接层会维护 `ActionAttackInstanceId -> CombatAttackInstanceId` 映射。Action 的 `AttackInstanceId` 用于跨来源配对，Combat 返回的运行时实例 ID 用于真正调用 `ICombatHitboxService.CloseHitbox`。

## 资产粒度

### AnimateAsset

一个 `AnimateAsset` 表示一段动画表现配置。

常用填写：

| 字段 | 填写方式 |
| --- | --- |
| `AnimateId` | 稳定 ID，例如 `sword_light_01_anim` |
| `Clip` | 拖动画 Clip |
| `DurationSeconds` | 动作逻辑时长；小于等于 0 时由 Clip 长度兜底 |
| `TimelineEvents` | 配置 HitboxOpen、HitboxClose、AudioCue 等时间轴事件 |

`TimelineEvents` 建议每个事件都填写 `EventId`。如果同一动作内有多个相同时间、相同类型的事件，必须使用不同 `EventId` 或不同 `PayloadId`，否则会被视为同一个事件去重。

### ComboAction

一个 `ComboAction` 表示一招动作逻辑。

常用填写：

| 字段 | 填写方式 |
| --- | --- |
| `ActionId` | 稳定 ID，例如 `sword_light_01` |
| `Animate` | 拖对应 `AnimateAsset` |
| `DamageMultiplier` | 连招伤害倍率，后续由 CombatBridge 写入 Combat 请求 |
| `StaminaCost` | 启动该动作消耗的体力 |
| `AudioCueId` | 后续 AudioBridge 使用的 CueId |
| `DefaultHitboxId` | 后续 CombatBridge 默认打开的 Hitbox 通道 |

第一版使用 `ComboAction` 资产。`ComboAction` 是共享资产，运行时只读引用，不会复制资产本身。

### ComboTreeAsset

一个 `ComboTreeAsset` 表示一套连招结构。

常用填写：

| 字段 | 填写方式 |
| --- | --- |
| `ComboTreeId` | 稳定 ID，例如 `sword_basic_tree` |
| `InputBufferSeconds` | 输入缓冲时间 |
| `Nodes` | 连招节点数组 |
| `StartNodeId` | 起手节点 ID；为空时使用第一个有效节点 |

`ComboNode` 内填写：

| 字段 | 填写方式 |
| --- | --- |
| `NodeId` | 稳定节点 ID，例如 `light_01` |
| `Action` | 拖 `ComboAction` |
| `InputId` | 触发该节点的输入，例如 `attack_light` |
| `CancelWindowStart01 / CancelWindowEnd01` | 0 到 1 的归一化取消窗口 |
| `Transitions` | 当前节点可转向的下一个节点 |

### MeleeWeaponSource

一个 `MeleeWeaponSource` 表示一把近战武器的动作来源。

常用填写：

| 字段 | 填写方式 |
| --- | --- |
| `WeaponSourceId` | 稳定 ID，例如 `iron_sword` |
| `BaseDamage` | 武器基础伤害 |
| `HoldStyleId` | 持握 / 架势 ID，第一版只作为 TPC 姿态切换预留 |
| `DefaultComboTree` | 拖该武器默认使用的 `ComboTreeAsset` |

## ComboTree 可视化编辑器

第五阶段已加入 `NiumaAction.Editor` 编辑器程序集，只在 Unity Editor 中生效，不会进入运行时包体。

打开方式：

- 在 Project 面板选中 `ComboTreeAsset`，Inspector 顶部点击 `打开连招树可视化编辑器`。
- 或使用菜单 `Tools / NiumaAction / Combo Tree Editor`，再在窗口顶部拖入 `ComboTreeAsset`。

窗口布局：

| 区域 | 用途 |
| --- | --- |
| 顶部 Toolbar | 选择资产、添加 / 复制 / 删除 / 上移 / 下移节点、聚焦起手节点、重建图、Fit All |
| 左侧资产与节点列表 | 编辑 `ComboTreeId`、`DisplayName`、`StartNodeId`、`InputBufferSeconds`，并快速选择节点 |
| 中间 Graph | 按 `Nodes` 数组显示节点，通过 `Transitions[].TargetNodeId` 自动连线 |
| 右侧详情 | 编辑当前节点的 `NodeId`、`Action`、取消窗口和 `Transitions` |
| 底部校验 | 显示重复 `NodeId`、缺失 `TargetNodeId`、取消窗口错误等问题，可点击聚焦到对应节点 |

当前规则：

- Graph 第一版只做可视化和选中同步，不支持拖线修改 Transition。
- Transition 仍在右侧详情面板中编辑，`TargetNodeId` 必须填写目标节点的 `NodeId`。
- 新增节点会自动生成不重复的 `NodeId`，默认取消窗口为 `0.55 - 0.9`。
- 所有字段修改都走 Unity `SerializedProperty`，支持 Undo / Dirty。
- 连续输入字段时 Graph 会延迟约 `0.3` 秒刷新，避免每个字符都重建图。
- `Fit All` 用于节点较多时快速缩放到完整视图。
- 第一版 Graph 节点按固定网格自动排列，不保存手动节点位置；复杂连招树的自定义布局留到后续 Editor metadata 阶段。
- 校验面板会顺着节点检查 `ComboAction` / `AnimateAsset` 的关键配置，例如未绑定动画、TimelineEvent 时间越界、Hitbox 事件缺少 `PayloadId` 等。

## 运行时使用流程

程序侧推荐流程：

```text
1. 从 GameContext 获取 IActionCommand。
2. 调用 EquipWeaponSource(actorId, weaponSource) 给角色装备武器动作来源。
3. 玩家输入攻击时调用 SubmitInput(actorId, inputId)。
4. ActionService 做条件、资源、取消窗口和输入缓冲判断。
5. 通过事件请求 TPCBridge 做本地 Precheck，可提交时回传 `ActionPlaybackPrecheckAccepted`。
6. ActionService 二次确认资源并扣除后发布 `ActionPlaybackCommitRequested`。
7. TPCBridge 在 Commit 阶段把播放意图写入 TPC 数据黑板 / ActionArbitration，并通过当前 Override 结果回传 `Committed / CommitRejected`。
8. 播放确认后，ActionService 开始 Tick 动作进度并抛出 TimelineEvent。
```

第一版 `SubmitInput` 不直接调用 TPC 或 Combat。正式流程必须依赖 `EventBus` 回传仲裁结果；`INiumaActionTPCPlaybackGateway` 只是无 EventBus 场景下验证“请求能否被桥接层校验或提交”的测试入口，不能单独跑完整动作闭环。没有 `EventBus` 时，`ActionService` 会在提交 Gateway 测试后清理 Pending 并返回结构化失败，避免动作状态卡死。

当 `EventBus` 存在时，`ActionService` 只发布播放意图事件，不会再调用 `Playback Gateway Provider`，避免同一个请求被 EventBus 订阅者和 Gateway 双重写入 TPC 黑板。

## TimelineEvent 去重规则

`ActionService` 会用 `FiredTimelineEventKeys` 记录一次动作中已经触发过的时间轴事件。

去重 Key 规则：

1. 优先使用 `TimelineEvent.EventId`。
2. 如果 `EventId` 为空，则使用 `Type + NormalizedTime + PayloadId`。
3. `ActionTimelineEventRaisedEvent.TimelineEventKey` 可由桥接层显式填写。

第三阶段 `TPCBridge` 从 Unity AnimationEvent 发布 `ActionTimelineEventRaisedEvent` 时，必须带上同一个 `ServiceInstanceId / RequestId / ActorId`，并尽量填写 `TimelineEventKey` 或 `EventId`。`ActionService` 收到外部事件后会同步写入已触发表，避免下一帧 Tick 兜底重复发布同一个 `HitboxOpen / HitboxClose / AudioCue`。桥接层会优先用 TPC 当前动画时间计算 `NormalizedTime`，拿不到有效动画时长时才回退到 `TimelineEvent.NormalizedTime` 配置值。

连招切换时，`ActionService` 会先为旧 `RequestId` 发布 `ActionPlaybackCancelledEvent(Reason=ComboTransition)`，再进入新动作 Pending。CombatBridge / AudioBridge 必须用这个取消事件关闭旧 Hitbox、清理旧音效，避免上一段攻击的 HitboxOpen 泄漏到下一段。

动作取消、预检失败、Commit 失败或动作结束时，`ClearActionState()` 会清空输入缓冲，避免已经失效的旧输入在下一次取消窗口里触发幽灵连招。成功连招切换不走 `ClearActionState()`，只清理旧 Request 的表现和 Hitbox。

## 第六阶段验收清单

第六阶段不再扩展新的战斗动作能力，目标是确认当前 `Runtime + TPCBridge + CombatBridge + Editor` 可以被别人按 README 配置和验证。

Unity 编译检查：

- 打开 Unity 6.0 `6000.0.75f1` 项目后，Console 不应出现 `NiumaAction.Runtime`、`NiumaAction.TPCBridge`、`NiumaAction.CombatBridge`、`NiumaAction.Editor` 的编译错误。
- `NiumaAction.Runtime` 只引用 `NiumaCore.Runtime` 与 `NiumaAttribute.Runtime`。
- `NiumaAction.Editor` 只在 Editor 平台启用，`UnityEditor` / `GraphView` 不得出现在 Runtime、TPCBridge、CombatBridge 中。

资产检查：

- 创建 `AnimateAsset`，绑定 `AnimationClip`，配置 `TimelineEvents`。
- 创建 `ComboAction`，绑定 `AnimateAsset`，填写 `ActionId`、`DamageMultiplier`、`StaminaCost`。
- 创建 `ComboTreeAsset`，至少添加两个 `ComboNode`，每个节点绑定 `ComboAction`。
- 在第一个节点的 `Transitions` 中填写 `InputId` 和第二个节点的 `TargetNodeId`。
- 创建 `MeleeWeaponSource`，填写 `WeaponSourceId`、`BaseDamage`、`HoldStyleId`，并绑定默认 `ComboTreeAsset`。

编辑器检查：

- 选中 `ComboTreeAsset` 时，Inspector 顶部能打开 `Combo Tree Editor`。
- 左侧节点列表、中间 Graph、右侧详情选中状态同步。
- 点击 `添加节点 / 复制节点 / 删除节点 / 上移 / 下移` 后，Graph 和校验面板刷新。
- `Undo / Redo` 后窗口内容同步刷新。
- 缺失 `TargetNodeId`、重复 `NodeId`、TimelineEvent 时间越界等问题会显示在底部校验面板。

运行时手动验证：

- 核心场景 `ActionRoot` 挂 `NiumaActionController`。
- 玩家对象或其子物体挂 `NiumaActionTPCBridge`，绑定 `NiumaCharacterController`。
- 需要 Hitbox 时，同一对象或子物体挂 `NiumaActionCombatBridge`，绑定 `NiumaCombatController` 或开启 `Resolve Combat From Context`。
- 程序或测试脚本调用 `EquipWeaponSource(actorId, weaponSource)` 后，再调用 `SubmitInput(actorId, inputId)`。
- 第一段攻击能进入起手节点；取消窗口外输入下一段不会切换；取消窗口内输入下一段能进入目标节点。
- TPC 拒绝播放时，不应扣资源、不进入动作状态、不打开 Hitbox。
- `HitboxOpen / HitboxClose` 能通过 CombatBridge 打开和关闭 Combat Hitbox。
- 动作取消、连招切换或 TPC 中断后，旧 `RequestId` 下已打开的 Hitbox 会被关闭。

## 当前阶段限制

- 阶段 3 已包含 Runtime 资产、协议、Service、Controller 和 `NiumaAction.TPCBridge`。
- `NiumaAction.TPCBridge` 第一版使用 TPC 现有 `ActionRequest`，暂不精确映射 `AvatarMask / LayerMode / FadeOutSeconds`。
- TPC 当前没有独立异步仲裁结果事件；`Precheck` 只做本地可提交校验，真正仲裁在 `Commit` 阶段；`Flush Immediately` 开启时，桥接通过读取 `RuntimeData.Override.Request` 确认是否被接受。
- TPC 当前 `ActionRequest` 没有 `RequestId`，桥接第一版用 `Clip / MotionData / Priority / FadeDuration / ApplyGravity` 组合匹配当前 Override。若后续 TPC 扩展请求元数据，应优先改为 RequestId 级匹配。
- `NiumaAction.CombatBridge` 已实现 HitboxOpen / HitboxClose 到 Combat HitboxService 的桥接；目标检测和伤害结算仍由 NiumaCombat 执行。
- `NiumaAction.AudioBridge` 尚未实现，AudioCue 事件暂时不会真正播放声音。
- 第一版不经过 `NiumaEquipment` 自动查询装备；武器动作来源由程序或测试脚本直接调用 `EquipWeaponSource` 传入。

## 版本

当前模块包版本：`0.6.0`。 
Unity 最低版本：`6000.0.0`，项目基准为 Unity 6.0 `6000.0.75f1`。
