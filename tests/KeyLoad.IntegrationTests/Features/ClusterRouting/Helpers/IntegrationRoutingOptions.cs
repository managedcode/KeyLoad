using KeyLoad.Orleans;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests;

/// <summary>Explicit validated native options for actual Orleans routing regression fixtures.</summary>
internal static class IntegrationRoutingOptions
{
    internal static IOptions<GrainRoutingOptions> Routing(GrainRoutingOptions? configured = null)
    {
        var value = configured ?? new GrainRoutingOptions();
        if (!value.IsValid())
        { throw new OptionsValidationException(Options.DefaultName, typeof(GrainRoutingOptions), [GrainRoutingOptions.ValidationMessage]); }
        return Options.Create(value);
    }

    internal static IOptions<PeerDiscoveryOptions> Discovery(PeerDiscoveryOptions? configured = null)
    {
        var value = configured ?? new PeerDiscoveryOptions();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<ReplicaTransportOptions> Transport(ReplicaTransportOptions? configured = null)
    {
        var value = configured ?? new ReplicaTransportOptions();
        if (!value.IsValid())
        { throw new OptionsValidationException(Options.DefaultName, typeof(ReplicaTransportOptions), [ReplicaTransportOptions.ValidationMessage]); }
        return Options.Create(value);
    }
}
