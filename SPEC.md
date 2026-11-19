# 蓝色大肥鱼原生实验版行为规格

版本：`0.1.0-playground.1`

## 启动、文件与身份

普通启动从 EXE 自身所在目录定位 assets，不依赖启动工作目录。--version 输出版本后退出；--pipe 进入 MCP stdio 桥接；--apply-update 进入更新工作进程；--test-mode 启动具有诊断文件和指令入口的桌宠。

桌宠在当前登录会话使用 Local\BigBlueFish.Playground.Native 单实例锁。任务栏身份与开机启动注册名称为 BigBlueFish.Playground.Native。内部 MCP 管道使用 BigBlueFish.Playground.Native.Control 前缀、用户目录哈希和实例随机标识。

运行数据位于 playground-data。程序为这个子目录配置 SYSTEM、Administrators、Users 的继承式完全控制，不改安装根目录权限。首次缺少设置时，从同级主线 data/preferences.json 读取初始设置，随后分别保存；主线数据和模型不移动。设置以 UTF-8 JSON 原子替换，未知字段保留，错误或缺失字段使用默认值，各数值按支持范围归一化。本地模型启用状态固定为关闭，话语由 Agent 生成。

## 窗口与交互

角色窗口透明、无边框、置顶，不占任务栏，不因单击取得键盘焦点；合成像素 Alpha 决定可点击范围，透明部分穿透。位置按角色可见区域限制在当前显示器工作区。角色与气泡分别在 50%–200% 调整大小。

设置是普通可激活窗口，重复打开恢复已有窗口，不永久置顶。关闭设置保存配置。MCP 设置变更仅刷新已有窗口，不主动打开或激活设置。

角色右键菜单提供设置、天气、吐字测试、隐藏桌宠；托盘菜单另外提供显示和退出。点击菜单外部关闭菜单。托盘在 Explorer 重建后重新注册，并定期检查恢复。用户隐藏与全屏隐藏分别记录；全屏结束仅撤销全屏引起的隐藏。“全屏自动隐藏桌宠”默认开启，可以关闭。

单击在系统双击判定窗口结束后播放点击池动作。真实连接 Agent 后双击打开贴身输入框，灰字为“通过鱼鱼和已连接的 Agent 说话”。拖动达到阈值后移动角色并播放悬空片段，释放后重新安排动作。点击和拖动作为事件提供给 Agent，相应请求由各自响应配置决定。

系统恢复或长时间计时中断后，清理过期交互和请求，重新安排计时并检查托盘。后台任务由所属服务持有；停止时取消并等待任务结束，再释放相关管道、窗口和数据对象。权限及配置完成提示回到界面线程显示。

## 动画播放与调度

动作规则来自 assets/fish/animations.json，加载 106 个动作，优先使用按 Id 命名的 MP4。视频由 Media Foundation H.264 解码；硬件路径共享 D3D11 设备和 DXGI 设备管理器，启用多线程保护。硬件路径无法建立或开始解码失败时使用系统软件解码。

NV12 解码数据恢复为预乘 BGRA。1920×1080 打包画面承载 1280×720 角色的颜色与透明度，小于 8 的 Alpha 视为透明。视频帧按媒体时间和播放速率呈现，暂停时停止播放时钟。新请求递增修订号，旧修订的帧和完成通知不再改变角色；正常片段结束以视频结束或片段终点为准。

待机池在片段边界选择下一个动作。普通事件到期后在可调度的完整片段边界触发；设置打开、角色隐藏或拖动时暂缓。普通动作间隔在 30–300 秒调节。事件结束后回到待机；外部排队动作在当前片段后执行，立即动作打断当前片段。移动包含离开、停留和返回阶段，位置随媒体时间推进。连续失败按有限次数重试。

拖拽只使用“被鼠标拖拽悬空反馈”的中间悬空片段。行礼以 1.5 倍速播放。普通启动按系统时段显示“主人早上/中午/下午/晚上好，鱼鱼给主人请安啦~”；普通退出显示“主人再见~鱼鱼喜欢主人哦”，配合行礼完成退出。

