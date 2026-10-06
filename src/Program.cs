using System;
using System.Threading;
using System.Windows.Forms;

namespace ClaudeVpnGuard
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool isFirstInstance;
            using (new Mutex(true, AppIdentity.SingleInstanceMutex, out isFirstInstance))
            {
                if (!isFirstInstance) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApplication());
            }
        }
    }
}
