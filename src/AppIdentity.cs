using System;
using System.IO;

namespace ClaudeVpnGuard
{
    internal static class AppIdentity
    {
        public const string Name = "ClaudeVpnGuard";
        public const string Version = "1.6";
        public const string ExecutableName = "ClaudeVpnGuard.exe";
        public const string ProcessName = "ClaudeVpnGuard";
        public const string UninstallerName = "Uninstall.exe";
        public const string FirewallGroup = "ClaudeVpnGuard";
        public const string SingleInstanceMutex = @"Global\ClaudeVpnGuard.SingleInstance";
        public const string GitHubUrl = "https://github.com/aokazantsev/ClaudeVpnGuard";
        public const string SiteUrl = "https://aokazantsev.ru/pets/claudevpnguard/";
        public const string AutostartDelay = "PT0S";

        public static string DataDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Name); }
        }

        public static string DefaultInstallDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Name); }
        }
    }
}
