using System.Collections.Immutable;

namespace KeyLoad.Core.ResourceExecution;

/// <summary>Shares one deduplicated resource lifetime ledger across an original request and its children.</summary>
internal sealed class ResourceLifetimeWorkResultRetention : IDisposable
{
    private const string Invalid = "The resource lifetime ledger input is invalid.";
    private readonly Lock gate = new();
    private readonly HashSet<ResourceLifetime> retained = [];
    private bool disposed;

    internal int RetainedEntries
    {
        get
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return retained.Count;
            }
        }
    }

    internal void Reserve(ImmutableArray<ResourceLifetime> lifetimes)
    {
        if (lifetimes.IsDefault)
        {
            throw Errors.Fail(ErrorCode.Validation, Invalid);
        }
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            retained.UnionWith(lifetimes);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            retained.Clear();
        }
    }
}
