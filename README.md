# 蓝色大肥鱼 · 原生 C++ 单文件实验支线

当前实验版本为 `0.1.0-playground.1`，工作分支为 `playground`。本目录独立演进，主线应用的版本号不随实验版改变。完整行为见 [SPEC.md](SPEC.md)。

程序代码全部位于 [BigBlueFish.cpp](BigBlueFish.cpp)。它使用编译器随附的 C++ 标准库和 Windows 系统头文件，图形、媒体、网络及 OCR 通过 Windows 自带接口运行，不需要另装第三方开发库或手填链接参数。

## 编译与运行

把 `BigBlueFish.cpp` 的全文粘贴到原版 Embarcadero Dev-C++ 6.3 / TDM-GCC 9.2 的一个空白 CPP 文件中，保存为 `BigBlueFish.cpp`，直接按 F11。编译时不读取资源目录。构建结果是一个 EXE，运行时按 EXE 所在位置查找同级 `assets`，不依赖启动工作目录。

```text
BigBlueFish.exe
assets/
  BigBlueFish.ico
  fish/                    106 段透明动画及动作规则
  fonts/                   乐米小奶泡体
  regions/                 天气地点及坐标
  skills/                  Agent 操作技能与人设
  licenses/                资源许可和来源
  native-version.txt
```

运行目标为具备 Media Foundation 的 Windows 10/11。视频由系统 H.264 解码器读取，优先使用共享且启用多线程保护的 D3D11 设备；不能建立硬件路径或开始解码失败时使用系统软件解码。角色画面为 1280×720、60fps，视频文件的 1920×1080 编码画面同时承载颜色和透明度，透明通道由程序还原。

实验版在 `playground-data` 保存设置、天气缓存、聊天记录和 Agent 状态，默认工作区为文档目录下的“蓝色大肥鱼实验工作区”。首次运行时，如果安装目录下有主线 `data/preferences.json`，读取设置作为实验版初始设置，随后分别保存。主线的模型和数据不移动、不删除。实验版使用独立的单实例锁、启动项、任务栏身份和内部 MCP 管道前缀。

## Agent 与维护

实验版使用外部 CLI Agent 生成话语。设置保留 21 项 Harness 入口，使用用户已安装的 CLI；需要 ACP 适配器的入口，把适配器安装到实验版自己的数据目录。连接、认证、模型及权限选项由相应 Harness 提供。ACP、MCP、会话、工作区、交互事件和唤醒调度均由本 CPP 实现；MCP stdio 桥接也是同一个 EXE 的运行模式。

未连接 Agent 时，动画、点击拖动、显示隐藏、设置和固定欢迎告别仍可工作。普通 Agent 长回答保留在会话中，鱼鱼的气泡文字由 Agent 调用桌宠工具单独提交。

更新只选择 `playground-v*` 标签，匹配 `BigBlueFish-Native-Windows-x64-<实验版本>.zip`。包中包括 EXE、完整 `assets` 和 `assets/native-version.txt`。程序使用 GitHub 附件元数据中的 SHA-256 校验下载，后台下载与解包，退出后由临时目录中的同一 EXE 替换程序和素材；替换失败时恢复原文件，保留用户数据。更新不依赖 SHA256 文本附件。

`--version` 输出实验版本；`--pipe <管道名>` 进入内部 MCP stdio 桥接；`--apply-update <任务文件>` 执行已准备好的更新；`--test-mode` 提供本地测试指令入口。正常使用直接启动 EXE。

角色动画、字体及地区资料的来源见 [第三方说明](assets/licenses/THIRD-PARTY.md)。
