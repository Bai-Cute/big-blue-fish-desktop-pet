# 蓝色大肥鱼内部工具参考

标准版通过 ACP 连接本机 CLI Harness，并在创建或恢复专用会话时自动提供本工具服务。用户操作见 [连接 Agent](ACP.md)。鱼鱼以当前进程随机命名的用户范围管道接收控制，随包 stdio 桥接程序使用 `--pipe <本次管道名>` 连接。管道配置由程序生成，无需手工加入全局 Harness 配置。

## 协议与结果

桥接程序通过 stdin/stdout 传递逐行 UTF-8 JSON-RPC 2.0，诊断只写 stderr。初始化协商支持 `2025-03-26` 和 `2025-11-25`，发送 `notifications/initialized` 后开始工具调用。支持 `ping`、`tools/list`、`tools/call`、日志级别、资源读取与订阅及取消。鱼鱼不通过 sampling 请求模型。

普通成功结果是 MCP `content` 数组，首项为 `type:text`，其 `text` 是 JSON 字符串。工具失败使用 `isError:true` 和说明文字；JSON-RPC 方法、资源和初始化错误使用协议 error。调用方先检查错误，再解析首项文本。`displayed`、`started`、`pending`、`queued` 表示提交状态，不能据此判断主人已看见或动作已播放完毕。

截图结果的首项文字包含目标、身份、宽高与截取时间，第二项是 `type:image`、`mimeType:image/png` 和 base64 `data`。图像在内存中取得；Windows 前台遵循鱼鱼已有前台选择规则，全屏包含全部显示器；Linux 由 X11 或 Wayland 门户捕获，门户授权由桌面环境处理。

## 设置

`get_settings` 返回 `settings` 与 `state`。settings 为持久化字段及系统启动项；state 为窗口可见性、隐藏来源、当前动作、播放阶段、动作队列、待说文字、工具连接、输入框、Agent状态和模型状态的快照。`modelAvailable` 表示有模型文件，不等于推理进程已驻留。

`update_settings` 参数为 `{"settings":{...}}`，省略的字段保留，字段名区分大小写，先验证整个补丁。`AgentEnabled`、`AgentHarnessId` 只读，由用户在连接页选择。以下为可写字段：

| 字段 | 类型与范围 | 含义 |
| --- | --- | --- |
| ModelEnabled | bool | 启用内置模型 |
| GpuEnabled | bool | 本地推理启用GPU |
| PublicInfo | bool | 内置天气、新闻 |
| ForegroundEnabled | bool | 内置模型读屏；不控制Agent截图工具 |
| StopModelOnBattery | bool | 离电暂停并卸载内置模型 |
| HideOnFullscreen | bool | 外部全屏时自动隐藏 |
| CheckUpdatesOnStartup | bool | 启动检查更新 |
| WeatherRegionCode | 六位字符串 | 内置地区表存在的区县代码 |
| Scale / BubbleScale | 0.5–2的有限数 | 角色／独立气泡倍率 |
| SpeechMinMinutes | 2、3、5、10、15、20之一 | 分别对应2–3、3–5、5–8、10–16、15–23、20–30分钟 |
| BubbleDurationIndex | 0–7整数 | 5、10、15、30、60、120、180秒，或直到下一句 |
| AnimationIntervalSeconds | 30–300整数 | 普通随机动作事件等待秒数，等完整片段结束再切换 |
| BreakfastHour / LunchHour / DinnerHour | 0–23整数 | 本地时间餐时中心小时 |
| Left / Top | -1000000–1000000有限数或null | Windows逻辑坐标，Linux像素；受工作区约束，null不主动移动 |
| LastBrushDate | yyyy-MM-dd有效日期或null | 每日刷牙去重日期 |
| StartupEnabled | bool | 当前用户开机启动 |
| PuppetMode | bool | 完全外部接管；开启须等待用户确认 |
| AgentDailySpeech | bool | 日常吐字交给Agent |
| AgentRespondToClick | bool | 点击事件主动发起ACP轮次，默认关 |
| AgentRespondToDrag | bool | 拖动结束主动发起ACP轮次，默认关 |

更新设置不打开、激活或置顶设置窗口。窗口已打开时保留页面、滚动、位置和窗口状态并刷新。修改GPU、模型、地区等按现有逻辑取消或重排对应任务；Linux每次应用设置都会停止本地模型并重排计时。开启关心触发与动作接管的确认异步显示，等待时角色仍可拖动；取消则不应用该补丁。IO或系统启动项写入失败可能发生在内存设置已变更后，调用方读取状态再决定下一步。

