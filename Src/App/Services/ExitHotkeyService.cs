using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using CommunityToolkit.Mvvm.Messaging;
using HardwareAuditToolkit.Core.Messages;

namespace HardwareAuditToolkit.App.Services;

/// <summary>
/// <para>
/// §9.2 — global Ctrl+E exit hotkey installed on its OWN dedicated background
/// thread with a minimal message loop. The low-level keyboard callback runs on
/// this thread, not the WPF Dispatcher thread, so exit responsiveness is
/// decoupled from whatever the UI thread (or a CPU burn-in test) is doing.
/// </para>
/// <para>
/// The callback only raises the <see cref="ExitRequestedMessage"/>; the
/// subscriber (the app bootstrap) marshals the actual shutdown onto the UI
/// thread, so this thread never blocks inside the hook.
/// </para>
/// </summary>
public sealed partial class ExitHotkeyService : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmQuit = 0x0012;
    private const int VkE = 0x45;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new(false);
    private LowLevelKeyboardProc? _proc;
    private IntPtr _hookId;
    private uint _threadId;
    private bool _disposed;

    public ExitHotkeyService()
    {
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "CtrlEExitHook",
        };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _thread.Start();
        // Wait only briefly; if the hook cannot be installed we still return and
        // the other exit paths (button / window close) remain available.
        _started.Wait(TimeSpan.FromSeconds(5));
    }

    private void Loop()
    {
        // Mark the thread as having a message queue before installing the hook.
        _threadId = GetCurrentThreadId();
        _started.Set();

        _proc = HookCallback;
        _hookId = SetWindowsHookEx(WhKeyboardLl, _proc, IntPtr.Zero, 0);
        if (_hookId == IntPtr.Zero)
        {
            Debug.WriteLine("ExitHotkeyService: SetWindowsHookEx failed; Ctrl+E disabled, other exit paths remain.");
            return;
        }

        while (!_disposed)
        {
            int ret = GetMessage(out MSG msg, IntPtr.Zero, 0, 0);
            if (ret == -1 || (uint)msg.message == WmQuit)
            {
                break;
            }

            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WmKeyDown || wParam == (IntPtr)WmSysKeyDown))
        {
            int vkCode = Marshal.ReadInt32(lParam);
            if (vkCode == VkE)
            {
                bool ctrl = (GetAsyncKeyState(VkLControl) & 0x8000) != 0
                            || (GetAsyncKeyState(VkRControl) & 0x8000) != 0;
                if (ctrl)
                {
                    // Non-blocking: the recipient marshals to the UI thread.
                    WeakReferenceMessenger.Default.Send(new ExitRequestedMessage());
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        // Wake the message loop so the thread can exit cleanly.
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }

        if (_thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        _started.Dispose();
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(IntPtr hhk);

    [LibraryImport("user32.dll")]
    private static partial IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("user32.dll")]
    private static partial int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll")]
    private static partial IntPtr DispatchMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}
