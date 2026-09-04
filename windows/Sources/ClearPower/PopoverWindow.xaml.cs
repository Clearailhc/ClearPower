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
        public DateTime HiddenAt { get; private set; } = DateTime.MinValue;
        private DateTime _shownAt = DateTime.MinValue;
        private bool _reactivated;

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);

        public PopoverWindow(AppState state)
        {
            InitializeComponent();
            _state = state;
            _appsTimer.Tick += (_, _) => PollApps();
            _contentTimer.Tick += (_, _) => SampleContent();
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
            };
            Sankey.FlowMode = _state.Prefs.FlowAnimation;
            Retext();
        }

        // ---- show / hide ----------------------------------------------------------------
        public void ShowAt(TrayIcon.RECT? iconRect, TrayIcon.POINT cursor)
        {
            _state.Engine.Touch();
            _state.Engine.Poke();
            RefreshAll();
            _shownAt = DateTime.UtcNow;
            _reactivated = false;
            Opacity = 0;
            Show();
            UpdateLayout();
            var src = PresentationSource.FromVisual(this);
            var toDip = src?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var work = SystemParameters.WorkArea;
            double ax, ay; bool above = true;
            if (iconRect != null)
            {
                var r = iconRect.Value;
                var tl = toDip.Transform(new Point(r.Left, r.Top));
                var br = toDip.Transform(new Point(r.Right, r.Bottom));
                ax = (tl.X + br.X) / 2;
                above = tl.Y > work.Top + work.Height / 2;
                ay = above ? tl.Y - 8 : br.Y + 8;
            }
            else
            {
                var c = toDip.Transform(new Point(cursor.X, cursor.Y));
                ax = c.X; ay = c.Y - 8;
            }
            var w = ActualWidth; var h = ActualHeight;
            var left = Math.Max(work.Left + 4, Math.Min(ax - w / 2, work.Right - w - 4));
            var top = above ? ay - h : ay;
            top = Math.Max(work.Top + 4, Math.Min(top, work.Bottom - h - 4));
            Left = left; Top = top;
            Opacity = 1;
            Activate();
            SetForegroundWindow(new WindowInteropHelper(this).Handle);
            Sankey.SetActive(true);
            PollApps();
            _appsTimer.Start();
            SyncContentTimer();
        }

        public void HidePopover()
        {
            if (!IsVisible) return;
            HiddenAt = DateTime.UtcNow;
            Hide();
            Sankey.SetActive(false);
            _appsTimer.Stop();
            SyncContentTimer();
        }

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
            if (snap.I("fan1", -1) > 0) parts.Add($"{snap.I("fan1")} rpm");
            Temps.Text = string.Join(" · ", parts);
        }

        private void PollApps()
        {
            var procs = _state.Engine.GetTopProcesses(3);
            Apps.Children.Clear();
            var sig = procs.Where(p => p.w >= AppMinW).ToList();
            if (sig.Count == 0)
            {
                Apps.Children.Add(new TextBlock { Text = I18n.T("noApps"), HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)FindResource("DimBrush"), FontSize = 12 });
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
    }
}
