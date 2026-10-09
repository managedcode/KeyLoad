using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Owns context responsibility while borrowing the single native receiver owner and original operation tokens.</summary>
internal sealed class PartitionMovementReceiverContext(OrleansNode node, PartitionHost partition, IOptions<NodeOptions> options, TimeProvider clock, Func<PartitionMovementSourceOwner> source)
{
    internal OrleansNode Node => node;
    internal PartitionHost Partition => partition;
    internal IOptions<NodeOptions> Options => options;
    internal TimeProvider Clock => clock;
    internal PartitionMovementSourceOwner Source() => source();
    internal async Task<GrainOperationReply> ExecuteOutcomeAsync(PartitionMovementOutcomeTransportRequest query,
        CancellationToken cancellationToken)
    {
        var principal = Administrator(PartitionMovementControlPrincipal.Resolve(partition.Database,
            query.Original.Envelope), cancellationToken);
        var requestId = Guid.NewGuid();
        var capability = new PartitionMovementOutcomeQuery(query.Original.CommandId, query.Original.Envelope,
            query.MaximumReadBytes, query.MaximumExaminedRecords, query.MaximumResultBytes);
        var signed = node.CatalogRequestCodec().CreatePartitionMovementOutcome(requestId, principal.Id,
            capability, query.ExpiresAt);
        return await ExecuteSignedAsync(principal, requestId, Guid.Empty, signed, command: false,
            cancellationToken).ConfigureAwait(false);
    }

    internal ReplicaSiloDiscovery Discovery() => node.Discovery?.Read()
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch);

    internal PhysicalShardRecord LocalOwner() => PhysicalOwnerConfiguredTuples.Local(options.Value, partition).Owner;

    internal async Task<GrainOperationReply> ApplyAsync(Guid commandId, PartitionMovePeerEnvelope verified,
        CancellationToken cancellationToken)
    {
        var principal = Administrator(PartitionMovementControlPrincipal.Resolve(partition.Database, verified), cancellationToken);
        var operation = verified.Stage == PartitionMovePeerStage.ControlCheckpoint
            ? partition.Database.CreateVerifiedPartitionMovementCheckpointOperation(commandId, principal.Id,
                clock.GetUtcNow(), verified, new ReadExecutionBudget(partition.Database.OperationLimitsOptions, clock, cancellationToken))
            : verified.Grant is { RequireReceiverIssuance: true }
                ? partition.Database.CreateVerifiedPartitionMovementParentEffect(commandId, principal.Id, verified,
                    new ReadExecutionBudget(partition.Database.OperationLimitsOptions, clock, cancellationToken))
                : partition.Database.CreateVerifiedPartitionMovementOperation(commandId, principal.Id,
                    clock.GetUtcNow(), verified);
        var requestId = Guid.NewGuid();
        var signed = node.CatalogRequestCodec().CreatePartitionMovementCommand(requestId, operation, verified.ExpiresAt);
        return await ExecuteSignedAsync(principal, requestId, commandId, signed, command: true,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<GrainOperationReply> ExecuteSignedAsync(PrincipalRecord principal, Guid requestId,
        Guid commandId, string signed, bool command, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        GrainOperationReply? reply = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var identity = node.OpenRequestContext(principal, requestId, commandId, cancellationToken);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                reply = await node.ExecuteAsync(requestId, signed, command, cancellationToken).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return reply ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid);
    }

    internal PrincipalRecord Administrator(CancellationToken cancellationToken)
        => Administrator(PartitionStoreProtocol.AdministratorId, cancellationToken);

    internal PrincipalRecord Administrator(string principalId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var principal = partition.Database.Store.Read(view => partition.Database.Principal(view,
            principalId, clock.GetUtcNow()));
        GrainRequestAuthority.RequireAdministrator(principal);
        partition.Database.VerifyPhysicalCommandOwner(LocalOwner(), false, cancellationToken);
        return principal;
    }
}
