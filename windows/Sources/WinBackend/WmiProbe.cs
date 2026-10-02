// Read-only WMI lookups used to decide whether a vendor's charge interface is present, and to
// report what a machine exposes when it is not covered yet.
//
// Everything here only asks "does this namespace / class / instance exist, and what does it offer",
// so it is safe to run on a machine that is not that vendor and needs no elevated rights. The vendor
// backends use it to avoid assuming anything: a missing namespace returns false instead of throwing,
// and a denied or unsupported query is treated as absent rather than as evidence of presence.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace ClearPower.Win
{
    /// <summary>One property or method of a WMI class, for the diagnostics report.</summary>
    public sealed class WmiMember
    {
        public string Name { get; }
        public string Type { get; }
        public bool IsMethod { get; }
        public WmiMember(string name, string type, bool isMethod) { Name = name; Type = type; IsMethod = isMethod; }
        public override string ToString() => IsMethod ? $"{Name}({Type})" : $"{Name}: {Type}";
    }

    internal static class WmiProbe
    {
        /// <summary>Cached, because the diagnostics path asks a lot of these in a row.</summary>
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

        /// <summary>Every name in a class list, first match wins. Backends pass a candidate list.</summary>
        public static string? FirstPresentClass(string ns, params string[] candidates)
        {
            foreach (var c in candidates)
                if (ClassExists(ns, c)) return c;
            return null;
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

        /// <summary>
        /// The properties and methods of a class. This is what makes an uncovered machine
        /// actionable: it shows exactly what a vendor's provider offers, so the charge setting can
        /// be named instead of guessed.
        /// </summary>
        public static List<WmiMember> DescribeClass(string ns, string className)
        {
            var members = new List<WmiMember>();
            try
            {
                using var cls = new ManagementClass(new ManagementScope(ns), new ManagementPath(className), null);
                foreach (PropertyData p in cls.Properties)
                {
                    if (p.Name == "__PATH") continue;
                    members.Add(new WmiMember(p.Name, p.Type.ToString(), false));
                }
                foreach (MethodData m in cls.Methods)
                {
                    var args = string.Join(", ", m.InParameters?.Properties.Cast<PropertyData>()
                        .Select(a => a.Name + ": " + a.Type.ToString()) ?? Enumerable.Empty<string>());
                    members.Add(new WmiMember(m.Name, args, true));
                }
            }
            catch (Exception) { }
            return members;
        }

        /// <summary>Namespaces that exist under root, for the diagnostics trace.</summary>
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
