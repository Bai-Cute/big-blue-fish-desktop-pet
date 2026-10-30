# 蓝色大肥鱼 · 1.1.0

本次更新完全重构了先前通过 MCP 连接实现的 Agent 功能，改用稳定、实用的 ACP 连接。鱼鱼会在后台直接启动本机已安装的 CLI Agent，建立专用会话，并为它自动接入动作、截图、说话等内部工具，不再需要手动把 MCP 配置填进 Agent。

连接现在只需一键：在设置的“连接 Agent”页面打开连接并选择 Agent 即可。页面会显示连接状态、Agent 版本、模型和会话编号，并提供重新检测、重新连接、开始新对话和取消当前请求等操作。Agent 需要登录时，可以选择它提供的登录方式，也可以在弹出的终端中完成登录。

本版本支持 21 款主流 CLI Agent，包括 Claude Code、Codex CLI、Gemini CLI、DeepSeek Harness、Kimi Code、Qwen Code、GitHub Copilot CLI、Cursor CLI、OpenCode 等。Claude Code、Codex CLI 和 Pi 所需的 ACP 适配器会通过 npm 自动安装，Antigravity CLI 的官方 ACP 组件也会在首次连接时自动准备。

大肥鱼现在拥有自己的工作区，所有 Agent 共用当前工作区中的文件，设置中可以新建、打开、查看和清空工作区。会话按工作区与 Agent 分别保存，多轮对话中的消息会在同一会话中保持，重新连接时优先恢复原有会话。设置页还可以直接切换当前 Agent 提供的模型、权限与工作模式，以及思考选项。

首次连接时会为 Agent 安装“大肥鱼操作”技能，其中包含鱼鱼的人设与工具用法，人设可以在设置中编辑。新增“把日常关心的吐字交给 Agent”和“把日常关心的触发和日常的动作切换逻辑交给 Agent”两个开关，可以分别开启：前者由 Agent 编写日常关心的内容，后者由 Agent 安排动作并预约下一次关心的时间。天气和吐字测试按当前设置交由本地模型或 Agent 回答。

依旧提供不含 Agent 连接的无ACP版。无ACP版在编译时移除 Agent 连接、内部控制服务和对话输入框，应用内更新会继续获取无ACP版。

Windows 标准版下载 `BigBlueFish-v1.1.0-OCR-Setup.exe`，无ACP版下载 `BigBlueFish-v1.1.0-Safe-OCR-Setup.exe`；Ubuntu／Debian x64 标准版下载 `BigBlueFish-v1.1.0-Linux-x64.deb`，无ACP版下载 `BigBlueFish-v1.1.0-Safe-Linux-x64.deb`。文件名中的 `Safe` 对应无ACP版，各安装包均提供配套 `.sha256` 校验文件。已安装的用户可以在设置中检查更新直接升级，也可以手动运行新版安装器；Windows 上选择原安装目录即可保留模型与设置。

连接方式与适配列表见 [连接本机 CLI Harness](https://github.com/Bai-Cute/big-blue-fish-desktop-pet/blob/v1.1.0/docs/ACP.md)，内部工具参数见 [内部工具参考](https://github.com/Bai-Cute/big-blue-fish-desktop-pet/blob/v1.1.0/docs/MCP.md)，完整行为见 [功能规格](https://github.com/Bai-Cute/big-blue-fish-desktop-pet/blob/v1.1.0/docs/AGENT_FUNCTIONAL_SPEC.md)，版本变化见 [更新日志](https://github.com/Bai-Cute/big-blue-fish-desktop-pet/blob/v1.1.0/CHANGELOG.md)。
