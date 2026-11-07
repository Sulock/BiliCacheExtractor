using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace BiliExtractor
{
    public sealed class MediaTrack
    {
        public string Path, Codec, CodecTag, Kind;
        public int Prefix, Width, Height;
        public double Duration;
        public long Bytes;
    }

    public sealed class CacheEntry
    {
        public string Directory, Title, Id, Detail, Status, Output;
        public MediaTrack Video, Audio;
        public bool Selected, Ready;
        public int StatusValue;
        public bool MultipleTracks;
        public readonly List<string> CacheFiles = new List<string>();
        public double Duration { get { return Video == null ? 0 : Video.Duration; } }
        public long Bytes { get { return (Video == null ? 0 : Video.Bytes) + (Audio == null ? 0 : Audio.Bytes); } }
        public string Resolution { get { return Video == null ? "—" : Video.Width + " × " + Video.Height; } }
    }

    public sealed class ProcessResult
    {
        public int Code;
        public string Output, Error;
    }

    public static class ChildProcess
    {
        // Quote according to CommandLineToArgvW rules, including a trailing backslash.
        public static string Quote(string text)
        {
            var b = new StringBuilder("\""); int slashes = 0;
            foreach (char c in text)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { b.Append('\\', slashes * 2 + 1); b.Append(c); }
                else { b.Append('\\', slashes); b.Append(c); }
                slashes = 0;
            }
            b.Append('\\', slashes * 2); b.Append('"'); return b.ToString();
        }

        public static ProcessResult Run(string exe, IEnumerable<string> args, CancellationToken token,
            Action<string> onOutput = null, int timeoutSeconds = 0)
        {
            token.ThrowIfCancellationRequested();
            var output = new StringBuilder(); var error = new StringBuilder();
            var info = new ProcessStartInfo(exe, string.Join(" ", args.Select(Quote))) {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            using (var process = new Process { StartInfo = info })
            {
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) {
                    if (e.Data == null) return;
                    if (output.Length < 2 * 1024 * 1024) output.AppendLine(e.Data);
                    if (onOutput != null) onOutput(e.Data);
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) {
                    if (e.Data == null) return;
                    if (error.Length > 128 * 1024) error.Remove(0, 64 * 1024);
                    error.AppendLine(e.Data);
                };
                process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
                var watch = Stopwatch.StartNew();
                try
                {
                    while (!process.WaitForExit(100))
                    {
                        token.ThrowIfCancellationRequested();
                        if (timeoutSeconds > 0 && watch.Elapsed.TotalSeconds > timeoutSeconds)
                            throw new TimeoutException("媒体工具运行超时：" + System.IO.Path.GetFileName(exe));
                    }
                    process.WaitForExit(); token.ThrowIfCancellationRequested();
                    return new ProcessResult { Code = process.ExitCode, Output = output.ToString(), Error = error.ToString() };
                }
                finally
                {
                    if (!process.HasExited) { process.Kill(); process.WaitForExit(); }
                }
            }
        }
    }

    public sealed partial class Engine
    {
        public readonly string Root, FFmpeg, FFprobe;
        public readonly Localizer Text = new Localizer();
        public Engine(string root)
        {
            Root = root;
            FFmpeg = Locate("ffmpeg.exe"); FFprobe = Locate("ffprobe.exe");
        }

        string Locate(string file)
        {
            string[] places = { System.IO.Path.Combine(Root, "tools", file),
                System.IO.Path.Combine(Root, "tools", "ffmpeg", "bin", file),
                System.IO.Path.Combine(Root, "ffmpeg-master-latest-win64-gpl-shared", "bin", file),
                System.IO.Path.Combine(Root, file) };
            string found = places.FirstOrDefault(File.Exists);
            if (found == null) throw new FileNotFoundException("找不到 " + file + "。请保留程序旁的 FFmpeg 文件夹，或把工具放入 tools 文件夹。");
            return found;
        }

        public static string FirstLine(string s)
        {
            return (s ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "未知错误";
        }

        public static int DetectPrefix(string path)
        {
            using (var input = File.OpenRead(path))
            {
                byte[] header = new byte[48]; int n = input.Read(header, 0, header.Length);
                bool prefix = n >= 17 && header.Take(9).All(b => b == (byte)'0');
                if (prefix)
                {
                    string box = Encoding.ASCII.GetString(header, 13, 4);
                    if (box == "ftyp" || box == "styp" || box == "moov" || box == "moof") return 9;
                }
                return 0;
            }
        }

        static List<string> Input(string path, int prefix)
        {
            var args = new List<string>();
            if (prefix > 0)
            {
                // A virtual file resets the MP4 origin, including absolute fragment offsets.
                // skip_initial_bytes alone leaves those offsets relative to the obfuscated file.
                args.AddRange(new[] { "-f", "mov" });
                path = "subfile,,start," + prefix.ToString(CultureInfo.InvariantCulture) + ",,:" + System.IO.Path.GetFullPath(path);
            }
            args.AddRange(new[] { "-i", path }); return args;
        }

        static string Str(Dictionary<string, object> map, string key, string fallback = "")
        {
            object value; return map.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : fallback;
        }
        static double Num(Dictionary<string, object> map, string key)
        {
            double value; double.TryParse(Str(map, key), NumberStyles.Any, CultureInfo.InvariantCulture, out value); return value;
        }

        public List<MediaTrack> Probe(string path, CancellationToken token)
        {
            int prefix = DetectPrefix(path);
            var args = new List<string> { "-v", "error" }; args.AddRange(Input(path, prefix));
            args.AddRange(new[] { "-show_entries", "stream=codec_type,codec_name,codec_tag_string,width,height,duration:format=duration", "-of", "json" });
            var result = ChildProcess.Run(FFprobe, args, token, null, 30);
            if (result.Code != 0) throw new InvalidDataException(FirstLine(result.Error));
            var json = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(result.Output);
            object streams; var tracks = new List<MediaTrack>();
            if (!json.TryGetValue("streams", out streams)) return tracks;
            double duration = 0; object format;
            if (json.TryGetValue("format", out format)) duration = Num((Dictionary<string, object>)format, "duration");
            foreach (Dictionary<string, object> stream in (System.Collections.IEnumerable)streams)
            {
                string kind = Str(stream, "codec_type"); if (kind != "audio" && kind != "video") continue;
                tracks.Add(new MediaTrack { Path = path, Prefix = prefix, Codec = Str(stream, "codec_name"), CodecTag = Str(stream, "codec_tag_string"), Kind = kind,
                    Width = (int)Num(stream, "width"), Height = (int)Num(stream, "height"),
                    Duration = Num(stream, "duration") > 0 ? Num(stream, "duration") : duration, Bytes = new FileInfo(path).Length });
            }
            return tracks;
        }

        public List<CacheEntry> Scan(string root, CancellationToken token, Action<string> log)
        {
            if (!System.IO.Directory.Exists(root)) throw new DirectoryNotFoundException(Text.Get("缓存目录不存在：{0}", root));
            var entries = new List<CacheEntry>(); var folders = new Stack<string>(); folders.Push(System.IO.Path.GetFullPath(root));
            while (folders.Count > 0)
            {
                token.ThrowIfCancellationRequested(); string folder = folders.Pop(); string[] files;
                try
                {
                    foreach (string child in System.IO.Directory.GetDirectories(folder))
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) folders.Push(child);
                    files = System.IO.Directory.GetFiles(folder, "*.m4s");
                }
                catch (Exception ex) { log(Text.Get("跳过目录 {0}：{1}", folder, ex.Message)); continue; }
                if (files.Length == 0) continue;
                var entry = new CacheEntry { Directory = folder, Id = System.IO.Path.GetFileName(folder), Title = System.IO.Path.GetFileName(folder) };
                entry.CacheFiles.AddRange(files);
                bool incomplete = false;
                string infoPath = System.IO.Path.Combine(folder, "videoInfo.json");
                if (File.Exists(infoPath))
                {
                    entry.CacheFiles.Add(infoPath);
                    try
                    {
                        var info = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(infoPath, Encoding.UTF8));
                        entry.Title = Str(info, "title", entry.Title);
                        string partTitle = Str(info, "tabName");
                        if (partTitle.Length > 0 && partTitle != entry.Title) entry.Title += " - " + partTitle;
                        string status = Str(info, "status");
                        incomplete = (status.Length > 0 && status != "completed") || (info.ContainsKey("progress") && Num(info, "progress") < 100);
                    }
                    catch (Exception ex) { log(Text.Get("元数据读取失败，使用目录名：{0}", ex.Message)); }
                }
                log(Text.Get("扫描：{0}", entry.Title));
                var tracks = new List<MediaTrack>();
                foreach (string file in files.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    token.ThrowIfCancellationRequested();
                    try { tracks.AddRange(Probe(file, token)); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { log(Text.Get("无法识别 {0}：{1}", System.IO.Path.GetFileName(file), ex.Message)); }
                }
                entry.Video = tracks.Where(t => t.Kind == "video").OrderByDescending(t => (long)t.Width * t.Height).ThenByDescending(t => t.Bytes).FirstOrDefault();
                entry.Audio = tracks.Where(t => t.Kind == "audio").OrderByDescending(t => t.Bytes).FirstOrDefault();
                entry.Ready = entry.Video != null && entry.Audio != null && !incomplete;
                entry.Selected = false;
                entry.Status = incomplete ? "下载未完成" : entry.Video == null ? "缺少视频轨" : entry.Audio == null ? "缺少音频轨" : "待提取";
                entry.Detail = folder;
                if (tracks.Count(t => t.Kind == "video") > 1 || tracks.Count(t => t.Kind == "audio") > 1)
                {
                    entry.MultipleTracks = true;
                    log(Text.Get("{0}：存在多个候选轨道，使用 {1}", entry.Title, entry.Video == null ? Text.Get("无视频") : System.IO.Path.GetFileName(entry.Video.Path)));
                }
                entries.Add(entry);
            }
            return entries.OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static string SafeName(string name)
        {
            var bad = System.IO.Path.GetInvalidFileNameChars();
            string clean = new string(name.Select(c => bad.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ');
            if (clean.Length > 85) clean = clean.Substring(0, 85).TrimEnd('.', ' ');
            if (clean.Length == 0) clean = "视频";
            string stem = clean.Split('.')[0].ToUpperInvariant();
            if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem)) clean = "_" + clean;
            return clean;
        }

        public string Export(CacheEntry entry, string destination, ExportMode mode, bool gpuDecode,
            CancellationToken token, Action<double> progress, Action<string> log, double limitSeconds = 0)
        {
            return Export(entry, destination, mode, EncodingDevice.Auto, gpuDecode, token, progress, log, limitSeconds);
        }

        public string Export(CacheEntry entry, string destination, ExportMode mode, EncodingDevice device, bool gpuDecode,
            CancellationToken token, Action<double> progress, Action<string> log, double limitSeconds = 0, Action<string> activity = null)
        {
            if (!entry.Ready) throw new InvalidOperationException(Text.Get(entry.Status));
            if (!CanExport(mode, device)) throw new InvalidOperationException(UnavailableMessage(mode, device));
            if (double.IsNaN(limitSeconds) || double.IsInfinity(limitSeconds) || limitSeconds < 0)
                throw new ArgumentOutOfRangeException("limitSeconds", "测试秒数必须是有限的非负数。");
            bool transcode = mode != ExportMode.Copy;
            string codec = mode == ExportMode.H265 ? "hevc" : "h264";
            var plans = transcode ? Plans(mode, device) : new List<EncoderPlan> { null };
            token.ThrowIfCancellationRequested();
            string directory = System.IO.Path.GetFullPath(destination); System.IO.Directory.CreateDirectory(directory);
            string partial = System.IO.Path.Combine(directory, ".extract-" + Guid.NewGuid().ToString("N") + ".partial.mp4");
            EncoderPlan completedPlan = null;
            try
            {
                for (int index = 0; index < plans.Count; index++)
                {
                    var plan = plans[index]; string exe = transcode ? plan.FFmpeg : FFmpeg;
                    bool useCuda = plan != null && plan.Device == EncodingDevice.Nvidia && gpuDecode &&
                        new[] { "h264", "hevc", "vp9", "mpeg2video" }.Contains(entry.Video.Codec);
                    ProcessResult result;
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        var args = new List<string> { "-hide_banner", "-loglevel", "warning", "-nostdin", "-n", "-xerror" };
                        if (useCuda) args.AddRange(new[] { "-hwaccel", "cuda", "-hwaccel_output_format", "cuda" });
                        args.AddRange(Input(entry.Video.Path, entry.Video.Prefix));
                        args.AddRange(Input(entry.Audio.Path, entry.Audio.Prefix));
                        args.AddRange(new[] { "-map", "0:v:0", "-map", "1:a:0", "-map_metadata", "-1" });
                        if (transcode)
                        {
                            plan.AddVideoArguments(args, useCuda); args.AddRange(new[] { "-c:a", "copy" });
                            if (mode == ExportMode.H265) args.AddRange(new[] { "-tag:v", "hvc1" });
                        }
                        else args.AddRange(new[] { "-c", "copy" });
                        if (limitSeconds > 0) args.AddRange(new[] { "-t", limitSeconds.ToString(CultureInfo.InvariantCulture) });
                        args.AddRange(new[] { "-metadata", "title=" + entry.Title, "-movflags", "+faststart", "-progress", "pipe:1", "-nostats", partial });
                        double duration = limitSeconds > 0 ? Math.Min(limitSeconds, entry.Duration) : entry.Duration;
                        string route = transcode ? plan.DisplayText(Text) + " · " + Text.Get(useCuda ? "GPU 解码" : "CPU 解码") : Text.Get("音视频直接合并");
                        log(route + "：" + entry.Title); if (activity != null) activity(route);
                        result = ChildProcess.Run(exe, args, token, delegate(string line) {
                            if (line.StartsWith("speed=", StringComparison.Ordinal) && activity != null)
                                activity(Text.Get("{0} · 处理速度 {1}", route, line.Substring(6).Trim()));
                            if (!line.StartsWith("out_time_ms=", StringComparison.Ordinal)) return;
                            double us; if (duration > 0 && double.TryParse(line.Substring(12), NumberStyles.Any, CultureInfo.InvariantCulture, out us))
                                progress(Math.Max(0, Math.Min(0.99, us / 1000000.0 / duration)));
                        });
                        if (result.Code == 0) break;
                        log(result.Error.Trim());
                        if (File.Exists(partial)) File.Delete(partial);
                        if (!useCuda) break;
                        useCuda = false; progress(0);
                        log(Text.Get("GPU 解码路径失败，改用 CPU 解码重试；输出格式保持 {0}。", ExportModes.Label(mode)));
                    }
                    if (result.Code == 0) { completedPlan = plan; break; }
                    if (device == EncodingDevice.Auto && index + 1 < plans.Count)
                    {
                        log(Text.Get("自动重试：{0}；输出格式保持 {1}。", plans[index + 1].DisplayText(Text), ExportModes.Label(mode)));
                        progress(0); continue;
                    }
                    throw new InvalidOperationException(FirstLine(result.Error));
                }
                token.ThrowIfCancellationRequested();
                var verified = Probe(partial, token);
                var v = verified.FirstOrDefault(t => t.Kind == "video"); var a = verified.FirstOrDefault(t => t.Kind == "audio");
                if (v == null || a == null || new FileInfo(partial).Length == 0) throw new InvalidDataException(Text.Get("输出校验失败：音视频轨不完整。"));
                if (v.Codec != (transcode ? codec : entry.Video.Codec)) throw new InvalidDataException(Text.Get("输出校验失败：视频编码与所选模式不符。"));
                if (a.Codec != entry.Audio.Codec) throw new InvalidDataException(Text.Get("输出校验失败：音频编码发生变化。"));
                if (mode == ExportMode.H265 && v.CodecTag != "hvc1") throw new InvalidDataException(Text.Get("输出校验失败：H.265 封装标记不正确。"));
                double expected = limitSeconds > 0 ? Math.Min(limitSeconds, entry.Duration) : entry.Duration;
                if (expected > 3 && v.Duration + Math.Max(2, expected * .01) < expected) throw new InvalidDataException(Text.Get("输出校验失败：视频时长不足。"));
                token.ThrowIfCancellationRequested();
                string stem = SafeName(entry.Title) + " [" + SafeName(entry.Id) + "]" + (transcode ?
                    "_" + ExportModes.Key(mode).ToUpperInvariant() + "_" + EncodingDevices.Suffix(completedPlan.Device) : "");
                string target = System.IO.Path.Combine(directory, stem + ".mp4"); int suffix = 2;
                while (true)
                {
                    if (File.Exists(target)) { target = System.IO.Path.Combine(directory, stem + " (" + suffix++ + ").mp4"); continue; }
                    try { File.Move(partial, target); break; }
                    catch (IOException) { if (!File.Exists(target)) throw; }
                }
                progress(1); return target;
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }
    }
}
