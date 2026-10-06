using System;
using System.IO;

namespace ClaudeVpnGuard
{
    internal static class FirewallCrashMarker
    {
        private static readonly string FilePath = Path.Combine(AppIdentity.DataDirectory, "firewall.pending");

        public static bool Exists()
        {
            return File.Exists(FilePath);
        }

        public static void Set(string step)
        {
            try
            {
                Directory.CreateDirectory(AppIdentity.DataDirectory);
                File.WriteAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + step);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        public static string Read()
        {
            try
            {
                return File.ReadAllText(FilePath).Trim();
            }
            catch (IOException)
            {
                return "";
            }
            catch (UnauthorizedAccessException)
            {
                return "";
            }
        }

        public static void Clear()
        {
            try
            {
                File.Delete(FilePath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
