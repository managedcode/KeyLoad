using System.Collections.Immutable;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.GraphTraversal;

namespace KeyLoad.Server.Features.GraphTraversal;

/// <summary>Matches old and fresh targets only after genuine native preparation proves their exact lifetime.</summary>
internal static class RemoteGraphParentPreparation
{
    private const int FirstPreparation = 0;

    internal static async Task<ImmutableArray<RemoteGraphVerifiedPreparationV1>> ExecuteAsync(OrleansNode node,
        PartitionHost partition, PrincipalRecord principal, GrainRequestEnvelope trusted,
        RemoteGraphSourceIdentityV1 original, RemoteGraphSourceSnapshotV1 current,
        RegisteredPhysicalOwnerV1 source, ReadExecutionBudget work, CancellationToken token)
    {
        var values = ImmutableArray.CreateBuilder<RemoteGraphVerifiedPreparationV1>();
        EntityRef? next = null;
        if (original.Upsert?.To is { } requested)
        {
            var fresh = await ReadOneAsync(node, partition, principal, trusted, original, source,
                requested, deleted: false, work, token).ConfigureAwait(false);
            next = RemoteGraphLifetimeAdmission.BindDestination(fresh.Preparation);
            values.Add(fresh);
        }
        if (current.Edge?.To is { } previous && previous.Partition != original.SourcePartition && previous != next)
        {
            var removed = await ReadOneAsync(node, partition, principal, trusted, original, source,
                previous, deleted: true, work, token).ConfigureAwait(false);
            values.Insert(FirstPreparation, removed);
        }
        return values.ToImmutable();
    }

    private static async Task<RemoteGraphVerifiedPreparationV1> ReadOneAsync(OrleansNode node,
        PartitionHost partition, PrincipalRecord principal, GrainRequestEnvelope trusted,
        RemoteGraphSourceIdentityV1 original, RegisteredPhysicalOwnerV1 source, EntityRef destination,
        bool deleted, ReadExecutionBudget work, CancellationToken token)
    {
        work.Check();
        token.ThrowIfCancellationRequested();
        var graph = original.Upsert?.Graph ?? original.Delete!.Graph;
        var route = RemoteGraphParentRouting.Resolve(node, partition, principal,
            original.SourcePartition, destination, graph, work);
        var target = route.Destination
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphNativeProtocol.Unavailable);
        var request = new RemoteGraphPreparationRequestV1(original.CommandId, principal.Id,
            original.SourcePartition, destination, graph, deleted, source.Owner, target);
        var prepared = await RemoteGraphParentCall.RemoteAsync<RemoteGraphTargetPreparationV1>(node,
            partition, principal, target, RemoteGraphNativeAction.TargetPreparation, request,
            trusted.ExpiresAt, work, token).ConfigureAwait(false);
        return new(prepared, string.Empty);
    }
}
