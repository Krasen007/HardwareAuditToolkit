namespace HardwareAuditToolkit.Infrastructure;

/// <summary>
/// A single raw keyboard sample, surfaced scan-code first so physical-layout
/// detection is robust (architecture §10 Phase 3). A key is identified by a
/// composite scan-code id (see <see cref="RawKeyboardInput"/>), not the virtual
/// key, so the same physical key resolves identically regardless of the active
/// keyboard layout / modifiers.
/// </summary>
public sealed class RawKeySample
{
    /// <summary>Composite scan-code id: <c>0xE000 | makeCode</c> for extended
    /// keys, otherwise the raw make code. Stable per physical key.</summary>
    public int ScanCodeId { get; init; }

    /// <summary>The Windows virtual key code (informational only).</summary>
    public uint VirtualKey { get; init; }

    /// <summary>True when the sample carried the E0 prefix.</summary>
    public bool IsExtended { get; init; }

    /// <summary>True for a key-down (make); false for key-up (break).</summary>
    public bool IsKeyDown { get; init; }
}

/// <summary>
/// Captures raw keyboard input independent of focus and of the active input
/// locale. Implementations must not block the calling thread in their input
/// callback — parse and raise <see cref="KeyReceived"/> promptly so the WPF
/// dispatcher stays responsive (architecture §9.2 / Phase 3 gotchas).
/// </summary>
public interface IRawKeyboardInput
{
    /// <summary>Raised for each raw keyboard event captured while active.</summary>
    event EventHandler<RawKeySample>? KeyReceived;

    /// <summary>Begin capturing raw keyboard input (idempotent).</summary>
    void Start();

    /// <summary>Stop capturing and release the underlying window/registration.</summary>
    void Stop();
}
