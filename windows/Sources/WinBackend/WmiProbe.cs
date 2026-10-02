// Read-only WMI lookups used to decide whether a vendor's charge interface is present.
//
// Everything here only asks "does this namespace / class / instance exist", so it is safe to run
// on a machine that is not that vendor, and it needs no elevated rights. The vendor backends use
// it to avoid assuming anything: a missing namespace returns false instead of throwing, and a
// denied or unsupported query is reported as missing rather than taken as evidence of presence.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace ClearPower.Win
{
    internal static class WmiProbe
    {
        /// <summary>Cache, because the diagnostics path asks a lot of these in a row.</summary>
        private static readonly Dictionary<string, bool> NamespaceCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, bool> ClassCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static readonly object Gate = new object();

        public static bool NamespaceExists(string ns)
        {
            lock (Gate)
            {
                if (NamespaceCache.TryGetValue(ns, out var hit)) return hit;
                bool ok;
                try
                {
                    var scope = new ManagementScope(ns);
                    scope.Connect();
                    ok = scope.IsConnected;
                }
                catch (Exception) { ok = false; }
                NamespaceCache[ns] = ok;
                return ok;
            }
        }

        public static bool ClassExists(string ns, string className)
        {
            var key = ns + "\\" + className;
            lock (Gate)
            {
                if (ClassCache.TryGetValue(key, out var hit)) return hit;
                bool ok;
                try
                {
                    if (!NamespaceExists(ns)) ok = false;
                    else
                    {
                        using var searcher = new ManagementObjectSearcher(new ManagementScope(ns), new ObjectQuery("SELECT * FROM " + className));
                        using var results = searcher.Get();
                        ok = results.Count > 0;
                    }
                }
                catch (Exception) { ok = false; }
                ClassCache[key] = ok;
                return ok;
            }
        }

        /// <summary>Class names in a namespace, filtered by a substring (null = all). For diagnostics.</summary>
        public static List<string> ListClasses(string ns, string? mustContain = null)
        {
            var list = new List<string>();
            try
            {
                using var searcher = new ManagementObjectSearcher(new ManagementScope(ns), new ObjectQuery("SELECT * FROM meta_class"));
                foreach (ManagementBaseObject o in searcher.Get())
                {
                    var name = (o as ManagementClass)?.ClassPath.ClassName ?? o.ClassPath.ClassName;
                    if (name == null) continue;
                    if (mustContain == null || name.IndexOf(mustContain, StringComparison.OrdinalIgnoreCase) >= 0) list.Add(name);
                }
            }
            catch (Exception) { }
            return list.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Namespaces that exist on this machine, for the diagnostics trace.</summary>
        public static List<string> ListNamespaces()
        {
            var found = new List<string>();
            try
            {
                using var searcher = new ManagementObjectSearcher(new ManagementScope(@"root"), new ObjectQuery("SELECT * FROM __NAMESPACE"));
                foreach (ManagementBaseObject o in searcher.Get())
                    found.Add(o["Name"] as string ?? "");
            }
            catch (Exception) { }
            return found.Where(x => x.Length > 0).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
