# 蓝色大肥鱼 · Windows 一键安装版

请在 GitHub Releases 下载 `BigBlueFish-Setup-x64.exe`，双击后按安装器提示完成部署。安装器会安装桌宠程序、动画资源、.NET 运行时和本地推理运行时；安装完成后会创建快捷方式并启动蓝色大肥鱼。

安装过程中需要联网下载约 2.7 GB 的本地 4B 模型。下载源为固定版本的 Hugging Face：

```text
https://huggingface.co/Biomanticus/Qwen3.5-4B-heretic-gguf/resolve/b63ff4662e4863cfa005c9cd8ed34b87ed44b7e1/Qwen3.5-4B-heretic-f16_Q4_K_M.gguf?download=true
```

安装器会在完成下载后校验模型。模型文件名为 `Qwen3.5-4B-heretic-Q4_K_M.gguf`，SHA-256 为：

```text
8485535a36c9f333574d08b650ad698ac02ec30752bd9cd87e493a3b7531bee1
```

模型不存储在 Git 仓库中，也不作为普通仓库文件上传；它由安装器在安装过程中从上面的固定来源获取。安装完成后，可以从桌面快捷方式、开始菜单或安装目录启动程序。

发布包和模型使用的许可见 `THIRD-PARTY.md`、`LICENSE` 和 `licenses/`。版本变化见仓库根目录的 `CHANGELOG.md`。
