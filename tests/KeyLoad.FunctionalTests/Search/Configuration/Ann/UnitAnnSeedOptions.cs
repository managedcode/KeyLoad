using KeyLoad.Core.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.FunctionalTests;

internal static class UnitAnnSeedOptions
{
    internal static IOptions<AnnSeedOptions> Execution(AnnSeedOptions? configured = null)
    {
        var value = configured ?? new AnnSeedOptions();
        value.Validate();
        return Options.Create(value);
    }
}
