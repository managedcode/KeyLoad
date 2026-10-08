using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Issues receiver-local capabilities only after configured-peer admission has succeeded.</summary>
internal sealed partial class PartitionMovementReceiver(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> options, TimeProvider clock, Func<PartitionMovementSourceOwner> source)
{
    internal ReplicaSiloDiscovery Discovery() => node.Discovery?.Read()
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch);

    internal PhysicalShardRecord LocalOwner() => PhysicalOwnerConfiguredTuples.Local(options.Value, partition).Owner;

    internal async Task<GrainOperationReply> ExecuteAsync(PartitionMovementTransportRequest verified,
        CancellationToken cancellationToken)
    {
        if (verified.Action == PartitionMovementTransportAction.Apply)
        { return await ApplyPhaseAsync(verified.CommandId, verified.Envelope, cancellationToken).ConfigureAwait(false); }
        var action = verified.Action switch
        {
            PartitionMovementTransportAction.Capture => PartitionMovementCaptureAction.Capture,
            PartitionMovementTransportAction.Page => PartitionMovementCaptureAction.Page,
            PartitionMovementTransportAction.Release => PartitionMovementCaptureAction.Release,
            _ => throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMoveProtocol.Invalid)
        };
        var principal = Administrator(cancellationToken);
        var requestId = Guid.NewGuid();
        var capability = new PartitionMovementCaptureCapability(action, verified.Envelope,
            verified.HandleId, verified.Ordinal);
        var signed = node.CatalogRequestCodec().CreatePartitionMovementCapture(requestId, principal.Id, capability);
        return await ExecuteSignedAsync(principal, requestId, Guid.Empty, signed, command: false,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<PartitionMovePhaseResult> SettleCaptureAsync(PartitionMovePeerEnvelope original,
        CancellationToken cancellationToken)
    {
        var commandId = original.Grant?.PhaseCommandId
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.Invalid);
        var reply = await ApplyAsync(commandId, original, cancellationToken).ConfigureAwait(false);
        if (reply.Error is { } code)
        { throw Errors.Fail(code, reply.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        var value = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as PartitionMovePhaseResult
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid);
        return value;
    }

    private async Task<GrainOperationReply> ApplyAsync(Guid commandId, PartitionMovePeerEnvelope verified,
        CancellationToken cancellationToken)
    {
        var principal = Administrator(PartitionMovementControlPrincipal.Resolve(partition.Database, verified), cancellationToken);
        var operation = partition.Database.CreateVerifiedPartitionMovementOperation(commandId, principal.Id,
            clock.GetUtcNow(), verified);
        var requestId = Guid.NewGuid();
        var signed = node.CatalogRequestCodec().CreatePartitionMovementCommand(requestId, operation, verified.ExpiresAt);
        return await ExecuteSignedAsync(principal, requestId, commandId, signed, command: true,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<GrainOperationReply> ExecuteSignedAsync(PrincipalRecord principal, Guid requestId,
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

    private PrincipalRecord Administrator(CancellationToken cancellationToken)
        => Administrator(PartitionStoreProtocol.AdministratorId, cancellationToken);

    private PrincipalRecord Administrator(string principalId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var principal = partition.Database.Store.Read(view => partition.Database.Principal(view,
            principalId, clock.GetUtcNow()));
        GrainRequestAuthority.RequireAdministrator(principal);
        partition.Database.VerifyPhysicalCommandOwner(LocalOwner(), false, cancellationToken);
        return principal;
    }
}
