using System.Runtime.InteropServices;
using System.Threading;

namespace HardwareAuditToolkit.Infrastructure;

/// <summary>
/// <para>
/// Phase 4 — raw mouse capture (architecture §10 Phase 4). Registers raw input
/// for the mouse usage page (0x01 / 0x02) with <c>RIDEV_INPUTSINK</c> so every
/// physical button, wheel, and movement arrives regardless of focus, parses each
/// <c>WM_INPUT</c> via <see cref="GetRawInputData"/>, and raises
/// <see cref="IRawMouseInput.MouseReceived"/> with a <see cref="RawMouseSample"/>.
/// </para>
/// <para>
/// The capture runs on a <b>dedicated background thread with its own Win32 message
/// loop</b> (architecture §9.2, same pattern as the keyboard raw-input capture and
/// the Ctrl+E hook). This removes any dependence on the WPF dispatcher pumping
/// <c>WM_INPUT</c> to a window it didn't create, which is the usual reason raw
/// capture silently receives nothing on a WPF thread. The native message-only
/// window (parent <c>HWND_MESSAGE</c>) is created and torn down on that thread,
/// so <c>WM_INPUT</c> is always delivered while capture is active. The callback
/// only parses one structure and hands the sample to subscribers; it never blocks
/// the loop (§9.2).
/// </para>
/// </summary>
public sealed class RawMouseInput : IRawMouseInput, IDisposable
{
    private const int WmInput = 0x00FF;
    private const int WmQuit = 0x0012;
    private const int RidInput = 0x10000003;
    private const int RimTypemouse = 2;
    private const int RidevInputSink = 0x100;
    private const int RidevRemove = 0x00000001;

    private const int HwndMessage = -3;

    // RAWMOUSE usButtonFlags values.
    private const ushort RiMouseLeftButtonDown = 0x0001;
    private const ushort RiMouseLeftButtonUp = 0x0002;
    private const ushort RiMouseRightButtonDown = 0x0004;
    private const ushort RiMouseRightButtonUp = 0x0008;
    private const ushort RiMouseMiddleButtonDown = 0x0010;
    private const ushort RiMouseMiddleButtonUp = 0x0020;
    private const ushort RiMouseButton4Down = 0x0040;
    private const ushort RiMouseButton4Up = 0x0080;
    private const ushort RiMouseButton5Down = 0x0100;
    private const ushort RiMouseButton5Up = 0x0200;
    private const ushort RiMouseWheel = 0x0400;

    private readonly object _gate = new();
    private Thread? _thread;
    private int _captureThreadId;
    private readonly ManualResetEventSlim _ready = new(false);
    private bool _disposed;

    // Owned exclusively by the capture thread — never touched from another thread.
    // ThreadStatic so a stop→start restart cannot have a still-tearing-down old
    // capture thread destroy the window/atom of the new capture thread: each
    // thread tears down its own native window and class registration.
    [ThreadStatic]
    private static IntPtr _hwnd;
    [ThreadStatic]
    private static UIntPtr _classAtom;
    private readonly string _className = "HATMouseCapture_" + Guid.NewGuid().ToString("N");
    private WndProc? _wndProc; // referenced for the lifetime of the window

    public event EventHandler<RawMouseSample>? MouseReceived;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (_thread is not null)
            {
                return; // already capturing
            }

