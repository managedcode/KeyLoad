using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionPairedIdentityPreparation
{
    internal static async Task<PartitionPairedIdentityCut> SeedAsync(PartitionHostRecoveryFixture fixture)
    {
        await using (var host = fixture.OpenHost())
        {
            host.Database.Store.Commit((transaction, _) =>
            {
                transaction.Put(PartitionPairedIdentityProtocol.OriginalKey, PartitionPairedIdentityProtocol.OriginalValue);
                return true;
            });
        }
        var runtime = ServerRuntimeTestOptions.Runtime(fixture.Options);
        using var stores = new PartitionStores(runtime.Node, fixture.Options.DataDirectory,
            runtime.StorageExecution, runtime.PointCache);
        var journalLength = new FileInfo(Path.Combine(fixture.Options.DataDirectory,
            PartitionHostRecoveryFixture.CanonicalDirectory, PartitionPairedIdentityProtocol.JournalFile)).Length;
        return new(stores.Canonical.Identity, stores.Replica.Identity,
            stores.Canonical.Position, stores.Replica.Position, journalLength);
    }

    internal static async Task AppendTornTailAsync(PartitionHostRecoveryFixture fixture, long originalPosition,
        CancellationToken token)
    {
        using var serializer = new WalSerializerFixture();
        var payload = serializer.Serialize([new ZoneTreeJournalMutation
        {
            Key = PartitionPairedIdentityProtocol.TornKey,
            Value = new ReadOnlyMemory<byte>(PartitionPairedIdentityProtocol.TornValue),
            Kind = ZoneTreeJournalMutation.PutKind
        }]);
        var frame = WalFileFixture.CreateFrame(payload, originalPosition + PartitionPairedIdentityProtocol.NextFrame);
        var path = Path.Combine(fixture.Options.DataDirectory,
            PartitionHostRecoveryFixture.CanonicalDirectory, PartitionPairedIdentityProtocol.JournalFile);
        await using var journal = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None);
        await journal.WriteAsync(frame.AsMemory(0, frame.Length - PartitionPairedIdentityProtocol.MissingFinalByte), token)
            .ConfigureAwait(false);
    }
}
