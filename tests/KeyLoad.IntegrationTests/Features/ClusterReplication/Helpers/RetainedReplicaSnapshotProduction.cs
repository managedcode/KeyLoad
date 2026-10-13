using static KeyLoad.IntegrationTests.Features.ClusterReplication.ClusterReplicationTestSupport;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class RetainedReplicaSnapshotProduction
{
    internal static async Task<RetainedReplicaSnapshotScenario.FinalCommand> ReplicateWhileStoppedAsync(
        ClusterFixture fixture, RetainedReplicaSnapshotScenario.SnapshotState state, int stoppedIndex,
        int nodeCount, int documentCount, string snapshotDirectory, CancellationToken cancellationToken)
    {
        var availableNodes = Enumerable.Range(0, state.Clients.Length).Where(index => index != stoppedIndex).ToArray();
        CommandRequest? last = null;
        CommitReceipt? committed = null;
        foreach (var index in Enumerable.Range(0, documentCount))
        {
            var command = new CommandRequest(Guid.NewGuid(), state.Partition,
                [new PutDocument(snapshotDirectory, "doc-" + index, "{\"n\":" + index + "}", 0)]);
            last = command;
            var survivorIndex = availableNodes[index % availableNodes.Length];
            var client = state.Clients[survivorIndex];
            string? lastErrorCode = null;
            string? lastSafeDetail = null;
            try
            {
                committed = Success(await RetryDuringElectionAsync(async () =>
                {
                    var result = await client.CommitAsync(command, cancellationToken);
                    if (!result.IsSuccess)
                    {
                        lastErrorCode = result.Problem?.ErrorCode;
                        lastSafeDetail = result.Problem?.Detail;
                    }
                    return result;
                }, cancellationToken));
            }
            catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested)
            {
                var failures = new List<Exception> { error };
                KeyLoad.Server.ServerFailureObserver.Observe(() =>
                    Console.Error.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        phase = nameof(ReplicateWhileStoppedAsync),
                        command.CommandId,
                        commandOrdinal = index,
                        survivorNode = NodeName(survivorIndex + 1),
                        lastErrorCode,
                        lastSafeDetail
                    })), failures);
                KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
                throw;
            }
        }

        foreach (var number in Enumerable.Range(1, nodeCount).Where(number => number != stoppedIndex + 1))
        {
            await EventuallyAsync(() => Task.FromResult(Directory.EnumerateFiles(
                Path.Combine(fixture.Root, NodeName(number), snapshotDirectory), "*.snapshot").Any()), cancellationToken);
        }

        return new(last!, committed!);
    }
}

