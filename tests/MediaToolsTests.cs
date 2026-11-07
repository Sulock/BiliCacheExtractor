using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using BiliExtractor;

static class MediaToolsTests
{
    static int passed;
    static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); passed++; Console.WriteLine("PASS: " + message); }
    static int Stub(string[] args)
    {
        string path = Assembly.GetExecutingAssembly().Location;
        string folder = Path.GetDirectoryName(path);
        string name = Path.GetFileNameWithoutExtension(path);
        if (args.Contains("-version")) {
            Console.WriteLine(name + " version " + (File.Exists(Path.Combine(folder, "mismatch")) && name == "ffprobe" ? "7.0" : "8.1.3-test")); return 0;
        }
        if (args.Contains("-protocols")) { Console.WriteLine("Input:\n file\n subfile\nOutput:\n file"); return 0; }
        if (args.Contains("-formats")) { Console.WriteLine(" D  mov,mp4,m4a,3gp MOV\n  E mp4 MP4"); return 0; }
        int codec = Array.IndexOf(args, "-c:v");
        if (codec >= 0 && new[] { "libx264", "libx265", "h264_nvenc" }.Contains(args[codec + 1])) return 0;
        return 1;
    }
    static void Pair(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (string name in new[] { "ffmpeg.exe", "ffprobe.exe" }) File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(directory, name));
    }
    static MediaToolDownload Archive(string path, string source, string extraEntry = null)
    {
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) {
            zip.CreateEntryFromFile(Path.Combine(source, "ffmpeg.exe"), "runtime/bin/ffmpeg.exe");
            zip.CreateEntryFromFile(Path.Combine(source, "ffprobe.exe"), "runtime/bin/ffprobe.exe");
            using (var writer = new StreamWriter(zip.CreateEntry("runtime/licenses/说明.txt").Open(), Encoding.UTF8)) writer.Write("test license");
            if (extraEntry != null) using (var writer = new StreamWriter(zip.CreateEntry(extraEntry).Open())) writer.Write("must not escape");
        }
        return new MediaToolDownload { version = "8.1.3-test", url = "https://example.invalid/runtime.zip", bytes = new FileInfo(path).Length,
            sha256 = MediaToolInstaller.Hash(path), archiveRoot = "runtime", license = "GPL-3.0-or-later", minimumNvidiaDriver = "551.76" };
    }
    static int Main(string[] args)
    {
        if (Path.GetFileName(Assembly.GetExecutingAssembly().Location).StartsWith("ff", StringComparison.OrdinalIgnoreCase)) return Stub(args);
        try {
            string project = Path.GetFullPath(args[0]), root = Path.Combine(project, "verification", "media-tools-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Check(VerifiedDownload.ValidRange("bytes 20-29/100",20,10,100), "resumed downloads accept the exact requested byte range");
            Check(!VerifiedDownload.ValidRange("bytes 0-9/100",20,10,100) && !VerifiedDownload.ValidRange("bytes 20-29/101",20,10,100), "resumed downloads reject wrong offsets and changed file sizes");
            Check(!VerifiedDownload.ValidRange("bytes 20-29/*",20,10,100) && !VerifiedDownload.ValidRange("bytes 0-99999999999999999999999/100",0,10,100), "invalid or overflowing range headers are rejected");
            string bundled = Path.Combine(root, "tools", "ffmpeg", "bin"), local = Path.Combine(root, "用户工具 有空格", "bin");
            Pair(bundled); Pair(local);
            var builtin = MediaTools.BuiltIn(root);
            Check(builtin.Directory == bundled && MediaTools.FindPair(Path.GetDirectoryName(local), "local").Directory == local, "bundled and external parent folders resolve paired executables");
            Check(MediaTools.Discover(root, "relative;\"" + local + "\";" + local.ToUpperInvariant()).Count == 2, "PATH discovery deduplicates installations and ignores relative paths");
            var external = MediaTools.FindPair(local, "local"); MediaTools.Save(root, external);
            string problem;
            Check(MediaTools.Resolve(root, out problem).Directory == local && problem == null, "explicit local selection persists independently of application preferences");
            string preferences = Path.Combine(root, "settings.json"); File.WriteAllText(preferences, "{\"Language\":\"en\",\"Source\":\"keep\"}");
            MediaTools.Save(root, builtin);
            Check(File.ReadAllText(preferences).Contains("keep") && MediaTools.ReadLanguage(root) == "en", "tool changes preserve existing source/output and language settings");
            MediaTools.Save(root, external); File.Move(Path.Combine(local, "ffprobe.exe"), Path.Combine(local, "ffprobe.saved"));
            Check(MediaTools.Resolve(root, out problem) == null && problem != null, "missing selected tools trigger configuration instead of silently switching versions");
            File.Move(Path.Combine(local, "ffprobe.saved"), Path.Combine(local, "ffprobe.exe"));
            File.WriteAllText(Path.Combine(root, MediaTools.SelectionFile), "{broken");
            Check(MediaTools.Resolve(root, out problem) == null && problem != null, "corrupt tool preferences are recoverable through the configuration dialog");
            var hardware = new HardwareInfo(); hardware.Gpus.Add(new GpuHardware { Name="test NVIDIA", VendorId=0x10de, DriverVersion="560.94" });
            var inspected = MediaToolInspector.Inspect(external, CancellationToken.None, hardware);
            Check(inspected.Valid && inspected.CpuH264 && inspected.CpuH265 && inspected.NvidiaH264 && !inspected.NvidiaH265, "actual encoder checks distinguish per-codec support while keeping CPU and lossless usable");
            File.WriteAllText(Path.Combine(local, "mismatch"), "");
            Check(!MediaToolInspector.Inspect(external, CancellationToken.None, hardware, false).Valid, "mismatched FFmpeg and FFprobe versions are rejected");
            File.Delete(Path.Combine(local, "mismatch"));
            File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(root, "tools", "ffmpeg-compat.exe"));
            var explicitEngine = new Engine(root, external); explicitEngine.UseHardwareSnapshot(hardware);
            explicitEngine.DetectEncoders(CancellationToken.None, s => { });
            Check(explicitEngine.Plans(ExportMode.H264, EncodingDevice.Nvidia).Single().FFmpeg == external.FFmpeg &&
                !explicitEngine.CanExport(ExportMode.H265, EncodingDevice.Nvidia), "explicit tool selection never falls back to a hidden legacy executable");
            string archive = Path.Combine(root, "runtime.zip"); var spec = Archive(archive, local);
            var installed = MediaToolInstaller.InstallArchive(root, spec, archive, CancellationToken.None);
            Check(File.Exists(Path.Combine(Path.GetDirectoryName(installed.Directory), "licenses", "说明.txt")) &&
                MediaToolInstaller.Hash(installed.FFmpeg) == MediaToolInstaller.Hash(external.FFmpeg), "verified archives install exact executables and retain Unicode license files");
            MediaTools.Save(root, installed);
            string saved = File.ReadAllText(Path.Combine(root, MediaTools.SelectionFile));
            Check(!saved.Contains(root.Replace("\\", "\\\\")) && MediaTools.Resolve(root, out problem).Directory == installed.Directory, "downloaded selection is stored relative to the portable application folder");
            string modified = "keep an existing modified installation"; File.WriteAllText(installed.FFmpeg, modified);
            var second = MediaToolInstaller.InstallArchive(root, spec, archive, CancellationToken.None);
            Check(second.Directory != installed.Directory && File.ReadAllText(installed.FFmpeg) == modified, "installation preserves an existing modified version and creates a separate folder");
            string bad = Path.Combine(root, "corrupt.zip"); File.Copy(archive, bad);
            using (var stream = new FileStream(bad, FileMode.Open, FileAccess.ReadWrite)) { int original = stream.ReadByte(); stream.Position=0; stream.WriteByte((byte)(original ^ 255)); }
            bool rejected = false; string untouched = Path.Combine(root, "checksum-test"); Directory.CreateDirectory(untouched);
            try { MediaToolInstaller.InstallArchive(untouched, spec, bad, CancellationToken.None); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && Directory.GetFileSystemEntries(untouched).Length == 0, "same-size archive tampering is rejected before any installation writes");
            foreach (string malicious in new[] { "runtime/../../escape.txt", "runtime\\..\\..\\escape.txt", "C:/escape.txt" }) {
                string zip = Path.Combine(root, "malicious-" + Guid.NewGuid().ToString("N") + ".zip"); var poison = Archive(zip, local, malicious); rejected = false;
                try { MediaToolInstaller.InstallArchive(root, poison, zip, CancellationToken.None); } catch (InvalidDataException) { rejected = true; }
                Check(rejected && !File.Exists(Path.Combine(root, "escape.txt")), "unsafe archive path is rejected: " + malicious);
            }
            using (var cts = new CancellationTokenSource()) {
                cts.Cancel(); bool cancelled = false;
                try { MediaToolInstaller.DownloadAsync(root, spec, cts.Token, (a,b) => {}, s => {}).GetAwaiter().GetResult(); } catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && Directory.GetDirectories(root, ".ffmpeg-setup-*").Length == 0, "cancelled downloads perform no network request and leave no staging directory");
            }
            string cliRoot = Path.Combine(root, "no-tools"); Directory.CreateDirectory(cliRoot);
            string cli = Path.Combine(cliRoot, "BiliCacheExtractor.Cli.exe"); File.Copy(Path.Combine(project, "BiliCacheExtractor.Cli.exe"), cli);
            var help = ChildProcess.Run(cli, new[] { "--help" }, CancellationToken.None, null, 10);
            Check(help.Code == 0 && help.Output.Contains("--ffmpeg-dir"), "CLI help works before FFmpeg has been installed");
            Console.WriteLine("MEDIA TOOLS RESULT: " + passed + " checks passed. Fixtures: " + root); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
