using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class NativeRemoteGraphRf3Assertions
{
    internal static async Task ResultAsync(NativeRemoteGraphRf3Seed seed, RemoteGraphEdgeResultV1 actual,
        long revision, bool deleted)
    {
        await Assert.That(actual.Deliveries.Length).IsEqualTo(1);
        await Assert.That(actual.SourceReceipt.Token.AtomicPartitionId).IsEqualTo(seed.SourcePartition.AtomicPartitionId);
        await Assert.That(actual.SourceReceipt.Token.Incarnation).IsEqualTo(seed.Directory.ControlOwner.Incarnation);
        var delivery = actual.Deliveries.Single();
        await SqlRf3Protocol.EqualAsync(seed.Upsert.Edge.To, delivery.Destination);
        await Assert.That(delivery.Revision).IsEqualTo(revision);
        await Assert.That(delivery.Deleted).IsEqualTo(deleted);
        await Assert.That(delivery.TargetReceipt.Token.AtomicPartitionId).IsEqualTo(seed.TargetPartition.AtomicPartitionId);
        await Assert.That(delivery.TargetReceipt.Token.Incarnation).IsEqualTo(seed.Directory.Owners.Single(
            value => value.Owner.PhysicalShardId != seed.Directory.ControlOwner.PhysicalShardId).Owner.Incarnation);
        await Assert.That(delivery.CompletionReceipt.Token.AtomicPartitionId).IsEqualTo(seed.SourcePartition.AtomicPartitionId);
        foreach (var receipt in new[] { actual.SourceReceipt, delivery.TargetReceipt, delivery.CompletionReceipt })
        {
            await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        }
    }

    internal static async Task IncomingAsync(RequestCqrsRf3Callers caller, NativeRemoteGraphRf3Seed seed,
        bool present, CancellationToken token)
    {
        var pages = await NativeRemoteGraphPublicCallers.IncomingParityAsync(caller.Sdk, caller.Mcp,
            new(1, seed.Upsert.Edge.To, NativeRemoteGraphRf3Seed.Graph, 8), token);
        foreach (var page in new[] { pages.Sdk, pages.Mcp })
        {
            await Assert.That(page.Projection).IsEqualTo("remote-owner-eventual-v1");
            await Assert.That(page.Rows.Length).IsEqualTo(present ? 1 : 0);
            var target = seed.Directory.Owners.Single(value => value.Owner.PhysicalShardId != seed.Directory.ControlOwner.PhysicalShardId).Owner;
            await Assert.That(page.TargetCut.Owner.PhysicalShardId).IsEqualTo(target.PhysicalShardId);
            await Assert.That(page.TargetCut.Incarnation).IsEqualTo(target.Incarnation);
            await Assert.That(page.TargetCut.NodeId).IsNotEqualTo(Guid.Empty);
            await Assert.That(page.TargetCut.PolicyEpoch).IsEqualTo(3L);
            if (!present)
            {
                continue;
            }
            var row = page.Rows.Single();
            await SqlRf3Protocol.EqualAsync(new EdgeRecord(seed.Upsert.Edge.EdgeId, seed.Upsert.Edge.From,
                seed.Upsert.Edge.To, seed.Upsert.Edge.Label, seed.Upsert.Edge.AttributesJson, 1), row.Edge);
            await Assert.That(row.DeliveredRevision).IsEqualTo(1L);
            await Assert.That(row.SourceCut.Owner.PhysicalShardId).IsEqualTo(seed.Directory.ControlOwner.PhysicalShardId);
            await Assert.That(row.SourceCut.Incarnation).IsEqualTo(seed.Directory.ControlOwner.Incarnation);
            await Assert.That(row.SourceCut.NodeId).IsNotEqualTo(Guid.Empty);
            await Assert.That(row.SourceCut.PolicyEpoch).IsEqualTo(3L);
        }
    }

    internal static async Task DocumentsAsync(TwoRf3MembershipWave wave, NativeRemoteGraphRf3Seed seed, CancellationToken token)
    {
        foreach (var (node, original, expected) in new[] { (TwoRf3MembershipProtocol.Node1, seed.SourceDocuments, seed.SourceReceipt),
            (TwoRf3MembershipProtocol.Node4, seed.TargetDocuments, seed.TargetReceipt) })
        {
            _ = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, node, wave.Profile.AdminKey, async root =>
            {
                await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(original, token)));
                await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                    await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, original, token))).Value);
                var request = new GetDocumentRequest(new(original.Partition, NativeRemoteGraphRf3Seed.Documents, NativeRemoteGraphRf3Seed.Vertex));
                var sdk = await PublicReadAssertions.DocumentAsync(await root.Sdk.GetAsync(request.Reference, token));
                var mcp = (await PublicReadAssertions.RequiredDocumentAsync(await root.Mcp.CallAsync(McpCallerTools.DocumentsGet, request, token))).Value;
                await Assert.That(sdk).IsNotNull();
                await Assert.That(sdk!.Json).IsEqualTo(NativeRemoteGraphRf3Seed.Json);
                await SqlRf3Protocol.EqualAsync(sdk, mcp);
                return true;
            }, token);
        }
    }
}
