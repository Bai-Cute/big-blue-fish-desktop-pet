# 开发说明

使用说明见仓库根目录 [README](../README.md)，发布说明见 [release/README](../release/README.md)。

## 编译与运行

需要 Windows x64、.NET 10 SDK，以及第一次恢复 NuGet 依赖时的网络连接。

```powershell
./build.ps1 -Locked
./run.ps1 -RuntimeDirectory "推理运行时目录" -ModelsDirectory "GGUF 模型目录"
```

默认可复用本机 `D:\Program Files\蓝色大肥鱼` 的运行依赖，也支持 `BLUE_WHALE_RUNTIME`、`BLUE_WHALE_MODELS`、`BLUE_WHALE_DOTNET` 环境变量。运行脚本通过目录连接复用模型，不把模型提交进仓库。SDK 可用 `-SdkPath` 指定。

生成目录为 `VPet-Simulator.Windows/bin/x64/Release/net10.0-windows7.0/`。三份应用 DLL 均从源码生成，安装 DLL 不作为构建引用。依赖在 `packages.lock.json` 中锁定。

## 修改入口

| 文件 | 功能 |
| --- | --- |
| `VPet-Simulator.Windows/Companion/CompanionWindow.cs` | 生命周期、拖动、动画、设置和电源事件 |
| `VPet-Simulator.Windows/Companion/CompanionMenus.cs` | 右键菜单、三级地点选择和手动天气播报 |
| `VPet-Simulator.Windows/Companion/CompanionPreferences.cs` | 设置默认值、校验和原子保存 |
| `VPet-Simulator.Windows/Companion/CompanionRegions.cs` | 省市区层级与定位 |
| `VPet-Simulator.Windows/Companion/CompanionWeather.cs` | 天气请求、天气代码中文映射和按地点缓存 |
| `VPet-Simulator.Windows/Companion/CompanionBrain.cs` | 模型启动、推理、电池策略和提示词 |
| `VPet-Simulator.Windows/Companion/CompanionSpeech.cs` | 所有模型回复的逐字显示、Unicode 字符边界、替换和取消 |
| `VPet-Simulator.Windows/Companion/CompanionEdgeLayout.cs` | 贴边、独立气泡窗口和 DPI 换算 |
| `VPet-Simulator.Windows/Companion/CompanionNewsGate.cs` | 新闻概率、静默期和每日上限 |
| `VPet-Simulator.Core/Graph/PNGAnimation.cs` | 动画读取和蓝色渲染 |
| `VPet-Simulator.Windows/assets/regions/` | 地区数据、来源版本和输入 SHA256 |
| `tests/RecoveryVerification/` | 实窗布局、设置、天气缓存、模型和电源验证 |

地区数据由 MIT 许可的 [AreaCity](https://github.com/xiangyuecn/AreaCity-JsSpider-StatsGov) `2025.251231.260403` 版本生成，包含 34 个省级项、392 个市级项、3209 个县区级项。上游部分台湾地区没有坐标；保留选择，查询时明确提示。区划为固定版本快照，不保证后续行政调整实时同步。

更新时下载对应的 `ok_data_level3.csv`、`ok_geo.csv`，运行：

```powershell
python ./tools/import-regions.py "ok_data_level3.csv" "ok_geo.csv"
```

脚本只保留区划和中心点；大陆中心点由 GCJ-02 近似转换为 WGS84。更换版本时同步更新脚本中的版本、提交和许可信息。

## 验证与发布

先退出桌宠，再运行：

```powershell
./test.ps1 -InstalledDirectory "参考安装目录"
../tools/Build-Release.ps1
```

实窗测试使用 `.verification/` 的独立设置，复用参考安装目录的模型和运行时，并比较原有提示词与文本清理行为。包含 24 组四角 / 缩放 / 长短文本布局、三级选择、菜单、天气缓存与失败恢复、两种电池策略和实际生成。

发布脚本生成带 .NET 运行时的 Windows x64 程序，获取固定 SHA256 的 llama.cpp `b10809` Vulkan 发布包。模型不放进发布 ZIP；用包内 `Setup-Model.ps1` 配置。

恢复基线见 `recovery-baseline.json`，原始 DLL 与动画校验见 `provenance.json`，当前验证见 `verification.json`。源码启用确定性构建并嵌入调试信息。升级依赖时更新锁文件并重跑相关测试。

没有穷举验证上游完整游戏、多人、Steam 或创意工坊功能；以定制桌宠实际入口为维护范围。
