# 蓝色大肥鱼桌宠 / Big Blue Fish Desktop Pet

蓝色大肥鱼是一款住在电脑桌面上的 AI 桌宠软件，支持 Windows 与 Linux 双平台。她是一位蓝发的小鲸鱼娘，形象来自对 DeepSeek 的二创虚拟形象“蓝色大肥鱼”。软件内置 4B 本地大模型，借助 OCR 技术读懂主人正在桌面上做的事情，每隔一段时间就从文字气泡里送来一句可爱的话，陪伴和关心主人；她还会查看天气、浏览科技新闻，再用自己的方式讲给主人听。

鱼鱼准备了一百多种可爱的动作表情，平时会在桌面上呼吸、哼歌、伸懒腰、吃零食、玩耍，到了饭点会认真吃饭，到了节日会换上应景的小活动，被点一下、被拎起来时也有专门设计的反应。每次启动，她都会提起裙摆向主人行礼请安；关闭时，也会用同样的礼仪和主人道别。

当前版本为 **1.0.5**，安装包请前往 [GitHub Releases](https://github.com/Bai-Cute/big-blue-fish-desktop-pet/releases/latest) 下载。

<p align="center">
  <img src="docs/images/bigbluefish-rest-transparent.png" width="38%" alt="蓝色大肥鱼休息状态" />
  <img src="docs/images/bigbluefish-speaking-transparent.png" width="58%" alt="蓝色大肥鱼吐字状态" />
</p>

## 认识鱼鱼

鱼鱼有一头柔软的蓝色长发，头顶戴着蕾丝女仆发饰，耳边是小小的鲸鳍，身后摇着一条鲸鱼尾巴。她穿着深蓝色的女仆装，白色围裙上印着一只小鲸鱼，整个人看起来就像一位住进电脑里的小鲸鱼娘。她平时安静地待在桌面边缘，身边透明的地方可以直接点到后面的桌面，主人也可以把她拖到任何喜欢的位置，大小可以在 50% 到 200% 之间调整。

她说话的语气亲切温柔，又带着一点可爱俏皮。看见主人在写代码，她会摇摇尾巴为认真工作的主人加油；看见主人和朋友约明天吃饭，她会忍不住对好吃的动一点小心思；天气变冷或者主人忙了很久，她也会轻轻地送来一句关心。启动时，她会按照时间说“主人早上好，鱼鱼给主人请安啦～”；道别时，她会说“主人再见～鱼鱼喜欢主人哦”。

<p align="center">
  <img src="docs/images/bigbluefish-maid-bow.gif" width="32%" alt="女仆屈膝礼仪" />
  <img src="docs/images/bigbluefish-idle-breath.gif" width="32%" alt="待机呼吸休闲" />
  <img src="docs/images/bigbluefish-click-wave.gif" width="32%" alt="点击回应元气挥手" />
</p>

## 她怎样陪伴主人

鱼鱼会读取当前前台窗口的画面，通过 OCR 识别其中的文字，并按照标题栏、侧边栏、正文等区域整理好，再交给内置的 4B 本地模型理解。无论主人在读文章、写文档、改代码，还是在聊天里提到了什么话题，她都能顺着眼前的内容组织一小段新的话；主人停在桌面、主页或新标签页时，她也会自然地问候、撒娇或者碎碎念。Windows 使用系统自带的 Windows OCR，Linux 使用 Tesseract，X11 与 Wayland 桌面均可使用。

启动约 45 秒后，鱼鱼会第一次开口，之后在主人选择的时间范围内随机安排下一次说话，范围从 2–3 分钟到 20–30 分钟共六档。她会挑主人暂时停下键盘和鼠标的间隙开口，准备说话时头边会转起一个蓝色的小圈圈，随后文字一个字一个字地出现在奶油色的气泡里。气泡大小和停留时间都可以在设置中调整，右键菜单里的“吐字测试”可以随时让她现场说一句。

主人可以按省、市、区县三级选择天气地点。鱼鱼每小时会悄悄更新一次天气，播报时结合气温、体感、湿度、风力、最高最低温和降水概率，用鲸鱼娘的口吻讲给主人听；右键菜单里的“天气”可以随时让她汇报。她也会从 IT之家浏览最新的科技新闻，挑一条讲清楚要点，再加上一小句自己的感受。

内置模型每次只进行一次单独的输出，没有对话历史，也不保留记忆。读屏内容只在本机交给内置模型使用，不会被上传到云端，也不会保存到本地。

笔记本电脑离开电源时，鱼鱼可以自动停止模型推理并把模型移出内存，帮主人照顾续航；外部程序全屏时，她会自动躲起来，结束后再回到桌面。这些行为都可以在设置中按自己的习惯调整。

## 一百多种动作表情

鱼鱼内置 106 段透明动画，覆盖待机、点击、拖动、日常事件、用餐、季节和节日等场景。每一种动作都有自己的出现权重，并会结合当前时间、日期、季节、饭点和主人的操作实时计算，这些概率都经过细致严谨的微调，让她的表情丰富有趣，又贴合主人当下的生活节奏。每段动作在没有新的交互打断时都会完整播放，再自然地衔接到下一段。

<p align="center">
  <img src="docs/images/bigbluefish-meal.gif" width="32%" alt="用餐动作" />
  <img src="docs/images/bigbluefish-fireworks.gif" width="32%" alt="节日烟花动作" />
  <img src="docs/images/bigbluefish-sleep.gif" width="32%" alt="夜间小憩沉眠" />
</p>

待机时，鱼鱼会在好几种安静的动作之间轮换，轻轻地呼吸、悠闲地哼歌；到了晚上 23 点之后，她会开始犯困，时不时打个小盹，直到第二天早上 8 点。主人点击她时，她会从好几种回应里挑一种，可能元气地挥挥手，可能害羞地吓一跳，也可能露出别的小表情，有些回应要多点几次才容易遇到。把她拎起来拖动时，她会悬在半空中保持被抓住的小模样，直到主人松手。

每隔一段时间（默认 40 秒，可在 30 秒到 5 分钟之间调整），鱼鱼会从大量日常事件中挑一件小事来做：跳舞、吹泡泡、敲小电脑，等等许多许多精美华丽的动作。三餐的时间可以在设置中按主人的作息填写，饭点前后一小时里，她会更常端出好多种美食和小吃。早上她更容易打哈欠，每天晚上 23 点后有空的时候，还会认真地刷一次牙。

节日是鱼鱼特别用心准备的部分。春节、元宵、情人节、520、端午、七夕、中秋、万圣节、圣诞节等节日前后，她都准备了专属的节日动作，比如春节写福字、放烟花，中秋赏月吃月饼，有些动作还会随着节日临近越来越常见；节日前后的饭点，她也会端出应景的节日美食。农历节日按照农历日期计算，闰月也能正确处理。

季节也会改变她的日常：冬天会堆雪人，秋天会被落叶埋住，夏天还有好几种消暑的小活动。她偶尔也会跑一跑、学螃蟹横着走，跑出一小段距离，在那里待一会儿，再回到原来的位置。

除了这些，鱼鱼还藏着许多没有写在这里的小心思。愿主人在每天的陪伴里慢慢发现她的可爱，每一次不经意的抬头，都能撞见一个小小的惊喜。

## 安装

普通用户请从 [GitHub Releases](https://github.com/Bai-Cute/big-blue-fish-desktop-pet/releases/latest) 下载对应系统的安装包。每个平台都提供标准版和无MCP版，文件名中带 `Safe` 的是无MCP版，各安装包都附有配套的 `.sha256` 校验文件。

| 平台 | 标准版 | 无MCP版 |
| --- | --- | --- |
| Windows x64 | `BigBlueFish-v1.0.5-OCR-Setup.exe` | `BigBlueFish-v1.0.5-Safe-OCR-Setup.exe` |
| Ubuntu／Debian x64 | `BigBlueFish-v1.0.5-Linux-x64.deb` | `BigBlueFish-v1.0.5-Safe-Linux-x64.deb` |

在 Windows 上，双击安装器即可开始部署。安装器会把桌宠程序、动画资源、.NET 运行时和本地推理运行时一并安装好，默认位置是 `Program Files` 下的“蓝色大肥鱼”文件夹，并创建桌面和开始菜单快捷方式。随后安装器会请主人选择模型的准备方式：Hugging Face（推荐）、国内源（稳定）或手动安装。自动下载的模型约 2.7 GB，下载完成后会经过 SHA-256 校验，中途断开也能从已有进度继续。下载时如果想改用手动安装，可以选择“放弃并转为手动安装”；手动安装会显示模型应放入的文件夹，并打开模型来源页面，也可以直接选择已经下载好的 GGUF 文件。卸载时可以在卸载界面勾选“保留模型和设置”。

在 Ubuntu／Debian 上，下载对应的 `.deb` 后，在其所在目录执行：

```bash
sudo apt install ./BigBlueFish-v1.0.5-Linux-x64.deb
```

安装无MCP版时换成对应的文件名即可。安装完成后，可以从应用菜单或 `bigbluefish` 命令启动鱼鱼，再到设置的“模型与信息”中点击“安装模型”，为她准备本地模型。

如果暂时不想下载模型，也可以先让鱼鱼以无模型模式住进桌面。这时她依然会行礼、问候、道别，播放全部动作表情，回应主人的点击和拖动。之后随时可以到设置中点击“安装模型”，让她开始读懂屏幕、开口说话。

鱼鱼可以在设置中检查更新，也可以在每次启动时自动检查。发现新版本后，应用会下载并校验对应版本的安装包，完成更新后重新启动，模型和设置都会继续保留。Windows 用户也可以退出桌宠后手动运行新版安装器，选择原安装目录完成更新。

## 内置模型

鱼鱼使用 Qwen3.5-4B 的 Q4_K_M 量化 GGUF 模型，由随包分发的 [llama.cpp](https://github.com/ggml-org/llama.cpp)（b10809，Vulkan 版本）在本机推理，可以使用 GPU 加速，也可以切换到 CPU 运行。两个预设来源的模型有所不同，但都适合本应用，主人可以按自己的网络环境选择。

Hugging Face 来源为 [Biomanticus/Qwen3.5-4B-heretic-gguf](https://huggingface.co/Biomanticus/Qwen3.5-4B-heretic-gguf/tree/b63ff4662e4863cfa005c9cd8ed34b87ed44b7e1)，它是 [p-e-w/Qwen3.5-4B-heretic](https://huggingface.co/p-e-w/Qwen3.5-4B-heretic) 的 GGUF 转换版本，基于 Qwen3.5-4B。国内来源为 ModelScope 上的 [diodel/Qwen3.5-4B-Q4_K_M-GGUF](https://modelscope.cn/models/diodel/Qwen3.5-4B-Q4_K_M-GGUF/files)，是原版 [Qwen/Qwen3.5-4B](https://huggingface.co/Qwen/Qwen3.5-4B) 的 Q4_K_M 转换版本。两个来源都使用固定修订，具体文件名、大小与校验值见[第三方声明](code/THIRD-PARTY.md)。

模型会根据当前设备的可用显存自动调整计算缓冲区，并由 llama.cpp 自动分配模型层，小显存设备也能顺利加载；GPU 初始化失败时，鱼鱼会自动切换到 CPU 兼容模式，并在设置中显示实际的运行模式。

## 连接桌面 Agent（MCP）

从 1.0.0 起，标准版的蓝色大肥鱼提供一个标准 MCP 接口，可以连接一个桌面 AI Agent，并接受它的命令。连接之后，主人可以在 AI Agent 端自行配置相关的逻辑和能力，实现电脑操控、与云端大模型对话、自定义动作逻辑等各种自定义功能。许多小伙伴期待的玩法，现在都可以交给一个普通的桌面 AI Agent 来完成，鱼鱼也因此拥有了无穷的拓展可能。

连接方法很简单：在设置的“Agent 与 MCP”页面中打开“开启 MCP 接口”，阅读并确认弹出的权限说明，再点击“复制 MCP 连接配置”，把复制到的内容填进 Agent 的 MCP 服务器配置中即可。配置指向随包安装的桥接程序，形如：

```json
{
  "mcpServers": {
    "big-blue-fish": {
      "command": "C:\\Program Files\\蓝色大肥鱼\\BigBlueFish.Mcp.exe",
      "args": []
    }
  }
}
```

Linux 上的 `command` 为 `/opt/bigbluefish/BigBlueFish.Mcp`。桥接程序会连接到正在运行的鱼鱼，连接成功后，设置页面会显示当前的连接数量。

连接后，Agent 可以使用以下能力：

- 读取和修改鱼鱼的设置，查看她当前的动作、队列和模型状态；
- 获取当前前台窗口或整个屏幕的截图；
- 查询全部 106 个动作，让鱼鱼立即播放某个动作，或者把动作排进队列依次播放；
- 让鱼鱼立即说出一段指定的话，或者在下一次正常说话时机说出来；
- 实时接收主人点击、拖动鱼鱼的交互事件。

如果希望完全交给 Agent 来安排，可以开启“提线木偶模式”。这时鱼鱼会暂停自己的说话和动作切换，安静地循环播放“待机呼吸休闲”，等待 Agent 指定动作和文字；外部动作播放完后，她会回到待机。想恢复原来的陪伴时，关闭提线木偶模式即可。开启“双击与鱼鱼对话”后，只要有 Agent 已经连接，双击鱼鱼就会在她身边展开一个小输入框，主人写下的话会交给 Agent，Agent 再通过鱼鱼的气泡回复主人。

即使接入了 AI Agent，鱼鱼在这条连接中做的事情也始终很简单：向 Agent 传入 Agent 主动要求的截图、主人主动输入的文字，以及主人点击、拖动她的交互事件，再接收 Agent 发来的文字、动作和设置等控制。鱼鱼不会自主命令 Agent，电脑操控、云端对话等各种操作能力，实际都由 Agent 自己执行。

**风险提醒：** 通过 MCP 连接后，截屏会被传递给 MCP 彼端，其处理方式由彼端应用决定，可能导致主人的信息被上传或保存。MCP 的权限对整个接口统一生效，开启后每个已连接的 Agent 都可以使用上面列出的全部能力，不按 Agent 或工具分别授权。设置中的“感知当前应用”开关只控制内置模型自己读屏，Agent 通过 MCP 请求的截图不受这个开关影响。MCP 接口默认关闭，开启前软件会弹窗说明这些权限，请只连接你信任的 Agent。

完整的连接方式、工具参数、事件格式和接入建议见 [MCP 接入参考](docs/MCP.md)。

## 无MCP版

为了彻底消除 MCP 可能带来的隐私风险，蓝色大肥鱼为 Windows 和 Linux 额外提供了无MCP版。无MCP版在编译时就移除了 MCP 控制服务、桥接程序、双击输入框和相关设置页面，安装包中只保留鱼鱼自己的陪伴功能，应用内更新也会继续获取无MCP版。安装包文件名中带有 `Safe` 字样，下载方式与标准版相同。

## 从仓库部署

仓库面向开发者和维护者，包含源码、动画资源、地区数据、构建与发布脚本、测试程序、依赖锁文件和许可文件。模型与推理运行时由脚本另行准备。在 Windows x64 上安装 .NET 10 SDK 后，可以从仓库根目录构建：

```powershell
# 标准版
./code/build.ps1 -Locked -InputMode Ocr

# 无MCP版
dotnet build ./code/BigBlueFish.Windows/BigBlueFish.Windows.csproj -c Release -p:Platform=x64 -p:CompanionEdition=Safe -p:RestoreLockedMode=true
```

准备模型时，可以用模型脚本从任一来源下载，或导入本机已有的 GGUF 文件，然后指定 llama.cpp 运行时目录和模型目录启动；不准备模型时，用 `-NoModel` 以无模型模式启动：

```powershell
./tools/Setup-Model.ps1 -Source HuggingFace -DestinationDirectory "D:\BigBlueFish-local"
./tools/Setup-Model.ps1 -Source ModelScope -DestinationDirectory "D:\BigBlueFish-local"
./tools/Setup-Model.ps1 -Source Manual -ModelFile "D:\下载\我的模型.gguf" -DestinationDirectory "D:\BigBlueFish-local"

./code/run.ps1 -InputMode Ocr -RuntimeDirectory "推理运行时目录" -ModelsDirectory "D:\BigBlueFish-local\models"
./code/run.ps1 -InputMode Ocr -NoModel
```

生成安装包需要一个已经核对过的 llama.cpp 运行时目录（Windows 使用 b10809 的 `win-vulkan-x64` 版本）。Linux 安装包在 WSL 中的 Ubuntu／Debian 环境里构建，需要 gcc、pkg-config、libdbus-1-dev、dpkg-deb 和 FFmpeg：

```powershell
# Windows 安装器，-Edition Safe 生成无MCP版
./tools/Build-Installer.ps1 -Edition Standard -RuntimeDirectory "Windows运行时目录"

# Ubuntu／Debian x64 安装包，-Edition Safe 生成无MCP版
./tools/Build-LinuxPackage.ps1 -Distribution "WSL发行版名称" -RuntimeDirectory "Linux运行时目录" -Edition Standard
```

项目版本统一记录在根目录的 `Version.props`，程序、MCP 桥接程序、安装器和卸载器都从这里生成版本信息。源码结构、修改入口、测试方式和历史视觉版的构建参数见[开发说明](code/README.md)。

## 文档与反馈

版本变化记录在 [CHANGELOG.md](CHANGELOG.md) 中。当前版本完整的窗口、动作权重、模型输入、天气新闻、安装更新等行为见[功能规格](docs/AGENT_FUNCTIONAL_SPEC.md)，各已发布版本的行为见[版本规格目录](docs/specs/README.md)，方便其他开发者和 AI 接手维护。欢迎通过 GitHub Issues 提交问题、建议和使用反馈，也感谢来自 B 站的小伙伴们一直以来的关爱和建议。

想让已连接的 Agent 用鱼鱼的口吻陪伴主人，可以参考这张[蓝色大肥鱼人设卡](docs/CHARACTER_CARD.txt)。

## 开源许可与第三方权利信息

本项目以 [Apache License 2.0](LICENSE) 发布。鱼鱼的形象来自对 DeepSeek 的二创虚拟形象“蓝色大肥鱼”；项目使用的动画、模型、运行时、字体和数据分别来自以下项目，各自遵循其原有许可：

- 角色动画：106 段透明 WebM 动画来自 [PC2005-cloud/dsh-pet](https://github.com/PC2005-cloud/dsh-pet)，MIT 许可，保留原有颜色、透明度与 24 帧时序，动作的选择与触发逻辑为本项目定制；
- 本地模型：Qwen3.5-4B 及其衍生的 heretic 版本与 GGUF 转换，Apache-2.0 许可，模型文件由用户另行下载；
- 推理与运行环境：llama.cpp（MIT）、.NET 10 运行时（MIT）、Microsoft Visual C++ 运行库（Microsoft 再分发条款）；
- 动画解码：Windows 版使用 FFmpeg 9.0.1（GPL-3.0-or-later），作为独立进程运行；Linux 版使用系统的 FFmpeg；
- Linux 界面：Avalonia、MicroCom.Runtime、Tmds.DBus.Protocol、SkiaSharp、HarfBuzzSharp（MIT）；
- 文字识别：Windows 版使用系统提供的 Windows.Media.Ocr，Linux 版使用 Tesseract；
- 气泡字体：乐米小奶泡体，按其公布的免费商用条款内嵌；
- 天气数据：[Open-Meteo](https://open-meteo.com/)，CC BY 4.0；科技新闻：[IT之家 RSS](https://www.ithome.com/rss/)；
- 行政区划数据：[AreaCity-JsSpider-StatsGov](https://github.com/xiangyuecn/AreaCity-JsSpider-StatsGov)，MIT 许可，仅内嵌层级与近似中心坐标。

各组件的版本、修订、校验值和版权说明见[第三方声明](code/THIRD-PARTY.md)，完整许可文本见[许可文件目录](code/licenses)。
