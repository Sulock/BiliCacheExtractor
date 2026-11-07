# FFmpeg setup / 媒体工具配置 / 媒體工具設定

[English](../README.md) | [简体中文](../README.zh-CN.md) | [繁體中文](../README.zh-TW.md)

## 简体中文

1.4.1 程序包默认内置 FFmpeg 8.1.3 的 Windows x64 静态便携构建，使用 NVENC API 12.2；扫描由同包 FFprobe 完成。解压即可使用，不修改系统 PATH，也不需要安装系统服务。

在主窗口点击 **FFmpeg 设置**：

- **使用内置版本**：检测程序包里的工具，然后点击“使用所选版本”。
- **选择本地版本**：选择包含 `ffmpeg.exe` 和 `ffprobe.exe` 的目录，或其上级目录。共享构建需要保留配套 DLL。程序会检查两者版本、MP4/subfile 支持，并实际测试编码器。
- **扫描本地 PATH**：查找系统 PATH 中的本地绝对路径，以及程序已下载的版本。不会遍历整块磁盘。
- **下载推荐版本**：下载 `ffmpeg-recommended.json` 固定的版本，验证大小及 SHA-256，解压到独立目录并测试。点击“使用所选版本”后生效。取消或失败不会替换已有版本。

选项保存在程序旁 `media-tools.json`。下载的版本位于 `tools/downloads/`，应用内路径以相对路径保存，方便整体移动。下载需要联网，配置完成后可离线处理本地缓存。1.4.1 使用分段续传；短暂中断会保留已接收的数据并自动重试，用户取消时清理临时文件。

用户明确选择一套工具后，应用使用该套 FFmpeg/FFprobe，不会偷偷调用旧版 4.2.2。自动编码设备模式仍按 NVIDIA、Intel、AMD、CPU 选择可用设备；无损提取不需要视频编码器。

推荐构建的 NVIDIA 接口最低驱动为 551.76，最终能否硬件编码以实际检测为准。显卡驱动版本在配置窗口显示；低于要求时仍可使用 CPU 或选择其他本地工具。

命令行可用 `--ffmpeg-dir 'C:\path\to\bin'` 临时指定工具，不修改保存的选择。`--help` 不再要求事先安装 FFmpeg。

```powershell
.\BiliCacheExtractor.Cli.exe --hardware --ffmpeg-dir 'C:\path\to\bin'
.\BiliCacheExtractor.exe --configure-ffmpeg
# 可选：直接下载并选择推荐版本
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-FFmpeg.ps1 -Download
# 可选：从已下载且匹配固定哈希的 ZIP 配置
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-FFmpeg.ps1 -ArchivePath 'C:\Downloads\FFmpeg-8.1.3-bce-nv12.2-win-x64.zip'
```

源码和许可见 [FFMPEG-SOURCES.md](../third_party/FFMPEG-SOURCES.md)。不再需要 FFplay 或单独的 legacy 下载步骤。

## English

The 1.4.1 portable package bundles FFmpeg 8.1.3 built for Windows x64 with NVENC API 12.2, alongside matching FFprobe. No system installation or PATH modification is required.

Open **FFmpeg tools** to select the bundled version, browse for a local installation, discover tools on local PATH entries, or download the recommended version. Checks validate the FFmpeg/FFprobe pair, MP4/subfile support and real encoder initialization. Shared builds must keep their accompanying DLLs. Click **Use selected** to apply a successfully checked installation.

The recommended download is pinned by URL, length and SHA-256 in `ffmpeg-recommended.json`. It is extracted into a separate folder under `tools/downloads/`. Failed/cancelled downloads preserve existing installations. The choice is saved in `media-tools.json`; application-local paths remain relative for portability. Downloads need internet access; normal use is offline. Version 1.4.1 resumes interrupted byte ranges, retries transient failures and cleans temporary files when cancelled.

An explicit tool selection is honored without a hidden FFmpeg 4.2.2 fallback. Automatic encoder selection still tries available NVIDIA, Intel and AMD devices before CPU. Driver versions and per-codec results are displayed. The recommended NVIDIA build requires Windows driver 551.76 or later; hardware support is tested before use.

Use `--ffmpeg-dir` for a temporary CLI override. `--help` works without media tools installed. `Install-FFmpeg.ps1 -Download` installs and selects the pinned version; `-ArchivePath` uses a verified local ZIP. See [source and license information](../third_party/FFMPEG-SOURCES.md).

## 繁體中文

1.4.1 套件內建 FFmpeg 8.1.3、NVENC API 12.2 的 Windows x64 靜態可攜式建置與 FFprobe，不需要安裝至系統或修改 PATH。

點選 **FFmpeg 設定**，可使用內建版本、選擇本機版本、掃描本機 PATH，或下載建議版本。程式會檢查 FFmpeg/FFprobe 配對、MP4/subfile 支援與實際編碼能力；共享建置需保留配套 DLL。檢查完成後，點選「使用所選版本」。

下載會驗證大小與 SHA-256，放入 `tools/downloads/` 的獨立目錄；失敗或取消時保留原有安裝。選擇儲存在 `media-tools.json`，程式內的路徑採相對路徑，便於整體移動。首次下載需連網，之後可離線使用。

選定工具後，程式使用該套 FFmpeg/FFprobe，無損提取不需要視訊編碼器。驅動版本與各編碼器結果會顯示在設定視窗中。授權條款及原始碼請參閱上述連結。