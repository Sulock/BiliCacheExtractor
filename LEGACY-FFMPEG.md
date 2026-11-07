# FFmpeg 4.2.2 legacy-driver setup

Run `Install-Legacy-FFmpeg.cmd` once, then start BiliCacheExtractor normally.
Setup downloads imageio-ffmpeg 0.4.9's Windows x64 wheel from PyPI, validates
the archive and embedded executable using SHA-256, and extracts only FFmpeg
to `tools/ffmpeg-compat.exe`. Python installation is not needed. Subsequent
use is offline. The application's newer FFmpeg and FFprobe remain in place.

The downloaded executable is byte-for-byte identical to the locally verified
FFmpeg 4.2.2 compatibility executable. On RTX 4060 Ti with driver 560.94,
automatic selection uses this executable for NVIDIA H.264 and H.265 encoding.
The application tests each encoder before using it. Lossless extraction does
not require a video encoder.

Without internet on the target computer, download the wheel at the URL in
`tools/ffmpeg-compat.lock.json` elsewhere and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-Legacy-FFmpeg.ps1 -ArchivePath 'C:\Downloads\imageio_ffmpeg-0.4.9-py3-none-win_amd64.whl'
```

The same checksum checks apply. Setup preserves an existing different
compatibility executable and asks you to move it to a backup folder first.

FFmpeg copyright (c) 2000-2019 the FFmpeg developers and the contributors to
its included libraries. The executable reports **GPL-3.0-or-later**. The
full GPLv3 text is supplied in `LICENSE`; the application's own license
is GPL-3.0-only. imageio-ffmpeg's Python wrapper uses BSD-2-Clause; its
wrapper code is not installed or bundled by this setup script.

Verified provenance:

- [imageio-ffmpeg 0.4.9 on PyPI](https://pypi.org/project/imageio-ffmpeg/0.4.9/).
- Wheel SHA-256: `7e900c695c6541b1cb17feb1baacd4009b30a53a45b81c23d53a67ab13ffb766`.
- Executable SHA-256: `404fdd541eecc577d8f24bf9a976f1e4209f4292022a41c01f5cc2f1b0d375ac`.
- [The identical imageio binary at a fixed commit](https://github.com/imageio/imageio-binaries/blob/a6bef81365f176179722fcb899039d13ced4d4af/ffmpeg/ffmpeg-win64-v4.2.2.exe).
- [FFmpeg 4.2.2 main source](https://ffmpeg.org/releases/ffmpeg-4.2.2.tar.xz).

The historical dependency revisions and complete build materials for this
static binary have not been recovered. This package supplies a setup script;
the old executable is obtained directly by the user from its upstream host.

## 简体中文

首次运行 `Install-Legacy-FFmpeg.cmd`，联网下载约 22 MB 的上游发行包，校验后
自动放置旧版 FFmpeg。无须安装 Python，以后可以离线使用。下载的 FFmpeg
与本地已验证可用的 4.2.2 完全相同。打开软件后选择 H.264/H.265 和「自动」
或「NVIDIA」即可。

该 FFmpeg 采用 GPL-3.0-or-later。历史构建的完整依赖源码和构建材料仍未找齐，
因此此方案由用户从 imageio 上游直接取得旧版文件，发行包只附自动配置脚本。

## 繁體中文

首次執行 `Install-Legacy-FFmpeg.cmd`，連網下載約 22 MB 的上游發行套件，驗證後
自動放置舊版 FFmpeg。不需要安裝 Python，之後可離線使用。下載的 FFmpeg
與本機已驗證可用的 4.2.2 完全相同。開啟軟體後選擇 H.264/H.265 與「自動」
或「NVIDIA」即可。
