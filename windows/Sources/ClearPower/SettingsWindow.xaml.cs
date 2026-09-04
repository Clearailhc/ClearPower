// Preferences window. Port of extension/clearpower@lhc/prefs.js.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ClearPower.Core;
using Microsoft.Win32;

namespace ClearPower.App
{
    public partial class SettingsWindow : Window
    {
        private readonly AppState _state;
        private bool _syncing;
        private readonly DispatcherTimer _limitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        private static readonly string[] PanelNicks = { "watts", "percent", "both", "runtime", "none" };
        private static readonly string[] FlowNicks = { "always", "on-ac", "never" };
        private static readonly string[] LangNicks = { "system", "en", "zh-cn" };
        private static readonly int[] Wins = { 10, 30, 60 };
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public SettingsWindow(AppState state)
        {
            InitializeComponent();
            _state = state;
            // Never taller than the screen: the content scrolls instead.
            MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 40);
            _limitTimer.Tick += (_, _) =>
            {
                _limitTimer.Stop();
                try { _state.Engine.SetChargeLimit((int)Math.Round(LimitSlider.Value)); LLimitSub.Text = I18n.T("prefsLimitSub"); }
                catch (Exception e) { LLimitSub.Text = e.Message; }
            };
            _state.SampleOnUi += OnSample;
            _state.ChargeStateChangedOnUi += SyncLimit;
            _state.LanguageChanged += Retext;
            Closed += (_, _) =>
            {
                _state.SampleOnUi -= OnSample;
                _state.ChargeStateChangedOnUi -= SyncLimit;
                _state.LanguageChanged -= Retext;
            };
            Retext();
        }

        private void Fill(ComboBox cb, string[] nicks, string[] labels, string current)
        {
            _syncing = true;
            cb.Items.Clear();
            foreach (var l in labels) cb.Items.Add(l);
            cb.SelectedIndex = Math.Max(0, Array.IndexOf(nicks, current));
            _syncing = false;
        }

        private void Retext()
        {
            var p = _state.Prefs;
            TTop.Text = I18n.T("winTray");
            LPanelText.Text = I18n.T("winTrayText");
            Fill(PanelText, PanelNicks, new[] { I18n.T("panelWatts"), I18n.T("panelPercent"), I18n.T("panelBoth"), I18n.T("panelRuntime"), I18n.T("panelNone") }, p.PanelText);
            LFlow.Text = I18n.T("prefsFlow"); LFlowSub.Text = I18n.T("prefsFlowSub");
            Fill(Flow, FlowNicks, new[] { I18n.T("flowAlways"), I18n.T("flowOnAc"), I18n.T("flowNever") }, p.FlowAnimation);
            LLang.Text = I18n.T("prefsLanguage");
            Fill(Lang, LangNicks, new[] { I18n.T("langSystem"), I18n.T("langEn"), I18n.T("langZh") }, p.Language);
            LLogin.Text = I18n.T("launchAtLogin");
            Login.IsChecked = LaunchAtLoginEnabled();

            TCharge.Text = I18n.T("prefsCharge");
            LLimit.Text = I18n.T("prefsLimit"); LLimitSub.Text = I18n.T("prefsLimitSub");
            var supported = _state.Engine.Charge.Supported;
            LimitSlider.IsEnabled = supported;
            LChargeStatus.Text = supported ? I18n.T("winChargeOk") : I18n.T("helperMissing");
            LChargeSub.Text = supported ? I18n.T("winChargeMethod") : I18n.T("winChargeMissing");
            SyncLimit();

            TRuntime.Text = I18n.T("prefsRuntime");
            LWin.Text = I18n.T("prefsWindow"); LWinSub.Text = I18n.T("prefsWindowSub");
            _syncing = true;
            Win.Items.Clear();
            foreach (var w in Wins) Win.Items.Add(I18n.T($"win{w}"));
            Win.SelectedIndex = Math.Max(0, Array.IndexOf(Wins, p.RuntimeWindow));
            _syncing = false;

            TDisplay.Text = I18n.T("prefsDisplay");
            LContent.Text = I18n.T("prefsContent"); LContentSub.Text = I18n.T("winContentSub");
            ContentAware.IsChecked = p.ContentAware;
            LCal.Text = I18n.T("prefsCalibrateTitle"); LCalSub.Text = I18n.T("winCalibrateSub");
            CalBtn.Content = I18n.T("prefsCalibrate");
            LEstimate.Text = I18n.T("estimateNote");
            About.Text = I18n.T("aboutVersion", "v", Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "");
            RefreshCal(_state.Snapshot);
        }

