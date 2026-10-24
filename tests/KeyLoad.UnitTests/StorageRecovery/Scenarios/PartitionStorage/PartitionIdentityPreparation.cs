using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.StorageRecovery;

internal static class PartitionIdentityPreparation
{
    internal static async Task<PartitionIdentityCut> SeedAsync(PartitionHostRecoveryFixture fixture)
    {
        await fixture.RunHostAsync(host =>
        {
            host.Database.Store.Commit((transaction, _) =>
            {
                transaction.Put(PartitionIdentityProtocol.OriginalKey, PartitionIdentityProtocol.OriginalValue);
                return true;
            });
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        PartitionIdentityCut cut = null!;
        await fixture.RunStoresAsync(stores =>
        {
            var journalLength = new FileInfo(Path.Combine(fixture.Options.DataDirectory,
                PartitionHostRecoveryFixture.CanonicalDirectory, PartitionIdentityProtocol.JournalFile)).Length;
            cut = new(stores.Canonical.Identity, stores.Canonical.Position, journalLength);
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        return cut;
    }

    internal static async Task AppendTornTailAsync(PartitionHostRecoveryFixture fixture, long originalPosition,
        CancellationToken token)
    {
        using var serializer = new WalSerializerFixture();
        var payload = serializer.Serialize([new ZoneTreeJournalMutation
        {
            Key = PartitionIdentityProtocol.TornKey,
            Value = new ReadOnlyMemory<byte>(PartitionIdentityProtocol.TornValue),
            Kind = ZoneTreeJournalMutation.PutKind
        }]);
        var frame = WalFileFixture.CreateFrame(payload, originalPosition + PartitionIdentityProtocol.NextFrame);
        var path = Path.Combine(fixture.Options.DataDirectory,
            PartitionHostRecoveryFixture.CanonicalDirectory, PartitionIdentityProtocol.JournalFile);
        await using var journal = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None);
        await journal.WriteAsync(frame.AsMemory(0, frame.Length - PartitionIdentityProtocol.MissingFinalByte), token)
            .ConfigureAwait(false);
    }
}
