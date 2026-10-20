using KeyLoad.Core.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed record DocumentPatchAtomicityState(long Position, long Applied, byte[] Rows, byte[] Identity, byte[][] Domain)
{
    internal static DocumentPatchAtomicityState Read(DocumentPatchAtomicityOwner owner)
    {
        var partition = owner.Source.Partition;
        var work = owner.Source.CreateReadWork(TestContext.Current!.Execution.CancellationToken);
        var image = owner.Store.Read(view =>
        {
            var rows = Scan(view, [], work, owner.Database.Limits.MaxScanRecords);
            byte[][] domain =
            [
                Scan(view, DocumentStorageKeys.Prefix(partition, DocumentPatchAtomicityContract.Collection), work, owner.Database.Limits.MaxScanRecords),
                Scan(view, KeySpace.Partition("index", partition, DocumentPatchAtomicityContract.Collection), work, owner.Database.Limits.MaxScanRecords),
                Scan(view, KeySpace.Partition("unique", partition, DocumentPatchAtomicityContract.Collection), work, owner.Database.Limits.MaxScanRecords),
                Scan(view, KeySpace.Partition("outbox", partition), work, owner.Database.Limits.MaxScanRecords),
                Scan(view, KeySpace.Partition("outbox-head", partition), work, owner.Database.Limits.MaxScanRecords)
            ];
            return (Rows: rows, Domain: domain);
        });
        return new(owner.Store.Position, owner.Database.LastApplied, image.Rows,
            OrleansSerialization.Serialize(owner.Store.Identity), image.Domain);
    }

    private static byte[] Scan(KeyLoad.Storage.IKeyValueView view, byte[] prefix,
        ReadExecutionBudget work, int limit)
    {
        var page = work.Scan(view, prefix, limit);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The genuine document fixture exceeded its bounded complete inventory.");
        }
        return OrleansSerialization.Serialize(page);
    }

    internal async Task RequireDomainAsync(DocumentPatchAtomicityState actual)
    {
        await Assert.That(actual.Domain.Length).IsEqualTo(Domain.Length);
        for (var index = 0; index < Domain.Length; index++)
        {
            await DocumentPatchAtomicityAssertions.BytesAsync(Domain[index], actual.Domain[index]);
        }
    }

    internal async Task RequireAsync(DocumentPatchAtomicityState actual)
    {
        await Assert.That(actual.Position).IsEqualTo(Position);
        await Assert.That(actual.Applied).IsEqualTo(Applied);
        await DocumentPatchAtomicityAssertions.BytesAsync(Rows, actual.Rows);
        await DocumentPatchAtomicityAssertions.BytesAsync(Identity, actual.Identity);
        await RequireDomainAsync(actual);
    }
}
