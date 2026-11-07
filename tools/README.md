# FFmpeg setup / 媒体工具配置

[English README](../README.md) | [简体中文说明](../README.zh-CN.md) | [繁體中文說明](../README.zh-TW.md)

## English

The **1.3.1 offline Release** already includes FFmpeg/FFprobe and the required DLLs under `tools/ffmpeg/`. Start the application directly. The bundled build and file hashes are recorded in [ffmpeg-bundle.lock.json](ffmpeg-bundle.lock.json); its [source and build materials](../third_party/FFMPEG-SOURCES.md) are provided with the Release.

For a source checkout, the optional `Install-FFmpeg.cmd` downloads the separate development build pinned in [ffmpeg.lock.json](ffmpeg.lock.json). It checks the archive size and SHA-256 before extraction and preserves the complete upstream distribution and license. Its first download needs an internet connection (about 86 MiB).

```text
BiliCacheExtractor.exe
BiliCacheExtractor.Cli.exe
tools/
  ffmpeg.lock.json
  ffmpeg/
    LICENSE.txt
    SOURCE-INFO.txt
    bin/
      ffmpeg.exe
      ffprobe.exe
      ...required DLLs...
```

For offline installation, download the exact ZIP linked in `ffmpeg.lock.json` on another computer, then run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-FFmpeg.ps1 -ArchivePath 'C:\Downloads\ffmpeg.zip'`. The same checksum verification applies. Re-running setup verifies an existing installation and leaves it in place. Existing manually installed tools are preserved; skip setup if they already work, or move them to a backup folder first.

FFmpeg and FFprobe run as separate executables. For manual configuration, obtain a Windows x64 build from the providers linked on the [FFmpeg download page](https://ffmpeg.org/download.html), keep its license/source information, and place `ffmpeg.exe`, `ffprobe.exe` and any required DLLs directly in `tools/`. Both executables are required, including when starting the graphical interface or requesting CLI help. Use them from the same distribution.

Required capabilities:

- MP4/MOV demuxing and muxing, the `subfile` protocol, and support for your source audio/video codecs.
- `libx264` for CPU H.264 and `libx265` for CPU HEVC encoding.
- `h264_nvenc` / `hevc_nvenc`, `h264_qsv` / `hevc_qsv`, or `h264_amf` / `hevc_amf` for the corresponding hardware, plus a compatible driver.
- `lavfi`, color/noise sources and AAC encoding for the included media tests and encoder checks.

The application tests encoders at runtime. An encoder listed by FFmpeg may still be unavailable with your GPU or driver. Lossless extraction does not require a video encoder.

The application searches `tools/` first, then `tools/ffmpeg/bin/`, the legacy `ffmpeg-master-latest-win64-gpl-shared/bin/` directory, and its own directory. It does not search the system PATH. An optional `tools/ffmpeg-compat.exe` is also tested as a fallback encoder; obtain and document it separately if needed. A newer FFmpeg can require a newer graphics driver. Automatic mode can use CPU encoding when the hardware encoder is unavailable.

Inspect your installed build with:

```powershell
.\tools\ffmpeg\bin\ffmpeg.exe -version
.\tools\ffmpeg\bin\ffmpeg.exe -L
.\tools\ffmpeg\bin\ffmpeg.exe -encoders
.\tools\ffmpeg\bin\ffprobe.exe -version
```

The source repository excludes FFmpeg binaries. The offline Release bundles build **N-128122-g215351c1c3-20261027**, under **GPL-3.0-or-later**. Its original `LICENSE.txt` is preserved and `SOURCE-INFO.txt` points to the source/build kit and fixed dependency source locations. See [third-party notices](../THIRD_PARTY_NOTICES.md).

## 简体中文

**1.3.1 离线程序包**已在 `tools/ffmpeg/` 中内置 FFmpeg、FFprobe 和配套 DLL，解压后直接运行程序。内置版本和文件哈希见 `ffmpeg-bundle.lock.json`，对应的主源码、依赖源码来源及构建资料见[源码说明](../third_party/FFMPEG-SOURCES.md)。

从源码检出时，可选用 `Install-FFmpeg.cmd` 下载 `ffmpeg.lock.json` 指定的另一套开发用构建。脚本核对大小与 SHA-256 后安装，保留原始发行目录、GPL-3.0-or-later 许可证与来源记录。首次下载约 86 MiB，重复运行会核对已有安装。

离线安装时，先下载 `ffmpeg.lock.json` 中指定的 ZIP，再运行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-FFmpeg.ps1 -ArchivePath 'C:\Downloads\ffmpeg.zip'`，脚本仍会校验下载。已有手动配置可直接使用；需要换用自动配置时，先将旧工具移至备份目录。

也可从 [FFmpeg 下载页](https://ffmpeg.org/download.html)列出的提供方取得 Windows x64 版 FFmpeg 与 FFprobe，保留发行方的许可证和源码信息，将两个 EXE 与共享构建所需的 DLL 直接放入 `tools/`。

CPU 转码需要 `libx264`、`libx265`，硬件转码需要对应的 NVENC、QSV 或 AMF 编码器以及兼容的显卡驱动。扫描与提取需要 MP4/MOV、`subfile` 协议及源媒体编码支持；测试需要 `lavfi`、色块/噪声源与 AAC 编码。程序会实际初始化编码器，检测通过后才提供对应选项。

查找顺序是 `tools/`、`tools/ffmpeg/bin/`、旧目录 `ffmpeg-master-latest-win64-gpl-shared/bin/`、程序根目录，不读取系统 PATH。可选的 `tools/ffmpeg-compat.exe` 用于尝试兼容编码器；需要时单独取得并记录其来源。新版 FFmpeg 可能要求更高版本的显卡驱动；硬件编码不可用时，自动模式可使用 CPU 编码。

源码仓库不存储 FFmpeg 二进制。离线 Release 内置 `N-128122-g215351c1c3-20261027` 构建，保留其 GPL-3.0-or-later 许可证，并提供主源码、构建配方、补丁及依赖源码的固定来源清单。
