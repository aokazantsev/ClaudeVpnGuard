using System.Collections.Generic;

namespace ClaudeVpnGuard
{
    internal sealed class FirewallSyncResult
    {
        public int Added;
        public int Repaired;
        public int Removed;
        public bool PresentAdaptersOnly;
        public readonly List<string> Errors = new List<string>();

        public bool Changed
        {
            get { return Added + Repaired + Removed > 0; }
        }
    }
}
