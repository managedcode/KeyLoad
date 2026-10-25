using KeyLoad.Core.ResourceExecution;
using KeyLoad.UnitTests.Messaging;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.DocumentStorage;

internal static class DocumentSessionAuthorityColdAssertions
{
    internal const string Root = "root";
    internal const string Collection = "canonical-session-documents";
    internal const string Id = "session-document";
    internal const string Json = "{\"value\":\"canonical-session\"}";

    internal static EntityRef Reference(DocumentPatchAtomicityOwner fixture)
    {
        var partition = fixture.Source.Partition;
        return fixture.Store.Read(view =>
        {
            var resource = fixture.Database.Resource(view, partition, Collection);
            var lifetime = (ResourceLifetimeCatalog.Read(view, partition.TenantId, partition.DatabaseId,
                Collection, resource)).Value ?? throw new InvalidOperationException(Collection);
            return new EntityRef(partition, Collection, Id) { Lifetime = new(lifetime.Incarnation.Value) };
        });
    }

    internal static DocumentResult? Read(DocumentPatchAtomicityOwner fixture, CommitToken minimum,
        CancellationToken token = default)
        => fixture.Database.GetDocument(Root, Reference(fixture), minimum, cancellationToken: token);

    internal static async Task UnchangedAsync(DocumentPatchAtomicityOwner fixture, string[] image, long position)
    {
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
    }

    internal static async Task ReplayAsync(DocumentPatchAtomicityOwner fixture, Command<DatabaseCommand> operation,
        CommitReceipt receipt)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var cut = fixture.ReadCut();
        await DocumentPatchAtomicityAssertions.BytesAsync(OrleansSerialization.Serialize(receipt),
            OrleansSerialization.Serialize(fixture.Submit(operation).Get<CommitReceipt>()));
        await DocumentPatchAtomicityAssertions.BytesAsync(OrleansSerialization.Serialize(receipt),
            OrleansSerialization.Serialize(fixture.Database.ResolveOutcome(operation).Get<CommitReceipt>()));
        await UnchangedAsync(fixture, image, cut.StorePosition);
        await Assert.That(fixture.ReadCut()).IsEqualTo(cut);
    }

    internal static async Task LiteralAsync(DocumentPatchAtomicityOwner fixture, CommitToken token, long revision)
        => await DocumentPatchAtomicityAssertions.BytesAsync(
            OrleansSerialization.Serialize(new DocumentResult(Reference(fixture), revision, Json, false, [])),
            OrleansSerialization.Serialize(Read(fixture, token)));
}
