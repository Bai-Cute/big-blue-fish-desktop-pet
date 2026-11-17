# 设计：DeepSeek Harness 桌面版接入

## 背景

“连接 Agent”目前只认 PATH 里的 `dsh`，参数 `--profile acp`。DSH 桌面版把同一套 CLI 打包在
`resources/app.asar` 里，不往 PATH 写 `dsh`，所以只装桌面版的用户永远显示“未检测到”。

桌面版自带 Electron 运行时。以 `ELECTRON_RUN_AS_NODE=1` 启动该运行时并传入包内
`@deepseek-ai/dsh/lib/bin.js`，就能得到与 npm 版完全相同的 ACP stdio 端点。协议层无需改动。

已验证（0.2.2-rc.3，Windows）：

```text
"DeepSeek Harness.exe"  (ELECTRON_RUN_AS_NODE=1)
  resources/app.asar/dsh/node_modules/@deepseek-ai/dsh/lib/bin.js  --profile acp --help
→ Usage: dsh --profile acp [options] / Serve automation clients over Agent Client Protocol stdio.
```

## 目标

- 新增目录条目 `dsh-desktop`（DeepSeek Harness 桌面版），无需用户安装 `dsh` CLI。
- 复用现有 ACP 通道、会话、技能、模型选择、权限逻辑，协议层不做任何改动。
- 桌面版不存在或过旧时给出可操作的提示，而不是笼统的失败。

## 非目标

- 不接管正在运行的桌面应用窗口或它的 GUI 会话。
- 不把桌面版当作 ACP 客户端去驱动；我们仍然是 ACP 客户端。
- 不下载或安装桌面版本体。

## 用户可见行为

1. 连接页出现第 22 项“DeepSeek Harness 桌面版”，检测到显示“已检测到”，否则“未检测到”。
2. 同时装了 npm 版和桌面版时，两者是独立条目，各自有独立会话与预约。
3. 选中桌面版连接后，握手、会话、`say`、技能加载与本机 CLI 完全一致；详情里的程序路径是桌面版可执行文件。

## 探测规则

安装根目录按平台依次尝试，第一个通过校验的胜出：

| 平台 | 来源 |
|---|---|
| Windows | `%LOCALAPPDATA%\Programs\DeepSeek Harness` |
| Windows | `%ProgramFiles%\DeepSeek Harness`、`%ProgramFiles(x86)%\DeepSeek Harness` |
| Windows | 运行中的同名进程的可执行文件目录 |
| Windows | 卸载注册表项的 `InstallLocation`（通过 `reg.exe` 读取，不引入 NuGet 包） |
| macOS | `/Applications/DeepSeek Harness.app`、`~/Applications/DeepSeek Harness.app` |
| Linux | `/opt/DeepSeek Harness/deepseek-harness`、`/opt/deepseek-harness/deepseek-harness` |
| Linux | 运行中的同名进程的可执行文件目录（AppImage 唯一可靠来源） |

macOS 的应用可执行文件是 `Contents/MacOS/DeepSeek Harness`；其余平台的资源目录是
`<安装根>/resources`，macOS 是 `Contents/Resources`。

## 校验

普通 .NET 进程看到的是 `app.asar` 这个文件本身，读不到里面的目录。因此校验改为解析 asar
档案头部（pickle：4 字节填充、头部长度、字符串长度、JSON 索引），在索引里确认三个路径：

1. `dsh/desktop-runtime.json` 存在，说明这是 DSH 桌面版而不是别的 Electron 应用；
2. `dsh/node_modules/@deepseek-ai/dsh/package.json` 存在，说明应用带有 harness 本体；
3. `dsh/node_modules/@deepseek-ai/dsh-acp-app/package.json` 与 `dsh/node_modules/@deepseek-ai/dsh/lib/bin.js` 存在，
   说明这个版本带 ACP 组件与入口。

