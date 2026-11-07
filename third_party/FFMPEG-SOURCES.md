# FFmpeg source and build information

BiliCacheExtractor 1.4.0 bundles **FFmpeg 8.1.3-bce-nv12.2** for Windows x64 and matching FFprobe. The static build uses **NVENC API 12.2** (nv-codec-headers 12.2.72.0). The NVIDIA interface requires Windows driver 551.76 or newer; actual encoder availability is checked on the user's computer.

## Downloads

The [1.4.0 Release](https://github.com/Sulock/BiliCacheExtractor/releases/tag/v1.4.0) provides both:

- [FFmpeg runtime ZIP](https://github.com/Sulock/BiliCacheExtractor/releases/download/v1.4.0/FFmpeg-8.1.3-bce-nv12.2-win-x64.zip).
- [Corresponding source and build kit](https://github.com/Sulock/BiliCacheExtractor/releases/download/v1.4.0/FFmpeg-8.1.3-bce-nv12.2-source-kit.zip).

The source kit contains the source inputs for FFmpeg, nv-codec-headers, x264, x265, dav1d, zlib, AMD AMF headers and Intel libvpl. All eight revisions are fixed in [portable-build/sources.json](portable-build/sources.json). AMF is a header-only build dependency here: the complete public headers and root notices are retained; unused SDK samples, sample media and third-party binary files are excluded.

The kit includes x265/libvpl build patches, the original build script, a standalone variant, configure diagnostics, the Ubuntu package inventory, compiler-runtime copyright notices and archive hashes. Read the kit's README.md before rebuilding. The convenience script fetches pinned repositories and requires network access; the source archives are also supplied for inspection and independent builds.

## Build provenance

The Windows executable bytes were produced by [GitHub Actions run 42353389139](https://github.com/Sulock/BiliCacheExtractor/actions/runs/42353389139), using recipe commit `a99e65056917d714082f599ee4e8cdd3c636b7e1`. [Run 42354838999](https://github.com/Sulock/BiliCacheExtractor/actions/runs/42354838999) packages the used source inputs and notices; it verifies that the executable hashes remain unchanged.

FFmpeg source commit: `1041abdc962f4cc4f394aa8de9dc5236c0c3b9e7` (tag `n8.1.3`). NVENC headers commit: `c69278340ab1d5559c7d7bf0edf615dc33ddbba7` (tag `n12.2.72.0`). Runtime/archive hashes are pinned in `tools/ffmpeg-bundle.lock.json` and `tools/ffmpeg-recommended.json`.

## Licenses

The resulting FFmpeg build is **GPL-3.0-or-later**. Its GPL text is in `tools/ffmpeg/LICENSE.txt`; component notices are in `tools/ffmpeg/licenses/`. x264 and x265 use GPL-2.0-or-later, dav1d uses BSD-2-Clause, zlib uses the zlib license, and nv-codec-headers/AMF/libvpl use MIT. Source archives retain original project notices. Compiler-runtime notices and the BtbN MIT recipe notice are also retained. Windows and graphics drivers are provided by the system.

The application's own source and license remain **GPL-3.0-only** and are supplied separately in `BiliCacheExtractor-v1.4.0-source.zip`.

## 简体中文

1.4.0 内置 FFmpeg 8.1.3 的静态便携构建和 FFprobe，固定使用 NVENC API 12.2。运行包与对应源码资料包均在同一 Release 提供，哈希写入工具锁文件。

源码包包含实际参与构建的八个项目源码输入、固定修订、补丁、构建脚本和环境记录。AMF 仅使用公开头文件，因此保留完整头文件及许可声明，去掉没有参与构建的示例程序、媒体样本和第三方二进制。可按源码包 README 的说明重建；便捷脚本会联网取得固定修订。

FFmpeg 采用 GPL-3.0-or-later，各组件保留原有许可。应用自身采用 GPL-3.0-only。显卡驱动由用户系统提供。

## 繁體中文

1.4.0 內建 FFmpeg 8.1.3 靜態可攜式建置與 FFprobe，固定使用 NVENC API 12.2。執行套件與對應原始碼資料包均在同一 Release 提供。

原始碼包包含八個參與建置的專案原始碼輸入、固定修訂、補丁、建置腳本與環境記錄；AMF 保留實際使用的完整公開標頭及授權聲明。依原始碼包 README 可重新建置。FFmpeg 採 GPL-3.0-or-later，應用程式採 GPL-3.0-only。