using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using CommunityToolkit.Mvvm.Messaging;
using HardwareAuditToolkit.App.Messages;

namespace HardwareAuditToolkit.App.Services;

/// <summary>
/// <para>
/// §9.5 — a hidden, message-only window listening for <c>WM_INPUT_DEVICE_CHANGE</c>
/// (paired with <c>RIDEV_DEVNOTIFY</c> on raw-input registration) and
/// <c>WM_DISPLAYCHANGE</c>, so keyboard/mouse arrival/removal and monitor
/// reconfiguration are reflected live without restarting the app.
/// </para>
/// <para>
/// Exposes live device counts and raises <see cref="DeviceTopologyChangedMessage"/>
/// on the event bus for any screen to react to.
/// </para>
/// </summary>
public sealed class DeviceChangeService : IDisposable, INotifyPropertyChanged
{
    private const int HwndMessage = -3;
    private const int WmInputDeviceChange = 0x00FE; // WM_INPUT_DEVICE_CHANGE (0x00FF is WM_INPUT)
    private const int WmDisplayChange = 0x007E;
    private const int GidcArrival = 1;
    private const int GidcRemoval = 2;
    private const int RidevDevnotify = 0x2000;
    private const int RidevInputSink = 0x100;
    private const int RimTypekeyboard = 1;
    private const int RimTypemouse = 2;
    private const int SmCMonitors = 80;

    private HwndSource? _hwndSource;
    private bool _disposed;

    private int _keyboardCount;
    private int _mouseCount;
    private int _monitorCount;
    private string _lastEvent = "Monitoring input and display devices…";

    public int KeyboardCount
    {
        get => _keyboardCount;
        private set { _keyboardCount = value; OnPropertyChanged(); }
    }

    public int MouseCount
    {
        get => _mouseCount;
        private set { _mouseCount = value; OnPropertyChanged(); }
    }

    public int MonitorCount
    {
        get => _monitorCount;
        private set { _monitorCount = value; OnPropertyChanged(); }
    }

    public string LastEvent
    {
        get => _lastEvent;
        private set { _lastEvent = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var parameters = new HwndSourceParameters("DeviceChangeListener")
        {
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(HwndMessage),
            WindowStyle = 0,
            ExtendedWindowStyle = 0x80, // WS_EX_TOOLWINDOW
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);

        RegisterRawInput();
        Refresh();
    }

    private void RegisterRawInput()
    {
        if (_hwndSource is null)
        {
            return;
        }

        // Subscribe to keyboard + mouse arrival/removal regardless of focus
        // (RIDEV_INPUTSINK routes notifications to our message-only window).
        var devices = new[]
        {
            new RawInputDevice { UsagePage = 0x01, Usage = 0x06, Flags = RidevDevnotify | RidevInputSink, Target = _hwndSource.Handle },
            new RawInputDevice { UsagePage = 0x01, Usage = 0x02, Flags = RidevDevnotify | RidevInputSink, Target = _hwndSource.Handle },
        };

        if (!RegisterRawInputDevices(devices, devices.Length, Marshal.SizeOf<RawInputDevice>()))
        {
            Debug.WriteLine("DeviceChangeService: RegisterRawInputDevices failed.");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmInputDeviceChange)
        {
            string kind = (int)wParam == GidcArrival ? "arrived" : "removed";
            Refresh();
            LastEvent = $"Input device {kind}. Keyboards={KeyboardCount}, Mice={MouseCount}.";
            Publish();
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WmDisplayChange)
        {
            Refresh();
            LastEvent = $"Display configuration changed. Monitors={MonitorCount}.";
            Publish();
            handled = true;
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private void Publish()
    {
        WeakReferenceMessenger.Default.Send(new DeviceTopologyChangedMessage
        {
            KeyboardCount = KeyboardCount,
            MouseCount = MouseCount,
            MonitorCount = MonitorCount,
            Detail = LastEvent,
        });
    }

    public void Refresh()
    {
        EnumerateRawInput(out int keyboards, out int mice);
        KeyboardCount = keyboards;
        MouseCount = mice;
        MonitorCount = GetSystemMetrics(SmCMonitors);
    }

    private void EnumerateRawInput(out int keyboards, out int mice)
    {
        keyboards = 0;
        mice = 0;

        int size = Marshal.SizeOf<RawInputDeviceList>();
        uint count = 0;
        if (GetRawInputDeviceList(IntPtr.Zero, ref count, (uint)size) == unchecked((uint)-1))
        {
            return;
        }

        if (count == 0)
        {
            return;
        }

        IntPtr buffer = IntPtr.Zero;
        try
        {
            buffer = Marshal.AllocHGlobal((int)(count * size));
            uint written = GetRawInputDeviceList(buffer, ref count, (uint)size);
            if (written == unchecked((uint)-1))
            {
                return;
            }

            for (int i = 0; i < written; i++)
            {
                var entry = Marshal.PtrToStructure<RawInputDeviceList>(IntPtr.Add(buffer, i * size));
                if (entry.dwType == RimTypekeyboard)
                {
                    keyboards++;
                }
                else if (entry.dwType == RimTypemouse)
                {
                    mice++;
                }
            }
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
        _hwndSource?.Dispose();
        _hwndSource = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class RawInputDevice
    {
        public short UsagePage;
        public short Usage;
        public int Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDeviceList
    {
        public IntPtr hDevice;
        public uint dwType;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(
        RawInputDevice[] pRawInputDeviceList, int uiNumDevices, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList(
        IntPtr pRawInputDeviceList, ref uint puiNumDevices, uint cbSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
