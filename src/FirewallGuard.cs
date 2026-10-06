using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ClaudeVpnGuard
{
    internal static class FirewallGuard
    {
        private const int DirectionOut = 2;
        private const int ActionBlock = 0;
        private const int ProtocolAny = 256;
        private const int ProfilesAll = 0x7FFFFFFF;
        private const int ModifyStateGroupPolicyOverride = 1;

        private static List<string> appliedInterfaces = new List<string>();
        private static bool presentAdaptersOnly;

        public static string RuleName(string executable)
        {
            return AppIdentity.Name + ": " + executable;
        }

        public static FirewallSyncResult Sync(ICollection<string> executables, AdapterSnapshot adapters)
        {
            var result = new FirewallSyncResult();
            AppLog.Trace("fw: opening policy");
            dynamic policy = OpenPolicy();
            AppLog.Trace("fw: reading rules");
            dynamic rules = policy.Rules;
            Dictionary<string, dynamic> existing = OwnRules(rules);
            AppLog.Trace("fw: own rules " + existing.Count);

            List<string> presentInterfaces = adapters.BlockedInterfaceNames(true);
            List<string> interfaces = presentAdaptersOnly ? presentInterfaces : adapters.BlockedInterfaceNames(false);
            var desiredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string executable in executables)
            {
                string name = RuleName(executable);
                desiredNames.Add(name);
                dynamic rule;
                bool isNew = !existing.TryGetValue(name, out rule);
                if (!isNew && Matches(rule, executable, interfaces)) continue;
                AppLog.Trace("fw: " + (isNew ? "adding" : "repairing") + " rule for " + executable + ", interfaces " + interfaces.Count);
                try
                {
                    if (isNew)
                    {
                        rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", true));
                        rule.Name = name;
                    }
                    rule.Description = "Claude выходит в сеть только через VPN. Создано " + AppIdentity.Name + ", не править вручную.";
                    rule.ApplicationName = executable;
                    rule.Protocol = ProtocolAny;
                    rule.Direction = DirectionOut;
                    rule.Action = ActionBlock;
                    rule.Profiles = ProfilesAll;
                    rule.Grouping = AppIdentity.FirewallGroup;
                    if (!TrySetInterfaces(rule, interfaces))
                    {
                        if (presentAdaptersOnly || !TrySetInterfaces(rule, presentInterfaces))
                        {
                            result.Errors.Add("не удалось задать список адаптеров для " + executable);
                            continue;
                        }
                        presentAdaptersOnly = true;
                        interfaces = presentInterfaces;
                    }
                    rule.Enabled = true;
                    if (isNew)
                    {
                        AppLog.Trace("fw: rules.Add");
                        rules.Add(rule);
                        result.Added++;
                    }
                    else
                    {
                        result.Repaired++;
                    }
                }
                catch (COMException error)
                {
                    result.Errors.Add(executable + ": " + error.Message);
                }
                catch (UnauthorizedAccessException error)
                {
                    result.Errors.Add(executable + ": " + error.Message);
                }
            }

            foreach (string name in existing.Keys)
            {
                if (desiredNames.Contains(name)) continue;
                try
                {
                    rules.Remove(name);
                    result.Removed++;
                }
                catch (COMException error)
                {
                    result.Errors.Add("не удалось убрать устаревшее правило «" + name + "»: " + error.Message);
                }
            }
            result.PresentAdaptersOnly = presentAdaptersOnly;
            appliedInterfaces = interfaces;
            AppLog.Trace("fw: sync done, added " + result.Added + ", repaired " + result.Repaired + ", removed " + result.Removed + ", errors " + result.Errors.Count);
            return result;
        }

        public static List<string> Verify(ICollection<string> executables)
        {
            var problems = new List<string>();
            dynamic rules = OpenPolicy().Rules;
            foreach (string executable in executables)
            {
                dynamic rule;
                try
                {
                    rule = rules.Item(RuleName(executable));
                }
                catch (COMException)
                {
                    problems.Add("нет правила для " + executable);
                    continue;
                }
                catch (System.IO.FileNotFoundException)
                {
                    problems.Add("нет правила для " + executable);
                    continue;
                }
                if (!Matches(rule, executable, appliedInterfaces)) problems.Add("правило для " + executable + " изменено");
            }
            return problems;
        }

        public static List<string> PolicyProblems()
        {
            var problems = new List<string>();
            AppLog.Trace("fw: reading policy state");
            object policy = OpenPolicy();
            Type type = policy.GetType();
            object state = type.InvokeMember("LocalPolicyModifyState", BindingFlags.GetProperty, null, policy, null);
            if (state is int && (int)state == ModifyStateGroupPolicyOverride)
            {
                problems.Add("групповая политика игнорирует локальные правила брандмауэра");
            }
            return problems;
        }

        public static int RemoveAll()
        {
            dynamic rules = OpenPolicy().Rules;
            int removed = 0;
            foreach (string name in OwnRules(rules).Keys)
            {
                try
                {
                    rules.Remove(name);
                    removed++;
                }
                catch (COMException)
                {
                }
            }
            return removed;
        }

        private static dynamic OpenPolicy()
        {
            return Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", true));
        }

        private static Dictionary<string, dynamic> OwnRules(dynamic rules)
        {
            var own = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);
            foreach (dynamic rule in rules)
            {
                string grouping = rule.Grouping;
                if (!string.Equals(grouping, AppIdentity.FirewallGroup, StringComparison.Ordinal)) continue;
                own[(string)rule.Name] = rule;
            }
            return own;
        }

        private static bool Matches(dynamic rule, string executable, List<string> interfaces)
        {
            try
            {
                string application = rule.ApplicationName;
                return (bool)rule.Enabled
                    && (int)rule.Action == ActionBlock
                    && (int)rule.Direction == DirectionOut
                    && (int)rule.Protocol == ProtocolAny
                    && ((int)rule.Profiles & ProfilesAll) == ProfilesAll
                    && string.Equals(application, executable, StringComparison.OrdinalIgnoreCase)
                    && SameInterfaces(rule.Interfaces, interfaces);
            }
            catch (COMException)
            {
                return false;
            }
        }

        private static bool SameInterfaces(object actual, List<string> expected)
        {
            var actualNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var array = actual as Array;
            if (array != null)
            {
                foreach (object item in array)
                {
                    if (item != null) actualNames.Add(item.ToString());
                }
            }
            return actualNames.SetEquals(expected);
        }

        private static bool TrySetInterfaces(dynamic rule, List<string> names)
        {
            try
            {
                object[] values = new object[names.Count];
                for (int i = 0; i < names.Count; i++) values[i] = names[i];
                rule.Interfaces = values;
                return true;
            }
            catch (COMException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
