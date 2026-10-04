using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Aspire.Hosting;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal sealed record NodeEpochRf3ReadIdentity(string PrincipalId, string Secret, PrincipalRecord Principal,
    ApiKeyRecord Credential)
{
    public override string ToString() => "NodeEpochRf3ReadIdentity(<private>)";
}

internal sealed record NodeEpochRf3Workload(PartitionRef Partition, string Collection, string SeriesSet,
    string SeriesId, string DocumentId, ImmutableArray<SampleData> PriorSamples,
    CommandRequest ReplayCommand, CommitReceipt ReplayReceipt, CommandRequest StaleDocumentCommand,
    NodeEpochRf3ReadIdentity Reader)
{
    internal const string DocumentJson = "{\"generation\":2,\"source\":\"native5\"}";
    internal const string CollectionName = "epoch-documents";
    internal const string SeriesSetName = "epoch-series";
    internal const string SeriesName = "epoch-prior";
    internal const string DocumentName = "stable-document";
    internal const string InitialDocumentJson = "{\"generation\":1,\"source\":\"native5\"}";
    internal const string StaleDocumentJson = "{\"generation\":99,\"source\":\"stale\"}";
    internal const string PriorEventPrefix = "native5-sample-";
    internal const int PriorCount = 24;
    private const string TenantPrefix = "native5-cold-";
    private const string DatabaseName = "database";
    private const string TransactionDomain = "native5-cold-domain";
    private const string PrincipalPrefix = "native5-reader-";
    private const string KeyPrefix = "native5-key-";

    internal static async Task<NodeEpochRf3Workload> ConfigureAsync(DistributedApplication app,
        string adminKey, CancellationToken cancellationToken)
    {
        var partition = CreatePartition();
        await using var callers = await NodeEpochRf3Callers.ConnectAsync(app, NodeEpochRf3Protocol.Node1,
            NodeEpochRf3Protocol.Node2, adminKey, cancellationToken).ConfigureAwait(false);
        await ConfigureResourcesAsync(callers.Sdk, partition, cancellationToken).ConfigureAwait(false);
        var reader = await CreateReaderAsync(callers.Sdk, partition, cancellationToken).ConfigureAwait(false);
        await ConfigureDocumentAsync(callers.Sdk, callers.Mcp, partition, cancellationToken).ConfigureAwait(false);
        var (replay, replayReceipt) = await AppendPriorSeriesAsync(callers.Sdk, callers.Mcp, partition,
            cancellationToken).ConfigureAwait(false);
        await VerifySameCommandReplayAsync(callers.Sdk, callers.Mcp, replay, replayReceipt, cancellationToken)
            .ConfigureAwait(false);
        await VerifySameEventReplayAsync(callers.Sdk, callers.Mcp, partition, cancellationToken).ConfigureAwait(false);
        var stale = await VerifyStaleDocumentAsync(callers.Sdk, callers.Mcp, partition, cancellationToken).ConfigureAwait(false);
        return new(partition, CollectionName, SeriesSetName, SeriesName, DocumentName,
            PriorData(), replay, replayReceipt, stale, reader);
    }

    internal static ImmutableArray<SampleData> PriorData()
        => Enumerable.Range(0, PriorCount).Select(SampleAt).ToImmutableArray();

    internal static SampleData SampleAt(int index)
    {
        var timestamp = NodeEpochRf3Protocol.SampleStart.AddMinutes(index);
        if (index == 11)
        { timestamp = NodeEpochRf3Protocol.SampleStart.AddMinutes(10).ToOffset(TimeSpan.FromHours(2)); }
        return new(PriorEventPrefix + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
            timestamp, index + 1);
    }

    private static PartitionRef CreatePartition() => new(TenantPrefix + Guid.NewGuid().ToString("N"),
        DatabaseName, TransactionDomain, Guid.NewGuid().ToString("N"));

    private static async Task ConfigureResourcesAsync(KeyLoadClient admin, PartitionRef partition,
        CancellationToken cancellationToken)
    {
        foreach (var definition in new[]
        {
            new ResourceDefinition(CollectionName, ResourceKind.Collection, partition.TransactionDomainId),
            new ResourceDefinition(SeriesSetName, ResourceKind.TimeSeries, partition.TransactionDomainId)
        })
        {
            var result = await admin.ConfigureResourceAsync(Guid.NewGuid(),
                new(partition.TenantId, partition.DatabaseId, definition), cancellationToken).ConfigureAwait(false);
            await McpCallerAssertions.SdkSuccessAsync(result).ConfigureAwait(false);
        }
    }

    private static async Task<NodeEpochRf3ReadIdentity> CreateReaderAsync(KeyLoadClient admin,
        PartitionRef partition, CancellationToken cancellationToken)
    {
        var id = PrincipalPrefix + Guid.NewGuid().ToString("N");
        var grants = ImmutableArray.Create(new ScopeGrant(partition.DatabaseId, CollectionName, Capability.DocumentsRead),
            new ScopeGrant(partition.DatabaseId, SeriesSetName, Capability.SeriesRead));
        var principal = new PrincipalRecord(id, partition.TenantId, grants, []);
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigurePrincipalAsync(
            Guid.NewGuid(), principal, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var keyId = KeyPrefix + Guid.NewGuid().ToString("N");
        var secret = keyId + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var key = new ApiKeyRecord(keyId, id,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigureApiKeyAsync(Guid.NewGuid(), key,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        return new(id, secret, persisted, key);
    }

    private static async Task ConfigureDocumentAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        PartitionRef partition, CancellationToken cancellationToken)
    {
        var create = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(CollectionName, DocumentName, InitialDocumentJson, ExpectedRevision: 0)]);
        var created = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(create, cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(created.Mutations[0].Revision).IsEqualTo(1L);
        var replace = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(CollectionName, DocumentName, DocumentJson, ExpectedRevision: 1, ExplicitReplacement: true)]);
        var replaced = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.DocumentsCommit, replace, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(replaced.Value.Mutations[0].Revision).IsEqualTo(2L);
    }

    private static async Task<(CommandRequest Command, CommitReceipt Receipt)> AppendPriorSeriesAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        PartitionRef partition, CancellationToken cancellationToken)
    {
        CommandRequest? replay = null;
        CommitReceipt? replayReceipt = null;
        for (var index = 0; index < PriorCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sample = SampleAt(index);
            var tags = Tags(index);
            var command = new CommandRequest(Guid.NewGuid(), partition,
                [new AppendSamples(SeriesSetName, SeriesName, [sample], tags)]);
            var receipt = index % 2 == 0
                ? await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, cancellationToken).ConfigureAwait(false))
                    .ConfigureAwait(false)
                : (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
                    McpCallerTools.DocumentsCommit, command, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false)).Value;
            await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
            await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
            if (index == PriorCount - 1)
            {
                replay = command;
                replayReceipt = receipt;
            }
        }
        return (replay ?? throw new InvalidOperationException("No prior time-series command was created."),
            replayReceipt ?? throw new InvalidOperationException("No prior time-series receipt was created."));
    }

    private static async Task<CommandRequest> VerifyStaleDocumentAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        PartitionRef partition, CancellationToken cancellationToken)
    {
        var stale = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(CollectionName, DocumentName, StaleDocumentJson, ExpectedRevision: 1, ExplicitReplacement: true)]);
        var sdkResult = await sdk.CommitAsync(stale, cancellationToken).ConfigureAwait(false);
        await Assert.That(sdkResult.IsFailed).IsTrue();
        await Assert.That(sdkResult.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.RevisionConflict));
        var mcpResult = await mcp.CallAsync(McpCallerTools.DocumentsCommit, stale, cancellationToken).ConfigureAwait(false);
        await McpCallerAssertions.ErrorAsync(mcpResult, ErrorCode.RevisionConflict, dispatched: true).ConfigureAwait(false);
        var document = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(
            new(partition, CollectionName, DocumentName), cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(document?.Revision).IsEqualTo(2L);
        await Assert.That(document?.Json).IsEqualTo(DocumentJson);
        return stale;
    }

    private static async Task VerifySameCommandReplayAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        CommandRequest command, CommitReceipt original, CancellationToken cancellationToken)
    {
        var first = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        var second = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(first.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(second.Value.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(JsonDefaults.Serialize(first).AsSpan().SequenceEqual(JsonDefaults.Serialize(original))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(second.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(original))).IsTrue();
        await Assert.That(first.Token).IsEqualTo(second.Value.Token);
        await Assert.That(JsonDefaults.Serialize(first).AsSpan().SequenceEqual(JsonDefaults.Serialize(second.Value))).IsTrue();
    }

    private static async Task VerifySameEventReplayAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        PartitionRef partition, CancellationToken cancellationToken)
    {
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new AppendSamples(SeriesSetName, SeriesName, [SampleAt(10)], Tags(10))]);
        var first = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        var second = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(first.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(second.Value.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(first.Token).IsEqualTo(second.Value.Token);
        await Assert.That(JsonDefaults.Serialize(first).AsSpan().SequenceEqual(JsonDefaults.Serialize(second.Value))).IsTrue();
    }

    internal static string Tags(int index) => "{\"ordinal\":"
        + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"source\":\"native5\"}";
}
