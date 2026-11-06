// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Sulock
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Reflection;

[assembly: AssemblyTitle("Bilibili 缓存提取器")]
[assembly: AssemblyDescription("无损提取、CPU 与显卡转码")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]
[assembly: AssemblyCopyright("Copyright (C) 2026 Sulock")]

namespace BiliExtractor
{
    static class Program
    {
        const string Usage = "用法：--hardware 查看硬件与编码能力；--scan 缓存目录；--export 缓存目录 输出目录 copy|h264|h265 [测试秒数] [--device auto|cpu|nvidia|intel|amd]。设备默认 auto，旧参数 gpu 表示 NVIDIA H.264。";

        [STAThread]
        static int Main(string[] args)
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                var engine = new Engine(root);
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
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
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
