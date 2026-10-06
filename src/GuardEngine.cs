using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace ClaudeVpnGuard
{
    internal sealed class GuardEngine
    {
        private const int DnsTimeoutMs = 2000;
        private const int LeakTicksBeforeAlarm = 2;
        private static readonly TimeSpan FullSyncInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan DnsRefreshInterval = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan DnsRetryInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan ConnectGrace = TimeSpan.FromSeconds(15);

        private readonly Action<Action> post;
        private readonly Action changed;
        private AppSettings settings;
        private Dictionary<string, string> pins;
        private string lastSignature;
        private DateTime lastFullSyncUtc = DateTime.MinValue;
        private DateTime nextDnsRefreshUtc = DateTime.MinValue;
        private DateTime lastProbeUtc = DateTime.MinValue;
        private bool dnsRefreshRunning;
        private bool probeRunning;
        private bool? probeOk;
        private bool wasVpnUp;
        private DateTime vpnUpSinceUtc = DateTime.MinValue;
        private bool firstEvaluation = true;
        private int leakTicks;

        public GuardEngine(AppSettings settings, Action<Action> post, Action changed)
        {
            this.settings = settings;
            this.post = post;
            this.changed = changed;
            pins = SafeReadPins();
        }

        public void Reconfigure(AppSettings newSettings)
        {
            settings = newSettings;
            lastSignature = null;
            nextDnsRefreshUtc = DateTime.MinValue;
            lastProbeUtc = DateTime.MinValue;
            probeOk = null;
        }

        public GuardReport Evaluate(bool forceFullSync)
        {
            var report = new GuardReport();
            DateTime now = DateTime.UtcNow;
            if (!IsElevated())
            {
                report.Status = GuardStatus.Broken;
                report.Headline = "Нет прав администратора — защита не обслуживается";
                report.Problems.Add(report.Headline);
                return report;
            }

            AdapterSnapshot adapters = AdapterInventory.Read(settings);
            SortedSet<string> executables = ClaudeExecutables.Find(settings);
            Dictionary<int, string> running = ClaudeProcesses.Running(executables);
            foreach (string path in running.Values)
            {
                if (executables.Add(path)) report.Events.Add("Найден запущенный Claude вне известных папок: " + path);
            }
            report.Executables.AddRange(executables);

            SyncFirewall(report, executables, adapters, forceFullSync, now);
            NetworkAdapter vpn = adapters.ActiveVpn;
            TrackVpn(report, vpn, now);
            RefreshPins(vpn, now);
            WritePins(report);
            AuditConnections(report, running, adapters);
            StartProbe(vpn, now);
            Classify(report, adapters, vpn, now);
            firstEvaluation = false;
            return report;
        }

        private void SyncFirewall(GuardReport report, SortedSet<string> executables, AdapterSnapshot adapters, bool forceFullSync, DateTime now)
        {
            string signature = string.Join("|", executables) + "#" + string.Join("|", adapters.BlockedInterfaceNames(false));
            try
            {
                bool inputsChanged = signature != lastSignature;
                List<string> drift = inputsChanged ? null : FirewallGuard.Verify(executables);
                bool due = forceFullSync || inputsChanged || now - lastFullSyncUtc > FullSyncInterval || drift.Count > 0;
                if (due)
                {
                    FirewallSyncResult result = FirewallGuard.Sync(executables, adapters);
                    lastSignature = signature;
                    lastFullSyncUtc = now;
                    if (result.Added > 0 && !firstEvaluation) report.Events.Add("Под защиту взято программ: " + result.Added);
                    if (result.Repaired > 0 && !inputsChanged) report.Events.Add("Правила брандмауэра были изменены извне и восстановлены: " + result.Repaired);
                    if (result.PresentAdaptersOnly) report.Details.Add("Правила знают только подключённые сейчас адаптеры");
                    foreach (string error in result.Errors) report.Problems.Add("Брандмауэр: " + error);
                }
                report.Problems.AddRange(FirewallGuard.PolicyProblems());
            }
            catch (COMException error)
            {
                lastSignature = null;
                report.Problems.Add("Брандмауэр недоступен: " + error.Message);
            }
            catch (UnauthorizedAccessException error)
            {
                lastSignature = null;
                report.Problems.Add("Брандмауэр недоступен: " + error.Message);
            }
        }

        private void TrackVpn(GuardReport report, NetworkAdapter vpn, DateTime now)
        {
            bool vpnUp = vpn != null;
            if (vpnUp && !wasVpnUp)
            {
                vpnUpSinceUtc = firstEvaluation ? DateTime.MinValue : now;
                nextDnsRefreshUtc = DateTime.MinValue;
                lastProbeUtc = DateTime.MinValue;
                probeOk = null;
                if (!firstEvaluation) report.Events.Add("VPN подключён");
            }
            if (!vpnUp && wasVpnUp) report.Events.Add("VPN отключён — у Claude нет сети");
            wasVpnUp = vpnUp;
        }

        private void RefreshPins(NetworkAdapter vpn, DateTime now)
        {
            if (vpn == null || dnsRefreshRunning || now < nextDnsRefreshUtc || vpn.DnsServers.Count == 0) return;
            dnsRefreshRunning = true;
            var hosts = new List<string>(settings.PinnedHosts);
            var servers = new List<IPAddress>(vpn.DnsServers);
            int vpnIndex = vpn.Ipv4Index;
            ThreadPool.QueueUserWorkItem(state =>
            {
                Dictionary<string, string> resolved = ResolveThroughVpn(hosts, servers, vpnIndex);
                post(() =>
                {
                    dnsRefreshRunning = false;
                    bool complete = resolved.Count == hosts.Count;
                    nextDnsRefreshUtc = DateTime.UtcNow + (complete ? DnsRefreshInterval : DnsRetryInterval);
                    foreach (KeyValuePair<string, string> pair in resolved) pins[pair.Key] = pair.Value;
                    changed();
                });
            });
        }

        private static Dictionary<string, string> ResolveThroughVpn(List<string> hosts, List<IPAddress> servers, int vpnIndex)
        {
            var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string host in hosts)
            {
                foreach (IPAddress server in servers)
                {
                    List<IPAddress> answers;
                    try
                    {
                        answers = DnsResolver.QueryA(server, host, DnsTimeoutMs);
                    }
                    catch (SocketException)
                    {
                        continue;
                    }
                    if (answers.Count == 0) continue;
                    IPAddress routed = answers.Find(address => RouteInspector.BestInterfaceIndex(address) == vpnIndex);
                    resolved[host] = routed == null ? HostsPinner.BlackholeAddress : routed.ToString();
                    break;
                }
            }
            return resolved;
        }

        private void WritePins(GuardReport report)
        {
            try
            {
                if (HostsPinner.Apply(settings.PinnedHosts, pins) && !firstEvaluation) report.Events.Add("Блок в hosts обновлён");
            }
            catch (IOException error)
            {
                report.Problems.Add("Файл hosts не записывается: " + error.Message);
            }
            catch (UnauthorizedAccessException error)
            {
                report.Problems.Add("Файл hosts не записывается: " + error.Message);
            }
        }

        private void AuditConnections(GuardReport report, Dictionary<int, string> running, AdapterSnapshot adapters)
        {
            ConnectionAudit audit = ConnectionAuditor.Audit(running, adapters, true);
            if (audit.Leaks.Count == 0)
            {
                leakTicks = 0;
                return;
            }
            leakTicks++;
            report.Events.Add("Соединения Claude мимо VPN: " + string.Join(", ", audit.Leaks) + (audit.Closed > 0 ? " — закрыто " + audit.Closed : ""));
            if (leakTicks >= LeakTicksBeforeAlarm) report.Problems.Add("Claude открывает соединения мимо VPN: " + audit.Leaks[0]);
        }

        private void StartProbe(NetworkAdapter vpn, DateTime now)
        {
            if (vpn == null || probeRunning || settings.ProbeHost.Length == 0 || now - lastProbeUtc < ProbeInterval) return;
            probeRunning = true;
            lastProbeUtc = now;
            CorporateProbe.CheckAsync(settings.ProbeHost, settings.ProbePort, ok => post(() =>
            {
                probeRunning = false;
                bool wasOk = probeOk.HasValue && probeOk.Value;
                probeOk = ok;
                if (ok != wasOk) changed();
            }));
        }

        private void Classify(GuardReport report, AdapterSnapshot adapters, NetworkAdapter vpn, DateTime now)
        {
            report.Details.Insert(0, "Программ под защитой: " + report.Executables.Count);
            if (report.Problems.Count > 0)
            {
                report.Status = GuardStatus.Broken;
                report.Headline = report.Problems[0];
                return;
            }
            if (!settings.HasVpnAdapters || !adapters.HasVpnAdapter)
            {
                report.Status = GuardStatus.Warning;
                report.Headline = "Адаптер VPN не выбран — у Claude нет сети. Открой настройки";
                return;
            }
            if (vpn == null)
            {
                report.Status = GuardStatus.Offline;
                report.Headline = "VPN выключен — у Claude нет сети";
                return;
            }
            report.Details.Add("VPN: " + vpn.Name + " (" + vpn.Description + ")");
            var warnings = new List<string>();
            if (settings.ProbeHost.Length > 0)
            {
                string probe = !probeOk.HasValue ? "проверяю" : probeOk.Value ? "отвечает" : "не отвечает";
                report.Details.Add(settings.ProbeHost + ": " + probe);
                if (probeOk.HasValue && !probeOk.Value) warnings.Add(settings.ProbeHost + " не отвечает — VPN подключён не полностью");
            }
            foreach (string host in settings.CriticalHosts)
            {
                string address;
                bool known = pins.TryGetValue(host, out address);
                if (!known || address == HostsPinner.BlackholeAddress)
                {
                    report.Details.Add(host + ": нет адреса через VPN");
                    warnings.Add(dnsRefreshRunning || !known ? "Узнаю адреса Claude у DNS VPN…" : host + " недоступен через VPN");
                    continue;
                }
                bool routed = adapters.IsVpnInterface(RouteInspector.BestInterfaceIndex(IPAddress.Parse(address)));
                report.Details.Add(host + " → " + address + (routed ? " (через VPN)" : " (мимо VPN)"));
                if (!routed) warnings.Add(host + " идёт мимо VPN и заблокирован");
            }
            if (warnings.Count > 0 && now - vpnUpSinceUtc < ConnectGrace)
            {
                report.Status = GuardStatus.Offline;
                report.Headline = "VPN подключается…";
                return;
            }
            if (warnings.Count > 0)
            {
                report.Status = GuardStatus.Warning;
                report.Headline = warnings[0];
                return;
            }
            report.Status = GuardStatus.Protected;
            report.Headline = "Claude работает только через VPN";
        }

        private static Dictionary<string, string> SafeReadPins()
        {
            try
            {
                return HostsPinner.ReadPins();
            }
            catch (IOException)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch (UnauthorizedAccessException)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static bool IsElevated()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
    }
}
