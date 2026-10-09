using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Server.Features.BlobStorage;
using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class ControlledDocumentCommandRouter(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> options, IOptions<DatabaseLimits> limits, ControlledBlobSourceRead blobReads, TimeProvider clock)
    : IControlledDocumentCommandRouter
{
    public async Task<OperationResult?> TryExecuteAsync(GrainRequestEnvelope envelope,
        ReadOnlyMemory<byte> payload, CancellationToken cancellationToken,
        Func<CancellationToken, ValueTask>? grantSettled = null,
        Func<CancellationToken, ValueTask>? outcomeObserved = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (envelope.CommandKind != OperationKind.Batch
            && (envelope.CommandKind is not { } kind || !BlobStorageOperations.Handles(kind)))
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteDocumentProtocol.Unavailable); }
        var runtime = node.Movement
            ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteDocumentProtocol.Unavailable);
        node.CatalogRequestCodec().ValidateScope(envelope);
        var failures = new List<Exception>();
        OperationResult? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var expiry = new CancellationTokenSource(envelope.ExpiresAt - clock.GetUtcNow(), clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, expiry.Token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await runtime.RunAsync(async token =>
                {
                    result = await ExecuteAtCutAsync(runtime, envelope, payload, grantSettled, outcomeObserved, token).ConfigureAwait(false);
                }, linked.Token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }
    private async Task<OperationResult?> ExecuteAtCutAsync(PartitionMovementRuntime runtime,
        GrainRequestEnvelope envelope, ReadOnlyMemory<byte> payload,
        Func<CancellationToken, ValueTask>? grantSettled,
        Func<CancellationToken, ValueTask>? outcomeObserved, CancellationToken token)
    {
        await partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        node.CatalogRequestCodec().ValidateScope(envelope);
        var principal = GrainRequestAuthority.ReloadForRequest(partition.Database, envelope, clock);
        var work = new ReadExecutionBudget(limits, clock, token);
        var original = partition.Database.CreateNativeOperation(envelope.CommandKind!.Value,
            envelope.CommandId, principal.Id, clock.GetUtcNow(), payload);
        var context = partition.Database.TryCaptureControlledDocumentCommand(principal.Id, original, work);
        if (context is null)
        { return null; }
        var control = PhysicalOwnerConfiguredTuples.Control(options.Value, partition).Owner;
        var phases = new ControlledDocumentPhaseExecution(runtime, control, partition, grantSettled, outcomeObserved);
        var flow = new ControlledDocumentCommandFlow(partition, phases, blobReads, envelope.RequestId, limits, clock);
        try
        {
            var result = await flow.ExecuteAsync(original, context, envelope.ExpiresAt, work, token)
                .ConfigureAwait(false);
            work.Check();
            return result;
        }
        catch (KeyLoadException error) when (phases.HasAcknowledgedTargetEffect
            && error.Code is ErrorCode.BudgetExceeded or ErrorCode.ResourceExhausted)
        {
            var unknown = Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteDocumentProtocol.Unavailable);
            unknown.Data[ControlledDocumentFailureProtocol.OriginalBudgetFailure] = error;
            throw unknown;
        }
    }

}