季节为 3–5 月春、6–8 月夏、9–11 月秋、其余冬；夜间专属动作在 23:00–08:00 参与。早餐、午餐和晚餐时刻可设置。季节和早间专属权重先替换基础权重，节日覆盖或按日期半径线性插值，符合用餐规则时再加 MealBoost。非正权重不参与抽取。

春节、元宵、端午、七夕、中秋、腊八按农历换算；情人节、520、圣诞等按阳历。计算包含相邻年份。移动动作按小时奇偶及分钟范围进入候选。每天本地 23 点内的“晨间刷牙”使用当天记录限为一次。工作状态上下文固定为未启用。各动作完整参数见文末规则表。

## 气泡与聊天

气泡使用独立透明窗口、光滑边框、小尖角和私有加载的乐米小奶泡体，跟随角色并保持在工作区内。文字先测量全文，再逐字显示，完成后开始驻留计时；用户可以关闭气泡。生成中在角色头旁显示加载环。

输入框依附角色，不打开独立任务聊天窗口。输入交给当前 ACP 会话。滚轮查看本地聊天记录，默认显示用户消息及相邻鱼鱼回复，也可显示所有鱼鱼话语。最多保留 300 条。Agent 普通回答不直接进入气泡，鱼鱼话语由 say 工具提交并通过统一气泡路径显示。请求结束未提交鱼鱼话语时记录诊断状态，不把普通回答全文转换成气泡。

## 日常关心、天气与新闻

普通关心首次到期为启动后 45 秒。之后从 2–3、3–5、5–8、10–16、15–23、20–30 分钟六档之一均匀抽取等待时间。普通关心要求角色可见、准备完成、未拖动、未退出、设置未打开，且触发与动作未交给 Agent。键鼠空闲要求为 8–600 秒，不满足时延后 30 秒检查。

开启“把日常关心的吐字交给 Agent”后，鱼鱼到期发送 daily_care 并安排下次时间。天气与吐字测试使用手动请求，不要求系统空闲，设置打开时仍可触发。无可用 Agent 时不伪造生成成功。

开启“把日常关心的触发和日常的动作切换逻辑交给 Agent”后，停止自主关心计时和动作选择，默认循环“待机呼吸休闲”，等待外部动作或唤醒安排。说话间隔禁用并显示原因。话语编写开关独立；两项同时开启时 Agent 可以预约关心。未连接或没有外部安排时保持待机。

公共信息开启后向 Open-Meteo 获取所选地点中心附近天气，同一地点每小时刷新，缓存有效期 6 小时，保存在数据目录。中国地点使用北京时间与摄氏度，美国使用华氏度和英里/小时，其他地点按当地时区。天气资料静默交给 Agent，不在气泡中报告传输操作。

日常关心以 3% 概率选择单条科技新闻，来自 IT之家 RSS，缓存 45 分钟，使用最近 48 小时资料。新闻话语要求约 30–60 字，包含核心内容和短小关切。普通关心也可按规则选择最近天气作辅助资料。

读屏开启时，前台截图失败后尝试全屏，仍失败则按时间、天气等已有信息继续。截图只在内存编码。支持图片的 Agent 接收截图，不支持或拒绝图片时使用 Windows 本地 OCR 整理文字。全屏资料标明范围，不沿用失败前台的身份。缺少资料静默回退，不把读屏失败说明当作气泡台词。

## Agent、认证与工作区

提供 21 项 Harness，检测 PATH 与常见安装位置。原生 ACP 入口直接启动，需要适配器时安装到 playground-data/agent/adapters；用户已有 CLI 不由桌宠卸载。ACP 使用子进程 stdio JSON-RPC，协商版本、能力和认证方式，需要时调用 authenticate；终端认证可打开登录进程。

权限由 Harness 返回，用户在对话框决定。模型、工作模式、权限及思考选项按实际公开配置绘制，修改确认后更新显示。每个工作区和 Harness 保存会话状态，恢复时明确找不到旧会话就新建，并继续使用相同工作区。新建对话不新建工作区。连接、会话或工作区变化取消旧请求，旧代次结果不进入新状态。请求队列最多 32 项，可合并的连接或关心请求合并重复项。