## 截图、动作与文字

| 工具 | 输入 | 行为 |
| --- | --- | --- |
| list_actions | 空对象 | 返回106项精确action、pool、基础weight和名义duration_seconds |
| capture_foreground | 可选max_dimension | 当前前台截图，默认最长边2560 |
| capture_screen | 可选max_dimension | 虚拟桌面截图，默认最长边4096 |
| play_action | action | 加入FIFO队列，最多100项 |
| interrupt_action | action | 清空未播放队列，立即切换动作 |
| say | text | 立即原文逐字显示，替换当前气泡并取消旧本地生成 |
| say_next | text | 覆盖待说原文，在下次符合日常节奏时显示 |
| request_local_speech | 空对象 | 立即请求真实本地模型按当前情境生成日常关心；需要AgentDailySpeech=false和可用的本地模型 |

max_dimension为256–16384整数。action精确匹配list_actions，最多200字符，Disabled池动作也可指定。重复动作不去重；隐藏或拖动时排队动作等待。外部动作播完后，普通模式恢复自主调度，关心触发与动作接管回到固定待机。立即动作和说话不会自动恢复被隐藏的角色。

text非空白且最多4000个UTF-16单元，按约28个组合字符/秒显示，保留原文，不执行Markdown、不经过本地模型文本清理。外部原文不依赖本地模型、GPU或插电。say不清除say_next；关闭连接或退出清除待说原文。say_next服从正常可见性、设置窗口、倒计时、拖动及空闲门槛，关心触发与动作接管暂停该节奏；立即说话应使用say。

## 操作指南与预约

`get_operation_guide` 无参数，返回工作目录中已安装的技能正文、人设与相应文件路径。文件内容以用户实际保存的版本为准。

`get_wakeups` 无参数，返回当前Harness、会话的预约以及预约机制是否启用。

`schedule_wakeup` 输入：

```json
{
  "purpose": "看看主人是否适合休息一下",
  "delay_seconds": 600,
  "repeat_seconds": 600
}
```

purpose为1–1000字符；delay_seconds与可选repeat_seconds为30–604800整数秒。省略或null的repeat_seconds表示一次性。可传最多100字符的既有id修改当前会话的预约，新预约返回自己的id、Harness、会话、用途、到期时间和周期。全局最多32项。

创建需要连接就绪并开启关心触发与动作接管。切换吐字编写者保留预约；关闭连接、离开关心触发与动作接管或开始新对话会清除当前Harness的预约。预约不要求Agent在一轮内持续等待，当前轮次可以结束。

AgentDailySpeech=true时，到期通过ACP向同一对话提交wakeup，附预约用途；false时，到期由鱼鱼直接调用本地模型生成日常关心。隐藏、拖动或未连接时推迟；多个到期预约合并。一次预约在提交前移除，重复预约从本次提交时间重新计时。Agent可检查、取消或修改后续预约。

`cancel_wakeup` 输入 `{"id":"预约编号"}`，只取消当前Harness会话中的对应项，返回是否取消。

## 事件与输入

点击、拖动开始和结束，以及用户输入形成sequence、UTC time、kind、detail事件。最近128项只保存在控制服务内存中，URI为`bigbluefish://interactions`。`resources/read`返回当前数组，不清空；`resources/subscribe`通过`notifications/resources/updated`告知变化，调用方自行读取。

info级别的`notifications/message`使用logger `bigbluefish.interaction`，可以通过`logging/setLevel`调整。通知和资源是同一事件的两种获取方式，按服务实例和sequence去重。超过队列容量的旧事件被覆盖，服务关闭释放事件记录。

双击输入直接通过ACP发起user_message，内容为主人提交的原文。Agent普通文字不进入气泡；回复通过say完成。输入固定可用，连接未就绪时发送按钮置灰并保留草稿。点击和拖动是否另发起Agent轮次由两个响应字段决定。

## 生命周期

关闭连接或退出取消工具请求、确认等待和ACP进程，释放内部服务。请求可通过`notifications/cancelled`取消；工具在执行前及异步工作后检查取消。动作和文字已经提交到UI后的副作用不自动回滚。多个工具请求可同时等待，UI中的实际控制由相同调度器执行。

Agent自行保存会话和处理模型请求；输入、截图或工具结果可能经其配置发送给云端模型。鱼鱼的连接告知统一覆盖其自身的控制工具；普通文件、终端等Agent操作仍按对应Harness的权限请求处理。
