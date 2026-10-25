using KeyLoad.Server;
using KeyLoad.Server.Search;
using TUnit.Assertions.Enums;
using static KeyLoad.FunctionalTests.Search.TextProviderAuditProtocol;

namespace KeyLoad.FunctionalTests.Search;

internal static class TextProviderAuditCold
{
    internal static async Task DamageAndRepairAsync(TextProtectedRebuildColdOwner owner, TextProviderAuditState state,
        TextProviderAuditDamage damage, CancellationToken token)
    {
        await ReopenAsync(owner);
        var manifests = Directory.EnumerateDirectories(owner.ProjectionRoot)
            .Select(path => Path.Combine(path, TextProtocol.ManifestFile)).Where(File.Exists);
        var path = manifests.Single(candidate => TextEnvelopeCodec.Decode<TextManifest>(
            File.ReadAllBytes(candidate)).Scope.Position == owner.Store.Position);
        var original = await File.ReadAllBytesAsync(path, token);
        await TextProviderAuditImages.ManifestAsync(owner, path, original, token);
        var changed = TextProviderAuditManifestDamage.Encode(original, damage);
        var canonical = TextProviderAuditImages.Capture(owner);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await File.WriteAllBytesAsync(path, changed, token);
            var execution = TextProviderAuditImages.RuntimeFiles(owner.ProjectionRoot);
            await RefuseAsync(owner, canonical, execution);
            await ReopenAsync(owner);
            await RefuseAsync(owner, canonical, execution);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await File.WriteAllBytesAsync(path, original, token);
            await Assert.That(await File.ReadAllBytesAsync(path, token)).IsEquivalentTo(original, CollectionOrdering.Matching);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await TextProviderAuditResults.CurrentAsync(owner, state, token);
        await TextProviderAuditImages.SameAsync(owner, canonical);
        await TextProviderAuditHistoryAssertions.AllAsync(owner, state, token);
    }

    internal static async Task ReopenAsync(TextProtectedRebuildColdOwner owner)
    {
        TextProviderAuditImages.RequireOriginalRoot(owner);
        var image = TextProviderAuditImages.Capture(owner);
        owner.Reopen(removeProjection: false);
        await TextProviderAuditImages.SameAsync(owner, image);
    }

    private static async Task RefuseAsync(TextProtectedRebuildColdOwner owner,
        (KeyLoad.Storage.StoreIdentity Identity, long Position, string[] Rows) canonical, string[] execution)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            _ = owner.Search;
        }) ?? throw new InvalidOperationException(ExpectedCorruptionRefusal);
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo(TextProtocol.ProjectionCorrupt);
        await Assert.That(TextProviderAuditImages.RuntimeFiles(owner.ProjectionRoot))
            .IsEquivalentTo(execution, CollectionOrdering.Matching);
        await TextProviderAuditImages.SameAsync(owner, canonical);
    }
}
