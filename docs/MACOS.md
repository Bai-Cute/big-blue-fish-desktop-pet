# macOS 桌宠与对话

## 已确认范围

支持 Apple Silicon（osx-arm64），提供可启动的 `.app`、106 段透明动画、点击回应、拖拽、右键与状态栏菜单、大小和位置保存，以及退出时清理解码进程。双击桌宠打开对话；聊天可选 OpenAI 兼容 API、本机 GGUF 模型或 ACP Agent。应用不读屏、不自动更新；本地模型只在用户明确点击下载时获取，不在后台自动下载模型或 Agent。Windows/Linux 入口保持不变。

## 对话和设置

双击桌宠打开“和大肥鱼聊天”窗口，也可从右键菜单或状态栏选择“打开对话”。桌宠右键菜单会在应用失去前台时自动关闭。聊天窗口标题区可拖动；固定后失焦时保持打开，未固定时失焦自动隐藏。回车发送消息，Shift + 回车换行。窗口默认显示完整记录并滚动至底部；向上阅读时新消息不会改变当前位置，底部会出现可点击的新消息提示。对话窗口透明度可调，蓝色与水色主题与桌宠协调。

发送的消息会立即进入聊天记录并按顺序处理。当前请求显示“正在思考…”，后续请求显示“排队中…”，生成期间可以继续输入和发送；每条回复会更新到对应消息下方。

所有后端配置都在“设置”窗口。设置采用普通白色窗口，调整桌宠大小、对话透明度和后端参数后，点击“保存设置”生效；直接关闭设置则丢弃尚未保存的更改。API Key 保存在 macOS 钥匙串中，可跨应用重启使用；不会写入设置 JSON。模型下载需要用户明确发起；Agent 不会自动下载。

设置中的“大肥鱼人格”以项目人设卡为基础，包含鱼鱼的身份、性格、称呼、说话方式、角色化小愿望和能力边界；可以直接编辑或恢复默认。API 和本机 GGUF 模型会把它作为系统级指令发送；修改人格后会清除旧对话上下文，避免旧历史继续影响新设定。ACP 没有通用的系统提示词字段，因此应用会把人格随每轮用户消息发送，并要求 Agent 遵循。所选 ACP Agent 的系统提示优先级可能更高；如果它仍自称 ChatGPT 或不遵循角色设定，需要在该 Agent 自己的配置中调整身份指令。旧版内置默认人格会升级为此完整设定，自定义人格会保留。

API 模式默认填写 DeepSeek 官方 Base URL `https://api.deepseek.com` 和当前模型 `deepseek-flash`；Base URL、模型及 API Key 都可自行修改。可选择 OpenAI 兼容的 Chat Completions 接口或 Responses 接口；DeepSeek 当前同时支持两种格式，默认使用 Chat Completions。远程地址须使用 HTTPS；本机回环地址（`localhost`、`127.0.0.1`、`::1`）可使用 HTTP。请确认自定义服务值得信任，因为发送的消息与所填密钥会交给该地址。GGUF 模式需填写本机 `llama-server` 可执行文件和 GGUF 文件的绝对路径，由应用启动及结束服务器进程。ACP 模式选择 Agent 类型后，应用查找已安装的对应可执行文件并预填绝对路径；路径始终可手动修改，然后勾选风险说明。选择后端本身不会连接或启动模型；发送第一条消息时才开始请求。

## 本地 4B 模型

设置中的下载入口提供原仓库固定修订的两种 Qwen3.5-4B Q4_K_M GGUF：Hugging Face 的 heretic 转换版（约 2.71 GB，默认推荐）和 ModelScope 的原版转换（约 2.71 GB，国内源）。模型文件不随 `.app` 分发，只有点击“下载”才联网；界面显示下载进度，也可暂停。文件放在用户的 `~/Library/Application Support/BigBlueFish/models/`；下载中的 `.partial` 文件在暂停或网络中断后保留，下次点击可续传。完成后校验仓库固定的 SHA-256 和 GGUF 文件结构，通过后自动把 GGUF 路径填入当前设置草稿；还需点击“保存设置”才能用于聊天。关闭设置会丢弃未保存的路径等配置，但不会删除已下载的模型文件。两个来源模型内容不同；精确文件名、修订号、大小及校验值见 `deployment/ModelStore.cs` 与 `code/THIRD-PARTY.md`。

下载模型不会安装推理程序。使用 GGUF 聊天仍需在设置中提供兼容的 `llama-server` 可执行文件；应用以 `local` 别名启动它，无需再填写云端模型名称。未下载时仍可使用 API 或 ACP 对话。

