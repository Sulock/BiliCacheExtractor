using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace BiliExtractor
{
    public sealed class CacheFileSnapshot
    {
        public readonly string Path, Title;
        public readonly long Length;
        public readonly DateTime Written, Created;
        internal CacheFileSnapshot(string path, string title)
        {
            var file = new FileInfo(path);
            Path = file.FullName; Title = title; Length = file.Length; Written = file.LastWriteTimeUtc; Created = file.CreationTimeUtc;
        }
    }
    public sealed class CacheDeletionPlan
    {
        public readonly string Root;
        public readonly System.Collections.ObjectModel.ReadOnlyCollection<CacheFileSnapshot> Files;
        public readonly int EntryCount;
        public long Bytes { get { return Files.Sum(f => f.Length); } }
        CacheDeletionPlan(string root, List<CacheFileSnapshot> files, int count) { Root = root; Files = files.AsReadOnly(); EntryCount = count; }
        static string DirectoryPath(string path)
        {
            string full = Path.GetFullPath(path), drive = Path.GetPathRoot(full);
            return full.Length == drive.Length ? full : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        static bool Within(string root, string path)
        {
            return path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        public static CacheDeletionPlan Create(string root, IEnumerable<CacheEntry> entries, Localizer text)
        {
            root = DirectoryPath(root);
            var files = new List<CacheFileSnapshot>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int count = 0;
            foreach (var entry in entries)
            {
                string folder = DirectoryPath(entry.Directory);
                if (!folder.Equals(root, StringComparison.OrdinalIgnoreCase) && !Within(root, folder))
                    throw new IOException(text.Get("缓存文件不在扫描目录内：{0}", folder));
                bool included = false;
                foreach (string source in entry.CacheFiles)
                {
                    string path = Path.GetFullPath(source);
                    if (!DirectoryPath(Path.GetDirectoryName(path)).Equals(folder, StringComparison.OrdinalIgnoreCase))
                        throw new IOException(text.Get("缓存文件不在扫描目录内：{0}", path));
                    ValidatePath(root, path, text);
                    if (!File.Exists(path) || !seen.Add(path)) continue;
                    files.Add(new CacheFileSnapshot(path, entry.Title)); included = true;
                }
                if (included) count++;
            }
            if (files.Count == 0) throw new IOException(text.Get("没有可删除的缓存文件，请重新扫描。"));
            return new CacheDeletionPlan(root, files, count);
        }
        static void ValidatePath(string root, string path, Localizer text)
        {
            if (!Within(root, path)) throw new IOException(text.Get("缓存文件不在扫描目录内：{0}", path));
            if (!Path.GetExtension(path).Equals(".m4s", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(path).Equals("videoInfo.json", StringComparison.OrdinalIgnoreCase))
                throw new IOException(text.Get("仅可回收扫描到的 .m4s 与 videoInfo.json 文件：{0}", path));
            for (string current = path; ; current = Path.GetDirectoryName(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException(text.Get("不支持通过链接删除缓存：{0}", current));
                if (current.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
                if (string.IsNullOrEmpty(Path.GetDirectoryName(current))) throw new IOException(text.Get("缓存文件不在扫描目录内：{0}", path));
            }
        }
        public void Validate(CacheFileSnapshot file, Localizer text)
        {
            ValidatePath(Root, file.Path, text);
            var current = new FileInfo(file.Path);
            if (!current.Exists || current.Length != file.Length || current.LastWriteTimeUtc != file.Written || current.CreationTimeUtc != file.Created)
                throw new IOException(text.Get("缓存文件已改变，请重新扫描并确认：{0}", file.Path));
            if (file.Path.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(Path.GetPathRoot(file.Path)).DriveType != DriveType.Fixed)
                throw new IOException(text.Get("该位置无法安全使用回收站，文件已保留：{0}", file.Path));
        }
    }

    public sealed class RecycleResult
    {
        public readonly HashSet<string> Recycled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Destinations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Failed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public bool Cancelled;
    }
    public static class CacheRecycle
    {
        public static Task<RecycleResult> RunAsync(CacheDeletionPlan plan, IntPtr owner, CancellationToken token, Localizer text)
        {
            var completion = new TaskCompletionSource<RecycleResult>();
            var thread = new Thread(delegate() {
                try { completion.SetResult(Run(plan, owner, token, text)); }
                catch (OperationCanceledException) { completion.SetCanceled(); }
                catch (Exception ex) { completion.SetException(ex); }
            }) { IsBackground = true, Name = "Cache recycle" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
        }
        static RecycleResult Run(CacheDeletionPlan plan, IntPtr owner, CancellationToken token, Localizer text)
        {
            token.ThrowIfCancellationRequested();
            foreach (var file in plan.Files) plan.Validate(file, text);
            var result = new RecycleResult(); var sink = new RecycleSink(plan, token, text, result);
            var operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09")));
            var items = new List<IShellItem>();
            try
            {
                // Windows 8+: explicitly recycle, add an undo record and stop on errors.
                // There is no permanent-delete fallback in this application.
                operation.SetOperationFlags(0x00080000 | 0x20000000 | 0x00100000 | 0x0004 | 0x0010 | 0x0400 | 0x1000);
                operation.SetOwnerWindow(owner);
                foreach (var file in plan.Files)
                {
                    IShellItem item; var id = typeof(IShellItem).GUID;
                    Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(file.Path, IntPtr.Zero, ref id, out item));
                    items.Add(item); operation.DeleteItem(item, sink);
                }
                try { operation.PerformOperations(); }
                catch (COMException ex)
                {
                    foreach (var file in plan.Files.Where(f => !result.Recycled.Contains(f.Path)))
                        if (!result.Failed.ContainsKey(file.Path)) result.Failed[file.Path] = ex.Message;
                }
                bool aborted; operation.GetAnyOperationsAborted(out aborted);
                result.Cancelled = token.IsCancellationRequested;
                foreach (var file in plan.Files.Where(f => !result.Recycled.Contains(f.Path)))
                    if (!result.Failed.ContainsKey(file.Path)) result.Failed[file.Path] = text.Get("系统未执行回收站操作，文件已保留。");
                GC.KeepAlive(sink); return result;
            }
            finally
            {
                foreach (var item in items) Marshal.ReleaseComObject(item);
                Marshal.ReleaseComObject(operation);
            }
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid id, out IShellItem item);
        internal static string FilePath(IShellItem item)
        {
            IntPtr value; item.GetDisplayName(0x80058000, out value);
            try { return Marshal.PtrToStringUni(value); } finally { Marshal.FreeCoTaskMem(value); }
        }
        [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
        sealed class RecycleSink : IFileOperationProgressSink
        {
            readonly CacheDeletionPlan plan; readonly CancellationToken token; readonly Localizer text; readonly RecycleResult result;
            public RecycleSink(CacheDeletionPlan plan, CancellationToken token, Localizer text, RecycleResult result)
            { this.plan = plan; this.token = token; this.text = text; this.result = result; }
            public int PreDeleteItem(uint flags, IShellItem item)
            {
                string path = "";
                try
                {
                    token.ThrowIfCancellationRequested(); path = FilePath(item);
                    var file = plan.Files.Single(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
                    plan.Validate(file, text);
                    // TSF_DELETE_RECYCLE_IF_POSSIBLE must be set by the shell.
                    if ((flags & 0x80) == 0) throw new IOException(text.Get("系统未执行回收站操作，文件已保留。"));
                    return 0;
                }
                catch (Exception ex) { if (path.Length > 0) result.Failed[path] = ex.Message; return unchecked((int)0x80004004); }
            }
            public int PostDeleteItem(uint flags, IShellItem item, int hr, IShellItem recycled)
            {
                try
                {
                    string path = FilePath(item);
                    if (hr >= 0 && !File.Exists(path))
                    {
                        result.Recycled.Add(path);
                        if (recycled != null) { try { result.Destinations[path] = FilePath(recycled); } catch (COMException) { } }
                    }
                    else result.Failed[path] = hr < 0 ? Marshal.GetExceptionForHR(hr).Message : text.Get("系统未执行回收站操作，文件已保留。");
                }
                catch (Exception) { return unchecked((int)0x80004005); }
                return 0;
            }
            public int StartOperations() { return 0; }
            public int FinishOperations(int hr) { return 0; }
            public int PreRenameItem(uint flags, IShellItem item, string name) { return 0; }
            public int PostRenameItem(uint flags, IShellItem item, string name, int hr, IShellItem created) { return 0; }
            public int PreMoveItem(uint flags, IShellItem item, IShellItem folder, string name) { return 0; }
            public int PostMoveItem(uint flags, IShellItem item, IShellItem folder, string name, int hr, IShellItem created) { return 0; }
            public int PreCopyItem(uint flags, IShellItem item, IShellItem folder, string name) { return 0; }
            public int PostCopyItem(uint flags, IShellItem item, IShellItem folder, string name, int hr, IShellItem created) { return 0; }
            public int PreNewItem(uint flags, IShellItem folder, string name) { return 0; }
            public int PostNewItem(uint flags, IShellItem folder, string name, string template, uint attrs, int hr, IShellItem created) { return 0; }
            public int UpdateProgress(uint total, uint done) { return 0; }
            public int ResetTimer() { return 0; }
            public int PauseTimer() { return 0; }
            public int ResumeTimer() { return 0; }
        }
    }
    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
        void BindToHandler(IntPtr context, ref Guid handler, ref Guid id, out IntPtr value);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint type, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }
    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileOperation
    {
        void Advise(IFileOperationProgressSink sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog([MarshalAs(UnmanagedType.Interface)] object dialog);
        void SetProperties([MarshalAs(UnmanagedType.Interface)] object properties);
        void SetOwnerWindow(IntPtr owner);
        void ApplyPropertiesToItem(IShellItem item);
        void ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object items);
        void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
        void RenameItems([MarshalAs(UnmanagedType.IUnknown)] object items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        void MoveItem(IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
        void MoveItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem folder);
        void CopyItem(IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
        void CopyItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem folder);
        void DeleteItem(IShellItem item, IFileOperationProgressSink sink);
        void DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object items);
        void NewItem(IShellItem folder, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, IFileOperationProgressSink sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
    [ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int hr);
        [PreserveSig] int PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, int hr, IShellItem created);
        [PreserveSig] int PreMoveItem(uint flags, IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostMoveItem(uint flags, IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string name, int hr, IShellItem created);
        [PreserveSig] int PreCopyItem(uint flags, IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostCopyItem(uint flags, IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string name, int hr, IShellItem created);
        [PreserveSig] int PreDeleteItem(uint flags, IShellItem item);
        [PreserveSig] int PostDeleteItem(uint flags, IShellItem item, int hr, IShellItem recycled);
        [PreserveSig] int PreNewItem(uint flags, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostNewItem(uint flags, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, uint attributes, int hr, IShellItem created);
        [PreserveSig] int UpdateProgress(uint total, uint done);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }
}
