# FFmpeg setup / 媒体工具配置

[English README](../README.md) | [简体中文说明](../README.zh-CN.md)

## English

FFmpeg and FFprobe run as separate executables. Obtain a Windows x64 build from the providers linked on the [FFmpeg download page](https://ffmpeg.org/download.html), keep its original license/source information, and place the tools as follows:

```text
BiliCacheExtractor.exe
BiliCacheExtractor.Cli.exe
tools/
  ffmpeg.exe
  ffprobe.exe
  ...required DLLs for a shared build...
```

Both executables are required, including when starting the graphical interface or requesting CLI help. Use FFmpeg and FFprobe from the same distribution. A shared build must keep all required DLLs beside the executables.

Required capabilities:

- MP4/MOV demuxing and muxing, the `subfile` protocol, and support for your source audio/video codecs.
- `libx264` for CPU H.264 and `libx265` for CPU HEVC encoding.
- `h264_nvenc` / `hevc_nvenc`, `h264_qsv` / `hevc_qsv`, or `h264_amf` / `hevc_amf` for the corresponding hardware, plus a compatible driver.
- `lavfi`, color/noise sources and AAC encoding for the included media tests and encoder checks.

The application tests encoders at runtime. An encoder listed by FFmpeg may still be unavailable with your GPU or driver. Lossless extraction does not require a video encoder.

The application searches `tools/` first, then the legacy `ffmpeg-master-latest-win64-gpl-shared/bin/` directory, then its own directory. It does not search the system PATH. An optional `tools/ffmpeg-compat.exe` is also tested as a fallback encoder; obtain and document it separately if needed. A newer FFmpeg can require a newer graphics driver.

Inspect your installed build with:

```powershell
.\tools\ffmpeg.exe -version
.\tools\ffmpeg.exe -L
.\tools\ffmpeg.exe -encoders
.\tools\ffprobe.exe -version
```

The source repository excludes locally obtained FFmpeg binaries. Distribution of a bundled build needs the licenses, notices and corresponding source applicable to that exact build and its libraries. See [third-party notices](../THIRD_PARTY_NOTICES.md).

## 简体中文

从 [FFmpeg 下载页](https://ffmpeg.org/download.html)列出的提供方取得 Windows x64 版 FFmpeg 与 FFprobe，保留发行方提供的许可证和源码信息，按上面的目录结构放入 `tools/`。两个 EXE 都是运行必需文件；共享构建还需要把配套 DLL 放在 EXE 旁。

CPU 转码需要 `libx264`、`libx265`，硬件转码需要对应的 NVENC、QSV 或 AMF 编码器以及兼容的显卡驱动。扫描与提取需要 MP4/MOV、`subfile` 协议及源媒体编码支持；测试需要 `lavfi`、色块/噪声源与 AAC 编码。程序会实际初始化编码器，检测通过后才提供对应选项。

查找顺序是 `tools/`、旧目录 `ffmpeg-master-latest-win64-gpl-shared/bin/`、程序根目录，不读取系统 PATH。可选的 `tools/ffmpeg-compat.exe` 用于尝试兼容编码器；需要时单独取得并记录其来源。新版 FFmpeg 可能要求更高版本的显卡驱动。

仓库不包含本地取得的 FFmpeg 二进制。若分发内置 FFmpeg 的程序包，应为准确的构建版本及所含库准备对应的许可证、声明和源码材料。
