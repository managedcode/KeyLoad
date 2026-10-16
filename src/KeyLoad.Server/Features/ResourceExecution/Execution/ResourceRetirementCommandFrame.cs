using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Borrows one numeric child grant from the original receiving frame for the existing command CQRS boundary.</summary>
internal sealed class ResourceRetirementCommandFrame(OrleansNode node, ResourceRetirementSignedExecution execution)
{
    internal async Task<ResourceRetirementPhaseResultV1> ExecuteAsync(PrincipalRecord principal, Guid commandId,
        ReplicatedOperation operation, DateTimeOffset originalExpiry, ReadExecutionBudget work, CancellationToken token)
    {
        ResourceRetirementFrameReadBudget.RequireAvailable(work);
        var bytes = work.RemainingReadGrantBytes;
        var records = work.RemainingReadGrantRecords;
        var grant = work.CreateReadGrant(bytes, records);
        var failures = new List<Exception>();
        ResourceRetirementPhaseResultV1? value = null;
        var begun = false;
        var imported = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            try
            {
                var admission = new NativeCommandMetadataAdmissionV1(NativeCommandMetadataProtocol.CurrentVersion,
                    bytes, records, work.MaximumResultBytes);
                var requestId = Guid.NewGuid();
                var signed = node.CatalogRequestCodec().CreateResourceRetirementCommand(requestId, operation,
                    originalExpiry, admission);
                var reply = await execution.ExecuteAsync(principal, requestId, commandId, signed, true,
                    () => begun = true, token).ConfigureAwait(false);
                RequireStatistics(reply, bytes, records, work.MaximumResultBytes);
                var statistics = reply.Metadata!;
                work.ImportSettledReadGrant(grant, statistics.ReadBytes, statistics.ExaminedRecords);
                imported = true;
                value = ResourceRetirementSignedExecution.Value<ResourceRetirementPhaseResultV1>(reply, token);
            }
            catch (Exception original)
            {
                if (begun && !imported)
                {
                    work.FailIndeterminateMetadata(original);
                }

                throw;
            }
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() => work.CompleteSettledReadGrant(grant), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return value ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
    }

    private static void RequireStatistics(GrainOperationReply reply, long bytes, int records, int results)
    {
        if (reply.Metadata is not { } statistics
            || statistics.ReadBytes < ResourceRetirementReadContract.NoReadBytes || statistics.ReadBytes > bytes
            || statistics.ExaminedRecords < ResourceRetirementReadContract.NoRecords || statistics.ExaminedRecords > records
            || statistics.ResultBytes != reply.Payload.Length || statistics.ResultBytes > results)
        {
            // A trusted existing native refusal retains its exact code, but missing cost observations never become zero.
            if (reply.Error is { } code && Enum.IsDefined(code))
            {
                throw Errors.Fail(code, ResourceRetirementNativeProtocol.Unavailable);
            }

            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
        }
    }
}
