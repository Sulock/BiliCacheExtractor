using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using BiliExtractor;

static class IntegrationTests
{
    static int passed;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message); passed++;
    }
    static void Run(Engine engine, params string[] args)
    {
        var result = ChildProcess.Run(engine.FFmpeg, args, CancellationToken.None, null, 30);
        if (result.Code != 0) throw new Exception(result.Error);
    }
    static void MakeCache(string path, string video, string audio, bool prefix, string metadata)
    {
        Directory.CreateDirectory(path);
        foreach (var item in new[] { new[] { video, "smaller-video.m4s" }, new[] { audio, "larger-audio.m4s" } })
        {
            using (var output = File.Create(Path.Combine(path, item[1])))
            {
                if (prefix) output.Write(Encoding.ASCII.GetBytes("000000000"), 0, 9);
                using (var input = File.OpenRead(item[0])) input.CopyTo(output);
            }
        }
        if (metadata != null) File.WriteAllText(Path.Combine(path, "videoInfo.json"), metadata, Encoding.UTF8);
    }
    static string Hash(Engine engine, string path, string stream)
    {
        var result = ChildProcess.Run(engine.FFmpeg, new[] { "-v", "error", "-nostdin", "-i", path,
            "-map", "0:" + stream + ":0", "-c", "copy", "-f", "hash", "-hash", "sha256", "-" }, CancellationToken.None, null, 30);
        if (result.Code != 0) throw new Exception(result.Error);
        return result.Output.Trim();
    }
    static CacheEntry CancellationEntry(Engine engine, string test, string video, string audio, string cachePath)
    {
        if (cachePath == null)
        {
            // Repeat synthetic media so cancellation remains observable even on fast encoders.
            // No personal cache is needed when running the default test command.
            string longVideo = Path.Combine(test, "cancel-video.mp4"), longAudio = Path.Combine(test, "cancel-audio.mp4");
            Run(engine, "-v", "error", "-nostdin", "-n", "-stream_loop", "99", "-i", video,
                "-t", "300", "-an", "-c:v", "copy", "-movflags", "+frag_keyframe+empty_moov", longVideo);
            Run(engine, "-v", "error", "-nostdin", "-n", "-stream_loop", "99", "-i", audio,
                "-t", "300", "-vn", "-c:a", "copy", "-movflags", "+frag_keyframe+empty_moov", longAudio);
            cachePath = Path.Combine(test, "cancel-cache");
            MakeCache(cachePath, longVideo, longAudio, true, null);
        }
        var entry = engine.Scan(cachePath, CancellationToken.None, s => { }).FirstOrDefault(e => e.Ready);
        if (entry == null) throw new InvalidOperationException("Cancellation test cache contains no extractable video. Omit -CachePath to use generated media.");
        return entry;
    }
    static int FakeTool(string[] args)
    {
        string folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        bool probe = Path.GetFileName(Assembly.GetExecutingAssembly().Location) == "ffprobe.exe";
        int codecIndex = Array.IndexOf(args, "-c:v"); string codec = codecIndex >= 0 ? args[codecIndex + 1] : "";
        bool cpu = codec == "libx264" || codec == "libx265";
        bool nvidia = codec.EndsWith("_nvenc");
        bool supported = cpu || (nvidia && !File.Exists(Path.Combine(folder, "no-nvidia")) &&
            (codec == "h264_nvenc" || Path.GetFileName(Assembly.GetExecutingAssembly().Location) == "ffmpeg-compat.exe")) ||
            (codec.EndsWith("_qsv") && File.Exists(Path.Combine(folder, "intel"))) ||
            (codec.EndsWith("_amf") && File.Exists(Path.Combine(folder, "amd")));
        if (!probe && !supported) return 1;
        if (!probe && !args.Contains("-progress")) return 0;
        if (!probe && !cpu && File.Exists(Path.Combine(folder, "fail-hardware")))
        {
            File.WriteAllText(args.Last(), "partial output from failed hardware");
            Console.Error.WriteLine("simulated hardware encoding failure"); return 1;
        }
        string redirect = Path.Combine(folder, probe ? "real-ffprobe.txt" : "real-ffmpeg.txt");
        if (!File.Exists(redirect)) return 1;
        var result = ChildProcess.Run(File.ReadAllText(redirect), args, CancellationToken.None);
        Console.Write(result.Output); Console.Error.Write(result.Error); return result.Code;
    }
    static void CheckEncoderAvailability(string test, Engine realEngine, CacheEntry first)
    {
        // This executable also acts as two deterministic encoders: the newer tool
        // supports H.264, while only the compatibility tool supports H.265.
        string root = Path.Combine(test, "encoder-availability"), tools = Path.Combine(root, "tools");
        Directory.CreateDirectory(tools);
        File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(tools, "ffmpeg.exe"));
        string compat = Path.Combine(tools, "ffmpeg-compat.exe");
        File.Copy(Assembly.GetExecutingAssembly().Location, compat);
        File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(tools, "ffprobe.exe"));
        File.WriteAllText(Path.Combine(tools, "real-ffmpeg.txt"), realEngine.FFmpeg);
        File.WriteAllText(Path.Combine(tools, "real-ffprobe.txt"), realEngine.FFprobe);
        var engine = new Engine(root); var log = new List<string>();
        engine.DetectEncoders(CancellationToken.None, log.Add);
        Check(engine.CanExport(ExportMode.H264, EncodingDevice.Nvidia) && engine.CanExport(ExportMode.H265, EncodingDevice.Nvidia) &&
            engine.Plans(ExportMode.H264, EncodingDevice.Auto).First().FFmpeg.EndsWith("ffmpeg.exe") &&
            engine.Plans(ExportMode.H265, EncodingDevice.Auto).First().FFmpeg.EndsWith("ffmpeg-compat.exe"),
            "each codec independently selects a working FFmpeg, including HEVC compatibility fallback");
        File.Delete(compat);
        engine.DetectEncoders(CancellationToken.None, s => { });
        string output = Path.Combine(root, "must-not-be-created"); bool rejected = false;
        try { engine.Export(new CacheEntry { Ready = true }, output, ExportMode.H265, EncodingDevice.Nvidia, false, CancellationToken.None, p => { }, s => { }); }
        catch (InvalidOperationException ex) { rejected = ex.Message.Contains("H.265"); }
        Check(engine.CanExport(ExportMode.Copy) && engine.CanExport(ExportMode.H264, EncodingDevice.Nvidia) && !engine.CanExport(ExportMode.H265, EncodingDevice.Nvidia) &&
            engine.Plans(ExportMode.H265, EncodingDevice.Auto).First().Device == EncodingDevice.Cpu &&
            rejected && !Directory.Exists(output), "unsupported NVIDIA HEVC uses CPU in auto mode, but manual NVIDIA mode is rejected");
        File.WriteAllText(Path.Combine(tools, "no-nvidia"), ""); engine.DetectEncoders(CancellationToken.None, s => { });
        Check(new[] { ExportMode.H264, ExportMode.H265 }.All(m => engine.Plans(m, EncodingDevice.Auto).First().Device == EncodingDevice.Cpu),
            "CPU-only computers retain both H.264 and H.265 through automatic selection");
        File.WriteAllText(Path.Combine(tools, "intel"), ""); engine.DetectEncoders(CancellationToken.None, s => { });
        Check(engine.Plans(ExportMode.H265, EncodingDevice.Auto).First().Device == EncodingDevice.Intel,
            "an available Intel encoder is selected ahead of CPU");
        File.Delete(Path.Combine(tools, "intel")); File.WriteAllText(Path.Combine(tools, "amd"), "");
        engine.DetectEncoders(CancellationToken.None, s => { });
        Check(engine.Plans(ExportMode.H264, EncodingDevice.Auto).First().Device == EncodingDevice.Amd,
            "an available AMD encoder is selected ahead of CPU");
        File.Delete(Path.Combine(tools, "amd")); File.Delete(Path.Combine(tools, "no-nvidia"));
        File.Copy(Assembly.GetExecutingAssembly().Location, compat);
        File.WriteAllText(Path.Combine(tools, "fail-hardware"), ""); engine.DetectEncoders(CancellationToken.None, s => { });
        log.Clear();
        string fallback = engine.Export(first, Path.Combine(root, "fallback"), ExportMode.H265, EncodingDevice.Auto, true, CancellationToken.None, p => { }, log.Add);
        Check(Path.GetFileName(fallback).EndsWith("_H265_CPU.mp4") && realEngine.Probe(fallback, CancellationToken.None).Single(t => t.Kind == "video").Codec == "hevc" &&
            log.Any(s => s.Contains("自动重试") && s.Contains("CPU")) && Directory.GetFiles(Path.GetDirectoryName(fallback), "*.partial.mp4").Length == 0,
            "hardware export failure retries on CPU, keeps HEVC, identifies the actual device and cleans partial files");
        string manual = Path.Combine(root, "manual-failure"); rejected = false;
        try { engine.Export(first, manual, ExportMode.H265, EncodingDevice.Nvidia, true, CancellationToken.None, p => { }, s => { }); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && Directory.GetFiles(manual).Length == 0, "manual hardware export failure is reported without silently switching to CPU");
    }
    static int Main(string[] args)
    {
        if (Path.GetFileName(Assembly.GetExecutingAssembly().Location).StartsWith("ff", StringComparison.OrdinalIgnoreCase)) return FakeTool(args);
        try
        {
            string root = Path.GetFullPath(args[0]); var engine = new Engine(root);
            string test = Path.Combine(root, "verification", "integration-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(test);
            string video = Path.Combine(test, "video.mp4"), audio = Path.Combine(test, "audio.mp4");
            Run(engine, "-v", "error", "-nostdin", "-n", "-f", "lavfi", "-i", "color=c=blue:s=320x180:r=30", "-t", "3", "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-movflags", "+frag_keyframe+empty_moov", video);
            Run(engine, "-v", "error", "-nostdin", "-n", "-f", "lavfi", "-i", "anoisesrc=duration=3:sample_rate=48000", "-vn", "-c:a", "aac", "-b:a", "192k", "-movflags", "+frag_keyframe+empty_moov", audio);
            string cache = Path.Combine(test, "缓存 & fixtures");
            string prefixed = Path.Combine(cache, "001"), plain = Path.Combine(cache, "nested", "002");
            MakeCache(prefixed, video, audio, true, "{\"title\":\"批量测试: \\\"中文\\\" & 空格\",\"status\":\"completed\",\"progress\":100}");
            MakeCache(plain, video, audio, false, null);
            MakeCache(Path.Combine(cache, "incomplete"), video, audio, true, "{\"title\":\"未完成\",\"status\":\"downloading\",\"progress\":50}");
            string missing = Path.Combine(cache, "missing-audio"); Directory.CreateDirectory(missing); File.Copy(video, Path.Combine(missing, "video.m4s"));
            string damaged = Path.Combine(cache, "corrupt"); Directory.CreateDirectory(damaged); File.WriteAllText(Path.Combine(damaged, "bad.m4s"), "000000000bad");
            var entries = engine.Scan(cache, CancellationToken.None, s => { });
            Check(entries.Count == 5 && entries.Count(e => e.Ready) == 2, "recursive scan identifies two ready entries; excludes corrupt, incomplete and missing-audio entries");
            var first = entries.Single(e => e.Id == "001");
            Check(first.Video.Kind == "video" && first.Audio.Bytes > first.Video.Bytes, "track classification works when audio file is larger than video");
            Check(first.Video.Prefix == 9 && entries.Single(e => e.Id == "002").Video.Prefix == 0, "prefixed and ordinary MP4 tracks both detected");
            string outputDir = Path.Combine(test, "输出 & batch");
            foreach (var entry in entries.Where(e => e.Ready)) engine.Export(entry, outputDir, ExportMode.Copy, false, CancellationToken.None, p => { }, s => { });
            Check(Directory.GetFiles(outputDir, "*.mp4").Length == 2, "batch extraction produces two complete MP4s");
            string duplicate = engine.Export(first, outputDir, ExportMode.Copy, false, CancellationToken.None, p => { }, s => { });
            Check(Path.GetFileName(duplicate).Contains("(2)") && Directory.GetFiles(outputDir, "*.mp4").Length == 3, "repeat extraction keeps existing output and assigns a new filename");
            Check(engine.Probe(duplicate, CancellationToken.None).Count == 2, "Chinese titles, quotes, ampersands and spaces survive process argument quoting");
            Check(Engine.SafeName("CON") == "_CON" && Engine.SafeName("a:b?. ") == "a_b_", "Windows reserved names and invalid filename characters are sanitized");
            string failed = Path.Combine(test, "failed"); double realDuration = first.Video.Duration; first.Video.Duration = 100;
            bool rejected = false;
            try { engine.Export(first, failed, ExportMode.Copy, false, CancellationToken.None, p => { }, s => { }); }
            catch (InvalidDataException) { rejected = true; }
            first.Video.Duration = realDuration;
            Check(rejected && Directory.GetFiles(failed).Length == 0, "duration validation rejects a truncated result and removes only its partial output");
            string expectedCodec = first.Video.Codec; first.Video.Codec = "hevc"; rejected = false;
            try { engine.Export(first, failed, ExportMode.Copy, false, CancellationToken.None, p => { }, s => { }); }
            catch (InvalidDataException) { rejected = true; }
            first.Video.Codec = expectedCodec;
            Check(rejected && Directory.GetFiles(failed).Length == 0, "codec validation rejects the wrong output codec and removes its partial file");
            CheckEncoderAvailability(test, engine, first);
            string hevcVideo = Path.Combine(test, "hevc-video.mp4"), hevcCache = Path.Combine(test, "hevc-cache");
            Run(engine, "-v", "error", "-nostdin", "-n", "-i", video, "-an", "-c:v", "libx265", "-preset", "ultrafast",
                "-x265-params", "log-level=error:pools=1", "-tag:v", "hvc1", "-movflags", "+frag_keyframe+empty_moov", hevcVideo);
            MakeCache(hevcCache, hevcVideo, audio, true, null);
            var hevcEntry = engine.Scan(hevcCache, CancellationToken.None, s => { }).Single();
            string hevcCopy = engine.Export(hevcEntry, outputDir, ExportMode.Copy, false, CancellationToken.None, p => { }, s => { });
            Check(engine.Probe(hevcCopy, CancellationToken.None).Single(t => t.Kind == "video").Codec == "hevc" &&
                Hash(engine, hevcCopy, "v") == Hash(engine, hevcVideo, "v") && Hash(engine, hevcCopy, "a") == Hash(engine, audio, "a"),
                "lossless extraction of prefixed HEVC preserves both compressed video and audio hashes");
            engine.DetectEncoders(CancellationToken.None, s => { });
            var cancellationEntry = CancellationEntry(engine, test, video, audio, args.Length > 1 ? args[1] : null);
            foreach (ExportMode mode in new[] { ExportMode.H264, ExportMode.H265 })
            {
                string cpuDir = Path.Combine(test, "cpu-" + ExportModes.Key(mode));
                var activity = new List<string>();
                string encoded = engine.Export(first, cpuDir, mode, EncodingDevice.Cpu, true, CancellationToken.None, p => { }, s => { }, 0, activity.Add);
                var v = engine.Probe(encoded, CancellationToken.None).Single(t => t.Kind == "video");
                Run(engine, "-v", "error", "-nostdin", "-xerror", "-i", encoded, "-f", "null", "-");
                Check(v.Codec == (mode == ExportMode.H265 ? "hevc" : "h264") && v.Width == 320 && v.Height == 180 &&
                    Math.Abs(v.Duration - 3) < .15 && (mode != ExportMode.H265 || v.CodecTag == "hvc1") &&
                    Path.GetFileName(encoded).Contains("_CPU") && Hash(engine, encoded, "a") == Hash(engine, audio, "a"),
                    ExportModes.Label(mode) + " CPU export preserves dimensions, duration and audio, and fully decodes");
                Check(activity.Any(s => s.Contains("CPU") && s.Contains("处理速度")), ExportModes.Label(mode) + " progress reports the actual device and measured processing speed");
                string cliDir = Path.Combine(test, "cli-cpu-" + ExportModes.Key(mode));
                var result = ChildProcess.Run(Path.Combine(root, "BiliCacheExtractor.Cli.exe"),
                    new[] { "--export", prefixed, cliDir, ExportModes.Key(mode), "--device", "cpu", "1" }, CancellationToken.None, null, 60);
                if (result.Code != 0) throw new Exception(result.Output + result.Error);
                Check(Path.GetFileName(Directory.GetFiles(cliDir, "*.mp4").Single()).Contains("_CPU"),
                    "CLI explicitly selects CPU for " + ExportModes.Label(mode));
            }
            foreach (ExportMode mode in new[] { ExportMode.H264, ExportMode.H265 })
            {
                if (!engine.CanExport(mode, EncodingDevice.Nvidia)) { Console.WriteLine("SKIP: " + ExportModes.Label(mode) + " NVIDIA unavailable"); continue; }
                string codec = mode == ExportMode.H265 ? "hevc" : "h264";
                string gpuOutput = Path.Combine(test, "gpu");
                foreach (bool decode in new[] { true, false })
                {
                    var entry = decode ? first : entries.Single(e => e.Id == "002");
                    var log = new List<string>();
                    string encoded = engine.Export(entry, gpuOutput, mode, EncodingDevice.Nvidia, decode, CancellationToken.None, p => { }, log.Add);
                    var tracks = engine.Probe(encoded, CancellationToken.None); var v = tracks.Single(t => t.Kind == "video");
                    Check(v.Codec == codec && v.Width == 320 && v.Height == 180 && Math.Abs(v.Duration - 3) < .15 &&
                        tracks.Single(t => t.Kind == "audio").Codec == "aac" &&
                        (mode != ExportMode.H265 || v.CodecTag == "hvc1") &&
                        Path.GetFileName(encoded).Contains("_" + ExportModes.Key(mode).ToUpperInvariant() + "_NVENC"),
                        ExportModes.Label(mode) + " export produces the requested codec, dimensions, duration and filename with " + (decode ? "GPU" : "CPU") + " decoding");
                    Run(engine, "-v", "error", "-nostdin", "-xerror", "-i", encoded, "-f", "null", "-");
                    Check(Hash(engine, encoded, "a") == Hash(engine, audio, "a"), ExportModes.Label(mode) + " output fully decodes and preserves compressed audio");
                }
                string again = engine.Export(first, gpuOutput, mode, EncodingDevice.Nvidia, true, CancellationToken.None, p => { }, s => { });
                Check(Path.GetFileName(again).Contains("(2)"), ExportModes.Label(mode) + " repeat export does not overwrite the previous video");
                string cliOutput = Path.Combine(test, "cli-" + ExportModes.Key(mode));
                string cliMode = mode == ExportMode.H264 ? "gpu" : "h265";
                var cli = ChildProcess.Run(Path.Combine(root, "BiliCacheExtractor.Cli.exe"),
                    new[] { "--export", prefixed, cliOutput, cliMode, "1" }, CancellationToken.None, null, 120);
                if (cli.Code != 0) throw new Exception(cli.Output + cli.Error);
                var cliVideo = engine.Probe(Directory.GetFiles(cliOutput, "*.mp4").Single(), CancellationToken.None).Single(t => t.Kind == "video");
                Check(cliVideo.Codec == codec && Math.Abs(cliVideo.Duration - 1) < .15,
                    "CLI " + cliMode + " exports the requested codec and respects the duration limit");
                {
                    string cancelDir = Path.Combine(test, "cancel-" + ExportModes.Key(mode)); bool cancelled = false; int reports = 0;
                    using (var cancel = new CancellationTokenSource())
                    {
                        cancel.CancelAfter(15000);
                        try { engine.Export(cancellationEntry, cancelDir, mode, EncodingDevice.Nvidia, true, cancel.Token, p => { if (p > 0 && p < 1) { Interlocked.Increment(ref reports); cancel.Cancel(); } }, s => { }); }
                        catch (OperationCanceledException) { cancelled = true; }
                    }
                    Check(cancelled && reports > 0 && Directory.GetFiles(cancelDir).Length == 0,
                        ExportModes.Label(mode) + " in-flight cancellation terminates FFmpeg and removes the partial output");
                }
            }
            {
                string cancelDir = Path.Combine(test, "cancel-cpu"); bool cancelled = false; int reports = 0;
                using (var cancel = new CancellationTokenSource())
                {
                    cancel.CancelAfter(15000);
                    try { engine.Export(cancellationEntry, cancelDir, ExportMode.H265, EncodingDevice.Cpu, false, cancel.Token,
                        p => { if (p > 0 && p < 1) { Interlocked.Increment(ref reports); cancel.Cancel(); } }, s => { }); }
                    catch (OperationCanceledException) { cancelled = true; }
                }
                Check(cancelled && reports > 0 && Directory.GetFiles(cancelDir).Length == 0, "CPU encoding can be cancelled in flight without leaving a partial file");
            }
            var invalid = ChildProcess.Run(Path.Combine(root, "BiliCacheExtractor.Cli.exe"), new[] { "--export", prefixed, failed, "typo" }, CancellationToken.None);
            var invalidTime = ChildProcess.Run(Path.Combine(root, "BiliCacheExtractor.Cli.exe"), new[] { "--export", prefixed, failed, "h265", "NaN" }, CancellationToken.None);
            var invalidDevice = ChildProcess.Run(Path.Combine(root, "BiliCacheExtractor.Cli.exe"), new[] { "--export", prefixed, failed, "h265", "--device", "typo" }, CancellationToken.None);
            Check(invalid.Code != 0 && invalidTime.Code != 0 && invalidDevice.Code != 0 && Directory.GetFiles(failed).Length == 0, "CLI rejects invalid formats, devices and duration values before producing output");
            Console.WriteLine("RESULT: " + passed + " checks passed. Fixtures: " + test);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
