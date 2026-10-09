using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionPairedIdentityPreflightTrial
{
    internal static async Task RunAsync(bool canonical, int capability, CancellationToken token)
    {
        using var fixture = new PartitionHostRecoveryFixture();
        var original = await PartitionPairedIdentityPreparation.SeedAsync(fixture).ConfigureAwait(false);
        var path = Path.Combine(fixture.Options.DataDirectory,
            canonical ? PartitionHostRecoveryFixture.CanonicalDirectory : ReplicaProtocol.ReplicaDirectory);
        var identityPath = RuntimeJournalReaderFixture.IdentityPath(path);
        var identityBytes = await File.ReadAllBytesAsync(identityPath, token).ConfigureAwait(false);
        await PartitionPairedIdentityPreparation.AppendTornTailAsync(fixture, original.CanonicalPosition, token)
            .ConfigureAwait(false);
        await RuntimeJournalReaderFixture.RewriteCapabilityAsync(path, capability).ConfigureAwait(false);
        var before = await PartitionPairedIdentityInventory.CaptureAsync(fixture.Options.DataDirectory).ConfigureAwait(false);
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(fixture.OpenAndDisposeHostAsync);
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await PartitionPairedIdentityInventory.RequireUnchangedAsync(fixture.Options.DataDirectory, before)
            .ConfigureAwait(false);
        using (var released = fixture.AcquireHostOwnership())
        {
            await Assert.That(released.CanWrite).IsTrue();
        }
        await File.WriteAllBytesAsync(identityPath, identityBytes, token).ConfigureAwait(false);
        await Assert.That(await File.ReadAllBytesAsync(identityPath, token).ConfigureAwait(false))
            .IsEquivalentTo(identityBytes);
        await PartitionPairedIdentityContinuation.RequireAsync(fixture, original).ConfigureAwait(false);
    }
}
