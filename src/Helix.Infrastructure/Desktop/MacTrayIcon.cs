#if MACCATALYST
using Foundation;
using Helix.Application.Abstractions.Desktop;
using Microsoft.Extensions.Logging;
using ObjCRuntime;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using UserNotifications;

namespace Helix.Infrastructure.Desktop;

[SupportedOSPlatform("maccatalyst")]
internal sealed class MacTrayIcon : ITrayIcon
{
    private const string LibObjC = "/usr/lib/libobjc.dylib";

    private const double VariableLength = -1;

    private const string SymbolName = "externaldrive.connected.to.line.below";

    private const string FallbackTitle = "Helix";

    private static readonly IntPtr ClickedSelector = Selector.GetHandle("helixItemClicked:");

    private readonly ILogger<MacTrayIcon> _logger;
    private readonly Lock _gate = new();
    private readonly MenuTarget _target;
    private readonly ForegroundPresenter _presenter = new();

    private IReadOnlyList<TrayMenuItem> _menu = [];
    private List<string> _ids = [];
    private IntPtr _statusItem;
    private string _tooltip = string.Empty;
    private bool _authorizationRequested;
    private bool _notificationsAllowed;

    public MacTrayIcon(ILogger<MacTrayIcon> logger)
    {
        _logger = logger;
        _target = new MenuTarget(OnClicked);
    }

    public bool IsSupported => true;

    public event EventHandler? Activated
    {
        add { }
        remove { }
    }

    public event EventHandler<string>? MenuItemSelected;

    public bool Show(string tooltip)
    {
        lock (_gate)
        {
            _tooltip = tooltip ?? string.Empty;
        }

        bool shown = false;

        OnMainThread(() =>
        {
            if (_statusItem == IntPtr.Zero && !CreateStatusItem())
            {
                return;
            }

            ApplyTooltip();
            ApplyMenu();

            shown = true;
        });

        return shown;
    }

    public void SetMenu(IReadOnlyList<TrayMenuItem> items)
    {
        lock (_gate)
        {
            _menu = items ?? [];
        }

        OnMainThread(() =>
        {
            if (_statusItem != IntPtr.Zero)
            {
                ApplyMenu();
            }
        });
    }

