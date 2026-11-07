// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BiliExtractor
{
    public sealed class MediaToolsDialog : Form
    {
        readonly string root;
        readonly Localizer text;
        readonly MediaToolInstallation current;
        readonly DataGridView list = new DataGridView();
        readonly TextBox details = new TextBox();
        readonly Label status = new Label(), recommended = new Label(), hardwareLabel = new Label();
        readonly ProgressBar progress = new ProgressBar();
        readonly Button use = new Button(), cancel = new Button();
        readonly List<Control> locked = new List<Control>();
        readonly List<MediaToolInspection> candidates = new List<MediaToolInspection>();
        MediaToolDownload download;
        HardwareInfo hardware;
        CancellationTokenSource cancellation;
        bool busy, closing, populating;
        public MediaToolInstallation SelectedInstallation { get; private set; }
        string T(string key, params object[] args) { return text.Get(key, args); }
        string ErrorText(string message) { var lines = message.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None); lines[0] = T(lines[0]); return string.Join(Environment.NewLine, lines); }

        public MediaToolsDialog(string root, MediaToolInstallation current, Localizer text, string problem = null)
        {
            this.root = root; this.current = current; this.text = text;
            Text = T("FFmpeg 媒体工具"); Font = new Font("Microsoft YaHei UI", 9.5f);
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(940, 650); MinimumSize = new Size(800, 570);
            StartPosition = FormStartPosition.CenterParent; BackColor = Color.FromArgb(244, 247, 251);
            MinimizeBox = false;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(18) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            Controls.Add(layout);
            var intro = new Label { Dock = DockStyle.Fill, Text = T("选择随程序提供的版本，或使用本地、下载的 FFmpeg。选择后会检查 FFprobe 和编码能力。"), TextAlign = ContentAlignment.MiddleLeft };
            layout.Controls.Add(intro, 0, 0);
            hardwareLabel.Dock = DockStyle.Fill; hardwareLabel.Text = T("正在读取显卡与驱动…"); layout.Controls.Add(hardwareLabel, 0, 1);
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            var builtin = Button(T("使用内置版本"));
            var local = Button(T("选择本地版本…"));
            var scan = Button(T("扫描本地 PATH"));
            var fetch = Button(T("下载推荐版本"));
            builtin.Click += async delegate { var pair = MediaTools.BuiltIn(root); if (pair == null) { status.Text = T("未找到内置版本，可选择本地版本或下载。"); return; } SelectOrAdd(pair); await InspectSelectedAsync(); };
            local.Click += async delegate { await ChooseLocalAsync(); };
            scan.Click += async delegate { await DiscoverAsync(); };
            fetch.Click += async delegate { await DownloadAsync(); };
            toolbar.Controls.AddRange(new Control[] { builtin, local, scan, fetch }); layout.Controls.Add(toolbar, 0, 2);
            locked.AddRange(new Control[] { builtin, local, scan, fetch, list });
            list.Dock = DockStyle.Fill; list.ReadOnly = true; list.AllowUserToAddRows = false; list.AllowUserToDeleteRows = false;
            list.AllowUserToResizeRows = false; list.MultiSelect = false; list.RowHeadersVisible = false;
            list.SelectionMode = DataGridViewSelectionMode.FullRowSelect; list.AutoGenerateColumns = false;
            list.BackgroundColor = Color.White; list.BorderStyle = BorderStyle.FixedSingle;
            list.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("来源"), Width = 95 });
            list.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("版本"), Width = 190 });
            list.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("检测结果"), Width = 100 });
            list.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("工具目录"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            list.SelectionChanged += delegate { if (!populating) UpdateDetails(); };
            layout.Controls.Add(list, 0, 3);
            details.Dock = DockStyle.Fill; details.Multiline = true; details.ReadOnly = true; details.WordWrap = false;
            details.ScrollBars = ScrollBars.Both; details.BackColor = Color.White;
            layout.Controls.Add(details, 0, 4);
            recommended.Dock = DockStyle.Fill; recommended.TextAlign = ContentAlignment.MiddleLeft; layout.Controls.Add(recommended, 0, 5);
            progress.Dock = DockStyle.Fill; layout.Controls.Add(progress, 0, 6);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int width in new[] { 140, 140, 95 }) footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
            status.Dock = DockStyle.Fill; status.AutoEllipsis = true; status.TextAlign = ContentAlignment.MiddleLeft;
            status.Text = problem == null ? T("请选择工具版本。") : ErrorText(problem); footer.Controls.Add(status, 0, 0);
            var inspect = Button(T("检测所选版本")); inspect.Dock = DockStyle.Fill; inspect.Click += async delegate { await InspectSelectedAsync(); };
            locked.Add(inspect); footer.Controls.Add(inspect, 1, 0);
            use.Text = T("使用所选版本"); use.Dock = DockStyle.Fill; use.Enabled = false;
            use.Click += delegate {
                var item = Selected();
                if (item == null || !item.Valid) return;
                try {
                    if (MediaTools.FindPair(item.Installation.Directory, item.Installation.Kind) == null) throw new InvalidOperationException(T("上次选择的 FFmpeg 不可用，请重新选择。"));
                    MediaTools.Save(root, item.Installation); SelectedInstallation = item.Installation;
                    DialogResult = DialogResult.OK; Close();
                } catch (Exception ex) { status.Text = T("设置未保存：{0}", ex.Message); }
            };
            footer.Controls.Add(use, 2, 0);
            cancel.Text = T("关闭"); cancel.Dock = DockStyle.Fill;
            cancel.Click += delegate { if (busy) { closing = true; cancellation.Cancel(); status.Text = T("正在取消…"); } else { DialogResult = DialogResult.Cancel; Close(); } };
            footer.Controls.Add(cancel, 3, 0); layout.Controls.Add(footer, 0, 7);
            CancelButton = cancel;
            Shown += async delegate { await DiscoverAsync(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { e.Cancel = true; closing = true; cancellation.Cancel(); } };
            try { download = MediaToolDownload.Load(root); }
            catch (Exception ex) { recommended.Text = T("推荐下载配置无效。") + " " + ex.Message; }
            fetch.Enabled = download != null;
            if (download != null) recommended.Text = T("推荐：FFmpeg {0} · 下载 {1} MiB · {2} · NVIDIA 驱动最低 {3}",
                download.version, (download.bytes / 1048576.0).ToString("0.0"), download.license, download.minimumNvidiaDriver);
            else if (recommended.Text.Length == 0) recommended.Text = T("此源码副本尚未配置推荐下载。");
            fetch.Tag = download;
        }
        Button Button(string title) { return new Button { Text = title, AutoSize = true, Height = 34, MinimumSize = new Size(115, 32), Margin = new Padding(0, 0, 8, 0) }; }
        MediaToolInspection Selected() { return list.SelectedRows.Count == 0 ? null : list.SelectedRows[0].Tag as MediaToolInspection; }
        string Origin(string kind) { return T(kind == "builtin" ? "内置版本" : kind == "downloaded" ? "已下载" : "本地版本"); }
        void Populate(MediaToolInstallation select)
        {
            populating = true;
            try {
                list.Rows.Clear();
                foreach (var item in candidates) {
                    int row = list.Rows.Add(Origin(item.Installation.Kind), item.Version ?? T("尚未检测"),
                        T(item.Valid ? "可用" : item.Error == null ? "尚未检测" : "不可用"), item.Installation.Directory);
                    list.Rows[row].Tag = item;
                }
                list.ClearSelection();
                if (select != null) foreach (DataGridViewRow row in list.Rows)
                    if (((MediaToolInspection)row.Tag).Installation.Directory.Equals(select.Directory, StringComparison.OrdinalIgnoreCase)) {
                        list.CurrentCell = row.Cells[0]; row.Selected = true; break;
                    }
            } finally { populating = false; }
            UpdateDetails();
        }
        void SelectOrAdd(MediaToolInstallation pair)
        {
            if (!candidates.Any(c => c.Installation.Directory.Equals(pair.Directory, StringComparison.OrdinalIgnoreCase)))
                candidates.Add(new MediaToolInspection { Installation = pair });
            Populate(pair);
        }
        string Yes(bool available) { return T(available ? "可用" : "不可用"); }
        void UpdateDetails()
        {
            var item = Selected(); use.Enabled = !busy && item != null && item.Valid;
            if (item == null) { details.Clear(); return; }
            var lines = new List<string> { "FFmpeg: " + item.Installation.FFmpeg, "FFprobe: " + item.Installation.FFprobe };
            if (item.Error != null) {
                string[] error = item.Error.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                lines.Add(T(error[0])); lines.AddRange(error.Skip(1));
            } else if (item.Version == null) lines.Add(T("点击“检测所选版本”检查可用功能。"));
            else {
                lines.Add(T("扫描与无损提取：{0}", Yes(item.Valid)));
                lines.Add(T("CPU 转码：H.264 {0} · H.265 {1}", Yes(item.CpuH264), Yes(item.CpuH265)));
                lines.Add(T("NVIDIA 转码：H.264 {0} · H.265 {1}", Yes(item.NvidiaH264), Yes(item.NvidiaH265)));
                if (hardware != null && hardware.Gpus.Any(g => g.VendorId == 0x8086)) lines.Add(T("Intel 转码：H.264 {0} · H.265 {1}", Yes(item.IntelH264), Yes(item.IntelH265)));
                if (hardware != null && hardware.Gpus.Any(g => g.VendorId == 0x1002)) lines.Add(T("AMD 转码：H.264 {0} · H.265 {1}", Yes(item.AmdH264), Yes(item.AmdH265)));
                lines.Add(""); lines.AddRange(item.Details);
            }
            details.Text = string.Join(Environment.NewLine, lines);
        }
        async Task RunAsync(string message, Func<CancellationToken, Task> action)
        {
            if (busy) return;
            busy = true; cancellation = new CancellationTokenSource();
            foreach (var control in locked) control.Enabled = false;
            use.Enabled = false; cancel.Text = T("取消"); progress.Style = ProgressBarStyle.Marquee; status.Text = T(message);
            try { await action(cancellation.Token); }
            catch (OperationCanceledException) { status.Text = T("操作已取消。"); }
            catch (Exception ex) { status.Text = T("操作失败：{0}", ErrorText(ex.Message)); details.AppendText(Environment.NewLine + ErrorText(ex.Message)); }
            finally {
                cancellation.Dispose(); cancellation = null; busy = false;
                foreach (var control in locked) control.Enabled = !(control is Button && ((Button)control).Text == T("下载推荐版本") && download == null);
                cancel.Text = T("关闭"); progress.Style = ProgressBarStyle.Continuous; progress.Value = 0; UpdateDetails();
                if (closing) { DialogResult = DialogResult.Cancel; Close(); }
            }
        }
        async Task DiscoverAsync()
        {
            await RunAsync("正在查找本地 FFmpeg…", async token => {
                var found = await Task.Run(() => MediaTools.Discover(root), token);
                if (current != null && !found.Any(p => p.Directory.Equals(current.Directory, StringComparison.OrdinalIgnoreCase))) found.Add(current);
                foreach (var pair in found) if (!candidates.Any(c => c.Installation.Directory.Equals(pair.Directory, StringComparison.OrdinalIgnoreCase))) candidates.Add(new MediaToolInspection { Installation = pair });
                if (hardware == null) hardware = await Task.Run(() => HardwareInfo.Read(token, s => { }, text), token);
                hardwareLabel.Text = hardware.Gpus.Count == 0 ? T("未检测到显卡，将检查 CPU 编码能力。") : string.Join("\r\n", hardware.Gpus.Select(g => g.Name + (string.IsNullOrEmpty(g.DriverVersion) ? "" : " · " + T("驱动 {0}", g.DriverVersion))));
                if (download != null) {
                    recommended.Text = T("推荐：FFmpeg {0} · 下载 {1} MiB · {2} · NVIDIA 驱动最低 {3}", download.version, (download.bytes / 1048576.0).ToString("0.0"), download.license, download.minimumNvidiaDriver);
                    Version minimum;
                    if (Version.TryParse(download.minimumNvidiaDriver, out minimum)) foreach (var gpu in hardware.Gpus.Where(g => g.VendorId == 0x10de)) {
                        Version driver;
                        if (Version.TryParse(gpu.DriverVersion, out driver)) recommended.Text += "\r\n" + T(driver >= minimum ? "当前 NVIDIA 驱动版本满足推荐版本要求。" : "当前 NVIDIA 驱动低于要求，仍可使用 CPU 转码和无损提取。");
                    }
                }
                Populate(current ?? (candidates.Count == 0 ? null : candidates[0].Installation));
                status.Text = T("找到 {0} 个工具目录。", candidates.Count);
            });
        }
        async Task InspectSelectedAsync()
        {
            var selected = Selected(); if (selected == null) { status.Text = T("请选择工具版本。"); return; }
            await RunAsync("正在检测版本与编码能力…", async token => {
                var inspection = await Task.Run(() => MediaToolInspector.Inspect(selected.Installation, token, hardware), token);
                candidates[candidates.IndexOf(selected)] = inspection; Populate(inspection.Installation);
                status.Text = T(inspection.Valid ? "检测完成，可使用所选版本。" : "此版本不可用，详情见下方。");
            });
        }
        async Task ChooseLocalAsync()
        {
            if (busy) return;
            using (var dialog = new FolderBrowserDialog { Description = T("选择包含 FFmpeg 和 FFprobe 的文件夹，或它们的上级目录。"), ShowNewFolderButton = false }) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var pair = MediaTools.FindPair(dialog.SelectedPath, "local");
                if (pair == null) { status.Text = T("所选目录缺少 FFmpeg 或 FFprobe。"); return; }
                SelectOrAdd(pair);
            }
            await InspectSelectedAsync();
        }
        async Task DownloadAsync()
        {
            if (download == null) return;
            await RunAsync("正在下载推荐版本…", async token => {
                var selected = await MediaToolInstaller.DownloadAsync(root, download, token,
                    (done, total) => Post(() => { progress.Style = ProgressBarStyle.Continuous; progress.Value = (int)Math.Min(100, done * 100 / total); status.Text = T("已下载 {0} / {1} MiB", (done / 1048576.0).ToString("0.0"), (total / 1048576.0).ToString("0.0")); }),
                    message => Post(() => { status.Text = T(message); progress.Style = ProgressBarStyle.Marquee; }));
                var inspection = await Task.Run(() => MediaToolInspector.Inspect(selected, token, hardware), token);
                candidates.Add(inspection); Populate(selected);
                status.Text = T(inspection.Valid ? "下载和检测完成，点击“使用所选版本”。" : "下载完成，但此版本未通过检测。");
            });
        }
        void Post(Action action) { if (!IsDisposed && IsHandleCreated) BeginInvoke(action); }
    }
}
