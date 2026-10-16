using KeyLoad.Clustering;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal sealed class D2ImportCaptureRuntime : IAsyncDisposable
{
    private readonly CacheMemoryBudget memory;
    private readonly NativeRequestWorkOwner? work;
    private readonly PartitionMovementSourceOwner? source;
    private readonly Lock disposalGate = new();
    private Task? closing;
    internal PartitionMovementSourceOwner Source => source!;

    internal D2ImportCaptureRuntime(D2ImportSession session, PartitionMovementTransportRequest request)
    {
        memory = new(session.SourceRuntime.Core.CacheMemory);
        try
        {
            work = new(session.SourceRuntime.GrainRouting);
            source = new(session.Source.Database, memory, session.SourceRuntime.Core.DatabaseLimits, work,
                session.Source.Database.EvaluationClock, new D2ImportCaptureSettlement(session, request).SettleAsync);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            Task? terminal = null;
            try
            {
                try
                {
                    terminal = DisposeAsync().AsTask();
                    terminal.GetAwaiter().GetResult();
                }
                catch (Exception cleanup)
                {
                    PreserveFailure(terminal, cleanup, failures);
                    throw;
                }
            }
            catch (Exception)
            {
                ServerFailureObserver.ThrowIfAny(failures);
                throw;
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (disposalGate)
        {
            if (closing is null)
            {
                var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                closing = CloseAsync(registered.Task);
                registered.SetResult();
            }
            return new(closing);
        }
    }

    private async Task CloseAsync(Task registered)
    {
        await registered.ConfigureAwait(false);
        var failures = new List<Exception>();
        Task? terminal = null;
        try
        {
            try
            {
                if (source is not null)
                {
                    terminal = source.DisposeAsync().AsTask();
                    await terminal.ConfigureAwait(false);
                }
            }
            catch (Exception failure)
            {
                PreserveFailure(terminal, failure, failures);
                throw;
            }
            finally
            {
                await CloseWorkAndMemoryAsync(failures).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task CloseWorkAndMemoryAsync(List<Exception> failures)
    {
        Task? terminal = null;
        try
        {
            if (work is not null)
            {
                terminal = work.DisposeAsync().AsTask();
                await terminal.ConfigureAwait(false);
            }
        }
        catch (Exception failure)
        {
            PreserveFailure(terminal, failure, failures);
            throw;
        }
        finally
        {
            CloseMemory(failures);
        }
    }

    private void CloseMemory(List<Exception> failures)
    {
        try
        {
            memory.Dispose();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
            throw;
        }
    }

    private static void PreserveFailure(Task? terminal, Exception failure, List<Exception> failures)
    {
        if (terminal?.Exception is { } aggregate)
        {
            failures.AddRange(aggregate.InnerExceptions);
        }
        else
        {
            failures.Add(failure);
        }
    }
}
