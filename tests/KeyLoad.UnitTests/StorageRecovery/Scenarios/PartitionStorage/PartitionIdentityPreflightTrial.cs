
namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionIdentityPreflightTrial
{
    internal static async Task RunAsync(int capability, CancellationToken token)
    {
        await PartitionHostRecoveryFixture.RunAsync(async fixture =>
        {
            var original = await PartitionIdentityPreparation.SeedAsync(fixture).ConfigureAwait(false);
            var path = Path.Combine(fixture.Options.DataDirectory,
                PartitionHostRecoveryFixture.CanonicalDirectory);
            var identityPath = RuntimeJournalReaderFixture.IdentityPath(path);
            var identityBytes = await File.ReadAllBytesAsync(identityPath, token).ConfigureAwait(false);
            await PartitionIdentityPreparation.AppendTornTailAsync(fixture, original.Position, token)
                .ConfigureAwait(false);
            await RuntimeJournalReaderFixture.RewriteCapabilityAsync(path, capability).ConfigureAwait(false);
            var before = await PartitionIdentityInventory.CaptureAsync(fixture.Options.DataDirectory).ConfigureAwait(false);
            var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(fixture.OpenAndDisposeHostAsync);
            await Assert.That(failure!.Code).IsEqualTo(ErrorCode.FormatUnsupported);
            await PartitionIdentityInventory.RequireUnchangedAsync(fixture.Options.DataDirectory, before)
                .ConfigureAwait(false);
            using (var released = fixture.AcquireHostOwnership())
            {
                await Assert.That(released.CanWrite).IsTrue();
            }
            await File.WriteAllBytesAsync(identityPath, identityBytes, token).ConfigureAwait(false);
            await Assert.That(await File.ReadAllBytesAsync(identityPath, token).ConfigureAwait(false))
                .IsEquivalentTo(identityBytes);
            await PartitionIdentityContinuation.RequireAsync(fixture, original).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
}
