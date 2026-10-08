using KeyLoad.Core.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

/// <summary>Composes validated downward seed limits from the original central policy and resident reservation.</summary>
[ConfigurationBinding]
internal static class NativeAnnSeedOptionsFactory
{
    private const int MinimumBytes = 1_024;
    private const int HeadroomDivisor = 2;

    internal static IOptions<AnnSeedOptions> Create(IOptions<AnnSeedOptions> configured, long available)
    {
        var original = configured.Value;
        original.Validate();
        if (available / HeadroomDivisor < MinimumBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, NativeAnnProtocol.Bound); }
        var bounded = original with
        {
            MaxOwnedBytes = Math.Min(original.MaxOwnedBytes, available / HeadroomDivisor),
            MaxPeakBytes = Math.Min(original.MaxPeakBytes, available)
        };
        bounded.Validate();
        return Options.Create(bounded);
    }
}
