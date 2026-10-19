using KeyLoad.Core.Features.ResourceExecution;
using K = KeyLoad.UnitTests.Features.OrleansExecution.BoundLeaseProtocol;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class BoundLeaseReferences
{
    internal static EntityRef Expected(BoundLeaseSeeded state, string id)
        => new(K.Partition, K.Collection, id)
        {
            Lifetime = new(ExpectedLifetime(state).Incarnation.Value)
        };

    internal static ResourceLifetime ExpectedLifetime(BoundLeaseSeeded state)
    {
        var request = OrleansSerialization.Deserialize<ConfigureResourceRequest>(state.ResourceCreation.Value!.Input.Span);
        return ResourceLifetimeIdentity.Create(state.ResourceCreation, request.TenantId, request.DatabaseId, request.Definition);
    }
}
