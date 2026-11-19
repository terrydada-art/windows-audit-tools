using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace AuditTools.Common
{
    /// <summary>
    /// 不依賴任何第三方套件的極簡 .xlsx 輸出 (Office Open XML)。
    /// 所有儲存格皆為 inlineStr 文字：Excel 不會把內容當公式執行 (防 CSV/Formula Injection)，
    /// 並透過 XmlWriter 做 XML 跳脫，避免 XML 注入。
    /// </summary>
    internal static class XlsxWriter
    {
        private const int ExcelMaxCellLength = 32767;
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        private const string NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        public static void Write(string path, string[] headers, double[] columnWidths, IEnumerable<string[]> rows)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("path");
            }

            string fullPath = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(dir))
            {
                throw new IOException(Lang.T("無效的儲存路徑", "Invalid save path."));
            }

            // 先寫到同目錄的暫存檔 (CreateNew：不會覆蓋/跟隨既有檔案)，成功後再取代目標，
            // 避免中途失敗留下損毀的 xlsx。
            string tmp = Path.Combine(dir, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    WriteText(zip, "[Content_Types].xml",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                        "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                        "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                        "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                        "</Types>");

                    WriteText(zip, "_rels/.rels",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                        "</Relationships>");

                    WriteText(zip, "xl/workbook.xml",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<workbook xmlns=\"" + NsMain + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                        "<sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
                        "</workbook>");

                    WriteText(zip, "xl/_rels/workbook.xml.rels",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                        "</Relationships>");

                    WriteText(zip, "xl/styles.xml",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<styleSheet xmlns=\"" + NsMain + "\">" +
                        "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                        "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                        "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                        "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
                        "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
                        "</styleSheet>");

                    WriteSheet(zip, headers, columnWidths, rows);
                }

                if (File.Exists(fullPath))
                {
                    File.Replace(tmp, fullPath, null);
                }
                else
                {
                    File.Move(tmp, fullPath);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tmp))
                    {
                        File.Delete(tmp);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private static void WriteText(ZipArchive zip, string name, string content)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var sw = new StreamWriter(entry.Open(), Utf8NoBom))
            {
                sw.Write(content);
            }
        }

        private static void WriteSheet(ZipArchive zip, string[] headers, double[] columnWidths, IEnumerable<string[]> rows)
        {
            ZipArchiveEntry entry = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
            var settings = new XmlWriterSettings { Encoding = Utf8NoBom, CheckCharacters = true, Indent = false };

            using (Stream s = entry.Open())
            using (XmlWriter w = XmlWriter.Create(s, settings))
            {
                w.WriteStartDocument(true);
                w.WriteStartElement("worksheet", NsMain);

                // 凍結標題列
                w.WriteStartElement("sheetViews", NsMain);
                w.WriteStartElement("sheetView", NsMain);
                w.WriteAttributeString("workbookViewId", "0");
                w.WriteStartElement("pane", NsMain);
                w.WriteAttributeString("ySplit", "1");
                w.WriteAttributeString("topLeftCell", "A2");
                w.WriteAttributeString("activePane", "bottomLeft");
                w.WriteAttributeString("state", "frozen");
                w.WriteEndElement();
                w.WriteEndElement();
                w.WriteEndElement();

                if (columnWidths != null && columnWidths.Length > 0)
                {
                    w.WriteStartElement("cols", NsMain);
                    for (int i = 0; i < columnWidths.Length; i++)
                    {
                        string idx = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        w.WriteStartElement("col", NsMain);
                        w.WriteAttributeString("min", idx);
                        w.WriteAttributeString("max", idx);
                        w.WriteAttributeString("width", columnWidths[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
                        w.WriteAttributeString("customWidth", "1");
                        w.WriteEndElement();
                    }
                    w.WriteEndElement();
                }

                w.WriteStartElement("sheetData", NsMain);
                int rowNum = 1;
                WriteRow(w, rowNum++, headers, 1);
                foreach (string[] row in rows)
                {
                    WriteRow(w, rowNum++, row, 0);
                }
                w.WriteEndElement(); // sheetData

                w.WriteEndElement(); // worksheet
                w.WriteEndDocument();
            }
        }

        private static void WriteRow(XmlWriter w, int rowNum, string[] cells, int style)
        {
            string r = rowNum.ToString(System.Globalization.CultureInfo.InvariantCulture);
            w.WriteStartElement("row", NsMain);
            w.WriteAttributeString("r", r);
            for (int i = 0; i < cells.Length; i++)
            {
                w.WriteStartElement("c", NsMain);
                w.WriteAttributeString("r", ColumnName(i) + r);
                w.WriteAttributeString("t", "inlineStr");
                if (style != 0)
                {
                    w.WriteAttributeString("s", style.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                w.WriteStartElement("is", NsMain);
                w.WriteStartElement("t", NsMain);
                w.WriteAttributeString("xml", "space", null, "preserve");
                w.WriteString(CleanForXml(cells[i]));
                w.WriteEndElement();
                w.WriteEndElement();
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }

        private static string ColumnName(int index)
        {
            var sb = new StringBuilder();
            int n = index + 1;
            while (n > 0)
            {
                int rem = (n - 1) % 26;
                sb.Insert(0, (char)('A' + rem));
                n = (n - 1) / 26;
            }
            return sb.ToString();
        }

        /// <summary>移除 XML 1.0 不允許的字元 (含孤立代理字元)，並限制在 Excel 儲存格長度上限內。</summary>
        private static string CleanForXml(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(Math.Min(s.Length, ExcelMaxCellLength));
            for (int i = 0; i < s.Length && sb.Length < ExcelMaxCellLength; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < s.Length && XmlConvert.IsXmlSurrogatePair(s[i + 1], c))
                    {
                        if (sb.Length + 2 > ExcelMaxCellLength)
                        {
                            break;
                        }
                        sb.Append(c).Append(s[i + 1]);
                        i++;
                    }
                }
                else if (XmlConvert.IsXmlChar(c))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
