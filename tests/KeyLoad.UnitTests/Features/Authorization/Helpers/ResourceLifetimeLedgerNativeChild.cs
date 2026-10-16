using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query;
using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.Authorization.SafeProjectorOperationProtocol;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class ResourceLifetimeLedgerNativeChild
{
    internal sealed record Observation(QueryPage? Page, Exception? Error);

    internal static async Task<Observation> ExecuteAsync(SafeProjectorOperationDatabase owner,
        NativeRequestWorkOwner workOwner, ResourceLifetimeWorkResultRetention retention,
        DateTimeOffset expiry, string resource, int? maximumBytes = null)
    {
        var lease = workOwner.Acquire(Guid.NewGuid(), NativeRequestWorkKind.ReadCapability);
        lease.AttachResultRetention(retention);
        var failures = new List<Exception>();
        QueryPage? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(owner.CallerToken, lease.Cancellation);
            await Assert.That(lease.ResultRetention).IsSameReferenceAs(retention);
            var budget = new ReadExecutionBudget(owner.Database.OperationLimitsOptions,
                owner.Database.EvaluationClock, linked.Token);
            budget.ConstrainLifetime(expiry);
            if (maximumBytes is { } limit)
            {
                budget.ConstrainResultBytes(limit);
            }
            result = new QueryEngine(owner.Database, UnitExecutionOptions.QueryExecution()).ExecuteBound(
                P.Reader, ResourceLifetimeLedgerNativeData.Request(owner, resource), budget,
                owner.Database.EvaluationClock, linked.Token, lease);
        }, failures);
        var original = failures.FirstOrDefault();
        var settlementFailures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var completion = lease.CompleteAsync(original, null);
            await Assert.That(ReferenceEquals(completion, lease.Settlement)).IsTrue();
            var report = await completion;
            await Assert.That(await lease.Settlement).IsSameReferenceAs(report);
            if (original is null)
            {
                await Assert.That(report.OriginalFailure).IsNull();
            }
            else
            {
                await Assert.That(report.OriginalFailure).IsSameReferenceAs(original);
            }
            await Assert.That(report.CleanupFailures.IsEmpty).IsTrue();
            await Assert.That(report.CleanupOrJoinFailure).IsNull();
        }, settlementFailures);
        if (settlementFailures.Count != ResourceLifetimeLedgerNativeData.NoEntries)
        {
            failures.AddRange(settlementFailures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
        if (original is not null && (original is not KeyLoadException
            || ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(original) is not null))
        {
            ServerFailureObserver.ThrowIfAny(failures);
        }
        return new(result, original);
    }
}
