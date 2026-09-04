// User preferences (%LOCALAPPDATA%\ClearPower\settings.json). Keys mirror the GNOME
// gschema nicks so the three ports read alike.
using System;
using System.Collections.Generic;
using System.IO;
using ClearPower.Core;
using ClearPower.Win;

namespace ClearPower.App
{
    public sealed class Prefs
    {
        public string PanelText { get; set; } = "watts";        // watts | percent | both | runtime | none
        public string FlowAnimation { get; set; } = "on-ac";    // always | on-ac | never
        public bool ShowPanelIcon { get; set; } = false;
        public string Language { get; set; } = "system";        // system | en | zh-cn
        public int RuntimeWindow { get; set; } = 30;            // 10 | 30 | 60
        public bool ContentAware { get; set; } = true;
        public bool LaunchAtLogin { get; set; } = true;

        public event Action<string>? Changed;

        public static Prefs Load()
        {
            var p = new Prefs();
            try
            {
                if (File.Exists(Paths.SettingsPath))
                {
                    var d = Json.ParseObject(File.ReadAllText(Paths.SettingsPath));
                    p.PanelText = d.S("panel-text", p.PanelText);
                    p.FlowAnimation = d.S("flow-animation", p.FlowAnimation);
                    p.ShowPanelIcon = d.B("show-panel-icon", p.ShowPanelIcon);
                    p.Language = d.S("language", p.Language);
                    p.RuntimeWindow = d.I("runtime-window", p.RuntimeWindow);
                    p.ContentAware = d.B("content-aware", p.ContentAware);
                    p.LaunchAtLogin = d.B("launch-at-login", p.LaunchAtLogin);
                }
            }
            catch (Exception) { }
            if (p.RuntimeWindow != 10 && p.RuntimeWindow != 30 && p.RuntimeWindow != 60) p.RuntimeWindow = 30;
            return p;
        }

        public void Save()
        {
            try
            {
                var d = new Dictionary<string, object?>
                {
                    ["panel-text"] = PanelText, ["flow-animation"] = FlowAnimation, ["show-panel-icon"] = ShowPanelIcon,
                    ["language"] = Language, ["runtime-window"] = RuntimeWindow, ["content-aware"] = ContentAware,
                    ["launch-at-login"] = LaunchAtLogin,
                };
                File.WriteAllText(Paths.SettingsPath, Json.Serialize(d, pretty: true));
            }
            catch (Exception) { }
        }

        public void Set(string key, object value)
        {
            switch (key)
            {
                case "panel-text": PanelText = (string)value; break;
                case "flow-animation": FlowAnimation = (string)value; break;
                case "show-panel-icon": ShowPanelIcon = (bool)value; break;
                case "language": Language = (string)value; break;
                case "runtime-window": RuntimeWindow = (int)value; break;
                case "content-aware": ContentAware = (bool)value; break;
                case "launch-at-login": LaunchAtLogin = (bool)value; break;
            }
            Save();
            Changed?.Invoke(key);
        }
    }
}
