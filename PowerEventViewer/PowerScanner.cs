using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using System.Threading;
using AuditTools.Common;

namespace PowerEventViewer
{
    [Flags]
    internal enum PowerCategory
    {
        None = 0,
        Boot = 1,
        Shutdown = 2,
        Sleep = 4,
        Hibernate = 8,
    }

    internal sealed class PowerRecord
    {
        public DateTime Time;
        public PowerCategory Category;
        public int EventId;
        public string Source = string.Empty;
        public string LogName = string.Empty;
        public string Detail = string.Empty;

        public string TimeText => Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        public string CategoryText => PowerScanner.CategoryName(Category);
        public string EventIdText => EventId.ToString(CultureInfo.InvariantCulture);
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
        public readonly List<PowerRecord> Records = new List<PowerRecord>();
        public bool Cancelled;
        public bool Truncated;
    }

    /// <summary>
    /// 事件對照：
    ///   12   Kernel-General        作業系統啟動              → 開機
    ///   13   Kernel-General        作業系統關機              → 關機
    ///   6005 EventLog              事件記錄服務啟動 (開機完成) → 開機
    ///   6006 EventLog              事件記錄服務停止 (正常關機) → 關機
    ///   6008 EventLog              非正常關機                → 關機
    ///   41   Kernel-Power          未正常關機/異常重啟        → 關機
    ///   42   Kernel-Power          進入睡眠                  → 睡眠
    ///   45   Kernel-Power          從休眠恢復                → 休眠
    ///   1074 User32                使用者/程式發起關機或重啟   → 關機
    ///   1    Power-Troubleshooter  從睡眠喚醒                → 睡眠
    ///   其他 Kernel-Power / Power-Troubleshooter 事件：內容含 hibernate/休眠 → 休眠，sleep/睡眠 → 睡眠
    ///
    /// 與 Python 版的差異 (安全/效能)：
    ///   Python 版會把 System + Security 兩個記錄檔的「所有」事件都拿來比對關鍵字。
    ///   實測 Security 記錄檔 60 天約 65 萬筆、讀取約 10 分鐘，命中的只有 4688 (程式建立) 與
    ///   4907 (稽核原則變更) 這類與電源無關的事件 —— 任何一般使用者執行含 "sleep" 字樣的命令
    ///   (例如 `timeout`/`sleep.exe`、或在命令列帶入 hibernate 字串) 就能偽造出「睡眠/休眠」紀錄。
    ///   因此關鍵字比對只套用在電源相關的提供者，Security 記錄檔不再讀取，也因此不再需要系統管理員權限。
    /// </summary>
    internal static class PowerScanner
    {
        public const int MaxRecords = 1_000_000;
        public const string LogName = "System";

        private const int ProgressInterval = 500;
        private const int MaxDetailLength = 4000;
        private const int MaxSourceLength = 128;
        private const string NoDetail = "無可顯示的詳細事件內容";

        private const string KernelGeneral = "Microsoft-Windows-Kernel-General";
        private const string KernelPower = "Microsoft-Windows-Kernel-Power";
        private const string PowerTroubleshooter = "Microsoft-Windows-Power-Troubleshooter";
        private const string EventLogProvider = "EventLog";
        private const string User32 = "User32";

        public static string CategoryName(PowerCategory c)
        {
            switch (c)
            {
                case PowerCategory.Boot: return "開機";
                case PowerCategory.Shutdown: return "關機";
                case PowerCategory.Sleep: return "睡眠";
                case PowerCategory.Hibernate: return "休眠";
                default: return "其他";
            }
        }

        /// <summary>讀取 System 記錄檔中指定區間 (本地時間, 含頭含尾) 的電源事件，依時間由舊到新。</summary>
        public static ScanResult Scan(DateTime startLocal, DateTime endLocal, PowerCategory selected, CancellationToken token, IProgress<ScanProgress> progress)
        {
            // 由事件記錄服務先依 提供者 + 事件 ID + 時間 篩選，不必把整個記錄檔讀進來。
            // XPath 內只有常數與程式自行格式化的 UTC 時間 (無使用者輸入)，不會有 XPath 注入。
            string xpath = string.Format(CultureInfo.InvariantCulture,
                "*[System[(" +
                "(Provider[@Name='{0}'] and (EventID=12 or EventID=13)) or " +
                "(Provider[@Name='{1}'] and (EventID=6005 or EventID=6006 or EventID=6008)) or " +
                "(Provider[@Name='{2}'] and EventID=1074) or " +
                "Provider[@Name='{3}'] or " +
                "Provider[@Name='{4}']" +
                ") and TimeCreated[@SystemTime>='{5}' and @SystemTime<='{6}']]]",
                KernelGeneral, EventLogProvider, User32, KernelPower, PowerTroubleshooter,
                ToXPathTime(startLocal), ToXPathTime(endLocal));

            var query = new EventLogQuery(LogName, PathType.LogName, xpath);   // 正向讀取 = 由舊到新
            var result = new ScanResult();
            List<PowerRecord> records = result.Records;
            int scanned = 0;

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
                        PowerRecord parsed = TryParse(rec, selected);
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

            // 記錄檔是依寫入順序 (RecordId) 排列，開機時的事件時間戳記可能前後交錯 (例如 6005 先寫入、
            // 但 12 的時間較早)，因此與 Python 版相同再依時間排序；OrderBy 為穩定排序，同時間保留原順序。
            List<PowerRecord> sorted = records.OrderBy(r => r.Time).ToList();
            records.Clear();
            records.AddRange(sorted);
            return result;
        }

