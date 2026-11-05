# 提交署名

提交保留实际开发者的作者身份和真实共同作者。不得将 Claude 或 Anthropic 自动添加为作者、提交者、Co-authored-by 或 Signed-off-by；使用相关工具开发不等于为其分配仓库贡献者署名。此规则不禁止在功能说明中正常提及 Claude Code 等客户端名称。

本地开发前执行 `git config core.hooksPath .githooks`，启用仓库中的提交与推送检查。检查脚本使用 Python 3，可用 `python tools/Check-Attribution.py --self-test` 检查规则，用 `python tools/Check-Attribution.py HEAD` 检查当前完整历史。

PR 在合并前需要通过 `No Claude attribution` 检查。维护者在整合贡献时同时检查作者、提交者和提交消息中的署名尾注；如有自动生成的 AI 署名，先清理署名，再合并，并保留真实贡献者的身份和贡献记录。