    public void Notify(string title, string message)
    {
        try
        {
            UNUserNotificationCenter center = UNUserNotificationCenter.Current;

            bool askFirst;

            lock (_gate)
            {
                askFirst = !_authorizationRequested;
                _authorizationRequested = true;
            }

            if (askFirst)
            {
                center.Delegate ??= _presenter;

                center.RequestAuthorization(
                    UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound,
                    (granted, error) =>
                    {
                        _notificationsAllowed = granted;

                        if (!granted)
                        {
                            _logger.LogInformation(
                                "Notifications are not allowed for Helix; tray messages will not be shown. {Reason}",
                                error?.LocalizedDescription);
                            return;
                        }

                        Post(center, title, message);
                    });

                return;
            }

            if (_notificationsAllowed)
            {
                Post(center, title, message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not post a notification.");
        }
    }

    public void Hide()
    {
        OnMainThread(() =>
        {
            if (_statusItem == IntPtr.Zero)
            {
                return;
            }

            IntPtr statusBar = SendIntPtr(Class.GetHandle("NSStatusBar"), Selector.GetHandle("systemStatusBar"));

            if (statusBar != IntPtr.Zero)
            {
                SendVoid(statusBar, Selector.GetHandle("removeStatusItem:"), _statusItem);
            }

            SendVoid(_statusItem, Selector.GetHandle("release"));

            _statusItem = IntPtr.Zero;
        });
    }

    private bool CreateStatusItem()
    {
        IntPtr statusBarClass = Class.GetHandle("NSStatusBar");
        if (statusBarClass == IntPtr.Zero)
        {
            _logger.LogWarning("AppKit's status bar is not available; no menu bar icon will be shown.");
            return false;
        }

        IntPtr statusBar = SendIntPtr(statusBarClass, Selector.GetHandle("systemStatusBar"));
        if (statusBar == IntPtr.Zero)
        {
            return false;
        }

        IntPtr item = SendIntPtr(statusBar, Selector.GetHandle("statusItemWithLength:"), VariableLength);
        if (item == IntPtr.Zero)
        {
            return false;
        }

        _statusItem = SendIntPtr(item, Selector.GetHandle("retain"));

        IntPtr button = Button();
        if (button == IntPtr.Zero)
        {
            return true;
        }

        IntPtr image = SymbolImage();

        if (image != IntPtr.Zero)
        {
            SendVoid(image, Selector.GetHandle("setTemplate:"), (byte)1);
            SendVoid(button, Selector.GetHandle("setImage:"), image);
        }
        else
        {
            using var title = new NSString(FallbackTitle);
            SendVoid(button, Selector.GetHandle("setTitle:"), title.Handle);
        }

        return true;
    }

    private static IntPtr SymbolImage()
    {
        IntPtr imageClass = Class.GetHandle("NSImage");
        IntPtr selector = Selector.GetHandle("imageWithSystemSymbolName:accessibilityDescription:");

        if (imageClass == IntPtr.Zero || SendByte(imageClass, Selector.GetHandle("respondsToSelector:"), selector) == 0)
        {
            return IntPtr.Zero;
        }

        using var name = new NSString(SymbolName);
        using var description = new NSString(FallbackTitle);

        return SendIntPtr(imageClass, selector, name.Handle, description.Handle);
    }

    private IntPtr Button() =>
        _statusItem == IntPtr.Zero ? IntPtr.Zero : SendIntPtr(_statusItem, Selector.GetHandle("button"));

    private void ApplyTooltip()
    {
        IntPtr button = Button();
        if (button == IntPtr.Zero)
        {
            return;
        }

        string tooltip;

        lock (_gate)
        {
            tooltip = _tooltip;
        }

        using var text = new NSString(tooltip);
        SendVoid(button, Selector.GetHandle("setToolTip:"), text.Handle);
    }

    private void ApplyMenu()
    {
        IReadOnlyList<TrayMenuItem> items;

        lock (_gate)
        {
            items = _menu;
        }

        var ids = new List<string>();

        IntPtr menu = NewMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        AppendItems(menu, items, ids);

        lock (_gate)
        {
            _ids = ids;
        }

        SendVoid(_statusItem, Selector.GetHandle("setMenu:"), menu);
        SendVoid(menu, Selector.GetHandle("release"));
    }

    private static IntPtr NewMenu()
    {
        IntPtr menuClass = Class.GetHandle("NSMenu");
        if (menuClass == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr menu = SendIntPtr(SendIntPtr(menuClass, Selector.GetHandle("alloc")), Selector.GetHandle("init"));

        SendVoid(menu, Selector.GetHandle("setAutoenablesItems:"), (byte)0);

        return menu;
    }

    private void AppendItems(IntPtr menu, IReadOnlyList<TrayMenuItem> items, List<string> ids)
    {
        IntPtr itemClass = Class.GetHandle("NSMenuItem");
        IntPtr addItem = Selector.GetHandle("addItem:");

        foreach (TrayMenuItem item in items)
        {
            if (item.IsSeparator)
            {
                SendVoid(menu, addItem, SendIntPtr(itemClass, Selector.GetHandle("separatorItem")));
                continue;
            }

            using var title = new NSString(item.Text);
            using var key = new NSString(string.Empty);

            IntPtr action = item.IsSubmenu ? IntPtr.Zero : ClickedSelector;

            IntPtr menuItem = SendIntPtr(
                SendIntPtr(itemClass, Selector.GetHandle("alloc")),
                Selector.GetHandle("initWithTitle:action:keyEquivalent:"),
                title.Handle,
                action,
                key.Handle);

            if (menuItem == IntPtr.Zero)
            {
                continue;
            }

            if (item.IsSubmenu)
            {
                IntPtr submenu = NewMenu();

                if (submenu != IntPtr.Zero)
                {
                    AppendItems(submenu, item.Children, ids);
                    SendVoid(menuItem, Selector.GetHandle("setSubmenu:"), submenu);
                    SendVoid(submenu, Selector.GetHandle("release"));
                }
            }
            else
            {
                ids.Add(item.Id);

                SendVoid(menuItem, Selector.GetHandle("setTarget:"), _target.Handle);
                SendVoid(menuItem, Selector.GetHandle("setTag:"), (IntPtr)ids.Count);
            }

            SendVoid(menuItem, Selector.GetHandle("setEnabled:"), item.IsEnabled ? (byte)1 : (byte)0);
            SendVoid(menu, addItem, menuItem);
            SendVoid(menuItem, Selector.GetHandle("release"));
        }
    }

    private void OnClicked(IntPtr sender)
    {
        nint tag = SendNint(sender, Selector.GetHandle("tag"));

        string? id;

        lock (_gate)
        {
            id = tag > 0 && tag <= _ids.Count ? _ids[(int)tag - 1] : null;
        }

        if (id is null)
        {
            return;
        }

        EventHandler<string>? handler = MenuItemSelected;
        if (handler is null)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                handler(this, id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "A menu bar item's handler failed.");
            }
        });
    }

    private void OnMainThread(Action action)
    {
        try
        {
            if (NSThread.IsMain)
            {
                action();
                return;
            }

            _target.InvokeOnMainThread(action);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The menu bar icon could not be updated.");
        }
    }

    private void Post(UNUserNotificationCenter center, string title, string message)
    {
        var content = new UNMutableNotificationContent
        {
            Title = title ?? string.Empty,
            Body = message ?? string.Empty,
        };

        var request = UNNotificationRequest.FromIdentifier(Guid.NewGuid().ToString(), content, null);

        center.AddNotificationRequest(request, error =>
        {
            if (error is not null)
            {
                _logger.LogWarning("A notification was refused: {Reason}", error.LocalizedDescription);
            }
        });
    }

    private sealed class MenuTarget(Action<IntPtr> clicked) : NSObject
    {
        [Export("helixItemClicked:")]
        public void ItemClicked(NSObject sender) => clicked(sender.Handle);
    }

    private sealed class ForegroundPresenter : UNUserNotificationCenterDelegate
    {
        public override void WillPresentNotification(
            UNUserNotificationCenter center,
            UNNotification notification,
            Action<UNNotificationPresentationOptions> completionHandler) =>
            completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List);
    }

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtr(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtr(IntPtr receiver, IntPtr selector, double arg);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, IntPtr arg3);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern byte SendByte(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern nint SendNint(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(IntPtr receiver, IntPtr selector, byte arg);
}
#endif
