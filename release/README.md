# 蓝色大肥鱼 · 1.0.1

本版本提高了本地大模型在各种设备上运行的稳健性。启用 GPU 时，程序会根据设备当前可用显存选择计算缓冲区大小，并由 llama.cpp 自动分配模型层，改善小显存设备的加载兼容性；GPU 初始化失败时自动切换到 CPU 兼容模式，并在设置中显示实际运行模式和加载失败原因。固定提示词在模型准备阶段预先处理并复用缓存，减少每次生成时的重复计算，提示词内容与说话风格保持不变。

与已连接 Agent 对话的输入框改为紧贴角色显示的紧凑浮层，空输入时显示提示文字“通过鱼鱼和已连接的Agent说话”，并随角色移动、隐藏和恢复。多行输入、Ctrl+Enter 发送和断线时保留草稿的行为保持不变。

模型自动下载过程中新增“放弃并转为手动安装”选项，确认后清理本次下载的未完成文件并进入手动安装；下载意外中断后可以从已有进度继续。

此外修复了若干问题。感谢 Tydecent 为本版本提交的贡献。

Windows 标准版下载 `BigBlueFish-v1.0.1-OCR-Setup.exe`，无MCP版下载 `BigBlueFish-v1.0.1-Safe-OCR-Setup.exe`；Ubuntu／Debian x64 标准版下载 `BigBlueFish-v1.0.1-Linux-x64.deb`，无MCP版下载 `BigBlueFish-v1.0.1-Safe-Linux-x64.deb`。文件名中的 `Safe` 对应无MCP版，各安装包均提供配套 `.sha256` 校验文件。已安装的用户可以在设置中检查更新直接升级，也可以手动运行新版安装器；Windows 上选择原安装目录即可保留模型与设置。

Agent 配置与接口说明见 [MCP 接入参考](https://github.com/Bai-Cute/big-blue-fish-desktop-pet/blob/v1.0.1/docs/MCP.md)，完整行为见 [功能规格](https://github.com/Bai-Cute/big-blue-fish-desktop-pet/blob/v1.0.1/docs/AGENT_FUNCTIONAL_SPEC.md)，版本变化见 [更新日志](https://github.com/Bai-Cute/big-blue-fish-desktop-pet/blob/v1.0.1/CHANGELOG.md)。
