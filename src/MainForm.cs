using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace BiliExtractor
{
    public sealed class Preferences
    {
        public string Source { get; set; }
        public string Destination { get; set; }
        public string Mode { get; set; }
        public string Device { get; set; }
        public string Language { get; set; }
        public bool GpuDecode { get; set; }
        public Preferences() { Mode = "copy"; Device = "auto"; Language = "zh-CN"; GpuDecode = true; }
    }

    public sealed class MainForm : Form
    {
        readonly Engine engine;
        readonly Icon applicationIcon;
        readonly Color ink = Color.FromArgb(27, 42, 61), muted = Color.FromArgb(99, 115, 135), blue = Color.FromArgb(33, 103, 224);
        readonly TextBox source = new TextBox(), destination = new TextBox(), search = new TextBox(), logs = new TextBox(), gpu = new TextBox();
        readonly ComboBox mode = new ComboBox(), device = new ComboBox(), language = new ComboBox();
        readonly CheckBox decode = new CheckBox();
        readonly DataGridView grid = new DataGridView();
        readonly Label cpu = new Label(), cpuSpecs = new Label(), count = new Label(), status = new Label(), hint = new Label(), explanation = new Label(), activity = new Label();
        readonly ProgressBar progress = new ProgressBar();
        readonly List<Control> lockWhileBusy = new List<Control>();
        readonly Dictionary<CacheEntry, DataGridViewRow> rows = new Dictionary<CacheEntry, DataGridViewRow>();
        readonly Dictionary<Control, string> labels = new Dictionary<Control, string>(), accessibleNames = new Dictionary<Control, string>();
        Button scan, start, cancel, delete;
        List<CacheEntry> entries = new List<CacheEntry>();
        CancellationTokenSource cancellation;
        bool busy, closePending, changingGrid, changingDevices, localizing;
        EncodingDevice requestedDevice = EncodingDevice.Auto;
        string scannedSource = "";
        string statusKey = "准备扫描缓存…";
        object[] statusArgs = new object[0];

        ExportMode SelectedMode { get { return ((KeyValuePair<ExportMode, string>)mode.SelectedItem).Key; } }

        public MainForm(Engine engine)
        {
            this.engine = engine;
            Text = T("Bilibili 缓存提取器");
            Font = new Font("Microsoft YaHei UI", 9.5f);
            AutoScaleDimensions = new SizeF(96f, 96f); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1100, 860); MinimumSize = new Size(1000, 780);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(244, 247, 251); ForeColor = ink;
            using (var resource = typeof(MainForm).Assembly.GetManifestResourceStream("BiliExtractor.AppIcon"))
                applicationIcon = new Icon(resource);
            Icon = applicationIcon;
            BuildLayout(); LoadPreferences(); ApplyLanguage();
            Shown += async delegate { if (await RefreshHardwareAsync() && !closePending) await ScanAsync(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (busy) { e.Cancel = true; closePending = true; cancellation.Cancel(); SetStatus("正在结束任务…"); }
                else SavePreferences();
            };
        }

        string T(string key, params object[] args) { return engine.Text.Get(key, args); }
        void Bind(Control control, string key) { labels[control] = key; control.Text = T(key); }
        void Accessible(Control control, string key) { accessibleNames[control] = key; control.AccessibleName = T(key); }
        void SetStatus(string key, params object[] args) { statusKey = key; statusArgs = args; status.Text = T(key, args); }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && applicationIcon != null) applicationIcon.Dispose();
        }

        Button MakeButton(string text, bool primary = false)
        {
            var button = new Button { Height = 34, Width = 104, UseVisualStyleBackColor = false,
                FlatStyle = FlatStyle.Flat, BackColor = primary ? blue : Color.White,
                ForeColor = primary ? Color.White : ink, Cursor = Cursors.Hand, Margin = new Padding(4, 0, 4, 0) };
            button.FlatAppearance.BorderColor = primary ? blue : Color.FromArgb(209, 218, 229);
            Bind(button, text); Accessible(button, text);
            return button;
        }

        Label TextLabel(string text, float size = 9.5f, bool bold = false)
        {
            var label = new Label { AutoSize = false, Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular), TextAlign = ContentAlignment.MiddleLeft };
            Bind(label, text); return label;
        }

        void BuildLayout()
        {
            var main = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 18), ColumnCount = 1, RowCount = 9 };
            foreach (float size in new[] { 76f, 108f, 100f, 108f, 46f }) main.RowStyles.Add(new RowStyle(SizeType.Absolute, size));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            Controls.Add(main);

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 242));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            header.Controls.Add(TextLabel("Bilibili 缓存提取", 22, true), 0, 0);
            var sub = TextLabel("把本地缓存保存为 MP4 · 无损合并 / H.264 / H.265 · CPU 与显卡均可使用"); sub.ForeColor = muted; header.Controls.Add(sub, 0, 1);
            header.SetColumnSpan(sub, 2);
            var languages = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 8, 0, 0) };
            languages.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76)); languages.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            languages.Controls.Add(TextLabel("语言"), 0, 0);
            language.DropDownStyle = ComboBoxStyle.DropDownList; language.DisplayMember = "Value"; language.Dock = DockStyle.Fill;
            language.Items.AddRange(new object[] { new KeyValuePair<string, string>("zh-CN", "简体中文"),
                new KeyValuePair<string, string>("zh-TW", "繁體中文"), new KeyValuePair<string, string>("en", "English") }); language.SelectedIndex = 0;
            Accessible(language, "语言"); languages.Controls.Add(language, 1, 0); header.Controls.Add(languages, 1, 0);
            language.SelectedIndexChanged += delegate {
                if (localizing || language.SelectedItem == null) return;
                engine.Text.Language = ((KeyValuePair<string, string>)language.SelectedItem).Key; ApplyLanguage(); SavePreferences();
            };
            lockWhileBusy.Add(language);
            main.Controls.Add(header, 0, 0);

            var hardware = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3,
                BackColor = Color.White, Padding = new Padding(12, 6, 12, 6), Margin = new Padding(0, 0, 0, 6) };
            hardware.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); hardware.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            hardware.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); hardware.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            hardware.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Bind(cpu, "CPU：正在读取硬件信息…"); cpu.Dock = DockStyle.Fill; cpu.TextAlign = ContentAlignment.MiddleLeft; cpu.AutoEllipsis = true;
            Accessible(cpu, "CPU 型号"); hardware.Controls.Add(cpu, 0, 0);
            Bind(cpuSpecs, "核心、线程与频率"); cpuSpecs.Dock = DockStyle.Fill; cpuSpecs.ForeColor = muted; cpuSpecs.TextAlign = ContentAlignment.MiddleLeft;
            Accessible(cpuSpecs, "CPU 规格"); hardware.Controls.Add(cpuSpecs, 0, 1);
            Bind(gpu, "GPU：正在读取型号与显存…"); gpu.Dock = DockStyle.Fill; gpu.Multiline = true; gpu.ReadOnly = true;
            gpu.BorderStyle = BorderStyle.None; gpu.BackColor = Color.White; gpu.ForeColor = muted; gpu.ScrollBars = ScrollBars.Vertical;
            Accessible(gpu, "GPU 型号与显存"); hardware.Controls.Add(gpu, 0, 2);
            var refreshHardware = MakeButton("重新检测"); refreshHardware.Dock = DockStyle.Top;
            refreshHardware.Click += async delegate { await RefreshHardwareAsync(); };
            hardware.Controls.Add(refreshHardware, 1, 0); hardware.SetRowSpan(refreshHardware, 3);
            lockWhileBusy.Add(refreshHardware); main.Controls.Add(hardware, 0, 1);

            var paths = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, BackColor = Color.White, Padding = new Padding(12, 8, 12, 8), Margin = new Padding(0, 3, 0, 7) };
            paths.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); paths.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            paths.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96)); paths.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            paths.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); paths.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            paths.Controls.Add(TextLabel("缓存目录"), 0, 0); paths.Controls.Add(TextLabel("输出目录"), 0, 1);
            source.Dock = destination.Dock = DockStyle.Fill; source.Margin = destination.Margin = new Padding(2, 6, 8, 4);
            Accessible(source, "缓存目录"); Accessible(destination, "输出目录");
            paths.Controls.Add(source, 1, 0); paths.Controls.Add(destination, 1, 1);
            var browseSource = MakeButton("选择…"); browseSource.Dock = DockStyle.Fill;
            browseSource.Click += delegate { ChooseFolder(source, false); }; paths.Controls.Add(browseSource, 2, 0);
            var browseOutput = MakeButton("选择…"); browseOutput.Dock = DockStyle.Fill;
            browseOutput.Click += delegate { ChooseFolder(destination, true); }; paths.Controls.Add(browseOutput, 2, 1);
            scan = MakeButton("扫描缓存"); scan.Dock = DockStyle.Fill; scan.Click += async delegate { await ScanAsync(); }; paths.Controls.Add(scan, 3, 0);
            var openOutput = MakeButton("打开输出"); openOutput.Dock = DockStyle.Fill;
            openOutput.Click += delegate {
                if (Directory.Exists(destination.Text)) Process.Start(new ProcessStartInfo { FileName = Path.GetFullPath(destination.Text), UseShellExecute = true });
                else SetStatus("输出目录将在首次提取时创建。");
            }; paths.Controls.Add(openOutput, 3, 1); main.Controls.Add(paths, 0, 2);
            lockWhileBusy.AddRange(new Control[] { source, destination, browseSource, browseOutput, scan });

            var options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 3, Margin = Padding.Empty };
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85)); options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85)); options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 182));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); options.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            options.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            options.Controls.Add(TextLabel("输出格式"), 0, 0);
            mode.DropDownStyle = ComboBoxStyle.DropDownList; mode.Dock = DockStyle.Fill; mode.Margin = new Padding(0, 4, 12, 0);
            mode.DisplayMember = "Value";
            mode.Items.AddRange(new object[] {
                new KeyValuePair<ExportMode, string>(ExportMode.Copy, "无损提取（推荐，保留原画质）"),
                new KeyValuePair<ExportMode, string>(ExportMode.H264, "转为 H.264（兼容性优先）"),
                new KeyValuePair<ExportMode, string>(ExportMode.H265, "转为 H.265（HEVC）") }); mode.SelectedIndex = 0;
            Accessible(mode, "输出格式"); options.Controls.Add(mode, 1, 0);
            options.Controls.Add(TextLabel("编码设备"), 2, 0);
            device.DropDownStyle = ComboBoxStyle.DropDownList; device.DisplayMember = "Value"; device.Dock = DockStyle.Fill;
            device.Margin = new Padding(0, 4, 12, 0); Accessible(device, "编码设备"); options.Controls.Add(device, 3, 0);
            Bind(decode, "GPU 解码（NVIDIA）"); decode.Checked = true; decode.Enabled = false; decode.Dock = DockStyle.Fill;
            options.Controls.Add(decode, 4, 0);
            hint.Dock = DockStyle.Fill; hint.TextAlign = ContentAlignment.MiddleLeft; hint.ForeColor = muted;
            hint.Text = "直接合并音视频，无需重新编码。适合把缓存快速保存为原画质视频。";
            options.Controls.Add(hint, 0, 1); options.SetColumnSpan(hint, 5);
            explanation.Dock = DockStyle.Fill; explanation.TextAlign = ContentAlignment.MiddleLeft; explanation.ForeColor = muted;
            options.Controls.Add(explanation, 0, 2); options.SetColumnSpan(explanation, 5);
            mode.SelectedIndexChanged += delegate { if (!localizing) { RebuildDeviceChoices(); UpdateModeControls(); } };
            device.SelectedIndexChanged += delegate {
                if (changingDevices || device.SelectedItem == null) return;
                requestedDevice = ((KeyValuePair<EncodingDevice, string>)device.SelectedItem).Key; UpdateModeControls();
            };
            lockWhileBusy.AddRange(new Control[] { mode, device, decode }); main.Controls.Add(options, 0, 3);

            var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Margin = new Padding(0, 5, 0, 7) };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            var all = MakeButton("全选"); all.Dock = DockStyle.Fill; all.Click += delegate { SelectVisible(true); };
            var none = MakeButton("全不选"); none.Dock = DockStyle.Fill; none.Click += delegate { SelectVisible(false); };
            var one = MakeButton("仅选当前行"); one.Dock = DockStyle.Fill;
            one.Click += delegate {
                var selected = grid.CurrentRow == null ? null : grid.CurrentRow.Tag as CacheEntry;
                foreach (var entry in entries) entry.Selected = entry == selected;
                RefreshChecks();
            };
            toolbar.Controls.Add(all, 0, 0); toolbar.Controls.Add(none, 1, 0); toolbar.Controls.Add(one, 2, 0);
            count.Dock = DockStyle.Fill; count.TextAlign = ContentAlignment.MiddleLeft; count.Padding = new Padding(12, 0, 0, 0); count.ForeColor = muted; count.AutoEllipsis = true;
            toolbar.Controls.Add(count, 3, 0); toolbar.Controls.Add(TextLabel("搜索"), 4, 0);
            search.Dock = DockStyle.Fill; search.Margin = new Padding(0, 4, 0, 0); Accessible(search, "搜索视频");
            search.TextChanged += delegate { PopulateGrid(); }; toolbar.Controls.Add(search, 5, 0);
            lockWhileBusy.AddRange(new Control[] { all, none, one, search }); main.Controls.Add(toolbar, 0, 4);

            grid.Dock = DockStyle.Fill; grid.Margin = Padding.Empty; grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false; grid.RowHeadersVisible = false; grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.AutoGenerateColumns = false;
            grid.EnableHeadersVisualStyles = false; grid.ColumnHeadersHeight = 40; grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(230, 237, 247); grid.ColumnHeadersDefaultCellStyle.ForeColor = ink;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(222, 234, 253); grid.DefaultCellStyle.SelectionForeColor = ink;
            grid.DefaultCellStyle.Padding = new Padding(5, 0, 5, 0); grid.RowTemplate.Height = 42;
            grid.GridColor = Color.FromArgb(235, 240, 246); grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 251, 254);
            grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "选择", Width = 60, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "视频标题", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 200, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "分辨率", Width = 120, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "时长", Width = 90, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "缓存大小", Width = 100, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "状态", Width = 130, ReadOnly = true });
            foreach (DataGridViewColumn col in grid.Columns) col.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.CurrentCellDirtyStateChanged += delegate { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs e) {
                if (changingGrid || e.RowIndex < 0 || e.ColumnIndex != 0) return;
                var entry = grid.Rows[e.RowIndex].Tag as CacheEntry;
                if (entry != null) entry.Selected = Convert.ToBoolean(grid.Rows[e.RowIndex].Cells[0].Value);
                UpdateCount();
            };
            grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) {
                if (e.RowIndex < 0 || busy || e.ColumnIndex == 0) return;
                var entry = grid.Rows[e.RowIndex].Tag as CacheEntry;
                if (entry != null) { entry.Selected = !entry.Selected; RefreshChecks(); }
            };
            Accessible(grid, "缓存视频列表"); main.Controls.Add(grid, 0, 5);

            var progressPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0, 4, 0, 2) };
            progressPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); progressPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            progressPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
            status.Text = "准备扫描缓存…"; status.Dock = DockStyle.Fill; status.AutoEllipsis = true; status.TextAlign = ContentAlignment.MiddleLeft;
            progress.Dock = DockStyle.Fill; progress.Maximum = 1000; progress.Margin = Padding.Empty;
            activity.Text = "实际设备与处理速度将在任务开始后显示。"; activity.Dock = DockStyle.Fill; activity.AutoEllipsis = true; activity.ForeColor = muted;
            progressPanel.Controls.Add(status, 0, 0); progressPanel.Controls.Add(activity, 0, 1); progressPanel.Controls.Add(progress, 0, 2); main.Controls.Add(progressPanel, 0, 6);

            logs.Multiline = true; logs.ReadOnly = true; logs.ScrollBars = ScrollBars.Vertical; logs.Dock = DockStyle.Fill;
            logs.BackColor = Color.FromArgb(235, 240, 247); logs.ForeColor = muted; logs.BorderStyle = BorderStyle.None;
            logs.Font = new Font("Microsoft YaHei UI", 8.5f); Accessible(logs, "运行日志"); main.Controls.Add(logs, 0, 7);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = new Padding(0, 12, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            var note = TextLabel("批量任务按顺序处理 · 同名文件自动编号"); note.ForeColor = muted; note.AutoEllipsis = true; footer.Controls.Add(note, 0, 0);
            cancel = MakeButton("取消任务"); cancel.Enabled = false; cancel.Dock = DockStyle.Fill;
            cancel.Click += delegate { if (cancellation != null) { cancellation.Cancel(); cancel.Enabled = false; SetStatus("正在取消当前任务…"); } };
            delete = MakeButton("删除缓存"); delete.Dock = DockStyle.Fill; delete.BackColor = Color.FromArgb(209, 53, 64); delete.ForeColor = Color.White;
            delete.FlatAppearance.BorderColor = delete.BackColor; delete.FlatAppearance.MouseOverBackColor = Color.FromArgb(192, 39, 50);
            delete.FlatAppearance.MouseDownBackColor = Color.FromArgb(173, 28, 39); delete.Click += async delegate { await DeleteCacheAsync(); };
            start = MakeButton("提取所选视频", true); start.Dock = DockStyle.Fill; start.Click += async delegate { await ExportAsync(); };
            footer.Controls.Add(cancel, 1, 0); footer.Controls.Add(delete, 2, 0); footer.Controls.Add(start, 3, 0); main.Controls.Add(footer, 0, 8);
        }

        void ChooseFolder(TextBox box, bool create)
        {
            using (var dialog = new FolderBrowserDialog { Description = T(create ? "选择 MP4 输出目录" : "选择 Bilibili 缓存目录"), ShowNewFolderButton = create })
            {
                if (Directory.Exists(box.Text)) dialog.SelectedPath = box.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.SelectedPath;
            }
        }

        void LoadPreferences()
        {
            source.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "bilibili");
            destination.Text = Path.Combine(engine.Root, "提取的视频");
            try
            {
                string file = Path.Combine(engine.Root, "settings.json");
                if (!File.Exists(file)) return;
                var saved = new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(file, Encoding.UTF8));
                if (saved != null && !string.IsNullOrWhiteSpace(saved.Source)) source.Text = saved.Source;
                if (saved != null && !string.IsNullOrWhiteSpace(saved.Destination)) destination.Text = saved.Destination;
                if (saved != null)
                {
                    engine.Text.Language = Localizer.Normalize(saved.Language);
                    ExportMode savedMode = ExportModes.Parse(saved.Mode ?? "copy");
                    requestedDevice = EncodingDevices.Parse(saved.Device);
                    for (int i = 0; i < mode.Items.Count; i++)
                        if (((KeyValuePair<ExportMode, string>)mode.Items[i]).Key == savedMode) mode.SelectedIndex = i;
                    decode.Checked = saved.GpuDecode;
                }
            }
            catch (Exception ex) { Log(T("读取设置失败：{0}", ex.Message)); }
        }

        void SavePreferences()
        {
            try { File.WriteAllText(Path.Combine(engine.Root, "settings.json"), new JavaScriptSerializer().Serialize(new Preferences {
                Source = source.Text, Destination = destination.Text, Mode = ExportModes.Key(SelectedMode), Device = EncodingDevices.Key(requestedDevice), Language = engine.Text.Language, GpuDecode = decode.Checked }), Encoding.UTF8); }
            catch (Exception ex) { Log(T("设置未保存：{0}", ex.Message)); }
        }

        void ApplyLanguage()
        {
            var selected = mode.SelectedItem == null ? ExportMode.Copy : SelectedMode;
            localizing = true;
            try
            {
                Text = T("Bilibili 缓存提取器");
                foreach (var binding in labels) binding.Key.Text = T(binding.Value);
                foreach (var binding in accessibleNames) binding.Key.AccessibleName = T(binding.Value);
                string[] columns = { "选择", "视频标题", "分辨率", "时长", "缓存大小", "状态" };
                for (int i = 0; i < columns.Length; i++) grid.Columns[i].HeaderText = T(columns[i]);
                mode.Items.Clear();
                mode.Items.Add(new KeyValuePair<ExportMode, string>(ExportMode.Copy, T("无损提取（推荐，保留原画质）")));
                mode.Items.Add(new KeyValuePair<ExportMode, string>(ExportMode.H264, T("转为 H.264（兼容性优先）")));
                mode.Items.Add(new KeyValuePair<ExportMode, string>(ExportMode.H265, T("转为 H.265（HEVC）")));
                mode.SelectedIndex = (int)selected;
                for (int i = 0; i < language.Items.Count; i++)
                    if (((KeyValuePair<string, string>)language.Items[i]).Key == engine.Text.Language) language.SelectedIndex = i;
            }
            finally { localizing = false; }
            RebuildDeviceChoices(); UpdateHardwareLabels(); UpdateModeControls(); PopulateGrid();
            status.Text = T(statusKey, statusArgs);
            activity.Text = T("实际设备与处理速度将在任务开始后显示。");
        }

        void UI(Action action)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) { if (IsHandleCreated) BeginInvoke(action); }
            else action();
        }

        void Log(string message)
        {
            UI(delegate {
                if (logs.TextLength > 100000) logs.Text = logs.Text.Substring(logs.TextLength - 50000);
                logs.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
            });
        }

        void SetBusy(bool value)
        {
            busy = value;
            foreach (var control in lockWhileBusy) control.Enabled = !value;
            grid.ReadOnly = value; cancel.Enabled = value; UpdateModeControls();
            if (!value && closePending) BeginInvoke(new Action(Close));
        }

        void UpdateModeControls()
        {
            ExportMode selected = SelectedMode;
            device.Enabled = !busy && selected != ExportMode.Copy;
            decode.Enabled = !busy && engine.CanUseCuda(selected, requestedDevice);
            UpdateActionButtons();
            hint.Text = engine.Describe(selected, requestedDevice);
            explanation.Text = T(selected == ExportMode.Copy ? "直接合并音视频，通常最快；H.264 / H.265 等源编码保持原样。" :
                selected == ExportMode.H265 ? "H.265 压缩效率较高，播放设备需支持 HEVC；CPU 编码较耗时，音频保持原样。" :
                "H.264 适合需要广泛播放兼容性的设备，音频保持原样。");
        }

        void UpdateActionButtons()
        {
            start.Enabled = !busy && entries.Any(e => e.Selected && e.Ready) && engine.CanExport(SelectedMode, requestedDevice);
            start.BackColor = start.Enabled ? blue : Color.FromArgb(221, 226, 234);
            start.ForeColor = start.Enabled ? Color.White : muted;
            start.FlatAppearance.BorderColor = start.BackColor;
            start.FlatAppearance.MouseOverBackColor = start.Enabled ? Color.FromArgb(27, 91, 203) : start.BackColor;
            start.FlatAppearance.MouseDownBackColor = start.Enabled ? Color.FromArgb(23, 77, 173) : start.BackColor;
            delete.Enabled = !busy;
        }

        void RebuildDeviceChoices()
        {
            changingDevices = true; device.Items.Clear();
            foreach (EncodingDevice choice in Enum.GetValues(typeof(EncodingDevice)))
            {
                bool available = SelectedMode == ExportMode.Copy ? engine.CanExport(ExportMode.H264, choice) || engine.CanExport(ExportMode.H265, choice) : engine.CanExport(SelectedMode, choice);
                if (choice != EncodingDevice.Auto && choice != EncodingDevice.Cpu && choice != requestedDevice && !available) continue;
                string label = T(EncodingDevices.Label(choice)) + (engine.EncodersDetected && !available ? T("（不可用）") : "");
                int index = device.Items.Add(new KeyValuePair<EncodingDevice, string>(choice, label));
                if (choice == requestedDevice) device.SelectedIndex = index;
            }
            changingDevices = false;
        }

        void UpdateHardwareLabels()
        {
            if (engine.Hardware == null) return;
            cpu.Text = T("CPU：{0}", engine.Hardware.CpuName == "CPU 型号未读取" ? T("CPU 型号未读取") : engine.Hardware.CpuName); cpuSpecs.Text = engine.Hardware.CpuSummaryText(engine.Text);
            gpu.Text = engine.Hardware.Gpus.Count == 0 ? T("GPU：未读取到显卡信息，编码能力以实际检测为准。") :
                string.Join(Environment.NewLine, engine.Hardware.Gpus.Select((g, i) => T("GPU {0}：{1} · {2}", i + 1, g.Name, g.MemorySummaryText(engine.Text))));
        }

        async Task<bool> RefreshHardwareAsync()
        {
            if (busy) return false;
            cancellation = new CancellationTokenSource(); SetBusy(true); progress.Style = ProgressBarStyle.Marquee;
            SetStatus("正在读取 CPU、GPU 并测试编码能力…");
            try
            {
                var token = cancellation.Token;
                await Task.Run(delegate { engine.ReadHardware(token, Log); }); UpdateHardwareLabels();
                await Task.Run(delegate { engine.DetectEncoders(token, Log); });
                SetStatus("硬件检测完成，可选择输出格式和编码设备。"); return true;
            }
            catch (OperationCanceledException) { SetStatus("已取消硬件检测。"); return false; }
            catch (Exception ex) { SetStatus("硬件检测失败：{0}", ex.Message); Log(ex.ToString()); return false; }
            finally
            {
                RebuildDeviceChoices(); progress.Style = ProgressBarStyle.Continuous; progress.Value = 0;
                cancellation.Dispose(); cancellation = null; SetBusy(false);
            }
        }

        async Task ScanAsync()
        {
            if (busy) return;
            string path = source.Text.Trim(); scannedSource = "";
            cancellation = new CancellationTokenSource(); SetBusy(true); progress.Style = ProgressBarStyle.Marquee;
            SetStatus("正在识别缓存和音视频轨道…");
            entries.Clear(); PopulateGrid();
            try
            {
                SavePreferences();
                var token = cancellation.Token;
                entries = await Task.Run(delegate { return engine.Scan(path, token, Log); });
                scannedSource = Path.GetFullPath(path); PopulateGrid();
                if (entries.Count == 0) SetStatus("未发现 .m4s 缓存。请选择包含下载缓存的目录，再扫描。");
                else SetStatus("扫描完成：{0} 个视频，{1} 个可提取。请勾选要处理的条目。", entries.Count, entries.Count(e => e.Ready));
            }
            catch (OperationCanceledException) { SetStatus("已取消扫描。"); }
            catch (Exception ex) { SetStatus("扫描失败：{0}", ex.Message); Log(ex.ToString()); }
            finally { progress.Style = ProgressBarStyle.Continuous; progress.Value = 0; cancellation.Dispose(); cancellation = null; SetBusy(false); }
        }

        void PopulateGrid()
        {
            var current = grid.CurrentRow == null ? null : grid.CurrentRow.Tag as CacheEntry;
            changingGrid = true; grid.Rows.Clear(); rows.Clear();
            string term = search.Text.Trim();
            foreach (var entry in entries.Where(e => term.Length == 0 || e.Title.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0 || e.Id.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                string duration = entry.Duration <= 0 ? "—" : TimeSpan.FromSeconds(entry.Duration).ToString(entry.Duration >= 3600 ? @"h\:mm\:ss" : @"mm\:ss");
                var row = grid.Rows[grid.Rows.Add(entry.Selected, entry.Title, entry.Resolution, duration, (entry.Bytes / 1048576.0).ToString("0.0") + " MB", T(entry.Status ?? "", entry.StatusValue))];
                row.Tag = entry; rows[entry] = row;
                foreach (DataGridViewCell cell in row.Cells) cell.ToolTipText = entry.Detail + (entry.MultipleTracks ? Environment.NewLine + T("存在多条轨道，已选择最高分辨率视频和最大音频轨。") : "");
                row.Cells[5].ToolTipText = T(entry.Status ?? "", entry.StatusValue) + Environment.NewLine + entry.Detail;
                if (!entry.Ready) row.DefaultCellStyle.ForeColor = muted;
            }
            DataGridViewRow previous; if (current != null && rows.TryGetValue(current, out previous)) grid.CurrentCell = previous.Cells[1];
            changingGrid = false; UpdateCount();
        }

        void UpdateCount()
        {
            int selected = entries.Count(e => e.Selected), ready = entries.Count(e => e.Selected && e.Ready);
            int hidden = entries.Count(e => e.Selected && !rows.ContainsKey(e));
            count.Text = T("共 {0} 项 · 已选 {1}", entries.Count, selected) + (hidden > 0 ? T("（隐藏 {0}）", hidden) : "");
            start.Text = ready == 0 ? T("提取所选视频") : T("提取所选（{0}）", ready);
            UpdateActionButtons();
        }

        void RefreshChecks()
        {
            changingGrid = true;
            foreach (var pair in rows) pair.Value.Cells[0].Value = pair.Key.Selected;
            changingGrid = false; UpdateCount();
        }

        void SelectVisible(bool value)
        {
            // Clearing selection always clears hidden rows as well.
            foreach (var entry in entries) if (!value || rows.ContainsKey(entry)) entry.Selected = value;
            RefreshChecks();
        }

        void RowStatus(CacheEntry entry, string key, int value = 0)
        {
            UI(delegate {
                entry.Status = key; entry.StatusValue = value; DataGridViewRow row;
                if (rows.TryGetValue(entry, out row)) { row.Cells[5].Value = T(key, value); row.Cells[5].ToolTipText = T(key, value) + Environment.NewLine + entry.Detail; }
            });
        }

        async Task DeleteCacheAsync()
        {
            if (busy) return;
            grid.EndEdit(); var selected = entries.Where(e => e.Selected).ToList();
            if (selected.Count == 0) { SetStatus("请先勾选要删除的缓存条目。"); return; }
            CacheDeletionPlan plan;
            try
            {
                if (!string.Equals(Path.GetFullPath(source.Text.Trim()), scannedSource, StringComparison.OrdinalIgnoreCase))
                { SetStatus("缓存目录已改变，请先点击“扫描缓存”。"); return; }
                plan = CacheDeletionPlan.Create(scannedSource, selected, engine.Text);
            }
            catch (Exception ex) { SetStatus("删除缓存失败：{0}", ex.Message); return; }
            using (var confirm = new DeleteConfirmationDialog(plan, selected.Count(e => !rows.ContainsKey(e)), engine.Text))
                if (confirm.ShowDialog(this) != DialogResult.OK) { SetStatus("缓存删除已取消。"); return; }
            cancellation = new CancellationTokenSource(); SetBusy(true); progress.Style = ProgressBarStyle.Marquee;
            SetStatus("正在将缓存移入回收站…");
            try
            {
                var result = await CacheRecycle.RunAsync(plan, Handle, cancellation.Token, engine.Text);
                foreach (string path in result.Recycled) Log(T("已移入回收站：{0}", path));
                foreach (var error in result.Failed) Log(error.Key + " : " + error.Value);
                if (result.Cancelled) SetStatus("缓存删除已取消。");
                else SetStatus("回收站处理结束：成功 {0} 个文件，失败 {1} 个。", result.Recycled.Count, result.Failed.Count);
                Log(status.Text);
            }
            catch (OperationCanceledException) { SetStatus("缓存删除已取消。"); }
            catch (Exception ex) { SetStatus("删除缓存失败：{0}", ex.Message); Log(status.Text); }
            finally
            {
                foreach (var entry in selected)
                {
                    if (entry.CacheFiles.All(p => !File.Exists(p))) entries.Remove(entry);
                    else if (entry.CacheFiles.Any(p => !File.Exists(p))) { entry.Ready = false; entry.Selected = false; entry.Status = "缓存已改变，请重新扫描"; }
                }
                PopulateGrid(); progress.Style = ProgressBarStyle.Continuous; progress.Value = 0;
                cancellation.Dispose(); cancellation = null; SetBusy(false);
            }
        }

        async Task ExportAsync()
        {
            if (busy) return;
            grid.EndEdit();
            var selected = entries.Where(e => e.Ready && e.Selected).ToList();
            if (selected.Count == 0) { SetStatus("请先勾选要提取的视频。"); return; }
            string output; ExportMode selectedMode = SelectedMode; EncodingDevice selectedDevice = requestedDevice; bool useDecode = decode.Checked;
            try
            {
                if (!string.Equals(Path.GetFullPath(source.Text.Trim()), scannedSource, StringComparison.OrdinalIgnoreCase))
                { SetStatus("缓存目录已改变，请先点击“扫描缓存”。"); return; }
                if (string.IsNullOrWhiteSpace(destination.Text)) throw new ArgumentException(T("请选择输出目录。"));
                output = Path.GetFullPath(destination.Text.Trim());
            }
            catch (Exception ex) { SetStatus("{0}", ex.Message); return; }
            if (!engine.CanExport(selectedMode, selectedDevice)) { SetStatus("{0}", engine.UnavailableMessage(selectedMode, selectedDevice)); return; }
            cancellation = new CancellationTokenSource(); SetBusy(true); progress.Value = 0; SavePreferences();
            int completed = 0, failed = 0; bool stopped = false;
            var watch = Stopwatch.StartNew();
            try
            {
                var token = cancellation.Token;
                for (int i = 0; i < selected.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    CacheEntry entry = selected[i]; int index = i;
                    SetStatus("正在处理 {0} / {1}：{2}", index + 1, selected.Count, entry.Title);
                    RowStatus(entry, "处理中…");
                    try
                    {
                        string result = await Task.Run(delegate {
                            return engine.Export(entry, output, selectedMode, selectedDevice, useDecode, token, p => UI(delegate {
                                progress.Value = Math.Min(1000, (int)((index + p) / selected.Count * 1000));
                                RowStatus(entry, "处理中 {0}%", (int)(p * 100));
                            }), Log, 0, text => UI(delegate { activity.Text = text; }));
                        });
                        entry.Output = result; completed++; RowStatus(entry, "已完成"); Log(T("已保存：{0}", result));
                    }
                    catch (OperationCanceledException) { RowStatus(entry, "已取消"); throw; }
                    catch (Exception ex) { failed++; RowStatus(entry, "失败（见日志）"); Log(entry.Title + "：" + ex.Message); }
                    progress.Value = (int)((index + 1.0) / selected.Count * 1000);
                }
            }
            catch (OperationCanceledException) { stopped = true; Log(T("任务已取消，已完成的视频保留。")); }
            finally
            {
                watch.Stop();
                SetStatus(stopped ? "已取消 · 成功 {0} · 失败 {1} · 用时 {2} 秒" : "任务结束 · 成功 {0} · 失败 {1} · 用时 {2} 秒", completed, failed, watch.Elapsed.TotalSeconds.ToString("0.0"));
                Log(status.Text); cancellation.Dispose(); cancellation = null; SetBusy(false);
            }
        }
    }
}
