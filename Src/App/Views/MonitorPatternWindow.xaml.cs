using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HardwareAuditToolkit.Infrastructure;

namespace HardwareAuditToolkit.App.Views;

/// <summary>
/// <para>
/// Phase 5 — fullscreen pattern window (architecture §10 Phase 5, §6). Renders a
/// solid/gradient/grid/crosshatch pattern edge-to-edge on the selected display
/// for dead-pixel and uniformity inspection. It uses no native window chrome and
/// relies on the auto-hiding "Back to controls" button (mouse-only path) plus the
/// reusable Exit overlay (§6) — Ctrl+E and the global Exit Test button still work
/// because the low-level hook runs on its own thread (§9.2).
/// </para>
/// <para>
/// Placement uses <c>SetWindowPos</c> in raw device pixels (from
/// <see cref="MonitorInfo"/>) so the window lands exactly on the target monitor
/// regardless of mixed-DPI setups; Per-Monitor V2 then scales the content to that
/// monitor's DPI automatically (§9.4, DoD).
/// </para>
/// </summary>
public partial class MonitorPatternWindow : Window
{
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private static readonly IntPtr HwndTopMost = new(-1);

    private readonly MonitorInfo _monitor;
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    public MonitorPatternWindow(MonitorInfo monitor, string pattern)
    {
        InitializeComponent();
        _monitor = monitor;

        _hideTimer.Tick += (_, _) =>
        {
            OverlayPanel.Visibility = Visibility.Collapsed;
            _hideTimer.Stop();
        };

        ApplyPattern(pattern);
        MouseMove += OnMouseMove;
        Loaded += (_, _) => _hideTimer.Start();
    }

    /// <summary>
    /// Places the window on the target display in raw device pixels once the
    /// handle exists, so it covers that monitor exactly across mixed-DPI fleets.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        SetWindowPos(
            hwnd,
            HwndTopMost,
            _monitor.Left,
            _monitor.Top,
            _monitor.Width,
            _monitor.Height,
            SwpNoActivate | SwpFrameChanged);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        OverlayPanel.Visibility = Visibility.Visible;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
        => Close();

    private void ApplyPattern(string pattern)
    {
        Brush brush = pattern.Trim() switch
        {
            "Solid — White" => Brushes.White,
            "Solid — Black" => Brushes.Black,
            "Solid — Red" => Brushes.Red,
            "Solid — Green" => Brushes.Green,
            "Solid — Blue" => Brushes.Blue,
            "Solid — Gray" => Brushes.Gray,
            "Gradient — Horizontal" => new LinearGradientBrush(Colors.Black, Colors.White, 0),
            "Grid lines" => MakeGridBrush(),
            "Crosshatch" => MakeCrosshatchBrush(),
            _ => Brushes.Black,
        };

        PatternBorder.Background = brush;
    }

    private static Brush MakeGridBrush()
    {
        const double tile = 64;
        var group = new GeometryGroup();
        group.Children.Add(new LineGeometry(new Point(0, 0), new Point(tile, 0)));
        group.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, tile)));

        var drawing = new GeometryDrawing(Brushes.Transparent, new Pen(Brushes.DimGray, 1), new RectangleGeometry(new Rect(0, 0, tile, tile)));
        var brush = new DrawingBrush
        {
            Drawing = drawing,
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, tile, tile),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        return brush;
    }

    private static Brush MakeCrosshatchBrush()
    {
        const double tile = 64;
        var group = new GeometryGroup();
        group.Children.Add(new LineGeometry(new Point(0, 0), new Point(tile, tile)));
        group.Children.Add(new LineGeometry(new Point(tile, 0), new Point(0, tile)));

        var drawing = new GeometryDrawing(Brushes.Transparent, new Pen(Brushes.DimGray, 1), new RectangleGeometry(new Rect(0, 0, tile, tile)));
        var brush = new DrawingBrush
        {
            Drawing = drawing,
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, tile, tile),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        return brush;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
