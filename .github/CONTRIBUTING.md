# 贡献指南

感谢你愿意为蓝色大肥鱼出一份力。本指南说明如何在本地搭建环境、构建与验证、提交改动，以及怎样把 Issue 和 Pull Request 描述清楚。动手前请先读一遍，其中的命令均来自仓库现有脚本。

- [贡献指南](#贡献指南)
  - [参与方式](#参与方式)
  - [开始之前](#开始之前)
  - [环境准备](#环境准备)
  - [构建与运行](#构建与运行)
  - [验证改动](#验证改动)
  - [代码、资源与文件格式](#代码资源与文件格式)
  - [分支、提交与 PR](#分支提交与-pr)
  - [文档、版本与更新日志](#文档版本与更新日志)
  - [第三方素材与许可](#第三方素材与许可)
  - [安全问题](#安全问题)
  - [提交署名](#提交署名)

## 参与方式

- **报告问题**：在本仓库的 Issues 中提交，带上版本号（设置中的“模型与信息”可以查看）、平台、复现步骤和期望结果。涉及日志时，使用设置中日志控制台的“保存为 TXT”导出；日志已对目录做匿名化，不记录截图、OCR、聊天或提示词正文。
- **提出建议**：同样走 Issues。涉及界面或行为变化时，附上截图或示意会更好沟通。
- **提交代码**：从 `main` 拉出分支，按[分支、提交与 PR](#分支提交与-pr)的约定提交 Pull Request。
- **贡献素材**：动画、字体、地区数据等素材必须能追溯来源并允许再分发，提交前先看[第三方素材与许可](#第三方素材与许可)。

## 开始之前

- **先开 Issue 再动大改动**。修改动画权重、模型提示词、协议行为或安装更新流程都属于影响面较大的改动，先和维护者确认方向，能避免 PR 白做。
- **小步推进**。一个 PR 只解决一个问题；顺手的格式化、重命名、依赖升级请单独提交。
- **不要提交生成物**。`bin/`、`obj/`、模型文件、推理运行时和缓存已在 `.gitignore` 中排除，请保持仓库干净。

## 环境准备

| 用途 | 需要 |
| --- | --- |
| Windows 构建 | Windows x64、.NET 10 SDK（版本见 `code/global.json`），首次还原 NuGet 依赖需要网络 |
| Linux 构建 | WSL 中的 Ubuntu／Debian，以及 gcc、pkg-config、libdbus-1-dev、dpkg-deb、FFmpeg |
| macOS 构建 | Apple Silicon、macOS 14 及以上、Xcode 命令行工具、.NET 10 |
| 提交检查与部分验证脚本 | Python 3 |
| 连接 Agent 的测试 | 已安装并登录对应 CLI Agent，以及 Node.js 与 npm |

克隆后先启用仓库自带的 Git 钩子，提交和推送会按下面的署名规则自动检查：

```bash
git config core.hooksPath .githooks
```

需要本地模型时，用模型脚本准备一个目录，再通过环境变量或启动参数指过去。脚本会在目标目录的 `data` 子目录下创建 `models` 并保存 `model-selection.json`：

```powershell
./tools/Setup-Model.ps1 -Source HuggingFace -DestinationDirectory "D:\BigBlueFish-local"
```

也可以用 `-Source ModelScope` 走国内源，或用 `-Source Manual -ModelFile <GGUF 路径>` 导入已有模型。不准备模型时用 `-NoModel` 启动，行礼、问候、动画和鼠标交互照常可用，只是不会开口说话。模型和运行时都不要提交进仓库。

## 构建与运行

从仓库根目录：

```powershell
# 标准版（默认 Ocr 输入路线）
./code/build.ps1 -Locked -InputMode Ocr

# 运行
./code/run.ps1 -InputMode Ocr -RuntimeDirectory "推理运行时目录" -ModelsDirectory "D:\BigBlueFish-local\data\models"
./code/run.ps1 -InputMode Ocr -NoModel
```

- `-Locked` 会以锁定模式还原依赖，CI 与发布都要求打开它；改动依赖时请同步更新对应的 `packages.lock.json`。
- 无ACP版使用 `-p:CompanionEdition=Safe` 构建，它会排除 Agent 连接、控制服务与桥接程序，新增共享代码时请确认仍能通过无ACP版编译。
- `build.ps1`、`run.ps1`、`test.ps1` 都接受 `-SdkPath`，用于本机装了多个 .NET 时指定 SDK。

源码结构、各修改入口、地区数据更新方式，以及安装包与 Linux 包的真实构建命令，见[开发说明](code/README.md)与根目录 [README](README.md) 的“从仓库部署”一节。多平台、多发行类型的组合建议在提交 PR 前至少构建一次。

## 验证改动

先退出正在运行的桌宠，再运行测试。完整测试需要一个已安装的参考目录作为模型与运行时来源：

```powershell
./code/test.ps1 -InstalledDirectory "C:\Program Files\蓝色大肥鱼" -InputMode Ocr
```

按改动类型选择对应的测试项目，它们都在 `code/tests/` 下：

| 改动范围 | 对应测试 |
| --- | --- |
| 动画权重、日期与节日规则、动作选择 | `AnimationVerification`、`AnimationUiVerification` |
| 前台窗口捕获与 OCR 版面 | `ForegroundVerification`、`OcrVerification`、`OcrLayoutVerification` |
| 动画解码、媒体处理 | `MediaVerification` |
| 模型启动、提示词、子进程故障恢复 | `RunnerVerification`、`ModelStoreVerification` |
| Agent 连接、协议与桥接 | `AgentVerification`、`McpProtocolVerification`、`McpUiVerification` |
| 无ACP版排除结果 | `SafeEditionVerification` |
| Linux 窗口与行为 | `LinuxVerification` |
| 安装、卸载、下载恢复、更新事务与界面布局 | `InstallerVerification`、`MaintenanceUiVerification`、`PowerNoticeVerification` |

安装器验证项目的 `--lifecycle`、`--ui-layout`、`--deployment`、`--model-ui` 分别检查安装卸载生命周期、窗口布局、模型下载与更新恢复、下载失败出口，具体用法见[开发说明](code/README.md)。

只改文档、注释或提示词文字时也需要运行对应的验证：`RunnerVerification` 会检查当前提示词，`AnimationVerification` 会检查动画资源清单中的数量、文件与日期/权重规则。

## 代码、资源与文件格式

- 跟随 `code/.editorconfig` 的命名与诊断建议；编译器诊断会作为 IDE 提示出现，请顺手修掉自己引入的新警告。
- 文本文件的换行由 `.gitattributes` 统一：`.cs`、`.xaml`、`.json`、`.md`、`.sh`、钩子脚本使用 LF，`.ps1` 使用 CRLF。请让编辑器照此保存，不要让整个文件被格式化掉。
- 用户可见的文字一律使用中文，语气与鱼鱼的设定保持一致；不要把开发说明或工具调用细节写进气泡文案。
- 新增网络请求、文件写入、提权或进程启动时，在 PR 中说明触发条件、失败时的回退行为和用户可见的提示。

## 分支、提交与 PR

分支从最新的 `main` 拉出，使用带类型的前缀，例如 `fix/acp-reconnect`、`feat/linux-wayland`、`docs/contributing`。

提交消息沿用仓库现有风格，类型前缀为 `feat:`、`fix:`、`docs:`、`test:`、`refactor:`、`chore:` 等，标题一行说清做了什么，必要时在正文中补充原因和影响范围。

提交 PR 前自查：

- [ ] 已从最新 `main` 拉出分支，并已执行 `git config core.hooksPath .githooks`；
- [ ] 改动范围内的构建（含无ACP版）通过，对应测试已运行，并在 PR 中写明命令与结果；
- [ ] 用户可见的行为变化已在 `CHANGELOG.md` 中补充一条，版本号只在发布时由维护者统一修改；
- [ ] 新增或替换的素材已在 `code/THIRD-PARTY.md` 登记来源、版本、许可与校验值；
- [ ] PR 中没有包含 `bin/`、`obj/`、模型、运行时或本机设置等生成物。

PR 描述里请写清问题、修改方式、验证方式和已知限制。提交的提交会经过 `No Claude attribution` 检查（见[提交署名](#提交署名)），维护者合并时也会再确认一次署名。

## 文档、版本与更新日志

- `README.md` 面向使用者，`code/README.md` 面向开发者，`docs/` 下是协议、规格与平台说明。改行为时要一起改对应的规格文档。
- 当前版本的行为规格是 `docs/AGENT_FUNCTIONAL_SPEC.md`；已发布版本的历史规格在 `docs/specs/`，新版本发布时由维护者归档，贡献者通常只需要更新当前规格。
- 版本号统一记录在根目录的 `Version.props`。贡献者不要在自己的 PR 里修改版本号，由维护者在发布时更新 `Version.props`、`CHANGELOG.md` 和 `release/README.md`。

## 第三方素材与许可

本项目以 [Apache License 2.0](LICENSE) 发布。提交素材或新增依赖时：

- 只接受允许再分发的素材和依赖，并保留其原始许可文本；新增许可文件放在 `code/licenses/`。
- 在 `code/THIRD-PARTY.md` 登记来源地址、固定版本或提交号、许可、文件大小与 SHA-256，模型与运行时同样适用。
- 不要提交来源不明、有版权争议或只允许个人使用的素材，包括动画、字体、音效和模型权重。

## 安全问题

请**不要**通过公开 Issue 报告安全漏洞。请按 [SECURITY.md](SECURITY.md) 使用仓库 Security 页的私密漏洞报告，说明影响范围与复现步骤；修复发布后会在致谢中公开感谢。

## 提交署名

提交保留实际开发者的作者身份和真实共同作者。不得将 Claude 或 Anthropic 自动添加为作者、提交者、Co-authored-by 或 Signed-off-by；使用相关工具开发不等于为其分配仓库贡献者署名。此规则不禁止在功能说明中正常提及 Claude Code 等客户端名称。

本地开发前执行 `git config core.hooksPath .githooks`，启用仓库中的提交与推送检查。检查脚本使用 Python 3，可用 `python tools/Check-Attribution.py --self-test` 检查规则，用 `python tools/Check-Attribution.py HEAD` 检查当前完整历史。

PR 在合并前需要通过 `No Claude attribution` 检查。维护者在整合贡献时同时检查作者、提交者和提交消息中的署名尾注；如有自动生成的 AI 署名，先清理署名，再合并，并保留真实贡献者的身份和贡献记录。
