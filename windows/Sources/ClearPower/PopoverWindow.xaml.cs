// The popover: header (limit / discharge / top up / settings), battery bar, runtime line,
// health, Sankey, power modes, apps. Port of extension/clearpower@lhc/indicator.js.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClearPower.Core;
using ClearPower.Win;

namespace ClearPower.App
{
    public partial class PopoverWindow : Window
    {
        private static readonly int[] Limits = { 80, 90, 100 };     // one click cycles through these
        private static readonly int[] Windows = { 10, 30, 60 };     // runtime averaging windows (minutes)
        private const double AppMinW = 0.5;
        private const int ContentIntervalS = 5;

        private readonly AppState _state;
        private readonly DispatcherTimer _appsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        private readonly DispatcherTimer _contentTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ContentIntervalS) };
        /// <summary>Display reconfiguration arrives as a burst of messages; act once it settles.</summary>
        private readonly DispatcherTimer _layoutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        public DateTime HiddenAt { get; private set; } = DateTime.MinValue;
        private DateTime _shownAt = DateTime.MinValue;
        private bool _reactivated;
        private HwndSource? _source;
        /// <summary>Where the popover is anchored, in physical screen pixels, kept so it can be
        /// re-placed when the display configuration or the window's DPI scale changes.</summary>
        private TrayIcon.RECT? _anchorRect;
        private TrayIcon.POINT _anchorCursor;
        private bool _positioned;

        private const int WM_DISPLAYCHANGE = 0x007E;

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);

        public PopoverWindow(AppState state)
        {
            InitializeComponent();
            _state = state;
            _appsTimer.Tick += (_, _) => PollApps();
            _contentTimer.Tick += (_, _) => SampleContent();
            _layoutTimer.Tick += (_, _) => { _layoutTimer.Stop(); Place(); };
            SizeChanged += (_, _) => { if (IsVisible) Place(); };
            LayoutUpdated += (_, _) => { if (IsVisible && !_positioned) Place(); };
            Deactivated += (_, _) =>
            {
                // Explorer sometimes takes the foreground back right after the tray click;
                // re-assert once within the first half second, hide on any later deactivation.
                if ((DateTime.UtcNow - _shownAt).TotalMilliseconds < 500 && !_reactivated)
                {
                    _reactivated = true;
                    Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) { Activate(); SetForegroundWindow(new WindowInteropHelper(this).Handle); } }));
                    return;
                }
                HidePopover();
            };
            KeyDown += (_, e) => { if (e.Key == Key.Escape) HidePopover(); };
            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int round = 2; DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, 4);
                int dark = Theme.AppsLight ? 0 : 1; DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, 4);
                _source = HwndSource.FromHwnd(hwnd);
                _source?.AddHook(WndProc);
            };
            Sankey.FlowMode = _state.Prefs.FlowAnimation;
            Retext();
        }

        /// <summary>Adding, removing or re-scaling a display invalidates the work area and the scale
        /// cached for the anchor screen, so re-resolve both once the burst of messages settles.</summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DISPLAYCHANGE) { _layoutTimer.Stop(); _layoutTimer.Start(); }
            return IntPtr.Zero;
        }

        // ---- show / hide ----------------------------------------------------------------
        public void ShowAt(TrayIcon.RECT? iconRect, TrayIcon.POINT cursor)
        {
            _state.Engine.Touch();
            _state.Engine.Poke();
            RefreshAll();
            _shownAt = DateTime.UtcNow;
            _reactivated = false;
            _anchorRect = iconRect;
            _anchorCursor = cursor;
            _positioned = false;
            Opacity = 0;
            Show();
            UpdateLayout();
            Place();
            Opacity = 1;
            Activate();
            SetForegroundWindow(new WindowInteropHelper(this).Handle);
            Sankey.SetActive(true);
            PollApps();
            _appsTimer.Start();
            SyncContentTimer();
        }

        /// <summary>
        /// Place the popover against its anchor using the work area of *the monitor the anchor is
        /// on*, converted at that monitor's scale. Re-entrant on purpose: it runs again once
        /// SizeToContent has settled the real height, and again after a display change, so the
        /// window can never end up clamped to the primary monitor's box or left off-screen.
        /// </summary>
        private void Place()
        {
            if (!IsVisible) return;
            double w = ActualWidth, h = ActualHeight;
            if (double.IsNaN(w) || double.IsNaN(h) || w <= 1 || h <= 1) return;   // not measured yet

            // Anchor in physical pixels: the tray icon when we have it, else the click point.
            LayoutRect anchor;
            if (_anchorRect != null)
            {
                var r = _anchorRect.Value;
                anchor = new LayoutRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            }
            else
            {
                anchor = new LayoutRect(_anchorCursor.X, _anchorCursor.Y, 0, 0);
            }

            var wa = Monitors.ForPoint(anchor.X + anchor.W / 2, anchor.Y + anchor.H / 2);
            var scale = wa.Fallback ? 1.0 : wa.Scale;
            var work = wa.Fallback ? PhysicalPrimaryWorkArea() : wa.Dip;

            var target = WindowGeometry.PlacementForPhysicalAnchor(anchor, w, h, work, scale);
            _positioned = true;
            // LayoutUpdated runs often; only touch the window when the placement actually changed.
            if (Math.Abs(Left - target.X) > 0.5 || Math.Abs(Top - target.Y) > 0.5)
            {
                Left = target.X;
                Top = target.Y;
            }
        }

        /// <summary>Last resort when Windows will not answer: the primary monitor's work area, at
        /// whatever scale WPF is currently laying this window out in.</summary>
        private LayoutRect PhysicalPrimaryWorkArea()
        {
            var src = PresentationSource.FromVisual(this);
            var dip = src?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var work = SystemParameters.WorkArea;
            var tl = dip.Transform(new Point(work.Left, work.Top));
            var br = dip.Transform(new Point(work.Right, work.Bottom));
            return new LayoutRect(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y);
        }

        public void HidePopover()
        {
            if (ShotMode || !IsVisible) return;
            HiddenAt = DateTime.UtcNow;
            Hide();
            Sankey.SetActive(false);
            _appsTimer.Stop();
            SyncContentTimer();
        }

        /// <summary>--shot mode: keep the window up even though it never gets focus.</summary>
        public bool ShotMode { get; set; }

        // ---- charge control -------------------------------------------------------------
        private void OnCycleLimit(object sender, RoutedEventArgs e)
        {
            var cur = _state.Engine.Charge.Limit;
            var i = Array.IndexOf(Limits, cur);
            var next = Limits[(i + 1) % Limits.Length];
            LimitBtn.Content = I18n.T("limit", "n", next);
            Try(() => _state.Engine.SetChargeLimit(next));
        }

        private void OnToggleDischarge(object sender, RoutedEventArgs e)
        {
            var eng = _state.Engine;
            Try(() => { if (eng.Charge.Mode == ChargeMode.Discharge) eng.CancelSpecial(); else eng.StartDischarge(0); });
        }

        private void OnToggleTopUp(object sender, RoutedEventArgs e)
        {
            var eng = _state.Engine;
            Try(() => { if (eng.Charge.Mode == ChargeMode.Topup) eng.CancelSpecial(); else eng.StartTopUp(); });
        }

        private void Try(Action a)
        {
            try { a(); }
            catch (Exception ex) { _state.Notify(ex); }
            SyncState();
        }

        private void OnOpenSettings(object sender, RoutedEventArgs e)
        {
            HidePopover();
            _state.OpenSettings();
        }

        private void OnProfile(object sender, RoutedEventArgs e)
        {
            var id = (sender as FrameworkElement)?.Tag as string ?? "balanced";
            PowerMode.Set(id);
            _state.Engine.Poke();
            SyncProfiles(id);
        }

        // ---- runtime window ---------------------------------------------------------------
        private int Window() => Windows.Contains(_state.Prefs.RuntimeWindow) ? _state.Prefs.RuntimeWindow : 30;
        private string WindowText() => I18n.T($"win{Window()}");

        private void OnCycleWindow(object sender, RoutedEventArgs e)
        {
            var i = Array.IndexOf(Windows, Window());
            _state.Prefs.Set("runtime-window", Windows[(i + 1) % Windows.Length]);
            WindowBtn.Content = WindowText();
            RefreshRuntime();
        }

        public void RefreshRuntime(Dictionary<string, object?>? snap = null)
        {
            snap ??= _state.Snapshot;
            WindowBtn.Content = WindowText();
            if (snap == null) return;
            var w = Window();
            var limit = _state.Engine.Charge.Limit;
            string text = "";
            var status = snap.S("bat_status");
            if (snap.S("calib_state") == "running")
                text = I18n.T("calibrating", "p", (int)Math.Round(snap.D("calib_progress", 0) * 100));
            else if (status == "Discharging")
            {
                var m = snap.D($"runtime_min_{w}");
                text = m > 0 ? ((snap.I("runtime_basis_s", 0) < 300 ? "~" : "") + I18n.T("remaining", "t", I18n.FmtDuration(m))) : I18n.T("estimating");
            }
            else if (status == "Charging")
            {
                var m = snap.D($"eta_min_{w}");
                text = m > 0 ? I18n.T("toLimit", "t", I18n.FmtDuration(m), "n", limit) : I18n.T("charging");
            }
            else if (snap.B("on_ac"))
                text = snap.I("bat_pct", 0) >= limit - 1 ? I18n.T("atLimit") : I18n.T("pluggedIn");
            Runtime.Text = text;
            WindowBtn.Visibility = (status == "Discharging" || status == "Charging") ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---- screen content sampling (OLED display estimate) ------------------------------
        private bool ContentWanted()
        {
            if (!_state.Prefs.ContentAware) return false;
            return IsVisible || _state.Snapshot?.S("calib_state") == "running";
        }

        public void SyncContentTimer()
        {
            var want = ContentWanted();
            if (want && !_contentTimer.IsEnabled) { SampleContent(); _contentTimer.Start(); }
            else if (!want && _contentTimer.IsEnabled) _contentTimer.Stop();
        }

        private void SampleContent()
        {
            var apl = ScreenLuminance.Sample();
            if (apl >= 0) _state.Engine.SetDisplayContent(apl);
        }

        // ---- sync ------------------------------------------------------------------------
        public void Retext()
        {
            Offline.Text = I18n.T("daemonOffline");
            ChargeBannerText.Text = I18n.T("winChargeMissing");
            WindowBtn.Content = WindowText();
            ProfSaver.ToolTip = I18n.T("powerSaver");
            ProfBalanced.ToolTip = I18n.T("powerBalanced");
            ProfPerf.ToolTip = I18n.T("powerPerformance");
            Sankey.Invalidate();
            SyncState();
            RefreshAll();
            PollApps();
        }

        public void SyncState()
        {
            var c = _state.Engine.Charge;
            var supported = c.Supported;
            LimitBtn.Content = I18n.T("limit", "n", c.Limit);
            LimitBtn.IsEnabled = c.Mode == ChargeMode.Limit && supported;
            LimitBtn.Opacity = LimitBtn.IsEnabled ? 1 : 0.6;
            LimitBtn.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
            TopUpBtn.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
            DischargeBtn.Visibility = c.DischargeSupported ? Visibility.Visible : Visibility.Collapsed;
            ChargeBanner.Visibility = supported ? Visibility.Collapsed : Visibility.Visible;
            DischargeBtn.IsChecked = c.Mode == ChargeMode.Discharge;
            TopUpBtn.IsChecked = c.Mode == ChargeMode.Topup;
            DischargeText.Text = c.Mode == ChargeMode.Discharge ? I18n.T("dischargingTo", "n", c.Target) : I18n.T("discharge");
            TopUpText.Text = c.Mode == ChargeMode.Topup ? I18n.T("toppingUp") : I18n.T("topUp");
            Bar.Update(limit: c.Limit, mode: c.Mode.Raw());
            RefreshRuntime();
        }

        private void SyncProfiles(string? active = null)
        {
            active ??= _state.Snapshot?.S("platform_profile") ?? "";
            ProfSaver.IsChecked = active == "power-saver";
            ProfBalanced.IsChecked = active == "balanced";
            ProfPerf.IsChecked = active == "performance";
            ProfRow.Visibility = active == "" ? Visibility.Collapsed : Visibility.Visible;
        }

        public void OnSample(Dictionary<string, object?> snap)
        {
            if (IsVisible) RefreshAll(snap);
        }

        public void RefreshAll(Dictionary<string, object?>? snap = null)
        {
            snap ??= _state.Snapshot;
            if (snap == null)
            {
                Offline.Visibility = Visibility.Visible;
                return;
            }
            Offline.Visibility = Visibility.Collapsed;
            Bar.Update(pct: snap.I("bat_pct", 0), status: snap.S("bat_status"), onAc: snap.B("on_ac"));
            Sankey.Update(snap);
            RefreshRuntime(snap);
            SyncProfiles();
            if (snap.D("bat_design_wh", 0) > 0)
            {
                Health.Text = I18n.T("health", new Dictionary<string, object>
                {
                    ["p"] = (int)Math.Round(100 * snap.D("bat_full_wh", 0) / snap.D("bat_design_wh", 1)),
                    ["full"] = snap.D("bat_full_wh", 0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                    ["design"] = snap.D("bat_design_wh", 0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                    ["n"] = snap.I("cycle_count", 0),
                });
                Health.Visibility = Visibility.Visible;
            }
            else Health.Visibility = Visibility.Collapsed;
            var parts = new List<string>();
            if (snap.D("temp_cpu") >= 0) parts.Add($"CPU {Math.Round(snap.D("temp_cpu"))}°");
            if (snap.D("temp_gpu") >= 0) parts.Add($"GPU {Math.Round(snap.D("temp_gpu"))}°");
            if (snap.D("temp_nvme") >= 0) parts.Add($"SSD {Math.Round(snap.D("temp_nvme"))}°");
            // 0 = measured stopped, -1 = unknown (nothing sampling / no sensor): show the former.
            if (snap.I("fan1", -1) >= 0) parts.Add($"{snap.I("fan1")} rpm");
            Temps.Text = string.Join(" · ", parts);
        }

        private void PollApps()
        {
            var procs = _state.Engine.GetTopProcesses(3);
            Apps.Children.Clear();
            // Three distinct states, exactly as on macOS: without per-block energy counters there is
            // no CPU power to attribute at all, so claiming "no app is using significant energy"
            // would be a lie. -1 means unknown/unavailable, never zero.
            if ((_state.Snapshot?.D("cpu_w") ?? -1) < 0)
            {
                Apps.Children.Add(AppsMessage(I18n.T("appPowerUnavailable")));
                return;
            }
            var sig = procs.Where(p => p.w >= AppMinW).ToList();
            if (sig.Count == 0)
            {
                Apps.Children.Add(AppsMessage(I18n.T("noApps")));
                return;
            }
            foreach (var (name, w, _) in sig)
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var n = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis };
                var v = new TextBlock { Text = I18n.FmtW(w), Foreground = (Brush)FindResource("DimBrush") };
                Grid.SetColumn(v, 1);
                row.Children.Add(n); row.Children.Add(v);
                Apps.Children.Add(row);
            }
        }

        private TextBlock AppsMessage(string text) => new TextBlock
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("DimBrush"),
            FontSize = 12,
        };
    }
}