任一条不满足就丢弃该候选，继续下一个；全部失败返回“未检测到”。校验只读档案头部，不启动进程。

## 启动

`HarnessDefinition` 增加可选属性 `DesktopBundle`（asar 内相对路径）。当它非空时：

- 可执行文件 = 桌面版可执行文件；
- 参数 = `[<asar>/<DesktopBundle>, ...Arguments]`，即 `bin.js --profile acp`；
- 环境变量追加 `ELECTRON_RUN_AS_NODE=1`。

`ELECTRON_RUN_AS_NODE` 让 Electron 跳过自身引导，因此不会与正在运行的桌面版抢单实例锁。

## 边界

| 单元 | 职责 |
|---|---|
| `HarnessCatalog` | 只放条目数据：id、名称、参数、技能目录、`DesktopBundle` |
| `HarnessDesktopLocator`（新增） | 平台探测、`desktop-runtime.json` 校验、参数拼装；不知道 ACP |
| `AgentAdapterStore` | 解析启动命令：有适配器走适配器，有 `DesktopBundle` 走桌面版，否则原样返回 |
| `AgentCoordinator` | 只加一个环境变量赋值，沿用现有 per-harness 分支的写法 |

## 失败处理

| 情况 | 提示 |
|---|---|
| 用户既没装 CLI 也没装桌面版 | 现有“未找到…请先按官方说明安装”文案 |
| 装的是旧桌面版（无 `dsh-acp-app`） | “此桌面版过旧，不包含 ACP 组件；请升级桌面版，或安装 `dsh` CLI。” |
| 桌面版目录被移动或删除 | 探测失败 → 回到“未检测到”，不缓存失效路径 |

## 测试

1. 目录条目：共 22 项且 id 唯一；`dsh-desktop` 的 `Arguments` 为 `--profile acp`、`DesktopBundle` 非空。
2. 参数拼装：给定伪造的可执行文件与 bundle 路径，得到 `[bin.js, "--profile", "acp"]`。
3. 校验：缺少 `app.asar`、缺少 harness 包、缺少 ACP 包、缺少入口脚本四种情况都判定失败，完整档案判定通过。
4. 真机握手：`--dsh-desktop` 直接驱动 ACP；`--probe dsh-desktop` 走完整的 `AgentCoordinator` 路径。

## 验证记录（Windows，0.2.2-rc.3）

```text
--dsh-desktop
EXECUTABLE D:\Softwares\deepseek\DeepSeek Harness.exe
ARGUMENTS ["...\resources\app.asar\dsh\node_modules\@deepseek-ai\dsh\lib\bin.js","--profile","acp"]
INIT {"agent":"deepseek-harness-acp","version":"0.0.1","capabilities":{"sessionCapabilities":{"close":{},"list":{},"resume":{}}}}
SESSION <uuid>；CONFIG 返回 model 与 reasoning_effort 两个选择器
RESULT desktop runtime served ACP: initialize, session/new and session/close all answered

--probe dsh-desktop（完整产品路径）
正在启动 DeepSeek Harness 桌面版… → 正在建立鱼鱼专用对话… → 已连接
ACP connected; version=0.0.1; restores=True
```

`verification.json` 记录 `executable` 为桌面版可执行文件、`sessionReady: true`，工作区技能
`workspace/.agents/skills/big-blue-fish/SKILL.md` 与 `persona.md` 均已安装。
`AgentVerification` 全套 131 项通过（含上述 asar 校验用例）。

## 风险

- Linux AppImage 挂载点随机，只能靠运行中的进程发现；用户没开着桌面版时可能显示未检测到。
- 桌面版升级若改变 asar 布局，条目需改 `DesktopBundle`；`desktop-runtime.json` 的 `schemaVersion` 是把它挡在“提示升级”的依据。
- macOS 上 `ELECTRON_RUN_AS_NODE` 在已签名应用中是否同样生效，需要在 mac 上验一次（本设计只在 Windows 验证）。
