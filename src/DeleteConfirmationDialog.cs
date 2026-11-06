using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace BiliExtractor
{
    public sealed class DeleteConfirmationDialog : Form
    {
        public DeleteConfirmationDialog(CacheDeletionPlan plan, int hidden, Localizer text)
        {
            Text = text.Get("确认删除缓存"); Font = new Font("Microsoft YaHei UI", 9.5f);
            ClientSize = new Size(750, 490); MinimumSize = new Size(650, 400);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); Controls.Add(layout);
            string message = text.Get("将 {0} 个勾选条目的 {1} 个缓存文件（{2}）移入回收站。", plan.EntryCount, plan.Files.Count, (plan.Bytes / 1048576.0).ToString("0.0") + " MiB");
            if (hidden > 0) message += Environment.NewLine + text.Get("其中 {0} 项被当前搜索条件隐藏。", hidden);
            message += Environment.NewLine + text.Get("仅处理下列缓存文件，已提取的 MP4 保留。");
            layout.Controls.Add(new Label { Text = message, Dock = DockStyle.Fill }, 0, 0);
            var files = new TextBox { Multiline = true, ReadOnly = true, WordWrap = false, ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill, BackColor = Color.White, Text = string.Join(Environment.NewLine + Environment.NewLine,
                    plan.Files.GroupBy(f => f.Title).Select(g => g.Key + Environment.NewLine + string.Join(Environment.NewLine, g.Select(f => f.Path)))) };
            layout.Controls.Add(files, 0, 1);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
            var cancel = new Button { Text = text.Get("取消"), DialogResult = DialogResult.Cancel, Width = 105, Height = 32 };
            var recycle = new Button { Text = text.Get("移入回收站"), DialogResult = DialogResult.OK, Width = 210, Height = 32,
                BackColor = Color.FromArgb(209, 53, 64), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            recycle.FlatAppearance.BorderSize = 0;
            buttons.Controls.Add(cancel); buttons.Controls.Add(recycle); layout.Controls.Add(buttons, 0, 2);
            CancelButton = cancel; AcceptButton = cancel; Shown += delegate { cancel.Select(); };
        }
    }
}
