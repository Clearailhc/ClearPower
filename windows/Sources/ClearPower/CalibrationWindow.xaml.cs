// Full-screen white surface shown while the engine sweeps brightness: maximal, known
// emission so the display's power-vs-brightness curve is measured with high SNR.
// Port of extension/clearpower@lhc/calibrationScreen.js.
using System;
using System.Windows;
using System.Windows.Input;
using ClearPower.Core;

namespace ClearPower.App
{
    public partial class CalibrationWindow : Window
    {
        private readonly Action _onCancel;

        public CalibrationWindow(Action onCancel)
        {
            InitializeComponent();
            _onCancel = onCancel;
            MouseDown += (_, _) => _onCancel();
            KeyDown += (_, e) => { if (e.Key == Key.Escape) _onCancel(); };
        }

        public void UpdateProgress(double progress)
        {
            Label.Text = I18n.T("calibrating", "p", (int)Math.Round(progress * 100)) + "\n" + I18n.T("calibrateHint");
        }
    }
}
