using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Owns bounded page borrowing, native encoding and stage settlement without retaining a borrowed reader.</summary>
internal static class PartitionMovementSourcePageReader
{
    private const string ClosedDetail = "The partition movement source capability is unavailable.";
    internal static Task<PartitionMovementPageResult> Read(DatabaseEngine database,
        IOptions<DatabaseLimits> limits, TimeProvider clock, PrincipalRecord principal,
        PartitionMovePeerEnvelope verified, PartitionMovementPageQuery query,
        PartitionMovementSourceEntry entry, CancellationToken cancellationToken)
    {
        var work = new ReadExecutionBudget(limits, clock, cancellationToken);
        database.ValidateVerifiedPartitionMovementCaptureScope(principal.Id, verified, work);
        var failures = new List<Exception>();
        PartitionMovementPageResult? result = null;
        ServerFailureObserver.Observe(() =>
        {
            using var page = entry.Session.Borrow(query.Ordinal, cancellationToken);
            using var stage = work.EnterStageCancellation(entry.Session.StageCancellation);
            ServerFailureObserver.Observe(() =>
            {
                work.CheckResult(page.Page);
                var encodedLength = NativeSerialization.Measure(page.Page);
                if (encodedLength > database.Limits.MaxBatchBytes)
                { throw Errors.Fail(ErrorCode.BudgetExceeded, ClosedDetail); }
                var encoded = NativeSerialization.Serialize(page.Page);
                work.Check();
                result = new(query.HandleId, query.Ordinal, encoded);
                work.CheckResult(result);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return Task.FromResult(result ?? throw Errors.Fail(ErrorCode.Corruption, ClosedDetail));
    }
}
