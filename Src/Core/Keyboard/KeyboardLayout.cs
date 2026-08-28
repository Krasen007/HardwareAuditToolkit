namespace HardwareAuditToolkit.Core.Keyboard;

/// <summary>
/// A single key in the on-screen vector layout. Positions are expressed in
/// keyboard "units" (1u ≈ one standard keycap); the view converts to pixels.
/// <see cref="Id"/> is the composite scan-code id produced by
/// <see cref="Infrastructure.RawKeyboardInput"/>, so a physical keypress maps
/// directly to its tile.
/// </summary>
public sealed class KeyLayoutDef(int id, string label, int row, double x, double width, double height = 1)
{
    public int Id { get; } = id;

    public string Label { get; } = label;

    public int Row { get; } = row;

    public double X { get; } = x;

    public double Width { get; } = width;

    public double Height { get; } = height;
}

/// <summary>
/// <para>
/// Phase 3 — data-driven ANSI (US, 104-key) vector keyboard layout (architecture
/// §10 Phase 3). Both the test grid rendering and the per-key untested → pressed
/// → confirmed tracking are driven from this single map; non-US layouts are an
/// explicit v2 deferral.
/// </para>
/// <para>
/// Key ids are composite scan codes: <c>0xE000 | makeCode</c> for E0-prefixed
/// keys, otherwise the raw make code. Labels are short to fit the tiles.
/// </para>
/// </summary>
public static class KeyboardLayout
{
    /// <summary>The ANSI layout, in render order.</summary>
    public static IReadOnlyList<KeyLayoutDef> Ansi { get; } = Build();

    /// <summary>Total expected keys — the pass-coverage denominator.</summary>
    public static int ExpectedCount => Ansi.Count;

    /// <summary>Resolves a layout label for a composite scan-code id, or null.</summary>
    public static string? GetLabel(int id)
        => Ansi.FirstOrDefault(k => k.Id == id)?.Label;

    private static int C(int scan, bool extended = false)
        => extended ? (0xE000 | scan) : scan;

