using System.Runtime.ExceptionServices;

namespace KeyLoad.UnitTests;

/// <summary>Memoizes the original physical close and deletion outcomes without owning disposable resources.</summary>
internal sealed class TestDatabaseCloseState
{
    private const string IncompleteClose = "The original fixture close has not completed.";
    private bool joinAttempted;
    private bool deleteAttempted;
    private bool joinCompleted;
    private bool deleteCompleted;
    private ExceptionDispatchInfo? joinFailure;
    private ExceptionDispatchInfo? deleteFailure;

    internal Lock Gate
    {
        get;
    } = new();

    internal void Delete(Action deleteDirectory)
    {
        lock (Gate)
        {
            RequireJoined();
            if (!deleteAttempted)
            {
                deleteAttempted = true;
                deleteFailure = (Observe(deleteDirectory)).Value;
                deleteCompleted = true;
            }
            if (!deleteCompleted)
            {
                throw new InvalidOperationException(IncompleteClose);
            }
            deleteFailure?.Throw();
        }
    }

    internal bool TryBeginJoin()
    {
        if (joinAttempted)
        {
            RequireJoined();
            return false;
        }
        joinAttempted = true;
        return true;
    }

    internal void CompleteJoin(List<Exception> failures)
    {
        joinFailure = (Observe(() => KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures))).Value;
        joinCompleted = true;
        RequireJoined();
    }

    private void RequireJoined()
    {
        if (!joinCompleted)
        {
            throw new InvalidOperationException(IncompleteClose);
        }
        joinFailure?.Throw();
    }

    private static global::ManagedCode.Communication.Result<ExceptionDispatchInfo?> Observe(Action operation)
    {
        try
        {
            operation();
            return global::ManagedCode.Communication.Result<ExceptionDispatchInfo?>.FailNotFound(nameof(Observe));
        }
        catch (Exception failure) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            return global::ManagedCode.Communication.Result<ExceptionDispatchInfo?>.Succeed(((ExceptionDispatchInfo?)(ExceptionDispatchInfo.Capture(failure))));
        }
        catch (Exception failure) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            return global::ManagedCode.Communication.Result<ExceptionDispatchInfo?>.Succeed(((ExceptionDispatchInfo?)(ExceptionDispatchInfo.Capture(failure))));
        }
    }
}
