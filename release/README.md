# 蓝色大肥鱼 · 1.0.0

蓝色大肥鱼现在可以通过 MCP 连接桌面 AI Agent 啦。开启接口后，Agent 可以读取屏幕、调整设置、安排动作，也可以让鱼鱼说出指定的话。开启“双击与鱼鱼对话”后，在 Agent 已经连接时，双击鱼鱼便能打开输入框，把想说的话交给它。内置模型暂不支持对话。

本次加入提线木偶模式。开启后，鱼鱼会暂停原有的自主说话和动作切换，安静地循环待机，等待 Agent 安排新的动作与文字。MCP 开启前会提示截图和输入内容的使用权限，请留意隐私安全；Agent 断开后，可以关闭提线木偶模式恢复普通陪伴。

1.0.0 同时提供 Windows 一键安装器和 Ubuntu／Debian x64 的 Linux `.deb` 安装包。由于 MCP 连接涉及上述隐私风险，两个平台均额外提供不包含 MCP 功能的无MCP版，主人可以按自己的使用方式选择。

Windows 标准版下载 `BigBlueFish-v1.0.0-OCR-Setup.exe`，无MCP版下载 `BigBlueFish-v1.0.0-Safe-OCR-Setup.exe`；Linux 标准版下载 `BigBlueFish-v1.0.0-Linux-x64.deb`，无MCP版下载 `BigBlueFish-v1.0.0-Safe-Linux-x64.deb`。文件名中的 `Safe` 对应无MCP版，各安装包均提供配套 `.sha256` 校验文件。更新 Windows 已有安装时选择原目录，模型与设置可以继续使用。

Agent 配置与完整接口说明见 [MCP 接入参考](https://github.com/Bai-Cute/big-blue-fish-desktop-pet/blob/v1.0.0/docs/MCP.md)，完整行为见 [功能规格](https://github.com/Bai-Cute/big-blue-fish-desktop-pet/blob/v1.0.0/docs/AGENT_FUNCTIONAL_SPEC.md)，版本变化见 [更新日志](https://github.com/Bai-Cute/big-blue-fish-desktop-pet/blob/v1.0.0/CHANGELOG.md)。
