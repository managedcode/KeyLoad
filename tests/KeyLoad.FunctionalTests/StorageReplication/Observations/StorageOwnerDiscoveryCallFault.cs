using System.Collections.Immutable;
using KeyLoad.Clustering;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.StorageReplication;

/// <summary>Refuses one prepare RPC, then holds the next real owner-cut discovery before transport.</summary>
internal sealed class StorageOwnerDiscoveryCallFault(Guid commandId) : IOutgoingGrainCallFilter
{
    private const int FirstCall = 1;
    private const int PayloadArgument = 0;
    private const int CancellationArgument = 0;
    private int refused;
    private int armed;
    private int cuts;
    private int unarmedCuts;
    private int prepared;
    internal Guid CommandId => commandId;
    internal TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int CutCalls => Volatile.Read(ref cuts);
    internal int UnarmedCutCalls => Volatile.Read(ref unarmedCuts);
    internal int PreparedCalls => Volatile.Read(ref prepared);

    public async Task Invoke(IOutgoingGrainCallContext context)
    {
        if (context.InterfaceMethod.DeclaringType == typeof(ISiloDatabaseService))
        {
            if (context.MethodName == nameof(ISiloDatabaseService.ReadCutAsync)
                && Volatile.Read(ref refused) != 0 && Volatile.Read(ref armed) == 0)
            {
                Interlocked.Increment(ref unarmedCuts);
            }
            if (context.MethodName == nameof(ISiloDatabaseService.PrepareBatchAsync) && Volatile.Read(ref armed) != 0)
            {
                Interlocked.Increment(ref prepared);
            }
            if (context.MethodName == nameof(ISiloDatabaseService.PrepareBatchAsync)
                && context.Request.GetArgument(PayloadArgument) is ImmutableArray<Command<DatabaseCommand>> operations
                && operations.Any(operation => operation.CommandId == commandId)
                && Interlocked.Exchange(ref refused, FirstCall) == 0)
            {
                throw new TimeoutException("The selected storage preparation transport was interrupted before dispatch.");
            }
            if (Volatile.Read(ref armed) != 0 && context.MethodName == nameof(ISiloDatabaseService.ReadCutAsync)
                && Interlocked.Increment(ref cuts) == FirstCall)
            {
                Arrived.TrySetResult();
                var token = (CancellationToken)context.Request.GetArgument(CancellationArgument)!;
                await Release.Task.WaitAsync(token).ConfigureAwait(true);
            }
        }
        await context.Invoke().ConfigureAwait(true);
    }

    internal void Arm() => Volatile.Write(ref armed, FirstCall);
    internal void ReleaseDiscovery() => Release.TrySetResult();
}
