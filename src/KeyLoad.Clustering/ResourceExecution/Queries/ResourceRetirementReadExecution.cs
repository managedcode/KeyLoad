using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal static class ResourceRetirementReadExecution
{
    internal static ResourceRetirementReadReplyV1 Execute(DatabaseEngine database, PrincipalRecord principal,
        DecodedGrainRequest request, SiloStorageIdentityProvider discovery, IOptions<DatabaseLimits> limits,
        TimeProvider clock, ReadExecutionBudget work, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        GrainRequestAuthority.RequireAdministrator(principal);
        var query = GrainNativePayload.Read<ResourceRetirementReadRequestV1>(request.Payload);
        ResourceRetirementReadShape.Require(query, principal, discovery.Read(), limits.Value, clock,
            request.Envelope.ExpiresAt);
        database.VerifyPhysicalCommandOwner(query.ObservedOwner.Owner, false, work, token);
        work.Check();
        var result = Read(database, principal.Id, query, work);
        ResourceRetirementReadShape.Require(query, principal, discovery.Read(), limits.Value, clock,
            request.Envelope.ExpiresAt);
        var completed = result with
        {
            ReadBytes = work.ReadBytes,
            ExaminedRecords = work.ConstrainedNativeRecords
        };
        if (NativeSerialization.Measure(completed) > query.MaximumResultBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementReadScope.InvalidRequest);
        }
        work.MeasureResult(completed);
        work.Check();
        return completed;
    }

    internal static ReadExecutionBudget CreateWork(DatabaseEngine database, TimeProvider clock,
        DecodedGrainRequest request, IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding);
        token.ThrowIfCancellationRequested();
        var query = GrainNativePayload.Read<ResourceRetirementReadRequestV1>(request.Payload);
        var options = database.OperationLimitsOptions;
        var limits = options.Value;
        if (query is null || query.ExpiresAt != request.Envelope.ExpiresAt
            || query.MaximumReadBytes <= ResourceRetirementReadContract.NoReadBytes
            || query.MaximumReadBytes > limits.MaxQueryReadBytes
            || query.MaximumExaminedRecords <= ResourceRetirementReadContract.NoRecords
            || query.MaximumExaminedRecords > limits.MaxScanRecords
            || query.MaximumResultBytes <= ResourceRetirementReadContract.NoRecords
            || query.MaximumResultBytes > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementReadScope.InvalidRequest);
        }

        if (query.ExpiresAt <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.CursorExpired, ResourceRetirementReadScope.InvalidRequest);
        }

        var work = new ReadExecutionBudget(options, clock, token);
        work.ConstrainLifetime(request.Envelope.ExpiresAt);
        work.ConstrainNativeWork(query.MaximumReadBytes, query.MaximumExaminedRecords);
        work.ConstrainResultBytes(query.MaximumResultBytes);
        work.AttachResourceLifetimeBinding(binding);
        return work;
    }

    private static ResourceRetirementReadReplyV1 Read(DatabaseEngine database, string principalId,
        ResourceRetirementReadRequestV1 query, ReadExecutionBudget work)
    {
        try
        {
            return ReadExisting(database, principalId, query, work);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.NotFound
            && query.Operation == ResourceRetirementReadOperation.Attempt && query.AttemptId == Guid.Empty
            && query.Context?.Purpose == ResourceRetirementAttemptPurpose.CleanupPage)
        {
            return new(null, null, null, ResourceRetirementReadContract.NoReadBytes, ResourceRetirementReadContract.NoRecords);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.NotFound
            && query.Operation is ResourceRetirementReadOperation.State or ResourceRetirementReadOperation.Fence
                or ResourceRetirementReadOperation.PreparedOperation or ResourceRetirementReadOperation.Preparation
                or ResourceRetirementReadOperation.OwnerStatus)
        {
            return new(null, null, null, ResourceRetirementReadContract.NoReadBytes, ResourceRetirementReadContract.NoRecords);
        }
    }

    private static ResourceRetirementReadReplyV1 ReadExisting(DatabaseEngine database, string principalId,
        ResourceRetirementReadRequestV1 query, ReadExecutionBudget work) => query.Operation switch
        {
            ResourceRetirementReadOperation.CleanupProgress or ResourceRetirementReadOperation.OwnerRelease
                or ResourceRetirementReadOperation.OriginalWorkSeal or ResourceRetirementReadOperation.OriginalD3Seal
                or ResourceRetirementReadOperation.DropParent => ResourceRetirementRecoveryReadExecution.Read(database, principalId, query, work),
            ResourceRetirementReadOperation.State => new(database.ReadResourceRetirementState(
                principalId, query.Request!, work), null, null, ResourceRetirementReadContract.NoReadBytes, ResourceRetirementReadContract.NoRecords),
            ResourceRetirementReadOperation.Fence => new(null, database.ReadResourceRetirementOwnerFence(
                principalId, query.Control!, work), null, ResourceRetirementReadContract.NoReadBytes, ResourceRetirementReadContract.NoRecords),
            ResourceRetirementReadOperation.CaptureRetiringInventory => new(null, null,
                database.CaptureRetiringResourceOwnerInventoryControl(principalId, query.Control!, query.Attempt!,
                    query.InventoryId, query.PageSize, query.ExpiresAt, work), ResourceRetirementReadContract.NoReadBytes, ResourceRetirementReadContract.NoRecords),
            ResourceRetirementReadOperation.Attempt => new(null, null, null,
                ResourceRetirementReadContract.NoReadBytes, ResourceRetirementReadContract.NoRecords)
            {
                Attempt = database.ReadResourceRetirementAttempt(principalId, query.Context!, query.AttemptId, work)
            },
            ResourceRetirementReadOperation.PreparedOperation => new(null, null, null,
                ResourceRetirementReadContract.NoReadBytes, ResourceRetirementReadContract.NoRecords)
            {
                PreparedOperation = database.ReadPreparedResourceRetirementOperation(principalId, query.Context!, query.Attempt!, work)
            },
            ResourceRetirementReadOperation.Preparation => new(null, null, null,
                ResourceRetirementReadContract.NoReadBytes, ResourceRetirementReadContract.NoRecords)
            {
                Preparation = database.ReadResourceRetirementPreparation(principalId, query.Request!, work)
            },
            ResourceRetirementReadOperation.OwnerStatus => new(null, null, null,
                ResourceRetirementReadContract.NoReadBytes, ResourceRetirementReadContract.NoRecords)
            {
                OwnerStatus = database.ReadResourceRetirementOwnerStatus(principalId, query.Preparation!, work)
            },
            // Issuance9 and original-work join10 are handled by the authenticated async native gateway.
            _ => throw Errors.Fail(ErrorCode.Validation, ResourceRetirementReadScope.InvalidRequest)
        };
}
