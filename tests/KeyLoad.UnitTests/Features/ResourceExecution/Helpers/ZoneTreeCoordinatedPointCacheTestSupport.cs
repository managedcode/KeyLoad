using System.Runtime.ExceptionServices;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ZoneTreeCoordinatedPointCacheTestSupport
{
    private const string OperationFailure = "A coordinated point-cache test operation failed.";
    private const string FailureSummary = "Coordinated point-cache test and cleanup failed.";
    private const string SetupFailure = "The real coordinated point cache setup was rejected.";
    private const string PermitFailure = "A fresh real-clock cache permit was rejected.";
    private const string RenewalFailure = "A fresh real-clock cache renewal was rejected.";
    internal static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    internal static CacheReadPermit CreateAcceptedPermit(long sequence, out Guid grantId,
        out CacheReadPermitAcceptance acceptance)
    {
        var permit = new CacheReadPermit(TimeProvider.System, UnitAdmissionOptions.Permit());
        grantId = Guid.NewGuid();
        if (!permit.TryAccept(grantId, sequence, TimeProvider.System.GetTimestamp(), out acceptance))
        {
            permit.Dispose();
            throw new InvalidOperationException(PermitFailure);
        }

        return permit;
    }

    internal static CacheReadPermitAcceptance AcceptRenewal(CacheReadPermit permit, long sequence,
        out Guid grantId)
    {
        grantId = Guid.NewGuid();
        if (!permit.TryAccept(grantId, sequence, TimeProvider.System.GetTimestamp(), out var acceptance))
        {
            throw new InvalidOperationException(RenewalFailure);
        }

        return acceptance;
    }

    internal static void Put(ZoneTreeStore store, byte[] key, byte[] value)
        => store.Commit((transaction, _) => { transaction.Put(key, value); return true; });

    internal static ZoneTreePointCacheControl CreateControl(ZoneTreeStore store,
        ZoneTreeCoordinatedPointCacheFileFixture fixture, CacheReadPermit permit)
    {
        var result = store.TryCreateCoordinatedPointCache(fixture.CreateOptions(), permit, out var control);
        if (result != ZoneTreePointCacheControlResult.Created || control is null)
        {
            throw new InvalidOperationException(SetupFailure);
        }

        return control;
    }

    internal static Task<T> StartLongRunning<T>(Func<T> action)
        => Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    internal static async Task JoinAndCollectAsync(Task operation, List<Exception> failures)
    {
        try
        {
            await WrapFailureAsync(() => operation.WaitAsync(WaitLimit, TimeProvider.System));
        }
        catch (AggregateException failure)
        {
            AddDistinctFailures(failures, failure.Flatten().InnerExceptions);
            if (operation.IsFaulted)
            {
                AddDistinctFailures(failures, operation.Exception!.Flatten().InnerExceptions);
            }

            ObserveLateFailure(operation);
        }
    }

    internal static async Task CollectFailureAsync(Func<Task> operation, List<Exception> failures)
    {
        try
        {
            await WrapFailureAsync(operation);
        }
        catch (AggregateException failure)
        {
            AddDistinctFailures(failures, failure.Flatten().InnerExceptions);
        }
    }

    internal static void ThrowFailures(List<Exception> failures)
    {
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        if (failures.Count > 1)
        {
            throw new AggregateException(FailureSummary, failures);
        }
    }

    private static async Task WrapFailureAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception failure)
        {
            throw new AggregateException(OperationFailure, failure);
        }
    }

    private static void AddDistinctFailures(List<Exception> failures, IEnumerable<Exception> additions)
    {
        foreach (var addition in additions)
        {
            if (!failures.Any(failure => ReferenceEquals(failure, addition)))
            {
                failures.Add(addition);
            }
        }
    }

    private static void ObserveLateFailure(Task operation)
        => _ = operation.ContinueWith(static completed => { _ = completed.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
