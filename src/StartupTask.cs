using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace ClaudeVpnGuard
{
    internal static class StartupTask
    {
        public const string TaskName = AppIdentity.Name;

        private const int CommandTimeoutMs = 15000;

        public static bool IsEnabled()
        {
            return RunSchtasks("/Query /TN \"" + TaskName + "\"") == 0;
        }

        public static string Enable(string executablePath)
        {
            string definitionPath = Path.Combine(Path.GetTempPath(), TaskName + "-task-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                File.WriteAllText(definitionPath, Definition(executablePath), Encoding.Unicode);
                return Explain(RunSchtasks("/Create /TN \"" + TaskName + "\" /XML \"" + definitionPath + "\" /F"), "включить автозапуск");
            }
            finally
            {
                File.Delete(definitionPath);
            }
        }

        public static string Disable()
        {
            return Explain(RunSchtasks("/Delete /TN \"" + TaskName + "\" /F"), "выключить автозапуск");
        }

        public static string Run()
        {
            return Explain(RunSchtasks("/Run /TN \"" + TaskName + "\""), "запустить " + AppIdentity.Name);
        }

        private static string Definition(string executablePath)
        {
            string user;
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                user = SecurityElement.Escape(identity.Name);
            }
            string command = SecurityElement.Escape(executablePath);
            string directory = SecurityElement.Escape(Path.GetDirectoryName(executablePath));
            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n"
                + "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n"
                + "  <RegistrationInfo><Description>" + AppIdentity.Name + ": Claude выходит в сеть только через VPN</Description></RegistrationInfo>\n"
                + "  <Triggers>\n"
                + "    <LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId></LogonTrigger>\n"
                + "  </Triggers>\n"
                + "  <Principals>\n"
                + "    <Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>\n"
                + "  </Principals>\n"
                + "  <Settings>\n"
                + "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\n"
                + "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\n"
                + "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\n"
                + "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\n"
                + "    <AllowHardTerminate>true</AllowHardTerminate>\n"
                + "    <StartWhenAvailable>true</StartWhenAvailable>\n"
                + "    <RestartOnFailure><Interval>PT1M</Interval><Count>10</Count></RestartOnFailure>\n"
                + "    <Enabled>true</Enabled>\n"
                + "  </Settings>\n"
                + "  <Actions Context=\"Author\">\n"
                + "    <Exec><Command>\"" + command + "\"</Command><WorkingDirectory>" + directory + "</WorkingDirectory></Exec>\n"
                + "  </Actions>\n"
                + "</Task>\n";
        }

        private static int RunSchtasks(string arguments)
        {
            var start = new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            try
            {
                using (Process process = Process.Start(start))
                {
                    process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    return process.WaitForExit(CommandTimeoutMs) ? process.ExitCode : -1;
                }
            }
            catch (Win32Exception)
            {
                return -1;
            }
        }

        private static string Explain(int exitCode, string action)
        {
            if (exitCode == 0) return null;
            return "Не удалось " + action + " (schtasks, код " + exitCode + ").";
        }
    }
}
