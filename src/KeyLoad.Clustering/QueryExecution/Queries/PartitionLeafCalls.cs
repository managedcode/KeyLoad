using KeyLoad.Core;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

/// <summary>Captures, routes, calls and settles one original request's partition leaves on their storage owners.</summary>
internal sealed class PartitionLeafCalls(DatabaseEngine database, string principalId, IServiceProvider services,
    DecodedGrainRequest original)
{
    private readonly IGrainFactory grains = services.GetRequiredService<IGrainFactory>();

    internal IPartitionQueryGrain Target(PartitionRef partition)
        => grains.GetGrain<IPartitionQueryGrain>(PartitionQueryGrainIdentity.For(partition));

    internal PartitionQueryLifetimeMetadata CaptureMetadata(PartitionRef partition, string collection, ReadExecutionBudget parent)
        => database.WithQueryView(principalId, partition, collection, Capability.None,
            (view, principal, definition) => new PartitionQueryLifetimeMetadata(definition,
                parent.BindResourceLifetimes(view, partition, [definition]), principal), parent);

    internal async Task<(SiloAddress Address, PhysicalShardRecord Owner)> ResolveOwnerAsync(IPartitionQueryGrain target,
        ReadExecutionBudget parent, CancellationToken token)
    {
        var selected = await target.ResolveOwnerAsync(database.Store.Identity.Incarnation, token).ConfigureAwait(true);
        parent.Check();
        if (selected.Address is null || selected.Owner is null || selected.Owner.PhysicalShardId == Guid.Empty
            || selected.Owner.Incarnation != database.Store.Identity.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        return selected;
    }

    internal async Task<TResult> RunAsync<TResult>(IPartitionQueryGrain target, SiloAddress address,
        GrainRequestEnvelope child, Guid childId, CancellationToken token) where TResult : class
    {
        using var identity = GrainRequestIdentityScope.ForChild(services, original.Envelope, childId, Guid.Empty, token);
        var reply = await target.ExecuteAsync(address, child, token).ConfigureAwait(true);
        token.ThrowIfCancellationRequested();
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? GrainRoutingProtocol.InvalidRequest);
        }
        return GrainReplyFactory.RequireValue(reply).Value as TResult
            ?? throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
    }

    internal static Task CompleteAsync(ReadExecutionBudget parent, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        parent.Check();
        return Task.CompletedTask;
    }
}
