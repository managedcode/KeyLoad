using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

/// <summary>Validated native observation policy for actual filesystem regression tests.</summary>
internal static class UnitAdminObservationOptions
{
    internal static IOptions<AdminObservationOptions> Execution(AdminObservationOptions? configured = null)
    {
        var value = configured ?? new AdminObservationOptions();
        value.Validate();
        return Options.Create(value);
    }
}
