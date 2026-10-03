# 蓝色大肥鱼

一只基于 VPet 的 Windows 桌面鲸鱼娘。使用本地模型主动说短句，也可以按所选地点用自然中文播报天气。

这是复原工程的独立改进副本。原安装版和原复原工程不作为修改目标。

## 功能

- 可拖动、缩放和贴边；独立文本气泡保持在屏幕内，可以手动关闭。
- 右键桌宠显示“设置”“天气”和“吐字测试”。天气和测试都调用本地模型，生成结果逐字显示，没有语音合成。
- 文字约每秒 28 个字符；气泡提前按全文留足空间，驻留计时从吐字完成后开始。
- 设置提供省级 / 市级 / 县区级三级联动下拉框，地点会保存并用于查询。
- “拔电时暂停并卸载模型”默认开启，关闭后允许电池供电时运行模型。
- 模型、GPU / CPU、说话间隔、气泡驻留、开机启动、应用感知和自动公开信息均可设置。

## 仓库结构

```text
code/       C# / XAML、动画、地区数据、依赖锁文件和测试
release/    本地发布包与发布说明；ZIP 作为 GitHub Release 附件
tools/      发布打包与模型配置脚本
README.md   项目说明（后续继续完善）
LICENSE     VPet 代码的 Apache-2.0 许可
```

## 使用

Windows 10 / 11 x64。完整解压 `release/` 下的发布 ZIP，运行 `Setup-Model.ps1` 配置模型，再启动 `VPet-Simulator.Windows.exe`。包内包含 .NET 10 运行时和 llama.cpp，不需开发 SDK。模型约 2.7 GB，也可以指定已有同型号文件。

具体说明见 [发布说明](release/README.md)。

天气来自 [Open-Meteo](https://open-meteo.com/)，按所选区县中心附近估算。自动公开信息可以关闭；主动点击“天气”仍会联网查询地点，由本地模型组织文本。失败时会提示，不编造天气或改用别的城市。

地区数据来自 [AreaCity](https://github.com/xiangyuecn/AreaCity-JsSpider-StatsGov)，为固定快照；部分台湾地区没有坐标，暂不能查询天气。

## 开发

需要 Windows 和 .NET 10 SDK：

```powershell
cd code
./build.ps1 -Locked
./run.ps1 -RuntimeDirectory "运行时目录" -ModelsDirectory "模型目录"
```

修改入口、数据更新、验证和依赖说明见 [开发说明](code/README.md)。

在仓库根目录生成发布包：

```powershell
./tools/Build-Release.ps1 -Version 0.2.2
```

已有同版本发布目录时，脚本停止，避免覆盖。

当前发布物为 `BlueWhale-v0.2.2-win-x64`。

## 准备上传 GitHub

代码、动画、地区数据、锁文件、脚本、说明和许可提交到 Git。`.gitignore` 排除模型、运行时、构建输出、设置、缓存、测试状态和发布二进制。

`release/` 的 ZIP 与 `.sha256` 可作为 GitHub Release 附件上传。当前只建立本地仓库，没有配置远端或上传。

## 来源和许可

底层来自 [LorisYounger/VPet](https://github.com/LorisYounger/VPet)，对应提交 `2e99a42ebeff71d792118f2e8de744b773042f8d`。定制代码从原始开发补丁恢复并用安装程序核对；贴边已整合为直接源码调用。

角色、推理运行时、模型、.NET、地区数据和天气的来源与许可见 [THIRD-PARTY](code/THIRD-PARTY.md) 和 [licenses](code/licenses/)。这是非官方定制桌宠。