默认工作区为文档目录下的“蓝色大肥鱼实验工作区”，各 Harness 共享其中的文件。支持新建、打开和清空。清空二次确认并重新核对路径；磁盘根、共享根、目录链接和链接子路径、用户和系统关键目录及其上级、安装目录、包含桌宠控制数据的目录均拒绝清空。删除不递归进入链接目标，锁定或权限不足时报告未完全清空。

工作区准备相应 Harness 的操作 Skill 与人设，保留用户已经改写的内容。指南或人设变化后送入后续会话资料；日常关心、用户输入、天气、新闻、点击、拖动和唤醒携带各自类型和资料范围。

## MCP 工具与通知

同一个 EXE 的 --pipe 模式桥接内部命名管道 MCP。工具发现返回描述和参数约束。服务保存最近 128 条交互事件，连接可订阅事件通知。

| 工具 | 行为 |
| --- | --- |
| get_settings | 读取设置、显示、动作、队列及连接状态 |
| update_settings | 校验并修改允许的设置；开启触发与动作接管需要用户确认 |
| list_actions | 返回 106 个动作、池、基础权重和名义长度 |
| capture_foreground | 严格按前台规则截取 PNG，返回范围信息 |
| capture_screen | 截取全部显示器组成的完整桌面 |
| play_action | 当前片段之后排队播放动作 |
| interrupt_action | 立即打断并播放动作 |
| say | 立即显示指定的鱼鱼话语 |
| say_next | 设置下次普通说话时机使用的固定文字 |
| request_local_speech | 返回当前实验版使用外部 Agent 的说明 |
| get_operation_guide | 返回指南、人设和控制约定 |
| get_wakeups | 读取当前 Harness 和会话的唤醒安排 |
| schedule_wakeup | 接管且真实连接时预约一次或重复唤醒 |
| cancel_wakeup | 按编号取消唤醒 |

每连接独立排队，最多 128 条待发消息和 32 MiB 待发内容。积压超过上限、发送超时或断开时关闭连接并取消其请求。界面发布事件不等待另一端读取。服务断开后，桥接中止标准输入等待并退出。

## 日志

设置显示最近 32768 条实时诊断记录，支持自动滚动与 TXT 导出。记录启动退出、设置变化、动作与交互、请求类别、连接和认证状态、公共信息获取、耗时及错误码。技术标识使用英文，普通行为使用中文。截图、OCR 正文、聊天、人设和 Agent 通信正文不写入日志；目录匿名化，动态字段按允许的类别处理。导出不附带聊天文件或截图。

## 更新

启动时检查更新可关闭，手动检查可用。只选择 playground-v 标签，比较完整 major.minor.patch-playground.revision 号，并选择最新有效实验版。Windows 附件名为 BigBlueFish-Native-Windows-x64-<版本>.zip，元数据须含 SHA-256。

下载后校验哈希，解包检查 EXE、版本标记、106 个动作及必要字体、图标与地区数据。只安装 EXE 和 assets 中支持的非可执行资源。更新工作进程使用临时目录中的同一 EXE，等待原进程退出，需要时请求写入权限；程序和素材一起替换，保留数据，失败则恢复原文件。使用原进程令牌重新启动，无法启动时提示用户。结果写入数据目录，后续启动清理临时辅助文件。

## Harness 入口

Claude Code、Codex CLI、Gemini CLI、DeepSeek Harness、Kimi Code、Qwen Code、Antigravity CLI、GitHub Copilot CLI、Cursor CLI、OpenCode、Cline CLI、CodeBuddy Code、Qoder CLI、TRAE CLI、Kiro CLI、Pi、Devin CLI、Grok Build、Hermes Agent、OpenClaw、MiniMax Code。

## 动作规则参数

以下参数对应当前动作表；未列出的附加字段为空。

