using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.Extensions.Options;
using P = KeyLoad.UnitTests.Features.ResourceExecution.OperationalAnalyticsNativeProtocol;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Retires a known failed query only after its real engine has joined; unknown ownership retains the root.</summary>
internal sealed class OperationalAnalyticsBudgetLifetime : IAsyncDisposable
{
    private readonly Lock lifecycle = new();
    private OperationalAnalyticsOwner? analytics;
    private Task? disposal;
    private Task? originalClose;
    private bool closeAttempted;
    private bool uncertain;
    private bool preserve;
    internal OperationalAnalyticsNativeOwner Canonical { get; } = new();
    internal void RetainEvidence() => preserve = true;

    internal OperationalAnalyticsOwner Open(OperationalAnalyticsOptions settings)
    {
        if (analytics is not null || uncertain || disposal is not null)
        {
            throw new InvalidOperationException(P.Invalid);
        }
        try
        {
            analytics = new(Canonical.NodeDirectory, Options.Create(settings), TimeProvider.System);
            closeAttempted = false;
            originalClose = null;
            return analytics;
        }
        catch (Exception)
        {
            uncertain = true;
            throw;
        }
    }

    internal async Task CloseHealthyAsync()
    {
        if (analytics is null)
        {
            return;
        }
        if (closeAttempted)
        {
            throw new InvalidOperationException(P.Invalid);
        }
        closeAttempted = true;
        try
        {
            originalClose = analytics.DisposeAsync().AsTask();
            await originalClose;
            await Assert.That(analytics.HasJoinedEngine).IsTrue();
            analytics = null;
        }
        catch (Exception)
        {
            uncertain = true;
            throw;
        }
    }

    internal async Task CloseExpectedFailureAsync(InvalidOperationException original)
    {
        ArgumentNullException.ThrowIfNull(analytics);
        var current = analytics;
        if (closeAttempted)
        {
            throw new InvalidOperationException(P.Invalid);
        }
        closeAttempted = true;
        try
        {
            var failures = new List<Exception>();
            try
            {
                originalClose = analytics.DisposeAsync().AsTask();
                await originalClose;
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            if (failures.Count == 1 && ReferenceEquals(failures[0], original) && current.HasJoinedEngine)
            {
                analytics = null;
                return;
            }
            if (failures.Count == 0)
            {
                failures.Add(new InvalidOperationException(P.Invalid));
            }
            ServerFailureObserver.ThrowIfAny(failures);
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
        if (!closeAttempted && analytics is not null)
        {
            closeAttempted = true;
            try
            {
                originalClose = analytics.DisposeAsync().AsTask();
                await originalClose;
                await Assert.That(analytics.HasJoinedEngine).IsTrue();
                analytics = null;
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                uncertain = true;
                failures.Add(error);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                uncertain = true;
                failures.Add(error);
            }
        }
        else if (analytics is not null && originalClose is { } retained)
        {
            await ServerFailureObserver.ObserveAsync(() => retained, failures);
        }
        if (analytics is not null || uncertain || preserve || failures.Count != 0)
        {
            Canonical.RetainEvidence();
        }
        try
        {
            await Canonical.DisposeAsync();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
