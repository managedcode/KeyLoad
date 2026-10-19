using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

/// <summary>Retains actual execution fault-test owners until their original disposal succeeds.</summary>
internal sealed class StorageFailureOwnerLifetime
{
    private readonly HashSet<IDisposable> owners = [];

    private bool IsCloseUncertain
    {
        get; set;
    }

    internal bool CanAdvance => !IsCloseUncertain && owners.Count == 0;

    internal T? Acquire<T>(Func<T> open, List<Exception> failures) where T : class, IDisposable
    {
        T? owner = null;
        ServerFailureObserver.Observe(() =>
        {
            owner = open();
            owners.Add(owner);
        }, failures);
        if (owner is null)
        {
            IsCloseUncertain = true;
        }
        return owner;
    }

    internal void Transfer(IDisposable original, IDisposable receiver)
    {
        owners.Add(receiver);
        owners.Remove(original);
    }

    internal void Close(IDisposable owner, List<Exception> failures)
    {
        var closed = false;
        ServerFailureObserver.Observe(() =>
        {
            owner.Dispose();
            closed = true;
        }, failures);
        if (closed)
        {
            owners.Remove(owner);
        }
        else
        {
            IsCloseUncertain = true;
        }
    }
}
