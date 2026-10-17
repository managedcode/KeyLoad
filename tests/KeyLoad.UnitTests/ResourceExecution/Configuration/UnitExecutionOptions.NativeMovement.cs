using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

/// <summary>Unit-only composition for the canonical bounded native movement process policy.</summary>
internal static partial class UnitExecutionOptions
{
    internal static IOptions<NativeMovementProcessOptions> NativeMovementProcess(NativeMovementProcessOptions? configured = null)
    {
        var value = configured ?? new NativeMovementProcessOptions();
        value.Validate();
        return Options.Create(value);
    }
}
