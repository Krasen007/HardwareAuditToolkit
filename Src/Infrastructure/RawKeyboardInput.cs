using System.Runtime.InteropServices;
using System.Threading;

namespace HardwareAuditToolkit.Infrastructure;

/// <summary>
/// <para>
/// Phase 3 — raw keyboard capture (architecture §10 Phase 3). Registers raw
/// input for the keyboard usage page (0x01 / 0x06) and raises
/// <see cref="IRawKeyboardInput.KeyReceived"/> with a scan-code-first
/// <see cref="RawKeySample"/>.
/// </para>
/// <para>
/// The capture runs on a <b>dedicated background thread with its own Win32 message
/// loop</b> (architecture §9.2, same pattern as the Ctrl+E hook). This removes any
/// dependence on the WPF dispatcher pumping <c>WM_INPUT</c> to a window it didn't
/// create, which is the usual reason raw capture silently receives nothing on a
/// WPF thread. The native message-only window (parent <c>HWND_MESSAGE</c>) is created
/// and torn down on that thread, so <c>WM_INPUT</c> is always delivered while
/// capture is active. The callback only parses one structure and hands the sample to
/// subscribers; it never blocks the loop (§9.2).
/// </para>
/// <para>
/// The composite scan-code id is <c>0xE000 | makeCode</c> for keys carrying the
/// E0 prefix and the raw make code otherwise, giving a stable per-physical-key
/// identifier that the vector layout maps to an on-screen key.
/// </para>
/// </summary>
public sealed class RawKeyboardInput : IRawKeyboardInput, IDisposable
{
    private const int WmInput = 0x00FF;
    private const int WmQuit = 0x0012;
    private const int RidInput = 0x10000003;
    private const int RimTypekeyboard = 1;
    private const int RidevInputSink = 0x100;
    private const int RidevRemove = 0x00000001;
    private const int RiKeyE0 = 0x02;   // E0 prefix in RAWKEYBOARD.Flags
    private const int RiKeyBreak = 0x01; // key-up (break) flag

    private const int HwndMessage = -3;

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
    private readonly string _className = "HATKbdCapture_" + Guid.NewGuid().ToString("N");
    private WndProc? _wndProc; // referenced for the lifetime of the window

    public event EventHandler<RawKeySample>? KeyReceived;

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
                Name = "HAT Keyboard Capture",
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

        // Wait until the loop is actually pumping, then post WM_QUIT directly to the
        // thread's queue so GetMessage returns and the loop tears down cleanly.
        _ready.Wait(TimeSpan.FromSeconds(2));
        if (_captureThreadId != 0)
        {
            PostThreadMessage(_captureThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }

        // Don't Join: if something prevented the quit we don't want to hang shutdown
        // on a background thread. IsBackground=true ensures it can't block process exit.
    }

    private void MessageLoop()
    {
        _wndProc = WndProcThunk; // references a static method, so the thunk is never collected

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
                new[] { new RawInputDevice { UsagePage = 0x01, Usage = 0x06, Flags = RidevInputSink, Target = _hwnd } },
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
        // Standard Win32 message pump. Exits on WM_QUIT (returns 0); -1 is an error.
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
                new[] { new RawInputDevice { UsagePage = 0x01, Usage = 0x06, Flags = RidevRemove, Target = IntPtr.Zero } },
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
        // First call with a NULL buffer queries the required size; on success the
        // function returns that size (a positive number), NOT zero. Only (UINT)-1
        // signals failure — otherwise we'd bail out before ever reading a key.
        if (GetRawInputData(lParam, RidInput, IntPtr.Zero, ref size, Marshal.SizeOf<Rawinputheader>()) == unchecked((uint)-1) ||
            size == 0)
        {
            return;
        }

        IntPtr buffer = IntPtr.Zero;
        try
        {
            buffer = Marshal.AllocHGlobal((int)size);
            // Second call copies the payload; it returns the number of bytes copied,
            // which must equal the size we just learned.
            if (GetRawInputData(lParam, RidInput, buffer, ref size, Marshal.SizeOf<Rawinputheader>()) != size)
            {
                return;
            }

            var header = Marshal.PtrToStructure<Rawinputheader>(buffer);
            if (header.dwType != RimTypekeyboard)
            {
                return;
            }

            var kb = Marshal.PtrToStructure<Rawkeyboard>(IntPtr.Add(buffer, Marshal.SizeOf<Rawinputheader>()));

            bool extended = (kb.Flags & RiKeyE0) != 0;
            int composite = extended ? (0xE000 | kb.MakeCode) : kb.MakeCode;

            var sample = new RawKeySample
            {
                ScanCodeId = composite,
                VirtualKey = kb.VKey,
                IsExtended = extended,
                IsKeyDown = (kb.Flags & RiKeyBreak) == 0,
            };

            KeyReceived?.Invoke(this, sample);
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

    [StructLayout(LayoutKind.Sequential)]
    private struct Rawkeyboard
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public ulong ExtraInformation;
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
