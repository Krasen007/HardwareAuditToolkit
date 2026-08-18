using System.Runtime.InteropServices;

namespace HardwareAuditToolkit.Infrastructure;

/// <summary>
/// <para>
/// Phase 3 — raw keyboard capture (architecture §10 Phase 3). Registers raw
/// input for the keyboard usage page (0x01 / 0x06) with <c>RIDEV_INPUTSINK</c>
/// so every physical key arrives regardless of focus, parses each
/// <c>WM_INPUT</c> via <see cref="GetRawInputData"/>, and raises
/// <see cref="IRawKeyboardInput.KeyReceived"/> with a scan-code-first
/// <see cref="RawKeySample"/>.
/// </para>
/// <para>
/// The capture window is a native message-only window (parent <c>HWND_MESSAGE</c>)
/// created on the calling thread, so this type carries no WPF dependency and can
/// live in Infrastructure. It is created on the WPF dispatcher thread (the module
/// starts capture from a UI command), where the dispatcher's message loop pumps
/// <c>WM_INPUT</c> to our window procedure. Capture is intentionally lightweight:
/// the callback only parses one structure and hands the sample to subscribers; it
/// never blocks the thread (§9.2).
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
    private const int RidInput = 0x10000003;
    private const int RimTypekeyboard = 1;
    private const int RidevInputSink = 0x100;
    private const int RidevRemove = 0x00000001;
    private const int RiKeyE0 = 0x02;   // E0 prefix in RAWKEYBOARD.Flags
    private const int RiKeyBreak = 0x01; // key-up (break) flag

    private const int HwndMessage = -3;

    private readonly object _gate = new();
    private IntPtr _hwnd;
    private string _className = "HATKbdCapture_" + Guid.NewGuid().ToString("N");
    private UIntPtr _classAtom;
    private WndProc? _wndProc; // kept rooted so the native thunk is never collected
    private bool _disposed;

    public event EventHandler<RawKeySample>? KeyReceived;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (_hwnd != IntPtr.Zero)
            {
                return;
            }

            var hInstance = GetModuleHandle(null);
            _wndProc = WndProcThunk;

            var wc = new Wndclassex
            {
                cbSize = (uint)Marshal.SizeOf<Wndclassex>(),
                lpfnWndProc = _wndProc,
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
                return;
            }

            RegisterRawInputDevices(
                new[] { new RawInputDevice { UsagePage = 0x01, Usage = 0x06, Flags = RidevInputSink, Target = _hwnd } },
                1,
                Marshal.SizeOf<RawInputDevice>());
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            // Unregister before destroying the window so no further WM_INPUT
            // arrives for our handle.
            RegisterRawInputDevices(
                new[] { new RawInputDevice { UsagePage = 0x01, Usage = 0x06, Flags = RidevRemove, Target = IntPtr.Zero } },
                1,
                Marshal.SizeOf<RawInputDevice>());

            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;

            if (_classAtom != UIntPtr.Zero)
            {
                UnregisterClass(_className, GetModuleHandle(null));
                _classAtom = UIntPtr.Zero;
            }

            _wndProc = null;
        }
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
        if (GetRawInputData(lParam, RidInput, IntPtr.Zero, ref size, Marshal.SizeOf<Rawinputheader>()) != 0 ||
            size == 0)
        {
            return;
        }

        IntPtr buffer = IntPtr.Zero;
        try
        {
            buffer = Marshal.AllocHGlobal((int)size);
            if (GetRawInputData(lParam, RidInput, buffer, ref size, Marshal.SizeOf<Rawinputheader>()) <= 0)
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

    private sealed class RawInputDevice
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class Wndclassex
    {
        public uint cbSize;
        public int style;
        public WndProc lpfnWndProc = null!;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName = string.Empty;
        public IntPtr hIconSm;
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern UIntPtr RegisterClassEx([In] ref Wndclassex lpwcx);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string? lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
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
}
