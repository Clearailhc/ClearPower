// Notification-area icon via Shell_NotifyIcon (no WinForms): version-4 messages, GUID
// identity so the "always show" preference sticks, re-add after Explorer restarts, and
// Shell_NotifyIconGetRect so the popover can anchor to the icon. The icon itself is a
// small bitmap rendered from text (watts / percent / runtime), the Windows stand-in for
// the GNOME top-bar label (indicator.js _updatePanel).
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ClearPower.App
{
    public sealed class TrayIcon : IDisposable
    {
        private const int WM_APP_TRAY = 0x8000 + 1;
        private const int WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_CONTEXTMENU = 0x007B, NIN_SELECT = 0x0400, NIN_KEYSELECT = 0x0401;
        private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
        private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_GUID = 0x20, NIF_SHOWTIP = 0x80;
        private const uint NOTIFYICON_VERSION_4 = 4;
        private static readonly Guid IconGuid = new Guid("b6f3d4c0-4a1c-4f0e-9a6d-7c2b0c3e5a11");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NOTIFYICONIDENTIFIER
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public Guid guidItem;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);
        [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

        private readonly HwndSource _source;
        private readonly uint _taskbarCreated;
        private IntPtr _hIcon;
        private string _text = "";
        private string _tip = "ClearPower";
        private bool _added;
        private bool _lightText;

        /// <summary>Left click / keyboard select.</summary>
        public event Action? Activated;
        /// <summary>Right click (screen coordinates, physical pixels).</summary>
        public event Action<int, int>? ContextMenu;

        public TrayIcon()
        {
            var p = new HwndSourceParameters("ClearPowerTray") { Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000) /* WS_POPUP */, ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */ };
            _source = new HwndSource(p);
            _source.AddHook(WndProc);
            _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
            _lightText = !Theme.SystemLight;
            Add();
        }

        private NOTIFYICONDATA Data(uint flags)
        {
            return new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _source.Handle,
                uID = 1,
                uFlags = flags,
                uCallbackMessage = WM_APP_TRAY,
                hIcon = _hIcon,
                szTip = _tip,
                szInfo = "",
                szInfoTitle = "",
                guidItem = IconGuid,
                uVersion = NOTIFYICON_VERSION_4,
            };
        }

        private void Add()
        {
            if (_hIcon == IntPtr.Zero) Render();
            var d = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_GUID | NIF_SHOWTIP);
            if (!Shell_NotifyIcon(NIM_ADD, ref d))
            {
                // A stale registration with the same GUID (crash) blocks NIM_ADD: delete and retry.
                Shell_NotifyIcon(NIM_DELETE, ref d);
                Shell_NotifyIcon(NIM_ADD, ref d);
            }
            var v = Data(NIF_GUID);
            Shell_NotifyIcon(NIM_SETVERSION, ref v);
            _added = true;
        }

        private void Modify()
        {
            if (!_added) return;
            var d = Data(NIF_ICON | NIF_TIP | NIF_GUID | NIF_SHOWTIP);
            Shell_NotifyIcon(NIM_MODIFY, ref d);
        }

        /// <summary>Short text drawn into the icon ("" = battery glyph) and the hover tooltip.</summary>
        public void Update(string text, string tip)
        {
            var light = !Theme.SystemLight;
            if (text == _text && tip == _tip && light == _lightText) return;
            var rerender = text != _text || light != _lightText;
            _text = text; _tip = tip.Length > 127 ? tip.Substring(0, 127) : tip; _lightText = light;
            if (rerender) Render();
            Modify();
        }

        /// <summary>Screen rectangle of the icon in physical pixels; null when in the overflow flyout.</summary>
        public RECT? IconRect()
        {
            var id = new NOTIFYICONIDENTIFIER { cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), hWnd = _source.Handle, uID = 1, guidItem = IconGuid };
            if (Shell_NotifyIconGetRect(ref id, out var r) == 0 && r.Right > r.Left && r.Bottom > r.Top) return r;
            return null;
        }

        public static POINT Cursor()
        {
            GetCursorPos(out var p);
            return p;
        }

        private void Render()
        {
            var size = Math.Max(16, GetSystemMetrics(49 /* SM_CXSMICON */));
            var scale = size / 16.0;
            using var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                var fg = _lightText ? Color.White : Color.Black;
                var outline = _lightText ? Color.FromArgb(160, 0, 0, 0) : Color.FromArgb(120, 255, 255, 255);
                if (_text == "")
                {
                    // battery glyph
                    using var pen = new Pen(fg, (float)(1.5 * scale));
                    var body = new RectangleF((float)(1.5 * scale), (float)(4.5 * scale), (float)(11 * scale), (float)(7 * scale));
                    g.DrawRectangle(pen, body.X, body.Y, body.Width, body.Height);
                    using var brush = new SolidBrush(fg);
                    g.FillRectangle(brush, (float)(13 * scale), (float)(6.5 * scale), (float)(1.5 * scale), (float)(3 * scale));
                    g.FillRectangle(brush, (float)(3 * scale), (float)(6 * scale), (float)(6 * scale), (float)(4 * scale));
                }
                else
                {
                    var lines = _text.Split('\n');
                    var fontPx = lines.Length > 1 ? 8.5 * scale : (_text.Length > 3 ? 8.0 * scale : 10.5 * scale);
                    using var font = new Font("Segoe UI", (float)fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
                    using var fmt = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    fmt.FormatFlags |= StringFormatFlags.NoWrap;
                    var rect = new RectangleF(0, 0, size, size);
                    using var path = new GraphicsPath();
                    path.AddString(_text, font.FontFamily, (int)FontStyle.Bold, font.Size, rect, fmt);
                    using var outlinePen = new Pen(outline, (float)(2.2 * scale)) { LineJoin = LineJoin.Round };
                    g.DrawPath(outlinePen, path);
                    using var brush = new SolidBrush(fg);
                    g.FillPath(brush, path);
                }
            }
            var old = _hIcon;
            _hIcon = bmp.GetHicon();
            if (old != IntPtr.Zero) DestroyIcon(old);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_APP_TRAY)
            {
                var ev = (int)(lParam.ToInt64() & 0xFFFF);
                var x = (short)(wParam.ToInt64() & 0xFFFF);
                var y = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                switch (ev)
                {
                    case NIN_SELECT:
                    case NIN_KEYSELECT:
                    case WM_LBUTTONUP:
                        Activated?.Invoke();
                        break;
                    case WM_CONTEXTMENU:
                    case WM_RBUTTONUP:
                        ContextMenu?.Invoke(x, y);
                        break;
                }
                handled = true;
            }
            else if (msg == (int)_taskbarCreated)
            {
                _added = false;
                Add();
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_added)
            {
                var d = Data(NIF_GUID);
                Shell_NotifyIcon(NIM_DELETE, ref d);
                _added = false;
            }
            if (_hIcon != IntPtr.Zero) { DestroyIcon(_hIcon); _hIcon = IntPtr.Zero; }
            _source.Dispose();
        }
    }
}
