using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Threading;
using AuditTools.Common;

namespace LoginAuditor
{
    internal enum RecordKind
    {
        Normal,
        Fail,
        Rare,
    }

    internal sealed class LogonRecord
    {
        public DateTime Time;
        public bool Success;
        public string Account = string.Empty;
        public string Ip = string.Empty;
        public string LogonType = string.Empty;
        public string Note = string.Empty;
        public RecordKind Kind = RecordKind.Normal;

        public string TimeText => Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        public string StatusText => Success ? "成功" : "失敗";
    }

    internal readonly struct ScanProgress
    {
        public ScanProgress(int scanned, int kept)
        {
            Scanned = scanned;
            Kept = kept;
        }

        public int Scanned { get; }
        public int Kept { get; }
    }

    internal sealed class ScanResult
    {
        public readonly List<LogonRecord> Records = new List<LogonRecord>();
        public bool Cancelled;
        public bool Truncated;
    }

    internal static class AuditScanner
    {
        /// <summary>上限：避免超大記錄檔耗盡記憶體，也剛好落在 Excel 1,048,576 列之內。</summary>
        public const int MaxRecords = 1_000_000;

        private const int ProgressInterval = 1000;
        private const int MaxFieldLength = 256;

        // 依欄位「名稱」取值，而不是 Python 版的固定索引 (data[5]/data[8]/data[18])，
        // 不會因 Windows 版本調整欄位順序而取錯欄。
        private static readonly string[] PropertyPaths =
        {
            "Event/EventData/Data[@Name='TargetUserName']",
            "Event/EventData/Data[@Name='TargetDomainName']",
            "Event/EventData/Data[@Name='LogonType']",
            "Event/EventData/Data[@Name='IpAddress']",
        };

        private static readonly Dictionary<string, string> LogonTypeNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["2"] = "本機登入",
            ["3"] = "網路共用",
            ["4"] = "批次作業",
            ["5"] = "服務啟動",
            ["7"] = "解鎖",
            ["10"] = "遠端桌面(RDP)",
            ["11"] = "快取",
        };

        /// <summary>
        /// 讀取 Security 記錄檔中指定區間 (本地時間, 含頭含尾) 的 4624/4625 事件。
        /// 篩選 (事件 ID + 時間) 交由事件記錄服務以 XPath 處理，不必逐筆讀完整個記錄檔。
        /// </summary>
        public static ScanResult Scan(DateTime startLocal, DateTime endLocal, CancellationToken token, IProgress<ScanProgress> progress)
        {
            // XPath 內只放程式自行格式化的 UTC 時間字串 (不含任何使用者輸入)，不會有 XPath 注入。
            string xpath = string.Format(CultureInfo.InvariantCulture,
                "*[System[(EventID=4624 or EventID=4625) and TimeCreated[@SystemTime>='{0}' and @SystemTime<='{1}']]]",
                ToXPathTime(startLocal), ToXPathTime(endLocal));

            var query = new EventLogQuery("Security", PathType.LogName, xpath) { ReverseDirection = true };
            var result = new ScanResult();
            List<LogonRecord> records = result.Records;
            int scanned = 0;

            using (var selector = new EventLogPropertySelector(PropertyPaths))
            using (var reader = new EventLogReader(query))
            {
                while (true)
                {
                    if (token.IsCancellationRequested)
                    {
                        result.Cancelled = true;
                        break;
                    }

                    if (records.Count >= MaxRecords)
                    {
                        result.Truncated = true;
                        break;
                    }

                    using (EventRecord rec = reader.ReadEvent())
                    {
                        if (rec == null)
                        {
                            break;
                        }

                        scanned++;
                        LogonRecord parsed = TryParse(rec as EventLogRecord, selector);
                        if (parsed != null)
                        {
                            records.Add(parsed);
                        }
                    }

                    if (scanned % ProgressInterval == 0)
                    {
                        progress?.Report(new ScanProgress(scanned, records.Count));
                    }
                }
            }

            progress?.Report(new ScanProgress(scanned, records.Count));
            MarkRecords(records);
            return result;
        }

        private static string ToXPathTime(DateTime local)
        {
            return local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }

        private static LogonRecord TryParse(EventLogRecord rec, EventLogPropertySelector selector)
        {
            if (rec == null)
            {
                return null;
            }

            int id = rec.Id;
            if (id != 4624 && id != 4625)
            {
                return null;
            }

            DateTime? created = rec.TimeCreated;
            if (!created.HasValue)
            {
                return null;
            }

            IList<object> values;
            try
            {
                values = rec.GetPropertyValues(selector);
            }
            catch (EventLogException)
            {
                return null;
            }

            if (values == null || values.Count < PropertyPaths.Length)
            {
                return null;
            }

            bool success = id == 4624;
            string user = AsString(values[0]);
            string domain = AsString(values[1]);

            // 排除系統帳號 (SYSTEM / NT AUTHORITY\SYSTEM / ANONYMOUS LOGON / 電腦帳號 xxx$)。
            // 只套用在「成功」登入：登入失敗一律保留，避免攻擊者以 SYSTEM 或結尾為 $ 的帳號名稱
            // 進行暴力嘗試時被隱藏而躲過稽核。
            if (success && IsSystemAccount(user, domain))
            {
                return null;
            }

            string code = AsString(values[2]);
            string typeName;
            if (!LogonTypeNames.TryGetValue(code, out typeName))
            {
                typeName = "其他";
            }

            return new LogonRecord
            {
                Time = created.Value.Kind == DateTimeKind.Utc ? created.Value.ToLocalTime() : created.Value,
                Success = success,
                Account = Sanitize(user),
                Ip = Sanitize(AsString(values[3])),
                LogonType = Sanitize(code) + " (" + typeName + ")",
            };
        }

        private static bool IsSystemAccount(string user, string domain)
        {
            string upper = user.ToUpperInvariant();
            string full = (domain + "\\" + user).ToUpperInvariant();
            return upper == "SYSTEM"
                || full.Contains("AUTHORITY\\SYSTEM")
                || upper == "ANONYMOUS LOGON"
                || upper.EndsWith("$", StringComparison.Ordinal);
        }

        /// <summary>統計帳號頻率並標記：失敗 (紅) / 少數帳號 &lt;= max(2, 3%) (紫) / 正常。</summary>
        private static void MarkRecords(List<LogonRecord> records)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (LogonRecord r in records)
            {
                int c;
                counts.TryGetValue(r.Account, out c);
                counts[r.Account] = c + 1;
            }

            int threshold = Math.Max(2, (int)(records.Count * 0.03));

            foreach (LogonRecord r in records)
            {
                if (!r.Success)
                {
                    r.Kind = RecordKind.Fail;
                    r.Note = "登入失敗";
                }
                else if (counts[r.Account] <= threshold)
                {
                    r.Kind = RecordKind.Rare;
                    r.Note = "少數帳號 (異常?)";
                }
            }
        }

        private static string AsString(object value)
        {
            return value == null ? string.Empty : (Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }

        /// <summary>
        /// 事件內容 (尤其 4625 的帳號名稱) 可由遠端攻擊者任意指定，視為不可信資料：
        /// 移除控制字元與 Unicode 雙向覆寫字元 (防止畫面/報表偽裝)，並限制長度。
        /// </summary>
        internal static string Sanitize(string s)
        {
            return TextSafety.Sanitize(s, MaxFieldLength, false);
        }
    }
}
