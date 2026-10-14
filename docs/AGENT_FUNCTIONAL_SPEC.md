# 蓝色大肥鱼 Git版：接手 agent 完整功能规格

文档维护日期：2026-10-14。当前公开版本为 `v0.4.0`；`v0.3.1` 是上一版维护记录，恢复行为的历史源码快照为 `b128cb00dcfac73de6e73de4cecb4253b5b7e226`。
2026-10-14 公开维护更新：0.4.0 公开 Release 只维护 OCR 路线；Vision 源码仍保留用于历史维护和对照，上一版视觉发行记录见第 25 节。
本文件是公开仓库中的完整功能规格，供开发者和 AI 接手、修改、构建与验收时使用。README 提供面向用户的简介，本文件记录源码实际行为、输入输出契约、状态机、资源、发布流程和验证方法。
本文面向需要维护、扩展、验证和发布该软件的 agent，独立于 README。

2026-10-14 本地0.4.1已移除未使用的VPet引擎和游戏入口，当前工程组成与清理契约见第29节；第1—27节涉及三个应用程序集、旧界面或旧依赖的内容为历史记录，不再作为当前构建清单。

## 0. 如何使用本规格

1. 先读第 1、2 节，明确项目身份、不可替代的用户要求及维护边界。
2. 根据任务读功能章节，沿第 3 节入口定位实际源码。
3. 修改前检查当前分支、工作区和实际运行程序路径；不能凭目录名称认定正在运行哪一版。
4. 修改后按第 20 节选相关验收，必要时重建发布物并测试实际发布入口。
5. 功能变化时同步修改本文件、必要的使用说明和验证记录；不能仅改 README 后宣称已经交接。

本文有三种不同性质的信息：

- **要求**：用户希望保留的产品行为。实现缺陷不能自动取消这些要求。
- **当前实现**：从基线源码核对的事实，含实际默认值、算法、状态和限制。
- **验证记录**：仓库内历史结果所记录的证据，不代表读到文档的 agent 已在自己的环境重测。

文件内路径均相对仓库根目录。外部来源链接仅用于标识已有依赖和来源，并非本次重新验证过的在线最新版本。
本仓库的 spec 是维护依据，不是逐行反编译说明；源码是精确调用关系、异常和参数的最终核对入口。

## 1. 项目身份、版本和恢复来源

### 1.1 三个名字必须区分

| 用户称呼 | 本机目录 | 用途 |
| --- | --- | --- |
| 安装版 | `D:\Program Files\蓝色大肥鱼` | 原先安装运行的版本，提供原行为核对和已有运行依赖 |
| 复原版 | `D:\AIRelated\CodexProject\2026-09-28\new-chat\outputs\蓝色大肥鱼-独立恢复工程` | 独立恢复出的可读源码工程 |
| Git版 | `D:\AIRelated\CodexProject\2026-09-28\new-chat\outputs\蓝色大肥鱼-Git版` | 本仓库，在新副本中继续改进、准备上传 GitHub |

路径是这台电脑在文档交付时的路径，移机后必须重新定位。不能以“修复 Git版”为理由覆盖安装版或复原版。
用户明确认为先前 GPT 6 Sol 的恢复成果不可信，不得从以下旧产物重新引入源码、构建 DLL 或生成脚本：

- `outputs/蓝色大肥鱼-恢复源码`。
- `outputs/蓝色大肥鱼-GitHub准备版`。
- 与旧恢复相关的 `work/recovered`、`work/port-companion.py` 等。

当前工程的恢复基于原始开发补丁记录、固定版本 VPet 上游和对安装程序的重新核对。
不是只把反编译文本当成长期工程，也不把旧恢复包当作可信中间产物。
源码生成 Windows、Core、Interface 三个应用 DLL；安装目录的同名 DLL 不作为应用项目构建引用。
来源校验见 [provenance.json](../code/provenance.json)，恢复基线见 [recovery-baseline.json](../code/recovery-baseline.json)。

### 1.2 已有版本

| 版本 | 主要行为 |
| --- | --- |
| `v0.1.0` | 公开首发版本，包含三级地区选择、电池暂停开关、天气、吐字测试和一键安装器 |
| `v0.1.1` | 安装器布局、离电固定提示和统一版本配置 |
| `v0.2.0` | 增强前台窗口内容读取，保留 Vision 与 OCR 两条输入路线 |
| `v0.3.0` | 完整重构动画资源、动作池、节日/季节/饭点规则和现代设置界面 |
| `v0.3.1` | Runner 恢复、安装目录写入权限、Windows 注册、独立卸载器和天气缓存 |
| `v0.4.0` | 鱼娘语气重建、六档说话间隔、50%–200%缩放、动画交互完善、欢迎词/告别词和设置窗口吐字测试修复 |

项目版本的唯一默认值位于根目录 `Version.props`；代码、应用 DLL、安装器和构建脚本均读取它。当前本地开发版本为 0.4.1，Windows 文件 / 程序集版本为 0.4.1.0；当前公开版本仍为 0.4.0。旧内部编号不作为项目版本或标签。
发布目录、程序集旧上游版本字段、Git 标签和源码提交是不同概念，不能单靠 exe 文件属性判断 Git 版版本。
公开仓库地址为 `https://github.com/Bai-Cute/big-blue-fish-desktop-pet`；本文件是公开接手入口，未公开的本地 handoff 文件不属于产品构建内容。

### 1.3 产品定位

这是 Windows 上透明、置顶的鲸鱼娘桌面宠物，使用本地模型主动说短句，也可主动播报所选地点天气。
“播报”“吐字”在当前产品中都指**气泡里的文字呈现**。当前没有语音合成、音频播放、麦克风和输入聊天窗口。
角色称用户为“主人”，风格亲昵、活泼，倾向简短口语，不催用户回答。
没有多轮对话历史功能；每次生成只有本轮 system / user 两条消息。

维护范围是定制 companion 入口。保留在源码中的上游养成游戏、Steam、多人、创意工坊等代码不等于这些功能已经成为本产品入口或经过验证。
不要通过打开上游入口来“补全”用户未要求的功能。

## 2. 用户明确要求与不可退化行为

| ID | 要求 | 实现对应 |
| --- | --- | --- |
| R-EDGE | 角色能贴到桌面边沿，输出文本框仍在正确位置，不出屏幕 | 可见轮廓贴边 + 独立气泡窗口 + 工作区限制 |
| R-SOURCE | 拥有可读、可更新、可维护的源码工程 | C# / XAML 源码、资源、依赖锁、构建和验证脚本 |
| R-REGION | 天气城市不再写死安徽合肥，使用省 / 市 / 县区三级下拉框 | 三级联动、县区代码保存、按中心点查询 |
| R-POWER | 拔电时不运行并卸载模型是可开关项 | 默认开启 `StopModelOnBattery`，关闭后电池允许运行 |
| R-MENU | 右键先弹菜单，点击设置进入设置，点击天气让模型自然语言播报 | ContextMenu，非右键直接打开设置 |
| R-GIT | 新副本有 code、release、README，适合准备上传 GitHub | 本仓库结构、忽略规则、发布脚本和许可 |
| R-TYPE | 修复先显一小段、隔约半分钟才显完整天气的异常 | 全文测量、文本变化订阅、基于经过时间的逐字显示 |
| R-TEST | 右键加“吐字测试”，用正常吐字方式说一段话 | 调用真实 `Generate(false,false)` 后走统一显示路径 |
| R-TEST-MODEL | 测试不能用固定文本，必须等待模型，GPU 上是否正常也要测 | 使用同一推理进程与 GPU / CPU 设置，不伪造成功 |
| R-SPEC | Git 中包含独立、极详细、供接手 agent 阅读的功能 spec | 本文件和本地交接文档 |

“吐字测试”不是只测 UI 定时器。固定文字可以用于专门的布局回归测试，但不能替代用户菜单中的测试。
GPU 模式提示只表达当前配置；要证明实际走 GPU，还需核对进程参数、Vulkan 模块和真实生成结果，不能仅看复选框。
天气必须由公开天气数据供给事实，再由本地模型组织中文，不能把固定天气话术作为成功结果。
开关自动公开信息不应阻止用户主动点击天气。
地区坐标缺失时要明确失败，不能静默换回合肥或邻县。

## 3. 架构、源码地图与资源

### 3.1 仓库结构

```text
AGENTS.md                       agent 入口与维护约定
docs/AGENT_FUNCTIONAL_SPEC.md    本规格
docs/LOCAL_GIT_HANDOFF.md        本机 Git 访问和接手方法
README.md                       面向使用者的简介
code/蓝色大肥鱼.sln              应用解决方案
code/VPet-Simulator.Windows/     WPF 主程序、定制 companion、图标和素材
code/VPet-Simulator.Core/        动画、图形、上游核心
code/VPet-Simulator.Windows.Interface/  上游接口和数据类型
code/tests/RecoveryVerification/       实窗 / 行为验证程序
code/tools/                     SDK 选择、地区导入
code/build.ps1、run.ps1、test.ps1
code/licenses/、THIRD-PARTY.md   第三方许可及来源
code/provenance.json             安装程序集 / 原始素材来源校验
code/recovery-baseline.json      恢复版本验证记录
code/verification.json           当前维护版本验证记录
tools/Build-Release.ps1           自包含发布、运行时、清单和 ZIP
tools/Setup-Model.ps1             配置指定 GGUF
release/README.md                发布使用说明（Git 跟踪）
release/BigBlueFish-Setup-x64.exe 本机安装器（Git 忽略）
```

### 3.2 定制源码职责

以下文件均位于 `code/VPet-Simulator.Windows/Companion/`：

| 文件 | 职责 | 重要入口 |
| --- | --- | --- |
| `CompanionWindow.cs` | 主窗、初始化、拖动、动画、主定时器、设置、供电、托盘、退出 | `InitializeCompanion`、`CompanionTick`、`OpenCompanionSettings`、`ShutdownCompanion` |
| `CompanionMenus.cs` | 右键菜单、手动天气、真实模型吐字测试、三级选择 | `BuildCompanionContextMenu`、`SpeakCompanionWeather`、`SpeakCompanionTest`、`AddCompanionRegionSelectors` |
| `CompanionSpeech.cs` | 全文保存、Unicode 边界、逐字计时、替换与取消 | `Show`、`Tick`、`FullText`、`Cancel` |
| `CompanionEdgeLayout.cs` | 可见轮廓、工作区、贴边、独立气泡定位和测量 | `PetBounds`、`WorkArea`、`Clamp`、`Refresh`、内部 `Follow.Update` |
| `CompanionPreferences.cs` | 默认值、范围校验、JSON 原子替换 | `Read`、`Save`、`BubbleDurations` |
| `CompanionBrain.cs` | 模型子进程、HTTP 推理、提示词、天气 / 新闻编排 | `WarmUp`、`Generate`、`GenerateWeather`、`Clean`、`Stop` |
| `CompanionWeather.cs` | 天气 URL、WMO 映射、缓存、事实资料 | `ForecastUrl`、`Context`、`Description` |
| `CompanionRegions.cs` | 内嵌区划数据、层级查找、默认县区、全名 | `All`、`Children`、`District`、`Parent`、`FullName` |
| `CompanionNewsGate.cs` | 新闻标题筛选、静默期、普通说话次数、每日预约 | `IsClearTitle`、`RecordOrdinarySpeech`、`TryReserve` |
| `CompanionNative.cs` | Win32 前台、窗口元数据与 UI Automation 线索、闲置、供电、窗口样式、全屏识别 | `OnAC`、`IdleSeconds`、`ForegroundContext`、`IsFullscreen` |
| `CompanionJob.cs` | Windows Job，宿主退出时清理模型进程 | `Attach`、`Dispose` |

另外必须注意：

- `App.xaml.cs`：把当前目录设为应用程序集所在目录；companion 启动分支和异常记录。
- `MainWindow.xaml.cs`：构造函数选择 companion 分支，上游常规初始化被绕开。
- `code/VPet-Simulator.Core/Graph/PNGAnimation.cs`：PNG 帧、缓存、渲染及 `BlueWhaleTint`。
- `code/VPet-Simulator.Windows/assets/pet/`：23 个动作目录，1006 个 PNG 帧。
- `code/VPet-Simulator.Windows/assets/regions/`：内嵌区划 JSON、来源信息。

### 3.3 调用关系

```text
App / MainWindow companion 分支
  → InitializeCompanion → preferences / brain / windows / menus / timer
  → Loaded → LoadCompanionAnimations
  → CompanionTick → WarmUp / 自动动作 / 自动 Generate
右键、托盘
  → SpeakCompanionWeather → 天气 Context → 本地 Complete
  → SpeakCompanionTest → Generate(false,false) → 本地 Complete
模型非空结果
  → ShowCompanionBubble → CompanionSpeech.Show(animate=true)
  → 全文布局 CompanionEdgeLayout.Refresh
  → 逐字 Tick → 完成 → ResetBubbleDeadline
系统提示
  → ShowCompanionNotice → 同一 Speech.Show(animate=false)
```

## 4. 启动、窗口、单实例和退出

### 4.1 启动

`CompanionBuild.Enabled` 固定为 `true`。此开关不是用户设置。
工作目录是 exe / 程序集目录，所以 `preferences.json`、`runtime/`、`models/` 和日志均相对此目录。
启动顺序主要为：

1. 获取命名 Mutex `Local\BlueWhaleCompanion`；不是第一个实例则应用退出。
2. 打开并持有 `companion.lock`，读写权限、共享读取；这不是可移机的锁服务。
3. 读取设置，构造 brain，并设置 GPU、电池策略和地区。
4. 初始化透明主窗、宠物容器、气泡、吐字器和边沿布局。
5. 应用缩放和保存坐标，无坐标时初始放主工作区右下附近，然后 clamp。
6. 建立右键 / 托盘、鼠标处理、电源与显示变化订阅。
7. 主定时器 40 ms；首次自动说话计划为启动后 45 秒，首次自动动作计划为 12 秒后。
8. Loaded 中加载动画；成功后 `companionReady=true`，写 `ready.status=ready`。

Mutex 名称跨上述三种版本相同，同一 Windows 会话不能同时运行它们。
`ready.status` 可能来自旧运行，不能只见文件就判定新进程已就绪，应结合进程路径、时间与实际窗体。
当前会尝试预热模型，不是必然等到第一次说话才启动；状态初始字符串“等待说话时启动”不代表完整启动策略。

### 4.2 主窗行为

主窗标题“蓝色大肥鱼 · VPet”，透明、无边框、不可调整边框大小、置顶，不显示任务栏按钮。
`ShowActivated=false`；原生扩展样式包含 `WS_EX_NOACTIVATE (0x08000000)` 与 `WS_EX_TOOLWINDOW (0x80)`。
`WM_MOUSEACTIVATE` 返回 `MA_NOACTIVATE (3)`，保持鼠标事件可用，避免点击宠物夺走前台应用焦点。
这是普通桌面窗口的行为，不是桌面壁纸引擎，也不承诺在系统锁屏、安全桌面或独占全屏里显示。
设置窗口可正常激活并显示任务栏按钮，这与角色不抢焦点是两个窗口的不同要求。

### 4.3 退出和异常

托盘 / 设置的“关闭桌宠”最终关闭主窗口并执行 `ShutdownCompanion`。
清理包含：关闭菜单、停止主计时器和吐字器、取消推理 / 关闭子进程、保存设置、关闭设置窗口、取消系统事件订阅、销毁托盘和动画对象、释放文件锁 / Mutex，然后应用退出。
`CompanionJob` 设置 kill-on-job-close，作为宿主异常退出时的子进程清理保障；附加 Job 失败则终止新启动的 runner 并报告错误。
普通 `StopRunner` 尝试 `Kill(true)` 终止模型进程树，然后释放 Process 对象。

Release 的 UI 未处理异常和进程级未处理异常写 `companion-errors.log`，带时间与异常堆栈；致命异常退出码 1。
companion 分支不走上游远端异常报告逻辑。Debug 的处理方式不同，不能依赖 Release 捕获来掩盖开发异常。
动画加载失败会写日志并显示“动画未能加载，请检查文件是否完整。”；此分支目前直接设置 TextBlock，不经过统一全文对象，见已知限制。

## 5. 动画、点击、拖动和自动动作

### 5.1 动作资源

目录名去掉 `_0` 成为动作名；PNG 文件按文件名第一个 `_` 前的整数排序。
`idle` 注册为 Default，其余 Common；加载经过 GraphCore 与 VPet Main。
角色原始参考图形尺寸 220，实际显示尺寸由设置缩放决定。
蓝色渲染在 PNGAnimation 中启用，对颜色作轻量修正，保留 alpha 和线条；原始 PNG 不被覆盖。
现有动作名为：

```text
angry blush cry dance drag eat happy idle music sit
sit_eat sit_happy sit_sleep sit_stretch sit_think sit_wave
sleep stretch surprise swim think wait wave
```

所有动作存在不等于都被自动随机选中。动画时长和帧解析以 PNGAnimation 源码及素材文件名为准。
`PlayCompanion` 播单次动作，完成回到 `DisplayNomal`；记录当前动作与 `actionUntil`，但主 Tick 目前不以 `actionUntil` 阻止后续动作。

