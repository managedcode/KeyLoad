using KeyLoad.Core;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class AtomicPartitionScopeLifecycleReplay
{
    internal static async Task AllAsync(DatabaseEngine database, AtomicPartitionScopeLifecycleState state, CancellationToken token)
    {
        foreach (var (request, original) in state.Originals)
        {
            var replay = AtomicPartitionScopeLifecycleCommands.Apply(database, request, token);
            if (Errors.Code(original.Problem) is null)
            {
                await ReplayResultAssertions.Same<CommitReceipt>(replay, original);
            }
            else
            {
                await AtomicPartitionScopeLifecycleReplies.FailureAsync(replay, original.Problem!.Detail!);
            }
            await AtomicPartitionScopeLifecycleModels.CurrentAsync(database, state, null, token);
        }
    }
    internal static async Task WriteAsync(DatabaseEngine database, AtomicPartitionScopeLifecycleState state,
        Guid commandId, int index, bool healthy, CancellationToken token)
    {
        var request = AtomicPartitionScopeLifecycleCommands.Write(commandId, state.Partition(index), healthy);
        var result = AtomicPartitionScopeLifecycleCommands.Apply(database, request, token);
        await AtomicPartitionScopeLifecycleReplies.SuccessAsync(result, request, state, healthy);
        if (healthy)
        {
            state.Healthy[index] = true;
        }
        else
        {
            state.Present[index] = true;
        }
        state.Originals.Add((request, result));
        await AtomicPartitionScopeLifecycleModels.CurrentAsync(database, state, index, token);
    }
    internal static async Task RefuseAsync(DatabaseEngine database, AtomicPartitionScopeLifecycleState state,
        CommandRequest request, string detail, CancellationToken token)
    {
        var result = AtomicPartitionScopeLifecycleCommands.Apply(database, request, token);
        await AtomicPartitionScopeLifecycleReplies.FailureAsync(result, detail);
        state.Originals.Add((request, result));
        await AtomicPartitionScopeLifecycleModels.CurrentAsync(database, state, null, token);
    }
}
