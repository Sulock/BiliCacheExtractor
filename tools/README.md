# FFmpeg setup / 媒体工具配置

[English README](../README.md) | [简体中文说明](../README.zh-CN.md) | [繁體中文說明](../README.zh-TW.md)

## English

Run `Install-FFmpeg.cmd` in the application/source folder to download and install the version pinned in [ffmpeg.lock.json](ffmpeg.lock.json). The setup checks the archive size and SHA-256 before extraction and keeps the complete upstream distribution, including its license. It needs an internet connection for the first download (about 86 MiB).

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

The source repository and application package exclude FFmpeg binaries; setup retrieves them from the upstream provider. The pinned build uses **GPL-3.0-or-later**. Its `LICENSE.txt` is preserved and `SOURCE-INFO.txt` records the version and source links. Redistribution of a bundled build needs the licenses, notices and corresponding source applicable to that exact build and its libraries. See [third-party notices](../THIRD_PARTY_NOTICES.md).

## 简体中文

在程序或源码目录运行 `Install-FFmpeg.cmd`，脚本会联网下载 `ffmpeg.lock.json` 指定的固定版本，核对大小与 SHA-256 后安装到 `tools/ffmpeg/`，保留完整上游发行目录及 GPL-3.0-or-later 许可证。首次下载约 86 MiB，重复运行会核对已有安装。`SOURCE-INFO.txt` 记录版本、下载来源、FFmpeg 源码与构建脚本链接。

离线安装时，先下载 `ffmpeg.lock.json` 中指定的 ZIP，再运行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-FFmpeg.ps1 -ArchivePath 'C:\Downloads\ffmpeg.zip'`，脚本仍会校验下载。已有手动配置可直接使用；需要换用自动配置时，先将旧工具移至备份目录。

也可从 [FFmpeg 下载页](https://ffmpeg.org/download.html)列出的提供方取得 Windows x64 版 FFmpeg 与 FFprobe，保留发行方的许可证和源码信息，将两个 EXE 与共享构建所需的 DLL 直接放入 `tools/`。

CPU 转码需要 `libx264`、`libx265`，硬件转码需要对应的 NVENC、QSV 或 AMF 编码器以及兼容的显卡驱动。扫描与提取需要 MP4/MOV、`subfile` 协议及源媒体编码支持；测试需要 `lavfi`、色块/噪声源与 AAC 编码。程序会实际初始化编码器，检测通过后才提供对应选项。

查找顺序是 `tools/`、`tools/ffmpeg/bin/`、旧目录 `ffmpeg-master-latest-win64-gpl-shared/bin/`、程序根目录，不读取系统 PATH。可选的 `tools/ffmpeg-compat.exe` 用于尝试兼容编码器；需要时单独取得并记录其来源。新版 FFmpeg 可能要求更高版本的显卡驱动；硬件编码不可用时，自动模式可使用 CPU 编码。

仓库和本项目程序包均不包含 FFmpeg 二进制，由自动配置脚本从上游单独取得。若以后分发内置 FFmpeg 的程序包，应为准确的构建版本及所含库准备对应的许可证、声明和源码材料。
