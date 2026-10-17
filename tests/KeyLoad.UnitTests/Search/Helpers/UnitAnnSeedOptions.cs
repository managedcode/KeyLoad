using KeyLoad.Core.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

internal static class UnitAnnSeedOptions
{
    internal static IOptions<AnnSeedOptions> Execution(AnnSeedOptions? configured = null)
    {
        var value = configured ?? new AnnSeedOptions();
        value.Validate();
        return Options.Create(value);
    }
}