        private void SyncLimit()
        {
            _syncing = true;
            LimitSlider.Value = _state.Engine.Charge.Limit;
            LimitValue.Text = $"{_state.Engine.Charge.Limit} %";
            _syncing = false;
        }

        private void OnSample(Dictionary<string, object?> snap) => RefreshCal(snap);

        private void RefreshCal(Dictionary<string, object?>? snap)
        {
            if (snap == null) return;
            var state = snap.S("calib_state");
            if (state == "running")
            {
                CalStatus.Text = I18n.T("calibrating", "p", (int)Math.Round(snap.D("calib_progress", 0) * 100));
                CalBtn.IsEnabled = false;
                CalMessage.Text = "";
                return;
            }
            CalBtn.IsEnabled = true;
            if (snap.B("display_calibrated"))
            {
                var at = snap.D("calibrated_at", 0);
                CalStatus.Text = I18n.T("calibratedOn", "d", at > 0 ? DateTimeOffset.FromUnixTimeSeconds((long)at).ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "–");
            }
            else CalStatus.Text = I18n.T("notCalibrated");
            var msg = snap.S("calib_message");
            CalMessage.Text = msg != "" ? I18n.T("calibFailed", "m", msg) : "";
        }

        // ---- handlers ----
        private void OnPanelText(object s, SelectionChangedEventArgs e) { if (!_syncing && PanelText.SelectedIndex >= 0) _state.Prefs.Set("panel-text", PanelNicks[PanelText.SelectedIndex]); }
        private void OnFlow(object s, SelectionChangedEventArgs e) { if (!_syncing && Flow.SelectedIndex >= 0) _state.Prefs.Set("flow-animation", FlowNicks[Flow.SelectedIndex]); }
        private void OnLang(object s, SelectionChangedEventArgs e) { if (!_syncing && Lang.SelectedIndex >= 0) _state.Prefs.Set("language", LangNicks[Lang.SelectedIndex]); }
        private void OnWin(object s, SelectionChangedEventArgs e) { if (!_syncing && Win.SelectedIndex >= 0) _state.Prefs.Set("runtime-window", Wins[Win.SelectedIndex]); }
        private void OnContent(object s, RoutedEventArgs e) => _state.Prefs.Set("content-aware", ContentAware.IsChecked == true);

        private void OnLimitSlider(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LimitValue == null || _state == null) return;  // fired while XAML is still loading
            LimitValue.Text = $"{(int)Math.Round(LimitSlider.Value)} %";
            if (_syncing) return;
            _limitTimer.Stop();
            _limitTimer.Start();
        }

        private void OnCalibrate(object s, RoutedEventArgs e)
        {
            if (!_state.Engine.CalibrateDisplay(out var message))
                CalMessage.Text = I18n.T("calibFailed", "m", message);
        }

        private void OnLogin(object s, RoutedEventArgs e) => SetLaunchAtLogin(Login.IsChecked == true);

        public static bool LaunchAtLoginEnabled()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey);
                return k?.GetValue("ClearPower") is string;
            }
            catch (Exception) { return false; }
        }

        public static void SetLaunchAtLogin(bool on)
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(RunKey);
                if (on) k?.SetValue("ClearPower", $"\"{Assembly.GetExecutingAssembly().Location}\"");
                else k?.DeleteValue("ClearPower", false);
            }
            catch (Exception) { }
        }
    }
}
