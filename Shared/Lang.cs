using System;
using System.Globalization;

namespace AuditTools.Common
{
    /// <summary>
    /// 介面語言：中文 Windows (zh-*) 顯示繁體中文，其他語言顯示英文；
    /// 可用命令列參數 /lang:en 或 /lang:zh 強制指定。
    /// UI language: Chinese on zh-* Windows, English otherwise; override with /lang:en or /lang:zh.
    /// </summary>
    internal static class Lang
    {
        public static readonly bool IsChinese = Detect();

        /// <summary>依目前語言傳回中文或英文字串。Returns the Chinese or English text.</summary>
        public static string T(string zh, string en)
        {
            return IsChinese ? zh : en;
        }

        private static bool Detect()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i].TrimStart('/', '-');
                if (a.Length < 6 || !a.StartsWith("lang", StringComparison.OrdinalIgnoreCase) || (a[4] != ':' && a[4] != '='))
                {
                    continue;
                }

                string value = a.Substring(5);
                if (value.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (value.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase);
        }
    }
}