        private static string ToXPathTime(DateTime local)
        {
            return local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }

        private static PowerRecord TryParse(EventRecord rec, PowerCategory selected)
        {
            DateTime? created = rec.TimeCreated;
            if (!created.HasValue)
            {
                return null;
            }

            string provider = rec.ProviderName ?? string.Empty;
            PowerCategory category = Classify(provider, rec.Id, rec);
            if (category == PowerCategory.None || (category & selected) == 0)
            {
                return null;
            }

            return new PowerRecord
            {
                Time = created.Value.Kind == DateTimeKind.Utc ? created.Value.ToLocalTime() : created.Value,
                Category = category,
                EventId = rec.Id,
                Source = TextSafety.Sanitize(provider, MaxSourceLength, true),
                LogName = LogName,
                Detail = BuildDetail(rec),
            };
        }

        private static PowerCategory Classify(string provider, int id, EventRecord rec)
        {
            if (Is(provider, KernelGeneral))
            {
                return id == 12 ? PowerCategory.Boot : id == 13 ? PowerCategory.Shutdown : PowerCategory.None;
            }

            if (Is(provider, EventLogProvider))
            {
                return id == 6005 ? PowerCategory.Boot
                    : (id == 6006 || id == 6008) ? PowerCategory.Shutdown
                    : PowerCategory.None;
            }

            if (Is(provider, User32))
            {
                return id == 1074 ? PowerCategory.Shutdown : PowerCategory.None;
            }

            if (Is(provider, KernelPower))
            {
                switch (id)
                {
                    case 41: return PowerCategory.Shutdown;
                    case 42: return PowerCategory.Sleep;
                    case 45: return PowerCategory.Hibernate;
                    default: return KeywordCategory(rec);
                }
            }

            if (Is(provider, PowerTroubleshooter))
            {
                return id == 1 ? PowerCategory.Sleep : KeywordCategory(rec);
            }

            return PowerCategory.None;
        }

        private static bool Is(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>僅用於電源相關提供者：依事件參數內容補充判斷 (先休眠、後睡眠，與 Python 版相同順序)。</summary>
        private static PowerCategory KeywordCategory(EventRecord rec)
        {
            IList<EventProperty> props = SafeProperties(rec);
            if (props.Count == 0)
            {
                return PowerCategory.None;
            }

            var parts = new List<string>(props.Count);
            foreach (EventProperty p in props)
            {
                parts.Add(AsString(p.Value));
            }

            string merged = string.Join(" ", parts).ToLowerInvariant();
            if (merged.Contains("hibernate") || merged.Contains("休眠"))
            {
                return PowerCategory.Hibernate;
            }

            if (merged.Contains("sleep") || merged.Contains("睡眠"))
            {
                return PowerCategory.Sleep;
            }

            return PowerCategory.None;
        }

        /// <summary>
        /// 說明欄：優先使用 Windows 的本地化事件訊息 (FormatDescription，比 Python 版只串接參數易讀)，
        /// 取不到時退回「事件參數去重後以 | 串接」(Python 版做法)。
        /// </summary>
        private static string BuildDetail(EventRecord rec)
        {
            string desc = null;
            try
            {
                desc = rec.FormatDescription();
            }
            catch (EventLogException)
            {
            }

            string clean = TextSafety.Sanitize(desc, MaxDetailLength, true);
            if (clean.Length > 0)
            {
                return clean;
            }

            var unique = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (EventProperty p in SafeProperties(rec))
            {
                if (p.Value is byte[])
                {
                    continue;   // 二進位資料不顯示 (Python 版以 UTF-8 硬解只會得到亂碼)
                }

                string v = TextSafety.Sanitize(AsString(p.Value), MaxDetailLength, true);
                if (v.Length > 0 && seen.Add(v))
                {
                    unique.Add(v);
                }
            }

            return unique.Count > 0
                ? TextSafety.Sanitize(string.Join(" | ", unique), MaxDetailLength, true)
                : NoDetail;
        }

        private static IList<EventProperty> SafeProperties(EventRecord rec)
        {
            try
            {
                return (IList<EventProperty>)rec.Properties ?? Array.Empty<EventProperty>();
            }
            catch (EventLogException)
            {
                return Array.Empty<EventProperty>();
            }
        }

        private static string AsString(object value)
        {
            return value == null ? string.Empty : (Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }
}