## 架构

独立入口位于 `code/BigBlueFish.Mac`，使用仓库已有 Avalonia 12.1.4。桌宠复用动作策略、偏好设置和气泡绘制；动画解码复用 Linux 播放器，仅增加可选解码器路径和像素格式参数，保持 Linux 默认行为不变。macOS 原生窗口适配负责透明像素鼠标穿透；设置保存在用户 Application Support，不写入应用包。聊天、Agent 会话、API 凭据和模型下载分别由独立组件管理。

## 验收

构建和断言测试通过；应用包含托管运行时、动画、字体和图标；FFmpeg 缺失时明确报错；真实窗口能播放动画，设置可打开且保存，退出不残留 FFmpeg。鼠标穿透、拖拽与多屏行为需要真实桌面检查，不把编译通过当作交互通过。对外发布的 Developer ID 签名、公证与独立分发 FFmpeg 不属于本地基础版范围。

## 构建与验证

依赖：Apple Silicon Mac、.NET 10 SDK、Xcode Command Line Tools、已安装的 FFmpeg（例如 `brew install ffmpeg`）。发布包内置 .NET 运行时，但目前不内置 FFmpeg；后者依次从 `BIGBLUEFISH_FFMPEG`、包内 `runtime/ffmpeg`、`/opt/homebrew/bin/ffmpeg`、`/usr/local/bin/ffmpeg` 定位。环境变量必须是绝对路径。

```bash
dotnet run --project code/tests/MacVerification -c Release -r osx-arm64
bash tools/build-macos.sh
python3 tools/test-macos.py release/macos/BigBlueFish.app
open release/macos/BigBlueFish.app
```

构建脚本只输出到 `release/macos/BigBlueFish.app`。它先在同一 `release/` 下完成暂存构建，再替换现有 `.app`；失败时保留上一版。真实窗口烟测使用隔离设置，检查动画解码、白色不透明设置窗口、关闭未保存设置不会写入、明确保存后写入、对话窗口默认不连接、隐藏/恢复、退出和解码进程清理。鼠标穿透单元测试不等于物理鼠标与多显示器验收。烟测需要已登录且正常工作的 macOS 图形桌面；若锁屏或无显示器导致 Avalonia.Native RenderTimer 无法启动，须在可用桌面上重试。

## ACP 方案与接入

2026-11-01 核对：OpenCode 官方支持 `opencode acp`，通过 stdio JSON-RPC 通信；Claude 与 Codex 可分别使用 ACP 组织维护的 `claude-agent-acp` 与 `codex-acp`。Mac 版直接复用仓库 `BigBlueFish.Agent/AcpConnection.cs`，不另造 JSON-RPC 实现，不自动下载适配器。参考：

- https://docs.opencode.ai/docs/acp/
- https://github.com/agentclientprotocol/claude-agent-acp
- https://github.com/agentclientprotocol/codex-acp

在设置中勾选风险说明，选择已安装的 Agent/适配器。应用从 PATH 及常见 macOS 安装目录查找 `opencode`、`claude-agent-acp` 或 `codex-acp`，找到后预填路径；未找到时可手动输入，已保存的手动路径不会被查找结果覆盖。先在终端完成官方 CLI 登录，应用内不执行认证。首次发送消息时建立 ACP 连接。支持同一连接中的多轮聊天、回复气泡和逐项批准；不提供永久批准选项。会话尚不跨应用重启恢复。

安全边界：默认关闭；不注入 MCP 桌宠工具，不提供截图，不执行 Agent 输出的文本。Agent 仍可能按其自身配置访问文件、运行命令和联网，批准窗口不是沙箱；尤其不能假设所有 Agent 操作都会请求批准。使用专用工作区 `Application Support/BigBlueFish/agent/workspace`，但这不是文件系统隔离。

已验证：ACP fixture 子进程握手、分块回复、会话隔离、多轮对话、输入长度限制和断开。现有 AgentVerification 在 Mac 默认临时目录下暴露 Unix socket 路径过长问题；仅在 `TMPDIR=/tmp` 的测试进程中通过全部 93 项 fixture 检查。本版只走 ACP stdio，不启用有此问题的命名管道工具桥。真实账号生成与各适配器端到端兼容性尚未宣称通过。

后续如果需要 Agent 控制动作或截图，再单独设计工具允许列表、授权和短路径工具桥，不把聊天接通当作全功能 Agent 集成。
