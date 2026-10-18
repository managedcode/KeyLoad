using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using K = KeyLoad.UnitTests.Features.ClusterRouting.NativeLocalChildLifetimeProtocol;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeLocalChildLifetimeReferences
{
    internal static EntityRef Expected(NativeLocalChildLifetimeSeeded state, string id)
        => new(K.Partition, K.Collection, id)
        {
            Lifetime = new(ExpectedLifetime(state).Incarnation.Value)
        };

    internal static ResourceLifetimeV1 ExpectedLifetime(NativeLocalChildLifetimeSeeded state)
    {
        var request = NativeSerialization.Deserialize<ConfigureResourceRequest>(state.ResourceCreation.Value!.Input.Span);
        return ResourceLifetimeIdentity.Create(state.ResourceCreation, request.TenantId, request.DatabaseId, request.Definition);
    }
}
