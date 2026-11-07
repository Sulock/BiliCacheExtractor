# Third-party components

## FFmpeg and FFprobe

- Project: [FFmpeg](https://ffmpeg.org/)
- Repository: [FFmpeg/FFmpeg](https://github.com/FFmpeg/FFmpeg)
- Copyright: the FFmpeg developers and the contributors to its included libraries.
- Upstream source: [FFmpeg source code](https://ffmpeg.org/download.html#get-sources).
- License information: [FFmpeg legal page](https://ffmpeg.org/legal.html).

BiliCacheExtractor invokes FFmpeg and FFprobe as separate processes. Neither this source repository nor the 1.3.0 application package includes FFmpeg executables or DLLs. `Install-FFmpeg.cmd` downloads the pinned upstream distribution separately as described in [tools/README.md](tools/README.md).

The license of an FFmpeg binary depends on its build configuration and included libraries. Inspect `ffmpeg -L` and `ffmpeg -version`, and retain the distributor's notices. Builds using `--enable-gpl --enable-version3`, including those used for the current local verification, report GPL version 3 or later. BiliCacheExtractor's own license is **GPL-3.0-only**.

The setup script uses **FFmpeg n8.1.3-27-g8dca1586f4**, BtbN's `autobuild-2026-10-31-12-59` Windows x64 GPL shared build, licensed under **GPL-3.0-or-later**. Its download URL and SHA-256 are recorded in [tools/ffmpeg.lock.json](tools/ffmpeg.lock.json). The original distribution is preserved under `tools/ffmpeg/`, including `LICENSE.txt`; `SOURCE-INFO.txt` records the version, origin, FFmpeg source and build-script links.

If a future release redistributes FFmpeg binaries, it must also provide the notices and corresponding source required by that exact build's licenses, including applicable library and build materials. The application source ZIP contains BiliCacheExtractor's corresponding source; FFmpeg source is provided separately by its upstream projects.

## Microsoft .NET Framework and Windows

The application uses the installed .NET Framework and Windows system APIs. This repository does not redistribute those system components.

## Optional icon generation tools

`tools/make-icon.py` uses [Pillow](https://python-pillow.github.io/) and [NumPy](https://numpy.org/) in the developer's Python environment. These libraries are not embedded in the application or distributed by this repository. Retain their respective licenses if redistributing them separately.

## 简体中文

FFmpeg/FFprobe 由程序通过独立进程调用。源码仓库及 1.3.0 程序包不包含其 EXE 或 DLL，自动配置脚本会从上游单独下载固定构建，并保留完整发行目录、许可证和来源记录。其具体许可取决于编译选项和所含第三方库，可通过 `-L`、`-version` 及发行方说明核对。

安装脚本选定 FFmpeg `n8.1.3-27-g8dca1586f4`，来自 BtbN 的 `autobuild-2026-10-31-12-59` Windows x64 GPL 共享构建，采用 `GPL-3.0-or-later`；本项目自身采用 `GPL-3.0-only`。下载地址和 SHA-256 见 `tools/ffmpeg.lock.json`，安装后的许可和来源文件位于 `tools/ffmpeg/`。若以后直接分发 FFmpeg 二进制，还需为该构建提供对应的许可、声明、源码及构建材料。

.NET Framework、Windows 系统组件，以及可选图标生成器使用的 Pillow、NumPy 均不由本仓库分发。
