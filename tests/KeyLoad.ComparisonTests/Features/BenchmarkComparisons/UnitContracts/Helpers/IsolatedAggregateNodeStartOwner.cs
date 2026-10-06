using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedAggregateNodeStartOwner : IDisposable
{
    private readonly IsolatedAggregateNodeFailureSet failures = new();
    private Process? process;
    private bool ownershipEnded;

    internal IsolatedAggregateNodeStartOwner()
        => process = new Process();

    internal static IsolatedAggregateNodeStartOwner Create(ProcessStartInfo startInfo)
    {
        var owner = new IsolatedAggregateNodeStartOwner();
        owner.Configure(startInfo);
        return owner;
    }

    internal void Configure(ProcessStartInfo startInfo)
    {
        try
        {
            var activeProcess = process
                ?? throw new ObjectDisposedException(nameof(IsolatedAggregateNodeStartOwner));
            IsolatedAggregateNodeGuardedInvocation.Invoke(() => activeProcess.StartInfo = startInfo);
        }
        catch (AggregateException envelope)
        {
            var primary = IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope);
            DisposeOwnedProcess();
            ownershipEnded = true;
            failures.Throw(primary);
        }
    }

    internal Process StartAndTransfer()
    {
        var activeProcess = process
            ?? throw new ObjectDisposedException(nameof(IsolatedAggregateNodeStartOwner));
        Exception? primary = null;
        var started = false;
        try
        {
            started = IsolatedAggregateNodeGuardedInvocation.Invoke(activeProcess.Start);
        }
        catch (AggregateException envelope)
        {
            primary = IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope);
        }

        if (primary is null && started)
        {
            process = null;
            ownershipEnded = true;
            return activeProcess;
        }
        primary ??= new InvalidOperationException(IsolatedAggregateNodeProcess.StartFailure);
        DisposeOwnedProcess();
        ownershipEnded = true;
        failures.Throw(primary);
        throw new InvalidOperationException(IsolatedAggregateNodeProcess.StartFailure);
    }

    public void Dispose()
    {
        if (ownershipEnded)
        {
            return;
        }
        try
        {
            IsolatedAggregateNodeGuardedInvocation.Capture(() => process?.Dispose(), failures.Add);
        }
        finally
        {
            process = null;
            ownershipEnded = true;
        }
        failures.Throw(primary: null);
    }

    private void DisposeOwnedProcess()
    {
        IsolatedAggregateNodeGuardedInvocation.Capture(() => process?.Dispose(), failures.Add);
        process = null;
    }
}
