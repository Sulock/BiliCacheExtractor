# Bilibili 緩存提取器

[English](README.md) | [简体中文](README.zh-CN.md) | **繁體中文**

<img src="assets/app-icon.png" width="96" alt="Bilibili 緩存提取器圖示">

將 Windows 上的 Bilibili 本機緩存儲存為 MP4，支援保留原始音訊與視訊壓縮資料的無損提取，以及 CPU 或顯示卡加速的 H.264/H.265 轉碼。

![繁體中文介面](assets/screenshot-zh-TW.png)

介面截圖使用合成的介面測試資料。

## 功能

- 遞迴掃描 `.m4s` 檔案，讀取 `videoInfo.json` 中的影片標題和分 P 標題。
- 無損提取、H.264 與 H.265/HEVC 三種輸出模式。
- CPU、NVIDIA NVENC、Intel Quick Sync、AMD AMF 編碼裝置選擇與自動回退。
- 批次勾選、標題搜尋、進度顯示、取消任務與同名檔案自動編號。
- 簡體中文、繁體中文和 English 介面。
- 預覽待刪除緩存，確認後移入 Windows 資源回收筒。
- 與圖形介面共用處理引擎的命令列入口。

## 開始使用

**1.3.1** 下載入口：[Release 頁面](https://github.com/Sulock/BiliCacheExtractor/releases/tag/v1.3.1) · [Windows x64 程式套件](https://github.com/Sulock/BiliCacheExtractor/releases/download/v1.3.1/BiliCacheExtractor-v1.3.1-win-x64.zip) · [原始碼 ZIP](https://github.com/Sulock/BiliCacheExtractor/releases/download/v1.3.1/BiliCacheExtractor-v1.3.1-source.zip) · [SHA-256 驗證檔案](https://github.com/Sulock/BiliCacheExtractor/releases/download/v1.3.1/SHA256SUMS.txt)。

已驗證環境為 **Windows 11 23H2 x64、.NET Framework 4.8.1**。程式編譯為 x64，介面、硬體偵測和資源回收筒功能使用 Windows API。只有重新產生圖示時需要 Python。

### 下載執行

1. 將 Windows x64 程式套件解壓縮至可寫入的目錄。
2. 按兩下 `BiliCacheExtractor.exe`。套件已包含 FFmpeg、FFprobe 及所需 DLL。

離線套件包含編譯好的 GUI、CLI、本機 FFmpeg 建置和對應的應用程式原始碼 ZIP。FFmpeg 授權條款與原始碼資訊保存在 `tools/ffmpeg/LICENSE.txt` 和 `tools/ffmpeg/SOURCE-INFO.txt`。同一 Release 提供 [FFmpeg 原始碼與建置資料](https://github.com/Sulock/BiliCacheExtractor/releases/download/v1.3.1/FFmpeg-N-128122-source-build-kit.zip)，詳細來源與建置方式請參閱[原始碼說明](third_party/FFMPEG-SOURCES.md)。顯示卡驅動程式不支援所需 NVENC API 時，內建 FFmpeg 可使用 CPU 編碼。

### 舊版 NVIDIA 驅動程式

另提供 [1.3.1 舊驅動相容版](https://github.com/Sulock/BiliCacheExtractor/releases/tag/v1.3.1-legacy)。下載其 [Windows x64 程式套件](https://github.com/Sulock/BiliCacheExtractor/releases/download/v1.3.1-legacy/BiliCacheExtractor-v1.3.1-legacy-win-x64.zip)，解壓縮後先執行 `Install-Legacy-FFmpeg.cmd`，連網下載約 22 MB，再開啟軟體；之後可離線使用。腳本取得的 FFmpeg 4.2.2 與原本機相容檔案完全相同，已在 RTX 4060 Ti、驅動程式 560.94 上驗證 NVIDIA H.264/H.265 編碼，自動模式會實測並選擇可用編碼器。請參閱[設定、來源與授權條款](LEGACY-FFMPEG.md)。

重現此發行變體時，在下方 `package.ps1` 命令中加入 `-LegacySetup`。應用程式版本仍為 1.3.1，`-legacy` 後綴用於標示封裝方式。

### 從原始碼建置

1. 解壓縮原始碼 ZIP 或複製本儲存庫。
2. 執行 `Install-FFmpeg.cmd`，或依照[媒體工具說明](tools/README.md)手動設定 FFmpeg/FFprobe。
3. 在原始碼目錄的 PowerShell 中執行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

4. 按兩下產生的 `BiliCacheExtractor.exe`。

建置使用 Windows .NET Framework C# 編譯器，無需下載 NuGet 套件。編譯 C# 不需要 FFmpeg；執行軟體和媒體測試時需要。系統缺少 .NET Framework 4.8.1 時，請先安裝該執行階段。

## 圖形介面

選擇緩存目錄並掃描，勾選影片，設定輸出目錄和格式後按一下「提取所選」。預設緩存目錄為目前使用者 Windows 影片資料夾下的 `bilibili`，預設輸出至程式旁的 `提取的视频` 資料夾。

| 模式 | 運作方式 |
| --- | --- |
| 無損提取 | 複製音訊與視訊壓縮資料並封裝為 MP4，保留來源編碼與畫質，通常最快。 |
| 轉為 H.264 | 重新編碼視訊，直接複製音訊，適合需要廣泛播放相容性的情況。 |
| 轉為 H.265（HEVC） | 重新編碼為 HEVC，使用 MP4 `hvc1` 標記，直接複製音訊，需要播放器支援 HEVC。 |

程式先用短片實際初始化各編碼器，再提供可用裝置。自動模式按 NVIDIA → Intel → AMD → CPU 的順序選擇；執行時硬體編碼失敗會嘗試下一種可用裝置，保持輸出格式不變。手動選擇裝置時，該裝置失敗會回報錯誤。NVIDIA 路徑可使用 GPU 解碼，失敗後會先嘗試 CPU 解碼。

搜尋會保留既有勾選，並顯示被搜尋隱藏的勾選數量。「全選」選擇目前的搜尋結果，「全不選」清除全部勾選。任務依序執行；取消會終止目前任務並清理暫存檔案，已完成的影片則會保留。同名輸出自動加上 `(2)`、`(3)` 等編號。

尚未下載完成、損壞或缺少音訊與視訊軌道的緩存會顯示原因。存在多條候選軌道時，視訊優先選擇最高解析度，相同解析度則選擇較大的檔案；音訊選擇最大的軌道。支援一般 `.m4s` 和帶有九位元組 `000000000` 前綴的輸入。

### 刪除緩存

紅色「刪除緩存」按鈕會開啟確認視窗，列出所選檔案和總容量，預設操作為「取消」。確認後，僅將所選項目掃描到的 `.m4s` 與 `videoInfo.json` 移入 Windows 資源回收筒，可以從資源回收筒還原。已匯出的 MP4、無關檔案和目錄會保留。

刪除前會檢查掃描目錄、檔案類型、連結路徑，以及確認後檔案是否改變。不支援相應資源回收筒操作的位置會回報錯誤並保留檔案。

### 設定與記錄

程式旁的 `settings.json` 儲存語言、目錄、輸出格式和裝置選項。程式目錄和輸出目錄需要可寫入。介面記錄顯示在視窗內，CLI 記錄輸出至終端機；測試結果放在 `verification/`。

## 命令列

```powershell
$cachePath = Join-Path ([Environment]::GetFolderPath('MyVideos')) 'bilibili'
.\BiliCacheExtractor.Cli.exe --help
.\BiliCacheExtractor.Cli.exe --hardware
.\BiliCacheExtractor.Cli.exe --scan $cachePath
.\BiliCacheExtractor.Cli.exe --export $cachePath '.\output' copy
.\BiliCacheExtractor.Cli.exe --export $cachePath '.\output' h264 --device cpu
.\BiliCacheExtractor.Cli.exe --export $cachePath '.\output' h265 --device nvidia
```

`--export` 處理指定目錄下的全部可提取影片。裝置參數為 `auto`（預設）、`cpu`、`nvidia`、`intel`、`amd`。測試時可額外傳入正數秒數限制輸出長度；舊參數 `gpu` 等同於 `h264 --device nvidia`。

## 開發與測試

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run.ps1
```

重新編譯前請關閉圖形介面。測試預設產生合成的音訊、視訊與緩存，不需要個人影片。可用 `-CachePath 'C:\path\to\cache'` 指定包含可提取影片的真實緩存，用於取消測試。加上 `-Recycle` 會額外對 `verification/` 中由測試建立的檔案執行真實回收與還原。

2026-11-06 驗證通過 **39 項媒體測試、24 項介面測試、7 項資源回收筒計畫測試**。涵蓋 CPU 與 NVIDIA H.264/H.265 真實轉碼、輸出解碼、壓縮資料雜湊、裝置回退及取消清理。NVIDIA 測試在編碼器可用時執行。Intel/AMD 選擇邏輯使用模擬工具驗證，該次驗證沒有相應的真實硬體；預設驗證未執行可選的真實回收與還原測試。

原始碼結構：

- `src/`：介面、語言、緩存掃描、匯出、硬體偵測和緩存回收。
- `tests/`：媒體、介面及資源回收筒測試。
- `assets/`：可編輯的 SVG 圖示與產生的 PNG、ICO。
- `tools/make-icon.py`：可選的圖示產生器，需要 Python 3.9+、Pillow 與 NumPy。
- `build.ps1`：使用系統 C# 編譯器產生 GUI 與 CLI。
- `Install-FFmpeg.ps1` 與 `tools/ffmpeg.lock.json`：驗證下載後安裝固定版本的媒體工具。
- `package.ps1`：從已提交的原始碼建置發布套件，附上對應原始碼 ZIP 和 SHA-256 驗證檔案。

重現離線套件可執行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\package.ps1 -Version 1.3.1 -Commit HEAD -FFmpegDirectory 'C:\path\to\ffmpeg' -FFmpegSourceArchive 'C:\path\to\FFmpeg-215351c1c3-source.tar.gz'`。請使用離線套件中的 FFmpeg 檔案，以及原始碼資料套件中的主原始碼歸檔，腳本會核對固定雜湊。輸出位於 `release-v1.3.1/`；再次建置時請用 `-OutputDirectory` 指定新的輸出目錄。

## 回報問題

請在 [Issues](https://github.com/Sulock/BiliCacheExtractor/issues) 提供 Windows 版本、程式版本、FFmpeg 版本、所選格式／裝置與重現步驟。分享記錄和截圖前，請檢查其中的路徑、影片標題及其他個人資訊。

## 授權條款

專案採用 **GPL-3.0-only**，完整正文請參閱 [LICENSE](LICENSE)。FFmpeg 等外部元件遵循各自的授權條款，詳見[第三方說明](THIRD_PARTY_NOTICES.md)。

Copyright (C) 2026 Sulock.
