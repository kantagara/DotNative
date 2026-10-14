using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DotNative;

namespace DotNative.Generated;

internal static partial class AppEntry
{
    private static void ReportError(Exception error)
    {
#if DOTNATIVE_ANDROID
        AndroidLog(6, "DotNative", error.ToString());
#else
        Console.Error.WriteLine(error);
#endif
    }

#if DOTNATIVE_ANDROID
    [LibraryImport(
        "liblog.so",
        EntryPoint = "__android_log_write",
        StringMarshalling = StringMarshalling.Utf8
    )]
    private static partial int AndroidLog(int priority, string tag, string message);
#endif

    private static partial Task InvokeMain();

    private static DotNative.Hosting.ApplicationSession? _session;
    private static NativeTransport? _transport;
    private static MainDispatcher? _dispatcher;
    private static DotNative.Hosting.DispatcherSynchronizationContext? _context;
    private static Task? _shutdown;
    private static bool _resizeQueued;
    private static float _resizeWidth,
        _resizeHeight;
    private static bool _insetsQueued;
    private static float _insetTop,
        _insetBottom,
        _insetKeyboard;
    private static AccessibilityPreferences _accessibility = AccessibilityPreferences.Default;
    private static ThemeMode _systemTheme = ThemeMode.Light;

    [UnmanagedCallersOnly(
        EntryPoint = "dotnative_app_accessibility",
        CallConvs = [typeof(CallConvCdecl)]
    )]
    public static int Accessibility(int reduceMotion, float fontScale, int highContrast)
    {
        try
        {
            if (
                reduceMotion is not (0 or 1)
                || highContrast is not (0 or 1)
                || !float.IsFinite(fontScale)
                || fontScale < .5f
                || fontScale > 5
            )
                return -1;
            _accessibility = new(reduceMotion != 0, fontScale, highContrast != 0);
            _session?.SetSystemAccessibility(_accessibility);
            return 0;
        }
        catch (Exception error)
        {
            ReportError(error);
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "dotnative_app_start", CallConvs = [typeof(CallConvCdecl)])]
    public static int Start(float width, float height)
    {
        try
        {
            if (_session is not null)
                return -1;
            _dispatcher = new MainDispatcher();
            _context = new(_dispatcher);
            _transport = new NativeTransport();
            _session = DotNative.Hosting.ApplicationSession.Begin(
                InvokeMain,
                _dispatcher,
                _transport,
                width,
                height,
                _systemTheme,
                _accessibility
            );
            _session.SetSystemTheme(_systemTheme);
            return 0; // Accepted: Main may still be awaiting initialization.
        }
        catch (Exception error)
        {
            ReportError(error);
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "dotnative_app_resize", CallConvs = [typeof(CallConvCdecl)])]
    public static int Resize(float width, float height)
    {
        try
        {
            if (_session is null)
                return -1;
            // AppKit can report layout synchronously while a native tree is being
            // committed. Apply the viewport on the next dispatcher turn.
            if (!float.IsFinite(width) || !float.IsFinite(height) || width < 0 || height < 0)
                return -1;
            _resizeWidth = width;
            _resizeHeight = height;
            if (_resizeQueued)
                return 0;
            var session = _session;
            _resizeQueued = true;
            _dispatcher!.Post(() =>
            {
                _resizeQueued = false;
                if (ReferenceEquals(_session, session))
                    session.Resize(_resizeWidth, _resizeHeight);
            });
            return 0;
        }
        catch (Exception error)
        {
            ReportError(error);
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "dotnative_app_insets", CallConvs = [typeof(CallConvCdecl)])]
    public static int Insets(float top, float bottom, float keyboard)
    {
        try
        {
            if (_session is null)
                return -1;
            if (
                !float.IsFinite(top)
                || !float.IsFinite(bottom)
                || !float.IsFinite(keyboard)
                || top < 0
                || bottom < 0
                || keyboard < 0
            )
                return -1;
            _insetTop = top;
            _insetBottom = bottom;
            _insetKeyboard = keyboard;
            if (_insetsQueued)
                return 0;
            var session = _session;
            _insetsQueued = true;
            _dispatcher!.Post(() =>
            {
                _insetsQueued = false;
                if (ReferenceEquals(_session, session))
                    session.SetWindowInsets(_insetTop, _insetBottom, _insetKeyboard);
            });
            return 0;
        }
        catch (Exception error)
        {
            ReportError(error);
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "dotnative_app_tick", CallConvs = [typeof(CallConvCdecl)])]
    public static int Tick()
    {
        try
        {
            _context?.Run(() => _dispatcher?.Drain());
            if (_session is null)
                return 0;
            _context?.Run(() => _transport?.DispatchPluginReplies(_session.Channels));
            if (_shutdown is not null)
            {
                int stopped = StopCore();
                return stopped == 1 ? 0 : stopped;
            }
            if (_session.Startup.IsFaulted || _session.Startup.IsCanceled)
            {
                int result = StopCore();
                return result == 1 ? 0 : result;
            }
            if (_session.MountedHost is { } host)
                _context?.Run(() =>
                {
                    _transport?.DispatchPendingEvents(host);
                    host.AdvanceAnimationFrame();
                });
            return 0;
        }
        catch (Exception error)
        {
            ReportError(error);
            return -1;
        }
    }

