using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferNativeReplayPairAssertions
{
    internal static async Task RequireOriginalRepliesAsync(IReadOnlyList<RequestCqrsNativeReplayWitness> pair,
        CommandRequest accept, IReadOnlyList<ReplicaSiloDiscovery> source, IReadOnlyList<ReplicaSiloDiscovery> destination,
        string sourceSecret, string destinationSecret, RemoteTransferPostAwaitNativeCut committed, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (pair.Count != 2 || pair.Select(value => value.Ordinal).Distinct().Count() != 2)
        {
            throw Missing();
        }
        var firstWitness = pair.Single(value => value.Ordinal == 1);
        var secondWitness = pair.Single(value => value.Ordinal == 2);
        if (firstWitness.ArmId != secondWitness.ArmId || firstWitness.SessionId != secondWitness.SessionId
            || firstWitness.RequestId != secondWitness.RequestId || firstWitness.CommandId != secondWitness.CommandId
            || firstWitness.ConnectionId != secondWitness.ConnectionId
            || !firstWitness.OriginalNativeRequest.AsSpan().SequenceEqual(secondWitness.OriginalNativeRequest))
        {
            throw Missing();
        }
        var first = await RemoteTransferNativeReplayAssertions.RequireAsync(firstWitness, RemoteModelNativeReplayVariation.Original2,
            1, accept, source, destination, sourceSecret, destinationSecret, token);
        var second = await RemoteTransferNativeReplayAssertions.RequireAsync(secondWitness, RemoteModelNativeReplayVariation.Original2,
            2, accept, source, destination, sourceSecret, destinationSecret, token);
        if (first.Request.Nonce == second.Request.Nonce)
        {
            throw Missing();
        }
        await RemoteTransferNativeReplyAssertions.RequireAsync(first.Request, first.Reply, sourceSecret, destinationSecret, committed);
        token.ThrowIfCancellationRequested();
    }

    private static InvalidOperationException Missing() => new(RemoteTransferDistinctProtocol.Missing);
}
