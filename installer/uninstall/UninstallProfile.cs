using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace ClaudeVpnGuard
{
    internal static class UninstallProfile
    {
        public const string ConfirmDetails =
            "Снимутся правила брандмауэра и блок в hosts: Claude снова сможет выходить в сеть без VPN.";

        public static void Remove(List<string> problems)
        {
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
        }
    }
}
