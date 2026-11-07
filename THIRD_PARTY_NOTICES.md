# Third-party components

BiliCacheExtractor invokes FFmpeg and FFprobe as separate processes. Version 1.4.0 bundles the Windows x64 **FFmpeg 8.1.3-bce-nv12.2** static build and corresponding FFprobe. It uses NVENC API 12.2 and is distributed under **GPL-3.0-or-later**. Copyright belongs to the FFmpeg developers and contributors to the included libraries.

- FFmpeg project: https://ffmpeg.org/
- FFmpeg source: https://github.com/FFmpeg/FFmpeg/tree/1041abdc962f4cc4f394aa8de9dc5236c0c3b9e7
- Full license: `tools/ffmpeg/LICENSE.txt` in the portable package.
- Component notices: `tools/ffmpeg/licenses/`.
- [Corresponding source, pinned revisions and build provenance](third_party/FFMPEG-SOURCES.md).

## Compiled dependencies

| Component | License |
| --- | --- |
| x264 | GPL-2.0-or-later |
| x265 | GPL-2.0-or-later |
| dav1d | BSD-2-Clause |
| zlib | Zlib |
| nv-codec-headers | MIT |
| AMD AMF public headers | MIT |
| Intel libvpl dispatcher | MIT |

Original source notices accompany the corresponding source archives. The distributed AMF inputs include the public headers used in the binary, not unrelated SDK samples or bundled third-party binaries. Compiler-runtime copyright notices are retained in the runtime and source kit.

Some dependency configuration options follow BtbN/FFmpeg-Builds recipes. Their MIT copyright and license are retained in `third_party/ffmpeg-builds.LICENSE` and in both FFmpeg archives. The executable files are built from the separately pinned source inputs listed in `third_party/portable-build/sources.json`.

## System and development components

The installed .NET Framework, Windows APIs and GPU drivers are not redistributed. `tools/make-icon.py` optionally uses Pillow and NumPy in the developer environment; these Python packages are not embedded in the application.

The application itself is **GPL-3.0-only**, copyright Sulock. Its license does not replace the original licenses of the components above.

## 简体中文

1.4.0 内置 FFmpeg 8.1.3、NVENC API 12.2 的 Windows x64 静态构建及 FFprobe。FFmpeg 为 GPL-3.0-or-later，应用自身为 GPL-3.0-only。组件许可、版权和对应源码均按上述入口提供。

## 繁體中文

1.4.0 內建 FFmpeg 8.1.3、NVENC API 12.2 的 Windows x64 靜態建置及 FFprobe。FFmpeg 採 GPL-3.0-or-later，應用程式採 GPL-3.0-only。元件授權、版權與對應原始碼請參閱上述連結。