            _ready.Reset();
            _thread = new Thread(MessageLoop)
            {
                IsBackground = true,
                Name = "HAT Mouse Capture",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }
    }

    public void Stop()
    {
        Thread? thread;
        lock (_gate)
        {
            if (_thread is null)
            {
                return;
            }

            thread = _thread;
            _thread = null;
        }

        _ready.Wait(TimeSpan.FromSeconds(2));
        if (_captureThreadId != 0)
        {
            PostThreadMessage(_captureThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private void MessageLoop()
    {
        _wndProc = WndProcThunk; // instance method kept alive for the window lifetime

        var hInstance = GetModuleHandle(null);
        var wc = new Wndclassex
        {
            cbSize = (uint)Marshal.SizeOf<Wndclassex>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = _className,
        };

        _classAtom = RegisterClassEx(ref wc);
        if (_classAtom == UIntPtr.Zero)
        {
            return;
        }

        _hwnd = CreateWindowEx(
            0, _className, null, 0,
            0, 0, 0, 0,
            new IntPtr(HwndMessage), IntPtr.Zero, hInstance, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            UnregisterClass(_className, hInstance);
            return;
        }

        if (!RegisterRawInputDevices(
                new[] { new RawInputDevice { UsagePage = 0x01, Usage = 0x02, Flags = RidevInputSink, Target = _hwnd } },
                1,
                Marshal.SizeOf<RawInputDevice>()))
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
            UnregisterClass(_className, hInstance);
            return;
        }

        _captureThreadId = GetCurrentThreadId();
        _ready.Set();

        var msg = new MSG();
        // Native message pump. Exits on WM_QUIT (returns 0); -1 is an error.
        while (GetMessage(out msg, IntPtr.Zero, 0, 0) != 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        Teardown(hInstance);
    }

    private void Teardown(IntPtr hInstance)
    {
        if (_hwnd != IntPtr.Zero)
        {
            RegisterRawInputDevices(
                new[] { new RawInputDevice { UsagePage = 0x01, Usage = 0x02, Flags = RidevRemove, Target = IntPtr.Zero } },
                1,
                Marshal.SizeOf<RawInputDevice>());
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        if (_classAtom != UIntPtr.Zero)
        {
            UnregisterClass(_className, hInstance);
            _classAtom = UIntPtr.Zero;
        }

        _wndProc = null;
    }

    private IntPtr WndProcThunk(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmInput)
        {
            RaiseSample(lParam);
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void RaiseSample(IntPtr lParam)
    {
        uint size = 0;
        if (GetRawInputData(lParam, RidInput, IntPtr.Zero, ref size, Marshal.SizeOf<Rawinputheader>()) == unchecked((uint)-1) ||
            size == 0)
        {
            return;
        }

        IntPtr buffer = IntPtr.Zero;
        try
        {
            buffer = Marshal.AllocHGlobal((int)size);
            if (GetRawInputData(lParam, RidInput, buffer, ref size, Marshal.SizeOf<Rawinputheader>()) != size)
            {
                return;
            }

            var header = Marshal.PtrToStructure<Rawinputheader>(buffer);
            if (header.dwType != RimTypemouse)
            {
                return;
            }

            var mouse = Marshal.PtrToStructure<Rawmouse>(IntPtr.Add(buffer, Marshal.SizeOf<Rawinputheader>()));

            var changes = MouseButtonChanges.None;
            if ((mouse.usButtonFlags & RiMouseLeftButtonDown) != 0) changes |= MouseButtonChanges.LeftDown;
            if ((mouse.usButtonFlags & RiMouseLeftButtonUp) != 0) changes |= MouseButtonChanges.LeftUp;
            if ((mouse.usButtonFlags & RiMouseRightButtonDown) != 0) changes |= MouseButtonChanges.RightDown;
            if ((mouse.usButtonFlags & RiMouseRightButtonUp) != 0) changes |= MouseButtonChanges.RightUp;
            if ((mouse.usButtonFlags & RiMouseMiddleButtonDown) != 0) changes |= MouseButtonChanges.MiddleDown;
            if ((mouse.usButtonFlags & RiMouseMiddleButtonUp) != 0) changes |= MouseButtonChanges.MiddleUp;
            if ((mouse.usButtonFlags & RiMouseButton4Down) != 0) changes |= MouseButtonChanges.X1Down;
            if ((mouse.usButtonFlags & RiMouseButton4Up) != 0) changes |= MouseButtonChanges.X1Up;
            if ((mouse.usButtonFlags & RiMouseButton5Down) != 0) changes |= MouseButtonChanges.X2Down;
            if ((mouse.usButtonFlags & RiMouseButton5Up) != 0) changes |= MouseButtonChanges.X2Up;

            int wheel = 0;
            if ((mouse.usButtonFlags & RiMouseWheel) != 0)
            {
                changes |= MouseButtonChanges.Wheel;
                wheel = (short)mouse.usButtonData;
            }

            var sample = new RawMouseSample
            {
                Buttons = changes,
                WheelDelta = wheel,
                DeltaX = mouse.lLastX,
                DeltaY = mouse.lLastY,
            };

            MouseReceived?.Invoke(this, sample);
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    // --- Native types & P/Invoke -------------------------------------------------
    // RawInputDevice and Wndclassex are sequential STRUCTS, not Auto-layout
    // classes: the interop marshaler cannot compute a layout for a class without
    // [StructLayout] (Marshal.SizeOf throws ArgumentException), and a class
    // passed by ref to RegisterClassEx fails with ERROR_INVALID_PARAMETER (87).
    // The struct's string fields are marshaled as Unicode, so the P/Invokes that
    // take class names must bind the W (Unicode) entry points too — mixing A/W
    // makes CreateWindowEx/UnregisterClass fail with ERROR_CANNOT_FIND_WND_CLASS
    // (1407) and capture silently dies.

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public short UsagePage;
        public short Usage;
        public int Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rawinputheader
    {
        public int dwType;
        public int dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct Rawmouse
    {
        [FieldOffset(0)] public ushort usFlags;
        [FieldOffset(2)] public ushort usButtonFlags;
        [FieldOffset(4)] public ushort usButtonData;
        [FieldOffset(8)] public uint ulRawButtons;
        [FieldOffset(12)] public int lLastX;
        [FieldOffset(16)] public int lLastY;
        [FieldOffset(20)] public uint ulExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Wndclassex
    {
        public uint cbSize;
        public int style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        // POINT is two LONGs (4-byte aligned). The interop marshaler aligns it
        // after DWORD time at offset 36 (x64) / 20 (x86) automatically, matching
        // the native layout — do NOT add an explicit padding field, which would
        // shift the point by 4 bytes and corrupt the coordinates.
        public int ptX;
        public int ptY;
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern UIntPtr RegisterClassEx([In] ref Wndclassex lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string? lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(
        RawInputDevice[] pRawInputDeviceList, int uiNumDevices, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, int cbSizeHeader);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(int idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentThreadId();
}
