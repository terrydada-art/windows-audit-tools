using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PowerEventViewer
{
    internal static class Program
    {
        private const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDefaultDllDirectories(uint directoryFlags);

        [STAThread]
        private static void Main()
        {
            // 縱深防禦：之後的原生 DLL 只從 System32 載入，避免 exe 所在目錄被放置惡意 DLL (DLL planting)。
            SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnThreadException;

            Application.Run(new MainForm());
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            // 只顯示訊息，不顯示堆疊 (避免把內部細節暴露給使用者畫面/截圖)
            MessageBox.Show(e.Exception.Message, "未預期的錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
