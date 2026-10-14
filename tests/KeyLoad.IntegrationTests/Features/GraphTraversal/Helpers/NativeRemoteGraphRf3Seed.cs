using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal sealed record NativeRemoteGraphRf3Seed(string Credential, PrincipalRecord Principal,
    PartitionRef SourcePartition, PartitionRef TargetPartition, PhysicalOwnerDirectoryV1 Directory,
    CommandRequest SourceDocuments, CommitReceipt SourceReceipt, CommandRequest TargetDocuments,
    CommitReceipt TargetReceipt, UpsertRemoteGraphEdgeRequestV1 Upsert,
    DeliverRemoteGraphEdgeRequestV1 Deliver, DeleteRemoteGraphEdgeRequestV1 Delete)
{
    internal Dictionary<string, PrincipalRecord> RestoredPrincipals { get; } = new(StringComparer.Ordinal);

    internal const string Graph = "remote-links";
    internal const string Documents = "remote-vertices";
    internal const string Vertex = "vertex";
    internal const string Json = "{\"remote\":true}";
    public override string ToString() => "NativeRemoteGraphRf3Seed(<private>)";

    internal static async Task<NativeRemoteGraphRf3Seed> CreateAsync(TwoRf3MembershipWave wave,
        string principalId, CancellationToken token)
    {
        await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, token);
        var directory = await PhysicalOwnerDirectoryRf3Assertions.ExpectedAsync(wave.Application, wave.Profile, token);
        var tenant = "remote-graph-" + Guid.NewGuid().ToString("N");
        var source = new PartitionRef(tenant, "graphdb", "graphdomain", Guid.NewGuid().ToString("N"));
        var target = source with
        {
            PartitionKey = Guid.NewGuid().ToString("N")
        };
        var principal = new PrincipalRecord(principalId, tenant,
            [new(source.DatabaseId, Graph, Capability.GraphRead | Capability.GraphWrite),
             new(source.DatabaseId, Documents, Capability.DocumentsRead)], []);
        var key = "graph-key-" + Guid.NewGuid().ToString("N");
        var credential = key + "." + Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var persistedKey = new ApiKeyRecord(key, principalId, Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(credential))));
        var sourceCommand = new CommandRequest(Guid.NewGuid(), source, [new PutDocument(Documents, Vertex, Json, 0)]);
        var targetCommand = new CommandRequest(Guid.NewGuid(), target, [new PutDocument(Documents, Vertex, Json, 0)]);
        var sourceReceipt = await PrepareAsync(wave, TwoRf3MembershipProtocol.Node1, principal, persistedKey,
            sourceCommand, target, directory, token);
        var targetReceipt = await PrepareAsync(wave, TwoRf3MembershipProtocol.Node4, principal, persistedKey,
            targetCommand, target, directory, token);
        var edge = new UpsertEdge(Graph, "remote-edge", new(source, Documents, Vertex), new(target, Documents, Vertex),
            "related", "{}", 0);
        var upsert = new UpsertRemoteGraphEdgeRequestV1(Guid.NewGuid(), source, edge);
        return new(credential, principal, source, target, directory, sourceCommand, sourceReceipt,
            targetCommand, targetReceipt, upsert,
            new(Guid.NewGuid(), source, Graph, edge.EdgeId, edge.To, 1),
            new(Guid.NewGuid(), source, Graph, edge.EdgeId, 1));
    }

    private static Task<CommitReceipt> PrepareAsync(TwoRf3MembershipWave wave, string node,
        PrincipalRecord principal, ApiKeyRecord key, CommandRequest documents, PartitionRef target,
        PhysicalOwnerDirectoryV1 directory, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, node, wave.Profile.AdminKey, async root =>
        {
            foreach (var definition in new[] { new ResourceDefinition(Graph, ResourceKind.Graph, target.TransactionDomainId),
                new ResourceDefinition(Documents, ResourceKind.Collection, target.TransactionDomainId) })
            {
                _ = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                    new(target.TenantId, target.DatabaseId, definition), token));
            }
            var actualTarget = directory.Owners.Single(value => value.Owner.PhysicalShardId
                != directory.ControlOwner.PhysicalShardId).Owner;
            await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.BindAtomicPartitionPlacementAsync(
                Guid.NewGuid(), new(1, 0, target, actualTarget.PhysicalShardId), token))).IsTrue();
            var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(new(1, target), token));
            await Assert.That(placement.PhysicalShardId).IsEqualTo(actualTarget.PhysicalShardId);
            await Assert.That(placement.Incarnation).IsEqualTo(actualTarget.Incarnation);
            var stored = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
            await SqlRf3Protocol.EqualAsync(principal, stored);
            _ = await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(), key, token));
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(documents, token));
            await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, documents, token))).Value);
            return receipt;
        }, token);
}