    [UnmanagedCallersOnly(
        EntryPoint = "dotnative_app_activity",
        CallConvs = [typeof(CallConvCdecl)]
    )]
    public static int Activity(int activity)
    {
        try
        {
            if (_session is null)
                return 0;
            if (activity < 0 || activity > 2)
                return -1;
            _session.SetActivity((ApplicationActivity)activity);
            return 0;
        }
        catch (Exception error)
        {
            ReportError(error);
            return -1;
        }
    }

    [UnmanagedCallersOnly(
        EntryPoint = "dotnative_app_system_theme",
        CallConvs = [typeof(CallConvCdecl)]
    )]
    public static int SystemTheme(int theme)
    {
        try
        {
            if (theme is not (1 or 2))
                return -1;
            _systemTheme = (ThemeMode)theme;
            _session?.SetSystemTheme(_systemTheme);
            return 0;
        }
        catch (Exception error)
        {
            ReportError(error);
            return -1;
        }
    }

    // 0 = stopped, 1 = pending (poll again on the UI thread), -1 = failed.
    // Never block UIKit/Android while awaited cleanup needs their dispatcher.
    [UnmanagedCallersOnly(EntryPoint = "dotnative_app_stop", CallConvs = [typeof(CallConvCdecl)])]
    public static int Stop() => StopCore();

    private static int StopCore()
    {
        try
        {
            if (_session is null)
                return 0;
            _shutdown ??= _session.DisposeAsync().AsTask();
            _context?.Run(() => _dispatcher?.Drain());
            if (!_shutdown.IsCompleted)
                return 1;
            _shutdown.GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception error)
        {
            ReportError(error);
            return -1;
        }
        finally
        {
            if (_shutdown?.IsCompleted == true)
            {
                _transport?.ResetPlugins();
                _session = null;
                _transport = null;
                _dispatcher = null;
                _context = null;
                _shutdown = null;
            }
        }
    }

    private sealed class MainDispatcher : IUiDispatcher
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly ConcurrentQueue<Action> _pending = new();

        public bool CheckAccess() => Environment.CurrentManagedThreadId == _thread;

        public void Post(Action action) => _pending.Enqueue(action);

        public void Drain()
        {
            if (!CheckAccess())
                throw new InvalidOperationException("UI dispatcher requires the main thread.");
            // Work posted while draining waits for the next display tick.
            int count = _pending.Count;
            while (count-- > 0 && _pending.TryDequeue(out var action))
                action();
        }
    }
}
