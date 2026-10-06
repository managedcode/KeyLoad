using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.UnitTests.Features.TestInfrastructure;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnSeedCancellationCapture
{
    internal static AnnSeedCancellationObservation CaptureAndCancel(TestDatabase database)
    {
        using var cancellation = new CancellationTokenSource();
        using var started = new ManualResetEventSlim(false);
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits), cancellationToken: cancellation.Token);
        var observer = new AnnSeedCancellationThread(budget, cancellation, started);
        var startedAt = new TestElapsedClock(TimeProvider.System);
        AnnSeed? seed = null;
        Exception? primary = null;
        OperationCanceledException? captureCancellation = null;
        List<Exception> settlementFailures;
        try
        {
            try
            {
                observer.StartAndWait();
            }
            catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
            {
                primary = failure;
            }
            catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
            {
                primary = failure;
            }
            if (primary is null)
            {
                Capture(database, budget, cancellation, ref seed, ref captureCancellation, ref primary);
            }
        }
        finally
        {
            settlementFailures = observer.JoinAfterCapture();
        }

        AnnSeedCancellationFailures.ThrowIfAny(primary, captureCancellation, settlementFailures);
        return new(startedAt.Elapsed, budget.ReadBytes,
            observer.ObservedReadBytes, observer.CancellationRequested, seed, captureCancellation);
    }

    private static void Capture(TestDatabase database, ReadExecutionBudget budget,
        CancellationTokenSource cancellation, ref AnnSeed? seed,
        ref OperationCanceledException? captureCancellation, ref Exception? primary)
    {
        try
        {
            seed = AnnSeedTestSupport.Capture(database, budget: budget);
        }
        catch (OperationCanceledException failure) when (cancellation.IsCancellationRequested)
        {
            captureCancellation = failure;
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            primary = failure;
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            primary = failure;
        }
    }
}
