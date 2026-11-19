using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AuditTools.Common;

namespace PowerEventViewer
{
    internal sealed class MainForm : Form
    {
        private static readonly Color BootColor = Color.FromArgb(0x00, 0x64, 0x00);       // #006400
        private static readonly Color ShutdownColor = Color.FromArgb(0x8B, 0x00, 0x00);   // #8B0000
        private static readonly Color SleepColor = Color.FromArgb(0xB8, 0x86, 0x0B);      // #B8860B
        private static readonly Color HibernateColor = Color.FromArgb(0x8B, 0x45, 0x13);  // #8B4513

        private static readonly string[] ExportHeaders = { "時間", "事件類型", "事件ID", "來源", "記錄檔", "說明" };
        private static readonly double[] ExportWidths = { 20, 10, 8, 38, 10, 100 };

        private readonly DateTimePicker _dtStart;
        private readonly DateTimePicker _dtEnd;
        private readonly CheckBox _chkBoot;
        private readonly CheckBox _chkShutdown;
        private readonly CheckBox _chkSleep;
        private readonly CheckBox _chkHibernate;
        private readonly Button _btnScan;
        private readonly Button _btnStop;
        private readonly Button _btnExport;
        private readonly Label _status;
        private readonly ListView _list;

        private List<PowerRecord> _records = new List<PowerRecord>();
        private CancellationTokenSource _cts;
        private bool _busy;

        public MainForm()
        {
            SuspendLayout();

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            Text = "Windows 電源事件檢視器";
            ClientSize = new Size(1180, 720);
            MinimumSize = new Size(900, 400);
            StartPosition = FormStartPosition.CenterScreen;
            AppIcon.Apply(this);

            // ---- 1. 控制區 ----
            var ctrlGroup = new GroupBox { Text = "篩選條件", Dock = DockStyle.Top, Height = 62, Padding = new Padding(8, 4, 8, 4) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };

            _dtStart = MakePicker(DateTime.Today.AddDays(-7));
            _dtEnd = MakePicker(DateTime.Today);
            _chkBoot = MakeCheck("開機");
            _chkShutdown = MakeCheck("關機");
            _chkSleep = MakeCheck("睡眠");
            _chkHibernate = MakeCheck("休眠");
            _btnScan = MakeButton("開始分析", OnScanClick, true);
            _btnStop = MakeButton("停止", OnStopClick, false);
            _btnExport = MakeButton("匯出 Excel", OnExportClick, false);

            flow.Controls.Add(MakeLabel("開始日期:", SystemColors.ControlText));
            flow.Controls.Add(_dtStart);
            flow.Controls.Add(MakeLabel("結束日期:", SystemColors.ControlText));
            flow.Controls.Add(_dtEnd);
            flow.Controls.Add(MakeSeparator());
            flow.Controls.Add(_chkBoot);
            flow.Controls.Add(_chkShutdown);
            flow.Controls.Add(_chkSleep);
            flow.Controls.Add(_chkHibernate);
            flow.Controls.Add(MakeSeparator());
            flow.Controls.Add(_btnScan);
            flow.Controls.Add(_btnStop);
            flow.Controls.Add(_btnExport);

            _status = new Label
            {
                Text = "準備就緒",
                ForeColor = Color.Blue,
                Dock = DockStyle.Right,
                Width = 300,
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true,
            };

            ctrlGroup.Controls.Add(flow);
            ctrlGroup.Controls.Add(_status);

            // ---- 2. 圖例 ----
            var legend = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 26, Padding = new Padding(16, 2, 0, 0), WrapContents = false };
            legend.Controls.Add(MakeLabel("圖例：", SystemColors.ControlText));
            legend.Controls.Add(MakeLabel("■ 開機", BootColor));
            legend.Controls.Add(MakeLabel("■ 關機", ShutdownColor));
            legend.Controls.Add(MakeLabel("■ 睡眠", SleepColor));
            legend.Controls.Add(MakeLabel("■ 休眠", HibernateColor));

