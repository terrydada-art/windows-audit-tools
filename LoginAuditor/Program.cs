using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace LoginAuditor
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
            // 程式以系統管理員權限執行：限制之後的原生 DLL 只從 System32 載入，
            // 避免 exe 放在可寫入目錄 (如「下載」) 時被同目錄的惡意 DLL 劫持 (DLL planting)。
            SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnThreadException;

            if (!IsAdministrator())
            {
                MessageBox.Show("請關閉程式，按右鍵選擇「以系統管理員身分執行」。", "權限不足",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Application.Run(new MainForm());
        }

        private static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (System.Security.SecurityException)
            {
                return false;
            }
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            // 只顯示訊息，不顯示堆疊 (避免把內部細節暴露給使用者畫面/截圖)
            MessageBox.Show(e.Exception.Message, "未預期的錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
