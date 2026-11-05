using System.Collections.Immutable;
using KeyLoad.Clustering;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.StorageReplication;

/// <summary>Refuses one original copy transport and holds its real repair RPC before execution.</summary>
internal sealed class StorageCopyCallFault(Guid commandId) : IOutgoingGrainCallFilter
{
    private const int FirstRefusal = 1;
    private const int OperationsArgument = 0;
    private const int CancellationArgument = 3;
    private int sends;
    private int refused;
    internal SiloAddress? InterruptedSilo { get; set; }
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
        Interlocked.Increment(ref sends);
        if (!SystemTargetGrainId.TryParse(context.TargetId, out var target) || InterruptedSilo is null)
        {
            throw new InvalidOperationException("The copy interruption requires its actual target silo.");
        }
        var selectedTarget = target.GetSiloAddress().Equals(InterruptedSilo);
        if (selectedTarget && Interlocked.CompareExchange(ref refused, FirstRefusal, 0) == 0)
        {
            throw new IOException("The selected original storage copy transport was interrupted.");
        }
        try
        {
            if (selectedTarget)
            {
                RepairArrived.TrySetResult();
                var token = (CancellationToken)context.Request.GetArgument(CancellationArgument)!;
                await Release.Task.WaitAsync(token).ConfigureAwait(true);
            }
            await context.Invoke().ConfigureAwait(true);
        }
        finally
        {
            if (selectedTarget)
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