### 5.2 点击和拖动

左键落在气泡外启动鼠标捕获，保存物理鼠标坐标和窗体起点，取消当前移动目标。
鼠标 delta 以当前 `DpiScaleX/Y` 换为 DIP。
`abs(dx)+abs(dy)>4 DIP` 判为实际拖动，切到 `drag` 动画。
拖动过程中更新窗体 Left / Top；松开后 clamp、更新 anchor、保存设置。
松开后，拖动则 `happy`，单击则 `blush`，下一次自动动作推迟到 20 秒后。
丢失鼠标捕获只清除 dragging，不能假定执行了完整松开保存流程。
关闭气泡按钮不能触发宠物拖动。

### 5.3 自动动作和轻移动

首次 12 秒，之后随机每 18–37 秒选动作；设置窗口打开时不启动新自动动作。
插电候选：`wave / stretch / happy / sit / sit_stretch / eat / music / swim / idle`。
电池候选：`idle / sit / sleep / stretch`。
即使关闭电池暂停，电池下仍采用较安静的动画候选，不因此改成插电动作池。
选 swim，或电池下 1/5 概率，会取 anchor.X 周围 -45 至 +45 DIP 的目标；先 clamp 目标再恢复当前位置，逐帧移动过去。
最大速度 16 DIP/s；每 Tick 的 dt 上限 0.15 秒；剩余横向差小于 1 DIP 时结束并恢复 PetGrid 变换。
这是横向短距移动，不是自由巡游、碰撞模拟或持续在屏幕边缘行走。
用户隐藏、全屏隐藏、未加载完成、正在拖动时，主 Tick 不执行后续动作和自动说话。

## 6. 尺寸、贴边、多显示器和 DPI

### 6.1 坐标契约

设缩放为 `scale`，角色正方形大小 `S=280*scale` DIP。
`scale` 默认 1，范围 0.65–1.6，所以 S 为 182–448 DIP。
主窗 `Width=max(320,S+20)`，`Height=S+115`，VPet `ZoomLevel=S/500`。
角色居中并在主窗底部，透明窗口外框不等于角色可见轮廓。

当前可见轮廓近似矩形：

```text
X = (window.Width-S)/2 + S*37/220
Y = window.Height-S + S*15/220
W = S*146/220
H = S*173/220
```

要求让该矩形贴边，允许透明窗体部分超出工作区。这是原贴边需求的关键，不能改成把整个透明窗口限制在桌面内。
此轮廓为针对当前素材的固定近似；更换角色或大幅不同动作外形时必须重新检查，不能认定自动 alpha 轮廓分析已经实现。

### 6.2 工作区和 clamp

取角色可见区域中心，通过 `PointToScreen` 找对应显示器，使用该显示器 `WorkingArea`，再 `PointFromScreen` 转回 WPF DIP。
未建立 PresentationSource 时回退主显示器并按 DPI 换算。
工作区通常排除任务栏，不能用全屏幕 Bounds 替代它来定位气泡。
Left 限制在 `[area.Left-bounds.Left, area.Right-bounds.Right]`，Top 同理。
距离任一轮廓边与工作区边小于 14 DIP 时吸附。
非有限位置会使用下限；当计算上限小于下限时，至少不让 Clamp 的上下限反向。

松开拖动、缩放、恢复显示、显示设置变化、DPI 变化等路径会 clamp。
显示设置变化后更新 anchor；窗口位置 / 大小变化通知气泡跟随更新。
混合 DPI、负坐标显示器、热插拔是应验收场景，但已有 24 组四角测试不代表穷举过所有硬件拓扑。
设置“回到右下角”从 `SystemParameters.WorkArea` 主工作区位置计算，然后 clamp；不是明确保留当前副屏的按钮。

## 7. 独立气泡、测量和防裁切

### 7.1 窗口与视觉

气泡 Border 从主窗 Grid 移出，放入独立 owned Window，标题“蓝色大肥鱼 · 对话”。
气泡窗透明、无边框、置顶、不可调边框、不激活、不显示任务栏，Owner 为宠物主窗。
鼠标激活处理同样 `MA_NOACTIVATE`。宠物不显示或 bubble Collapsed 时 popup 隐藏。
气泡不是 MainWindow 高度中的固定文本区域，不能重构回随主窗裁切的子元素。

| 样式 | 当前值 |
| --- | --- |
| 字体 | Microsoft YaHei UI，15 DIP，自动换行 |
| 字色 | RGB 49,64,94 |
| 背景 | RGB 244,249,255 |
| 描边 | RGB 160,188,225，1.5 DIP |
| 圆角 | 18 DIP |
| Padding | 左15、上10、右10、下10 |
| 文本右 margin | 26 DIP，为关闭按钮留位 |
| 关闭按钮 | 22×22，Segoe MDL2 Assets `E711`，自动化名称“关闭气泡” |
| 最大宽 | min(300 DIP，工作区宽-16 DIP)，下限1 |

### 7.2 全文预留布局

所有模型结果在首字显示时就把全文放入 `CompanionSpeech.FullText`。
测量读取 `CompanionBubbleLayoutText`，正常为 FullText，而不是当前仅显示的前缀。
测量 TextBlock 复制实际字体、大小、字重、换行与 margin。
先按最大内容宽度测量，得候选气泡宽；再按实际候选内容宽度重测一次，避免候选宽度变化多包一行造成裁切。
候选高度为文本测量高度至少 22 DIP，再加 padding / border，最多工作区高-16 DIP。
因此短系统提示替换为长天气时，第一字出现前就留足完整天气的窗口尺寸。
逐字增加只改变显示文字，不能让前几行可见、后几行被旧 popup 高度隐藏。

水平尽量围绕宠物轮廓中心，并限制在工作区左右 8 DIP 以内。
竖直优先角色轮廓上方 10 DIP；上面放不下则下方 10 DIP；最后限制到工作区上下各 8 DIP。
极小工作区无法同时容纳所有边距与内容时是降级条件，不能声称数学上始终完整呈现任意长度内容。
当前没有长文本滚动条，正常模型输出靠短句限制和 Clean 控制长度。

### 7.3 跟随刷新、关闭和回归原因

跟随对象用 ConditionalWeakTable 关联宠物窗，监听 Loaded、LocationChanged、SizeChanged、IsVisibleChanged、DpiChanged、Closed。
同时监听气泡 Visibility、SizeChanged 与 TextBlock.Text 依赖属性。
变化时立即 Update，再按 DispatcherPriority.Loaded 队列合并刷新，避免刚 Show 时布局尚未稳定。
宠物关闭时移除气泡属性监听并关闭 popup。
关闭按钮取消当前吐字并折叠 bubble，不关闭桌宠、不禁止下一句话、不直接取消模型生成。

历史快照 0fb2621 的异常不是可以当作正常“思考停顿”的设计：旧 popup 未正确跟随新文本重新测量，长文字可能被短提示高度裁切，形成先见一段、很久后才见全文的观感。
历史快照 b128cb0 增加全文测量、实际宽度重测和 Text 订阅，并统一逐字显示路径。
维护时应分别量“等待模型时间”“吐字完成时间”“文字是否被窗口裁切”，不能只看最后完整字符串。

## 8. 逐字显示与驻留

### 8.1 统一显示契约

普通主动短句、主动天气、吐字测试的非空模型结果均调用 `ShowCompanionBubble → speech.Show(text,true)`。
加载 / 等待 / 失败等系统提示调用 `ShowCompanionNotice → speech.Show(text,false)`，即时整段显示，不把它当模型生成成果。
普通气泡显示前把 `bubbleUntil=DateTime.MaxValue`，正常吐字完成回调才启动驻留倒计时。
新消息替换旧消息时，先停止旧 timer，再保存新 FullText、新 Unicode 边界、新已显示数量。
没有把 token 流直接显示出来的模型 streaming 功能；推理 HTTP `stream=false`，先得到完整响应，再做 UI 吐字。

### 8.2 时间和 Unicode

目标速度 `CharactersPerSecond=28` 个文本元素 / 秒。
用 `StringInfo.ParseCombiningCharacters` 分段，避免把代理对、组合字符等按单个 UTF-16 code unit 切碎。
动画首个文本元素立即显示；DispatcherPriority.Render timer 每 25 ms 计算一次。
以 Stopwatch 单调经过时间计算 `count=min(total,1+floor(elapsedSeconds*28))`。
UI 短暂延迟后按实际经过时间补齐，不把“每一次 Tick 吐一个字”作为速度来源。
预计 N 个文本元素显示时间约 `(N-1)/28` 秒，再加 UI 调度延迟；不是强实时保证。
模型加载 / 推理时间不包含在该吐字时间内，首次点击可能先等待较久。
Cancel 停 timer 和 Stopwatch，不触发完成回调；bubble 不可见时 Tick 也取消。

### 8.3 驻留选项

| BubbleDurationIndex | 秒数 | 界面 |
| --- | --- | --- |
| 0 | 5 | 5秒 |
| 1 | 10 | 10秒 |
| 2 | 15 | 15秒 |
| 3 | 30 | 30秒（默认） |
| 4 | 60 | 1分钟 |
| 5 | 120 | 2分钟 |
| 6 | 180 | 3分钟 |
| 7 | 0 特殊值 | 直至下一句 |

0 秒映射 DateTime.MaxValue，不是立即消失。
下一句话、手动关闭、隐藏、供电暂停、退出仍可移除“直至下一句”的气泡。
设置里修改驻留时，当前可见气泡会重新计算 deadline。当前即使正在吐字也会重算，这是正常“完成后驻留”规则的一个尚未单独保护的例外，见第 21 节。

## 9. 右键、托盘、隐藏与全屏

### 9.1 菜单

角色右键在鼠标位置弹 ContextMenu，顺序精确为：**设置、天气、吐字测试**。
点击菜单后先关闭菜单再执行功能。不要恢复成右键直接进入设置的旧行为。
托盘菜单顺序为：**显示桌宠、设置、天气、吐字测试、关闭桌宠**。
托盘标题“蓝色大肥鱼”，图标读取 `vpeticon.ico`；双击托盘恢复显示。
托盘没有单独“隐藏”项目，隐藏按钮位于设置窗口。

### 9.2 用户隐藏和恢复

设置“隐藏到任务栏通知区”设置 hiddenByUser，取消当前吐字。
供电允许时取消当前模型请求，保留驻留进程；供电不允许时 Stop 卸载模型。
折叠气泡、隐藏角色、关闭设置；托盘保持存在。
“显示桌宠”清除用户隐藏，Show、clamp，并把下一次自动说话推迟一个 SpeechMinMinutes。
手动天气 / 测试在用户隐藏时先恢复宠物，但它们仍受模型设置、供电与全屏结果显示检查约束。

### 9.3 全屏规则

取真实前台窗，忽略本程序和 explorer；窗口矩形覆盖所在显示器完整 Bounds 则视为全屏。
检测到全屏且未被用户隐藏时设置 fullscreenHidden、Hide，并 CancelGeneration。
全屏结束后清除标记；用户没有隐藏才重新显示并 clamp。
全屏本身不要求卸载驻留模型。电源预热逻辑在主 Tick 的可见性早退之前，隐藏时也可能保留 / 预热模型。
这是矩形启发式，不是对所有游戏模式和覆盖层的严格识别。
用户隐藏与全屏隐藏是不同标记，不能在退出全屏时无条件覆盖用户的隐藏选择。

## 10. 设置窗口和持久化

### 10.1 界面

设置标题“大肥鱼设置”，宽 460 DIP，按内容高度，最大高度 `max(400,主工作区高-60)`，不可调整边框，居中、可激活、显示任务栏按钮。
内部 ScrollViewer 垂直自动滚动，字体 Microsoft YaHei UI 14 DIP。
只有一个设置窗口，重复打开 Activate 现有窗，不重复创建。
设置打开期间，自动说话和新自动动作暂停；手动菜单请求不由这一条件统一禁用。
主窗轻移动目标在开设置时清空。

控件从上到下：

1. 蓝色大肥鱼标题。
2. 启用本地大模型。
3. 拔电时暂停并卸载模型。
4. 使用显卡加速（关闭则使用 CPU）。
5. 开机自动启动。
6. 自动获取天气和科技新闻。
7. 感知当前应用（仅在本机处理）。
8. 天气地点三个联动选择器及来源提示。
9. 缩放滑块和百分比。
10. 说话间隔下拉框。
11. 气泡驻留滑块和标签。
12. 供电 / 模型状态。
13. 隐藏到任务栏通知区、回到右下角、关闭桌宠。
14. 基于 VPet / 社区素材 / 无聊天记录的提示。

### 10.2 设置字段、默认值和副作用

| JSON 字段 | 默认 | 范围 / 意义 | 更改影响 |
| --- | --- | --- | --- |
| ModelEnabled | true | 模型总开关 | 关则 Stop；开则允许立即预热、下一自动计划约20秒后 |
| GpuEnabled | true | true GPU，false CPU | Stop 旧进程，改 UseGpu，下一 Tick 可预热 |
| PublicInfo | true | 自动天气 / 科技新闻 | 后续普通自动生成使用；不禁用主动天气 |
| ForegroundEnabled | true | 本机前台应用感知 | 后续普通自动生成使用 |
| StopModelOnBattery | true | 电池暂停并卸载 | 立即重算供电策略；不允许则 Stop 并折叠气泡 |
| WeatherRegionCode | 340104 | 合法 Level2 县区代码 | 更新 prefs / brain，地点变化取消活动请求 |
| Scale | 1 | 0.65–1.6 | 改角色和主窗、clamp、保存；UI 步长0.05 |
| SpeechMinMinutes | 8 | Read clamp 3–60 | UI 为3、8、15、30；更改下一说话计划并保存 |
| BubbleDurationIndex | 3 | 0–7 | 改标签 / tooltip，可见气泡重设 deadline |
| Left | null | WPF 主窗横坐标 | 缺省使用主工作区右下附近 |
| Top | null | WPF 主窗纵坐标 | 同上 |

默认地点是安徽省 / 合肥市 / 蜀山区，而不是继续固定全产品只能查合肥。
安装版或本机试用 prefs 不是发行默认值；不要把用户当前选中云霄县等值写进默认配置。
说话间隔 UI 显示 3–10、8–15、15–22、30–37 分钟。
JSON 中自定义 3–60 合法值可用于计划，但不属于 UI 四档时打开设置会显示 8 档；只有用户更改选择才写成 UI 档位。
保存为即时生效，不依赖“确定 / 取消”按钮；关闭设置也会保存。
状态每秒刷新供电状态与 brain.Status。

### 10.3 JSON 文件与恢复

先读 `preferences.json`，无效则尝试 `preferences.json.bak`，仍失败使用默认值。
JSON 使用 C# 字段名，未提供字段保留属性初始化默认值。
范围按上表 clamp；非法 / 非县区代码经 District 回退 340104。
Save 先写 `preferences.json.tmp`，主文件存在时 File.Replace 到主文件并保存 `.bak`，不存在时 Move。
这属于应用现有的原子设置保存机制，不是要求接手 agent 为任务额外复制一份备份。
SaveCompanion 把当前 Left / Top 写回，并吞掉保存异常。因此“设置已点选”不能单独证明磁盘保存成功，需重开或检查文件。
运行目录必须可写。只读目录或权限不足可能导致锁 / 就绪文件写入失败或设置保存失败。
不同发布目录各自持有自己的 prefs，升级不是自动跨目录迁移；迁移应保留用户选择并核对新字段默认值。

### 10.4 开机启动

不存于上述 JSON，读 HKCU `Software\Microsoft\Windows\CurrentVersion\Run` 的 `BlueWhaleCompanion` 值是否存在。
开启写带引号的当前 `Environment.ProcessPath`，关闭删除该值，不要求管理员权限。
该值跨安装 / 复原 / Git版共享；在某一版切换可能改掉另一版开机启动位置。
移动或删除 exe 后旧注册表路径可能失效；当前没有自动修复或版本迁移逻辑。

## 11. 省、市、县区三级选择与数据

### 11.1 UI 与联动

三个 ComboBox 自动化名 / Name：

| Name | 标签 |
| --- | --- |
| WeatherProvince | 省 / 自治区 / 直辖市 |
| WeatherCity | 市 / 州 |
| WeatherDistrict | 县 / 区 |

显示 `Name`，值为 `Code`。初始化从 prefs 县区推导市和省，不依靠三项独立设置避免不一致。
省列表是 ParentCode=`0` 的 children。
改变省：重载市列表并选第一项，再重载该市县区列表并选第一项，最终保存。
改变市：重载县区并选第一项，最终保存。
改变县区：直接保存新县区。
`changing` 标记防止联动过程递归保存过渡组合。
无有效县区选中时不写无效值。地区改变调用 brain.CancelGeneration；手动天气持有旧地区快照，显示前还检查地区相同。

### 11.2 数据契约

