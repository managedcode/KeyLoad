using KeyLoad.Client;
using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueuePolicyEpochRf3Assertions
{
    internal static async Task CountersAsync(IReadOnlyDictionary<string, NativeBlobWholeOwnerCut> cuts,
        QueuePolicyEpochRf3Seed seed, CancellationToken token)
    {
        foreach (var node in TwoRf3MembershipProtocol.Nodes.Take(TwoRf3MembershipProtocol.MembersPerGroup))
        {
            token.ThrowIfCancellationRequested();
            var key = Convert.ToHexString(KeySpace.Partition("queue-counters", seed.Lane.Partition, seed.Lane.Queue));
            var row = cuts[node].CanonicalRows.Single(value => value.StartsWith(key + ":", StringComparison.Ordinal));
            var counters = NativeSerialization.Deserialize<QueueCounters>(Convert.FromHexString(row[(key.Length + 1)..]));
            await Assert.That(counters.StoredMessages).IsEqualTo(0L);
            await Assert.That(counters.StoredBytes).IsEqualTo(0L);
            await Assert.That(counters.InFlightMessages).IsEqualTo(0L);
            await Assert.That(counters.InFlightBytes).IsEqualTo(0L);
            await Assert.That(counters.DeadLetterMessages).IsEqualTo(0L);
            await Assert.That(counters.DeadLetterBytes).IsEqualTo(0L);
        }
    }

    internal static async Task<MessageInspection> InspectAsync(KeyLoadClient sdk, QueuePolicyEpochRf3Seed seed,
        CancellationToken token)
        => await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(new(seed.Lane,
            QueuePolicyEpochRf3Seed.Message), token))
            ?? throw new InvalidOperationException("The original acknowledged queue message is absent.");

    internal static Task<bool> OriginalAsync(TwoRf3MembershipWave wave, QueuePolicyEpochRf3Seed seed,
        CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            seed.Credential, async callers =>
        {
            var original = await InspectAsync(callers.Sdk, seed, token);
            await Assert.That(original.Metadata.Id).IsEqualTo(QueuePolicyEpochRf3Seed.Message);
            await Assert.That(original.Metadata.State).IsEqualTo(MessageState.Acked);
            await Assert.That(original.Metadata.Attempts).IsEqualTo(1);
            await Assert.That(original.Metadata.LeaseOwner).IsNull();
            await Assert.That(original.Metadata.LeaseUntil).IsNull();
            await Assert.That(original.PayloadJson).IsNull();
            await Assert.That(original.HeadersJson).IsNull();
            var future = await PublicReadAssertions.MessageAsync(await callers.Sdk.InspectAsync(
                new(new(seed.Lane.Partition, QueuePolicyEpochRf3Seed.FutureQueue), QueuePolicyEpochRf3Seed.Message), token));
            await SqlRf3Protocol.EqualAsync(seed.Future, future);
            return true;
        }, token);
}
