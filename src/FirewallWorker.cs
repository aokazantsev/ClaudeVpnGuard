using System;
using System.Runtime.InteropServices;

namespace ClaudeVpnGuard
{
    internal static class FirewallWorker
    {
        public const string Argument = "/firewall-worker";
        public const int Succeeded = 0;
        public const int BadArguments = 2;

        public static int Run(string[] args)
        {
            if (args.Length != 3) return BadArguments;
            FirewallRequest request = FirewallRequest.Load(args[1]);
            AppLog.TraceEnabled = request.Trace;
            AppLog.Trace("fw worker: " + (request.VerifyOnly ? "verify" : "sync") + ", executables " + request.Executables.Count);
            FirewallSyncResult result;
            try
            {
                result = request.VerifyOnly ? FirewallGuard.Verify(request) : FirewallGuard.Sync(request);
                FirewallGuard.AddPolicyProblems(result);
            }
            catch (COMException error)
            {
                result = Unavailable(error);
            }
            catch (UnauthorizedAccessException error)
            {
                result = Unavailable(error);
            }
            result.Save(args[2]);
            AppLog.Trace("fw worker: done");
            return Succeeded;
        }

        private static FirewallSyncResult Unavailable(Exception error)
        {
            var result = new FirewallSyncResult();
            result.Errors.Add("брандмауэр недоступен: " + error.Message);
            return result;
        }
    }
}
