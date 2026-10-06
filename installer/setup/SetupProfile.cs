using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ClaudeVpnGuard
{
    internal static class SetupProfile
    {
        private const string DefaultVpnAdapter = "Fortinet";
        private const string DefaultProbeHost = "example.com";
        private const string VpnAdapterKey = "vpnAdapter";
        private const string ProbeHostKey = "probeHost";
        private const string ProbePortKey = "probePort";

        public const string Intro =
            "Claude (десктоп, Claude Code, расширения редакторов) сможет выходить в сеть только через VPN. "
            + "Выключен VPN — у Claude нет сети вообще. Программа живёт в трее и сама берёт под защиту новые версии Claude.";

        public static string Notice()
        {
            return null;
        }

        public static List<SetupField> Fields()
        {
            AppSettings current = AppSettings.Load();
            var adapter = new SetupField
            {
                Key = VpnAdapterKey,
                Label = "Адаптер VPN — часть его названия",
                Value = current.HasVpnAdapters ? string.Join("|", current.VpnAdapters) : DefaultVpnAdapter,
                Hint = "Claude выходит в сеть только через адаптеры, в названии которых есть этот текст. Несколько — через «|»."
            };
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (!adapter.Suggestions.Contains(networkInterface.Description)) adapter.Suggestions.Add(networkInterface.Description);
            }
            var probeHost = new SetupField
            {
                Key = ProbeHostKey,
                Label = "Корпоративный узел для проверки VPN",
                Value = current.ProbeHost.Length > 0 ? current.ProbeHost : DefaultProbeHost,
                Hint = "Узел, доступный только через VPN. Не отвечает — значок жёлтый. Пусто — не проверяется."
            };
            var probePort = new SetupField
            {
                Key = ProbePortKey,
                Label = "Порт узла",
                Value = current.ProbePort.ToString(CultureInfo.InvariantCulture)
            };
            return new List<SetupField> { adapter, probeHost, probePort };
        }

        public static List<SetupOption> Options()
        {
            return new List<SetupOption>();
        }

        public static void BeforeExtract(InstallRequest request, Action<int, string> report, List<string> notes)
        {
            report(5, "Папка настроек…");
            PrepareDataDirectory();
            AppSettings settings = AppSettings.Load();
            settings.VpnAdapters = AppSettings.ParseList(request.Value(VpnAdapterKey));
            settings.ProbeHost = request.Value(ProbeHostKey);
            int port;
            if (int.TryParse(request.Value(ProbePortKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out port) && port > 0 && port < 65536)
            {
                settings.ProbePort = port;
            }
            else
            {
                settings.ProbePort = AppSettings.DefaultProbePort;
                notes.Add("Порт узла не число от 1 до 65535 — поставлен " + AppSettings.DefaultProbePort + ".");
            }
            if (!settings.TrySave()) throw new InvalidOperationException("Не удалось записать настройки в " + AppSettings.FilePath + ".");
            if (!settings.HasVpnAdapters) notes.Add("Адаптер VPN не указан — у Claude не будет сети, пока не выберешь его в настройках программы.");
        }

        public static void AfterExtract(InstallRequest request, Action<int, string> report, List<string> notes)
        {
        }

        public static void Launch(string executable)
        {
            Process.Start(new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = false });
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
