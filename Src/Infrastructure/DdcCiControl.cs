using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace HardwareAuditToolkit.Infrastructure;

/// <summary>
/// <para>
/// Phase 5 — DDC/CI wrapper over <c>dxva2.dll</c> (architecture §10 Phase 5, §2).
/// Enumerates physical monitors via <c>EnumDisplayMonitors</c> (so screen bounds
/// are already in device pixels and need no DPI conversion), then opens each
/// monitor's physical handle to read a friendly name and to get/set the VCP 0x10
/// brightness feature.
/// </para>
/// <para>
/// Every call is wrapped so a missing driver, a DDC/CI-disabled monitor, or a
/// best-effort-without-admin failure returns an empty list / an unsupported
/// reading instead of throwing — the monitor module records "unsupported"
/// cleanly (§10 Phase 5, DoD).
/// </para>
/// </summary>
public sealed class DdcCiControl : IDdcCiControl
{
    private const byte VcpBrightness = 0x10;

    // Probed once: some systems ship a dxva2.dll that lacks the VCP/physical-monitor
    // entry points entirely. Rather than let the binder throw EntryPointNotFoundException
    // (which would surface as a crash even though DDC/CI is meant to degrade gracefully),
    // we detect availability up front and short-circuit to "unsupported" (architecture §10
    // Phase 5, DoD: DDC/CI reports "unsupported" cleanly where not present).
    private static readonly Lazy<bool> _apiAvailable = new(ProbeApiAvailable);

    private static readonly MonitorEnumDelegate MonitorEnumCallback = MonitorEnumProc;

    public IReadOnlyList<MonitorInfo> EnumerateMonitors()
    {
        var collected = new MonitorEnum();
        var handle = GCHandle.Alloc(collected);
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, MonitorEnumCallback, GCHandle.ToIntPtr(handle));
        }
        catch
        {
            return Array.Empty<MonitorInfo>();
        }
        finally
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }

        var result = new List<MonitorInfo>();
        for (int i = 0; i < collected.Items.Count; i++)
        {
            var it = collected.Items[i];
            string friendly = it.Device;
            try
            {
                var pm = new PHYSICAL_MONITOR[1];
                if (GetPhysicalMonitorsFromHMONITOR(it.Hmonitor, 1, pm))
                {
                    if (!string.IsNullOrWhiteSpace(pm[0].szPhysicalMonitorDescription))
                    {
                        friendly = pm[0].szPhysicalMonitorDescription;
                    }

                    DestroyPhysicalMonitors(1, pm);
                }
            }
            catch
            {
                friendly = it.Device;
            }

            result.Add(new MonitorInfo
            {
                Index = i,
                DeviceName = it.Device,
                FriendlyName = friendly,
                IsPrimary = it.Primary,
                Left = it.Rect.Left,
                Top = it.Rect.Top,
                Width = Math.Max(1, it.Rect.Right - it.Rect.Left),
                Height = Math.Max(1, it.Rect.Bottom - it.Rect.Top),
            });
        }

        return result;
    }

    public BrightnessReading GetBrightness(int index)
    {
        if (!_apiAvailable.Value)
        {
            return new BrightnessReading { Supported = false, Detail = "DDC/CI API not available on this system." };
        }

        IntPtr hmonitor = GetHmonitor(index);
        if (hmonitor == IntPtr.Zero)
        {
            return new BrightnessReading { Supported = false, Detail = "Monitor index out of range." };
        }

        var pm = new PHYSICAL_MONITOR[1];
        if (!GetPhysicalMonitorsFromHMONITOR(hmonitor, 1, pm) || pm[0].hPhysicalMonitor == IntPtr.Zero)
        {
            return new BrightnessReading { Supported = false, Detail = "DDC/CI not available (no physical monitor handle; may be disabled in OSD)." };
        }

        try
        {
            if (!GetVCPFeature(pm[0].hPhysicalMonitor, VcpBrightness, out uint current, out uint max))
            {
                return new BrightnessReading { Supported = false, Detail = "Monitor does not report brightness (VCP 0x10 unsupported)." };
            }

            return new BrightnessReading
            {
                Supported = true,
                Current = (int)current,
                Minimum = 0,
                Maximum = (int)max,
                Detail = "DDC/CI brightness available.",
            };
        }
        catch (Exception ex)
        {
            return new BrightnessReading { Supported = false, Detail = $"DDC/CI read failed: {ex.Message}" };
        }
        finally
        {
            DestroyPhysicalMonitors(1, pm);
        }
    }

    public bool SetBrightness(int index, int value)
    {
        if (!_apiAvailable.Value)
        {
            return false;
        }

        IntPtr hmonitor = GetHmonitor(index);
        if (hmonitor == IntPtr.Zero)
        {
            return false;
        }

        var pm = new PHYSICAL_MONITOR[1];
        if (!GetPhysicalMonitorsFromHMONITOR(hmonitor, 1, pm) || pm[0].hPhysicalMonitor == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            uint clamped = (uint)Math.Clamp(value, 0, 100);
            return SetVCPFeature(pm[0].hPhysicalMonitor, VcpBrightness, clamped);
        }
        catch
        {
            return false;
        }
        finally
        {
            DestroyPhysicalMonitors(1, pm);
        }
    }

    private static bool ProbeApiAvailable()
    {
        try
        {
            IntPtr lib = LoadLibrary("dxva2.dll");
            if (lib == IntPtr.Zero)
            {
                return false;
            }

            bool ok =
                GetProcAddress(lib, "GetPhysicalMonitorsFromHMONITOR") != IntPtr.Zero &&
                GetProcAddress(lib, "DestroyPhysicalMonitors") != IntPtr.Zero &&
                GetProcAddress(lib, "GetVCPFeature") != IntPtr.Zero &&
                GetProcAddress(lib, "SetVCPFeature") != IntPtr.Zero;

            FreeLibrary(lib);
            return ok;
        }
        catch
        {
            return false;
        }
    }

    private IntPtr GetHmonitor(int index)
    {
        var collected = new MonitorEnum();
        var handle = GCHandle.Alloc(collected);
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, MonitorEnumCallback, GCHandle.ToIntPtr(handle));
        }
        catch
        {
            return IntPtr.Zero;
        }
        finally
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }

        if (index < 0 || index >= collected.Items.Count)
        {
            return IntPtr.Zero;
        }

        return collected.Items[index].Hmonitor;
    }

    private sealed class MonitorEnum
    {
        public readonly List<(IntPtr Hmonitor, RECT Rect, string Device, bool Primary)> Items = new();
    }

    private static bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData)
    {
        var list = (MonitorEnum?)GCHandle.FromIntPtr(dwData).Target;
        if (list is null)
        {
            return false;
        }

        var info = new MONITORINFOEX
        {
            cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>(),
        };

        try
        {
            if (GetMonitorInfo(hMonitor, info))
            {
                bool primary = (info.dwFlags & 1u) != 0;
                list.Items.Add((hMonitor, lprcMonitor, info.szDevice, primary));
            }
        }
        catch
        {
            // Skip a display we can't describe rather than failing the whole enumeration.
        }

        return true;
    }

    // --- Native types & P/Invoke -------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MONITORINFOEX
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice = string.Empty;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, [In, Out] MONITORINFOEX lpmi);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyPhysicalMonitors(uint dwArraySize, PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVCPFeature(IntPtr hPhysicalMonitor, byte bVCPCode, out uint pdwCurrentValue, out uint pdwMaximumValue);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetVCPFeature(IntPtr hPhysicalMonitor, byte bVCPCode, uint dwNewValue);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpLibFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr hModule);
}
