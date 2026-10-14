using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
namespace DotNative.WinUI;
public static unsafe partial class WinUIBridge
{
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint hwnd, uint msg, nuint w, nint l);
    [DllImport("user32.dll")] private static extern bool PostMessageW(nint hwnd, uint msg, nuint w, nint l);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    private sealed class WindowRegression
    {
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
        private readonly nint _host;
        private int _step;
        internal WindowRegression(nint host)
        {
            _host = host;
            _timer.Tick += Tick;
            _timer.Start();
        }
        private void Tick(object? sender, object args)
        {
            if (_step++ == 0)
            {
                if (SendMessageW(_host, 0x8003, 0, 0) != 1)
                {
                    Console.Error.WriteLine("FAIL host window activation context cannot resolve WinUI input factory");
                    _timer.Stop();
                    return;
                }
                Console.WriteLine("Window regression: enter native move loop");
                _ = Task.Run(() => { Thread.Sleep(300); PostMessageW(_host, 0x1f, 0, 0); });
                SendMessageW(_host, 0x112, 0xf010, 0); // SC_MOVE enters DefWindowProc's modal pump.
                Console.WriteLine("Window regression: native move loop returned");
            }
            else
            {
                PostMessageW(_host, 0x1f, 0, 0); // WM_CANCELMODE ends the test-only move.
                SetWindowPos(_host, 0, 100, 100, 1100, 900, 0x14);
                _timer.Stop();
                _timer.Tick -= Tick;
                Console.WriteLine("PASS native window move/resize loop");
            }
        }
    }
}
