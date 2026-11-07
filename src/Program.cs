// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Sulock
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Reflection;
using System.Collections.Generic;

[assembly: AssemblyTitle("Bilibili 缓存提取器")]
[assembly: AssemblyDescription("无损提取、CPU 与显卡转码")]
[assembly: AssemblyVersion("1.4.1.0")]
[assembly: AssemblyFileVersion("1.4.1.0")]
[assembly: AssemblyCopyright("Copyright (C) 2026 Sulock")]

namespace BiliExtractor
{
    static class Program
    {
        const string Usage = "用法：--hardware 查看硬件与编码能力；--scan 缓存目录；--export 缓存目录 输出目录 copy|h264|h265 [测试秒数] [--device auto|cpu|nvidia|intel|amd]。可用 --ffmpeg-dir 目录 临时指定工具。--configure-ffmpeg 打开配置窗口。设备默认 auto。";

        [STAThread]
        static int Main(string[] args)
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                var options = new List<string>(args); string explicitDirectory = null;
                int toolOption = options.IndexOf("--ffmpeg-dir");
                if (toolOption >= 0) {
                    if (toolOption + 1 >= options.Count) throw new ArgumentException(Usage);
                    explicitDirectory = options[toolOption + 1]; options.RemoveRange(toolOption, 2);
                }
                args = options.ToArray();
                if (args.Length == 1 && (args[0] == "--help" || args[0] == "-h")) { Console.WriteLine(Usage); return 0; }
                if (args.Length >= 1 && args[0] == "--install-ffmpeg") {
                    if (args.Length > 2) throw new ArgumentException("--install-ffmpeg [本地 ZIP 路径]");
                    var spec = MediaToolDownload.Load(root);
                    if (spec == null) throw new InvalidDataException("未配置推荐下载。");
                    MediaToolInstallation installed;
                    if (args.Length == 2) installed = MediaToolInstaller.InstallArchive(root, spec, args[1], CancellationToken.None);
                    else {
                        int lastProgress = -10;
                        installed = MediaToolInstaller.DownloadAsync(root, spec, CancellationToken.None,
                            (done, total) => { int percent = (int)(done * 100 / total); if (percent >= lastProgress + 10) { Console.WriteLine("Download: " + percent + "%"); lastProgress = percent; } }, Console.WriteLine).GetAwaiter().GetResult();
                    }
                    MediaTools.Save(root, installed); Console.WriteLine("FFmpeg ready: " + installed.Directory); return 0;
                }
                bool configure = args.Length == 1 && args[0] == "--configure-ffmpeg";
                string problem = null;
                var installation = explicitDirectory == null ? MediaTools.Resolve(root, out problem) : MediaTools.FindPair(explicitDirectory, "local");
                if (installation != null) {
                    var check = MediaToolInspector.Inspect(installation, CancellationToken.None, null, false);
                    if (!check.Valid) { problem = check.Error; installation = null; }
                }
                if (args.Length == 0 || configure) {
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    if (installation == null || configure) {
                        using (var dialog = new MediaToolsDialog(root, installation, new Localizer { Language = MediaTools.ReadLanguage(root) }, problem)) {
                            if (dialog.ShowDialog() != DialogResult.OK) return 0;
                            installation = dialog.SelectedInstallation;
                        }
                    }
                    if (configure) return 0;
                }
                if (installation == null) throw new FileNotFoundException(problem ?? "未找到 FFmpeg 和 FFprobe。请使用 --ffmpeg-dir 指定目录，或打开图形界面配置工具。");
                var engine = new Engine(root, installation);
                if (args.Length > 0)
                {
                    // CLI shares the exact scanner and exporter used by the desktop UI.
                    Action<string> log = s => Console.WriteLine(s);
                    if (args[0] == "--help" || args[0] == "-h") { log(Usage); return 0; }
                    if (args[0] == "--hardware" && args.Length == 1)
                    {
                        engine.ReadHardware(CancellationToken.None, log);
                        engine.DetectEncoders(CancellationToken.None, log);
                        foreach (var mode in new[] { ExportMode.H264, ExportMode.H265 }) log(engine.Describe(mode, EncodingDevice.Auto));
                        return 0;
                    }
                    if (args[0] == "--scan" && args.Length == 2)
                    {
                        foreach (var entry in engine.Scan(args[1], CancellationToken.None, log))
                            Console.WriteLine(entry.Title + " | " + entry.Resolution + " | " + entry.Status);
                        return 0;
                    }
                    if (args[0] == "--export" && args.Length >= 4)
                    {
                        ExportMode mode = ExportModes.Parse(args[3]);
                        EncodingDevice device = args[3].Equals("gpu", StringComparison.OrdinalIgnoreCase) ? EncodingDevice.Nvidia : EncodingDevice.Auto;
                        double limit = 0; bool hasLimit = false, hasDevice = false;
                        for (int i = 4; i < args.Length; i++)
                        {
                            if (args[i] == "--device" && !hasDevice && i + 1 < args.Length)
                            { device = EncodingDevices.Parse(args[++i]); hasDevice = true; }
                            else if (!hasLimit && !args[i].StartsWith("--", StringComparison.Ordinal))
                            { limit = double.Parse(args[i], System.Globalization.CultureInfo.InvariantCulture); hasLimit = true; }
                            else throw new ArgumentException(Usage);
                        }
                        if (hasLimit && (double.IsNaN(limit) || double.IsInfinity(limit) || limit <= 0))
                            throw new ArgumentException("测试秒数必须是有限的正数；导出完整视频时省略该参数。");
                        if (mode != ExportMode.Copy)
                        {
                            if (device != EncodingDevice.Cpu) engine.ReadHardware(CancellationToken.None, log);
                            engine.DetectEncoders(CancellationToken.None, log, device);
                        }
                        if (!engine.CanExport(mode, device)) throw new InvalidOperationException(engine.UnavailableMessage(mode, device));
                        var entries = engine.Scan(args[1], CancellationToken.None, log);
                        if (!entries.Any(e => e.Ready)) throw new InvalidOperationException("没有可提取的缓存。");
                        foreach (var e in entries.Where(e => e.Ready))
                            log(engine.Export(e, args[2], mode, device, true, CancellationToken.None, p => { }, log, limit));
                        return 0;
                    }
                    throw new ArgumentException(Usage);
                }
                Application.Run(new MainForm(engine)); return 0;
            }
            catch (Exception ex)
            {
                if (args.Length > 0) Console.Error.WriteLine(ex.ToString());
                else MessageBox.Show(ex.Message, "缓存提取器", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
