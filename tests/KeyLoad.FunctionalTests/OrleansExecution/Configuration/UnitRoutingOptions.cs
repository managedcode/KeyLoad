using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.FunctionalTests;

/// <summary>Explicit validated execution options for actual Orleans routing tests.</summary>
internal static class UnitRoutingOptions
{
    internal static IOptions<GrainRoutingOptions> Routing(GrainRoutingOptions? configured = null)
    {
        var value = configured ?? new GrainRoutingOptions();
        if (!value.IsValid())
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(GrainRoutingOptions), [GrainRoutingOptions.ValidationMessage]);
        }
        return Options.Create(value);
    }
}