`CompanionRegion` 字段：Code string、ParentCode string、Level int、Name string、Latitude double?、Longitude double?。
Level 0 省，1 市，2 县区；代码保留为字符串，不能假定所有层都是六位数字。
当前内嵌 34 省级、392 市级、3209 县区级，总3635项。
代码 JSON 作为程序集资源加载，缺资源或空数据会抛异常，不从网络临时拉取替代。
District 只返回合法 Level2；不存在 / null / 错层级回退默认县区。
FullName 将省市县区 Name 去重后以 ` · ` 连接，处理直辖等重复显示名称。

数据来源 AreaCity `2025.251231.260403`，提交 `c6c6e35bea3066d674efe2cded189dc57a86e7d8`。
导入用 `ok_data_level3.csv` 和 `ok_geo.csv`；海外占位91前缀被排除；只保留层级和中心点，不分发多边形。
大陆中心 GCJ-02 近似反算 WGS84，天气仅区县中心附近估算，不是 GPS 实测。
香港 / 澳门层级按来源提供的粒度保留；不能把占位层级解释为完整所有现实行政小区。
部分台湾地区源坐标 EMPTY，JSON 为 null；可以选择，但查询应明确提示缺坐标。
区划固定快照，不保证行政变更实时同步。升级需同步来源版本、输入哈希、许可和选择兼容策略。

导入命令（在 code 目录）：

```powershell
python ./tools/import-regions.py "ok_data_level3.csv" "ok_geo.csv"
```

## 12. 主动天气：从点击到结果

### 12.1 前置条件与状态

v0.1.1：笔记本离电且 StopModelOnBattery=true 时，手动天气与吐字测试统一显示固定提示“主人，当前设置为笔记本离电暂停模型，可以在设置里调节哦。”提示使用正常逐字显示路径，但来源是预设系统提示，不调用模型或天气网络接口。全文完成后按当前气泡驻留设置计时。启用模型总开关的检查仍优先，关闭模型时沿用启用模型提示。

若 weatherRequestPending 或 speechTestPending 或程序关闭，忽略新天气点击。
用户隐藏则先恢复。模型关闭时提示先启用模型；供电策略禁止时提示插电或关闭拔电暂停选项。
符合条件后：weatherRequestPending=true，递增 speechRequestVersion，记录当前县区代码。
立刻显示含完整地点的“正在查看…的天气”系统提示。
若 brain.Busy，最多等待100秒，每100ms轮询，且尊重关闭 / 供电变化。
等完检查版本、关闭、模型开关、供电和地区是否仍相同；不满足丢弃请求。
模型仍忙提示“模型暂时还在忙，请稍后再试一下”。
实际调用 GenerateWeather；最终非空模型结果走正常吐字并 wave，空结果显示 brain.Status。
显示前还检查用户隐藏与全屏隐藏，不能把旧结果弹到当前不应显示的场景。
finally 清 weatherRequestPending，下一自动计划延后 SpeechMinMinutes。
用户连续点击并非无限排队；只有当前请求，重复点击不追加后台任务。

### 12.2 天气请求

使用 Open-Meteo GET `/v1/forecast`，坐标采用所选县区中心：

```text
latitude / longitude                 InvariantCulture
current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m
daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max
forecast_days=1
timezone=Asia/Shanghai
```

公共 HttpClient 请求超时12秒；后台刷新独立于当前生成 token，应用关闭取消刷新，手动请求取消仅取消等待。
缺坐标在联网前抛 IOException：“所选地区暂缺天气坐标，请选择其他区县。”
检查 HTTP 成功、按约定解析 current / daily 第一个日值；值缺失、null 或结构变动可能失败，不能拼造一个成功结果。
事实 context 包含完整地点、中心估算声明、天气数据北京时间、中文天气、气温、体感、湿度、风速、今日最低 / 最高和最高降水概率。
数值保留最多一位小数，温度℃、湿度%、风速公里/小时。
降水概率不是已经下雨，不能把100%概率转述成当前已下雨或精确未来开始时间。

### 12.3 缓存与天气代码

0.3.1：天气以县区代码为键，成功资料原子写入安装目录 `cache/weather.json`，重启加载。启用 PublicInfo 时普通 UI Timer 调 RefreshIfDue，启动缺缓存立即刷新，之后每小时尝试一次；地区变更按新地区独立计时，同地区并发请求合并。网络失败、解析失败保留原成功数据，下次按小时重试。
读取时下载时间和天气观测时间都不得在未来，也都不得超过六小时，超过有效期不向模型提供。资料标注观测北京时间及高低温预报所属日期，跨午夜不将旧日预报冒充新日预报。
普通吐字只读内存缓存，不等待联网。手动天气忽略 PublicInfo 开关，先用有效缓存并按需发起后台小时刷新；缺有效缓存时等待首次或已经进行中的刷新，失败显示无资料状态。同地区旧缓存不可供其他地区使用。
升级保留 cache 目录；卸载勾选保留模型和设置则保留天气缓存，默认完整卸载清除缓存。回归入口：RunnerVerification --weather-cache，涵盖小时/六小时边界、断网、重启、并发、地区隔离、损坏和中断写入；--live 核验真实 Open-Meteo 数据下载及离线重读。

| WMO code | 中文 |
| --- | --- |
| 0 / 1 / 2 / 3 | 晴朗 / 大部晴朗 / 局部多云 / 阴天 |
| 45,48 | 有雾 |
| 51,53,55 / 56,57 | 毛毛雨 / 冻毛毛雨 |
| 61 / 63 / 65 / 66,67 | 小雨 / 中雨 / 大雨 / 冻雨 |
| 71 / 73 / 75 / 77 | 小雪 / 中雪 / 大雪 / 雪粒 |
| 80 / 81 / 82 / 85,86 | 小阵雨 / 阵雨 / 强阵雨 / 阵雪 |
| 95 / 96,99 | 雷雨 / 雷雨伴冰雹 |
| 其他 | 天气状况未提供 |

### 12.4 天气模型提示与失败

GenerateWeather 自身 Busy 排他，整体取消期限100秒；先取天气，再 EnsureRunner，再 Complete。
WeatherPersona 要求称主人、先说地点、当前天气和气温，适当提高低温 / 概率，再给有依据关心；最多三个短句、90中文字，无标题 / 列表 / 思考 / 动作旁白。
只说天气，不掺新闻 / 当前应用，不编实测、温度变化、精确降雨时间或预警。
这是提示词要求，不是逐事实验证器。Clean 只作基础文本清理，模型仍可能表达错误，维护需看真实输出。
temperature .4，max_tokens220，其余请求参数见模型章节。
取消状态“天气播报已取消”；IOException 使用具体消息；其他错误统一“暂时无法获取或播报天气，请稍后重试”。
失败没有固定天气、外部云模型或另一城市作为兜底。
界面等待Busy最多100秒和 GenerateWeather最多100秒是两段，整体可能接近200秒；不能宣称整个点击必在100秒内结束。

## 13. 吐字测试：必须真实生成

### 13.1 行为要求

点击“吐字测试”要验证：当前模型能启动 / 驻留、按当前 GPU 或 CPU 配置完成真实推理，结果经 Clean、全文布局与正常吐字呈现。
不能用随机固定台词池、硬编码一句话、UI 动画演示、过去缓存结果代替推理。
失败必须明确提示；不能在模型没有输出时仍显示成功测试台词。

### 13.2 当前流程

1. speechTestPending 或关闭时忽略；隐藏则恢复。
2. 模型关闭或供电禁止：系统提示要求启用模型并确认供电，不越过开关。
3. 设置 pending，递增 speechRequestVersion，使较早自动 / 天气结果失效。
4. 显示“正在等待本地模型生成测试短句（显卡加速 / CPU）”系统提示，模式取 brain.UseGpu。
5. 若已有预热 / 推理，最多等100秒，每100ms检查 Busy，不立即杀掉它。
6. 等待后检查关闭、模型开关、供电；仍忙则提示稍后再试。
7. 调用同一 brain 的 `Generate(false,false)`，不获取天气 / 新闻，不读取前台标题。
8. 它仍给模型本地日期时间与“用户正在使用电脑”context，并使用普通 Persona / SpeechRequest。
9. 结果版本和显示条件有效：非空 ShowCompanionBubble + wave；空显示“吐字测试未能生成文字：{Status}”。
10. finally 清 pending，并延后下一自动说话 SpeechMinMinutes。

测试可在天气等待 / 推理中发起，新的版本会让旧天气不能覆盖它；天气则在测试 pending 时被忽略。
重复测试不重入、不排队。测试等待与生成同样可能分两段各100秒。
测试用普通生成，所以成功会计入普通短句次数；这是当前新闻门控的副作用，不是新闻测试入口。
菜单显示 GPU 不证明实际 GPU 工作；验收需看到 `-ngl99`、Vulkan模块和实际生成，而 CPU 对应 `-ngl0`。
当前没有专门 GPU 性能评分、温度监控、负载图或全模型正确性检查。

## 14. 自动说话、应用感知和提示词

### 14.1 触发与节奏

主 Tick 中：未到 nextSpeech、Busy、手动天气 / 测试 pending、模型关闭、供电不允许、设置窗口打开则不生成。
上述判断前还有隐藏 / 全屏 / 未 ready / dragging 的早退。
系统闲置须在8–600秒；小于8或大于600把计划推迟30秒重试。
符合时，在发请求前安排下一次 `SpeechMinMinutes + Random.Next(0,8)` 分钟，即最小值加0–7分钟。
自动请求捕获 speechRequestVersion，但不递增。生成后版本仍一致、未关闭 / 隐藏 / 全屏、模型和供电允许、非空、闲置至少5秒才显示。
生成过程中用户恢复高频输入可能让该句被丢弃，不是每个请求一定可见。
手动天气 / 测试不使用闲置条件；最后安排最小间隔，不立即自动补一句。
恢复、供电事件、改频率等会重排 nextSpeech，不能理解为后台精确固定定时任务。

### 14.2 前台感知

GetForegroundWindow / GetWindowThreadProcessId / ProcessName / GetWindowText（缓冲256）得到进程名与标题，并读取窗口类别、根 UI Automation 名称和控件类型。
在控件树的有限深度内保留最多16条非空子控件名称与类型，每条和整体 context 都有长度上限；重复线索会合并。前台是桌宠自身时返回“用户正在查看桌宠”。进程名失败用“未知应用”。
这些资料以结构化字段传给模型：窗口标题与应用名称是活动主线，无障碍名称和控件线索用于确认项目、网页主题、文档或当前界面状态。程序不先把活动压缩成固定的几个类别。
不读取窗口正文、文件内容、网页 DOM、键盘输入或截图；窗口标题可能含文档名或网页标题，UI Automation 线索可能包含当前界面上可访问的控件文字。
关闭 ForegroundEnabled 则只给泛化“用户正在使用电脑”，不能继续偷偷把标题放进请求。
context 和推理在本机处理，普通短句没有云模型 API。

### 14.3 普通 Persona

提示词要求一至两短句，20–65中文字，最多80字，亲昵、俏皮、内容和开头变化，不要求用户回复。
不输出标题、引号、动作旁白、表情符号或思考。
优先使用窗口资料中最明确的应用、项目、文档、网页主题或界面状态，让话术对当前场景有针对性；资料只支持宽泛判断时保持宽泛。模型根据资料明确程度选择表达粒度，不把程序预先分类的结果当作唯一输入。
没有天气资料不能谈凉快 / 降温；只有变化数据才能说降温。示例是口吻，不是本轮事实。
实际提示全文应直接阅读 CompanionBrain 的 Persona / SpeechRequest，不从本段重新手工拼一个近似提示。
自动公开信息开启时，非新闻普通生成有15%概率附天气 context；自动天气取失败会返回空 context，不让整个普通生成因天气网络错误失败。
普通生成温度 .65，max_tokens180。

## 15. 科技新闻门控和标题来源

自动公开信息开启才可能尝试新闻，不存在手动“新闻”菜单。
CompanionNewsGate 在进程启动后先静默2小时，至少累计12次成功非空普通短句，随机 roll<.02，且当天未预约，才允许新闻尝试。
普通短句次数上限12；新闻预约后计数归零，并再设置2小时门槛。
`news-last-day.txt` 只记本地日历日期，不保存对话；先写 `.tmp` 再 File.Move 覆盖，写失败不预约。
读取日期损坏视为无记录；状态是每个运行目录独立，不是全机多发布版本共享数据库。
每日限制是**预约尝试**限制：即使后续联网失败、标题不足或模型返回略过，也可能已消耗当天机会。
不保证每天播一条，更不是每2小时必播一条。

RSS 地址 `https://www.ithome.com/rss/`；最多取10个不同标题。
只保留 pubDate 不在未来且距当前少于2天的条目，再用 IsClearTitle 筛选。
标题长度12–160；排除“某、据传、网传、疑似、曝光、爆料、或将、即将、重磅、震撼、不一样、大幅提升、悬念、问号”等；须包含发布 / 推出 / 开源 / 上线 / 宣布 / 支持 / 更新 / 正式 / 获批 / 发现之一。
标题缓存45分钟，过期时先清空并设下次过期，再联网；因此获取失败后通常也会等缓存期才重试。
选与 lastNewsTitle 不同的首个标题；没有文章正文抓取和历史新闻归档。

NewsPersona 要求先“主人，看到一条科技消息：”，独立交代是谁 / 什么技术 / 发生什么，35–85字，最多两短句。
只能根据标题，不补原因、细节、结论，不把新闻说成用户活动，不混天气 / 时间。
信息不足输出“略过”；生成后含略过或不以规定前缀开头则丢弃，不冒充成功新闻。
新闻温度 .4，max_tokens180；保留前缀和策略是原行为核对的一部分。

## 16. 模型进程、GPU / CPU、HTTP 与清理

### 16.1 文件、加载与健康检查

推理程序相对路径 `runtime/llama-server.exe`，模型 `models/Qwen3.5-4B-heretic-Q4_K_M.gguf`。
缺任一文件抛 IOException“模型或推理程序尚未就绪”，不自动在主程序里下载。
随机端口用 Loopback TcpListener port0 探测后释放；这有小概率探测到启动之间被别的进程占用的竞争，不能视作强端口租约。
子进程只 bind 127.0.0.1，不启动 WebUI。已有活 runner 则 EnsureRunner 直接复用，不先重新探活健康接口。
新启动后接收并消费 stdout / stderr，当前回调丢弃内容，不持久保存推理日志。
附加 Job 成功后轮询 `/health` 最多120次，每次间隔500ms，期间检查取消、供电和子进程退出。
网络调用自身也耗时，所以不是严格正好60秒；外层取消期限才控制该次 WarmUp / Generate。

### 16.2 启动参数

```text
-m <模型绝对路径>
--host 127.0.0.1 --port <随机端口>
-c 2048
-ngl 99（GPU）或0（CPU）
-ub 32（GPU）或512（CPU）
-t 4 --parallel 1
--no-webui --log-disable --jinja --reasoning off
```

GPU 使用 Vulkan 运行时。小 microbatch32 是针对该 Qwen35 / Intel 驱动组合输出问题保留的参数，不能未经实际对比“优化”为512。
CPU 不是自动降级：当前 GPU 启动失败不会透明改成 CPU 成功，用户需明确关闭显卡开关。
更换模型、驱动、运行时后应重新检查可读输出，不以健康接口200认定文本推理正常。

### 16.3 Chat 请求

本地 HttpClient 禁用代理，Timeout100秒。POST `/v1/chat/completions`：

```json
{
  "model": "local",
  "messages": [
    {"role": "system", "content": "本次 Persona"},
    {"role": "user", "content": "本轮事实及请求"}
  ],
  "temperature": 0.65,
  "top_p": 0.9,
  "max_tokens": 180,
  "stream": false,
  "chat_template_kwargs": {"enable_thinking": false}
}
```

新闻 / 天气温度 .4，天气 token220。读取 choices[0].message.content，结构不符按生成异常处理。
没有 OpenAI / DeepSeek 云端凭证、云推理接口、多轮记忆或工具调用。
模型、天气、新闻各自用途分离：外部网络只提供天气资料 / RSS标题，文字由本地 runner 组织。

### 16.4 Busy、预热与状态

WarmUp：Busy / 已有runner / 供电禁止则不做；取消期限90秒；成功状态“本地模型已驻留”，异常 StopRunner 后“模型未就绪，稍后重试”。
主 Tick 模型开启、供电允许、非Busy、无runner且到 nextWarmup 才预热；发起前把 nextWarmup 推迟2分钟。
Generate / GenerateWeather：Busy 或供电禁止返回空；取消期限100秒，finally清Busy、释放CTS，供电不允许则StopRunner。
常见状态包括“正在加载本地模型”“本地生成短句”“正在获取所选地区天气”“正在生成天气播报”“本地模型就绪”“模型已停止”。
Status 为面向设置和提示的文字，不是完备机器状态枚举；判断活进程要看 RunnerPid / HasExited。
Stop 取消 active、终止runner并置停止状态；CancelGeneration 只取消 active，不直接卸载已驻留runner。
模型 / GPU 开关、供电和退出等操作可能在 await 期间到来，必须保留最终显示条件检查。

### 16.5 Clean 契约

