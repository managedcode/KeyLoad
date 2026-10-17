using System.Numerics;

namespace KeyLoad;

/// <summary>Requires native .NET vector acceleration before supported database execution.</summary>
public static class SimdRuntime
{
    private const string HardwareRequired = "Native SIMD hardware acceleration is required.";

    /// <summary>Rejects unsupported hardware without choosing a full scalar execution mode.</summary>
    /// <exception cref="PlatformNotSupportedException">Native vector acceleration is unavailable.</exception>
    public static void RequireHardwareSupport()
    {
        if (!Vector.IsHardwareAccelerated)
        {
            throw new PlatformNotSupportedException(HardwareRequired);
        }
    }
}
