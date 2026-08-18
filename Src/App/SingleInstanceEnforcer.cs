using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace HardwareAuditToolkit.App;

/// <summary>
/// <para>Enforces a single running instance (§9.3).</para>
/// <para>
/// A system-wide <c>Global\</c> mutex is acquired before anything else runs.
/// The first instance owns a hidden signal window; a later instance locates
/// that window by its fixed title and posts a registered window message that
/// tells the first instance to foreground its main window.
/// </para>
/// </summary>
public sealed class SingleInstanceEnforcer : IDisposable
{
    private const string MutexName = @"Global\HardwareAuditToolkit.SingleInstance";
    private const string SignalWindowTitle = "HardwareAuditToolkit.SingleInstance";
    private const string ActivateMessageName = "HardwareAuditToolkit.ActivateMainWindow";

    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExToolWindow = 0x00000080;

    private static readonly uint ActivateMessage = RegisterWindowMessage(ActivateMessageName);

    private Mutex? _mutex;
    private HwndSource? _signalWindow;
    private bool _ownsMutex;
    private bool _disposed;

    /// <summary>True when this process is the first (and only) instance.</summary>
    public bool IsFirstInstance { get; private set; }

    /// <summary>Raised on the first instance when a second launch requests activation.</summary>
    public event Action? ActivateRequested;

    /// <summary>
    /// Attempts to become the single running instance. Returns false when another
    /// instance is already running (call <see cref="SignalFirstInstance"/> then exit).
    /// </summary>
    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (createdNew)
        {
            _ownsMutex = true;
        }
        else
        {
            try
            {
                if (!_mutex.WaitOne(0))
                {
                    return false;
                }
            }
            catch (AbandonedMutexException)
            {
                // The previous instance crashed without releasing; we now own it.
            }

            _ownsMutex = true;
        }

        IsFirstInstance = true;
        CreateSignalWindow();
        return true;
    }

    /// <summary>
    /// Signals the first instance to foreground its main window. Called by the
    /// second instance just before it exits.
    /// </summary>
    public static void SignalFirstInstance()
    {
        // The first instance creates its signal window right after acquiring
        // the mutex, so a short retry covers the startup race.
        for (int attempt = 0; attempt < 50; attempt++)
        {
            IntPtr hwnd = FindWindowW(null, SignalWindowTitle);
            if (hwnd != IntPtr.Zero)
            {
                PostMessage(hwnd, ActivateMessage, IntPtr.Zero, IntPtr.Zero);
                return;
            }

            Thread.Sleep(50);
        }
    }

    private void CreateSignalWindow()
    {
        var parameters = new HwndSourceParameters(SignalWindowTitle)
        {
            Width = 0,
            Height = 0,
            WindowStyle = WsPopup,
            ExtendedWindowStyle = WsExToolWindow,
        };

        _signalWindow = new HwndSource(parameters);
        _signalWindow.AddHook(WindowProc);
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == ActivateMessage)
        {
            ActivateRequested?.Invoke();
            handled = true;
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _signalWindow?.Dispose();
        _signalWindow = null;

        if (_ownsMutex)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned by this thread — nothing to release.
            }
        }

        _mutex?.Dispose();
        _mutex = null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);
}
