# 在 macOS 上构建（Apple Silicon）

macOS 版没有官方安装包，从源码构建。

## 1. 准备（一次）

```bash
xcode-select --install          # clang、swiftc
brew install --cask dotnet-sdk  # 或安装到 ~/.dotnet，需 .NET 10
brew install node               # 仅连接 Agent（Claude 等）时需要 npm
```

需要 Apple Silicon Mac、macOS 14 及以上。

## 2. 构建

```bash
tools/build-macos.sh            # 标准版
tools/build-macos.sh Safe       # 无 MCP 版
```

本机装了多个 .NET 时，指定 .NET 10：

```bash
DOTNET=~/.dotnet/dotnet tools/build-macos.sh
```

产物：`release/蓝色大肥鱼.app`。

## 3. 运行

```bash
open release/蓝色大肥鱼.app
```

- 读屏和 MCP 截图需要授权：系统设置 › 隐私与安全性 › 录屏与系统录音，勾选蓝色大肥鱼。
- 数据目录：`~/Library/Application Support/bigbluefish`。
- 应用只有本机临时签名，只能在构建它的 Mac 上直接运行。

## 更新

拉取新源码后重新执行第 2 步。