移除闭合 `<think>...</think>`（忽略大小写）；仍含未闭合 `<think>` 则输出空。
合并空白、Trim外部空白及中英文双引号。
无任何Unicode字母 / 数字，或同一字符连续9次及以上，则空。
按 Rune 计数，少于100原样保留；否则取前98个Rune，最后一个 `。！？～` 位置至少15则在该处截句，否则加省略号。
这保护基本展示长度和退化输出，不验证天气事实、不保证新闻准确、不强制所有 Persona 的字数 / 句数要求。
Unicode 吐字以文本元素计数，Clean 截断以 Rune 计数，两个单位不同，验收要区分。

## 17. 电池开关和电源事件

### 17.1 策略真值表

| ModelEnabled | 供电 | StopModelOnBattery | 应有模型行为 |
| --- | --- | --- | --- |
| false | 任意 | 任意 | 不允许新预热 / 生成，切关时停止runner |
| true | AC | true或false | 允许预热、生成和驻留 |
| true | 电池 | true | 取消 / 停止runner，隐藏气泡，手动请求提示被暂停 |
| true | 电池 | false | 允许预热、生成和驻留，使用电池动作池 |

brain.CanRunOnCurrentPower=`!StopOnBattery || OnAC`；它不包含 ModelEnabled，总开关由主窗调用侧检查。
OnAC 从 GetSystemPowerStatus AC==1 得到；查询失败 / 未知不作为插电。
“卸载模型”指终止本地模型进程、释放其驻留资源，不删除 GGUF / 运行时文件。
关闭电池暂停不是关闭 GPU，也不自动换CPU。

### 17.2 变化响应

v0.1.1：主窗以 bubbleIsModelReply 区分模型回复和系统提示。离电暂停仍取消推理并卸载模型；供电检查和电源事件只取消吐字、隐藏模型回复，保留系统提示及其吐字计时。系统提示与模型回复统一按全文测量、驻留从逐字完成后开始。手动关闭、隐藏与全屏显示规则沿用原行为。

原生 WM_POWERBROADCAST 调 ApplyCompanionPowerPolicy；SystemEvents.PowerModeChanged 切 UI Dispatcher 执行。
Suspend 先brain.Stop；再应用策略，下一自动说话延后 SpeechMinMinutes。
ApplyPolicy 将 prefs 开关同步到brain；不允许则Stop+collapse，nextWarmup置最早，插回电后可重试。
每40ms主 Tick也兜底检查供电，不允许且Busy或有runner则Stop。
当前没有独立“系统正进入挂起”的锁存状态，Suspend事件后到真正睡眠前可能仍有Tick；不承诺经过所有睡眠 / 休眠硬件组合的实测。
历史验证用测试供电覆盖，不是物理拔电实测，交接必须诚实标明。

## 18. 请求并发、取消与旧结果防覆盖

UI 使用 WPF Dispatcher，异步 Tick / 菜单 await 期间可以处理新的 UI / 电源事件。
brain.Busy 限制同一brain的一次预热 / 生成；不是外部可共享的任务队列。
weatherRequestPending / speechTestPending 用于UI请求互斥与优先权，不能省掉后只靠Busy，否则等待期间容易重入。
speechRequestVersion 是结果代次：手动天气或测试递增，自动只捕获，结果显示比较。
天气还持有regionCode快照，地区变化取消 active 且旧地区结果不可显示。

| 事件 | 当前处理 | 不应发生 |
| --- | --- | --- |
| 自动生成时点天气 | 等自动 / 预热释放Busy，自动旧结果代次失效 | 自动结果盖过主动等待提示 |
| 天气未完点吐字测试 | 测试递增代次，等Busy释放，天气结果丢弃 | 旧天气覆盖新测试结果 |
| 测试未完点天气 | 天气请求直接忽略 | 无界排队或两个runner |
| 重复点同一种手动项 | pending阻止重复 | 多份耗时推理重入 |
| 切地区 | 保存地区、取消active、天气结果比较地区 | 新地点下播旧县天气 |
| 关闭模型 / 供电禁止 | Stop，最终显示条件拒绝旧结果 | 关模型后又冒生成结果 |
| 切GPU | Stop旧runner，切UseGpu，允许重新预热 | 旧CPU/GPU进程继续冒充新模式 |
| 用户隐藏 / 全屏 | 取消请求或阻止结果显示 | 用户正在全屏时出现旧气泡 |
| 关闭气泡 | Cancel吐字、collapse | 直接退出桌宠或锁死后续说话 |
| 主窗退出 | 设置closed、dispose各对象和进程 | await返回后重新显示窗口 |

现有最终检查不等于所有异常路径都经过形式验证；修改并发逻辑要用真实交错事件复测。
本产品不要求后台多模型并行、请求优先级调度器或持久化任务队列，避免为简单UI增加未要求系统。

## 19. 构建、运行依赖和发布契约

### 19.1 开发环境

Windows x64，.NET10 SDK；global.json 基线10.0.100、latestFeature roll-forward，恢复记录实际SDK10.0.401。
三个应用项目 TFM=`net10.0-windows7.0`，发布RID=`win-x64`。
TFM名字带windows7.0不代表.NET10应用支持Windows7；当前用户发行说明为Windows10/11x64。
SDK选择：显式-SdkPath、BLUE_WHALE_DOTNET、PATH中的dotnet、当前工作区本机fallback。
本机fallback为外部工作区 `work/dotnet-sdk/dotnet.exe`，clone或移机后不保证该路径存在。
所有项目依赖锁文件提交Git；Deterministic、源信息 / 调试信息设置在props和项目中。
“确定性构建”配置不能替代不同环境下实际产物一致性核对。

```powershell
# 仓库根目录执行
./code/build.ps1 -Locked
./code/run.ps1 -RuntimeDirectory "已核对的runtime目录" -ModelsDirectory "已核对的models目录"
```

build输出默认 `code/VPet-Simulator.Windows/bin/x64/Release/net10.0-windows7.0/`。
run先locked build，验证两种依赖，再建立缺失目录junction，最后检查已有桌宠实例并启动。
默认runtime/models指安装版，也可用BLUE_WHALE_RUNTIME / BLUE_WHALE_MODELS。
已存在junction不会因新参数自动换指向，调试时需核对真正目标，不要仅看本次命令。
模型文件不提交Git，clone不包含它；引用安装依赖不代表运行安装版应用DLL。

### 19.2 发布脚本与一键安装器

2026-10-04 安装器界面修正：`InstallerForm` 使用单列 `TableLayoutPanel` 和自动行高，说明、下载来源和状态文字按可用宽度换行，路径选择行和安装按钮按字体测量撑开；采用 DPI 自动缩放，窗口保留最小宽度并随内容增高。布局验收以同一窗体在 100%、125%、150%、200% 的字体与控件尺寸模拟下检查文字、按钮和控件边界，并渲染下载中状态；不触发安装或模型下载。

AGENTS.md、本规格和 LOCAL_GIT_HANDOFF.md 属于本地内部维护资料，已排除 Git 跟踪，禁止上传公开仓库。

公开首发使用一键安装器，构建命令为：

```powershell
./tools/Build-Installer.ps1 -SdkPath "dotnet.exe绝对路径" -RuntimeDirectory "已核对的runtime目录"
```

输出文件为 `release/BigBlueFish-Setup-x64.exe`，另有同名 `.sha256`。安装器是自包含单文件 Windows x64 EXE，内部携带桌宠程序和推理运行时；运行时从固定 Hugging Face 来源下载模型并校验 SHA256，模型不嵌入 Git 仓库或安装器。

旧的 `Build-Release.ps1` 仍可用于开发期生成 ZIP 目录和发布包，但普通用户的正式 Release 不再把 ZIP 作为安装入口，正式 Release 只上传 `BigBlueFish-Setup-x64.exe` 与校验文件。

