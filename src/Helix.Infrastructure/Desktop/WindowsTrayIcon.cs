#if WINDOWS
using Helix.Application.Abstractions.Desktop;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Helix.Infrastructure.Desktop;

/// <summary>
/// A Shell_NotifyIcon tray icon, owned by a hidden window on its own thread.
/// </summary>
/// <remarks>
/// Why its own thread and not the UI one: a tray icon is driven entirely by window
/// messages, and the menu it shows is modal — <c>TrackPopupMenuEx</c> does not return
/// until the user picks something or clicks away. Running that on the MAUI dispatcher
/// would freeze the app for as long as the menu is open. A private thread with its own
/// message loop keeps the two independent, and means the icon still answers while the
/// UI thread is busy connecting a drive.
///
/// The window is a real top-level window rather than a message-only one, even though it
/// is never shown. A message-only window cannot be made foreground, and a popup menu
/// whose owner is not foreground does not dismiss when the user clicks elsewhere — it
/// hangs around over everything else until it is clicked directly.
///
/// Every public method may be called from any thread. Shell_NotifyIcon itself is safe to
/// call from one, so only the menu — read on the message-loop thread, written from the
/// caller's — needs the lock.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsTrayIcon : ITrayIcon, IDisposable
{
    private const uint IconId = 1;

    /// <summary>How long <see cref="Show"/> waits for the window and icon to appear.</summary>
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long teardown waits for the message loop to finish. Bounded because a thread
    /// stuck in a modal menu must not hold sign-out or shutdown open.
    /// </summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);

    private readonly ILogger<WindowsTrayIcon> _logger;
    private readonly Lock _gate = new();
    private readonly ManualResetEventSlim _ready = new(false);

    /// <summary>The menu as it will be built the next time the user right-clicks.</summary>
    private IReadOnlyList<TrayMenuItem> _menu = [];

    private Thread? _thread;
    private IntPtr _windowHandle;
    private IntPtr _icon;

    /// <summary>
    /// Whether <see cref="_icon"/> is ours to destroy.
    /// </summary>
    /// <remarks>
    /// The two sources differ: an icon extracted from the executable is a handle this
    /// class allocated and must free, while the generic fallback is a shared system icon
    /// that belongs to Windows and must be left alone. Destroying the latter corrupts it
    /// for every other process that asked for it.
    /// </remarks>
    private bool _ownsIcon;

    /// <summary>
    /// The registered window class, kept so it can be unregistered again. Each run uses a
    /// fresh name, so without this every sign-in would leave one behind for the life of
    /// the process.
    /// </summary>
    private string? _className;

    private IntPtr _instanceHandle;

    private string _tooltip = string.Empty;
    private bool _iconAdded;
    private bool _disposed;

    /// <summary>
    /// Held in a field for as long as the window lives. The window class stores a raw
    /// function pointer, which is not a GC reference — letting the delegate be collected
    /// leaves Windows calling into freed memory the next time a message arrives.
    /// </summary>
    private WndProcDelegate? _wndProc;

    /// <summary>Sent by the shell when Explorer restarts and every tray icon is lost.</summary>
    private uint _taskbarCreatedMessage;

    public WindowsTrayIcon(ILogger<WindowsTrayIcon> logger)
    {
        _logger = logger;
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

                // The menu is modal and the loop is a classic Win32 pump; neither wants
                // an apartment that pumps COM messages behind its back.
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
            }
        }

        // Wait for the window to exist before the first Shell_NotifyIcon call, which
        // needs its handle. Bounded, so a failure to create it cannot hang sign-in.
        if (!_ready.Wait(StartupTimeout))
        {
            _logger.LogWarning("The tray icon window did not come up within the timeout; no icon will be shown.");
            return false;
        }

        UpdateTooltip();

        // The loop sets this before signalling, so the wait above is what makes it safe
        // to read from here. False means the shell refused the icon or the window was
        // never created — either way there is nothing in the tray to click.
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

            // Ends the message loop, which tears the window down on its own thread —
            // DestroyWindow is only legal from the thread that created the window.
            PostMessageW(_windowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

            _windowHandle = IntPtr.Zero;

            loop = _thread;
            _thread = null;

            _ready.Reset();
        }

        // Waited on outside the lock, and that placement is load-bearing: the loop thread
        // takes this same lock to copy the menu when the user right-clicks, so joining
        // while holding it would deadlock the two against each other.
        //
        // Waiting at all is what keeps a sign-out followed straight by a sign-in from
        // running two loops at once. The second would overwrite the icon handle and class
        // name while the first was still draining, and the first would then destroy the
        // second's icon and un-root the delegate its window class still points at.
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
            // A thread that will not come back is not worth blocking sign-out over.
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

        // Hide waits for the loop to finish, so by the time it returns nothing is left to
        // signal on the handle. Without that wait, disposing here raced the thread's own
        // finally: the Set would throw from outside the catch guarding the loop, which is
        // an unhandled exception on a background thread and takes the process down on the
        // way out. The Set is guarded as well, for the case where the join times out.
        Hide();

        _ready.Dispose();
    }

    // --- message loop -----------------------------------------------------

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
            // The tray is a convenience. A failure here must never take the app with it.
            _logger.LogError(ex, "The tray icon message loop faulted; the icon is gone for this session.");
        }
        finally
        {
            // The loop is over, so the window is already gone (WM_DESTROY is what posted
            // the quit message). Everything it allocated goes with it — otherwise each
            // sign-out and back in leaves another icon handle and class registration
            // behind for the life of the process.
            ReleaseIcon();
            UnregisterWindowClass();

            // Let go of the thread slot if this loop is still the one occupying it, so a
            // start that failed can be retried. Hide has usually cleared it already; what
            // this covers is the loop ending by itself — a window that could not be
            // created would otherwise leave the field set and every later Show would
            // decline to start a replacement, with no tray for the rest of the session.
            lock (_gate)
            {
                if (ReferenceEquals(_thread, Thread.CurrentThread))
                {
                    _thread = null;
                }
            }

            // Unblocks anyone still waiting in Show() after a failed start. Guarded: a
            // Dispose that gave up waiting may already have taken the handle away.
            try
            {
                _ready.Set();
            }
            catch (ObjectDisposedException)
            {
                // Shutting down, and nobody is left to wait on it.
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
        _wndProc = null;

        if (className is null)
        {
            return;
        }

        // Safe here and only here: a class cannot be unregistered while a window of it
        // still exists, and by this point the window has been destroyed.
        if (!UnregisterClassW(className, _instanceHandle))
        {
            _logger.LogDebug(
                "Could not unregister the tray window class (Win32 error {Error}).",
                Marshal.GetLastWin32Error());
        }
    }

    private bool CreateHiddenWindow()
    {
        _wndProc = WindowProcedure;

        // Unique per instance: registering a class name twice in one process fails, and
        // a stale registration from a previous sign-in would otherwise collide.
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

        // Remembered so the loop can hand it back when it ends.
        _className = className;
        _instanceHandle = windowClass.hInstance;

        // Never shown: no WS_VISIBLE, and zero size. It exists to own messages and the
        // popup menu, both of which need a real top-level window.
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
            // The mouse message is in the low word of lParam; the icon id is in the high.
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

        // Explorer restarted and threw away every tray icon; put ours back.
        if (_taskbarCreatedMessage != 0 && message == _taskbarCreatedMessage)
        {
            _iconAdded = false;
            AddIcon();

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
            // Command ids are 1-based: TrackPopupMenuEx returns 0 for "dismissed", so 0
            // cannot also mean "the first item".
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

            // Required by the docs: without it the menu does not close when the user
            // clicks away, because this window is not the foreground one.
            SetForegroundWindow(hWnd);

            int selected = TrackPopupMenuEx(
                menu,
                TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY,
                cursor.X,
                cursor.Y,
                hWnd,
                IntPtr.Zero);

            // Also from the docs: gives the menu a chance to finish tidying up.
            PostMessageW(hWnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            if (selected <= 0 || selected > items.Length)
            {
                return;
            }

            TrayMenuItem chosen = items[selected - 1];

            // Handlers run use cases and touch the UI, and this thread is inside a modal
            // menu's aftermath — hand the work off rather than blocking the loop on it.
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

    // --- icon plumbing ----------------------------------------------------

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

    /// <summary>
    /// The icon Explorer already shows for this executable, so the tray matches the
    /// taskbar. Falls back to the generic application icon rather than showing nothing —
    /// an icon-less entry is a blank gap the user cannot click with any confidence.
    /// </summary>
    /// <returns>
    /// The icon, and whether it is ours to destroy — see <see cref="_ownsIcon"/>. The
    /// extracted one is; the shared fallback is not.
    /// </returns>
    private (IntPtr Icon, bool Owned) LoadApplicationIcon()
    {
        try
        {
            string? executable = Environment.ProcessPath;

            if (!string.IsNullOrEmpty(executable))
            {
                IntPtr extracted = ExtractIconW(GetModuleHandleW(null), executable, 0);

                // ExtractIcon returns 1 for "the file has no icons at all".
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

    /// <summary>
    /// Clips to what the fixed-size buffer in NOTIFYICONDATAW holds. Marshalling a
    /// longer string into a ByValTStr field throws, which would turn a long drive name
    /// into a crash.
    /// </summary>
    private static string Truncate(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

    // --- Win32 interop ----------------------------------------------------

    private const uint WM_TRAY_CALLBACK = 0x0400 + 1; // WM_APP + 1
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
