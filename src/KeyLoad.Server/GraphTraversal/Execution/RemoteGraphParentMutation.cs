using System.Collections.Immutable;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.GraphTraversal;

internal static class RemoteGraphParentMutation
{
    private const int RequiredDeliveryIntentCount = 1;
    internal static async Task<RemoteGraphEdgeResultV1> ExecuteAsync(OrleansNode node, PartitionHost partition,
        PrincipalRecord principal, GrainRequestEnvelope trusted, OperationKind kind, ReadOnlyMemory<byte> payload,
        RemoteGraphEffectBoundary effects, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        if (kind == OperationKind.DeliverRemoteGraphEdge)
        {
            return await DeliverAsync(node, partition, principal, trusted, payload, effects, work, binding, token).ConfigureAwait(false);
        }
        var original = Original(kind, payload);
        if (original.CommandId != trusted.CommandId)
        {
            throw Errors.Fail(ErrorCode.Validation, RemoteGraphNativeProtocol.InvalidProof);
        }
        var graph = original.Upsert?.Graph ?? original.Delete!.Graph;
        var edgeId = original.Upsert?.EdgeId ?? original.Delete!.EdgeId;
        var initial = RemoteGraphParentRouting.Resolve(node, partition, principal,
            original.SourcePartition, null, graph, work);
        var query = new RemoteGraphSourceReadRequestV1(original.CommandId, original.SourcePartition,
            graph, edgeId, initial.Source.Owner, original);
        var current = await SnapshotAsync(node, partition, principal, trusted, query, work, binding, token).ConfigureAwait(false);
        RemoteGraphSourceResultV1 actual;
        if (current.OriginalOutcome is { } retained)
        {
            var previous = retained.Result.Get<RemoteGraphSourceResultV1>();
            effects.Begin(token);
            var replay = partition.Database.CreateVerifiedRemoteGraphSource(principal.Id,
                previous.Original, trusted.ExpiresAt, work);
            actual = await RemoteGraphParentCall.CommandAsync<RemoteGraphSourceResultV1>(node, partition,
                principal, replay, trusted.ExpiresAt, effects, work, token).ConfigureAwait(false);
        }
        else
        {
            var preparations = await RemoteGraphParentPreparation.ExecuteAsync(node, partition, principal, trusted, original,
                current, initial.Source, work, token).ConfigureAwait(false);
            var mutation = new RemoteGraphSourceMutationV1(RemoteGraphProtocol.Version, original,
                current.OwnerRevision, preparations, current.Edge);
            var operation = partition.Database.CreateVerifiedRemoteGraphSource(principal.Id,
                mutation, trusted.ExpiresAt, work);
            actual = await RemoteGraphParentCall.CommandAsync<RemoteGraphSourceResultV1>(node, partition,
                principal, operation, trusted.ExpiresAt, effects, work, token).ConfigureAwait(false);
            current = await SnapshotAsync(node, partition, principal, trusted, query, work, binding, token).ConfigureAwait(false);
        }
        return await RemoteGraphParentDelivery.ExecuteAsync(node, partition, principal, trusted,
            original.CommandId, current.Cut, actual, effects, work, token).ConfigureAwait(false);
    }

    private static async Task<RemoteGraphEdgeResultV1> DeliverAsync(OrleansNode node, PartitionHost partition,
        PrincipalRecord principal, GrainRequestEnvelope trusted, ReadOnlyMemory<byte> payload,
        RemoteGraphEffectBoundary effects, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        var delivery = GrainNativePayload.ReadCommand<DeliverRemoteGraphEdgeRequestV1>(payload);
        if (delivery.CommandId != trusted.CommandId)
        {
            throw Errors.Fail(ErrorCode.Validation, RemoteGraphNativeProtocol.InvalidProof);
        }
        var route = RemoteGraphParentRouting.Resolve(node, partition, principal, delivery.SourcePartition,
            delivery.Destination, delivery.Graph, work);
        var read = new RemoteGraphSourceReadRequestV1(Guid.Empty, delivery.SourcePartition,
            delivery.Graph, delivery.EdgeId, route.Source.Owner, null, delivery);
        var snapshot = await SnapshotAsync(node, partition, principal, trusted, read, work, binding, token).ConfigureAwait(false);
        var source = snapshot.OriginalOutcome?.Result.Get<RemoteGraphSourceResultV1>()
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteGraphNativeProtocol.InvalidProof);
        var selected = source.Intents.Where(value => (value.Content.Destination with
        {
            Lifetime = null
        }) == (delivery.Destination with
        {
            Lifetime = null
        })
            && RemoteGraphLifetimeAdmission.BindOriginalDestination(value, delivery.Destination) == value.Content.Destination
            && value.Content.Revision == delivery.ExpectedRevision).ToImmutableArray();
        if (selected.Length != RequiredDeliveryIntentCount)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteGraphNativeProtocol.InvalidProof);
        }
        effects.Begin(token);
        return await RemoteGraphParentDelivery.ExecuteAsync(node, partition, principal, trusted,
            delivery.CommandId, snapshot.Cut, source with
            {
                Intents = selected
            }, effects, work, token).ConfigureAwait(false);
    }

    private static RemoteGraphSourceIdentityV1 Original(OperationKind kind, ReadOnlyMemory<byte> payload)
    {
        if (kind == OperationKind.UpsertRemoteGraphEdge)
        {
            var request = GrainNativePayload.ReadCommand<UpsertRemoteGraphEdgeRequestV1>(payload);
            return new(request.CommandId, request.SourcePartition, request.Edge, null);
        }
        if (kind == OperationKind.DeleteRemoteGraphEdge)
        {
            var request = GrainNativePayload.ReadCommand<DeleteRemoteGraphEdgeRequestV1>(payload);
            return new(request.CommandId, request.SourcePartition, null,
                new DeleteEdge(request.Graph, request.EdgeId, request.ExpectedRevision));
        }
        throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteGraphNativeProtocol.InvalidProof);
    }

    private static Task<RemoteGraphSourceSnapshotV1> SnapshotAsync(OrleansNode node, PartitionHost partition,
        PrincipalRecord principal, GrainRequestEnvelope trusted, RemoteGraphSourceReadRequestV1 query,
        ReadExecutionBudget work, IResourceLifetimeWorkBinding binding, CancellationToken token)
        => RemoteGraphParentCall.ReadAsync<RemoteGraphSourceSnapshotV1>(node, partition, principal,
            GrainReadKind.RemoteGraphSourceIntent, query, trusted.ExpiresAt, work, binding, token);

}
