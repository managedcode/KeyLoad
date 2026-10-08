using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Owns the real shared host admission ledger, native work registry and source image/page service.</summary>
internal sealed class ControlledPartitionMovementCaptureRuntime : IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly CacheMemoryBudget memory;
    private readonly NativeRequestWorkOwner? work;
    private readonly PartitionMovementSourceOwner? source;
    private Task? closing;

    internal ControlledPartitionMovementCaptureRuntime(ControlledPartitionMovementNode node,
        ServerRuntimeOptions runtime, ControlledPartitionMovementCaptureSettlement settlement)
    {
        memory = new(runtime.Core.CacheMemory);
        try
        {
            work = new(runtime.GrainRouting);
            source = new(node.Database, memory, runtime.Core.DatabaseLimits, work,
                node.Database.EvaluationClock, settlement.SettleAsync);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            try
            { DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
            catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal PartitionMovementSourceOwner Source => source
        ?? throw new InvalidOperationException("The original source capability owner was not published.");

    internal CacheMemorySnapshot Memory => memory.GetSnapshot();

    public ValueTask DisposeAsync()
    {
        lock (gate)
        { return new(closing ??= DisposeCoreAsync()); }
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        if (source is not null)
        {
            try
            { await source.DisposeAsync().ConfigureAwait(false); }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        }
        if (work is not null)
        {
            try
            { await work.DisposeAsync().ConfigureAwait(false); }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        }
        try
        { memory.Dispose(); }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
