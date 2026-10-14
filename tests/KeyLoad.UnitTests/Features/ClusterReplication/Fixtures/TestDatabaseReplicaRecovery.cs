using KeyLoad.Core;
using KeyLoad.Replication;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

internal static class TestDatabaseReplicaRecovery
{
    private const string InvalidRecovery = "The fixture cannot recover this original failed committed entry.";
    private const int RequiredSubmissionFailures = 2;
    private const int RetainedReplayFailureIndex = 1;

    internal static ReplicaMaterializer Recover(ReplicaMaterializer original, DatabaseEngine database,
        DurableReplicaLog log, IReplicaSnapshotStore snapshots, IOptions<ReplicaExecutionOptions> options,
        ReplicatedOperation operation, Action restoreFaultOwnedRow, Action retireJoinedOwner, List<Exception> originalFailures,
        CancellationToken cancellationToken, ErrorCode? expectedApplyError = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = log.ReadEntry(log.State.CommittedIndex);
        if (entry?.Operation is not { } retained || log.State.CommittedIndex != log.State.LastIndex
            || database.LastApplied >= entry.Index || !Same(retained, operation)
            || database.ResolveOutcome(operation).Error != ErrorCode.RecoveryRequired)
        {
            throw new InvalidOperationException(InvalidRecovery);
        }
        RequireFailed(original);
        var expectedError = RequireOriginalError(originalFailures, expectedApplyError);
        var joined = new List<Exception>();
        try
        {
            original.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null)
        {
            joined.Add(error);
        }
        originalFailures.AddRange(joined);
        if (joined.Count == 0 || joined.Any(error => !IsOriginalFailure(error, expectedError)))
        {
            if (joined.Count != 0)
            {
                KeyLoad.Server.ServerFailureObserver.ThrowIfAny(joined);
            }
            throw new InvalidOperationException(InvalidRecovery);
        }
        retireJoinedOwner();
        cancellationToken.ThrowIfCancellationRequested();
        restoreFaultOwnedRow();
        return new(database, log, snapshots, options);
    }

    private static void RequireFailed(ReplicaMaterializer original)
    {
        try
        {
            _ = original.AppliedPosition;
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.RecoveryRequired)
        {
            return;
        }
        throw new InvalidOperationException(InvalidRecovery);
    }

    private static ErrorCode RequireOriginalError(List<Exception> failures, ErrorCode? expectedApplyError)
    {
        if (failures.Count != RequiredSubmissionFailures || failures[0] is not KeyLoadException
            { Code: ErrorCode.Corruption or ErrorCode.RecoveryRequired } first
            || !IsOriginalFailure(failures[RetainedReplayFailureIndex], ErrorCode.RecoveryRequired)
            || expectedApplyError is not (null or ErrorCode.Corruption or ErrorCode.RecoveryRequired)
            || expectedApplyError is not null && first.Code != ErrorCode.RecoveryRequired)
        {
            throw new InvalidOperationException(InvalidRecovery);
        }
        return expectedApplyError ?? first.Code;
    }

    private static bool IsOriginalFailure(Exception error, ErrorCode expectedError)
        => error is KeyLoadException typed ? typed.Code == expectedError
            : error is AggregateException aggregate && aggregate.InnerExceptions.Count != 0
                && aggregate.InnerExceptions.All(inner => IsOriginalFailure(inner, expectedError));

    private static bool Same(ReplicatedOperation retained, ReplicatedOperation original)
        => retained.Id == original.Id && retained.Kind == original.Kind
            && retained.PrincipalId == original.PrincipalId && retained.EvaluatedAt == original.EvaluatedAt
            && retained.PayloadJson == original.PayloadJson
            && retained.NativePayload.Span.SequenceEqual(original.NativePayload.Span);
}