            // ---- 3. 結果列表 ----
            var resultGroup = new GroupBox { Text = "事件紀錄", Dock = DockStyle.Fill, Padding = new Padding(10) };
            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                VirtualMode = true,
                VirtualListSize = 0,
                ShowItemToolTips = true,
            };
            _list.Columns.Add("時間", 140);
            _list.Columns.Add("事件類型", 80);
            _list.Columns.Add("事件 ID", 70, HorizontalAlignment.Center);
            _list.Columns.Add("來源", 250);
            _list.Columns.Add("記錄檔", 80, HorizontalAlignment.Center);
            _list.Columns.Add("說明", 500);
            _list.RetrieveVirtualItem += OnRetrieveVirtualItem;
            resultGroup.Controls.Add(_list);

            // Dock 順序：集合中越後面的越先停靠
            Controls.Add(resultGroup);
            Controls.Add(legend);
            Controls.Add(ctrlGroup);

            FormClosing += (s, e) => _cts?.Cancel();

            ResumeLayout(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cts?.Cancel();
                _cts = null;
            }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------ UI helpers

        private static DateTimePicker MakePicker(DateTime value)
        {
            return new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "yyyy-MM-dd",
                Width = 110,
                Value = value,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(3, 3, 8, 3),
            };
        }

        private static Label MakeLabel(string text, Color color)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = color, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 3) };
        }

        private static Label MakeSeparator()
        {
            return new Label { BorderStyle = BorderStyle.Fixed3D, Width = 2, Height = 24, Anchor = AnchorStyles.Left, Margin = new Padding(10, 3, 10, 3) };
        }

        private static CheckBox MakeCheck(string text)
        {
            return new CheckBox { Text = text, Checked = true, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 5, 3, 3) };
        }

        private static Button MakeButton(string text, EventHandler onClick, bool enabled)
        {
            var b = new Button { Text = text, AutoSize = true, Enabled = enabled, Anchor = AnchorStyles.Left, Margin = new Padding(3, 2, 3, 2) };
            b.Click += onClick;
            return b;
        }

        private void SetStatus(string text, Color color)
        {
            _status.Text = text;
            _status.ForeColor = color;
        }

        private void SetBusy(bool busy, bool cancellable)
        {
            _busy = busy;
            _btnScan.Enabled = !busy;
            _btnStop.Enabled = busy && cancellable;
            _btnExport.Enabled = !busy && _records.Count > 0;
            _dtStart.Enabled = !busy;
            _dtEnd.Enabled = !busy;
            _chkBoot.Enabled = !busy;
            _chkShutdown.Enabled = !busy;
            _chkSleep.Enabled = !busy;
            _chkHibernate.Enabled = !busy;
            UseWaitCursor = busy && !cancellable;
        }

        private PowerCategory SelectedCategories()
        {
            PowerCategory c = PowerCategory.None;
            if (_chkBoot.Checked) c |= PowerCategory.Boot;
            if (_chkShutdown.Checked) c |= PowerCategory.Shutdown;
            if (_chkSleep.Checked) c |= PowerCategory.Sleep;
            if (_chkHibernate.Checked) c |= PowerCategory.Hibernate;
            return c;
        }

        private static Color ColorOf(PowerCategory c)
        {
            switch (c)
            {
                case PowerCategory.Boot: return BootColor;
                case PowerCategory.Shutdown: return ShutdownColor;
                case PowerCategory.Sleep: return SleepColor;
                case PowerCategory.Hibernate: return HibernateColor;
                default: return SystemColors.WindowText;
            }
        }

        // ------------------------------------------------------------------ list

        private void OnRetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            List<PowerRecord> records = _records;
            if (e.ItemIndex < 0 || e.ItemIndex >= records.Count)
            {
                e.Item = new ListViewItem(new[] { string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty });
                return;
            }

            PowerRecord r = records[e.ItemIndex];
            e.Item = new ListViewItem(new[] { r.TimeText, r.CategoryText, r.EventIdText, r.Source, r.LogName, r.Detail })
            {
                ForeColor = ColorOf(r.Category),
                ToolTipText = r.Detail,
            };
        }

        // ------------------------------------------------------------------ actions

        private async void OnScanClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }

            DateTime start = _dtStart.Value.Date;
            DateTime end = _dtEnd.Value.Date.AddDays(1).AddMilliseconds(-1); // 當天 23:59:59.999
            if (start > end)
            {
                MessageBox.Show(this, "開始日期不能晚於結束日期。", "日期錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            PowerCategory selected = SelectedCategories();
            if (selected == PowerCategory.None)
            {
                MessageBox.Show(this, "請至少勾選一種事件類型。", "篩選錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _list.VirtualListSize = 0;
            _records = new List<PowerRecord>();
            SetBusy(true, true);
            SetStatus("正在讀取事件記錄...", Color.Blue);

            var cts = new CancellationTokenSource();
            _cts = cts;
            var progress = new Progress<ScanProgress>(p =>
            {
                if (!IsDisposed && _busy)
                {
                    SetStatus(string.Format(CultureInfo.InvariantCulture, "已掃描 {0:N0} 筆，符合條件 {1:N0} 筆", p.Scanned, p.Kept), Color.Blue);
                }
            });

            ScanResult result;
            try
            {
                result = await Task.Run(() => PowerScanner.Scan(start, end, selected, cts.Token, progress));
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    SetBusy(false, false);
                    SetStatus("讀取 " + PowerScanner.LogName + " 記錄檔失敗", Color.Red);
                    MessageBox.Show(this, ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }
            finally
            {
                if (ReferenceEquals(_cts, cts))
                {
                    _cts = null;
                }
                cts.Dispose();
            }

            if (IsDisposed)
            {
                return;
            }

            _records = result.Records;
            _list.VirtualListSize = _records.Count;
            SetBusy(false, false);

            if (_records.Count == 0)
            {
                SetStatus(result.Cancelled ? "已停止，無資料" : "分析完成，查無符合條件的事件", Color.Blue);
                return;
            }

            string msg = string.Format(CultureInfo.InvariantCulture, "分析完成，共顯示 {0:N0} 筆事件", _records.Count);
            if (result.Cancelled)
            {
                msg += " (已停止，部分結果)";
            }
            if (result.Truncated)
            {
                msg += string.Format(CultureInfo.InvariantCulture, " (已達上限 {0:N0} 筆)", PowerScanner.MaxRecords);
            }
            SetStatus(msg, result.Cancelled || result.Truncated ? Color.DarkOrange : Color.Green);
        }

        private void OnStopClick(object sender, EventArgs e)
        {
            CancellationTokenSource cts = _cts;
            if (cts != null)
            {
                cts.Cancel();
                _btnStop.Enabled = false;
                SetStatus("正在停止...", Color.Blue);
            }
        }

        private async void OnExportClick(object sender, EventArgs e)
        {
            List<PowerRecord> snapshot = _records;
            if (_busy || snapshot.Count == 0)
            {
                return;
            }

            string path;
            using (var dlg = new SaveFileDialog
            {
                Title = "另存為 Excel",
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                DefaultExt = "xlsx",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = "power_events_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".xlsx",
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                path = dlg.FileName;
            }

            SetBusy(true, false);
            SetStatus("正在匯出...", Color.Blue);
            try
            {
                await Task.Run(() => XlsxWriter.Write(path, ExportHeaders, ExportWidths, ToRows(snapshot)));
                if (IsDisposed)
                {
                    return;
                }
                SetBusy(false, false);
                SetStatus(string.Format(CultureInfo.InvariantCulture, "匯出完成 ({0:N0} 筆)", snapshot.Count), Color.Green);
                MessageBox.Show(this, "Excel 匯出完成。", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                if (IsDisposed)
                {
                    return;
                }
                SetBusy(false, false);
                SetStatus("匯出失敗", Color.Red);
                MessageBox.Show(this, ex.Message, "匯出失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static IEnumerable<string[]> ToRows(List<PowerRecord> records)
        {
            foreach (PowerRecord r in records)
            {
                yield return new[] { r.TimeText, r.CategoryText, r.EventIdText, r.Source, r.LogName, r.Detail };
            }
        }
    }
}
