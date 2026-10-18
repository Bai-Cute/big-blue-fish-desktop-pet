# 开发说明

使用说明见仓库根目录 [README](../README.md)，发布说明见 [release/README](../release/README.md)。

MCP 的连接配置、工具字段、动作与文字调度、事件消费及错误处理见 [MCP 接入参考](../docs/MCP.md)；共享协议代码在 `BigBlueFish.Control/`，stdio 桥接入口在 `BigBlueFish.Mcp/`，平台执行器位于 Windows 的 `CompanionMcp.cs` 与 Linux 的 `LinuxMcp.cs`。

## 编译与运行

需要 Windows x64、.NET 10 SDK，以及第一次恢复 NuGet 依赖时的网络连接。

```powershell
./build.ps1 -Locked -InputMode Ocr
./run.ps1 -InputMode Ocr -RuntimeDirectory "推理运行时目录" -ModelsDirectory "GGUF 模型目录"
```

默认可复用本机 `C:\Program Files\蓝色大肥鱼` 的运行依赖，也支持 `BLUE_WHALE_RUNTIME`、`BLUE_WHALE_MODELS`、`BLUE_WHALE_DOTNET` 环境变量。运行脚本通过目录连接复用模型，不把模型提交进仓库。SDK 可用 `-SdkPath` 指定。

准备内置 4B 本地模型时，可以运行以下任一种命令。`DestinationDirectory` 是模型准备目录，脚本会在其下创建 `models` 并保存 `model-selection.json`；两个来源的模型有所不同，均适合本应用。

```powershell
../tools/Setup-Model.ps1 -Source HuggingFace -DestinationDirectory "D:\BigBlueFish-local"
../tools/Setup-Model.ps1 -Source ModelScope -DestinationDirectory "D:\BigBlueFish-local"
../tools/Setup-Model.ps1 -Source Manual -ModelFile "D:\下载\我的模型.gguf" -DestinationDirectory "D:\BigBlueFish-local"
```

Hugging Face 为推荐来源，ModelScope 为国内稳定来源。手动安装保留文件的实际名称，检查 GGUF 文件头；SHA-256 不属于两个预设模型时，提示确认后仍可使用。自动化调用可用 `-AllowUnverifiedModel` 明确接受这一提示。OCR 模型准备只下载主模型。

源码启动读取模型目录上一级的 `model-selection.json`，按选择记录复用实际文件名，也能识别两个来源的标准文件名。以上述目录为例，运行 `./run.ps1 -InputMode Ocr -ModelsDirectory "D:\BigBlueFish-local\models" -RuntimeDirectory "推理运行时目录"`。切换目录时，脚本会更新构建目录中的连接，保留原模型目录和设置。

无模型启动使用 `./run.ps1 -InputMode Ocr -NoModel`，此时无需模型文件或推理运行时。也可先运行 `../tools/Setup-Model.ps1 -Source None -DestinationDirectory "D:\BigBlueFish-local"` 保存无模型选择，再用对应的 `-ModelsDirectory` 启动。内置模型可以暂不使用，欢迎与告别、动画和鼠标交互照常运行。

OCR 版生成目录为 `BigBlueFish.Windows/bin/x64/Release/net10.0-windows10.0.26100.0/`；视觉版为 `BigBlueFish.Windows/bin/x64/Release/net10.0-windows7.0/`。主程序 `BigBlueFish.exe` 与 `BigBlueFish.dll` 从源码生成。OCR 版依赖在 `packages.lock.json` 中锁定，视觉版使用 `packages.vision.lock.json`。

## 修改入口

| 文件 | 功能 |
| --- | --- |
| `BigBlueFish.Windows/Companion/CompanionWindow.cs` | 生命周期、拖动、动画、设置和电源事件 |
| `BigBlueFish.Windows/Companion/CompanionMenus.cs` | 右键菜单、三级地点选择和手动天气播报 |
| `BigBlueFish.Windows/Companion/CompanionPreferences.cs` | 设置默认值、校验和原子保存 |
| `BigBlueFish.Windows/Companion/CompanionRegions.cs` | 省市区层级与定位 |
| `BigBlueFish.Windows/Companion/CompanionWeather.cs` | 天气请求、天气代码中文映射和按地点缓存 |
| `BigBlueFish.Windows/Companion/CompanionBrain.cs` | 模型启动、推理、电池策略和提示词 |
| `BigBlueFish.Windows/Companion/CompanionSpeech.cs` | 所有模型回复的逐字显示、Unicode 字符边界、替换和取消 |
| `BigBlueFish.Windows/Companion/CompanionEdgeLayout.cs` | 贴边、独立气泡窗口和 DPI 换算 |
| `BigBlueFish.Windows/Companion/CompanionNews.cs` | 新闻概率、RSS标题和摘要整理 |
| `BigBlueFish.Windows/Companion/CompanionVideoPlayer.cs` | 透明视频解码、帧计时与播放 |
| `BigBlueFish.Windows/assets/regions/` | 地区数据、来源版本和输入 SHA256 |
| `../deployment/` | 主程序、安装器和卸载器共用的模型准备、下载、更新与现代界面 |
| `BigBlueFish.Windows/Companion/CompanionMaintenance.cs` | 设置中安装模型、检查更新与更新提示 |
| `tests/InstallerVerification/` | 下载故障、模型安装交互、更新事务和完整安装卸载验证 |
| `tests/RunnerVerification/` | 当前提示词、真实子进程故障恢复及本地模型验证 |

