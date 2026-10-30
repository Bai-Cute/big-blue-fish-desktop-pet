# 连接本机 CLI Harness

标准版的“连接 Agent”页面通过 ACP 驱动用户本机已经安装的 CLI Harness。开启连接、选择 Harness 后，鱼鱼在后台启动该产品的 ACP 入口，建立自己的会话并安装 `big-blue-fish` 操作技能。用户双击鱼鱼即可输入，Enter 换行，Ctrl+Enter 或发送按钮提交。

## 支持目录

下表列出内置适配项。程序从 PATH 寻找对应命令；Windows 同时检查进程 PATH 与当前用户、系统 PATH，优先查找 exe、cmd、bat、ps1。软件不在检测时启动所有产品，也不安装所选 Harness 本体。各产品需要完成自己的登录、模型和运行环境配置。

| 名称 | 命令 | ACP 启动方式 |
| --- | --- | --- |
| Claude Code | claude | claude-agent-acp 适配器 |
| Codex CLI | codex | codex-acp 适配器，使用找到的 Codex 路径 |
| Gemini CLI | gemini | --acp --allowed-tools mcp_big-blue-fish_* |
| DeepSeek Harness | dsh | --profile acp |
| Kimi Code | kimi | acp |
| Qwen Code | qwen | --acp |
| Antigravity CLI | agy 或 agy_acp_server | Google 官方 agy_acp_server |
| GitHub Copilot CLI | copilot | --acp |
| Cursor CLI | cursor-agent 或 agent | acp |
| OpenCode | opencode | acp |
| Cline CLI | cline | --acp |
| CodeBuddy Code | codebuddy | --acp |
| Qoder CLI | qoder 或 qodercli | --acp |
| TRAE CLI | traecli | acp serve |
| Kiro CLI | kiro-cli | acp |
| Pi | pi | pi-acp 适配器 |
| Devin CLI | devin | acp |
| Grok Build | grok | agent stdio |
| Hermes Agent | hermes | acp |
| OpenClaw | openclaw | acp，连接其已配置的 Gateway |
| MiniMax Code | mcode | acp |

Claude、Codex 与 Pi 的 ACP 适配器优先从 PATH 查找；没有时安装到鱼鱼数据目录的 `agent/adapters/<id>`，使用 `HarnessCatalog` 中固定的 npm 版本。安装需要 Node.js 与 npm，下载使用官方 npm registry，缓存位于 `agent/adapter-cache`。Antigravity 使用带配套运行程序的已安装 ACP 组件，或下载 Google 官方发布包并核对 SHA256，同时准备 `agy_acp_server` 与 `localharness_external`，沿用官方启动参数。已下载组件参与检测，尚未准备时可直接连接并下载。原有 CLI 不被升级或卸载。

Agent 要求认证时，鱼鱼展示它公布的登录方式。协议认证通过 ACP `authenticate` 完成；终端认证会打开交互式登录终端，成功后重新连接并继续原对话请求。兼容 Kimi 等产品的旧 `_meta.terminal-auth` 格式，使用检测到的本机程序及其公布的登录参数。已有登录的复用由官方组件处理。认证可等待5分钟，也可以取消。Antigravity 的普通交互入口已经登录时，首次 ACP 连接仍可能需要选择 Google 账号等认证方式。

Windows 使用可直接启动的程序入口打开登录终端；Linux 使用系统已有的 GNOME Terminal、Konsole 或 xterm。登录方式和参数来自 Agent，认证窗口展示终端参数与环境项。没有公布认证方式的产品沿用它自己的配置、登录方式和“安装与登录说明”入口。生成开始前凭据失效时，鱼鱼可以恢复连接并继续原轮次；已经发生工具调用或发言的轮次不会自动重发。

这些条目表示程序采用的接入路径。CLI 版本、可用平台、账号及模型配置由各产品决定。DeepSeek Harness 的 ACP profile 与 Web profile 分开配置；OpenClaw 的 ACP 桥接需要可用的 Gateway。连接页显示实际协商状态与错误原因。

## 工作区、对话与项目

工作区是持久存放文件的目录，所有 Harness 使用当前选定的同一工作区。默认目录为用户文档下的“蓝色大肥鱼工作区”；系统没有提供文档路径时使用用户主目录下的 Documents。连接时准备目录与操作技能。工作区选择及最近路径保存在鱼鱼数据目录的 `agent/workspace-state.json`。

“新建工作区”创建独立文件夹并切换过去；“打开工作区”选择已有文件夹并切换；“查看文件夹”打开系统文件管理器。“清空工作区”显示目标路径并要求确认，删除其中全部文件和子目录，保留目录本身，重置此工作区的会话绑定与关心预约，并重新准备默认技能和人设。系统根目录、用户主目录、文档根目录、程序目录及包含鱼鱼控制数据的目录拒绝清空。目录链接不作为清空目标，子目录链接的外部目标保留。

项目由用户和 Agent 按任务组织，可以使用工作区内的子文件夹。新建对话或项目继续使用当前工作区。切换工作区会取消当前轮次与尚未执行的请求，关闭原自有连接，再按目标目录恢复或建立会话；工作文件保留。文件操作在后台执行。

会话记录按工作区绝对路径与 Harness 隔离，保存在 `agent/sessions/<工作区路径SHA256>/<id>.json`，包含会话编号、程序路径和工作目录。Windows 路径大小写不影响绑定。新会话通过 `session/new` 创建，同时提供工作目录和鱼鱼内部 MCP 的 stdio 配置。

