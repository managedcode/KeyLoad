using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Owns actual native analytics until its original close succeeds; uncertainty retains the whole root.</summary>
internal sealed class OperationalAnalyticsNativeOwner : IAsyncDisposable
{
    private readonly Lock lifecycle = new();
    private OperationalAnalyticsOwner? analytics;
    private Task? disposal;
    private bool closeAttempted;
    private bool uncertain;
    private bool preserve;
    internal TestDatabase Canonical { get; } = new(nativeReplicaAdmission: true);
    internal string NodeDirectory => Canonical.Directory;
    internal void RetainEvidence() => preserve = true;

    internal OperationalAnalyticsOwner Open()
    {
        if (analytics is not null || uncertain || disposal is not null)
        {
            throw new InvalidOperationException(OperationalAnalyticsNativeProtocol.Invalid);
        }
        try
        {
            analytics = new(NodeDirectory, Options.Create(new OperationalAnalyticsOptions()), TimeProvider.System);
            closeAttempted = false;
            return analytics;
        }
        catch (Exception)
        {
            uncertain = true;
            throw;
        }
    }

    internal async Task CloseAsync()
    {
        if (analytics is null)
        {
            return;
        }
        if (closeAttempted)
        {
            throw new InvalidOperationException(OperationalAnalyticsNativeProtocol.Invalid);
        }
        closeAttempted = true;
        try
        {
            await analytics.DisposeAsync().ConfigureAwait(false);
            analytics = null;
        }
        catch (Exception)
        {
            uncertain = true;
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            return new(disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        if (!closeAttempted)
        {
            await ServerFailureObserver.ObserveAsync(CloseAsync, failures);
        }
        if (analytics is null && !uncertain && !preserve && failures.Count == 0)
        {
            try
            {
                Canonical.Dispose();
            }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
        }
        else
        {
            ServerFailureObserver.Observe(Canonical.JoinOwnedResources, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
