using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AuditTools.Common
{
    /// <summary>
    /// 由 exe 本身的 Win32 圖示資源 (ApplicationIcon) 設定視窗圖示，不必再內嵌一份 .ico，exe 不會變大。
    /// 透過 LoadImage 由 Windows 解析 ico，支援 PNG 壓縮的圖框 (.NET Framework 的 Icon 類別不支援)，
    /// 並依目前 DPI 取得正確尺寸的大/小圖示。
    /// Sets the window icon from the exe's own Win32 icon resource (no duplicate embedded .ico).
    /// </summary>
    internal static class AppIcon
    {
        private const int IconGroupId = 32512;   // C# 編譯器為 ApplicationIcon 產生的圖示群組 ID
        private const uint IMAGE_ICON = 1;
        private const int SM_CXICON = 11;
        private const int SM_CYICON = 12;
        private const int SM_CXSMICON = 49;
        private const int SM_CYSMICON = 50;
        private const int WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr LoadImage(IntPtr hInst, IntPtr name, uint type, int cx, int cy, uint load);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>於表單建構式呼叫。Call from the form constructor.</summary>
        public static void Apply(Form form)
        {
            IntPtr hInst = Marshal.GetHINSTANCE(typeof(AppIcon).Module);
            if (hInst == IntPtr.Zero || hInst == new IntPtr(-1))
            {
                return;
            }

            IntPtr big = Load(hInst, SM_CXICON, SM_CYICON);
            IntPtr small = Load(hInst, SM_CXSMICON, SM_CYSMICON);

            if (big != IntPtr.Zero)
            {
                form.Icon = Icon.FromHandle(big);
            }

            if (small != IntPtr.Zero)
            {
                // WinForms 會由大圖示縮出小圖示；Load 時改送清晰的原生小尺寸
                form.Load += (s, e) => SendMessage(form.Handle, WM_SETICON, (IntPtr)ICON_SMALL, small);
            }
        }

        private static IntPtr Load(IntPtr hInst, int cxMetric, int cyMetric)
        {
            // 圖示控制代碼在整個程式生命週期內使用，不需釋放
            return LoadImage(hInst, (IntPtr)IconGroupId, IMAGE_ICON, GetSystemMetrics(cxMetric), GetSystemMetrics(cyMetric), 0);
        }
    }
}
