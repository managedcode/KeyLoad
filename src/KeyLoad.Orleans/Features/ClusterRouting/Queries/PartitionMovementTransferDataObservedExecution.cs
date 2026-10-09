using KeyLoad.Core.Features.ClusterRouting.Queries;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

/// <summary>Observes only the owning page-byte refusal after the original synchronous read has unwound.</summary>
internal readonly struct PartitionMovementTransferDataObservedExecution(INativePartitionMovementTransferRead owner,
    IOptions<DatabaseLimits> limits, TimeProvider clock, GrainRequestCodec codec)
{
    internal Task<PartitionMovementTransferDataResult> ExecuteAsync(PrincipalRecord principal,
        DecodedGrainRequest request, IGrainContext context, CancellationToken cancellationToken)
    {
        var query = GrainNativePayload.Read<PartitionMovementTransferDataCapability>(request.Payload);
        return codec.HasPhaseObserver
            ? ExecuteObservedAsync(principal, query, request, context, cancellationToken)
            : PartitionMovementTransferDataExecution.ExecuteAsync(owner, principal, query, limits, clock,
                request.Envelope.ExpiresAt, cancellationToken);
    }

    private async Task<PartitionMovementTransferDataResult> ExecuteObservedAsync(PrincipalRecord principal,
        PartitionMovementTransferDataCapability query, DecodedGrainRequest request, IGrainContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var actual = await PartitionMovementTransferDataExecution.ExecuteAsync(owner, principal,
                query, limits, clock,
                request.Envelope.ExpiresAt, cancellationToken).ConfigureAwait(true);
            if (query.Action == PartitionMovementTransferDataAction.Page)
            {
                await codec.ObservePhaseAsync(request, GrainRequestPhase.TransferPageReturned,
                    context, cancellationToken).ConfigureAwait(true);
            }
            return actual;
        }
        catch (KeyLoadException original) when (original.Code == ErrorCode.BudgetExceeded
            && string.Equals(original.Message, PartitionRecordPageReader.RetainedBudgetExceeded, StringComparison.Ordinal))
        {
            await ObserveOriginalAsync(original, request, context, cancellationToken).ConfigureAwait(true);
            throw;
        }
    }

    private async Task ObserveOriginalAsync(KeyLoadException original, DecodedGrainRequest request,
        IGrainContext context, CancellationToken cancellationToken)
    {
        try
        {
            await codec.ObservePhaseAsync(request, GrainRequestPhase.TransferPageRetainedBudgetExceeded,
                context, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            throw NativeCapabilityWorkLifetime.CombinePrimary(original, failure);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            throw NativeCapabilityWorkLifetime.CombinePrimary(original, failure);
        }
    }
}
