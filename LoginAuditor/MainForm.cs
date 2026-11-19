using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AuditTools.Common;

namespace LoginAuditor
{
    internal sealed class MainForm : Form
    {
        private static readonly Color RareColor = Color.FromArgb(0x80, 0x00, 0x80);
        private static readonly Color FailBackColor = Color.FromArgb(0xFF, 0xF0, 0xF0);

        private static readonly string[] ExportHeaders = { "時間", "狀態", "帳號", "來源IP", "登入類型", "備註" };
        private static readonly double[] ExportWidths = { 20, 8, 20, 18, 24, 18 };

        private readonly DateTimePicker _dtStart;
        private readonly DateTimePicker _dtEnd;
        private readonly Button _btnScan;
        private readonly Button _btnStop;
        private readonly Button _btnExport;
        private readonly Label _status;
        private readonly ListView _list;
        private readonly Font _boldFont;

        private List<LogonRecord> _records = new List<LogonRecord>();
        private CancellationTokenSource _cts;
        private bool _busy;

        public MainForm()
        {
            SuspendLayout();

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            Text = "Windows 登入稽核 (排除系統帳號 SYSTEM)";
            ClientSize = new Size(1100, 700);
            MinimumSize = new Size(820, 400);
            StartPosition = FormStartPosition.CenterScreen;
            AppIcon.Apply(this);

            _boldFont = new Font(Font, FontStyle.Bold);

            // ---- 1. 控制區 ----
            var ctrlGroup = new GroupBox { Text = "篩選條件", Dock = DockStyle.Top, Height = 62, Padding = new Padding(8, 4, 8, 4) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };

            _dtStart = MakePicker(DateTime.Today.AddDays(-7));
            _dtEnd = MakePicker(DateTime.Today);
            _btnScan = MakeButton("開始分析", OnScanClick, true);
            _btnStop = MakeButton("停止", OnStopClick, false);
            _btnExport = MakeButton("匯出 Excel", OnExportClick, false);

            flow.Controls.Add(MakeLabel("開始日期:", SystemColors.ControlText, false));
            flow.Controls.Add(_dtStart);
            flow.Controls.Add(MakeLabel("結束日期:", SystemColors.ControlText, false));
            flow.Controls.Add(_dtEnd);
            flow.Controls.Add(new Label { BorderStyle = BorderStyle.Fixed3D, Width = 2, Height = 24, Anchor = AnchorStyles.Left, Margin = new Padding(10, 3, 10, 3) });
            flow.Controls.Add(_btnScan);
            flow.Controls.Add(_btnStop);
            flow.Controls.Add(_btnExport);

            _status = new Label
            {
                Text = "準備就緒",
                ForeColor = Color.Blue,
                Dock = DockStyle.Right,
                Width = 380,
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true,
            };

            ctrlGroup.Controls.Add(flow);
            ctrlGroup.Controls.Add(_status);

            // ---- 2. 圖例 ----
            var legend = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 26, Padding = new Padding(16, 2, 0, 0), WrapContents = false };
            legend.Controls.Add(MakeLabel("圖例說明：", SystemColors.ControlText, false));
            legend.Controls.Add(MakeLabel("■ 登入失敗 (警告)", Color.Red, false));
            legend.Controls.Add(MakeLabel("■ 少數/異常帳號 (頻率<3%)", RareColor, true));
            legend.Controls.Add(MakeLabel("■ 正常登入", SystemColors.ControlText, false));

            // ---- 3. 結果列表 ----
            var resultGroup = new GroupBox { Text = "分析結果 (已隱藏 SYSTEM 成功登入)", Dock = DockStyle.Fill, Padding = new Padding(10) };
            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                VirtualMode = true,
                VirtualListSize = 0,
            };
            _list.Columns.Add("時間", 140);
            _list.Columns.Add("狀態", 70);
            _list.Columns.Add("帳號名稱", 140);
            _list.Columns.Add("來源 IP", 130);
            _list.Columns.Add("登入類型", 180);
            _list.Columns.Add("備註", 150);
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
            base.Dispose(disposing);   // 先釋放子控制項，再釋放它們共用的字型
            if (disposing)
            {
                _boldFont.Dispose();
            }
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

        private Label MakeLabel(string text, Color color, bool bold)
        {
            var lbl = new Label { Text = text, AutoSize = true, ForeColor = color, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 3) };
            if (bold)
            {
                lbl.Font = _boldFont;
            }
            return lbl;
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
            UseWaitCursor = busy && !cancellable;
        }

        // ------------------------------------------------------------------ list

        private void OnRetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            List<LogonRecord> records = _records;
            if (e.ItemIndex < 0 || e.ItemIndex >= records.Count)
            {
                e.Item = new ListViewItem(new[] { string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty });
                return;
            }

            LogonRecord r = records[e.ItemIndex];
            var item = new ListViewItem(new[] { r.TimeText, r.StatusText, r.Account, r.Ip, r.LogonType, r.Note });
            switch (r.Kind)
            {
                case RecordKind.Fail:
                    item.ForeColor = Color.Red;
                    item.BackColor = FailBackColor;
                    break;
                case RecordKind.Rare:
                    item.ForeColor = RareColor;
                    item.Font = _boldFont;
                    break;
            }
            e.Item = item;
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
                MessageBox.Show(this, "開始日期不能晚於結束日期", "日期錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _list.VirtualListSize = 0;
            _records = new List<LogonRecord>();
            SetBusy(true, true);
            SetStatus("正在讀取並過濾 SYSTEM 帳號...", Color.Blue);

            var cts = new CancellationTokenSource();
            _cts = cts;
            var progress = new Progress<ScanProgress>(p =>
            {
                if (!IsDisposed && _busy)
                {
                    SetStatus(string.Format(CultureInfo.InvariantCulture, "已掃描: {0:N0} 筆 | 有效紀錄: {1:N0} 筆", p.Scanned, p.Kept), Color.Blue);
                }
            });

            ScanResult result;
            try
            {
                result = await Task.Run(() => AuditScanner.Scan(start, end, cts.Token, progress));
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    SetBusy(false, false);
                    SetStatus("讀取失敗", Color.Red);
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
                SetStatus(result.Cancelled ? "已停止，無資料" : "未發現在此期間的登入紀錄 (SYSTEM已過濾)", Color.Blue);
                return;
            }

            string msg = string.Format(CultureInfo.InvariantCulture, "分析完成，顯示 {0:N0} 筆紀錄", _records.Count);
            if (result.Cancelled)
            {
                msg += " (已停止，部分結果)";
            }
            if (result.Truncated)
            {
                msg += string.Format(CultureInfo.InvariantCulture, " (已達上限 {0:N0} 筆，請縮小日期範圍)", AuditScanner.MaxRecords);
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
            List<LogonRecord> snapshot = _records;
            if (_busy || snapshot.Count == 0)
            {
                return;
            }

            string path;
            using (var dlg = new SaveFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                DefaultExt = "xlsx",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = "login_audit_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".xlsx",
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
                MessageBox.Show(this, "匯出完成", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                if (IsDisposed)
                {
                    return;
                }
                SetBusy(false, false);
                SetStatus("匯出失敗", Color.Red);
                MessageBox.Show(this, ex.Message, "失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static IEnumerable<string[]> ToRows(List<LogonRecord> records)
        {
            foreach (LogonRecord r in records)
            {
                yield return new[] { r.TimeText, r.StatusText, r.Account, r.Ip, r.LogonType, r.Note };
            }
        }
    }
}
