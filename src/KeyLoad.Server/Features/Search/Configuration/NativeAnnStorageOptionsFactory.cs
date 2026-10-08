using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

/// <summary>Composes only validated downward native storage ceilings from central options and actual reservations.</summary>
[ConfigurationBinding]
internal static class NativeAnnStorageOptionsFactory
{
    internal static IOptions<PackedAnnStorageOptions> ForFile(IOptions<PackedAnnStorageOptions> configured,
        long available)
    {
        var original = configured.Value;
        original.Validate();
        var bounded = original with { MaxFileBytes = Math.Min(original.MaxFileBytes, available) };
        bounded.Validate();
        return Options.Create(bounded);
    }

    internal static IOptions<PackedAnnStorageOptions> ForPeak(IOptions<PackedAnnStorageOptions> configured,
        long available)
    {
        var original = configured.Value;
        original.Validate();
        var bounded = original with { MaxPeakBytes = Math.Min(original.MaxPeakBytes, available) };
        bounded.Validate();
        return Options.Create(bounded);
    }
}
