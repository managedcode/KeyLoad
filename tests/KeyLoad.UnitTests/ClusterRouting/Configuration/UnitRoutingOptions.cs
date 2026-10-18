using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

/// <summary>Explicit validated native options for actual Orleans routing regression fixtures.</summary>
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

    internal static IOptions<OrleansMembershipOptions> Membership(OrleansMembershipOptions? configured = null)
    {
        var value = configured ?? new OrleansMembershipOptions();
        value.Validate();
        return Options.Create(value);
    }

}
