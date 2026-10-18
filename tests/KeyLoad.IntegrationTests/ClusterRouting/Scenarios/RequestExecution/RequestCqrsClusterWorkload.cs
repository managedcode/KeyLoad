using System.Collections.Immutable;
using System.Text.Json;
using Aspire.Hosting;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record RequestCqrsClusterWorkload(PartitionRef Partition, CommandRequest SeedCommand,
    CommitReceipt SeedReceipt, string[] InitialDocuments)
{
    internal static async Task<RequestCqrsClusterWorkload> SeedAsync(DistributedApplication app,
        NodeEpochClusterProfile profile, CancellationToken cancellationToken)
    {
        var partition = new PartitionRef("c1-" + Guid.NewGuid().ToString("N"), RequestCqrsClusterProtocol.Database,
            RequestCqrsClusterProtocol.Domain, Guid.NewGuid().ToString("N"));
        await using var clients = await RequestCqrsClusterCallers.ConnectAsync(app,
            RequestCqrsClusterProtocol.Node1, profile.AdminKey, cancellationToken).ConfigureAwait(false);
        await ConfigureAsync(clients.Sdk, partition, cancellationToken).ConfigureAwait(false);
        var initial = Enumerable.Range(0, RequestCqrsClusterProtocol.RequestCount)
            .Select(index => InitialJson(index)).ToArray();
        var command = CreateSeedCommand(partition, initial);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await clients.Sdk.CommitAsync(command, cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        var replay = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await clients.Mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await AssertReceiptAsync(replay.Value, receipt).ConfigureAwait(false);
        return new(partition, command, receipt, initial);
    }

    internal async Task VerifyPreservedAsync(DistributedApplication app, NodeEpochClusterProfile profile,
        CancellationToken cancellationToken, bool currentWriteExists = false)
    {
        await using var clients = await RequestCqrsClusterCallers.ConnectAsync(app,
            RequestCqrsClusterProtocol.Node1, profile.AdminKey, cancellationToken).ConfigureAwait(false);
        var sdkReceipt = await McpCallerAssertions.SdkSuccessAsync(await clients.Sdk.CommitAsync(SeedCommand,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var mcpReceipt = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await clients.Mcp.CallAsync(
            McpCallerTools.DocumentsCommit, SeedCommand, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await AssertReceiptAsync(sdkReceipt, SeedReceipt).ConfigureAwait(false);
        await AssertReceiptAsync(mcpReceipt.Value, SeedReceipt).ConfigureAwait(false);
        for (var index = 0; index < InitialDocuments.Length; index++)
        {
            var updated = currentWriteExists && index == 0;
            await VerifyDocumentAsync(clients, index,
                updated ? RequestCqrsClusterProtocol.ChangedDocumentJson : InitialDocuments[index],
                updated ? 2 : 1, cancellationToken).ConfigureAwait(false);
        }
    }

    internal async Task<CommitReceipt> AppendCurrentWriteAsync(DistributedApplication app,
        NodeEpochClusterProfile profile, string writerNode, CancellationToken cancellationToken)
    {
        await using var clients = await RequestCqrsClusterCallers.ConnectAsync(app, writerNode, profile.AdminKey, cancellationToken)
            .ConfigureAwait(false);
        var id = DocumentId(0);
        var command = new CommandRequest(Guid.NewGuid(), Partition,
            [new PutDocument(RequestCqrsClusterProtocol.AdminCollection, id,
                RequestCqrsClusterProtocol.ChangedDocumentJson, ExpectedRevision: 1, ExplicitReplacement: true)]);
        var receipt = await RequestCqrsClusterCurrentWriteReconciliation.CommitAsync(clients.Sdk, command,
            cancellationToken).ConfigureAwait(false);
        await VerifyDocumentAsync(clients, 0, RequestCqrsClusterProtocol.ChangedDocumentJson, 2, cancellationToken)
            .ConfigureAwait(false);
        return receipt;
    }

    internal async Task VerifyCurrentWriteAsync(DistributedApplication app, NodeEpochClusterProfile profile,
        string readerNode, CommitReceipt expected, CancellationToken cancellationToken)
    {
        await using var clients = await RequestCqrsClusterCallers.ConnectAsync(app, readerNode, profile.AdminKey, cancellationToken)
            .ConfigureAwait(false);
        var id = DocumentId(0);
        var command = new CommandRequest(expected.CommandId, Partition,
            [new PutDocument(RequestCqrsClusterProtocol.AdminCollection, id,
                RequestCqrsClusterProtocol.ChangedDocumentJson, ExpectedRevision: 1, ExplicitReplacement: true)]);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await clients.Sdk.CommitAsync(command,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await clients.Mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await AssertReceiptAsync(sdk, expected).ConfigureAwait(false);
        await AssertReceiptAsync(mcp.Value, expected).ConfigureAwait(false);
        await VerifyDocumentAsync(clients, 0, RequestCqrsClusterProtocol.ChangedDocumentJson, 2, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ConfigureAsync(KeyLoadClient sdk, PartitionRef partition,
        CancellationToken cancellationToken)
    {
        var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId,
            new ResourceDefinition(RequestCqrsClusterProtocol.AdminCollection, ResourceKind.Collection,
                RequestCqrsClusterProtocol.Domain));
        await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureResourceAsync(Guid.NewGuid(), request,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
    }

    private static CommandRequest CreateSeedCommand(PartitionRef partition, IReadOnlyList<string> documents)
    {
        var mutations = documents.Select((json, index) => (Mutation)new PutDocument(
            RequestCqrsClusterProtocol.AdminCollection, DocumentId(index), json, ExpectedRevision: 0)).ToArray();
        return new(Guid.NewGuid(), partition, ImmutableArray.CreateRange(mutations));
    }

    private async Task VerifyDocumentAsync(RequestCqrsClusterCallers clients, int index, string expected,
        long revision, CancellationToken cancellationToken)
    {
        var reference = new EntityRef(Partition, RequestCqrsClusterProtocol.AdminCollection, DocumentId(index));
        var sdk = await PublicReadAssertions.DocumentAsync(await clients.Sdk.GetAsync(reference,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await PublicReadAssertions.DocumentAsync(await clients.Mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), cancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);
        await Assert.That(sdk?.Revision).IsEqualTo(revision);
        await Assert.That(mcp.Value?.Revision).IsEqualTo(revision);
        await Assert.That(sdk?.Json).IsEqualTo(expected);
        await Assert.That(mcp.Value?.Json).IsEqualTo(expected);
    }

    private static async Task AssertReceiptAsync(CommitReceipt actual, CommitReceipt expected)
    {
        await Assert.That(actual.CommandId).IsEqualTo(expected.CommandId);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    private static string DocumentId(int index)
        => "document-" + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);

    private static string InitialJson(int index)
        => JsonSerializer.Serialize(new
        {
            cohort = "rpc1",
            ordinal = index,
            value = index + 1
        });
}
