using System.Runtime.InteropServices;

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
/// The capture window is a native message-only window (parent <c>HWND_MESSAGE</c>)
/// created on the calling thread, so this type carries no WPF dependency and can
/// live in Infrastructure. It is created on the WPF dispatcher thread (the module
/// starts capture from a UI command), where the dispatcher's message loop pumps
/// <c>WM_INPUT</c> to our window procedure. Capture is intentionally lightweight:
/// the callback only parses one structure and hands the sample to subscribers; it
/// never blocks the thread (§9.2).
/// </para>
/// </summary>
public sealed class RawMouseInput : IRawMouseInput, IDisposable
{
    private const int WmInput = 0x00FF;
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
    private IntPtr _hwnd;
    private string _className = "HATMouseCapture_" + Guid.NewGuid().ToString("N");
    private UIntPtr _classAtom;
    private WndProc? _wndProc; // kept rooted so the native thunk is never collected
    private bool _disposed;

    public event EventHandler<RawMouseSample>? MouseReceived;

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
                new[] { new RawInputDevice { UsagePage = 0x01, Usage = 0x02, Flags = RidevInputSink, Target = _hwnd } },
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

            RegisterRawInputDevices(
                new[] { new RawInputDevice { UsagePage = 0x01, Usage = 0x02, Flags = RidevRemove, Target = IntPtr.Zero } },
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