    private static List<KeyLayoutDef> Build()
    {
        var keys = new List<KeyLayoutDef>();

        // (label, id, width, gapBefore) — x is accumulated per row.
        void Row(int row, params (string Label, int Id, double Width, double Gap)[] entries)
        {
            double x = 0;
            foreach (var (Label, Id, Width, Gap) in entries)
            {
                x += Gap;
                keys.Add(new KeyLayoutDef(Id, Label, row, x, Width));
                x += Width;
            }
        }

        // Row 0 — function row + PrtSc/Scroll/Pause (right cluster).
        Row(0,
            ("Esc", C(0x01), 1, 0),
            ("F1", C(0x3B), 1, 1),
            ("F2", C(0x3C), 1, 0),
            ("F3", C(0x3D), 1, 0),
            ("F4", C(0x3E), 1, 0),
            ("F5", C(0x3F), 1, 0.5),
            ("F6", C(0x40), 1, 0),
            ("F7", C(0x41), 1, 0),
            ("F8", C(0x42), 1, 0),
            ("F9", C(0x43), 1, 0.5),
            ("F10", C(0x44), 1, 0),
            ("F11", C(0x57), 1, 0),
            ("F12", C(0x58), 1, 0),
            // Top-right cluster sits directly above Ins/Home/PgUp (x ≈ 15.5–18.5).
            ("PrtSc", C(0x37, true), 1, 0.5),
            ("ScrLk", C(0x46), 1, 0),
            ("Pause", C(0x45, true), 1, 0));

        // Row 1 — number row + nav (Ins/Home/PgUp) + numpad (NumLk / * -).
        Row(1,
            ("`", C(0x29), 1, 0),
            ("1", C(0x02), 1, 0),
            ("2", C(0x03), 1, 0),
            ("3", C(0x04), 1, 0),
            ("4", C(0x05), 1, 0),
            ("5", C(0x06), 1, 0),
            ("6", C(0x07), 1, 0),
            ("7", C(0x08), 1, 0),
            ("8", C(0x09), 1, 0),
            ("9", C(0x0A), 1, 0),
            ("0", C(0x0B), 1, 0),
            ("-", C(0x0C), 1, 0),
            ("=", C(0x0D), 1, 0),
            ("Backspace", C(0x0E), 2, 0),
            ("Insert", C(0x52, true), 1, 0.5),
            ("Home", C(0x47, true), 1, 0),
            ("Page Up", C(0x49, true), 1, 0),
            ("NumLk", C(0x45), 1, 0.5),
            ("/", C(0x35, true), 1, 0),
            ("*", C(0x37), 1, 0),
            ("-", C(0x4A), 1, 0));

        // Row 2 — QWERTY top + nav (Del/End/PgDn) + numpad (7 8 9 + spanning 2 rows).
        Row(2,
            ("Tab", C(0x0F), 1.5, 0),
            ("Q", C(0x10), 1, 0),
            ("W", C(0x11), 1, 0),
            ("E", C(0x12), 1, 0),
            ("R", C(0x13), 1, 0),
            ("T", C(0x14), 1, 0),
            ("Y", C(0x15), 1, 0),
            ("U", C(0x16), 1, 0),
            ("I", C(0x17), 1, 0),
            ("O", C(0x18), 1, 0),
            ("P", C(0x19), 1, 0),
            ("[", C(0x1A), 1, 0),
            ("]", C(0x1B), 1, 0),
            ("\\", C(0x2B), 1.5, 0),
            ("Delete", C(0x53, true), 1, 0.5),
            ("End", C(0x4F, true), 1, 0),
            ("Page Down", C(0x51, true), 1, 0),
            ("7", C(0x47), 1, 0.5),
            ("8", C(0x48), 1, 0),
            ("9", C(0x49), 1, 0),
            ("+", C(0x4E), 1, 0));

        // Row 3 — home row + numpad (4 5 6).
        Row(3,
            ("CapsLk", C(0x3A), 1.75, 0),
            ("A", C(0x1E), 1, 0),
            ("S", C(0x1F), 1, 0),
            ("D", C(0x20), 1, 0),
            ("F", C(0x21), 1, 0),
            ("G", C(0x22), 1, 0),
            ("H", C(0x23), 1, 0),
            ("J", C(0x24), 1, 0),
            ("K", C(0x25), 1, 0),
            ("L", C(0x26), 1, 0),
            (";", C(0x27), 1, 0),
            ("'", C(0x28), 1, 0),
            ("Enter", C(0x1C), 2.25, 0),
            ("4", C(0x4B), 1, 4),
            ("5", C(0x4C), 1, 0),
            ("6", C(0x4D), 1, 0));

        // Row 4 — bottom letter row + Up arrow + numpad (1 2 3 Enter spanning 2).
        Row(4,
            ("LShift", C(0x2A), 2.25, 0),
            ("Z", C(0x2C), 1, 0),
            ("X", C(0x2D), 1, 0),
            ("C", C(0x2E), 1, 0),
            ("V", C(0x2F), 1, 0),
            ("B", C(0x30), 1, 0),
            ("N", C(0x31), 1, 0),
            ("M", C(0x32), 1, 0),
            (",", C(0x33), 1, 0),
            (".", C(0x34), 1, 0),
            ("/", C(0x35), 1, 0),
            ("RShift", C(0x36), 2.75, 0),
            ("Up", C(0x48, true), 1, 1.5),
            ("1", C(0x4F), 1, 1.5),
            ("2", C(0x50), 1, 0),
            ("3", C(0x51), 1, 0),
            ("Enter", C(0x1C, true), 1, 0));

        // Row 5 — modifiers + Left/Down/Right + numpad (0 .)
        Row(5,
            ("LCtrl", C(0x1D), 1.25, 0),
            ("LWin", C(0x5B, true), 1.25, 0),
            ("LAlt", C(0x38), 1.25, 0),
            ("Space", C(0x39), 6.25, 0),
            ("RAlt", C(0x38, true), 1.25, 0),
            ("RWin", C(0x5C, true), 1.25, 0),
            ("RCtrl", C(0x1D, true), 1.25, 0),
            ("Left", C(0x4B, true), 1, 1.75),
            ("Down", C(0x50, true), 1, 0),
            ("Right", C(0x4D, true), 1, 0),
            ("0", C(0x52), 2, 0.5),
            (".", C(0x53), 1, 0));

        return keys;
    }
}