地区数据由 MIT 许可的 [AreaCity](https://github.com/xiangyuecn/AreaCity-JsSpider-StatsGov) `2025.251231.260403` 版本生成，包含 34 个省级项、392 个市级项、3209 个县区级项。上游部分台湾地区没有坐标；保留选择，查询时明确提示。

更新时下载对应的 `ok_data_level3.csv`、`ok_geo.csv`，运行：

```powershell
python ./tools/import-regions.py "ok_data_level3.csv" "ok_geo.csv"
```

脚本只保留区划和中心点；大陆中心点由 GCJ-02 近似转换为 WGS84。更换版本时同步更新脚本中的版本、提交和许可信息。

## 验证与发布

先退出桌宠，再运行：

```powershell
./test.ps1 -InstalledDirectory "C:\Program Files\蓝色大肥鱼" -InputMode Ocr
../tools/Build-Installer.ps1 -InputMode Ocr -RuntimeDirectory "已核对的运行时目录"
```

默认测试包含 Runner 故障恢复与当前提示词验证、动画权重和日期规则、真实前台窗口捕获、WPF 动画与设置界面验证。实窗测试使用 `.verification/` 的独立设置，复用参考安装目录的模型和运行时。安装器验证项目的 `--lifecycle` 检查普通用户写入权限、Windows 注册、快捷方式和两种数据保留方式的卸载；`--ui-layout <目录>` 检查安装、卸载、模型与更新窗口布局；`--deployment <目录>` 检查模型选择、下载恢复、更新元数据与失败恢复；`--model-ui <目录>` 验证下载失败出口与手动安装确认。

发布脚本生成 `BigBlueFish-v1.0.1-OCR-Setup.exe` 一键安装器；Vision 构建入口仍保留用于历史维护，不随当前公开 Release 同步发布。安装器携带包含 .NET 运行时的 Windows x64 程序，以及已核对的 llama.cpp `b10809` Vulkan 运行时；模型可从 Hugging Face 或 ModelScope 固定来源下载并校验，也支持手动安装或无模型启动。模型选择记录在安装目录的 model-selection.json。


根目录 `Version.props` 统一管理项目版本。主程序、安装器和卸载器的产品版本由它生成；Windows 文件版本采用对应的四段格式。构建脚本默认读取该文件，也可以用 `-Version` 指定构建版本。

项目提供 Vision 与 Ocr 两种输入路线，当前源码版本由根目录 `Version.props` 管理，默认构建 Ocr，通过 `-InputMode Vision` 构建视觉版。Vision 直接把前台截图交给多模态模型；Ocr 识别前台窗口文字，按位置组织为文本再交给本地模型。`tools/Build-LocalVariant.ps1 -InputMode Ocr -SdkPath <SDK绝对路径>` 生成自包含应用，改成 Vision 即可构建视觉版；`code/build.ps1` 和 `code/run.ps1` 也接受 `-InputMode`。

## Linux 与无MCP版

当前版本为 1.0.1，Windows 和 Linux 都提供标准版与无MCP版。无MCP版使用 `CompanionEdition=Safe` 编译，排除控制服务、工具执行器和对话输入窗口，安装包不分发 MCP 桥接程序及 Control 程序集；`Safe` 是构建和安装包文件名中的版本类型标识。

Windows 安装器运行 `../tools/Build-Installer.ps1 -Edition Standard -RuntimeDirectory "Windows运行时目录"`，将 `Standard` 改为 `Safe` 可生成无MCP版。直接构建 Windows 无MCP版使用 `dotnet build ./BigBlueFish.Windows/BigBlueFish.Windows.csproj -c Release -p:Platform=x64 -p:CompanionEdition=Safe -p:RestoreLockedMode=true`。

Linux 使用 Avalonia、FFmpeg 与 Tesseract。需要 .NET 10 SDK，以及含 gcc、pkg-config、libdbus-1-dev、dpkg-deb 和 FFmpeg 的 WSL Ubuntu／Debian 构建环境。运行 `../tools/Build-LinuxPackage.ps1 -Distribution "WSL发行版名称" -RuntimeDirectory "Linux llama.cpp运行时目录" -Edition Standard`，同样可用 `-Edition Safe`。构建输出为自包含 .NET 的 linux-x64 程序及 `.deb` 包，系统依赖由 apt 安装。

Linux 主入口在 `BigBlueFish.Linux/`，跨平台控制协议在 `BigBlueFish.Control/`，stdio 桥接在 `BigBlueFish.Mcp/`。共享项目分别锁定 Windows 和 Linux 的 RID 依赖；Windows 无MCP版另用 `packages.safe.lock.json`。Linux 的用户数据目录、X11 截图、Wayland 门户及更新行为详见[当前行为规格](../docs/AGENT_FUNCTIONAL_SPEC.md)。

`tests/McpProtocolVerification/` 验证真实 stdio 桥接与协议，`tests/McpUiVerification/` 验证 Windows MCP、窗口焦点、确认交互与拖动，`tests/LinuxVerification/` 验证 Linux 的实际窗口与 MCP；`tests/SafeEditionVerification/` 检查无MCP版的排除结果。测试需分别使用对应平台及版本类型构建。
