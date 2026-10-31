using KeyLoad.Query;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class PartitionQueryCancellationRun
{
    internal static PartitionQueryCancellationOutcome Execute(TestDatabase database, QueryEngine engine,
        CancellationTokenSource cancellation, ReadExecutionBudget budget)
    {
        using var armed = new ManualResetEventSlim(false);
        using var settled = new ManualResetEventSlim(false);
        var observer = new PartitionQueryCancellationObserver(
            () => database.Store.GetReadDiagnostics().RangeBaselineEntries, cancellation, armed, settled);
        var outcome = new PartitionQueryCancellationOutcome(false, null, 0, false);
        Exception? primary = null;
        List<Exception> settlement;
        try
        {
            observer.StartAndWait();
            observer.BeginQuery();
            outcome = ExecuteOnCallingThread(database, engine, cancellation, budget);
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            primary = failure;
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            primary = failure;
        }
        finally
        {
            SignalQuerySettlement(settled, ref primary);
            settlement = observer.JoinAfterQuery();
            outcome = outcome with
            {
                ObservedEntries = observer.ObservedEntries,
                CancellationRequested = observer.CancellationRequested
            };
        }
        PartitionQueryCancellationFailures.ThrowIfAny(primary, settlement);
        return outcome;
    }

    private static PartitionQueryCancellationOutcome ExecuteOnCallingThread(TestDatabase database, QueryEngine engine,
        CancellationTokenSource cancellation, ReadExecutionBudget budget)
    {
        try
        {
            _ = engine.ExecutePartitionQuery(PartitionQueryTestSupport.Principal,
                PartitionQueryTestSupport.Request(database, 1), [database.Partition], budget);
            return new PartitionQueryCancellationOutcome(true, null, 0,
                cancellation.IsCancellationRequested);
        }
        catch (OperationCanceledException failure) when (failure.CancellationToken == cancellation.Token)
        {
            return new PartitionQueryCancellationOutcome(false, failure, 0,
                cancellation.IsCancellationRequested);
        }
    }

    private static void SignalQuerySettlement(ManualResetEventSlim settled, ref Exception? primary)
    {
        try
        {
            settled.Set();
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            primary = primary is null ? failure : new AggregateException(primary, failure);
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            primary = primary is null ? failure : new AggregateException(primary, failure);
        }
    }
}
