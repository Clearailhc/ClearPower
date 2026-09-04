// WPF application object: single instance, theme, tray icon, popover, calibration screen,
// engine lifecycle. Port of extension.js + ClearPowerApp.swift.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ClearPower.Core;
using ClearPower.Win;
using Microsoft.Win32;

namespace ClearPower.App
{
    public partial class App : Application
    {
        public const string ShowEventName = @"Local\ClearPower.Show";
        public const string QuitEventName = @"Local\ClearPower.Quit";
        private Mutex? _mutex;
        private AppState? _state;
        private TrayIcon? _tray;
        private PopoverWindow? _popover;
        private CalibrationWindow? _calScreen;
        private ContextMenu? _menu;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _mutex = new Mutex(true, @"Local\ClearPower.Tray", out var createdNew);
            if (!createdNew)
            {
                // Another instance owns the tray icon: ask it to open its popover and leave.
                try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch (Exception) { }
                Shutdown();
                return;
            }
            Theme.Refresh();
            _state = new AppState(Dispatcher);
            _popover = new PopoverWindow(_state);
            _tray = new TrayIcon();
            _tray.Activated += OnTrayActivated;
            _tray.ContextMenu += OnTrayContextMenu;
            _calScreen = new CalibrationWindow(() => _state.Engine.CancelCalibration());

            _state.SampleOnUi += OnSample;
            _state.ChargeStateChangedOnUi += () => _popover.SyncState();
            _state.LanguageChanged += () => { _popover.Retext(); UpdateTray(); BuildMenu(); };
            _state.Prefs.Changed += key =>
            {
                if (key == "panel-text") UpdateTray();
                else if (key == "flow-animation") _popover.Sankey.FlowMode = _state.Prefs.FlowAnimation;
                else if (key == "runtime-window") _popover.RefreshRuntime();
                else if (key == "content-aware") _popover.SyncContentTimer();
            };
            SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() => { Theme.Refresh(); UpdateTray(); }));
            SystemEvents.PowerModeChanged += (_, a) => { if (a.Mode == PowerModes.Resume) _state.Engine.OnResume(); };
            SystemEvents.SessionEnding += (_, _) => Quit();
            Theme.Changed += () => _popover?.Sankey.Invalidate();

            ListenFor(ShowEventName, () => Dispatcher.BeginInvoke(new Action(OpenPopover)));
            ListenFor(QuitEventName, () => Dispatcher.BeginInvoke(new Action(Quit)));
            BuildMenu();
            _state.Engine.Start();
            if (ShotPath != null) RunShot(ShotPath);
        }

        /// <summary>Dev aid: --shot file.png renders the popover (and settings) off-screen and exits.</summary>
        public static string? ShotPath;

        private void RunShot(string path)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                try
                {
                    var p = _popover!;
                    p.Deactivated -= null;
                    p.ShowAt(null, new TrayIcon.POINT { X = 400, Y = 400 });
                    p.UpdateLayout();
                    Save(p, path);
                    var s = new SettingsWindow(_state!) { WindowStartupLocation = WindowStartupLocation.Manual, Left = 10, Top = 10 };
                    s.Show();
                    s.UpdateLayout();
                    Save(s, System.IO.Path.ChangeExtension(path, null) + "-settings.png");
                }
                catch (Exception e) { _state?.Log($"shot failed: {e}"); }
                Quit();
            };
            t.Start();
        }

        private static void Save(Window w, string path)
        {
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(w);
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(w.ActualWidth * dpi.DpiScaleX), (int)(w.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(w);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var f = System.IO.File.Create(path);
            enc.Save(f);
        }

        private static void ListenFor(string name, Action action)
        {
            var ev = new EventWaitHandle(false, EventResetMode.AutoReset, name);
            var t = new Thread(() => { while (true) { ev.WaitOne(); action(); } }) { IsBackground = true };
            t.Start();
        }

        private void OnTrayActivated()
        {
            if (_popover == null) return;
            if (_popover.IsVisible) { _popover.HidePopover(); return; }
            // Clicking the icon deactivates (hides) the popover first; do not immediately reopen it.
            if ((DateTime.UtcNow - _popover.HiddenAt).TotalMilliseconds < 250) return;
            OpenPopover();
        }

        private void OpenPopover()
        {
            if (_popover == null || _tray == null) return;
            _popover.ShowAt(_tray.IconRect(), TrayIcon.Cursor());
        }

        private void BuildMenu()
        {
            _menu = new ContextMenu();
            var settings = new MenuItem { Header = I18n.T("settings") };
            settings.Click += (_, _) => _state?.OpenSettings();
            var about = new MenuItem { Header = I18n.T("aboutVersion", "v", typeof(App).Assembly.GetName().Version?.ToString(3) ?? ""), IsEnabled = false };
            var quit = new MenuItem { Header = I18n.T("quit") };
            quit.Click += (_, _) => Quit();
            _menu.Items.Add(settings);
            _menu.Items.Add(about);
            _menu.Items.Add(new Separator());
            _menu.Items.Add(quit);
        }

        private void OnTrayContextMenu(int x, int y)
        {
            if (_menu == null || _popover == null) return;
            _popover.HidePopover();
            _menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            _menu.IsOpen = true;
        }

        private void OnSample(Dictionary<string, object?> snap)
        {
            UpdateTray();
            _popover?.OnSample(snap);
            var running = snap.S("calib_state") == "running";
            if (running)
            {
                _popover?.HidePopover();
                if (_calScreen != null && !_calScreen.IsVisible) _calScreen.Show();
                _calScreen?.UpdateProgress(snap.D("calib_progress", 0));
            }
            else if (_calScreen != null && _calScreen.IsVisible)
            {
                _calScreen.Hide();
            }
            _popover?.SyncContentTimer();
        }

        /// <summary>Tray icon text + tooltip (indicator.js _updatePanel).</summary>
        private void UpdateTray()
        {
            if (_tray == null || _state == null) return;
            var snap = _state.Snapshot;
            if (snap == null) { _tray.Update("", "ClearPower"); return; }
            var mode = _state.Prefs.PanelText;
            var w = I18n.FmtW(snap.D("sys_w"), 1);
            var p = $"{snap.I("bat_pct", 0)}%";
            var rt = p;
            var win = _state.Prefs.RuntimeWindow;
            if (snap.S("bat_status") == "Discharging")
            {
                var m = snap.D($"runtime_min_{win}");
                if (m > 0) rt = I18n.FmtDuration(m);
            }
            var wShort = w == "–" ? "–" : w.Replace(" W", "");
            var rtShort = rt;
            if (snap.S("bat_status") == "Discharging" && snap.D($"runtime_min_{win}") > 0)
            {
                var m = (int)Math.Round(snap.D($"runtime_min_{win}"));
                rtShort = m >= 60 ? $"{m / 60}:{m % 60:D2}" : $"{m}m";
            }
            var text = mode switch
            {
                "watts" => wShort,
                "percent" => p,
                "both" => $"{wShort}\n{p}",
                "runtime" => rtShort,
                "none" => "",
                _ => wShort,
            };
            var approx = snap.S("sys_source") == "estimate" ? "≈" : "";
            var tip = $"ClearPower · {approx}{w} · {p}";
            if (snap.S("bat_status") == "Discharging" && rt != p) tip += " · " + I18n.T("remaining", "t", rt);
            _tray.Update(text, tip);
        }

        private void Quit()
        {
            try
            {
                _state?.Engine.Stop();
                _state?.Engine.Dispose();
            }
            catch (Exception) { }
            _tray?.Dispose();
            _tray = null;
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            base.OnExit(e);
        }
    }
}
