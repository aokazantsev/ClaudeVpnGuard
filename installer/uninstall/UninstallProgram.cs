using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ClaudeVpnGuard
{
    internal static class UninstallProgram
    {
        private const string RemoveCommand = "/remove";
        private const string QuietFlag = "/quiet";
        private const int DeleteAttempts = 20;
        private const int DeleteRetryMs = 500;
        private const string Title = "Удаление " + AppIdentity.Name;

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            bool quiet = Array.IndexOf(args, QuietFlag) >= 0;
            if (args.Length >= 2 && args[0] == RemoveCommand) return Remove(args[1], quiet);
            return Confirm(Path.GetDirectoryName(Application.ExecutablePath), quiet);
        }

        private static int Confirm(string installDirectory, bool quiet)
        {
            if (!quiet)
            {
                DialogResult answer = MessageBox.Show(
                    "Удалить " + AppIdentity.Name + "?" + Environment.NewLine + Environment.NewLine
                    + "Снимутся правила брандмауэра и блок в hosts: Claude снова сможет выходить в сеть без VPN. "
                    + "Удалятся программа, настройки, журнал, автозапуск и запись в «Приложениях».",
                    Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) return 1;
            }
            string worker = Path.Combine(Path.GetTempPath(), AppIdentity.Name + "Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(Application.ExecutablePath, worker, true);
            string arguments = RemoveCommand + " \"" + installDirectory + "\"" + (quiet ? " " + QuietFlag : "");
            Process.Start(new ProcessStartInfo(worker, arguments) { UseShellExecute = false });
            return 0;
        }

        private static int Remove(string installDirectory, bool quiet)
        {
            var problems = new List<string>();
            if (!RunningApp.Stop()) problems.Add(AppIdentity.Name + " не закрылся");
            try
            {
                FirewallGuard.RemoveAll();
            }
            catch (COMException error)
            {
                problems.Add("правила брандмауэра (группа «" + AppIdentity.FirewallGroup + "») не сняты: " + error.Message);
            }
            try
            {
                HostsPinner.Remove();
            }
            catch (IOException error)
            {
                problems.Add("блок в hosts не снят: " + error.Message);
            }
            catch (UnauthorizedAccessException error)
            {
                problems.Add("блок в hosts не снят: " + error.Message);
            }
            if (StartupTask.IsEnabled())
            {
                string startupProblem = StartupTask.Disable();
                if (startupProblem != null) problems.Add(startupProblem);
            }
            UninstallRegistration.Unregister();
            string dataProblem = DeleteWithRetries(AppIdentity.DataDirectory);
            if (dataProblem != null) problems.Add("папка настроек " + dataProblem);
            if (File.Exists(Path.Combine(installDirectory, AppIdentity.ExecutableName)))
            {
                string folderProblem = DeleteWithRetries(installDirectory);
                if (folderProblem != null) problems.Add("папка программы " + folderProblem);
            }

            if (!quiet)
            {
                if (problems.Count == 0)
                {
                    MessageBox.Show(AppIdentity.Name + " удалён, защита снята.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("Удаление закончено не полностью:" + Environment.NewLine + "• " + string.Join(Environment.NewLine + "• ", problems),
                        Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            ScheduleSelfDelete();
            return problems.Count == 0 ? 0 : 2;
        }

        private static string DeleteWithRetries(string directory)
        {
            if (!Directory.Exists(directory)) return null;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    Directory.Delete(directory, true);
                    return null;
                }
                catch (IOException error)
                {
                    if (attempt >= DeleteAttempts) return "удалена не полностью (" + error.Message + ")";
                }
                catch (UnauthorizedAccessException error)
                {
                    if (attempt >= DeleteAttempts) return "удалена не полностью (" + error.Message + ")";
                }
                Thread.Sleep(DeleteRetryMs);
            }
        }

        private static void ScheduleSelfDelete()
        {
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & del /f /q \"" + Application.ExecutablePath + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
    }
}
