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

        private static readonly string[] ExportHeaders = Lang.IsChinese
            ? new[] { "時間", "事件類型", "事件ID", "來源", "記錄檔", "說明" }
            : new[] { "Time", "Category", "Event ID", "Source", "Log", "Description" };
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
            Text = Lang.T("Windows 電源事件檢視器", "Windows Power Event Viewer");
            ClientSize = new Size(1260, 720);   // 英文介面的勾選框/按鈕較寬
            MinimumSize = new Size(900, 400);
            StartPosition = FormStartPosition.CenterScreen;
            AppIcon.Apply(this);

            // ---- 1. 控制區 ----
            var ctrlGroup = new GroupBox { Text = Lang.T("篩選條件", "Filter"), Dock = DockStyle.Top, Height = 62, Padding = new Padding(8, 4, 8, 4) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };

            _dtStart = MakePicker(DateTime.Today.AddDays(-7));
            _dtEnd = MakePicker(DateTime.Today);
            _chkBoot = MakeCheck(PowerScanner.CategoryName(PowerCategory.Boot));
            _chkShutdown = MakeCheck(PowerScanner.CategoryName(PowerCategory.Shutdown));
            _chkSleep = MakeCheck(PowerScanner.CategoryName(PowerCategory.Sleep));
            _chkHibernate = MakeCheck(PowerScanner.CategoryName(PowerCategory.Hibernate));
            _btnScan = MakeButton(Lang.T("開始分析", "Analyze"), OnScanClick, true);
            _btnStop = MakeButton(Lang.T("停止", "Stop"), OnStopClick, false);
            _btnExport = MakeButton(Lang.T("匯出 Excel", "Export to Excel"), OnExportClick, false);

            flow.Controls.Add(MakeLabel(Lang.T("開始日期:", "From:"), SystemColors.ControlText));
            flow.Controls.Add(_dtStart);
            flow.Controls.Add(MakeLabel(Lang.T("結束日期:", "To:"), SystemColors.ControlText));
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
                Text = Lang.T("準備就緒", "Ready"),
                ForeColor = Color.Blue,
                Dock = DockStyle.Right,
                Width = 280,
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true,
            };

            ctrlGroup.Controls.Add(flow);
            ctrlGroup.Controls.Add(_status);

            // ---- 2. 圖例 ----
            var legend = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 26, Padding = new Padding(16, 2, 0, 0), WrapContents = false };
            legend.Controls.Add(MakeLabel(Lang.T("圖例：", "Legend:"), SystemColors.ControlText));
            legend.Controls.Add(MakeLabel("■ " + PowerScanner.CategoryName(PowerCategory.Boot), BootColor));
            legend.Controls.Add(MakeLabel("■ " + PowerScanner.CategoryName(PowerCategory.Shutdown), ShutdownColor));
            legend.Controls.Add(MakeLabel("■ " + PowerScanner.CategoryName(PowerCategory.Sleep), SleepColor));
            legend.Controls.Add(MakeLabel("■ " + PowerScanner.CategoryName(PowerCategory.Hibernate), HibernateColor));

            // ---- 3. 結果列表 ----
            var resultGroup = new GroupBox { Text = Lang.T("事件紀錄", "Events"), Dock = DockStyle.Fill, Padding = new Padding(10) };
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
            _list.Columns.Add(Lang.T("時間", "Time"), 140);
            _list.Columns.Add(Lang.T("事件類型", "Category"), 80);
            _list.Columns.Add(Lang.T("事件 ID", "Event ID"), 70, HorizontalAlignment.Center);
            _list.Columns.Add(Lang.T("來源", "Source"), 250);
            _list.Columns.Add(Lang.T("記錄檔", "Log"), 80, HorizontalAlignment.Center);
            _list.Columns.Add(Lang.T("說明", "Description"), 500);
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
                MessageBox.Show(this, Lang.T("開始日期不能晚於結束日期。", "The start date cannot be later than the end date."),
                    Lang.T("日期錯誤", "Invalid date"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            PowerCategory selected = SelectedCategories();
            if (selected == PowerCategory.None)
            {
                MessageBox.Show(this, Lang.T("請至少勾選一種事件類型。", "Please select at least one event category."),
                    Lang.T("篩選錯誤", "Invalid filter"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _list.VirtualListSize = 0;
            _records = new List<PowerRecord>();
            SetBusy(true, true);
            SetStatus(Lang.T("正在讀取事件記錄...", "Reading the event log..."), Color.Blue);

            var cts = new CancellationTokenSource();
            _cts = cts;
            var progress = new Progress<ScanProgress>(p =>
            {
                if (!IsDisposed && _busy)
                {
                    SetStatus(string.Format(CultureInfo.InvariantCulture,
                        Lang.T("已掃描 {0:N0} 筆，符合條件 {1:N0} 筆", "Scanned {0:N0}, matched {1:N0}"), p.Scanned, p.Kept), Color.Blue);
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
                    SetStatus(string.Format(CultureInfo.InvariantCulture,
                        Lang.T("讀取 {0} 記錄檔失敗", "Failed to read the {0} log"), PowerScanner.LogName), Color.Red);
                    MessageBox.Show(this, ex.Message, Lang.T("錯誤", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                SetStatus(result.Cancelled
                    ? Lang.T("已停止，無資料", "Stopped, no data")
                    : Lang.T("分析完成，查無符合條件的事件", "Done, no matching events"), Color.Blue);
                return;
            }

            string msg = string.Format(CultureInfo.InvariantCulture, Lang.T("分析完成，共顯示 {0:N0} 筆事件", "Done, showing {0:N0} events"), _records.Count);
            if (result.Cancelled)
            {
                msg += Lang.T(" (已停止，部分結果)", " (stopped, partial results)");
            }
            if (result.Truncated)
            {
                msg += string.Format(CultureInfo.InvariantCulture, Lang.T(" (已達上限 {0:N0} 筆)", " (limit of {0:N0} reached)"), PowerScanner.MaxRecords);
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
                SetStatus(Lang.T("正在停止...", "Stopping..."), Color.Blue);
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
                Title = Lang.T("另存為 Excel", "Save as Excel"),
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
            SetStatus(Lang.T("正在匯出...", "Exporting..."), Color.Blue);
            try
            {
                await Task.Run(() => XlsxWriter.Write(path, ExportHeaders, ExportWidths, ToRows(snapshot)));
                if (IsDisposed)
                {
                    return;
                }
                SetBusy(false, false);
                SetStatus(string.Format(CultureInfo.InvariantCulture, Lang.T("匯出完成 ({0:N0} 筆)", "Exported ({0:N0} records)"), snapshot.Count), Color.Green);
                MessageBox.Show(this, Lang.T("Excel 匯出完成。", "Excel export completed."), Lang.T("成功", "Success"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                if (IsDisposed)
                {
                    return;
                }
                SetBusy(false, false);
                SetStatus(Lang.T("匯出失敗", "Export failed"), Color.Red);
                MessageBox.Show(this, ex.Message, Lang.T("匯出失敗", "Export failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
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
