using System.Numerics;

namespace KeyLoad;

/// <summary>Requires execution .NET vector acceleration before supported database execution.</summary>
public static class SimdRuntime
{
    private const string HardwareRequired = "Execution SIMD hardware acceleration is required.";

    /// <summary>Rejects unsupported hardware without choosing a full scalar execution mode.</summary>
    /// <exception cref="PlatformNotSupportedException">Execution vector acceleration is unavailable.</exception>
    public static void RequireHardwareSupport()
    {
        if (!Vector.IsHardwareAccelerated)
        {
            throw new PlatformNotSupportedException(HardwareRequired);
        }
    }
}
