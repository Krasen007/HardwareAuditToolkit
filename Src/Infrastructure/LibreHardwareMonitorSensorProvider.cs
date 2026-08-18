using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Messaging;
using LibreHardwareMonitor.Hardware;

namespace HardwareAuditToolkit.Infrastructure;

/// <summary>
/// <para>
/// Phase 2 — LibreHardwareMonitorLib-backed <see cref="ISensorProvider"/>.
/// Polls hardware sensors on a background timer and publishes each sample on the
/// event bus as a <see cref="SensorReadingsMessage"/> (architecture §3).
/// </para>
/// <para>
/// All sensor access is best-effort and must not require elevation (Confirmed
/// decisions). If the underlying library fails to open (e.g. restricted by
/// policy), the provider quietly exposes no readings — an empty reading set is
/// "unavailable", never an error (architecture §7 / Phase 7 degradation rule).
/// </para>
/// </summary>
public sealed class LibreHardwareMonitorSensorProvider : ISensorProvider
{
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsMemoryEnabled = true,
        IsMotherboardEnabled = true,
        IsGpuEnabled = true,
        IsStorageEnabled = true,
        IsNetworkEnabled = false,
        IsControllerEnabled = false,
        IsBatteryEnabled = true,
    };

    private readonly List<SensorReading> _latest = new();
    private readonly object _gate = new();
    private Timer? _timer;
    private bool _opened;

    public LibreHardwareMonitorSensorProvider()
    {
        try
        {
            _computer.Open();
            _opened = true;
        }
        catch
        {
            // Best-effort: no sensors available without sufficient privilege.
            _opened = false;
        }
    }

    public void Start()
    {
        if (!_opened)
        {
            return;
        }

        _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public IReadOnlyList<SensorReading> ReadAll()
    {
        lock (_gate)
        {
            return _latest.ToList();
        }
    }

    private void Poll()
    {
        if (!_opened)
        {
            return;
        }

        try
        {
            var readings = new List<SensorReading>();
            _computer.Accept(new UpdateVisitor());
            foreach (var hardware in _computer.Hardware)
            {
                Collect(hardware, readings);
            }

            lock (_gate)
            {
                _latest.Clear();
                _latest.AddRange(readings);
            }

            WeakReferenceMessenger.Default.Send(new SensorReadingsMessage { Readings = readings });
        }
        catch
        {
            // Degrade to "unavailable" rather than crashing the polling loop.
        }
    }

    private static void Collect(IHardware hardware, List<SensorReading> readings)
    {
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.Value is not { } value)
            {
                continue;
            }

            readings.Add(new SensorReading(
                hardware.Name,
                sensor.Name,
                sensor.SensorType.ToString(),
                value,
                UnitFor(sensor.SensorType)));
        }

        foreach (var sub in hardware.SubHardware)
        {
            Collect(sub, readings);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string UnitFor(SensorType type) => type switch
    {
        SensorType.Temperature => "°C",
        SensorType.Load => "%",
        SensorType.Clock => "MHz",
        SensorType.Power => "W",
        SensorType.Voltage => "V",
        SensorType.Fan => "RPM",
        SensorType.Flow => "L/h",
        SensorType.Control => "%",
        SensorType.Level => "%",
        SensorType.Energy => "mWh",
        SensorType.Data => "GB",
        SensorType.Factor => "×",
        SensorType.Frequency => "Hz",
        SensorType.Throughput => "MB/s",
        _ => string.Empty,
    };

    public void Dispose()
    {
        Stop();
        if (_opened)
        {
            try
            {
                _computer.Close();
            }
            catch
            {
                // Best-effort cleanup.
            }

            _opened = false;
        }
    }

    /// <summary>
    /// Refreshes sensor values before they are read (LibreHardwareMonitor sample
    /// pattern). Traverses the hardware tree and updates each node.
    /// </summary>
    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer)
            => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware)
            {
                sub.Accept(this);
            }

            foreach (var sensor in hardware.Sensors)
            {
                sensor.Accept(this);
            }
        }

        public void VisitSensor(ISensor sensor)
        {
            // Values are refreshed by hardware.Update(); nothing else to do.
        }

        public void VisitParameter(IParameter parameter)
        {
            // No-op.
        }
    }
}