重新连接时，若程序路径和工作目录一致，优先使用对方声明的 `session/resume` 或 `session/load` 恢复。仅在恢复返回会话或资源不存在时建立新会话；登录、网络、权限等错误保留原记录。没有恢复能力的产品在进程重启后新建会话。开始新对话按钮使用新的会话编号，并清除该 Harness 的关心预约。

对话消息通过 `session/prompt` 发出，一轮结束后继续使用同一会话。请求队列最多32项，普通日常事件及连接请求合并。模型轮次最多等待5分钟；取消或超时发送 `session/cancel`，并关闭该次自有进程，再恢复会话供后续消息使用。

## 模型、权限与思考选项

设置页的“模型与权限”根据 Agent 返回的配置绘制选择器，保留对方提供的名称、选项分组、说明和当前值。模型、权限／工作模式、思考深度及其他参数按返回顺序显示。初始值沿用 Harness，操作不会向其他配置文件写入固定模型或固定权限。实际作用范围遵循对应 Harness。

优先使用 ACP `configOptions`，支持 select 和已声明能力的 boolean 类型；通过 `session/set_config_option` 修改，并接收完整配置结果与 `config_option_update`。一次修改可以替换模型列表或思考选项。没有配置选项时兼容旧 `models`、`modes` 状态，分别调用 `session/set_model`、`session/set_mode`；不支持的切换方法改为只读展示。没有可调项的连接显示“由 Harness 配置”。连接、切换或生成期间，选择器等待当前请求结束后启用。

权限／工作模式与单项操作批准分别处理。Agent 请求执行文件、命令等操作时，鱼鱼展示它提供的具体内容和批准选项。切换模型、会话或工作区不把普通 ACP 回答送入气泡。

## 技能与人设

`big-blue-fish/SKILL.md` 与 `persona.md` 安装到鱼鱼工作目录中对应产品的技能目录。文件已存在时保留，用户可在设置中编辑或清空人设。操作技能描述何时使用、工具调用、日常关心和预约，人设描述鲸鱼娘的性格与语气。

新会话的第一次事件指明技能名称和路径；人设内容变化后再次要求读取。消息包含软件来源、事件类型、本地时间、当前接管模式与本次内容，中文正文保持可读。指南不会整份附在每一次消息中；Agent 可以读取安装文件，也可以调用 `get_operation_guide`。

## 文字与工具

Agent 准备让主人看到的话，通过 `say` 或 `say_next` 的 text 参数交给鱼鱼。普通 ACP 回答、思考、工作记录和恢复时的历史重放不进入气泡。内部 MCP 连接随 ACP 会话自动配置，工具参数见 [内部工具参考](MCP.md)。

开启连接时确认的权限包括鱼鱼自身的设置、动作、文字及截图工具。能够识别为鱼鱼自有工具的 ACP 权限请求可按单次授权响应；其他请求显示操作内容及对方提供的选项，交由用户选择。取消、断开或退出会结束等待。

## 日常关心与动作安排

| 触发和动作交给 Agent | 吐字交给 Agent | 行为 |
| --- | --- | --- |
| 关 | 关 | 沿用内置日常说话和动画逻辑 |
| 关 | 开 | 原有说话计时到期后向 Agent 发送 daily_care，由工具显示其编拟的文字 |
| 开 | 关 | Agent预约日常关心，到期由本地模型生成；Agent也可立即触发本地生成 |
| 开 | 开 | Agent 通过 schedule_wakeup 安排关心，鱼鱼执行计时并发送 wakeup |

双击对话在开启连接后固定可用；实际会话未就绪时保留输入框和连接反馈，发送按钮不可用。文字输入发起一轮 Agent 请求。点击、拖动事件保存在内部交互资源中；是否主动发起对应轮次由 `AgentRespondToClick`、`AgentRespondToDrag` 控制，两项默认关闭，可以由 Agent 修改，设置页只显示状态。

开启关心触发与动作接管时发送start_care，要求Agent检查并安排预约。预约可以一次或重复，带用途与编号，按Harness和会话隔离。吐字交给Agent时，到期向Agent发送wakeup；吐字交给本地模型时，到期直接调用本地模型。request_local_speech可立即请求真实本地生成。鱼鱼运行期间处理到期事件；隐藏、全屏隐藏、拖动或退出时不投递预约。恢复后的过期时段合并，不补发每个错过的周期。切换吐字编写者保留预约；关闭连接或离开关心触发与动作接管时清除当前Harness的日常预约。

说话间隔由鱼鱼自行触发时可调，Agent接管触发时置灰并显示说明。本地模型是否自动加载取决于吐字编写者及模型、供电设置；Agent只接管触发时，本地模型仍可驻留。

手动“天气”和“吐字测试”始终保留入口，分别按当前吐字编写者调用本地模型或Agent，接管日常触发不禁用按钮。Agent收到speech_test或weather_request后通过say显示结果；weather_request包含鱼鱼为所选地区准备的天气资料。

## 连接状态与数据

连接页区分未检测到、正在安装适配器、启动、恢复、新建、已连接、正在处理、等待权限、需要登录和失败。详情包含程序路径、Agent 版本、模型、会话编号和内部工具连接数；另显示下次预约时间。没有调用说话工具的一轮，状态明确显示未调用说话工具。

鱼鱼保存所选 Harness、设置、会话编号、技能、人设、预约及适配器文件。Agent 自己保存会话的方式遵循其配置，可能将输入或截图发送给配置的模型服务。鱼鱼不将 ACP 普通回答自动存成聊天记录。无MCP版在编译时排除 ACP、内部工具与对话页面。
