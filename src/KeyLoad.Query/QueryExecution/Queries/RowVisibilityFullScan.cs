using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class RowVisibilityFullScan
{
    private const string CandidateLimitExceeded = "The query access path exceeds its candidate budget.";

    internal static void Visit(DatabaseEngine database, PrincipalRecord principal,
        ResourceDefinition resource, AstQueryRequest request, ReadExecutionBudget budget, long? sourceEpoch,
        Func<StorageRecordVisitor, StorageScanResult> scan, Action<DocumentRecord> accept)
    {
        RowVisibilityLease? lease = null;
        RowVisibilityDraft? draft = null;
        Exception? primary = null;
        try
        {
            try
            {
                budget.Check();
                if (sourceEpoch is { } epoch && database.RowVisibility is { } cache)
                {
                    var scope = database.CaptureRowVisibility(principal, resource, request.Partition,
                        request.Query.Collection, epoch);
                    lease = cache.Acquire(scope);
                    draft = BeginMissing(cache, scope, lease);
                }
                var result = ScanOriginal(database, principal, budget, lease, draft, scan, accept);
                if (result.HasMore)
                {
                    throw Errors.Fail(ErrorCode.BudgetExceeded, CandidateLimitExceeded);
                }
                budget.Check();
                draft?.Complete();
            }
            catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
            {
                primary = error;
                throw;
            }
            catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
            {
                primary = error;
                throw;
            }
            finally { draft?.Dispose(); lease?.Dispose(); }
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        {
            RowVisibilityFailureLedger.ThrowJoined(primary, error);
            throw;
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            RowVisibilityFailureLedger.ThrowJoined(primary, error);
            throw;
        }
    }
    private static RowVisibilityDraft? BeginMissing(RowVisibilityCache cache, RowVisibilityScope scope, RowVisibilityLease? lease)
        => lease is null ? cache.Begin(scope) : null;

    private static StorageScanResult ScanOriginal(DatabaseEngine database, PrincipalRecord principal,
        ReadExecutionBudget budget, RowVisibilityLease? lease, RowVisibilityDraft? draft,
        Func<StorageRecordVisitor, StorageScanResult> scan, Action<DocumentRecord> accept)
    {
        return scan((_, bytes) =>
            {
                budget.Check();
                var document = OrleansSerialization.Deserialize<DocumentRecord>(bytes);
                if (!document.Deleted && (lease is null || lease.Contains(document.Reference.Id))
                    && database.Authorization.CanReadRow(principal, document.Access))
                {
                    draft?.AddVisible(document.Reference.Id);
                    accept(document);
                }
                return true;
            });
    }
}
