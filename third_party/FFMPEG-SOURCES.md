# FFmpeg source and build information

The Windows offline package bundles the local FFmpeg build **N-128122-g215351c1c3-20261027** and FFprobe, with their required DLLs. This build reports **GPL-3.0-or-later**. Its original `LICENSE.txt` is included under `tools/ffmpeg/`.

## Source access

The [v1.3.1 Release](https://github.com/Sulock/BiliCacheExtractor/releases/tag/v1.3.1) provides **FFmpeg-N-128122-source-build-kit.zip** alongside the application binaries. It contains:

- The FFmpeg source archive for commit `215351c1c37dc0345a6794f4c96af54cf30e1c7b`.
- The BtbN build recipes and patches from `autobuild-2026-10-27-18-38`, with their MIT license.
- `ffmpeg-dependencies.json`, listing the repositories and fixed Git commits/tags or SVN revisions used by the recipe snapshot. This is the complete upstream recipe map, including recipes disabled for some build variants.
- The binary's reported build configuration and a script that pins the FFmpeg source checkout when using the upstream build system.

The dependency sources are available from the upstream repositories listed in the JSON manifest. For a Git entry, clone its `repository`, check out its exact `revision`, and initialize any submodules recursively. For SVN entries, check out `repository@revision`. The build recipes also contain the source retrieval commands, patches and configuration for each component. Libraries with additional language-package dependencies retain their upstream lockfiles and build instructions.

FFmpeg source is also directly available from [the exact upstream commit](https://github.com/FFmpeg/FFmpeg/tree/215351c1c37dc0345a6794f4c96af54cf30e1c7b) and its [source archive](https://codeload.github.com/FFmpeg/FFmpeg/tar.gz/215351c1c37dc0345a6794f4c96af54cf30e1c7b). Source access is provided through these fixed upstream locations as well as the attached source/build kit.

## Building FFmpeg

Use a Linux environment with Docker/Buildx, Bash, Python 3 and unzip. Extract the source/build kit, then run:

```bash
bash build-ffmpeg-pinned.sh
```

The script unpacks the preserved BtbN build system, builds its Windows x64 GPL shared dependency image, and builds FFmpeg at the specified commit. The upstream `download.sh` fetches dependency source snapshots into `.cache/downloads/`. Network access is required for those source repositories and general build tools.

The application and the distributed local FFmpeg binaries were tested on Windows. The FFmpeg build recipe is supplied for rebuilding; this Windows release run did not recompile FFmpeg itself.

## 简体中文

离线程序包内置本地主构建 `N-128122-g215351c1c3-20261027` 的 FFmpeg、FFprobe 及配套 DLL，采用 **GPL-3.0-or-later**。原始许可文件保存在 `tools/ffmpeg/LICENSE.txt`。

同一 Release 中的 **FFmpeg-N-128122-source-build-kit.zip** 包含 FFmpeg 精确提交的主源码、BtbN 对应日期的构建配方和补丁、MIT 许可、二进制构建配置，以及依赖源码清单。依赖清单记录每个上游仓库和固定 Git 提交／标签或 SVN 修订号；依赖源码通过这些上游位置提供。清单覆盖整个上游构建系统，也包含当前平台或变体未启用的配方。

在具备 Docker/Buildx、Bash、Python 3 和 unzip 的 Linux 环境中，运行 `bash build-ffmpeg-pinned.sh` 可使用固定的 FFmpeg 提交构建。上游下载脚本会将依赖源码快照放入 `.cache/downloads/`；首次准备构建工具和依赖源码需要联网。

本次验证了应用与所分发的本地 FFmpeg 二进制，未在 Windows 上重新编译 FFmpeg。应用自身的对应源码另见 `BiliCacheExtractor-v1.3.1-source.zip`。
