using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Selects an actual committed entry above its original snapshot; never reconstructs a compacted proposal.</summary>
internal static class PartitionWriterOrleansRetainedEntry
{
    private const long EmptyIndex = 0;

    internal static async Task<PartitionWriterOrleansOriginal> ReadAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, CancellationToken cancellationToken)
    {
        PartitionWriterOrleansOriginal? selected = null;
        foreach (var node in TwoRf3MembershipProtocol.Nodes.Take(TwoRf3MembershipProtocol.MembersPerGroup))
        {
            await PartitionWriterOrleansStores.UseAsync(wave, node, seed, async (_, _, log, database) =>
            {
                var first = checked((log.State.Snapshot?.Index ?? EmptyIndex) + PartitionWriterOrleansProtocol.PositionStep);
                var last = log.State.CommittedIndex;
                if (last - first > database.Limits.MaxScanRecords)
                { throw new InvalidOperationException(PartitionWriterOrleansProtocol.IncompleteImage); }
                for (var index = first; index <= last; index = checked(index + PartitionWriterOrleansProtocol.PositionStep))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = log.ReadEntry(index)
                        ?? throw new InvalidOperationException(PartitionWriterOrleansProtocol.MissingRetainedEntry);
                    if (entry.Operation is not { Kind: OperationKind.PartitionMovementPhase } operation)
                    { continue; }
                    var command = NativeCommandPayload.Read<PartitionMovePhaseCommand>(operation);
                    if (command.MoveId != seed.FirstRequest.MoveId
                        || command.Stage != PartitionMovePeerStage.ControlCompleteRetirement)
                    { continue; }
                    var result = database.ResolveOutcome(operation);
                    await PartitionWriterOrleansNativeAssertions.OriginalAsync(seed, entry, command, result);
                    selected = new(node, entry, result, NativeSerialization.Serialize(log.State));
                }
            }).ConfigureAwait(false);
            if (selected is not null)
            { return selected; }
        }
        throw new InvalidOperationException(PartitionWriterOrleansProtocol.MissingRetainedEntry);
    }
}
