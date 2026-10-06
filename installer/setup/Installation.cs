using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ClaudeVpnGuard
{
    internal sealed class Installation
    {
        public const string PayloadResource = "ClaudeVpnGuard.Payload.zip";

        private readonly string presetPath;
        private readonly bool applyPreset;
        private readonly bool enableStartup;
        private readonly Action<int, string> report;
        private readonly List<string> warnings = new List<string>();

        public Installation(string presetPath, bool applyPreset, bool enableStartup, Action<int, string> report)
        {
            this.presetPath = presetPath;
            this.applyPreset = applyPreset;
            this.enableStartup = enableStartup;
            this.report = report;
        }

        public List<string> Warnings
        {
            get { return warnings; }
        }

        public void Run()
        {
            string target = AppIdentity.DefaultInstallDirectory;
            report(0, "Останавливаю запущенный " + AppIdentity.Name + "…");
            if (!RunningApp.Stop()) throw new InvalidOperationException(AppIdentity.Name + " не закрывается. Закрой его в трее и повтори установку.");

            report(5, "Распаковка файлов…");
            long size = ExtractPayload(target);

            report(80, "Папка настроек…");
            PrepareDataDirectory();
            if (applyPreset && presetPath != null)
            {
                File.Copy(presetPath, AppSettings.FilePath, true);
            }

            report(88, "Запись в «Приложения» Windows…");
            UninstallRegistration.Register(target, size);

            string executable = Path.Combine(target, AppIdentity.ExecutableName);
            if (enableStartup)
            {
                report(93, "Автозапуск при входе в Windows…");
                string problem = StartupTask.Enable(executable);
                if (problem != null) warnings.Add(problem);
            }
            else if (StartupTask.IsEnabled())
            {
                StartupTask.Disable();
            }

            report(97, "Запуск…");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable) { WorkingDirectory = target, UseShellExecute = false });
            report(100, "Готово.");
        }

        private long ExtractPayload(string target)
        {
            Directory.CreateDirectory(target);
            string root = target.TrimEnd('\\') + "\\";
            long totalBytes = 0;
            using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource))
            {
                if (payload == null) throw new InvalidOperationException("В установщике нет архива программы — он собран неправильно.");
                using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
                {
                    int index = 0;
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        index++;
                        string relative = entry.FullName.Replace('/', '\\');
                        string destination = Path.GetFullPath(Path.Combine(target, relative));
                        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                        if (relative.EndsWith("\\"))
                        {
                            Directory.CreateDirectory(destination);
                            continue;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        entry.ExtractToFile(destination, true);
                        totalBytes += entry.Length;
                        report(5 + index * 70 / archive.Entries.Count, "Распаковка: " + relative);
                    }
                }
            }
            return totalBytes;
        }

        private static void PrepareDataDirectory()
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.CreateDirectory(AppIdentity.DataDirectory);
            Directory.SetAccessControl(AppIdentity.DataDirectory, security);
        }
    }
}
