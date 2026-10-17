using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Typed timer policy for the real held-response operation and original caller.</summary>
[ConfigurationOptions]
internal sealed class SdkResponseTailOptions
{
    private const int TimeoutSeconds = 30;
    private const string Invalid = "The SDK response-tail fixture deadline is invalid.";
    private static readonly TimeSpan MaximumTimeout = TimeSpan.FromSeconds(TimeoutSeconds);
    internal TimeSpan OperationTimeout
    {
        get;
        init;
    } = MaximumTimeout;

    [ConfigurationBinding]
    internal static IOptions<SdkResponseTailOptions> Capture()
    {
        var configured = new SdkResponseTailOptions();
        if (configured.OperationTimeout <= TimeSpan.Zero || configured.OperationTimeout > MaximumTimeout)
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(SdkResponseTailOptions), [Invalid]);
        }
        return Options.Create(configured);
    }
}
