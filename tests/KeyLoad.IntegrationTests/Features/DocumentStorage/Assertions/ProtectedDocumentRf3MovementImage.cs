using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.DocumentStorage.ProtectedDocumentRf3MovementProtocol;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal static class ProtectedDocumentRf3MovementImage
{
    internal static async Task<string> CaptureAsync(KeyLoadClient source, KeyLoadClient target,
        ProtectedDocumentRf3MovementState movement, CancellationToken token)
    {
        var reference = new EntityRef(movement.Request.Partition, ProtectedDocumentRf3Seed.Collection, ProtectedDocumentRf3Seed.Entity);
        var expected = new DocumentResult(reference, P.DocumentRevision, ProtectedDocumentRf3Seed.Initial, false, []);
        var publication = movement.Receipt.PublishedPlacement ?? throw new InvalidOperationException(P.MissingReceipt);
        var minimum = new CommitToken(movement.TargetCut.Incarnation, reference.Partition.AtomicPartitionId,
            movement.TargetCut.Applied, publication.PlacementEpoch);
        var sourceDocument = await PublicReadAssertions.DocumentAsync(await source.GetAsync(reference, minimum, token));
        var targetDocument = await PublicReadAssertions.DocumentAsync(await target.GetAsync(reference, minimum, token));
        await SqlRf3Protocol.EqualAsync(expected, sourceDocument);
        await SqlRf3Protocol.EqualAsync(expected, targetDocument);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await source.OutboxStatusAsync(movement.Request.Partition, token));
        await Assert.That(outbox.Head.Tail).IsEqualTo(P.DocumentRevision);
        await Assert.That(outbox.Head.FirstAvailable).IsEqualTo(P.DocumentRevision);
        await Assert.That(outbox.Head.StoredRecords).IsEqualTo(P.DocumentRevision);
        await Assert.That(outbox.Head.StoredBytes).IsGreaterThan(P.ZeroPosition);
        await Assert.That(outbox.Consumers).IsEmpty();
        return JsonSerializer.Serialize(new
        {
            SourceDocument = sourceDocument,
            TargetDocument = targetDocument,
            Outbox = outbox
        }, JsonDefaults.Options);
    }
}
