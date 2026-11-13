# macOS 安装与源码构建（Apple Silicon）

## 安装发行包

在 [GitHub Releases](https://github.com/Bai-Cute/big-blue-fish-desktop-pet/releases/latest) 下载标准版 `BigBlueFish-v1.1.3-macOS-arm64.dmg` 或无MCP版 `BigBlueFish-v1.1.3-Safe-macOS-arm64.dmg`。打开镜像，将其中的应用拖入 Applications，再从“应用程序”启动。运行应用所需的 .NET、动画解码和本地推理程序随包提供。需要 Apple Silicon Mac、macOS 14 及以上。

发行包使用临时签名，未经过 Apple 公证。首次打开如被 macOS 安全提示阻止，可以先尝试打开一次，再到“系统设置 → 隐私与安全性”中为该应用选择“仍要打开”，按系统提示确认。维护者本人未在 Mac 上亲自试用。

读屏或截图时，在系统的屏幕录制权限中允许蓝色大肥鱼；数据目录为 `~/Library/Application Support/bigbluefish`。覆盖更新应用会继续使用该目录中的模型和设置。

## 从源码构建

### 1. 准备（一次）

```bash
xcode-select --install          # clang、swiftc
brew install --cask dotnet-sdk  # 或安装到 ~/.dotnet，需 .NET 10
brew install node               # 仅连接 Agent（Claude 等）时需要 npm
```

需要 Apple Silicon Mac、macOS 14 及以上。

### 2. 构建

```bash
tools/build-macos.sh            # 标准版
tools/build-macos.sh Safe       # 无 MCP 版
```

本机装了多个 .NET 时，指定 .NET 10：

```bash
DOTNET=~/.dotnet/dotnet tools/build-macos.sh
```

产物：`release/蓝色大肥鱼.app`。

### 3. 运行

```bash
open release/蓝色大肥鱼.app
```

- 读屏和 MCP 截图需要授权：系统设置 › 隐私与安全性 › 录屏与系统录音，勾选蓝色大肥鱼。
- 数据目录：`~/Library/Application Support/bigbluefish`。
- 源码构建同样使用临时签名。

## 更新

发行包用户下载新版镜像，用新版应用替换 Applications 中的原应用；源码构建用户拉取新源码后重新执行构建步骤。