版本必须符合脚本正则语义版本格式；使用未占用新版本号。公开首发版本为 `0.1.0`；安装器输出文件名固定以 `BigBlueFish` 开头，已存在文件时拒绝覆盖。
publish Release、Platformx64、RIDwin-x64、self-contained=true、RestoreLockedMode=true。
可传RuntimeDirectory复用已核对运行时，或RuntimeZip使用固定哈希压缩包；默认下载官方固定版本。
RuntimeDirectory路径只检查llama-server存在，不验证整个目录是b10809；需要操作者主动核对。
官方固定llama包：`llama-b10809-bin-win-vulkan-x64.zip`。
SHA256：`97e50b3ef0cdd2cb4d5afd446a9006b3496bee6c0d0ba7083d32f36075771870`。
来源：[llama.cpp b10809 包](https://github.com/ggml-org/llama.cpp/releases/download/b10809/llama-b10809-bin-win-vulkan-x64.zip)。

安装器内部 payload 包含exe、三份应用DLL和依赖、.NET运行时、动画图标、runtime、licenses、THIRD-PARTY、Setup-Model、安装说明和models说明。
不含GGUF和用户prefs；模型在安装过程中从固定来源下载，安装器校验通过后才写入安装目录。
manifest.json记录version、platform、selfContained、modelIncluded和每个文件path / size / SHA256，manifest自身不在files列表。
安装器构建失败保留未完成中间目录供检查，不自动反复覆盖或清理。安装器发布文件旁边生成 `.sha256`。
创建发行文件属于发布产物，不是自动安全备份；维护agent不要另外未经请求复制旧安装目录。

### 19.3 模型配置

`Setup-Model.ps1` 被复制到发行根目录后执行；若直接执行仓库tools里的模板，默认目标是tools目录，需要显式DestinationDirectory。
支持从ModelFile复制已有正确模型，或下载固定revision。

| 项目 | 固定值 |
| --- | --- |
| 本地文件名 | Qwen3.5-4B-heretic-Q4_K_M.gguf |
| 上游文件名 | Qwen3.5-4B-heretic-f16_Q4_K_M.gguf |
| 大小 | 2708804480字节 |
| SHA256 | 8485535a36c9f333574d08b650ad698ac02ec30752bd9cd87e493a3b7531bee1 |
| HF revision | b63ff4662e4863cfa005c9cd8ed34b87ed44b7e1 |

下载来源：[固定GGUF](https://huggingface.co/Biomanticus/Qwen3.5-4B-heretic-gguf/resolve/b63ff4662e4863cfa005c9cd8ed34b87ed44b7e1/Qwen3.5-4B-heretic-f16_Q4_K_M.gguf?download=true)。
已存在正确目标则no-op，错误目标拒绝覆盖；写入`.partial`，完整哈希验证后Move最终路径。
遗留partial拒绝继续，需先检查处理；没有断点续传功能。
硬链接可以在同卷、本机明确需要时节省同模型副本空间，但Git并不保存模型硬链接；移机和ZIP模型配置需重新建立。
不要用改名其他GGUF“满足存在检查”，应验证哈希、量化和真实生成。

### 19.4 Git跟踪与许可

提交源码、动画、区划、锁文件、脚本、文档和许可。
忽略bin、obj、.verification、.work、GGUF、runtime、models、cache、preferences、新闻日标记、锁、日志、测试状态与发布二进制。
release目录只有README默认跟踪；ZIP与sha可在用户要求发布时作为GitHub Release附件，而非大二进制塞入Git历史。
文本换行约定见.gitattributes（PS1 CRLF，主要代码 / Markdown LF）。
VPet上游固定提交 `2e99a42ebeff71d792118f2e8de744b773042f8d`，Apache-2.0；动画来源社区项目、MIT；运行时MIT；Qwen模型Apache-2.0；地区数据MIT；天气Open-Meteo署名见THIRD-PARTY。
更新依赖须同步锁、许可证 / NOTICE及实际验证，不能删署名或把社区定制宣称官方产品。
完整来源见 [THIRD-PARTY.md](../code/THIRD-PARTY.md) 与 [licenses目录](../code/licenses)。

## 20. 验收规格、现有证据和诊断

### 20.1 测试执行边界

v0.1.1 针对性验收入口为 code/tests/PowerNoticeVerification：独立沙盒借用已经核对的模型、运行时及素材目录；--expect-bug 用于旧实现的下一次 Tick 清除提示复现；修复后覆盖天气 / 测试同文案、逐字显示、5 秒驻留、直到下一句、重复电源事件、离电无模型进程、真实 CPU 生成、模型回复在离电时取消与卸载。供电使用 TestPowerOverride 模拟，不能称为物理插拔实测。测试不写用户安装目录设置。

代码验证入口 [RecoveryVerification/Program.cs](../code/tests/RecoveryVerification/Program.cs)。
`code/test.ps1`要求先退出所有桌宠实例，创建code/.verification沙盒，并junction源码assets与参考安装runtime/models。
测试依赖Windows实窗和参考安装目录，不是完全脱离安装程序的跨平台单元测试。
参考安装程序集用于原有提示 / Clean行为对比，不能把“测试引用安装DLL”误读为应用发布复用旧DLL。
文档改动不需要停止用户试用或重跑全部实窗 / 大模型测试。

```powershell
./code/test.ps1 -InstalledDirectory "D:\Program Files\蓝色大肥鱼"
```

`.verification/result.json`是运行结果、Git忽略；`code/verification.json`是提交的验证摘要。
后者必须标注日期、版本和真正测过的内容，不复制旧结果装作本次验证。

### 20.2 功能验收矩阵

| ID | 操作 / 条件 | 必须观察 | 证据 / 注意 |
| --- | --- | --- | --- |
| A-BOOT | 正常发行exe启动 | 动画出现、托盘存在、没有普通任务栏按钮 | 进程完整路径、实际窗口；不能只看ready旧文件 |
| A-ONE | 当前版运行再启动另一版 | 不出现第二宠物或第二托管模型 | 跨目录同Mutex |
| A-FOCUS | 点击 / 拖动宠物及关气泡 | 原前台不因角色窗激活丢焦点 | HWND / 原生样式和实际输入观察 |
| A-EDGE | 四角，各scale0.65/1/1.6 | 可见角色贴工作区边，气泡在工作区内 | 四角×三缩放×短长文本24组实窗 |
| A-LONG | 先短提示，再长天气 | 首字开始前按全文留尺寸，末行全可见 | 同时读popup实际矩形与文字，不能只读字符串 |
| A-TYPE | 约73字普通输出 | 持续吐字，约2.6秒量级完成，无半分钟中断 | 从首字计时，模型等待另计 |
| A-UNICODE | 代理对 / 组合字符文本 | 不出现半个Unicode元素 | 专门显示测试，可固定输入，不能替代菜单模型测试 |
| A-DWELL | 选择驻留5秒，长句 | 正常吐字期间不消失，完成后再计5秒 | 见修改驻留时的已知例外 |
| A-STAY | 选直至下一句 | 不按时间消失，可手动关 / 被新句替换 | 关闭按钮不退出宠物 |
| A-REPLACE | 旧句未吐完来新句 | 旧timer停止，显示新句全文布局 | 不出现旧前缀串入新句 |
| A-MENU | 角色右键 | 设置 / 天气 / 吐字测试三项 | 实际Click进入对应功能 |
| A-TRAY | 隐藏后双击 / 点显示 | 角色恢复，位置合法，托盘可退出 | 用户隐藏不同于全屏隐藏 |
| A-REGION | 改省→市→县，再重启 | 联动合法，县区保存，新天气地点一致 | 不只看三个caption |
| A-REGION-BAD | 无效县区代码 / 缺坐标县区 | 无效设置回默认；合法缺坐标明确失败 | 两种情况不能混淆 |
| A-WEATHER | 关闭PublicInfo，手动点天气 | 仍GET所选地区并让模型自然播报 | 原始事实和模型输出对照 |
| A-CACHE | 同地区自动两次、换区、主动刷新 | 自动可缓存、换区不混、主动绕缓存 | fixture HttpHandler可记录请求次数 |
| A-WEBFAIL | HTTP失败 / JSON坏，再正常请求 | 明确错误，不污染cache，之后可恢复 | 不用静态成功文本掩盖 |
| A-TEST-GPU | GPU开启点吐字测试 | 等真模型，非固定短句，正常吐字 | 实际runner -ngl99 / -ub32、Vulkan模块、结果 |
| A-TEST-CPU | GPU关闭重建runner再点测试 | CPU模式真实生成与正常吐字 | -ngl0 / -ub512；不能只改提示标签 |
| A-NOMODEL | 关模型点天气 / 测试 | 提示被禁用，无新推理进程 | 不越过用户开关 |
| A-POWER-ON | 电池模拟且暂停开关开启 | 旧模型退出、新请求禁止 | TestPowerOverride是模拟非真拔电 |
| A-POWER-OFF | 电池模拟且暂停开关关闭 | 实际可预热 / 生成并驻留 | 与开启分支分别测 |
| A-POWER-AC | 模拟插回电 | 能重新预热 / 生成 | 参数仍对应GPU选择 |
| A-STALE | 天气中换区、隐藏、关模型或点测试 | 旧结果不覆盖新状态 | 真实交错而非只单独测试各按钮 |
| A-FULL | 全屏前台与退出全屏 | 角色隐藏 / 恢复，用户隐藏不被撤销 | 记录探测策略、游戏拓扑未穷举 |
| A-PREF | 损坏主prefs，保留有效bak | 读bak；均坏时defaults | 原子保存与重启读取 |
| A-EXIT | 正常关闭、宿主终止 | runner不残留、托盘消失 | Job保障与正常退出分别核对 |
| A-RELEASE | 新ZIP解压、配模型、运行 | 自包含入口工作，资源齐全、模型未捆绑 | 构建成功不等于发布exe验收通过 |
| A-GIT | 干净clone后查看结构 | 有源码、spec、锁、许可；没有用户模型 / 设置 | release二进制不随clone自动出现 |

### 20.3 历史证据（非本次重新执行）

`code/verification.json`的 historicalVerification 保存历史源码快照于2026-10-04的Release x64验收，0构建错误、205 checks、24实窗四角场景；顶层保存当前公开版本的针对性验收。
记录34/392/3209地区数量、级联保存、菜单点击、天气缓存 / 故障恢复、两种电池策略、模型退出。
逐字回归记录旧版可复现，73字符约2.697秒，全量布局立即预留、驻留从完成后开始。
自包含发行入口曾实际执行吐字测试与天气，记录真实runner、GPU参数、Vulkan加载和用户prefs保留。
这些支持相应功能，但不是全模型语义、GPU吞吐或物理拔电认证。
恢复基线另有157 checks、1006素材、原提示词 / Clean核对；不要把不同版本check数拼成一次测试总数。
验证摘要也保留了普通模型在没有天气context时说“明天天气若凉快…”的样本，这说明提示词约束未被完全保证。

### 20.4 `--test-mode` 文件接口

只有启动参数含`--test-mode`才启用。正常试用程序不应加此参数，也不会消费测试命令文件。
主Tick约每1秒写`test-status.json`；写入包含：ready、visible、hiddenByUser、fullscreenHidden、fullscreen、onAC、runner、busy、status、hwnd、foreground、startupForeground、style、Left/Top/Width/Height、bubble当前前缀、bubbleVisible、bubbleDurationIndex、bubbleRemainingSeconds、action。
无限deadline的remaining=-1。bubble字段不是全文，要测量截断必须同时看窗口 / FullText路径。
单槽`test-command.txt`读trim后先删除再执行；不是队列，也没有事务响应ID，连续覆盖可能丢命令。

| 命令 | 作用 |
| --- | --- |
| settings / close-settings | 打开 / 关闭设置 |
| weather / speech-test | 调真正用户天气 / 吐字测试路径 |
| bubble / bubble-next | 固定文字布局 / 替换辅助，不代表真实模型测试 |
| battery / ac / real-power | 设置false / true / null的测试供电覆盖 |
| hide / restore / exit | 对应窗口操作 |
| generate | 把下一自动计划设为当前时刻，仍受闲置和其他条件限制 |
| inspectable / normal-style | 测试调整任务栏与原生样式 / 恢复 |
| model-off / model-on | 测试内改模型开关并停止 / 允许预热 |
| render | 渲染companionLayout到test-render.png |
| action:动作名 | 播指定动作 |

`render`不包括已经移到独立popup的气泡，不能用它证明气泡可见性。
测试内改设置可能在关闭主窗保存，需用沙盒而不是用户试用目录。
纯模拟AC和battery仅覆盖Native.OnAC，不改变Windows真实供电。

## 21. 已知限制、诊断方向和接手优先事项

本节列出应诚实交接的实际限制，不把它们当作用户已经授权的新开发任务。

| 问题 / 限制 | 影响 / 诊断 | 维护建议 |
| --- | --- | --- |
| 模型偶发无依据天气 / 活动句 | Clean不作事实校验，提示词不是硬保证 | 复现并对照context，未来改进需保留真实推理；不要用固定文本隐瞒 |
| 修改驻留时可重置正在吐字的deadline | 很短驻留与异常长句可能提前折叠 | 若修复，typing期间仍无限、完成后读最新驻留值 |
| 动画失败直接改Text而非Speech.FullText | 初始化错误气泡全文测量可能不是正常模型路径 | 若改错误显示，统一Notice路径并测试早期初始化 |
| 极小工作区 / 超长非正常文本 | 高度被cap且无滚动，无法保证无限长内容完整 | 维持正常短句限制，扩大文本范围时设计明确降级 |
| 混合DPI / 多屏 / 任务栏拓扑未穷举 | 24角落测试是有限覆盖 | 在实际硬件测，区分Bounds与WorkingArea |
| 未物理拔电验证 | 两种策略是测试覆盖供电值 | 用户需物理试用时报告准确证据范围 |
| Suspend未锁存独立挂起状态 | 入睡前Tick可能再预热 | 若出现再加载，考虑按真实系统事件改善 |
| 已活runner不先重探健康 | 异常驻留子进程可能到请求时才失败 | 核对活性与失败恢复，不以PID当推理成功 |
| 空闲端口探测后释放 | 存在小概率端口竞争 | 遇启动失败看实际占用，勿固定公共端口掩盖 |
| GPU模式无自动CPU fallback | GPU失败明确失败，不自动假装模式成功 | 保留用户选择，若未来降级需明确提示并更新spec |
| stdout/stderr丢弃 | 一些GPU错误缺详细runner日志 | 调试时用受控临时日志，不把用户内容常态上传 |
| 保存异常被吞 | UI设置可能未落盘 | 检查可写目录 / 文件占用及重启结果 |
| 自定义间隔UI显示8档 | 文件值与展示档位可能不一致 | 扩展档位时明确兼容，不无声重置用户值 |
| 启动项跨版本共享 | 切开关可改另一个版本路径 | 修改前检查实际注册表位置 |
| run/test旧junction复用 | 新参数未必换目标 | 先检查链接绝对目标，不递归误删连接指向内容 |
| 新闻机会按预约消耗 | 失败当天也可能不再尝试 | 这是当前策略，若改需重新验每日上限 |
| 新闻 / 天气schema变化 | 解析失败、cache不更新 | 正确显示失败并恢复，不输出伪天气 / 新闻 |
| 无TTS / 聊天 / 多轮记忆 | “播报”不是音频，“测试”不是对话窗口 | 新需求要显式设计，不误写为现有功能 |
| 源码保留上游未用功能 | 编译通过不证明这些入口工作 | companion是当前范围，游戏 / Steam未完整测试 |

处理“先一小段很久后才完整”的顺序：

1. 确认正在运行的新exe路径和版本，而非安装版 / 旧发行目录。
2. 区分模型等待提示与首字后吐字；记录首字时间、全文可见时间。
3. 比较TextBlock前缀、FullText和popup实际高度，查是否裁切而非模型卡顿。
4. 检查全量测量、实际候选宽度二次测量和Text属性订阅是否保留。
5. 检查Speech Stopwatch与字符边界，不把模型100秒超时或驻留30秒当作吐字速度。
6. 用真实菜单speech-test确认模型后，再用布局辅助隔离窗口问题；分别报告证据。

## 22. 维护任务完成标准

源码修复完成：已定位真实入口、改动可读、相关行为验收通过、记录限制并更新spec；不能仅有反编译文件。
发布完成：新版本号、发布构建与校验、解压实际入口测试、用户设置迁移核对、Git提交；是否上传GitHub取决于用户明确要求。
文档交接完成：spec及访问文档已提交main，本地URL能读HEAD，接手agent知道clone没有GGUF / 发布二进制，也知道哪些结果是历史记录。
报告应区分“已实现”“本次实测”“历史验证”“仍未验证”。不以日志中的一个ready、一个HTTP200或一条固定台词替代最终产品行为。

接手后如用户要求继续功能开发，先复核本规格与当前源码是否发生漂移；本文基线不是对未来提交不变的承诺。

## 23. 本地 0.1.3 / 0.1.4 对照开发（2026-10-06）

本节更新第 13、14、16、19 节有关输入、测试入口和构建的历史行为。默认编译为 0.1.4；使用 CompanionInputMode=Vision 编译为 0.1.3。保持 main，不提交、推送或替换 GitHub Release。两个编译选择共享角色、设置、菜单和吐字生命周期，输入路线独立。

### 23.1 当前统一触发入口

普通倒计时满足条件和菜单/托盘吐字测试均进入 SpeechCountdownZeroAsync。第23.9节修复后统一解析当前前台，只有桌宠或菜单占据焦点时才使用经过可见性复核的外部窗口记录，记录不可用时回到桌面。菜单弹出前刷新记录，不再无条件复用lastExternalForeground。设置、模型、供电和 Busy 检查保留。BeginModelSpeech 清掉旧模型气泡并显示加载动画，模型流式片段经 ShowCompanionPartial 展示，最后 FinishCompanionPartial 后开始驻留计时。加载动画放在角色头旁的空白区域。当前流式是实验的延续，StreamingResponses=true；此前全文结果后动画吐字的记录属于历史。

### 23.2 0.1.3：Vision

保留此前完整 Persona / SpeechRequest。前台窗口经 PrintWindow（失败时 BitBlt）截取，最长边 768px，以 JPEG 的 image_url content 与文本共同发送本机 Qwen 服务。投影文件 models/Qwen3.5-4B-heretic.mmproj-f16.gguf 存在时加载它，image-max-tokens=512；GPU ubatch2048/batch2048/fa off（同日对照优化，证据见23.7）。前台辅助资料包含进程、窗口标题和 UI Automation。产品版本0.1.3，文件版本0.1.3.0。

### 23.3 0.1.4：OCR

在一次请求开始时固定 HWND，同一窗口用于截图和辅助身份字段。CompanionOcr.ReadAsync 在工作线程截取最长边2560px的PNG并调用 Windows.Media.Ocr，图片在内存中解码为BGRA8。优先简体中文，次选英文或已有OCR语言。系统语言组件缺失或截图失败明确返回失败状态，模型不伪造成功结果。

不遍历 UI Automation；进程、标题只是辅助，屏幕文字是主要证据。可见文档、网页、聊天正文都进入OCR；不因内容是聊天正文而屏蔽。模型输入包含上部、左侧、主体、右侧、底部五个空间区域，每个区域内从上到下排列。词按Y中心归行，大横向空白拆成片段，中文词连续拼接，英文词保留空格，明显纵向空白分段。区域文字预算分别700/650/3000/500/650字符，超长保留头部与尾部，避免丢失文档身份及最近聊天。这里是空间编排，不是预先把用户活动归为固定类别。

本机服务仅接收system/user纯文本，imageAttached=false，不加载mmproj，-c8192。GPU参数为-ngl99/-ub2048/-b2048/-fa off，CPU为-ngl0/-ub512/-b2048/-fa off；Vision输入路线保持独立，当前同样采用这组参数，见23.7。OcrPersona保留原完整鱼娘人设，附加屏幕旁观者视角及示例，避免将画面上的对话当成对桌宠说的话。输出聚焦活动、项目或聊天主题；事实仍需对照OCR和真实画面验证。产品版本0.1.4，文件版本0.1.4.0。

正常运行不保存图片或OCR正文。仅--test-mode会写ocr-status.json，含词位置、实际请求、响应、截图/识别/首流式片段/总耗时；没有自动上传。验收后退出测试模式并删除本次诊断文件。0.1.3对应vision诊断也已加入忽略规则。

### 23.4 对照构建

tools/Build-LocalVariant.ps1 -InputMode Vision|Ocr -SdkPath <本机SDK>，分别输出.work/BigBlueFish-v0.1.3-vision与.work/BigBlueFish-v0.1.4-ocr。它只构建自包含应用，不安装、不下载模型、不启动服务、不上传。code/build.ps1、run.ps1、test.ps1均有InputMode选择。三个应用的Vision依赖锁使用packages.vision.lock.json，OCR使用packages.lock.json。

主程序Ocr构建TFM net10.0-windows10.0.26100.0，最低平台版本10.0.19041.0；随self-contained发布物包含Microsoft.Windows.SDK.NET.dll及WinRT.Runtime.dll等投影依赖，OCR语言来自本机Windows组件。Vision保持此前net10.0-windows7.0。Core/Interface仍用原TFM。已有Windows实窗测试项目按输入模式选择对应TFM。

### 23.5 本轮验证记录

OcrLayoutVerification的7项确定性验证通过：左右栏、中文拼接、英文空格、聊天顺序、底部区域、空文本及长内容首尾保留。OcrVerification使用5张程序生成的文字画面（代码项目/聊天/Word课程报告/科技新闻/视频教程），真实Windows OCR与本机模型已执行；这些是合成画面，不能冒称真实微信/Word/浏览器实测。

初次聊天输出把屏幕消息当成与自己对话；加入系统旁观者视角后，同一测试输出“主人正在和朋友约明天吃火锅，光想想热腾腾的锅子，小鱼娘都馋啦～”。合成画面的OCR约20-50ms，整段模型响应耗时随prompt缓存与请求变化，不将其作为真实应用首字速度。两条路线均已独立self-contained编译验证。

本轮初次真实前台测试时桌面锁定，GetForegroundWindow=0且同会话LogonUI运行。未绕过锁屏、未把合成图结果替代真实窗口结果。真实应用的识别质量与首字计时需在解锁后验证，并把记录补充于此。

### 23.6 0.1.4 首字延迟故障修复（2026-10-06）

用户报告首字约16秒。本轮桌面已解锁，实际C盘speech-test入口复现首个有效片段20764ms；同轮截图105ms、OCR145ms，输入确实是纯文本。固定真实OCR请求做无缓存对照：原GPU ub32处理1097个prompt token耗时11043ms、首片段11071ms；单独改ub128/512虽然快却连续输出问号，禁用F16和Vulkan fusion也没有消除此症状，未采用。

ub2048/b2048在部分输入上正确，auto Flash Attention在另一组输入上仍出现无关短句。最终显式关闭Flash Attention，GPU保持开启，模型、原Persona/SpeechRequest/OcrPersona/OcrSpeechRequest均未缩短或更换。固定真实请求的对照约1100token，prompt处理约2699ms、首片段约2719ms；这确定了本机有效配置，不把故障归因到所有Qwen模型或所有Intel驱动。上游有相似Vulkan/Qwen批量相关正确性报告，但本轮未验证具体shader级根因。

OcrVerification改为真实流式请求，检查输出长度、主人称呼及画面活动词，避免只有非空文本就算成功。五类合成画面无缓存轮和启用缓存轮各通过一次，共十次正确的活动/旁观者检查；无缓存首片段1.61-1.79秒，有缓存0.99-1.79秒。合成画面不替代真实应用。

最终C盘同一真实ChatGPT前台连续三次正常speech-test：Generate进入到首片段分别3274/2396/3516ms；截图82-140ms，OCR200-250ms；模型请求到首片段2764/2033/1970ms；第三次天气获取额外1236ms。每轮都记录到模型气泡可见及实际响应。通过文件投递命令再轮询状态观察到的首次气泡为5313/3465/4522ms，含测试命令轮询和观测间隔，不混写成用户菜单点击的精确像素首字时间。实际输出包括“主人正在调试蓝色大肥鱼的模型配置呢……”等，与OCR画面对应；这三轮证实延迟改善，不代表所有应用内容理解已全面验收。

OCR诊断新增publicInfoMs、inputReadyMs、runnerReadyMs及modelToFirstContentMs，generation计时移到联网资料查询之前，避免漏计网络等待。仅test-mode落盘，结束后切回普通模式并清理本次诊断正文。两个模式均已重新self-contained构建；没有修改D盘安装或上传GitHub。

### 23.7 0.1.3 同参数对照优化（2026-10-06）

用户要求0.1.3也尝试0.1.4的批量/Flash Attention优化。固定模型和mmproj，保留image-max-tokens512、768px最长边及完整Persona/SpeechRequest，GPU参数改为ub2048/b2048/fa off，仍发送图片。没有改成OCR、降低图像分辨率或缩短提示词。Vision测试诊断增加elapsedMs、firstContentMs、runnerReadyMs、modelToFirstContentMs，普通模式不记录。

用5张768x512程序生成画面，经真实本机Vulkan模型和投影文件进行流式请求。相同开发画面837prompt token：原ub32/autoFA且无缓存时首片段17294ms；优化配置首次无命中缓存时7890ms、下一请求命中362个文本token时4702ms。两次请求画面相同且仅末尾测试轮次编号不同；模型输出均为“哇，编译成功啦！主人今天这么厉害，小鱼娘要给你鼓掌哦～”。这不是实际前台窗口或菜单操作的计时。

其他画面的优化首片段（首次/重复，毫秒）：聊天7607/5135、办公7159/5080、网页7733/4789、视频6855/4546。所有10次输出经核对均对应合成图片中的活动，未出现问号乱码。聊天点出与小白约明天火锅、办公点出课程报告、网页点出电脑、视频点出PS绘画。系统提示词中没有提供这些具体内容。第一轮并发启动基准导致端口冲突的请求已排除；有效开发对照重新在独占端口上顺序执行。基准助手已增加端口占用检查、启动健康检查及退出等待。

真实截图准备时GetForegroundWindow=0，同会话LogonUI存在，已请用户解锁并继续合成测试；没有执行锁屏UI操作。0.1.3 self-contained构建完成于.work/BigBlueFish-v0.1.3-vision，版本已核对0.1.3。C盘不覆盖为0.1.3，测试结束恢复普通0.1.4进程。真实前台图片、辅助UIA信息、实际菜单吐字首字时间仍待解锁验证；不把合成首片段耗时承诺为所有应用的实际耗时。D盘、GitHub未修改。

### 23.8 桌面与入口页面的活动误判优化（2026-10-06）

目标：两版在只有桌面图标、新标签页、主页、推荐卡片或空搜索框时，围绕眼前界面亲昵地说话；已展开代码、文档、文章或聊天时，继续结合活动、项目和话题表达。桌面文件图标不等于正在整理文件，网址占位文字不等于正在输入网址，首页新闻推荐不等于正在阅读该新闻。

源码：原鱼娘人设与丰富口吻保留为BasePersona，两版附加共用SceneGuidance；SpeechRequest/OcrSpeechRequest在末尾加入共用SceneRequestGuidance，区分明确活动和入口界面两种事实层级。旧提示中“应用和标题可以判断活动”改为应用名/标题辨认界面、已展开主体判断活动，知乎示例改为问题正文而非仅应用名。OCR旁观者提示的活动要求改为以明确主体内容为条件。没有新增按应用字段硬编码的活动分类、第二次模型请求或固定台词替代。第23.2/23.3/23.7节的“保留完整提示词”描述此前性能优化，此后采用本节的事实依据调整与场景指导，原活泼、亲昵和调皮的口吻未删减。

验收：OcrVerification新增桌面、新标签页和主页三张程序生成画面，包含容易被误认成任务的项目图标、文档名、常用网站、推荐新闻、空搜索框。连同原五类明确任务画面，通过真实本机模型分别走图片输入和Windows OCR后的纯文本输入，检查入口画面是否虚构活动，并人工复核具体措辞。测试画面是合成材料，不能当成真实应用或实际菜单操作的验证。关键词检查不是事实判定的完整保证，缺少具体主题的响应须记录。

最终提示词的无缓存记录：每版8类画面、每类2次，共32次真实推理。OCR的16次自动检查通过；Vision的入口画面6次均未声称正在整理文件、输入网址或阅读推荐新闻。Vision全组命令报告3次关键词检查失败：一条Word输出已经提及“教育实践”，但此前检查词仅课程/报告/总结，已补充教育实践；另外两条视频输出只点出视频界面，没有点出绘画主题，作为剩余质量差异保留。未将检查词补充冒称为模型质量改善，也未宣称所有明确任务已精准识别。OCR示例包括“主人正在给蓝色大肥鱼写代码呀，编译成功啦，小鱼娘摇尾巴给主人加油～”；Vision入口示例包括“主人面前是新标签页呢，小鱼娘探出头，好奇下一站会有什么有趣的东西呀～”。

两版均已重新self-contained发布到23.4所列目录，版本分别核对0.1.3与0.1.4。2026-10-06 19:57 C盘覆盖为本次0.1.4，主应用、deps/runtimeconfig、Core/Interface、Windows SDK/WinRT共8个文件哈希与发布物一致，preferences哈希未变。普通模式启动后ready.status刷新，本机模型health=ok，实际服务ub2048/b2048/fa off、不加载mmproj。此轮没有进行真实应用界面内容/实际菜单首字的验收；以上画面质量证据均为合成测试。D盘、GitHub与Release未修改。

### 23.9 吐字测试复用最小化窗口故障及独立本地Switch（2026-10-06）

用户在桌面连续五次测试，输出却认为正在使用ChatGPT。源码确认旧SpeakCompanionTest无条件复用lastExternalForeground，记录检查和Capture均未检查最小化状态；PrintWindow可读取已最小化窗口，导致被排除在实际前台之外的旧应用仍可进入输入。用户过去五次的实际输入没有常态落盘，因此不能声称已追溯取得那五次的截图。

两版共同修复：IsVisibleCaptureTarget校验有效窗口、可见性、IsIconic及DWM cloaked；捕获层也执行相同拒绝检查。ResolveSpeechForeground优先实际当前窗口，仅在宠物/菜单或外壳临时占据前台时检查记忆中的有效可见窗口；旧记录最小化、隐藏或关闭时回退Shell桌面。当前前台为零时返回零，不复用旧记录。右键菜单和托盘菜单弹出前重新解析前台，倒计时与测试入口共同使用该选择器。原先将所有Windows.UI.Core.CoreWindow排除的限制移除，继续由可见窗口截图提供内容。

捕获时临时使用线程PerMonitorV2 DPI上下文，GetWindowRect与PrintWindow/BitBlt统一为物理像素坐标，离开捕获函数后恢复原上下文。诊断工具原先因DPI坐标不一致截出1440x900的左上部分，修正后OCR获得完整桌面2560x1600、Vision获得完整桌面768x480；这项证据是诊断工具复现，不冒称原WPF进程在所有应用上均存在裁切。

ForegroundVerification使用独立子进程的真实WinForms窗口验证可见、最小化、恢复、隐藏、关闭及桌面回退，14项通过。真实Shell桌面内容经生产Capture及请求拼接代码送本机服务，两版各5次均称画面为桌面，未声称正在用ChatGPT；OCR其中一次尾句仍猜“正在整理文件”，属于剩余模型事实推测，未算成完全准确。这里验证窗口/捕获选择和实际桌面输入，未冒称重放了用户原菜单点击的五次请求。

独立Switch放在当前Codex任务work/BigBlueFishSwitch，桌面入口D:\31273\Desktop\蓝色大肥鱼 Switch.lnk。它是Windows PowerShell/WinForms辅助工具，不加入仓库或应用构建，读取23.4已有的两份发布物。GUI显示安装版本、进程状态、模型识别方式/health和操作结果，切换期间禁用按钮。只在C盘测试目录关闭旧实例、复制并逐文件验证应用发布物、清除仅另一构建含有的应用文件、保留设置/模型/runtime/cache，再启动普通模式。preferences在旧程序正常退出后取哈希，并确认复制前后不变。主版本与识别方式不一致不报成功；模型设置关闭或离电暂停时显示对应状态。

工具同一后端已实际完成0.1.4→0.1.3→0.1.4，两次启动均ready与模型health通过，并确认mmproj仅在0.1.3启用；GUI布局已渲染核对，桌面快捷方式启动的窗口标题和句柄已核对。最终两份本地构建均含窗口选择及DPI修复，C盘恢复普通0.1.4。版本号不变，D盘和GitHub未修改。


## 24. 0.3.0 动画重构（2026-10-07，本地开发）

### 用户确认的目标与规则来源

动画全部替换为 PC2005-cloud/dsh-pet 的106段透明WebM，来源提交 eb7426771342413cdbf55298dae576b1d8137f52，MIT。用户在本地预览台的106条最新批示（更新时间2026-10-07 02:42:59 +08）已逐项转换为 assets/fish/animations.json；工具 Import-AnimationRules.py 可重新导入。对话对待机权重的确认优先：呼吸休闲100，哼歌20，23:00—08:00小憩40。原来的PNG素材不再用于构建，原配色不再套蓝色滤镜。

### 当前实现与源码入口

CompanionAnimationPolicy.cs 是纯选择策略，CompanionAnimations.cs 是窗口调度，CompanionVideoPlayer.cs 以单独的FFmpeg进程流式解码640×360、24fps的VP9 alpha。强制libvpx-vp9以保留透明通道；低于8的压缩残留alpha归零，CompanionSpriteImage.cs 按实际alpha接受点击。解码缓冲有界，不将视频展开为PNG文件或长期保留所有帧，切换时取消旧任务、结束旧进程并挡住旧回调；CompanionJob保证宿主退出时子进程结束。暂停隐藏的窗口时暂停帧时钟。非交互关闭先完整播放一次鱼娘屈膝礼仪再退出；系统会话结束允许直接关闭。

待机、普通事件、点击响应是三个独立权重池。默认事件间隔40秒（按最后一次40秒概率讨论作为可调整的初始值），设置范围30—300秒，调整后重设下一次事件期限。期限到达后等当前完整动画结束才选择事件；不积压多个待执行事件。启动礼仪完整播放后开始事件计时。点击选择20/100/100/20/20，拖拽完整播放后保留末帧至松开或失去捕获，松开回到待机。余额三个舍弃动作及垂头叹气冒汗不参与普通事件。部分其他余额、工作命名动作按用户批示正常参与普通池。

工作识别加权字段WorkKind/WorkWeight已留在策略中，调用处WorkRecognitionEnabled固定false。不会为了动画启动额外模型请求；相关动作的普通权重保留，未来启用识别上下文后才提高到1000。此次未修改CompanionBrain、Vision/OCR捕获、提示词、模型请求或推理参数。吐字仍走原来的生成与气泡显示流程；原来的说话完成后强制挥手已去掉，避免普通动作被吐字打断。

饭点加权前后各1小时，窗口内平坦加权，不作按小时三角分布。默认早餐08:00、午餐12:00、晚餐18:00，是用户未指定具体时钟时的初始选择，设置界面可调小时。中午加权“吃晚餐”，晚上加权“吃午餐”，严格遵从批示。“吃冰淇淋融化”夏季饭点是100+400=500。元宵/端午/腊八食品在各节日前后7天内平坦窗口，饭点加2400；其余日期按30/30/100。节日动作按批示前后7或14天按日期天数三角加权，不按小时再细分。放烟花峰1000、窗外30；凭空生花在2/14及5/20附近由100提高到1000。农历日期用ChineseLunisolarCalendar换算并排除闰月重复；跨年窗口检查前后年度，超出支持范围的农历日期不触发，公历节日仍可用。季节按北半球气象月份：春3—5、夏6—8、秋9—11、冬12—2。

奔跑组200、螃蟹组200；本实现选择奇数小时奔跑、偶数小时螃蟹，每小时05:00—54:59可入池。出发—一段待机—镜像返回，整个往返只有一个普通事件，不占第二个随机事件名额。位移约70DIP乘缩放，按可见角色范围限位，并回到出发位置；点击/拖拽或打开设置时取消尚未完成的移动组，停在当前实际位置，不回跳原位置。晨间刷牙在23点一小时内挑空闲的完整动画边界使用一次，LastBrushDate随设置持久化，重启不在同一日重复补播。启动/退出礼仪不入概率池。

设置窗口仍为WPF，重做成侧栏、卡片、开关、圆角下拉框和滑杆布局（CompanionSettings.cs / CompanionSettingsStyles.xaml），无需引入Electron运行时。三页为陪伴与外观、说话与天气、模型与供电；保留地区、模型开关、GPU、供电策略、开机启动、缩放、说话间隔和气泡驻留选择。新版贴边和加载圈采用新角色在640×360画布中的可见几何范围。2026-10-07后续按用户选择改为奶白光滑圆角气泡、头发主蓝色描边/文字和乐米小奶泡体，新增朝向角色的小尖角；保持独立窗口。

### 构建、交接与验收

Version.props唯一版本源为0.3.0。Setup-Media.ps1下载固定FFmpeg9.0.1 Gyan essentials构建，校验压缩包SHA256；二进制忽略提交，许可证随源码和发布物保留。代码build、项目直接build以及本地/Release/安装器脚本均准备解码组件。发布应用仍自包含.NET，不要求用户单独安装.NET。现阶段只本地开发与C盘测试，D盘个人安装不修改，GitHub上传需用户审阅。公开README/CHANGELOG/Release说明未在本阶段擅自改写。

验收入口：code/tests/AnimationVerification（权重、时令、日期、隔离），tools/Verify-AnimationMedia.py（106段实际解码透明性），code/tests/AnimationUiVerification（实际WPF播放时钟、完整边界、透明点击范围、控件保存、三档缩放、点击/拖拽/往返、原供电提示及退出），code/tests/InstallerVerification（自包含附件、106素材、解码组件、清单与设置/模型保留）。安装器验证工具 --apply-local-update 分支可显式调用生产安装器的同一个ExtractApplication进行本地测试更新，不添加测试夹具、不下载模型。历史老RecoveryVerification含旧素材、旧版本和旧提示词假设，不作为本次0.3.0验收依据。


2026-10-07本次验证记录：AnimationVerification 2326项通过；106段视频使用随包解码器实际解码透明性全部通过；AnimationUiVerification最终37项通过（24fps完整时钟、透明点击边界、三个设置页、即时保存、三档缩放、滚动、拖拽末帧保持、往返归位及完整退出）；ForegroundVerification14项、OcrLayoutVerification7项通过；最终Vision安装器547项通过。Vision/OCR两个源码配置均编译通过。删除源码中的生成解码exe后，项目直接构建可从已校验缓存自动恢复并将exe复制到输出，Windows PowerShell5兼容编码与哈希路径已实测。C盘安装更新调用生产ExtractApplication，更新前后设置文件SHA256一致，GGUF大小及修改时间一致；最终使用最新版完整安装器再次部署。原生Windows自动点击工具当前取前台进程失败，鼠标系统输入未自动验收，实际WPF控件事件、窗口和渲染为本次UI证据。公开上传、用户手工长时间试用和UI喜好验收未执行。

### 0.3.0 后续气泡外观调整（2026-10-07）

用户明确选择乐米小奶泡体，要求使用角色头发的蓝色，并追加要求小尖角。CompanionBubble.cs以连续几何轮廓绘制奶白圆角主体和9DIP尖角；从当前动画头发区域取代表色#506098，用于描边、文字、关闭按钮和加载圈。字体为乐米小奶泡体1.000原始TTF，以WPF Resource嵌入应用，不依赖系统安装；字体来源页的免费商用说明保留在code/licenses/Lemi-Xiao-Nai-PaoTi.txt。气泡优先放角色上方，放在下方时尖角朝上，贴边时横向指向可见角色中心并避开圆角。字体17DIP，全文测量与流式显示仍用原路径。此次未修改动画调度、提示词或推理配置。

本次实际WPF验证73项通过：确认嵌入字体确实解析为乐米小奶泡体，三档缩放、四角、长短句共24场景检查工作区限位、全文不裁切和尖角方向。预览为生产WPF控件渲染，不是模型吐字测试。

## 25. 0.3.1 故障恢复与安装生命周期（2026-10-07）

用户要求修复当前SpeechRequest验证、失败Runner清理、安装目录普通用户权限和标准卸载。隐藏与预加载逻辑经用户确认保持现状，未调整。Version.props唯一版本源改为0.3.1，OCR和Vision两种构建使用同一版本。

EnsureRunner将进程存在与服务就绪分开：只有加载阶段健康检查成功才标记就绪；复用驻留Runner前再次检查health。加载超时、取消、子进程退出、启动或Job附加失败都会清理进程与引用。HTTP请求挂起也受加载总超时限制。已经失效的驻留进程会被清理，下一次请求可以重新启动；本地completion的HTTP/IO失败同样使Runner失效。普通取消仍保留已经就绪的进程。推理参数、提示词、隐藏策略、动画触发规则不变。

RecoveryVerification中SpeechRequest和Persona的旧窗口标题/无障碍断言改为当前画面内容与入口界面规则。新增RunnerVerification，使用实际子进程和本地HTTP故障服务验证普通生成、天气生成、启动超时、取消、进程退出、健康失效和重新请求。故障服务响应不作为模型质量证据；--real-model分支使用实际本地模型验证生产请求、驻留复用和退出后的重新加载。code/test.ps1默认使用当前Runner、动画规则、前台窗口和WPF动画UI检查；历史恢复验收仅通过-LegacyRecovery显式启用。

安装器不移动数据或安装位置。完成安装时给应用子目录的BUILTIN Users SID S-1-5-32-545授予FullControl并继承到文件/子目录，同时补齐已存在的受保护ACL。禁止对磁盘根目录、Program Files父目录、Windows、用户主目录、桌面根目录或临时目录根目录操作；不跟随目录链接向安装目录外设置权限。

0.3.1后续修复：安装器与卸载器为两个独立工程和可执行文件，共享安装生命周期代码。构建时将自包含独立Uninstall.exe放入payload，安装器不再根据参数进入卸载。Uninstall.exe无参数启动从自身文件位置推导安装目录并校验安装记录，工作目录不影响目标；显式--uninstall与--uninstall-worker沿用Windows入口和临时工作进程。按安装路径生成独立HKLM 64位卸载项，避免C/D两个安装相互覆盖。卸载由临时自包含进程执行，先结束该目录下的桌宠/Runner/FFmpeg，清除属于该安装的快捷方式和自启动，删除清单拥有的程序文件及卸载登记；通过HKLM App Paths登记应用入口，在CommonPrograms的蓝色大肥鱼组创建应用与卸载快捷方式。“保留模型和设置”复选框默认不勾选；勾选保留，不勾选删除。卸载界面使用自动行高布局和DPI缩放。临时卸载进程文件安排重启后清除。

InstallerVerification --lifecycle必须证明受限令牌在授权前不能写入，授权后能创建/修改/重命名/删除，验证卸载登记、保留数据卸载、重装后完整卸载以及父目录拒绝。正式附件仍必须自包含.NET，含FFmpeg、106段WebM、当前字体和对应OCR/视觉组件，模型与投影沿用既定安装下载流程。构建审阅与公开上传按用户授权执行，不能把本地构建完成视为已发布。
本次独立性核验发现llama.cpp依赖开发机System32中的msvcp140/vcruntime140/vcruntime140_1。发布脚本新增Copy-VisualCppRuntime.ps1，从本机Visual Studio正式x64 CRT Redist目录取原始组件，验证Microsoft签名和PE x64架构，应用内配置整套CRT；随包记录版本与SHA256，并附许可来源。实际进程模块路径仍须验证为安装目录runtime，而不能因为本机已有VC++就宣称通过。

2026-10-07 0.3.1本地验收完成：Vision安装器580项、OCR安装器582项；受限令牌写入与父目录ACL隔离、Windows卸载/App Paths登记、应用与卸载快捷方式均通过。卸载UI模拟100/200%共8项和真实192DPI共4项通过，控件可见不交叠，保留复选框默认不勾选。Runner故障注入每路线18项；默认Vision回归包含动画规则2326、前台14、WPF37项通过。真实Vision在C盘Runner重启后生成完成、图片附加且气泡可见；真实OCR4项包含驻留复用、Runner退出后同对象恢复及释放。最终C盘为普通0.3.1 Vision，health ok，.NET与VC运行库均从安装子目录加载；模型大小和修改日期、Program Files父ACL不变，用户设置值保留，位置Top经运行时clamp从647.5变647。安装器在work/release-031-review，仅本地审阅，不上传0.3.1；0.3.0 Release美术升级说明已更新。

0.3.1卸载入口修复的验收增加实际安装Uninstall.exe无参数启动（工作目录为系统临时目录）、显式注册入口、默认未勾选保留、取消不删除，以及实际独立worker执行保留/完整卸载，不能仅调用内部函数代替入口验收。

## 26. 0.4.0对话重建的本地第一轮原型（未发布）

2026-10-14本地preview.4更新本节的请求拼接和桌面输入：实际Shell截图包含多显示器及黑色间隙，整体缩到2560px后图标文字过小；Windows OCR对白色阴影图标标签仍有大量乱码。原代码把这些标签标为画面主体，真实本机模型8次都偏离桌面，包括乱码/神秘代码/作业等。Capture对经过Shell句柄或explorer的Progman/WorkerW确认的桌面层，在PrintWindow完整渲染后裁到鼠标所在显示器，再缩放；普通应用仍只截其窗口，最小化/隐藏拒绝保持。桌面仍实际截图OCR，原词保留于临时测试诊断；模型context表达桌面壁纸和快捷方式场景，不把图标OCR碎片作为活动正文。应用正文仍按五区编排，仅移除没有字母/数字的独立标点片段，句内标点保留。

BuildMessages统一标准system/user消息，所有人设、旁观规则、语气侧重和短句要求在system，本轮画面资料在user分隔块；天气/新闻user也只承载资料。Jinja使用模型内置模板，不手工伪造ChatML。测试模式model-request.json记录实际发送payload，OCR诊断新增windowHandle/isDesktop，普通运行不写。桌面短句对照8次无乱码困惑；此前12次桌面场景输出也无代码误判但偏长，最终保留原短句要求于system末尾。构造应用场景10次输出论文、项目编译、火锅、周报等对应，但新标签页仍一次把推荐手机当作在看手机，不能认定所有入口误判消失。C盘安装和实窗验收结果随后补记。

preview.4本轮完成：OCR布局8项、真实窗口/最小化/Shell选择15项、Runner和生产字段21项通过。标准自包含安装器以生产ExtractApplication与FinishInstallation覆盖C盘，安装前后设置SHA256相同，模型大小2708804480及2026-10-07修改日期不变；主DLL与payload哈希匹配。C盘实际统一speech-test请求完成，窗口HWND6163614、isDesktop=false、722个OCR词、2560×1867，首片段5573ms，响应“唔……主人还在搞代码呢？鱼鱼想看看那个仓库是做什么的嘞！”；Computer Use气泡截图确实呈现该响应。model-request.json核对真实发送system/user职责分离。该条是ChatGPT前台实窗，不冒称桌面菜单测试；桌面8次为生产Shell截图/请求与同一真实服务的固定seed对照。正式版本号仍0.3.1，未提交Git/上传GitHub。诊断模式正常退出并清除安装目录测试正文文件，最终普通OCR PID8240、Runner12516 health ok，hostfxr/coreclr/hostpolicy全部从安装目录加载，Computer Use内核已reset。

2026-10-13用户授权按已讨论计划做提示词调整和真实4B试验。用户填写30条是跨场景语气/性格示范，不是场景固定应答。保持普通OCR路径、单次无历史会话、既有时间触发、新闻稀有调度和天气小时缓存；本轮只改表达。

CompanionDialogue集中管理语气、十条用户示范和轻量的随机表达侧重。示范不带场景标签，学习跨场景语气与性格。同一场景可自然变化，表达侧重不绑定应用，情境适合时可自行换语气。普通、天气与新闻都优先保留鱼鱼的可爱灵气，允许轻轻猜测、俏皮夸张和小愿望；自然说一个小反应即可，没有新增硬性字数限制。新闻固定前缀取消，略过语义与原调度仍保留。

试验入口RunnerVerification --persona-trial：真实生产Brain启动模型，使用生产Complete、流式清洗与同一采样温度；上下文为标明的构造材料，不能称真实App截图。--persona-screen从真实前台截图OCR得到资料，通过相同persona/request/Complete输出。记录位于当前聊天work/dialogue-040，不发布。Version.props仍0.3.1，只有实验源码，尚未认定0.4.0完成，也不覆盖C/D安装版。验证结果完成后补记。

用户随后明确：语气和可爱灵气优先于严谨，可接受小概率胡言乱语；不得为了事实检查丢掉语气。最终实验撤去硬性字数范围和重型事实提示，保留轻量的主体/背景说明与OCR旁观身份。天气、新闻也优先沿用同一鱼鱼口吻，采样温度统一0.65；输入资料、时间调度、缓存有效期仍未改。早期严格版本仅为对照，不作为最终产品方向。

2026-10-13第一轮最终候选真实试验完成：20个构造场景各三次，共60次真实Qwen3.5-4B-heretic-Q4_K_M输出，记录final-voice-results.json；平均长度23.9字符，开头哇33、唔22、咦5，部分同场景变化仍小，天气有一次长输出。真实前台通过生产截图OCR（ChatGPT窗口2560x1864，800词）另生成三次，原话分别为“唔……主人又在和 Git 仓库较劲啦？”、“唔……正在跑回归检查呢？看起来有点忙呀！”、“唔……主人好像在忙代码呢，鱼鱼悄悄帮主人加油啦！”。构造场景与真实前台明确区分，不能由这次单窗口试验推断所有应用识别质量。原型已经通过实际编译和生产Complete流式路径，输出未人工润色；尚待用户审阅语气，不认定0.4.0已交付。C/D安装及设置、调度未修改，实验Runner已结束；不提交或发布。
最终候选RunnerVerification OCR回归18项通过（故障注入与提示请求接线检查，不作为真实模型风格证据）；git diff --check通过。

2026-10-13用户更新部署约定：C:\Program Files\蓝色大肥鱼为测试安装，今后完成的最新本地改动默认构建、覆盖同步到C盘，保留用户设置与模型并恢复普通运行。D盘旧0.1.0安装已由用户自行删除，不再是保留、恢复或验证目标。公网构建审阅/发布授权仍独立遵守，不由C盘同步推导GitHub上传许可。

本轮已用标准自包含OCR安装器覆盖C盘，安装器及应用的实际版本0.4.0-preview.1（通过本地构建Version参数标识，仓库正式Version.props仍0.3.1，0.4.0未正式定稿）。C盘普通进程PID9240，模型PID39316，health返回ok；已安装exe、主DLL、runtimeconfig、WinRT.Runtime及Windows SDK组件SHA256均与本轮payload吻合。.NET实际加载hostfxr/coreclr/hostpolicy均来自C盘安装目录；模型大小2708804480、SHA256和2026-10-07修改时间不变。用户功能设置不变，LastBrushDate由实际运行推进到2026-10-13，Left/Top因像素对齐轻微调整。独立Uninstall及Windows登记随标准安装流程更新。没有改D盘，没有Git提交或推送。本轮没有通过安装后GUI点击再次跑吐字，模型风格证据为先前的60次实际Brain生成及三次真实前台OCR。
2026-10-14本地0.4.0-preview.2修复目标与源码实际调整：用户指定六档说话随机范围2–3、3–5、5–8、10–16、15–23、20–30分钟；CompanionPreferences统一范围，持续均匀采样实数分钟，常规生成、手动天气结束、设置切换、恢复显示、电源事件的时间重排均使用相同范围。启动首次45秒/开启模型20秒和原8秒–10分钟键鼠条件本轮未改。旧SpeechMinMinutes字段保留，3映射3–5、8映射10–16、15映射15–23、30映射20–30；新默认10–16。缩放读取与UI统一50%–200%，5%步进，原85%保留。

自动和测试仍共用SpeechCountdownZeroAsync；删除空结果将brain.Status包装成气泡的OCR失败兜底，两条路均无错误台词，内部状态仍可在模型设置页查看。shared入口不再检查设置窗口，自动Tick仍检查；主动测试允许设置窗口打开并不关闭它。shared入口使用speechRequestPending单一请求门，忙碌/预加载/手动天气时显示圈并异步等待，重复测试点击合并，避免吞掉请求或并发生成。等待结束后重新显示圈，取消/退出/失败释放门和圈。等候最多100秒，不改变模型生成超时。

新增RunnerVerification --speech-flow在隔离设置及生产资源中验证真实WPF窗口、真实GPU模型、真实前台OCR和流式气泡。首轮36项通过：冷模型加载期间带着打开的设置窗口点击测试，6.420秒首字、7.143秒完成，原文“唔……主人刚才改了好多代码诶！累不累呀？”；无效HWND注入证实读取错误不产生气泡，下一次原文“唔！主人在修代码呢！那个设置窗口还没关就暂停说话……好奇怪诶，鱼鱼不懂为什么要绕这一步呢？”成功恢复。随机范围每档1000次、旧配置迁移、六个菜单项、两个尺寸端点与屏幕边界通过。本轮未修改公开文档、人设或GitHub，后续需补充C盘真实安装验收。
2026-10-14安装后验收补充：标准自包含0.4.0-preview.2 OCR安装器覆盖C盘，主exe/DLL/runtimeconfig SHA256与payload匹配，模型大小及修改日期不变，preferences安装前后SHA256相同。C盘测试模式保留设置窗口打开，speech-test实际模型生成6918.926ms首字、8698.695ms完成，原文“咦？主人刚才在纠结为啥要设计逻辑诶，看起来有点困惑呢！”。随后Computer Use实际右键桌宠菜单点击“吐字测试”，圈立刻可见；5193.402ms首字、6881.257ms完成，实际窗口无障碍文本及截图都显示“哇！主人刚才那个测试居然一次就通过了，好厉害诶！”。这不是仅检查任务返回，气泡确实显示实际4B输出。最终Runner18项通过，36项真实WPF/模型/范围/缩放/失败恢复检查通过；git diff --check通过。测试模式正常退出后恢复C盘普通OCR PID38688，模型PID32084，health ok；.NET从安装目录加载。设置窗口已关闭，用户85%缩放、三分钟下限（新3–5档）、地区/供电/GPU/其他设置保留。无Git提交/推送、无公开文档修改。
2026-10-14本地0.4.0-preview.3：拖拽反馈仅解码原视频1.5–6.0秒（108帧），第一帧即悬空，播放完保持最后悬空姿态，松手恢复待机；不重编码或改变原文件。Greeting池行礼（启动/关闭）播放速度1.5x，原241帧全播放，时长约6.7秒；普通动画仍24fps原速，移动进度使用原素材时间。右键角色菜单增加“隐藏桌宠”，调用同一HideCompanion，托盘显示恢复原路径。

用户追加预设问候，最终明确一个ASCII波浪线：启动时固定“主人早上/中午/下午/晚上好，鱼鱼给主人请安啦~”，实际按每次启动的系统本地小时选择：05≤h<11早上、11≤h<14中午、14≤h<18下午、其余晚上；关闭固定“主人再见~鱼鱼喜欢主人哦”。直接显示，不依赖模型开关/驻留/推理；问候气泡持续由行礼生命周期控制，启动完整行礼结束收起，关闭随行礼窗口退出，普通气泡驻留设置不决定其时长。额外鼠标动作中断行礼时问候也收起，不隐藏随后产生的模型气泡。

AnimationUiVerification实窗56项通过：模型关闭的即时欢迎、8个小时边界、行礼完成后收起、完整原帧1.5x启动/退出时长、悬空段起点1.5和终点<6秒/108帧、持续抓取保留姿态/松手待机、移动往返、50%和200%缩放、右键隐藏与恢复、单波浪线告别。桌面只保留D:\31273\Desktop\蓝色大肥鱼.lnk（C盘当前安装）；移除的是旧“蓝色大肥鱼-台词填写.lnk”，其页面与用户30条台词数据未删除。后续补C盘安装实际验收，未改公开文本或上传GitHub。
0.4.0-preview.3 C盘验收完成：标准安装器更新成功，主DLL/exe SHA256与本轮payload吻合；preferences SHA256安装前后相同（81A77F...），模型2708804480字节及2026-10-07修改时间不变。实际C盘启动诊断在模型“等待说话时启动”时已显示“主人晚上好，鱼鱼给主人请安啦~”；随后观察欢迎气泡随Startup结束收起。实际关闭阶段Computer Use截图显示“主人再见~鱼鱼喜欢主人哦”与行礼同时存在，之后进程正常退出。测试模式已退出，最终C盘普通OCR PID34700，模型health ok，.NET组件从安装目录加载，唯一桌面蓝色大肥鱼.lnk正确指向该安装。未更新GitHub/公开文档。


## 27. 0.4.0 公开实现契约（2026-10-14）

本节把 0.4.0 的当前源码行为集中记录，作为 README 之外的开发和接手依据。前面的历史章节保留旧版本的设计、故障和验证证据；遇到冲突时，以当前源码、`Version.props` 和本节的现行契约为准。

### 27.1 版本与发行路线

根目录 `Version.props` 的唯一版本值为 `0.4.0`。主程序、Core、Interface、OCR 发布物和安装器均从该值生成产品版本、文件版本和 Release 文件名。0.4.0 公开 Release 提供 OCR 安装器；Vision 输入代码和构建入口保留在源码中，作为历史路线和维护对照，不随 0.4.0 公开更新。

仓库提交包含源码、动画、测试、构建脚本、许可和本公开规格；模型权重、用户设置、天气缓存、运行时二进制和安装器生成物不进入 Git 跟踪。GitHub Release 单独附带经过独立性核验的自包含 OCR 安装器及其 SHA-256 文件。

### 27.2 鱼娘身份与模型吐字

对外文档把角色称为“鱼娘”或“鲸鱼娘”。角色面向用户称“主人”，默认使用亲近、活泼、可爱、带少女感的中文短句。0.4.0 的人设调校目标是让模型保留灵气和变化，根据当前屏幕资料、天气和科技新闻组织新的表达；应用不会用固定台词冒充模型生成。内置提示词属于模型推理实现，和对外文档的人设称呼分开维护。

每次自动说话或吐字测试都是一次独立生成，不保留连续对话历史。生成前显示角色头旁的加载转圈，模型返回后沿正常逐字显示路径呈现气泡；测试和自动说话共用同一套请求、等待、清洗、显示和驻留流程。设置窗口保持打开时，吐字测试仍可发起，窗口不会因为测试自动关闭。

### 27.3 对话触发时间

定时说话的设置选项为六档：2–3 分钟、3–5 分钟、5–8 分钟、10–16 分钟、15–23 分钟和 20–30 分钟。下一次触发时间在所选档位内持续均匀随机采样；自动说话、手动天气播报结束后的重排、设置切换、恢复显示和电源事件使用同一组范围。旧 `SpeechMinMinutes` 数值读取时映射到相应新档位，以便保留已有设置。

模型正在加载、推理忙碌、应用关闭、桌宠隐藏或全屏规则阻止显示时，不会把旧结果覆盖到新的气泡；请求门和取消逻辑负责防止重复生成和过期回调。启动欢迎词和关闭告别词不等待模型，分别随启动/退出行礼生命周期显示。

### 27.4 尺寸与动画

桌宠缩放范围为 50%–200%，设置界面以 5% 为步进并保存到 preferences。贴边、气泡位置和显示器工作区计算使用缩放后的角色可见区域，应用重新显示或 DPI 改变时重新 clamp。

动画资源来自当前透明视频动作包。无新交互打断时，普通动画按原始时长播放完整，再进入下一动作。拖拽时直接从悬空段开始，使用素材的中间 1.5–6.0 秒区间，松手后恢复待机；行礼动作使用完整素材并以 1.5 倍速度播放。右键角色菜单提供“隐藏桌宠”，隐藏和托盘恢复走同一生命周期入口。

### 27.5 欢迎词、告别词与系统时间

启动欢迎词由本地系统小时选择：05:00–10:59 为“主人早上好，鱼鱼给主人请安啦~”，11:00–13:59 为“主人中午好，鱼鱼给主人请安啦~”，14:00–17:59 为“主人下午好，鱼鱼给主人请安啦~”，其余时间为“主人晚上好，鱼鱼给主人请安啦~”。关闭时固定显示“主人再见~鱼鱼喜欢主人哦”。两者使用一个 ASCII 波浪线，气泡寿命由对应行礼动作控制，不受普通模型驻留设置影响。

### 27.6 OCR 输入和前台窗口

0.4.0 OCR 路线读取当前前台窗口的可见文字，在本机完成窗口识别、文字布局整理和上下文拼接，然后只把整理后的文本资料交给本地模型。桌面 Shell、最小化、隐藏和 cloaked 窗口具有单独的识别路径；当前窗口资料、天气和新闻字段在请求中按固定边界区分。

ChatGPT 桌面页面存在一种已知的内容误判：当聊天正文描述桌面或代码时，小模型可能把聊天讨论的对象当成正在使用的界面。这一现象在 0.4.0 未继续增加针对 ChatGPT 的特殊分支，以避免把普通窗口识别逻辑变成应用名硬编码；其他网页和办公类窗口继续使用通用 OCR 路线。

### 27.7 构建、安装与接手

开发者需要 Windows x64、.NET 10 SDK、已核对的 llama.cpp 运行时和模型下载来源。`code/build.ps1 -Locked -InputMode Ocr` 生成 OCR 自包含应用，`tools/Build-Installer.ps1 -InputMode Ocr -RuntimeDirectory <目录>` 生成 OCR 安装器。安装器内置 .NET、VC 运行库、llama.cpp 和应用依赖，安装过程中按固定来源下载模型；更新同一目录时先校验已有模型，校验通过则复用，不重复下载。

安装器把应用、独立卸载器、动画和运行时放在蓝色大肥鱼自己的安装子目录，为该子目录授予普通用户写入权限，并在 Windows 中登记开始菜单、App Paths 和卸载入口。卸载器默认删除程序、缓存和模型；勾选“保留模型和设置”时保留对应数据。

验证应覆盖源码测试、真实自包含发布物、包内 .NET/VC/llama.cpp 模块路径、安装器生命周期、模型复用、默认卸载和保留卸载。通过本地编译或单元测试不能替代真实 Release 附件的入口检查；每次公开 Release 都要重新核对附件哈希和包内依赖。

## 28. 0.4.1 独立文字气泡大小（2026-10-14，本地开发）

### 28.1 基线与设置契约

用户已否决此前的 0.4.1 对话优化试验，其源码、测试和文档改动已撤回到 `v0.4.0`；该试验不作为本轮基础。新 0.4.1 增加气泡尺寸独立调节，并修复设置窗口前台激活；OCR、消息拼接、人设提示、模型参数、生成与流式吐字使用 0.4.0 实现。

`CompanionPreferences.BubbleScale` 独立于角色 `Scale`，默认 1，范围 0.5–2.0；旧设置没有该字段时使用 1，读取越界值时 clamp，非有限值回退 1。设置页“说话与天气”新增“文字气泡大小”滑块，以 0.05 为步进，修改后立即更新已有气泡并保存。调整不取消或重放正在显示的文字，不重置气泡驻留倒计时。

### 28.2 布局与实现

`ApplyCompanionBubbleScale` 在气泡根 Border 上设置统一 `LayoutTransform`，文字、字距、内边距、圆角、描边、尖角和关闭按钮整体缩放；角色动画与加载圈不随此设置变化。欢迎词、告别词、普通模型气泡和系统提示共用该气泡。`CompanionEdgeLayout.Follow.Update` 按未缩放单位测量完整文本，最大基础宽度 300 DIP，再按 `BubbleScale` 换算 popup 的桌面 DIP 宽高与坐标；工作区可用宽高先除以缩放比，防止放大后越界。尖角目标坐标除以相同缩放比后交给 `PointAt`，保持朝向角色。

角色缩放、移动、DPI 和显示器变化继续走既有布局刷新，气泡缩放不写入角色尺寸。气泡仍是独立且不激活的 owned Window，沿用全文测量与文字变化订阅。极小工作区和异常长文本的高度上限沿用既有规则，不增加滚动或改变吐字内容。

### 28.3 验收入口

`AnimationUiVerification <隔离目录> --bubble-scale` 使用生产 WPF 主窗、设置控件和气泡，验证旧配置默认值、滑块范围/步进、即时更新、持久化重读、四档比例、角色尺寸独立、三种角色尺寸×三种气泡尺寸×四角工作区限位和完整文本布局，以及关闭按钮与设置读取的上下限。测试中的固定文字仅为布局夹具，不是模型测试。完成记录与 C 盘同步结果在实际验收后补充。

2026-10-14 本轮实际验证：专用 WPF 回归 131 项通过，覆盖 50%/100%/135%/200% 的同比例缩放与保存重读，以及角色/气泡三档组合的 36 个四角布局；发现半尺寸实际排布宽度取整可使边界词多换一行，二次全文测量增加 2 基础 DIP 余量后全部通过。真实显示器/DPI使用本机当前环境，没有穷举混合 DPI 拓扑。字体、配色、圆角和尖角保持 0.4.0 样式。

标准自包含 OCR 安装器 569 项内容/依赖/解包/数据保留检查通过，生产 ExtractApplication/FinishInstallation 已同步 C 盘。更新瞬间 preferences 哈希未变，模型 SHA256 仍为 `8485535a36c9f333574d08b650ad698ac02ec30752bd9cd87e493a3b7531bee1`，大小及 2026-10-07 修改时间不变。C 盘关键 exe/DLL/runtimeconfig/WinRT 文件哈希与本轮 payload 相同；实际安装版设置页通过 Computer Use 点击进入并将气泡调为150%，UI与JSON均显示1.5，角色尺寸保持0.8，随后恢复100%。已关闭设置、退出诊断模式、清除本轮 test-status/test-command 并恢复普通启动。CompanionBrain/CompanionDialogue 与 v0.4.0 无差异，没有沿用已否决的对话试验，没有 Git 提交或 GitHub 上传。

### 28.4 设置窗口前台激活修复

设置窗口为独立普通 Window，显式 ShowActivated=true、ShowInTaskbar=true、Topmost=false，Owner不绑定置顶桌宠。首次打开及重新唤出均调用 `ActivateCompanionSettings`：如果最小化则恢复 Normal，其他状态（含最大化）保留，Show/Activate后在 Dispatcher ContextIdle 再执行一次短暂的 Topmost=true→false 置前和激活，避开角色/托盘菜单关闭时恢复旧前台的时序。该操作仅发生于用户打开设置，不在 Tick、Deactivated 或后台定时器中重复；完成后立即回到普通非置顶层级，其他应用仍可正常覆盖设置。

本次针对设置窗口修改，不更改桌宠主窗的 Topmost、WS_EX_NOACTIVATE、WS_EX_TOOLWINDOW、WM_MOUSEACTIVATE 和气泡不抢焦点样式。仅重试 Activate 的候选未通过真实窗口菜单焦点恢复回归，因此没有采用。

`AnimationUiVerification <隔离目录> --settings-focus` 验证真实 WPF/Win32 前台窗口句柄：普通样例窗在前台时经生产角色菜单回调打开设置，在 Input 优先级模拟菜单后置焦点恢复，再确认设置获得前台；覆盖普通窗口切换、重开同一设置、最小化恢复、最大化保留、设置无 NOACTIVATE、不绑定宠物Owner，以及桌宠/气泡仍置顶且不抢前台和关闭后延迟回调失效。本机14项通过；这些属于实窗自动回归，菜单项以WPF事件触发，不等同于真实鼠标逐项点击。安装后验收另补。

同日安装后验收：本轮安装器569项通过，调用生产更新覆盖C盘，应用exe/DLL/runtimeconfig与本轮payload哈希一致，更新瞬间preferences哈希未变。安装版经诊断settings命令走正常OpenCompanionSettings，未先通过Computer Use激活；实际截图已显示设置页，test-status报告前台HWND3149220等于设置窗口句柄，宠物句柄8651504保持独立。随后Computer Use真实点击最小化按钮，再次触发同一入口，设置自行恢复且前台仍为3149220，截图再次确认显示。验收后关闭设置、正常退出诊断实例、清除本轮test-status/test-command，恢复C盘普通0.4.1运行；未提交或上传GitHub。

## 29. 0.4.1 清理未使用的 VPet 实现（2026-10-14）

### 29.1 回退基线与清理范围

用户明确要求谨慎清理、保持可回滚。清理前先提交 `3c007f125c1c0e2a1410d9d157707ec45294ae3e`，包含已验收的气泡独立缩放与设置前台修复；该提交是本次清理的完整源码回退点。没有复制安装目录或模型作为备份，没有上传GitHub。用户的未跟踪 `durations.json` 不参与清理或提交。

依赖盘点确认Companion目录中的动画、模型、OCR、天气、菜单、设置、气泡和Win32代码不调用旧引擎；遗留引用仅为无效的Setting初始化、ZoomLevel写入和空引擎Dispose。删除这些调用和using后，将MainWindow基类由Panuon WindowX改为原生WPF Window，保留InitializeCompanion生命周期。App只保留现行工作目录、Args及本地异常日志，不保留旧游戏启动和异常报告分支。

删除未使用的Core、Windows.Interface工程，旧MainWindow分部、Function、WinDesign、MutiPlayer、PetHelper，旧养成/物品/成就/图库/联机/Steam/创意工坊代码、Steam本机DLL和富状态文件、旧PNG界面素材与字体，以及仅针对旧恢复版的RecoveryVerification。按Git跟踪文件逐个删除，共222个文件；不递归删除目录，不接触未跟踪文件、构建输出或目录链接。历史来源记录和Apache-2.0等必要署名、当前动画与字体许可保留。

### 29.2 当前工程及依赖

解决方案只含 `VPet-Simulator.Windows` 一个原生WPF主工程，继续支持Windows x64的OCR及历史Vision编译选择，不重命名程序集、命名空间、可执行文件、安装注册入口或单实例Mutex。主工程不再引用Core/Interface或Facepunch、LinePutScript、Panuon、NAudio、SkiaSharp、WpfAnimatedGif等NuGet包；Windows/.NET自带WPF、WinForms托盘、Windows OCR投影和外部FFmpeg/llama.cpp仍按原方式使用。重新生成主工程两种路线及保留测试工程的锁文件。

106段透明WebM、动画配置、乐米小奶泡体、图标、地区数据、模型请求/提示词/流式显示/供电逻辑均保留。卸载器和安装器未更换架构，仍由installation-manifest.json管理应用文件，更新时删除旧清单中不再属于新payload的Core、Interface、Steam和其他旧依赖，保留models/preferences/cache。InstallerVerification新增断言确保新payload不携带被移除组件。

`code/test.ps1`只运行当前产品回归，不再提供LegacyRecovery分支；保留动画UI、权重、Runner故障恢复、天气缓存、前台捕获、OCR布局、真实OCR/模型及电源提示验证。run.ps1默认借用当前C盘安装的runtime/models；生产源代码不引用安装目录中的应用DLL。

### 29.3 清理阶段验证

移除后OCR与Vision均locked编译通过，0警告/0错误；完整动画/设置实窗56项通过，包括原帧率及透明点击、三页设置、缩放、拖拽悬空段、往返移动、离电提示、隐藏恢复及欢迎/告别完整退出。动画权重2326项、Runner/消息字段21项、天气缓存21项通过。Runner该组使用故障夹具，不当作真实模型验证。菜单改动与最终自包含安装验收随后另记。

清理阶段单独提交为 `217ccff`，菜单改动另行提交，便于分别revert；Git历史保留完整旧文件。随后额外删除未使用的SendKeys App.config。未移除当前字体、106段动画、地区数据、模型/视觉投影配置或安装/卸载生命周期。

### 29.4 右键菜单与托盘外观

`CompanionMenuStyles.xaml`仅用于角色ContextMenu，不污染App全局默认样式。白色面板、#DCE5F2边框、#31405E字色、#EDF3FC/#DCEAFF高亮沿用设置配色；圆角12、轻阴影、字体14DIP、行高至少38DIP，图标为系统Segoe MDL2 Assets矢量字符。保留设置、天气、吐字测试、隐藏桌宠四个原菜单项及其原回调，不新增页面或更改模型策略。

托盘保留WinForms NotifyIcon/ContextMenuStrip的原生输入、关闭和键盘处理；`CompanionTrayMenuRenderer`只自定义配色与圆角高亮绘制，保留显示桌宠、设置、天气、吐字测试、关闭桌宠五个命令。没有引入Electron或新的UI依赖。菜单绘制器恢复Graphics原抗锯齿状态，临时画刷和几何对象及时Dispose。

专用 `AnimationUiVerification <隔离目录> --menus` 15项实际WPF/WinForms检查通过：检查控件模板、行高/图标/不重叠，调用四个生产菜单回调确认设置显示、天气/测试的关闭模型状态、隐藏与恢复，以及托盘项和可见文本/内边距。预览来自实际生产控件渲染，保存在测试沙盒；不是AI生成图。WinForms内部绘制面可以延伸入隐藏的图像栏，因此托盘检查用可见文字和内边距，不将不可见绘制面误认为文字被裁。

清理后真实本机GPU模型通过36项共享吐字回归：冷加载期间设置保持打开、重复请求合并、真实OCR与气泡、无效前台句柄失败不显示调试台词，以及后续实际生成恢复。输入为当时真实前台内容，两次原始输出均围绕主人清理旧代码；不把语义联想视为程序结构的判据。本次未改模型提示词和参数。测试工具修正双缩放滑块定位、使用与C盘相同的离电允许运行策略、在App构造前解析沙盒绝对路径，并排除欢迎词被计为模型首字。最终构建及安装结果后补。

### 29.5 最终构建及 C 盘验收

最终OCR/Vision主工程locked构建均0警告/0错误；气泡缩放131项、设置前台14项、菜单15项、前台捕获15项、OCR布局8项通过。对应前述清理阶段的完整动画UI56项、动作规则2326项、Runner21项、天气21项，以及最终真实GPU吐字36项，分别记录各自范围，不相加为一次测试。最终OCR自包含安装器557项通过（含12项旧组件缺席断言）；检查数比旧包减少源于已移除程序集，不意味着跳过文件哈希检查。

使用生产ExtractApplication/FinishInstallation覆盖C盘，关键exe/DLL/runtimeconfig/WinRT哈希与最终payload一致；更新瞬间preferences哈希未变，模型SHA256、2708804480字节大小和2026-10-07修改时间均保持。安装目录确认已删除旧Core/Interface、Steam、Panuon、LinePutScript、NAudio、SkiaSharp、WpfAnimatedGif组件，未移动数据目录。新安装器314963981字节，清理前同版本设置修复包349301394字节；没有重压模型或改变推理依赖。

安装版测试模式下使用既有inspectable命令便于Computer Use发现窗口（只临时显示任务栏入口/去除ToolWindow位），真实右键角色图像后，新菜单的四项文字、图标、圆角和高亮在截图/无障碍树中可见。菜单弹出超出父窗边界时，自动化点击“设置”遭坐标限制，未将该点击记作通过；菜单功能依据上述15项生产控件回调测试。随后恢复normal-style、正常退出诊断实例，按本轮进程启动时间清理诊断文件，重新普通启动，Computer Use已reset。最终0.4.1运行于C盘，health=ok，.NET/VC/Vulkan模块均从安装目录加载；没有源码提示词或模型配置改动，没有上传GitHub。

### 29.6 菜单外部点击收起

用户实测非前台桌宠菜单仅在点击本窗空白时关闭。Windows限制后台窗口的鼠标捕获范围，不能只依赖WPF ContextMenu的默认捕获。新增CompanionMenuDismissal，菜单Opened时安装WH_MOUSE_LL，Closed及程序退出时释放；只处理左/右/中/侧键按下，移动/滚轮不处理。按下点和menu.PointToScreen计算的真实物理像素边界比较，内部点击交给菜单，外部点击关闭菜单。

原生回调不拦截事件，始终CallNextHookEx；回调安装于UI线程时先同步关闭菜单、释放捕获，再交还该次输入，避免第一下外部点击只被旧捕获吃掉。只做两次坐标变换和关闭，不执行布局、文件IO或模型操作。若回调不在UI线程则通过Dispatcher Input优先级处理，使用会话代次防止旧回调关闭重新打开的菜单。主窗口WS_EX_NOACTIVATE/WM_MOUSEACTIVATE及常态前台逻辑不变，无需把桌宠激活或永久注册全局监听。

专用AnimationUiVerification --menu-dismiss中，Computer Use真实点击位于桌宠和菜单以外的样例按钮，第一下点击到达按钮且菜单关闭，监听随之移除，7项检查通过；曾尝试仅异步关闭，外部目标未收到第一次点击，因此改为上述同步释放捕获。--menus增加内部点不关闭、外部点关闭、挂接/释放/重开检查。正式安装结果随后补记。

本轮--menus共19项通过，主工程OCR构建0警告/0错误。新自包含OCR安装器557项检查通过，以生产更新器覆盖C盘；应用exe/DLL/runtimeconfig与最终payload哈希一致，更新前后preferences和GGUF哈希一致。随后直接以普通参数启动0.4.1，模型health=ok，未进入C盘诊断模式；Computer Use内核已reset。此次不改变版本号、不上传GitHub，菜单修复以独立本地提交保留回退路径。
