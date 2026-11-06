using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace BiliExtractor
{
    public enum ExportMode { Copy, H264, H265 }
    public enum EncodingDevice { Auto, Cpu, Nvidia, Intel, Amd }

    public static class ExportModes
    {
        public static ExportMode Parse(string value)
        {
            switch ((value ?? "").ToLowerInvariant())
            {
                case "copy": return ExportMode.Copy;
                case "gpu": case "h264": return ExportMode.H264;
                case "h265": return ExportMode.H265;
                default: throw new ArgumentException("输出格式应为 copy、h264 或 h265；gpu 是 NVIDIA H.264 的兼容写法。");
            }
        }
        public static string Key(ExportMode mode)
        {
            switch (mode)
            {
                case ExportMode.Copy: return "copy";
                case ExportMode.H264: return "h264";
                case ExportMode.H265: return "h265";
                default: throw new ArgumentOutOfRangeException("mode");
            }
        }
        public static string Label(ExportMode mode)
        {
            switch (mode)
            {
                case ExportMode.Copy: return "无损提取";
                case ExportMode.H264: return "H.264";
                case ExportMode.H265: return "H.265";
                default: throw new ArgumentOutOfRangeException("mode");
            }
        }
    }
    public static class EncodingDevices
    {
        public static EncodingDevice Parse(string value)
        {
            switch ((value ?? "auto").ToLowerInvariant())
            {
                case "auto": return EncodingDevice.Auto;
                case "cpu": return EncodingDevice.Cpu;
                case "nvidia": return EncodingDevice.Nvidia;
                case "intel": return EncodingDevice.Intel;
                case "amd": return EncodingDevice.Amd;
                default: throw new ArgumentException("编码设备应为 auto、cpu、nvidia、intel 或 amd。");
            }
        }
        public static string Key(EncodingDevice device) { return device.ToString().ToLowerInvariant(); }
        public static string Label(EncodingDevice device)
        {
            switch (device)
            {
                case EncodingDevice.Auto: return "自动选择（推荐）";
                case EncodingDevice.Cpu: return "CPU 软件编码";
                case EncodingDevice.Nvidia: return "NVIDIA 显卡（NVENC）";
                case EncodingDevice.Intel: return "Intel 显卡（Quick Sync）";
                case EncodingDevice.Amd: return "AMD 显卡（AMF）";
                default: throw new ArgumentOutOfRangeException("device");
            }
        }
        public static string Suffix(EncodingDevice device)
        {
            return device == EncodingDevice.Nvidia ? "NVENC" : device == EncodingDevice.Intel ? "QSV" :
                device == EncodingDevice.Amd ? "AMF" : "CPU";
        }
    }

    public sealed class EncoderPlan
    {
        public ExportMode Mode;
        public EncodingDevice Device;
        public string FFmpeg;
        public string Codec { get { return Mode == ExportMode.H265 ? "hevc" : "h264"; } }
        public string Display { get { return ExportModes.Label(Mode) + " · " + EncodingDevices.Label(Device); } }
        public string DisplayText(Localizer text) { return text.Get(ExportModes.Label(Mode)) + " · " + text.Get(EncodingDevices.Label(Device)); }
        public void AddVideoArguments(List<string> args, bool cuda)
        {
            if (!cuda) args.AddRange(new[] { "-pix_fmt", Device == EncodingDevice.Intel || Device == EncodingDevice.Amd ? "nv12" : "yuv420p" });
            if (Device == EncodingDevice.Cpu)
                args.AddRange(new[] { "-c:v", Mode == ExportMode.H265 ? "libx265" : "libx264", "-preset", "fast", "-crf", Mode == ExportMode.H265 ? "24" : "20" });
            else if (Device == EncodingDevice.Nvidia)
                args.AddRange(new[] { "-c:v", Codec + "_nvenc", "-preset", "fast", "-rc", "vbr", "-cq", "20", "-b:v", "0" });
            else if (Device == EncodingDevice.Intel)
                args.AddRange(new[] { "-c:v", Codec + "_qsv", "-preset", "fast", "-global_quality", "23" });
            else if (Device == EncodingDevice.Amd)
                args.AddRange(new[] { "-c:v", Codec + "_amf", "-quality", "balanced", "-rc", "cqp", "-qp_i", "22", "-qp_p", "22" });
            else throw new ArgumentOutOfRangeException("Device");
        }
    }

    public sealed partial class Engine
    {
        readonly Dictionary<string, EncoderPlan> encoders = new Dictionary<string, EncoderPlan>();
        static readonly EncodingDevice[] preference = { EncodingDevice.Nvidia, EncodingDevice.Intel, EncodingDevice.Amd, EncodingDevice.Cpu };
        public HardwareInfo Hardware { get; private set; }
        public bool EncodersDetected { get; private set; }
        public void ReadHardware(CancellationToken token, Action<string> log) { Hardware = HardwareInfo.Read(token, log, Text); }
        static string EncoderKey(ExportMode mode, EncodingDevice device) { return ExportModes.Key(mode) + ":" + EncodingDevices.Key(device); }

        public List<EncoderPlan> Plans(ExportMode mode, EncodingDevice device)
        {
            ExportModes.Key(mode); EncodingDevices.Label(device);
            var plans = new List<EncoderPlan>();
            if (mode == ExportMode.Copy) return plans;
            foreach (var candidate in device == EncodingDevice.Auto ? preference : new[] { device })
            {
                EncoderPlan plan;
                if (encoders.TryGetValue(EncoderKey(mode, candidate), out plan)) plans.Add(plan);
            }
            return plans;
        }
        public bool CanExport(ExportMode mode, EncodingDevice device = EncodingDevice.Auto)
        {
            return mode == ExportMode.Copy || Plans(mode, device).Count > 0;
        }
        public string UnavailableMessage(ExportMode mode, EncodingDevice device = EncodingDevice.Auto)
        {
            return Text.Get("{0} 的{1}不可用，请重新检测设备或选择其他编码设备。详情见日志。", Text.Get(ExportModes.Label(mode)), Text.Get(EncodingDevices.Label(device)));
        }
        public string Describe(ExportMode mode, EncodingDevice device)
        {
            if (mode == ExportMode.Copy) return Text.Get("保留源编码与画质 · 无需视频编码器");
            var plan = Plans(mode, device).FirstOrDefault();
            if (plan == null) return EncodersDetected ? UnavailableMessage(mode, device) : Text.Get("正在检测可用编码设备…");
            return Text.Get(device == EncodingDevice.Auto ? "自动选择：{0}" : "将使用：{0}", plan.DisplayText(Text));
        }
        public bool CanUseCuda(ExportMode mode, EncodingDevice device)
        {
            var plan = Plans(mode, device).FirstOrDefault();
            return plan != null && plan.Device == EncodingDevice.Nvidia;
        }
        public void DetectEncoders(CancellationToken token, Action<string> log, EncodingDevice requested = EncodingDevice.Auto)
        {
            encoders.Clear(); EncodersDetected = false;
            foreach (EncodingDevice device in new[] { EncodingDevice.Cpu, EncodingDevice.Nvidia, EncodingDevice.Intel, EncodingDevice.Amd })
            {
                if (requested != EncodingDevice.Auto && device != requested) continue;
                uint vendor = device == EncodingDevice.Nvidia ? 0x10deU : device == EncodingDevice.Intel ? 0x8086U : 0x1002U;
                if (device != EncodingDevice.Cpu && Hardware != null && Hardware.Gpus.Count > 0 && !Hardware.Gpus.Any(g => g.VendorId == vendor)) continue;
                foreach (ExportMode mode in new[] { ExportMode.H264, ExportMode.H265 })
                {
                    foreach (string candidate in new[] { FFmpeg, Path.Combine(Root, "tools", "ffmpeg-compat.exe") }.Distinct())
                    {
                        token.ThrowIfCancellationRequested();
                        if (!File.Exists(candidate)) continue;
                        var plan = new EncoderPlan { Mode = mode, Device = device, FFmpeg = candidate };
                        try
                        {
                            var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i",
                                "color=c=black:s=640x360:r=30", "-frames:v", "10" };
                            plan.AddVideoArguments(args, false); args.AddRange(new[] { "-f", "null", "-" });
                            var result = ChildProcess.Run(candidate, args, token, null, 15);
                            if (result.Code == 0)
                            {
                                encoders[EncoderKey(mode, device)] = plan;
                                log(Text.Get("编码实测通过：{0}；工具：{1}", plan.DisplayText(Text), Path.GetFileName(candidate))); break;
                            }
                            log(Text.Get("{0}（{1}）检测失败：{2}", plan.DisplayText(Text), Path.GetFileName(candidate), FirstLine(result.Error)));
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) { log(Text.Get("{0} 检测失败：{1}", plan.DisplayText(Text), ex.Message)); }
                    }
                }
            }
            EncodersDetected = true;
        }
    }
}
