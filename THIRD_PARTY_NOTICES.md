# Third-party components

## FFmpeg and FFprobe

- Project: [FFmpeg](https://ffmpeg.org/)
- Repository: [FFmpeg/FFmpeg](https://github.com/FFmpeg/FFmpeg)
- Copyright: the FFmpeg developers and the contributors to its included libraries.
- Upstream source: [FFmpeg source code](https://ffmpeg.org/download.html#get-sources).
- License information: [FFmpeg legal page](https://ffmpeg.org/legal.html).

BiliCacheExtractor invokes FFmpeg and FFprobe as separate processes. The **1.3.1 offline application package** bundles the local FFmpeg build `N-128122-g215351c1c3-20261027`, FFprobe and the required DLLs. The source repository records their hashes in `tools/ffmpeg-bundle.lock.json` and excludes the binary files themselves.

The license of an FFmpeg binary depends on its build configuration and included libraries. Inspect `ffmpeg -L` and `ffmpeg -version`, and retain the distributor's notices. Builds using `--enable-gpl --enable-version3`, including those used for the current local verification, report GPL version 3 or later. BiliCacheExtractor's own license is **GPL-3.0-only**.

The bundled FFmpeg uses **GPL-3.0-or-later**. Its original license is retained in `tools/ffmpeg/LICENSE.txt`. The Release provides **FFmpeg-N-128122-source-build-kit.zip**, containing the precise FFmpeg main source archive, the dated BtbN build recipes/patches, and the fixed dependency source locations. See [FFmpeg source access and build instructions](third_party/FFMPEG-SOURCES.md).

For source checkouts, the optional setup script downloads a separate FFmpeg `n8.1.3-27-g8dca1586f4` development build under GPL-3.0-or-later. Its download URL and checksum are recorded in `tools/ffmpeg.lock.json`.

## Optional legacy FFmpeg

The `v1.3.1-legacy` package adds `Install-Legacy-FFmpeg.cmd`. It obtains FFmpeg 4.2.2 directly from imageio-ffmpeg 0.4.9 on PyPI and verifies both the wheel and executable against [fixed hashes](tools/ffmpeg-compat.lock.json). The old executable is downloaded separately and is not in our Release assets. It reports **GPL-3.0-or-later**, copyright (c) 2000-2019 the FFmpeg developers and contributors to its included libraries. The full GPLv3 text is in `LICENSE`; [LEGACY-FFMPEG.md](LEGACY-FFMPEG.md) records provenance, source availability and setup. The imageio Python wrapper is not installed.

兼容版通过首次联网配置取得与本地相同的 FFmpeg 4.2.2，之后可离线使用。旧版采用 GPL-3.0-or-later，从上游独立下载，不内置在本项目附件中。哈希、来源及历史源码材料的现状见上述说明。

## BtbN build recipes

The preserved recipe archive is from `BtbN/FFmpeg-Builds`, tag `autobuild-2026-10-27-18-38`, copyright 2020-2021 BtbN. It is distributed under the MIT license, retained in `third_party/ffmpeg-builds.LICENSE` and in the original recipe archive. Dependency projects retain their own copyright and license files in the source repositories listed in `third_party/ffmpeg-dependencies.json`.

## Microsoft .NET Framework and Windows

The application uses the installed .NET Framework and Windows system APIs. This repository does not redistribute those system components.

## Optional icon generation tools

`tools/make-icon.py` uses [Pillow](https://python-pillow.github.io/) and [NumPy](https://numpy.org/) in the developer's Python environment. These libraries are not embedded in the application or distributed by this repository. Retain their respective licenses if redistributing them separately.

## 简体中文

FFmpeg/FFprobe 由程序通过独立进程调用。1.3.1 离线包内置本地主构建 `N-128122-g215351c1c3-20261027` 的程序及 DLL，并保留其原始 GPL-3.0-or-later 许可文件。源码仓库仅记录版本、哈希和源码信息，不存储这些二进制。

同一 Release 提供 FFmpeg 主源码、对应日期的构建配方与补丁，以及依赖源码仓库和固定修订号清单。BtbN 构建配方采用 MIT 许可，原文与版权声明已保留。应用自身采用 `GPL-3.0-only`；可选的源码开发配置脚本使用另一套单独下载的 FFmpeg 构建，信息见 `tools/ffmpeg.lock.json`。

.NET Framework、Windows 系统组件，以及可选图标生成器使用的 Pillow、NumPy 均不由本仓库分发。
