using System.Text.Json;
using Aspire.Hosting;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Adds and proves genuine public writes sufficient for the prior wave's native snapshot threshold.</summary>
internal static class RequestCqrsRf3CheckpointSeed
{
    private const string Collection = "cluster-routing-c1-checkpoint";
    private const string DocumentPrefix = "checkpoint-";
    private const string MutationKind = "putDocument";
    private const string PayloadCohort = "native6-checkpoint";

    internal static async Task SeedAndVerifyAsync(DistributedApplication app,
        RequestCqrsRf3Workload workload, NodeEpochRf3Profile profile, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(app, RequestCqrsRf3Protocol.Node1);
        var sdk = new KeyLoadClient(http, profile.AdminKey, IntegrationClientOptions.Execution());
        await ConfigureCollectionAsync(sdk, workload.Partition, cancellationToken).ConfigureAwait(false);
        var receipts = await CommitRecordsAsync(sdk, workload.Partition, profile.Incarnation, cancellationToken)
            .ConfigureAwait(false);
        var last = receipts[^1];
        await NodeEpochRf3StatusOracle.EventuallyCaughtUpAsync(app, profile, last.Token.Position, cancellationToken)
            .ConfigureAwait(false);
        var observations = await NodeEpochRf3StatusOracle.CaptureAsync(app, profile, cancellationToken)
            .ConfigureAwait(false);
        foreach (var observation in observations)
        {
            await Assert.That(observation.Status.Applied).IsGreaterThanOrEqualTo(last.Token.Position);
        }
    }

    private static async Task ConfigureCollectionAsync(KeyLoadClient sdk, PartitionRef partition,
        CancellationToken cancellationToken)
    {
        var definition = new ResourceDefinition(Collection, ResourceKind.Collection, partition.TransactionDomainId);
        var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
        await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureResourceAsync(Guid.NewGuid(), request,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
    }

    private static async Task<CommitReceipt[]> CommitRecordsAsync(KeyLoadClient sdk, PartitionRef partition,
        Guid incarnation, CancellationToken cancellationToken)
    {
        var receipts = new List<CommitReceipt>(NodeEpochRf3Protocol.MinimumCommands);
        var commandIds = new HashSet<Guid>();
        var lastPosition = 0L;
        for (var ordinal = 0; ordinal < NodeEpochRf3Protocol.MinimumCommands; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = DocumentId(ordinal);
            var commandId = Guid.NewGuid();
            await Assert.That(commandId).IsNotEqualTo(Guid.Empty);
            await Assert.That(commandIds.Add(commandId)).IsTrue();
            var request = new CommandRequest(commandId, partition,
                [new PutDocument(Collection, id, Json(ordinal), ExpectedRevision: 0)]);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(request, cancellationToken)
                .ConfigureAwait(false)).ConfigureAwait(false);
            await VerifyReceiptAsync(receipt, commandId, id, incarnation, lastPosition).ConfigureAwait(false);
            await VerifyRecordAsync(sdk, partition, id, ordinal, cancellationToken).ConfigureAwait(false);
            lastPosition = receipt.Token.Position;
            receipts.Add(receipt);
        }
        await Assert.That(receipts.Count).IsEqualTo(NodeEpochRf3Protocol.MinimumCommands);
        return [.. receipts];
    }

    private static async Task VerifyReceiptAsync(CommitReceipt receipt, Guid commandId, string documentId,
        Guid incarnation, long previousPosition)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(incarnation);
        await Assert.That(receipt.Token.Position).IsGreaterThan(previousPosition);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(1);
        var mutation = receipt.Mutations[0];
        await Assert.That(mutation.Kind).IsEqualTo(MutationKind);
        await Assert.That(mutation.Resource).IsEqualTo(Collection);
        await Assert.That(mutation.Id).IsEqualTo(documentId);
        await Assert.That(mutation.Revision).IsEqualTo(1L);
    }

    private static async Task VerifyRecordAsync(KeyLoadClient sdk, PartitionRef partition,
        string documentId, int ordinal, CancellationToken cancellationToken)
    {
        var reference = new EntityRef(partition, Collection, documentId);
        var document = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(reference, cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(document).IsNotNull();
        await Assert.That(document!.Reference).IsEqualTo(reference);
        await Assert.That(document.Revision).IsEqualTo(1L);
        await Assert.That(document.Json).IsEqualTo(Json(ordinal));
        await Assert.That(document.Redacted).IsFalse();
    }

    private static string DocumentId(int ordinal)
        => DocumentPrefix + ordinal.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);

    private static string Json(int ordinal)
        => JsonSerializer.Serialize(new { cohort = PayloadCohort, ordinal });
}
