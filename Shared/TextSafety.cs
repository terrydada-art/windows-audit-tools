using System.Text;

namespace AuditTools.Common
{
    /// <summary>
    /// 事件記錄內容可能來自不可信來源 (遠端登入帳號名稱、程式命令列…)，
    /// 顯示或匯出前先移除控制字元與 Unicode 雙向覆寫字元 (防止畫面/報表偽裝)，並限制長度。
    /// </summary>
    internal static class TextSafety
    {
        /// <param name="s">原始字串</param>
        /// <param name="maxLength">最大長度 (超過以 … 結尾)</param>
        /// <param name="flattenWhitespace">true：換行/Tab 轉為空白並合併連續空白、去頭尾 (適用多行說明文字)</param>
        public static string Sanitize(string s, int maxLength, bool flattenWhitespace)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(System.Math.Min(s.Length, maxLength + 1));
            bool lastWasSpace = false;
            foreach (char raw in s)
            {
                if (sb.Length >= maxLength)
                {
                    sb.Append('…');
                    break;
                }

                char c = raw;
                if (flattenWhitespace)
                {
                    if (c == '\r' || c == '\n' || c == '\t' || c == ' ' || c == ' ')
                    {
                        if (lastWasSpace || sb.Length == 0)
                        {
                            continue;
                        }
                        sb.Append(' ');
                        lastWasSpace = true;
                        continue;
                    }
                }

                if (IsDirectionMark(c))
                {
                    continue;   // LRM/RLM/ALM：Windows 訊息常在日期前後插入，不可見且無法重排文字，直接移除
                }

                sb.Append(IsUnsafeChar(c) ? '?' : c);
                lastWasSpace = false;
            }

            if (flattenWhitespace && sb.Length > 0 && sb[sb.Length - 1] == ' ')
            {
                sb.Length--;
            }

            return sb.ToString();
        }

        private static bool IsDirectionMark(char c)
        {
            return c == '‎' || c == '‏' || c == '؜';   // LRM/RLM/ALM
        }

        /// <summary>控制字元與可重排文字順序的雙向嵌入/覆寫/隔離字元 → 以 ? 顯示，讓偽裝痕跡可見。</summary>
        private static bool IsUnsafeChar(char c)
        {
            return char.IsControl(c)
                || (c >= '‪' && c <= '‮')   // LRE/RLE/PDF/LRO/RLO
                || (c >= '⁦' && c <= '⁩');  // LRI/RLI/FSI/PDI
        }
    }
}
