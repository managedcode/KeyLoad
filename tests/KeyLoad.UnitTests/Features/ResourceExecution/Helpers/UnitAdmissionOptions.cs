using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

/// <summary>Explicit validated native options for admission and cache policy regression fixtures.</summary>
internal static class UnitAdmissionOptions
{
    internal static IOptions<CommandAdmissionLimits> Command(CommandAdmissionLimits? configured = null)
    {
        var value = configured ?? new CommandAdmissionLimits();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<HttpAdmissionLimits> Http(HttpAdmissionLimits? configured = null)
    {
        var value = configured ?? new HttpAdmissionLimits();
        value.Validate();
        return Options.Create(value);
    }

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
