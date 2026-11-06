using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using BiliExtractor;

static class UiTests
{
    static int passed;
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message); passed++;
    }
    static T Field<T>(MainForm form, string name)
    {
        return (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
    }
    static void Call(MainForm form, string name, params object[] args)
    {
        typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, args);
    }
    static void CreateHandles(Control control)
    {
        // Create complete child controls even though the test window stays off screen.
        typeof(Control).GetMethod("CreateControl", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(bool) }, null).Invoke(control, new object[] { true });
        var form = control as Form;
        if (form != null && !form.Visible)
        {
            // UI tests supply their own fixtures; suppress production startup scanning.
            var shownField = typeof(Form).GetFields(BindingFlags.Static | BindingFlags.NonPublic)
                .Single(field => field.Name.IndexOf("shown", StringComparison.OrdinalIgnoreCase) >= 0);
            var events = (System.ComponentModel.EventHandlerList)typeof(Control)
                .GetProperty("Events", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form, null);
            object key = shownField.GetValue(null);
            events.RemoveHandler(key, events[key]);
            // Create an off-screen window so native combo boxes render their selected text.
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-30000, -30000);
            form.Show();
        }
        control.PerformLayout();
        Application.DoEvents();
    }
    static void SelectDevice(MainForm form, EncodingDevice value)
    {
        var choices = Field<ComboBox>(form, "device");
        for (int i = 0; i < choices.Items.Count; i++)
            if (((KeyValuePair<EncodingDevice, string>)choices.Items[i]).Key == value) { choices.SelectedIndex = i; return; }
        throw new Exception("Device option missing: " + value);
    }
    static CacheEntry AddEntry(MainForm form, bool selected = true, bool ready = true)
    {
        var entry = new CacheEntry { Title = ready ? "简体标题保持原样" : "未完成的缓存", Id = ready ? "001" : "002", Directory = @"C:\test-cache",
            Detail = @"C:\test-cache", Ready = ready, Selected = selected, Status = ready ? "待提取" : "下载未完成",
            Video = new MediaTrack { Width = 1920, Height = 1080, Duration = 60 } };
        Field<List<CacheEntry>>(form, "entries").Add(entry); Call(form, "PopulateGrid"); return entry;
    }
    static void Render(MainForm form, string path)
    {
        CreateHandles(form); form.PerformLayout();
        using (var bitmap = new Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(path, ImageFormat.Png); }
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string root = Path.GetFullPath(args[0]);
            string test = Path.Combine(root, "verification", "ui-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            string tools = Path.Combine(test, "tools"); Directory.CreateDirectory(tools);
            string encoderStub = Path.Combine(root, "verification", "IntegrationTests.exe");
            File.Copy(encoderStub, Path.Combine(tools, "ffmpeg.exe"));
            File.Copy(encoderStub, Path.Combine(tools, "ffmpeg-compat.exe"));
            File.WriteAllText(Path.Combine(tools, "ffprobe.exe"), "unused in UI tests");
            // Only the controls and settings are exercised here; media tests use real FFmpeg.
            var engine = new Engine(test); engine.DetectEncoders(CancellationToken.None, s => { });
            var hardware = new HardwareInfo { CpuName = "硬件显示测试 CPU", Cores = 10, Threads = 16, Frequency = "系统报告频率 2.50 GHz" };
            hardware.Gpus.Add(new GpuHardware { Name = "硬件显示测试 GPU", VendorId = 0x10de, DedicatedMemory = 16UL * 1073741824, SharedMemory = 16UL * 1073741824 });
            typeof(Engine).GetProperty("Hardware").SetValue(engine, hardware, null);
            string settings = Path.Combine(test, "settings.json");
            File.WriteAllText(settings, "{\"Source\":\"C:\\\\test-cache\",\"Destination\":\"C:\\\\test-output\"}");
            using (var form = new MainForm(engine))
            {
                var mode = Field<ComboBox>(form, "mode"); var decode = Field<CheckBox>(form, "decode");
                var start = Field<Button>(form, "start");
                var delete = Field<Button>(form, "delete");
                Check(mode.Items.Count == 3 && mode.SelectedIndex == 0 && decode.Checked && !decode.Enabled && !Field<ComboBox>(form, "device").Enabled,
                    "old settings open in lossless mode and disable irrelevant device controls");
                Check(!start.Enabled && start.BackColor == Color.FromArgb(221, 226, 234) && delete.BackColor == Color.FromArgb(209, 53, 64),
                    "extract starts disabled and gray, while delete remains red with no selection");
                var ready = AddEntry(form, false); var incomplete = AddEntry(form, false, false);
                Check(!start.Enabled, "scanned but unchecked videos do not activate extraction");
                Field<DataGridView>(form, "grid").Rows[0].Cells[0].Value = true;
                Check(start.Enabled && start.BackColor == Color.FromArgb(33, 103, 224), "checking a ready video turns extraction blue");
                Field<TextBox>(form, "search").Text = "no visible matches";
                Check(start.Enabled && Field<Label>(form, "count").Text.Contains("隐藏"), "a hidden checked video is still counted and remains selected");
                Call(form, "SelectVisible", false);
                Check(!start.Enabled && start.BackColor == Color.FromArgb(221, 226, 234), "clearing selection also clears hidden checks and returns extraction to gray");
                Field<TextBox>(form, "search").Text = "";
                incomplete.Selected = true; Call(form, "RefreshChecks");
                Check(!start.Enabled && delete.Enabled, "incomplete caches can be selected for deletion without enabling extraction");
                incomplete.Selected = false; ready.Selected = true; Call(form, "RefreshChecks");
                mode.SelectedIndex = 2;
                Check(decode.Enabled && start.Enabled && Field<Label>(form, "hint").Text.Contains("H.265"),
                    "H.265 auto mode selects available NVIDIA encoding and enables GPU decoding");
                SelectDevice(form, EncodingDevice.Cpu);
                Check(!decode.Enabled && start.Enabled && Field<Label>(form, "hint").Text.Contains("CPU"),
                    "manual CPU selection changes the actual route and disables NVIDIA decoding");
                Call(form, "SetBusy", true);
                Check(!mode.Enabled && !Field<ComboBox>(form, "device").Enabled && !decode.Enabled && !start.Enabled, "format and device controls remain locked during a task");
                Check(delete.BackColor == Color.FromArgb(209, 53, 64) && !delete.Enabled && !Field<ComboBox>(form, "language").Enabled,
                    "delete stays red while task controls and language switching are locked");
                Call(form, "SetBusy", false);
                decode.Checked = false; Call(form, "SavePreferences");
                Call(form, "UpdateHardwareLabels");
                Check(Field<Label>(form, "cpuSpecs").Text.Contains("10 核 16 线程") && Field<Label>(form, "cpuSpecs").Text.Contains("2.50 GHz") &&
                    Field<TextBox>(form, "gpu").Text.Contains("专用显存 16.0 GiB") && Field<TextBox>(form, "gpu").Text.Contains("共享系统内存上限 16.0 GiB"),
                    "hardware panel displays core/thread counts, labeled frequency and separate 64-bit dedicated/shared memory capacities");
                CreateHandles(form);
                Check(mode.Text.Contains("H.265"), "the selected H.265 option has a visible display label");
                foreach (int width in new[] { 1100, 984 })
                {
                    form.ClientSize = new Size(width, width == 1100 ? 860 : 742); form.PerformLayout();
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                        bitmap.Save(Path.Combine(test, "h265-" + width + ".png"), ImageFormat.Png);
                    }
                }
                form.ClientSize = new Size(1100, 860);
                var languages = Field<ComboBox>(form, "language");
                languages.SelectedIndex = 2;
                Check(form.Text == "Bilibili Cache Extractor" && delete.Text == "Delete cache" &&
                    Field<DataGridView>(form, "grid").Columns[1].HeaderText == "Video title" && Field<Label>(form, "cpuSpecs").Text.Contains("10 cores / 16 threads") &&
                    Field<Label>(form, "hint").Text.Contains("CPU software encoding") && ready.Title == "简体标题保持原样" && ready.Selected && mode.SelectedIndex == 2,
                    "English updates labels, headers, hardware and encoder descriptions without changing titles, selection or format");
                Check(Field<Dictionary<Control, string>>(form, "labels").All(binding => Localizer.HasEnglish(binding.Value)), "the English catalog covers every static UI label");
                Render(form, Path.Combine(test, "english-selected.png"));
                languages.SelectedIndex = 1;
                Check(form.Text.Contains("緩存") && delete.Text == "刪除緩存" && Field<Label>(form, "hint").Text.Contains("編碼") && ready.Title == "简体标题保持原样",
                    "Traditional Chinese converts interface text while preserving user content");
                Render(form, Path.Combine(test, "traditional-selected.png"));
                languages.SelectedIndex = 0;
                ready.Selected = false; Call(form, "RefreshChecks");
                Render(form, Path.Combine(test, "simplified-unselected.png"));
                ready.Selected = true; Call(form, "RefreshChecks");
            }
            using (var form = new MainForm(engine))
            {
                AddEntry(form);
                Check(Field<ComboBox>(form, "mode").SelectedIndex == 2 && !Field<CheckBox>(form, "decode").Checked &&
                    ((KeyValuePair<EncodingDevice, string>)Field<ComboBox>(form, "device").SelectedItem).Key == EncodingDevice.Cpu,
                    "H.265 format, CPU device and decode preference survive saving and reopening");
                Field<ComboBox>(form, "mode").SelectedIndex = 0;
                Check(!Field<CheckBox>(form, "decode").Enabled && Field<Button>(form, "start").Enabled,
                    "returning to lossless mode disables the decode option and permits extraction");
            }
            File.Delete(Path.Combine(tools, "ffmpeg-compat.exe"));
            engine.DetectEncoders(CancellationToken.None, s => { });
            using (var form = new MainForm(engine))
            {
                AddEntry(form);
                SelectDevice(form, EncodingDevice.Auto);
                Check(Field<Button>(form, "start").Enabled && !Field<CheckBox>(form, "decode").Enabled && Field<Label>(form, "hint").Text.Contains("CPU"),
                    "automatic HEVC remains usable through CPU when NVIDIA HEVC is unavailable");
            }
            File.WriteAllText(settings, new JavaScriptSerializer().Serialize(new Preferences { Mode = "h265", Device = "nvidia" }));
            using (var form = new MainForm(engine))
            {
                AddEntry(form);
                Check(!Field<Button>(form, "start").Enabled && Field<Label>(form, "hint").Text.Contains("不可用"),
                    "an unavailable saved device stays selected and explains why export is disabled");
                Field<ComboBox>(form, "mode").SelectedIndex = 1;
                Check(Field<Button>(form, "start").Enabled, "H.264 remains selectable when only H.265 is unavailable");
                Field<ComboBox>(form, "language").SelectedIndex = 2;
            }
            using (var form = new MainForm(engine)) Check(form.Text == "Bilibili Cache Extractor", "the selected language is restored on reopening");
            Check(new Localizer { Language = "zh-TW" }.Get("扫描：{0}", "简体标题") == "掃描：简体标题", "localization preserves formatted user text exactly");
            string cache = Path.Combine(test, "delete-preview"); Directory.CreateDirectory(cache);
            var toDelete = new CacheEntry { Directory = cache, Title = "确认窗口测试" };
            string cacheFile = Path.Combine(cache, "video.m4s"); File.WriteAllText(cacheFile, "preview only"); toDelete.CacheFiles.Add(cacheFile);
            var plan = CacheDeletionPlan.Create(cache, new[] { toDelete }, new Localizer());
            using (var dialog = new DeleteConfirmationDialog(plan, 1, new Localizer { Language = "en" }))
            {
                Check(dialog.AcceptButton == dialog.CancelButton && ((Button)dialog.AcceptButton).DialogResult == DialogResult.Cancel && File.Exists(cacheFile),
                    "deletion requires a separate confirmation whose default action is Cancel");
                typeof(Control).GetMethod("CreateControl", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null).Invoke(dialog, new object[] { true });
                using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size)); bitmap.Save(Path.Combine(test, "delete-confirmation.png"), ImageFormat.Png); }
            }
            Console.WriteLine("UI RESULT: " + passed + " checks passed. Renders: " + test);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
