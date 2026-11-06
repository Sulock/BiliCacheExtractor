# Third-party components

## FFmpeg and FFprobe

- Project: [FFmpeg](https://ffmpeg.org/)
- Repository: [FFmpeg/FFmpeg](https://github.com/FFmpeg/FFmpeg)
- Copyright: the FFmpeg developers and the contributors to its included libraries.
- Upstream source: [FFmpeg source code](https://ffmpeg.org/download.html#get-sources).
- License information: [FFmpeg legal page](https://ffmpeg.org/legal.html).

BiliCacheExtractor invokes FFmpeg and FFprobe as separate processes. This source repository does not include their executables, DLLs or source distributions. Users obtain them separately as described in [tools/README.md](tools/README.md).

The license of an FFmpeg binary depends on its build configuration and included libraries. Inspect `ffmpeg -L` and `ffmpeg -version`, and retain the distributor's notices. Builds using `--enable-gpl --enable-version3`, including those used for the current local verification, report GPL version 3 or later. BiliCacheExtractor's own license is **GPL-3.0-only**.

A bundled binary release must document the exact FFmpeg build, origin and checksums and provide the notices and corresponding source required by that build's licenses, including applicable library and build materials. The C# source archive alone does not contain FFmpeg's corresponding source. Bundled release materials are still being prepared.

## Microsoft .NET Framework and Windows

The application uses the installed .NET Framework and Windows system APIs. This repository does not redistribute those system components.

## Optional icon generation tools

`tools/make-icon.py` uses [Pillow](https://python-pillow.github.io/) and [NumPy](https://numpy.org/) in the developer's Python environment. These libraries are not embedded in the application or distributed by this repository. Retain their respective licenses if redistributing them separately.

## 简体中文

FFmpeg/FFprobe 由程序通过独立进程调用，仓库未包含它们的 EXE、DLL 或源码发行包。其具体许可取决于编译选项和所含第三方库，可通过 `-L`、`-version` 及发行方说明核对。

本地验证使用的 FFmpeg 构建启用了 `--enable-gpl --enable-version3`，报告适用 GPL 第 3 版或以后版本；本项目自身采用 `GPL-3.0-only`。发布内置 FFmpeg 的程序包时，需要记录该构建的准确来源和校验值，并提供对应的许可、声明、源码及构建材料。当前 C# 源码不能替代 FFmpeg 的对应源码。

.NET Framework、Windows 系统组件，以及可选图标生成器使用的 Pillow、NumPy 均不由本仓库分发。
