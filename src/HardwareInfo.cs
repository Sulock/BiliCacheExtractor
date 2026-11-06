using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace BiliExtractor
{
    public sealed class GpuHardware
    {
        public string Name, Id;
        public uint VendorId;
        public ulong DedicatedMemory;
        public ulong? SharedMemory;
        public string MemorySummary
        {
            get { return "专用显存 " + Capacity(DedicatedMemory) + " · 共享系统内存上限 " + (SharedMemory.HasValue ? Capacity(SharedMemory.Value) : "未读取"); }
        }
        public string MemorySummaryText(Localizer text)
        {
            return text.Get("专用显存 {0} · 共享系统内存上限 {1}", Capacity(DedicatedMemory), SharedMemory.HasValue ? Capacity(SharedMemory.Value) : text.Get("未读取"));
        }
        static string Capacity(ulong bytes)
        {
            return bytes >= 1073741824 ? (bytes / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture) + " GiB" :
                (bytes / 1048576.0).ToString("0", CultureInfo.InvariantCulture) + " MiB";
        }
    }

    public sealed class HardwareInfo
    {
        public string CpuName = "CPU 型号未读取", Frequency = "频率未读取";
        public int Cores, Threads;
        public readonly List<GpuHardware> Gpus = new List<GpuHardware>();
        public string CpuSummary
        {
            get { return (Cores > 0 ? Cores.ToString() : "未知") + " 核 " + Threads + " 线程 · " + Frequency; }
        }

        public string CpuSummaryText(Localizer text)
        {
            string frequency = text.Get(Frequency);
            foreach (string prefix in new[] { "系统报告频率", "系统标称频率" })
                if (Frequency.StartsWith(prefix + " ", StringComparison.Ordinal)) frequency = text.Get(prefix + " {0}", Frequency.Substring(prefix.Length + 1));
            return text.Get("{0} 核 {1} 线程 · {2}", Cores > 0 ? Cores.ToString() : text.Get("未知"), Threads, frequency);
        }

        public static HardwareInfo Read(CancellationToken token, Action<string> log, Localizer text = null)
        {
            if (text == null) text = new Localizer();
            var info = new HardwareInfo { Threads = Environment.ProcessorCount };
            token.ThrowIfCancellationRequested();
            try
            {
                using (var search = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor"))
                {
                    search.Options.Timeout = TimeSpan.FromSeconds(5);
                    using (var processors = search.Get())
                    {
                        var names = new List<string>(); var frequencies = new List<string>(); int threads = 0;
                        foreach (ManagementObject cpu in processors)
                        using (cpu)
                        {
                            token.ThrowIfCancellationRequested();
                            names.Add(Convert.ToString(cpu["Name"]).Trim());
                            info.Cores += Convert.ToInt32(cpu["NumberOfCores"]);
                            threads += Convert.ToInt32(cpu["NumberOfLogicalProcessors"]);
                            double mhz = Convert.ToDouble(cpu["MaxClockSpeed"]);
                            if (mhz > 0) frequencies.Add((mhz / 1000).ToString("0.00", CultureInfo.InvariantCulture) + " GHz");
                        }
                        if (names.Count > 0) info.CpuName = string.Join(" / ", names.Distinct());
                        if (threads > 0) info.Threads = threads;
                        if (frequencies.Count > 0) info.Frequency = "系统报告频率 " + string.Join(" / ", frequencies.Distinct());
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                log(text.Get("CPU 详细信息：{0}", ex.Message));
                // WMI can be disabled by system policy. These local Windows sources
                // still provide the model, nominal frequency and processor topology.
                using (var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    if (cpu != null)
                    {
                        info.CpuName = Convert.ToString(cpu.GetValue("ProcessorNameString", info.CpuName)).Trim();
                        double mhz; if (double.TryParse(Convert.ToString(cpu.GetValue("~MHz")), out mhz) && mhz > 0)
                            info.Frequency = "系统标称频率 " + (mhz / 1000).ToString("0.00", CultureInfo.InvariantCulture) + " GHz";
                    }
                }
            }
            if (info.Cores == 0) info.Cores = CountCores();
            token.ThrowIfCancellationRequested();
            try { ReadAdapters(info.Gpus); }
            catch (Exception ex) { log(text.Get("GPU 信息读取失败：{0}", ex.Message)); }
            // NVML reports installed NVIDIA memory and physical devices, avoiding
            // duplicate DXGI entries introduced by indirect display drivers.
            try
            {
                var result = ChildProcess.Run("nvidia-smi.exe", new[] { "--query-gpu=uuid,name,memory.total", "--format=csv,noheader,nounits" }, token, null, 8);
                var nvidia = new List<GpuHardware>();
                if (result.Code == 0)
                foreach (string line in result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] fields = line.Split(','); double mib;
                    if (fields.Length != 3 || !double.TryParse(fields[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out mib) || mib <= 0) continue;
                    string name = fields[1].Trim(); var dxgi = info.Gpus.FirstOrDefault(g => g.VendorId == 0x10de && g.Name == name);
                    nvidia.Add(new GpuHardware { Id = fields[0].Trim(), Name = name, VendorId = 0x10de,
                        DedicatedMemory = (ulong)(mib * 1048576), SharedMemory = dxgi == null ? (ulong?)null : dxgi.SharedMemory });
                }
                if (nvidia.Count > 0) { info.Gpus.RemoveAll(g => g.VendorId == 0x10de); info.Gpus.AddRange(nvidia); }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { }
            log(text.Get("CPU：{0}", info.CpuName) + " · " + info.CpuSummaryText(text));
            foreach (var gpu in info.Gpus) log("GPU: " + gpu.Name + " · " + gpu.MemorySummaryText(text));
            return info;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);
        static int CountCores()
        {
            uint length = 0; GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref length);
            if (length == 0) return 0;
            IntPtr buffer = Marshal.AllocHGlobal((int)length);
            try
            {
                if (!GetLogicalProcessorInformationEx(0, buffer, ref length)) return 0;
                int count = 0, offset = 0;
                while (offset + 8 <= length)
                {
                    int size = Marshal.ReadInt32(buffer, offset + 4);
                    if (size < 8 || offset + size > length) break;
                    if (Marshal.ReadInt32(buffer, offset) == 0) count++;
                    offset += size;
                }
                return count;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        // DXGI uses pointer-sized memory capacities. Win32_VideoController.AdapterRAM
        // is a 32-bit field and truncates the memory size of modern discrete GPUs.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct AdapterDescription
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
            public uint VendorId, DeviceId, SubSysId, Revision;
            public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
            public uint LuidLow; public int LuidHigh; public uint Flags;
        }
        [DllImport("dxgi.dll")]
        static extern int CreateDXGIFactory1(ref Guid id, out IntPtr factory);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int EnumAdapters(IntPtr factory, uint index, out IntPtr adapter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int GetDescription(IntPtr adapter, out AdapterDescription description);
        static Delegate Method(IntPtr obj, int slot, Type type)
        {
            return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size), type);
        }
        static void ReadAdapters(List<GpuHardware> output)
        {
            var id = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); IntPtr factory;
            Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref id, out factory));
            try
            {
                var enumerate = (EnumAdapters)Method(factory, 12, typeof(EnumAdapters));
                for (uint i = 0; ; i++)
                {
                    IntPtr adapter; int hr = enumerate(factory, i, out adapter);
                    if (hr == unchecked((int)0x887A0002)) break;
                    Marshal.ThrowExceptionForHR(hr);
                    try
                    {
                        AdapterDescription desc;
                        Marshal.ThrowExceptionForHR(((GetDescription)Method(adapter, 10, typeof(GetDescription)))(adapter, out desc));
                        if ((desc.Flags & 2) != 0) continue; // software renderer
                        string identity = desc.LuidHigh.ToString("X8") + desc.LuidLow.ToString("X8");
                        if (output.Any(g => g.Id == identity)) continue;
                        output.Add(new GpuHardware { Id = identity, Name = desc.Description.Trim(), VendorId = desc.VendorId,
                            DedicatedMemory = desc.DedicatedVideoMemory.ToUInt64(), SharedMemory = desc.SharedSystemMemory.ToUInt64() });
                    }
                    finally { Marshal.Release(adapter); }
                }
            }
            finally { Marshal.Release(factory); }
        }
    }
}
