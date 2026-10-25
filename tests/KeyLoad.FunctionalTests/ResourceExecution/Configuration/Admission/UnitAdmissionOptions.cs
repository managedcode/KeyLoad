using Microsoft.Extensions.Options;

namespace KeyLoad.FunctionalTests;

/// <summary>Explicit validated execution options for cache policy fixtures.</summary>
internal static class UnitAdmissionOptions
{

    internal static IOptions<CacheMemoryLimits> Cache(CacheMemoryLimits? configured = null)
    {
        var value = configured ?? new CacheMemoryLimits();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<CacheReadPermitOptions> Permit(CacheReadPermitOptions? configured = null)
    {
        var value = configured ?? new CacheReadPermitOptions();
        value.Validate();
        return Options.Create(value);
    }
}
