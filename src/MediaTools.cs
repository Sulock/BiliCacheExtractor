// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace BiliExtractor
{
    public sealed class MediaToolInstallation
    {
        public string Directory, FFmpeg, FFprobe, Kind;
        public override string ToString() { return Directory; }
    }

    public sealed class MediaToolSelection
    {
        public string Mode { get; set; }
        public string Directory { get; set; }
        public MediaToolSelection() { Mode = "builtin"; }
    }

    public static class MediaTools
    {
        public const string SelectionFile = "media-tools.json";
        public static MediaToolInstallation FindPair(string directory, string kind)
        {
            if (string.IsNullOrWhiteSpace(directory)) return null;
            foreach (string folder in new[] { directory, Path.Combine(directory, "bin") })
            {
                string full;
                try { full = Path.GetFullPath(folder); } catch (ArgumentException) { continue; } catch (NotSupportedException) { continue; }
                string ffmpeg = Path.Combine(full, "ffmpeg.exe"), ffprobe = Path.Combine(full, "ffprobe.exe");
                if (File.Exists(ffmpeg) && File.Exists(ffprobe))
                    return new MediaToolInstallation { Directory = full, FFmpeg = ffmpeg, FFprobe = ffprobe, Kind = kind };
            }
            return null;
        }
        public static MediaToolInstallation BuiltIn(string root)
        {
            foreach (string folder in new[] { Path.Combine(root, "tools", "ffmpeg"), Path.Combine(root, "tools"),
                Path.Combine(root, "ffmpeg-master-latest-win64-gpl-shared"), root })
            {
                var pair = FindPair(folder, "builtin");
                if (pair != null) return pair;
            }
            return null;
        }
        public static MediaToolInstallation Resolve(string root, out string problem)
        {
            problem = null;
            string config = Path.Combine(root, SelectionFile);
            if (!File.Exists(config)) return BuiltIn(root);
            try
            {
                var saved = new JavaScriptSerializer().Deserialize<MediaToolSelection>(File.ReadAllText(config, Encoding.UTF8));
                if (saved == null) throw new InvalidDataException();
                if (saved.Mode == "builtin") return BuiltIn(root);
                if (saved.Mode != "local" && saved.Mode != "downloaded") throw new InvalidDataException();
                if (string.IsNullOrWhiteSpace(saved.Directory)) throw new InvalidDataException();
                string path = Path.IsPathRooted(saved.Directory) ? saved.Directory : Path.Combine(root, saved.Directory);
                var pair = FindPair(path, saved.Mode);
                if (pair == null) problem = "上次选择的 FFmpeg 不可用，请重新选择。";
                return pair;
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is ArgumentException) && !(ex is InvalidOperationException) && !(ex is UnauthorizedAccessException)) throw;
                problem = "无法读取 FFmpeg 配置，请重新选择。";
                return null;
            }
        }
        public static void Save(string root, MediaToolInstallation installation)
        {
            string directory = installation.Directory;
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (directory.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) directory = directory.Substring(prefix.Length);
            var selected = new MediaToolSelection { Mode = installation.Kind, Directory = installation.Kind == "builtin" ? null : directory };
            string destination = Path.Combine(root, SelectionFile), temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(selected), new UTF8Encoding(false));
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static List<MediaToolInstallation> Discover(string root, string searchPath = null)
        {
            var found = new List<MediaToolInstallation>();
            Action<MediaToolInstallation> add = pair => {
                if (pair != null && !found.Any(p => p.Directory.Equals(pair.Directory, StringComparison.OrdinalIgnoreCase))) found.Add(pair);
            };
            add(BuiltIn(root));
            string problem; add(Resolve(root, out problem));
            string downloads = Path.Combine(root, "tools", "downloads");
            if (System.IO.Directory.Exists(downloads))
                foreach (string path in System.IO.Directory.GetDirectories(downloads)) add(FindPair(path, "downloaded"));
            foreach (string entry in (searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                string path = Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"'));
                // Automatic discovery stays on local absolute paths; manual selection also accepts network folders.
                if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) continue;
                add(FindPair(path, "local"));
            }
            return found;
        }
        public static string ReadLanguage(string root)
        {
            try
            {
                var values = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(root, "settings.json"), Encoding.UTF8));
                object value;
                if (values != null && values.TryGetValue("Language", out value)) return Localizer.Normalize(Convert.ToString(value));
            }
            catch (Exception ex) { if (!(ex is IOException) && !(ex is ArgumentException) && !(ex is InvalidOperationException) && !(ex is UnauthorizedAccessException)) throw; }
            return "zh-CN";
        }
    }

    public sealed class MediaToolInspection
    {
        public MediaToolInstallation Installation;
        public string Version, Error;
        public bool Valid, CpuH264, CpuH265, NvidiaH264, NvidiaH265, IntelH264, IntelH265, AmdH264, AmdH265;
        public readonly List<string> Details = new List<string>();
    }

    public static class MediaToolInspector
    {
        public static MediaToolInspection Inspect(MediaToolInstallation installation, CancellationToken token,
            HardwareInfo hardware = null, bool testEncoders = true)
        {
            var result = new MediaToolInspection { Installation = installation };
            try
            {
                var ffmpeg = ChildProcess.Run(installation.FFmpeg, new[] { "-version" }, token, null, 10);
                var ffprobe = ChildProcess.Run(installation.FFprobe, new[] { "-version" }, token, null, 10);
                if (ffmpeg.Code != 0 || ffprobe.Code != 0) throw new InvalidDataException("FFmpeg 或 FFprobe 无法运行，请检查配套 DLL。" + "\r\n" + Engine.FirstLine(ffmpeg.Error + ffprobe.Error));
                string first = Regex.Match(ffmpeg.Output, @"(?m)^ffmpeg version (\S+)").Groups[1].Value;
                string second = Regex.Match(ffprobe.Output, @"(?m)^ffprobe version (\S+)").Groups[1].Value;
                if (first.Length == 0 || second.Length == 0) throw new InvalidDataException("所选文件未返回有效的 FFmpeg 版本信息。");
                if (first != second) throw new InvalidDataException("FFmpeg 和 FFprobe 版本不一致，请选择同一发行包。");
                result.Version = first;
                var protocols = ChildProcess.Run(installation.FFmpeg, new[] { "-hide_banner", "-protocols" }, token, null, 10);
                var formats = ChildProcess.Run(installation.FFmpeg, new[] { "-hide_banner", "-formats" }, token, null, 10);
                var probeProtocols = ChildProcess.Run(installation.FFprobe, new[] { "-hide_banner", "-protocols" }, token, null, 10);
                var probeFormats = ChildProcess.Run(installation.FFprobe, new[] { "-hide_banner", "-formats" }, token, null, 10);
                if (protocols.Code != 0 || formats.Code != 0 || !Regex.IsMatch(protocols.Output, @"(?m)^\s*subfile\s*$") ||
                    !Regex.IsMatch(formats.Output, @"(?m)^\s*D\S*\s+\S*\bmov\b") || !Regex.IsMatch(formats.Output, @"(?m)^\s*\S*E\s+mp4\b") ||
                    probeProtocols.Code != 0 || probeFormats.Code != 0 || !Regex.IsMatch(probeProtocols.Output, @"(?m)^\s*subfile\s*$") ||
                    !Regex.IsMatch(probeFormats.Output, @"(?m)^\s*D\S*\s+\S*\bmov\b"))
                    throw new InvalidDataException("此构建缺少 MP4 或 subfile 支持。");
                result.Valid = true;
                if (testEncoders)
                {
                    // A selected external installation is tested and used as a pair, without a hidden legacy executable.
                    var engine = new Engine(Path.GetDirectoryName(installation.Directory), installation);
                    engine.UseHardwareSnapshot(hardware);
                    engine.DetectEncoders(token, result.Details.Add);
                    result.CpuH264 = engine.CanExport(ExportMode.H264, EncodingDevice.Cpu);
                    result.CpuH265 = engine.CanExport(ExportMode.H265, EncodingDevice.Cpu);
                    result.NvidiaH264 = engine.CanExport(ExportMode.H264, EncodingDevice.Nvidia);
                    result.NvidiaH265 = engine.CanExport(ExportMode.H265, EncodingDevice.Nvidia);
                    result.IntelH264 = engine.CanExport(ExportMode.H264, EncodingDevice.Intel);
                    result.IntelH265 = engine.CanExport(ExportMode.H265, EncodingDevice.Intel);
                    result.AmdH264 = engine.CanExport(ExportMode.H264, EncodingDevice.Amd);
                    result.AmdH265 = engine.CanExport(ExportMode.H265, EncodingDevice.Amd);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { result.Error = ex.Message; result.Valid = false; }
            return result;
        }
    }

    public sealed class MediaToolDownload
    {
        public string version { get; set; }
        public string url { get; set; }
        public long bytes { get; set; }
        public string sha256 { get; set; }
        public string archiveRoot { get; set; }
        public string license { get; set; }
        public string sourceUrl { get; set; }
        public string minimumNvidiaDriver { get; set; }
        public string sourceArchive { get; set; }
        public string sourceSha256 { get; set; }
        public long sourceBytes { get; set; }
        public static MediaToolDownload Load(string root)
        {
            string path = Path.Combine(root, "tools", "ffmpeg-recommended.json");
            if (!File.Exists(path)) return null;
            var value = new JavaScriptSerializer().Deserialize<MediaToolDownload>(File.ReadAllText(path, Encoding.UTF8));
            Validate(value);
            return value;
        }
        public static void Validate(MediaToolDownload value)
        {
            Uri uri;
            if (value == null || !Uri.TryCreate(value.url, UriKind.Absolute, out uri) || uri.Scheme != "https" ||
                value.bytes <= 0 || value.bytes > 1024L * 1024 * 1024 || !Regex.IsMatch(value.sha256 ?? "", "^[a-fA-F0-9]{64}$") ||
                !Regex.IsMatch(value.archiveRoot ?? "", "^[A-Za-z0-9._-]+$") || value.archiveRoot == "." || value.archiveRoot == "..")
                throw new InvalidDataException("推荐下载配置无效。");
        }
    }

    public static class MediaToolInstaller
    {
        public static string Hash(string path)
        {
            using (var input = File.OpenRead(path)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
        public static async Task<MediaToolInstallation> DownloadAsync(string root, MediaToolDownload spec,
            CancellationToken token, Action<long, long> progress, Action<string> status)
        {
            token.ThrowIfCancellationRequested(); MediaToolDownload.Validate(spec);
            string scratch = Path.Combine(root, ".ffmpeg-setup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            try
            {
                string archive = Path.Combine(scratch, "download.zip");
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var request = (HttpWebRequest)WebRequest.Create(spec.url);
                request.UserAgent = "BiliCacheExtractor";
                request.Timeout = request.ReadWriteTimeout = 30000;
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromMinutes(10));
                    using (timeout.Token.Register(request.Abort))
                    {
                        try
                        {
                            using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                            using (var input = response.GetResponseStream())
                            using (var output = File.Create(archive))
                            {
                                if (response.ResponseUri.Scheme != "https") throw new InvalidDataException("下载地址必须使用 HTTPS。");
                                if (response.ContentLength >= 0 && response.ContentLength != spec.bytes) throw new InvalidDataException("下载大小与固定版本不符。");
                                long total = 0; var buffer = new byte[65536]; int count;
                                while ((count = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) != 0)
                                {
                                    total += count;
                                    if (total > spec.bytes) throw new InvalidDataException("下载大小与固定版本不符。");
                                    await output.WriteAsync(buffer, 0, count, timeout.Token).ConfigureAwait(false);
                                    progress(total, spec.bytes);
                                }
                            }
                        }
                        catch (WebException) { if (timeout.IsCancellationRequested) { token.ThrowIfCancellationRequested(); throw new TimeoutException("下载超时，请重试或选择本地版本。"); } throw; }
                        catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); throw new TimeoutException("下载超时，请重试或选择本地版本。"); }
                    }
                }
                status("正在校验和解压 FFmpeg…");
                return InstallArchive(root, spec, archive, token);
            }
            finally { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
        }
        public static MediaToolInstallation InstallArchive(string root, MediaToolDownload spec, string archive, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); MediaToolDownload.Validate(spec);
            if (new FileInfo(archive).Length != spec.bytes || !Hash(archive).Equals(spec.sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("下载文件校验失败，未安装。");
            string downloads = Path.Combine(root, "tools", "downloads");
            Directory.CreateDirectory(downloads);
            string destination = Path.Combine(downloads, spec.archiveRoot + "-" + spec.sha256.Substring(0, 12));
            if (Directory.Exists(destination))
            {
                // Preserve any existing installation, including modified files; a new copy gets a separate folder.
                destination += "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            }
            string scratch = Path.Combine(root, ".ffmpeg-setup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            try
            {
                string unpack = Path.Combine(scratch, "unpack"); Directory.CreateDirectory(unpack);
                string prefix = Path.GetFullPath(unpack).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                long total = 0;
                using (var input = ZipFile.OpenRead(archive))
                {
                    if (input.Entries.Count > 20000) throw new InvalidDataException("压缩包文件数量异常。");
                    foreach (var entry in input.Entries)
                    {
                        token.ThrowIfCancellationRequested();
                        string name = entry.FullName.Replace('\\', '/');
                        if (!name.StartsWith(spec.archiveRoot + "/", StringComparison.Ordinal) ||
                            name.Contains(":") || name.Split('/').Any(p => p == ".." || p == ".") || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                            throw new InvalidDataException("压缩包包含不安全的文件路径。");
                        string target = Path.GetFullPath(Path.Combine(unpack, name.Replace('/', Path.DirectorySeparatorChar)));
                        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("压缩包包含不安全的文件路径。");
                        total += entry.Length;
                        if (total > 1024L * 1024 * 1024) throw new InvalidDataException("解压大小超过限制。");
                        if (name.EndsWith("/", StringComparison.Ordinal)) { Directory.CreateDirectory(target); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (var source = entry.Open()) using (var output = new FileStream(target, FileMode.CreateNew))
                        {
                            var buffer = new byte[65536]; int count; long written = 0;
                            while ((count = source.Read(buffer, 0, buffer.Length)) != 0) {
                                token.ThrowIfCancellationRequested(); written += count;
                                if (written > entry.Length) throw new InvalidDataException("解压大小与归档记录不符。");
                                output.Write(buffer, 0, count);
                            }
                            if (written != entry.Length) throw new InvalidDataException("解压大小与归档记录不符。");
                        }
                    }
                }
                string extracted = Path.Combine(unpack, spec.archiveRoot);
                var pair = MediaTools.FindPair(extracted, "downloaded");
                if (pair == null) throw new InvalidDataException("压缩包缺少 FFmpeg 或 FFprobe。");
                var inspection = MediaToolInspector.Inspect(pair, token, null, false);
                if (!inspection.Valid) throw new InvalidDataException(inspection.Error);
                token.ThrowIfCancellationRequested();
                Directory.Move(extracted, destination);
                return MediaTools.FindPair(destination, "downloaded");
            }
            finally { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
        }
    }
}
