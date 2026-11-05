using System.Collections.Concurrent;
using System.Collections.Immutable;
using KeyLoad.Clustering;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.StorageReplication;

/// <summary>Records the actual coordinator and storage-service addresses for one authorized command.</summary>
internal sealed class StoragePreparationObservation(Guid commandId) : IOutgoingGrainCallFilter
{
    private readonly ConcurrentQueue<(SiloAddress Source, SiloAddress Target)> calls = new();
    internal (SiloAddress Source, SiloAddress Target)[] Calls => calls.ToArray();

    public async Task Invoke(IOutgoingGrainCallContext context)
    {
        if (context.InterfaceMethod.DeclaringType == typeof(ISiloDatabaseService)
            && context.MethodName == nameof(ISiloDatabaseService.PrepareBatchAsync)
            && context.Request.GetArgument(0) is ImmutableArray<Command<DatabaseCommand>> operations
            && operations.Any(operation => operation.CommandId == commandId))
        {
            if (context.SourceContext?.Address.SiloAddress is not { } source
                || !SystemTargetGrainId.TryParse(context.TargetId, out var target))
            {
                throw new InvalidOperationException("The storage preparation call did not expose its actual Orleans addresses.");
            }
            calls.Enqueue((source, target.GetSiloAddress()));
        }
        await context.Invoke();
    }
}
