// Shared application state: the engine, preferences, the latest snapshot (on the UI
// thread), notifications and window plumbing. Port of macos AppState.swift.
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ClearPower.Core;
using ClearPower.Win;

namespace ClearPower.App
{
    public sealed class AppState
    {
        public Engine Engine { get; }
        public Prefs Prefs { get; }
        public Dictionary<string, object?>? Snapshot { get; private set; }
        public event Action<Dictionary<string, object?>>? SampleOnUi;
        public event Action? ChargeStateChangedOnUi;
        public event Action? LanguageChanged;
        private readonly Dispatcher _dispatcher;
        private SettingsWindow? _settings;
        private readonly object _logGate = new object();

        public AppState(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            Prefs = Prefs.Load();
            I18n.SetLanguage(Prefs.Language);
            Engine = new Engine(chargeHardware: ChargeBackends.Detect(Log)) { Log = Log };
            Engine.Sample += snap => _dispatcher.BeginInvoke(new Action(() =>
            {
                Snapshot = snap;
                SampleOnUi?.Invoke(snap);
            }));
            Engine.ChargeStateChanged += () => _dispatcher.BeginInvoke(new Action(() => ChargeStateChangedOnUi?.Invoke()));
            Prefs.Changed += key =>
            {
                if (key == "language")
                {
                    I18n.SetLanguage(Prefs.Language);
                    LanguageChanged?.Invoke();
                }
            };
        }

        public void Log(string line)
        {
            try
            {
                lock (_logGate)
                {
                    var path = Paths.LogPath;
                    if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.WriteAllText(path, "");
                    File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}\n");
                }
            }
            catch (Exception) { }
        }

        /// <summary>Error from a charge/calibration action: same wording as the GNOME frontend.</summary>
        public void Notify(Exception e)
        {
            Log($"action failed: {e.Message}");
            var msg = e.Message;
            if (e is ChargeException ce && ce.Errno == 95) msg = I18n.T("errUnsupported");
            else if (msg.IndexOf("not supported", StringComparison.OrdinalIgnoreCase) >= 0) msg = I18n.T("errUnsupported");
            else if (msg.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0) msg = I18n.T("errPermission");
            MessageBox.Show(msg, "ClearPower", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public void OpenSettings()
        {
            if (_settings == null || !_settings.IsLoaded)
            {
                _settings = new SettingsWindow(this);
                _settings.Closed += (_, _) => _settings = null;
            }
            _settings.Show();
            _settings.Activate();
        }
    }
}
