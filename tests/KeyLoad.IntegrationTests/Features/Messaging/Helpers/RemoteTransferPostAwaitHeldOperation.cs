using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferPostAwaitHeldOperation
{
    internal static async Task<PrincipalRecord> ExecuteAsync(PartitionMovementLateNativeOwners owners,
        PartitionMovementBlobWireCallers administrator, RemoteTransferPostAwaitCallers calls,
        RemoteTransferColdSeed seed, CommandRequest accept, RemoteTransferOriginalResponseGate gate,
        CancellationToken token)
    {
        var original = calls.Sdk.CommitAsync(accept, token);
        owners.TrackProducer(original);
        try
        {
            var first = await Task.WhenAny(gate.Held, original).WaitAsync(token);
            if (first == original)
            {
                _ = await McpCallerAssertions.SdkSuccessAsync(await original);
                throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
            }
            await gate.Held;
            var producer = gate.OriginalProducer ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
            owners.TrackProducer(producer);
            var before = RemoteTransferPostAwaitNativeCut.Read(owners, seed, accept);
            var source = RemoteTransferPostAwaitNativeCut.ReadSource(owners, seed, accept);
            await RemoteTransferColdAssertions.ReceiptLiteralAsync(before.OriginalReceipt, accept,
                seed.Scenario.DestinationQueue, seed.TransferId, RemoteTransferColdProtocol.AcceptKind,
                before.OriginalReceipt.Token.Position);
            var revoked = MessagingRf3Identity.WithCapability(seed.Identity.Principal, seed.Scenario.DestinationQueue,
                RemoteTransferColdProtocol.Target & ~Capability.QueuePublish);
            await SqlRf3Protocol.EqualAsync(revoked, await McpCallerAssertions.SdkSuccessAsync(
                await administrator.Source.ConfigurePrincipalAsync(Guid.NewGuid(), revoked, token)));
            await before.RequireAsync(RemoteTransferPostAwaitNativeCut.Read(owners, seed, accept));
            await Assert.That(RemoteTransferPostAwaitNativeCut.ReadSource(owners, seed, accept)).IsEqualTo(source);
            gate.Release();
            await QueueProducerRf3Assertions.DeniedAsync(await original, ErrorCode.UnknownWriteOutcome);
            await producer;
            await before.RequireAsync(RemoteTransferPostAwaitNativeCut.Read(owners, seed, accept));
            await Assert.That(RemoteTransferPostAwaitNativeCut.ReadSource(owners, seed, accept)).IsEqualTo(source);
            var restored = MessagingRf3Identity.WithCapability(revoked, seed.Scenario.DestinationQueue,
                RemoteTransferColdProtocol.Target);
            await SqlRf3Protocol.EqualAsync(restored, await McpCallerAssertions.SdkSuccessAsync(
                await administrator.Source.ConfigurePrincipalAsync(Guid.NewGuid(), restored, token)));
            await calls.ReplayAsync(accept, before.OriginalReceipt, token);
            await before.RequireAsync(RemoteTransferPostAwaitNativeCut.Read(owners, seed, accept));
            return restored;
        }
        finally
        {
            gate.Release();
            if (gate.OriginalProducer is { } producer && !producer.IsCompleted)
            { owners.TrackProducer(producer); }
        }
    }
}
