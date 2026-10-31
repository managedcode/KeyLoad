using System.Collections.Immutable;
using KeyLoad.Clustering;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.StorageReplication;

/// <summary>Refuses one original copy transport and holds its real repair RPC before execution.</summary>
internal sealed class StorageCopyCallFault(Guid commandId) : IOutgoingGrainCallFilter
{
    private const int RefusedCopy = 2;
    private const int OperationsArgument = 0;
    private const int CancellationArgument = 2;
    private int sends;
    internal TaskCompletionSource RepairArrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource RepairSettled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int Sends => Volatile.Read(ref sends);

    public async Task Invoke(IOutgoingGrainCallContext context)
    {
        if (!Selects(context))
        {
            await context.Invoke().ConfigureAwait(true);
            return;
        }
        var attempt = Interlocked.Increment(ref sends);
        if (attempt == RefusedCopy)
        {
            throw new IOException("The selected original storage copy transport was interrupted.");
        }
        try
        {
            if (attempt > RefusedCopy)
            {
                RepairArrived.TrySetResult();
                var token = (CancellationToken)context.Request.GetArgument(CancellationArgument)!;
                await Release.Task.WaitAsync(token).ConfigureAwait(true);
            }
            await context.Invoke().ConfigureAwait(true);
        }
        finally
        {
            if (attempt > RefusedCopy)
            {
                RepairSettled.TrySetResult();
            }
        }
    }

    private bool Selects(IOutgoingGrainCallContext context)
        => context.InterfaceMethod.DeclaringType == typeof(ISiloDatabaseService)
            && context.MethodName == nameof(ISiloDatabaseService.ApplyBatchAsync)
            && context.Request.GetArgument(OperationsArgument) is ImmutableArray<Command<DatabaseCommand>> operations
            && operations.Any(operation => operation.CommandId == commandId);

    internal void ReleaseRepair() => Release.TrySetResult();
}
