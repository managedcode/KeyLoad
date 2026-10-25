using KeyLoad.FunctionalTests.StorageRecovery;
using Microsoft.Extensions.Options;

namespace KeyLoad.FunctionalTests;

/// <summary>Composes only the bounded complete recovery-image capture policy.</summary>
internal static partial class UnitExecutionOptions
{
    internal static IOptions<RecoveryImageCaptureOptions> RecoveryImageCapture(RecoveryImageCaptureOptions? configured = null)
    {
        var value = configured ?? new RecoveryImageCaptureOptions();
        value.Validate();
        return Options.Create(value);
    }
}
