# 原生实验版第三方资源说明

角色动画来自 [PC2005-cloud/dsh-pet](https://github.com/PC2005-cloud/dsh-pet)，原始引用提交 eb7426771342413cdbf55298dae576b1d8137f52，MIT，Copyright (c) 2026 PC2005-cloud。106 段动画经过离线超分、插帧及颜色和透明度打包，以 H.264 MP4 提供 1280×720、60fps 的角色画面。原始许可随 Character-MIT.txt 和 Character-pet-MIT.txt 保留。

Real-ESRGAN animevideov3（Xintao Wang，BSD-3-Clause）和 RIFE v4.6（MIT）用于离线制作动画，不是运行时组件。对应许可证保存在本目录。

乐米小奶泡体由乐米字库提供，按原字体的免费商用说明随包私有加载，见 Lemi-Xiao-Nai-PaoTi.txt 和 https://www.maoken.com/freefonts/27620.html 。

行政区划和近似中心坐标源于 xiangyuecn/AreaCity-JsSpider-StatsGov（MIT），见 AreaCity-MIT.txt。全球城市资料来自 GeoNames（CC BY 4.0），国家中文名称来自 Unicode CLDR（Unicode License v3），见 regions 中的来源元数据与 Unicode-CLDR.txt。中国地区层级包含香港、澳门、台湾，不把它们列为独立国家。

天气服务来自 [Open-Meteo](https://open-meteo.com/)，按 CC BY 4.0 署名；新闻来源为 IT之家 RSS。外部 CLI Agent 及其 ACP 适配器分别遵循自身许可。

程序的图形、媒体、OCR 和网络能力使用 Windows 系统组件。SHA-256、JSON、Base64 与 Deflate/ZIP 实现位于 CPP 中。项目源代码按 Apache-2.0 发布，许可文本见 Apache-2.0.txt。
