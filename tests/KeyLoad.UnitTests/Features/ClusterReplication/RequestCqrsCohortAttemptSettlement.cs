using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class RequestCqrsCohortAttemptSettlement
{
    private const string UnexpectedReturn = "A cancelled discovery attempt returned successfully.";
    private const string UnexpectedShutdownReturn = "A shutdown discovery attempt returned successfully.";

    internal static async Task JoinCancelledAsync(Task? attempt, RequestCqrsCohortHttpGate gate,
        List<Exception> failures, CancellationToken cancellationToken)
    {
        if (attempt is not null)
        {
            await ServerFailureObserver.ObserveAsync(
                () => ExpectCallerCancellationAsync(attempt, cancellationToken), failures);
        }

        if (gate.Entered.IsCompletedSuccessfully)
        {
            await ServerFailureObserver.ObserveAsync(() => gate.Aborted, failures);
        }
    }

    internal static Task JoinDisposalAsync(Task? disposal, List<Exception> failures)
    {
        if (disposal is null)
        {
            return Task.CompletedTask;
        }

        var actualDisposal = disposal;
        return ServerFailureObserver.ObserveAsync(() => actualDisposal, failures);
    }

    internal static async Task JoinShutdownAsync(Task? attempt, RequestCqrsCohortHttpGate gate,
        List<Exception> failures)
    {
        if (attempt is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => ExpectShutdownCancellationAsync(attempt), failures);
        }

        if (gate.Entered.IsCompletedSuccessfully)
        {
            await ServerFailureObserver.ObserveAsync(() => gate.Aborted, failures);
        }
    }

    private static async Task ExpectCallerCancellationAsync(Task attempt, CancellationToken cancellationToken)
    {
        try
        {
            await attempt;
            throw new InvalidOperationException(UnexpectedReturn);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task ExpectShutdownCancellationAsync(Task attempt)
    {
        try
        {
            await attempt;
            throw new InvalidOperationException(UnexpectedShutdownReturn);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
