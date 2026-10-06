# Third-party notices

This is a customized companion build, not an official VPet or DeepSeek release.

* VPet: https://github.com/LorisYounger/VPet , commit 2e99a42ebeff71d792118f2e8de744b773042f8d. Apache-2.0. Modified startup, companion behavior, local inference, power handling and PNG tint rendering. Original copyright notices are retained in source.
* Character animation: https://github.com/minghaoxia61-web/dsh-dafeiyu . MIT notices included. Project credits include xituisuany-max/dsh-client-ui-pet and desktop1190fasheqi. Frame timing has been adapted for VPet; blue tint is applied during rendering. No affiliation with DeepSeek is implied; repository licenses do not grant trademark rights.
* llama.cpp: https://github.com/ggml-org/llama.cpp , b10809, Windows x64 runtime. MIT. Copyright (c) 2023-2026 The ggml authors. LLVM OpenMP license included.
* Model origin: https://huggingface.co/p-e-w/Qwen3.5-4B-heretic . Base Qwen3.5-4B, Apache-2.0.
* GGUF: https://huggingface.co/Biomanticus/Qwen3.5-4B-heretic-gguf , revision b63ff4662e4863cfa005c9cd8ed34b87ed44b7e1. Upstream filename Qwen3.5-4B-heretic-f16_Q4_K_M.gguf. Expected local filename Qwen3.5-4B-heretic-Q4_K_M.gguf; model is not included in Git or the release ZIP. Size 2708804480 bytes. SHA256 8485535a36c9f333574d08b650ad698ac02ec30752bd9cd87e493a3b7531bee1. Apache-2.0 text is included as VPet-Apache-2.0.txt and also applies to the model.
* Vision projector GGUF: https://huggingface.co/mradermacher/Qwen3.5-4B-heretic-GGUF , revision 0d92f575bfcb057411f3d4088c5eabed979a9b3f, upstream filename and expected local filename Qwen3.5-4B-heretic.mmproj-f16.gguf. It is the matching multimodal projector for the Qwen3.5-4B-heretic model and is downloaded by the installer; it is not included in Git or the release ZIP. Size 672423552 bytes. SHA256 E638DC8DE3B75309A190092BA006307759343B62AE0D21ED8359DF76B9B76C3B. Apache-2.0.
* .NET 10 runtime: Microsoft and .NET contributors, MIT. Additional runtime notices are included where distributed with the SDK.
* Windows OCR: Microsoft Windows.Media.Ocr, provided by the installed Windows text recognition language component. The 0.2.0 OCR build uses Microsoft.Windows.SDK.NET.Ref 10.0.26100.1 and its runtime projections; Microsoft Windows SDK license: https://aka.ms/WinSDKLicenseURL .
* Weather: https://open-meteo.com/ , CC BY 4.0 attribution. User-selected district center coordinates; current and daily forecast are queried at runtime. No weather dataset is bundled.
* News: IT之家, https://www.ithome.com/rss/ . Headlines are retrieved at runtime; no news archive is bundled.

VPet dependencies include LinePutScript, Panuon WPF, SkiaSharp, NAudio, WpfAnimatedGif and Facepunch Steamworks. Original package license metadata remains in the accompanying source project; Steam and the upstream online reporting/chat startup paths are bypassed in this companion build.

* Administrative region data: https://github.com/xiangyuecn/AreaCity-JsSpider-StatsGov , commit c6c6e35bea3066d674efe2cded189dc57a86e7d8, release 2025.251231.260403. MIT; see licenses/AreaCity-MIT.txt. Only hierarchy and approximate center coordinates are embedded. Mainland GCJ-02 centers are approximately converted to WGS84; polygons are not redistributed.

