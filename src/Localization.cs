using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace BiliExtractor
{
    public sealed class Localizer
    {
        public string Language { get; set; }
        public Localizer() { Language = "zh-CN"; }
        public static string Normalize(string value)
        {
            return value == "en" || value == "en-US" ? "en" : value == "zh-TW" || value == "zh-Hant" ? "zh-TW" : "zh-CN";
        }
        public string Get(string key, params object[] args)
        {
            string value = key;
            if (Language == "en") { string translated; if (English.TryGetValue(key, out translated)) value = translated; }
            else if (Language == "zh-TW") value = Traditional(key);
            return args.Length == 0 ? value : string.Format(CultureInfo.InvariantCulture, value, args);
        }
        public static bool HasEnglish(string key) { return English.ContainsKey(key); }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int LCMapStringEx(string locale, uint flags, string source, int sourceLength,
            StringBuilder destination, int destinationLength, IntPtr version, IntPtr reserved, IntPtr parameter);
        static string Traditional(string value)
        {
            var buffer = new StringBuilder(value.Length * 2 + 1);
            if (LCMapStringEx("zh-CN", 0x04000000, value, -1, buffer, buffer.Capacity, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) == 0) return value;
            return buffer.ToString();
        }
        static readonly Dictionary<string, string> English = new Dictionary<string, string> {
            { "Bilibili 缓存提取器", "Bilibili Cache Extractor" },
            { "Bilibili 缓存提取", "Bilibili Cache Extractor" },
            { "把本地缓存保存为 MP4 · 无损合并 / H.264 / H.265 · CPU 与显卡均可使用", "Save cached videos as MP4 · Lossless / H.264 / H.265 · CPU and GPU encoding" },
            { "语言", "Language" },
            { "CPU：正在读取硬件信息…", "CPU: Reading hardware information…" },
            { "核心、线程与频率", "Cores, threads and frequency" },
            { "GPU：正在读取型号与显存…", "GPU: Reading model and memory…" },
            { "CPU 型号", "CPU model" }, { "CPU 规格", "CPU specifications" },
            { "GPU 型号与显存", "GPU model and memory" }, { "重新检测", "Detect again" },
            { "缓存目录", "Cache folder" }, { "输出目录", "Output folder" },
            { "选择…", "Browse…" }, { "扫描缓存", "Scan cache" }, { "打开输出", "Open output" },
            { "输出目录将在首次提取时创建。", "The output folder will be created when you extract a video." },
            { "输出格式", "Format" }, { "编码设备", "Encoder" },
            { "无损提取（推荐，保留原画质）", "Lossless (keep original)" },
            { "转为 H.264（兼容性优先）", "H.264 (compatibility)" },
            { "转为 H.265（HEVC）", "H.265 (HEVC)" },
            { "GPU 解码（NVIDIA）", "GPU decode (NVIDIA)" },
            { "全选", "Select all" }, { "全不选", "Clear all" }, { "仅选当前行", "Select current" },
            { "搜索", "Search" }, { "搜索视频", "Search videos" },
            { "选择", "Select" }, { "视频标题", "Video title" }, { "分辨率", "Resolution" },
            { "时长", "Duration" }, { "缓存大小", "Cache size" }, { "状态", "Status" },
            { "缓存视频列表", "Cached videos" }, { "运行日志", "Activity log" },
            { "准备扫描缓存…", "Ready to scan cache…" },
            { "实际设备与处理速度将在任务开始后显示。", "The actual encoder and processing speed will appear here." },
            { "批量任务按顺序处理 · 同名文件自动编号", "Sequential jobs · Existing files are kept" },
            { "取消任务", "Cancel task" }, { "正在取消当前任务…", "Cancelling the current task…" },
            { "正在结束任务…", "Finishing the current task…" },
            { "提取所选视频", "Extract selected" }, { "提取所选（{0}）", "Extract selected ({0})" },
            { "删除缓存", "Delete cache" },
            { "缓存已改变，请重新扫描", "Cache changed; scan again" },
            { "已取消 · 成功 {0} · 失败 {1} · 用时 {2} 秒", "Cancelled · Succeeded {0} · Failed {1} · {2} seconds" },
            { "任务结束 · 成功 {0} · 失败 {1} · 用时 {2} 秒", "Finished · Succeeded {0} · Failed {1} · {2} seconds" },
            { "选择 MP4 输出目录", "Choose an MP4 output folder" },
            { "选择 Bilibili 缓存目录", "Choose a Bilibili cache folder" },
            { "读取设置失败：{0}", "Could not load settings: {0}" },
            { "设置未保存：{0}", "Could not save settings: {0}" },
            { "直接合并音视频，通常最快；H.264 / H.265 等源编码保持原样。", "Copies video and audio without re-encoding. Keeps the original codec and quality." },
            { "H.265 压缩效率较高，播放设备需支持 HEVC；CPU 编码较耗时，音频保持原样。", "HEVC needs a compatible player. CPU encoding is slower; audio is copied." },
            { "H.264 适合需要广泛播放兼容性的设备，音频保持原样。", "H.264 offers broad playback compatibility. Audio is copied." },
            { "（不可用）", " (unavailable)" },
            { "GPU：未读取到显卡信息，编码能力以实际检测为准。", "GPU details unavailable. Encoder availability is determined by a test encode." },
            { "GPU {0}：{1} · {2}", "GPU {0}: {1} · {2}" }, { "CPU：{0}", "CPU: {0}" },
            { "正在读取 CPU、GPU 并测试编码能力…", "Reading CPU/GPU details and testing encoders…" },
            { "硬件检测完成，可选择输出格式和编码设备。", "Hardware detection complete. Choose a format and encoder." },
            { "已取消硬件检测。", "Hardware detection cancelled." },
            { "硬件检测失败：{0}", "Hardware detection failed: {0}" },
            { "正在识别缓存和音视频轨道…", "Scanning cache and identifying media tracks…" },
            { "未发现 .m4s 缓存。请选择包含下载缓存的目录，再扫描。", "No .m4s cache found. Choose a download cache folder and scan again." },
            { "扫描完成：{0} 个视频，{1} 个可提取。请勾选要处理的条目。", "Scan complete: {0} entries, {1} ready to extract. Select the entries to process." },
            { "已取消扫描。", "Scan cancelled." }, { "扫描失败：{0}", "Scan failed: {0}" },
            { "共 {0} 项 · 已选 {1}", "Total {0} · Selected {1}" }, { "（隐藏 {0}）", " ({0} hidden)" },
            { "存在多条轨道，已选择最高分辨率视频和最大音频轨。", "Multiple tracks found. The highest-resolution video and largest audio track are selected." },
            { "下载未完成", "Incomplete" }, { "缺少视频轨", "Missing video" }, { "缺少音频轨", "Missing audio" },
            { "待提取", "Ready" }, { "处理中…", "Processing…" }, { "处理中 {0}%", "Processing {0}%" },
            { "已完成", "Completed" }, { "已取消", "Cancelled" }, { "失败（见日志）", "Failed (see log)" },
            { "请先勾选要提取的视频。", "Select at least one video ready to extract." },
            { "缓存目录已改变，请先点击“扫描缓存”。", "The cache folder has changed. Scan it again first." },
            { "请选择输出目录。", "Choose an output folder." },
            { "正在处理 {0} / {1}：{2}", "Processing {0} / {1}: {2}" },
            { "已保存：{0}", "Saved: {0}" }, { "任务已取消，已完成的视频保留。", "Task cancelled. Completed videos have been kept." },
            { "任务结束", "Finished" }, { "{0} · 成功 {1} · 失败 {2} · 用时 {3} 秒", "{0} · Succeeded {1} · Failed {2} · {3} seconds" },
            { "无损提取", "Lossless extraction" }, { "自动选择（推荐）", "Automatic (recommended)" },
            { "CPU 软件编码", "CPU software encoding" }, { "NVIDIA 显卡（NVENC）", "NVIDIA GPU (NVENC)" },
            { "Intel 显卡（Quick Sync）", "Intel GPU (Quick Sync)" }, { "AMD 显卡（AMF）", "AMD GPU (AMF)" },
            { "{0} 的{1}不可用，请重新检测设备或选择其他编码设备。详情见日志。", "{1} is unavailable for {0}. Detect again or choose another encoder. See the log for details." },
            { "保留源编码与画质 · 无需视频编码器", "Keeps the source codec and quality · No video encoding needed" },
            { "正在检测可用编码设备…", "Detecting available encoders…" },
            { "自动选择：{0}", "Automatic: {0}" }, { "将使用：{0}", "Will use: {0}" },
            { "编码实测通过：{0}；工具：{1}", "Encoder test passed: {0}; tool: {1}" },
            { "{0}（{1}）检测失败：{2}", "{0} ({1}) test failed: {2}" }, { "{0} 检测失败：{1}", "{0} test failed: {1}" },
            { "GPU 解码", "GPU decoding" }, { "CPU 解码", "CPU decoding" }, { "音视频直接合并", "Copying video and audio" },
            { "{0} · 处理速度 {1}", "{0} · Speed {1}" },
            { "GPU 解码路径失败，改用 CPU 解码重试；输出格式保持 {0}。", "GPU decoding failed. Retrying with CPU decoding; the output stays {0}." },
            { "自动重试：{0}；输出格式保持 {1}。", "Automatic retry: {0}; the output stays {1}." },
            { "输出校验失败：音视频轨不完整。", "Output validation failed: missing video or audio." },
            { "输出校验失败：视频编码与所选模式不符。", "Output validation failed: the video codec does not match the selected format." },
            { "输出校验失败：音频编码发生变化。", "Output validation failed: the audio codec changed." },
            { "输出校验失败：H.265 封装标记不正确。", "Output validation failed: the HEVC container tag is incorrect." },
            { "输出校验失败：视频时长不足。", "Output validation failed: the video is too short." },
            { "缓存目录不存在：{0}", "Cache folder does not exist: {0}" },
            { "扫描：{0}", "Scanning: {0}" }, { "跳过目录 {0}：{1}", "Skipping folder {0}: {1}" },
            { "元数据读取失败，使用目录名：{0}", "Could not read metadata; using the folder name: {0}" },
            { "无法识别 {0}：{1}", "Could not identify {0}: {1}" },
            { "{0}：存在多个候选轨道，使用 {1}", "{0}: multiple candidate tracks; using {1}" }, { "无视频", "no video" },
            { "CPU 型号未读取", "CPU model unavailable" }, { "频率未读取", "Frequency unavailable" }, { "未知", "unknown" },
            { "{0} 核 {1} 线程 · {2}", "{0} cores / {1} threads · {2}" },
            { "系统报告频率 {0}", "Reported frequency {0}" }, { "系统标称频率 {0}", "Nominal frequency {0}" },
            { "专用显存 {0} · 共享系统内存上限 {1}", "Dedicated VRAM {0} · Shared memory limit {1}" }, { "未读取", "unavailable" },
            { "CPU 详细信息：{0}", "CPU details: {0}" }, { "GPU 信息读取失败：{0}", "Could not read GPU details: {0}" },
            { "确认删除缓存", "Confirm cache deletion" }, { "移入回收站", "Move to Recycle Bin" }, { "取消", "Cancel" },
            { "将 {0} 个勾选条目的 {1} 个缓存文件（{2}）移入回收站。", "Selected entries: {0} · Cache files: {1} ({2}).\nMove these files to the Recycle Bin?" },
            { "其中 {0} 项被当前搜索条件隐藏。", "Selected entries hidden by the current search: {0}." },
            { "仅处理下列缓存文件，已提取的 MP4 保留。", "Only the cache files listed below will be recycled. Exported MP4 files are kept." },
            { "请先勾选要删除的缓存条目。", "Select the cache entries to delete first." },
            { "正在将缓存移入回收站…", "Moving cache files to the Recycle Bin…" },
            { "已移入回收站：{0}", "Moved to the Recycle Bin: {0}" },
            { "回收站处理结束：成功 {0} 个文件，失败 {1} 个。", "Recycle operation finished: {0} files recycled, {1} failed." },
            { "删除缓存失败：{0}", "Could not delete cache: {0}" },
            { "缓存删除已取消。", "Cache deletion cancelled." },
            { "没有可删除的缓存文件，请重新扫描。", "No cache files to delete. Scan again." },
            { "缓存文件不在扫描目录内：{0}", "The cache file is outside the scanned folder: {0}" },
            { "仅可回收扫描到的 .m4s 与 videoInfo.json 文件：{0}", "Only scanned .m4s and videoInfo.json files can be recycled: {0}" },
            { "不支持通过链接删除缓存：{0}", "Cache deletion through symbolic links or junctions is not supported: {0}" },
            { "缓存文件已改变，请重新扫描并确认：{0}", "The cache file changed. Scan and confirm again: {0}" },
            { "该位置无法安全使用回收站，文件已保留：{0}", "The Recycle Bin is not available at this location. The file was kept: {0}" },
            { "系统未执行回收站操作，文件已保留。", "Windows did not perform a recycle operation. The file was kept." },
            { "部分文件未移入回收站，请查看日志。", "Some files were not recycled. See the log for details." }
        };
    }
}
