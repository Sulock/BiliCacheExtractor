using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BiliExtractor;

static class RecycleTests
{
    static int passed;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message); passed++;
    }
    static bool Rejected(Action action)
    {
        try { action(); return false; } catch (IOException) { return true; }
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            var text = new Localizer();
            string test = Path.Combine(Path.GetFullPath(args[0]), "verification", "recycle-test-" + Guid.NewGuid().ToString("N"));
            string root = Path.Combine(test, "cache"), folder = Path.Combine(root, "中文 & selected"), nested = Path.Combine(folder, "unselected");
            Directory.CreateDirectory(nested);
            var entry = new CacheEntry { Directory = folder, Title = "回收站测试", Selected = true };
            var original = new Dictionary<string, string>();
            foreach (string name in new[] { "video.m4s", "audio.m4s", "videoInfo.json" })
            {
                string path = Path.Combine(folder, name), content = "Recycle test " + Guid.NewGuid().ToString("N");
                File.WriteAllText(path, content); original[path] = content; entry.CacheFiles.Add(path);
            }
            string output = Path.Combine(folder, "keep-output.mp4"), notes = Path.Combine(folder, "keep-notes.txt"), other = Path.Combine(nested, "keep-unselected.m4s");
            File.WriteAllText(output, "preserve output"); File.WriteAllText(notes, "preserve notes"); File.WriteAllText(other, "preserve unselected cache");
            var plan = CacheDeletionPlan.Create(root, new[] { entry }, text);
            Check(plan.Files.Count == 3 && plan.EntryCount == 1 && !plan.Files.Any(f => f.Path == output || f.Path == notes || f.Path == other),
                "delete plan contains only selected tracks and metadata, retaining exports, notes and nested unselected caches");
            var dangerous = new CacheEntry { Directory = folder, Title = "invalid" }; dangerous.CacheFiles.Add(output);
            Check(Rejected(() => CacheDeletionPlan.Create(root, new[] { dangerous }, text)), "MP4 files cannot be submitted for cache deletion");
            dangerous.CacheFiles.Clear(); dangerous.CacheFiles.Add(other);
            Check(Rejected(() => CacheDeletionPlan.Create(root, new[] { dangerous }, text)), "a selected row cannot delete another directory's cache files");
            string outside = root + "-outside"; Directory.CreateDirectory(outside);
            dangerous.Directory = outside; dangerous.CacheFiles.Clear(); dangerous.CacheFiles.Add(Path.Combine(outside, "video.m4s"));
            File.WriteAllText(dangerous.CacheFiles[0], "outside cache root");
            Check(Rejected(() => CacheDeletionPlan.Create(root, new[] { dangerous }, text)), "matching path prefixes outside the actual cache root are rejected");
            File.AppendAllText(entry.CacheFiles[0], "changed");
            Check(Rejected(() => plan.Validate(plan.Files[0], text)), "a file changed after confirmation cannot be recycled using a stale plan");
            File.WriteAllText(entry.CacheFiles[0], original[entry.CacheFiles[0]]);
            plan = CacheDeletionPlan.Create(root, new[] { entry }, text);
            bool cancelled = false;
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                try { CacheRecycle.RunAsync(plan, IntPtr.Zero, cancellation.Token, text).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { cancelled = true; }
            }
            Check(cancelled && original.All(f => File.ReadAllText(f.Key) == f.Value), "cancelling before a recycle operation leaves every cache file intact");
            var directRootEntry = new CacheEntry { Directory = folder, Title = "root-level cache" }; directRootEntry.CacheFiles.AddRange(entry.CacheFiles);
            Check(CacheDeletionPlan.Create(folder, new[] { directRootEntry }, text).Files.Count == 3,
                "a cache folder selected as the scan root recycles its listed files without deleting the root directory");
            if (args.Contains("--recycle"))
            {
                var result = CacheRecycle.RunAsync(plan, IntPtr.Zero, CancellationToken.None, text).GetAwaiter().GetResult();
                foreach (var error in result.Failed) Console.WriteLine(error.Key + ": " + error.Value);
                Check(result.Recycled.Count == 3 && result.Failed.Count == 0 && original.Keys.All(p => !File.Exists(p)),
                    "Windows recycles all three selected test files");
                Check(File.Exists(output) && File.Exists(notes) && File.Exists(other) && Directory.Exists(folder),
                    "recycling retains exported MP4s, unrelated files, nested caches and the parent folder");
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic bin = shell.NameSpace(10); dynamic items = bin.Items();
                var matches = new List<object>();
                for (int i = 0; i < (int)items.Count; i++)
                {
                    dynamic item = items.Item(i);
                    string deletedFrom = Convert.ToString(item.ExtendedProperty("System.Recycle.DeletedFrom"));
                    if (string.Equals(deletedFrom, folder, StringComparison.OrdinalIgnoreCase)) matches.Add(item);
                }
                Check(matches.Count == 3, "all test files are visible in the Windows Recycle Bin with their original directory");
                foreach (dynamic item in matches) item.InvokeVerb("undelete");
                var end = DateTime.UtcNow.AddSeconds(10);
                while (original.Keys.Any(p => !File.Exists(p)) && DateTime.UtcNow < end) Thread.Sleep(100);
                Check(original.All(f => File.Exists(f.Key) && File.ReadAllText(f.Key) == f.Value), "Recycle Bin restore recovers all test files with identical contents");
            }
            Console.WriteLine("RECYCLE RESULT: " + passed + " checks passed. Fixtures: " + test); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