| 动作 | 池 | 基础权重 | 条件及加权参数 |
| --- | --- | ---: | --- |
| 三球抛接 | Event | 100 |  |
| 下五子棋 | Event | 100 |  |
| 东张西望 | Event | 1000 |  |
| 中秋赏月吃月饼 | Event | 0 | Festival=MidAutumn；RadiusDays=7；FestivalMode=Triangle；FestivalWeight=1000 |
| 优雅女仆舞 | Event | 300 |  |
| 余额-分文不剩 | Disabled | 0 |  |
| 余额-数金皱眉 | Disabled | 0 |  |
| 余额-袋空如洗 | Disabled | 0 |  |
| 余额-金袋叮当 | Event | 100 |  |
| 余额-钱袋如常 | Event | 100 |  |
| 余额-钱袋满溢 | Event | 30 |  |
| 偷吃零食被抓住 | Event | 100 |  |
| 写代码 | Event | 100 | WorkKind=Coding；WorkWeight=1000 |
| 写福字 | Event | 0 | Festival=SpringFestival；RadiusDays=14；FestivalMode=Triangle；FestivalWeight=1000 |
| 凭空生花 | Event | 100 | Festival=ValentineOrMay20；RadiusDays=7；FestivalMode=Triangle；FestivalWeight=1000 |
| 动物环绕 | Event | 30 |  |
| 原地专心玩魔方 | Event | 100 |  |
| 原地小憩沉眠 | Idle | 40 | NightOnly=True |
| 原地左转奔跑 | Event | 200 | MoveKind=Run；HourParity=1 |
| 原地敲击桌面互动 | Event | 100 |  |
| 原地漂浮踏步 | Event | 1000 |  |
| 原地跳跃抓碎头顶物品 | Event | 100 |  |
| 原地蹲下玩玩具汽车 | Event | 100 |  |
| 变鸽子 | Event | 100 |  |
| 可爱宅舞 | Event | 100 |  |
| 吃Token | Event | 100 | Meal=Any；MealBoost=400 |
| 吃冰淇淋融化 | Event | 100 | SeasonOnly=Summer；Meal=Any；MealBoost=400 |
| 吃午餐 | Event | 100 | Meal=Dinner；MealBoost=900 |
| 吃大闸蟹 | Event | 100 | Meal=Any；MealBoost=400 |
| 吃年糕 | Event | 100 | Meal=Any；MealBoost=400 |
| 吃早餐 | Event | 100 | Meal=Breakfast；MealBoost=900 |
| 吃晚餐 | Event | 100 | Meal=Lunch；MealBoost=900 |
| 吃汤圆 | Event | 30 | Festival=Lantern；RadiusDays=7；FestivalMode=Window；FestivalWeight=100；Meal=Any；MealBoost=2400 |
| 吃白饭 | Event | 1000 |  |
| 吃粽子 | Event | 30 | Festival=DragonBoat；RadiusDays=7；FestivalMode=Window；FestivalWeight=100；Meal=Any；MealBoost=2400 |
| 吃糖葫芦 | Event | 30 |  |
| 吃腊八粥 | Event | 100 | Festival=Laba；RadiusDays=7；FestivalMode=Window；FestivalWeight=100；Meal=Any；MealBoost=2400 |
| 吃西瓜 | Event | 100 | SummerWeight=300 |
| 吃重阳糕 | Event | 100 | Meal=Any；MealBoost=400 |
| 吃长寿面 | Event | 100 | Meal=Any；MealBoost=400 |
| 吃青团 | Event | 100 | Meal=Any；MealBoost=400 |
| 吃饺子 | Event | 100 | Meal=Any；MealBoost=400 |
| 吹气球 | Event | 100 |  |
| 吹笛子 | Event | 100 |  |
| 哈欠连天 | Event | 100 | MorningWeight=500 |
| 堆雪人 | Event | 300 | SeasonOnly=Winter |
| 大口吃零食 | Event | 30 |  |
| 女仆屈膝礼仪 | Greeting | 100 |  |
| 小幅度原地360度旋转展示 | Event | 100 |  |
| 小提琴演奏 | Event | 100 |  |
| 工作状态-原地踱步张望 | Event | 1000 |  |
| 工作状态-垂头叹气冒汗 | Disabled | 0 |  |
| 工作状态-忙碌点按 | Event | 100 | WorkKind=Working；WorkWeight=1000 |
| 工作状态-思考冒泡 | Event | 100 | WorkKind=Working；WorkWeight=1000 |
| 工作状态-清点归档 | Event | 100 | WorkKind=Working；WorkWeight=1000 |
| 工作状态-雀跃庆祝 | Event | 30 |  |
| 待机呼吸休闲 | Idle | 100 |  |
| 扑克魔术 | Event | 100 |  |
| 抽陀螺 | Event | 100 |  |
| 打瞌睡被惊醒 | Event | 100 |  |
| 拆礼物 | Event | 100 |  |
| 插茱萸赏菊 | Event | 100 |  |
| 摇扇纳凉 | Event | 100 | WinterWeight=0；SummerWeight=300 |
| 撸猫 | Event | 100 |  |
| 收红包 | Event | 0 | Festival=SpringFestival；RadiusDays=14；FestivalMode=Triangle；FestivalWeight=1000 |
| 放孔明灯 | Event | 100 |  |
| 放河灯 | Event | 100 |  |
| 放烟花 | Event | 30 | Festival=SpringFestival；RadiusDays=14；FestivalMode=Triangle；FestivalWeight=1000 |
| 放风筝 | Event | 100 |  |
| 整体换装试色 | Event | 100 |  |
| 是啊，吃什么 | Event | 30 |  |
| 晨间刷牙 | Daily | 100 |  |
| 涮火锅 | Event | 100 | Meal=Any；MealBoost=400 |
| 深度思考碎碎念 | Event | 100 |  |
| 点击回应-傲娇生气 | Click | 20 |  |
| 点击回应-元气挥手 | Click | 100 |  |
| 点击回应-害羞惊讶 | Click | 100 |  |
| 点击回应-开心跃动 | Click | 20 |  |
| 点击回应-挠痒咯咯笑 | Click | 20 |  |
| 照镜子 | Event | 100 |  |
| 玩水枪 | Event | 100 |  |
| 玩游戏气急败坏 | Event | 100 |  |
| 用鲸鱼尾巴拍打地面 | Event | 100 |  |
| 碎碎念-发呆碎碎念 | Event | 100 |  |
| 碎碎念-对屏碎碎念 | Event | 500 |  |
| 碎碎念-擦桌碎碎念 | Event | 100 |  |
| 穿针乞巧 | Event | 0 | Festival=Qixi；RadiusDays=7；FestivalMode=Triangle；FestivalWeight=1000 |
| 舞狮头 | Event | 0 | Festival=SpringFestival；RadiusDays=7；FestivalMode=Triangle；FestivalWeight=1000 |
| 荡秋千 | Event | 100 |  |
| 萌化小幽灵 | Event | 100 |  |
| 蓝鲸现世 | Event | 100 |  |
| 蝴蝶蜜蜂环绕头顶开花 | Event | 30 |  |
| 螃蟹走路 | Event | 200 | MoveKind=Crab；HourParity=0 |
| 被吓一跳 | Event | 30 |  |
| 被落叶淹没 | Event | 100 | SeasonOnly=Autumn |
| 被鼠标拖拽悬空反馈 | Drag | 100 |  |
| 装点圣诞树 | Event | 0 | Festival=Christmas；RadiusDays=14；FestivalMode=Triangle；FestivalWeight=1000 |
| 讨糖南瓜灯 | Event | 0 | Festival=Halloween；RadiusDays=14；FestivalMode=Triangle；FestivalWeight=1000 |
| 超大伸懒腰 | Event | 100 |  |
| 踢毽子 | Event | 100 |  |
| 轻快记录 | Event | 100 |  |
| 轻快摇摆舞 | Event | 1000 |  |
| 骑木马 | Event | 100 |  |
| 鲸鱼吐泡泡特效 | Event | 200 |  |
| 原地重力下蹲压缩 | Event | 100 |  |
| 悠闲哼歌 | Idle | 20 |  |
