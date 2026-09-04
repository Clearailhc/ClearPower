// The Windows "power mode" slider (overlay scheme), exposed with the same ids the GNOME
// frontend uses for power-profiles-daemon: power-saver | balanced | performance.
// Port of extension/clearpower@lhc/powerProfiles.js. Per-user setting, no privileges.
using System;

namespace ClearPower.Win
{
    public static class PowerMode
    {
        public static readonly Guid BestPowerEfficiency = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");
        public static readonly Guid Balanced = Guid.Empty;
        public static readonly Guid BetterPerformance = new Guid("3af9b8d9-7c97-431d-ad78-34a8bfea439f");  // Windows 10 middle step
        public static readonly Guid BestPerformance = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");

        public static readonly string[] Ids = { "power-saver", "balanced", "performance" };

        /// <summary>Active id, or "" when the API is unavailable (very old builds).</summary>
        public static string Read()
        {
            try
            {
                if (NativeMethods.PowerGetEffectiveOverlayScheme(out var g) != 0) return "";
                if (g == BestPowerEfficiency) return "power-saver";
                if (g == BestPerformance) return "performance";
                return "balanced";
            }
            catch (Exception)
            {
                return "";
            }
        }

        public static bool Set(string id)
        {
            Guid g = id switch
            {
                "power-saver" => BestPowerEfficiency,
                "performance" => BestPerformance,
                _ => Balanced,
            };
            try
            {
                return NativeMethods.PowerSetActiveOverlayScheme(g) == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
