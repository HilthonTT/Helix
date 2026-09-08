#if WINDOWS
using Helix.Application.Abstractions.Desktop;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Helix.Infrastructure.Desktop;

[SupportedOSPlatform("windows")]
internal sealed class WindowsTrayIcon : ITrayIcon, IDisposable
{
    private const uint IconId = 1;

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);

    private readonly ILogger<WindowsTrayIcon> _logger;
    private readonly Lock _gate = new();
    private readonly ManualResetEventSlim _ready = new(false);

    private IReadOnlyList<TrayMenuItem> _menu = [];

    private Thread? _thread;
    private IntPtr _windowHandle;
    private IntPtr _icon;

    private bool _ownsIcon;

    private string? _className;

    private IntPtr _instanceHandle;

    private string _tooltip = string.Empty;
    private bool _iconAdded;
    private bool _disposed;

    private readonly WndProcDelegate _wndProc;

    private uint _taskbarCreatedMessage;

    public WindowsTrayIcon(ILogger<WindowsTrayIcon> logger)
    {
        _logger = logger;
        _wndProc = WindowProcedure;
    }

    public bool IsSupported => true;

    public event EventHandler? Activated;

    public event EventHandler<string>? MenuItemSelected;

    public bool Show(string tooltip)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            _tooltip = tooltip ?? string.Empty;

            if (_thread is null)
            {
                _thread = new Thread(RunMessageLoop)
                {
                    IsBackground = true,
                    Name = "Helix tray icon",
                };

                _thread.SetApartmentState(ApartmentState.STA);

                _ready.Reset();

                _thread.Start();
            }
        }

        if (!_ready.Wait(StartupTimeout))
        {
            _logger.LogWarning("The tray icon window did not come up within the timeout; no icon will be shown.");
            return false;
        }

        lock (_gate)
        {
            if (!_iconAdded)
            {
                AddIcon();
            }
        }

        UpdateTooltip();

        return _iconAdded;
    }

    public void SetMenu(IReadOnlyList<TrayMenuItem> items)
    {
        lock (_gate)
        {
            _menu = items ?? [];
        }
    }

    public void Notify(string title, string message)
    {
        if (_windowHandle == IntPtr.Zero || !_iconAdded)
        {
            return;
        }

        NOTIFYICONDATAW data = CreateIconData();
        data.uFlags = NIF_INFO;
        data.szInfoTitle = Truncate(title, 63);
        data.szInfo = Truncate(message, 255);
        data.dwInfoFlags = NIIF_INFO;

        if (!Shell_NotifyIconW(NIM_MODIFY, ref data))
        {
            _logger.LogWarning("The shell rejected a tray notification.");
        }
    }

    public void Hide()
    {
        Thread? loop;

        lock (_gate)
        {
            if (_windowHandle == IntPtr.Zero)
            {
                return;
            }

            RemoveIcon();

            PostMessageW(_windowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

            _windowHandle = IntPtr.Zero;

            loop = _thread;
            _thread = null;

            _ready.Reset();
        }

        JoinLoop(loop);
    }

    private void JoinLoop(Thread? loop)
    {
        if (loop is null)
        {
            return;
        }

        try
        {
            if (!loop.Join(ShutdownTimeout))
            {
                _logger.LogWarning("The tray icon thread did not finish within the timeout.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "The tray icon thread did not join cleanly.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Hide();

        _ready.Dispose();
    }

    private void RunMessageLoop()
    {
        try
        {
            if (!CreateHiddenWindow())
            {
                return;
            }

            _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
            (_icon, _ownsIcon) = LoadApplicationIcon();

            AddIcon();

            _ready.Set();

            while (GetMessageW(out MSG message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The tray icon message loop faulted; the icon is gone for this session.");
        }
        finally
        {
            bool owner;

            lock (_gate)
            {
                owner = _thread is null || ReferenceEquals(_thread, Thread.CurrentThread);

                if (owner)
                {
                    ReleaseIcon();
                    UnregisterWindowClass();

                    _thread = null;
                }
            }

            if (!owner)
            {
                _logger.LogDebug("A newer tray icon loop has taken over; this one leaves its resources to it.");
            }
            else
            {
                try
                {
                    _ready.Set();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }

    private void ReleaseIcon()
    {
        IntPtr icon = _icon;
        bool owned = _ownsIcon;

        _icon = IntPtr.Zero;
        _ownsIcon = false;

        if (!owned || icon == IntPtr.Zero)
        {
            return;
        }

        if (!DestroyIcon(icon))
        {
            _logger.LogDebug("Could not destroy the tray icon (Win32 error {Error}).", Marshal.GetLastWin32Error());
        }
    }

    private void UnregisterWindowClass()
    {
        string? className = _className;

        _className = null;

        if (className is null)
        {
            return;
        }

        if (!UnregisterClassW(className, _instanceHandle))
        {
            _logger.LogDebug(
                "Could not unregister the tray window class (Win32 error {Error}).",
                Marshal.GetLastWin32Error());
        }
    }

    private bool CreateHiddenWindow()
    {
        string className = $"HelixTrayIcon_{Guid.NewGuid():N}";

        var windowClass = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandleW(null),
            lpszClassName = className,
        };

        if (RegisterClassExW(ref windowClass) == 0)
        {
            _logger.LogError("Could not register the tray window class (Win32 error {Error}).", Marshal.GetLastWin32Error());
            return false;
        }

        _className = className;
        _instanceHandle = windowClass.hInstance;

        _windowHandle = CreateWindowExW(
            0,
            className,
            "Helix",
            WS_OVERLAPPED,
            0, 0, 0, 0,
            IntPtr.Zero,
            IntPtr.Zero,
            windowClass.hInstance,
            IntPtr.Zero);

        if (_windowHandle == IntPtr.Zero)
        {
            _logger.LogError("Could not create the tray window (Win32 error {Error}).", Marshal.GetLastWin32Error());
            return false;
        }

        return true;
    }

    private IntPtr WindowProcedure(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WM_TRAY_CALLBACK)
        {
            switch ((uint)(lParam.ToInt64() & 0xFFFF))
            {
                case WM_LBUTTONUP:
                    Raise(Activated);
                    break;

                case WM_RBUTTONUP:
                case WM_CONTEXTMENU:
                    ShowContextMenu(hWnd);
                    break;
            }

            return IntPtr.Zero;
        }

        if (_taskbarCreatedMessage != 0 && message == _taskbarCreatedMessage)
        {
            lock (_gate)
            {
                _iconAdded = false;
                AddIcon();
            }

            return IntPtr.Zero;
        }

        if (message == WM_CLOSE)
        {
            DestroyWindow(hWnd);
            return IntPtr.Zero;
        }

        if (message == WM_DESTROY)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return DefWindowProcW(hWnd, message, wParam, lParam);
    }

    private void ShowContextMenu(IntPtr hWnd)
    {
        TrayMenuItem[] items;
        lock (_gate)
        {
            items = [.. _menu];
        }

        if (items.Length == 0)
        {
            return;
        }

        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            for (int index = 0; index < items.Length; index++)
            {
                TrayMenuItem item = items[index];

                if (item.IsSeparator)
                {
                    AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
                    continue;
                }

                uint flags = item.IsEnabled ? MF_STRING : MF_STRING | MF_GRAYED;

                AppendMenuW(menu, flags, (UIntPtr)(index + 1), item.Text);
            }

            GetCursorPos(out POINT cursor);

            SetForegroundWindow(hWnd);

            int selected = TrackPopupMenuEx(
                menu,
                TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY,
                cursor.X,
                cursor.Y,
                hWnd,
                IntPtr.Zero);

            PostMessageW(hWnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            if (selected <= 0 || selected > items.Length)
            {
                return;
            }

            TrayMenuItem chosen = items[selected - 1];

            EventHandler<string>? handler = MenuItemSelected;
            if (handler is not null)
            {
                ThreadPool.QueueUserWorkItem(_ => SafeInvoke(() => handler(this, chosen.Id)));
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void AddIcon()
    {
        if (_windowHandle == IntPtr.Zero || _iconAdded)
        {
            return;
        }

        NOTIFYICONDATAW data = CreateIconData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.uCallbackMessage = WM_TRAY_CALLBACK;
        data.hIcon = _icon;
        data.szTip = Truncate(_tooltip, 127);

        _iconAdded = Shell_NotifyIconW(NIM_ADD, ref data);

        if (!_iconAdded)
        {
            _logger.LogError("The shell refused the tray icon (Win32 error {Error}).", Marshal.GetLastWin32Error());
        }
    }

    private void UpdateTooltip()
    {
        if (_windowHandle == IntPtr.Zero || !_iconAdded)
        {
            return;
        }

        NOTIFYICONDATAW data = CreateIconData();
        data.uFlags = NIF_TIP;
        data.szTip = Truncate(_tooltip, 127);

        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    private void RemoveIcon()
    {
        if (!_iconAdded)
        {
            return;
        }

        NOTIFYICONDATAW data = CreateIconData();
        Shell_NotifyIconW(NIM_DELETE, ref data);

        _iconAdded = false;
    }

    private NOTIFYICONDATAW CreateIconData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _windowHandle,
        uID = IconId,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private (IntPtr Icon, bool Owned) LoadApplicationIcon()
    {
        try
        {
            string? executable = Environment.ProcessPath;

            if (!string.IsNullOrEmpty(executable))
            {
                IntPtr extracted = ExtractIconW(GetModuleHandleW(null), executable, 0);

                if (extracted != IntPtr.Zero && extracted != 1)
                {
                    return (extracted, true);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the application icon; falling back to the generic one.");
        }

        return (LoadIconW(IntPtr.Zero, IDI_APPLICATION), false);
    }

    private void Raise(EventHandler? handler)
    {
        if (handler is null)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(_ => SafeInvoke(() => handler(this, EventArgs.Empty)));
    }

    private void SafeInvoke(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A tray icon event handler threw.");
        }
    }

    private static string Truncate(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

    private const uint WM_TRAY_CALLBACK = 0x0400 + 1;
    private const uint WM_NULL = 0x0000;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;

    private const uint WS_OVERLAPPED = 0x00000000;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;

    private const uint NIIF_INFO = 0x00000001;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_GRAYED = 0x00000001;
    private const uint MF_SEPARATOR = 0x00000800;

    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_NONOTIFY = 0x0080;

    private static readonly IntPtr IDI_APPLICATION = 32512;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIconW(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);
}
#endif
