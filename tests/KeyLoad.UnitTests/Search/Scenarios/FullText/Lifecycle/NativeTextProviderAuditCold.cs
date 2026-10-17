using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using TUnit.Assertions.Enums;
using static KeyLoad.UnitTests.Features.Search.NativeTextProviderAuditProtocol;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextProviderAuditCold
{
    internal static async Task DamageAndRepairAsync(NativeTextProtectedRebuildColdOwner owner, NativeTextProviderAuditState state,
        NativeTextProviderAuditDamage damage, CancellationToken token)
    {
        await ReopenAsync(owner);
        var manifests = Directory.EnumerateDirectories(owner.ProjectionRoot)
            .Select(path => Path.Combine(path, NativeTextProtocol.ManifestFile)).Where(File.Exists);
        var path = manifests.Single(candidate => NativeTextEnvelopeCodec.Decode<NativeTextManifest>(
            File.ReadAllBytes(candidate)).Scope.Position == owner.Store.Position);
        var original = await File.ReadAllBytesAsync(path, token);
        await NativeTextProviderAuditImages.ManifestAsync(owner, path, original, token);
        var changed = NativeTextProviderAuditManifestDamage.Encode(original, damage);
        var canonical = NativeTextProviderAuditImages.Capture(owner);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await File.WriteAllBytesAsync(path, changed, token);
            var native = NativeTextProviderAuditImages.NativeFiles(owner.ProjectionRoot);
            await RefuseAsync(owner, canonical, native);
            await ReopenAsync(owner);
            await RefuseAsync(owner, canonical, native);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await File.WriteAllBytesAsync(path, original, token);
            await Assert.That(await File.ReadAllBytesAsync(path, token)).IsEquivalentTo(original, CollectionOrdering.Matching);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await NativeTextProviderAuditResults.CurrentAsync(owner, state, token);
        await NativeTextProviderAuditImages.SameAsync(owner, canonical);
        await NativeTextProviderAuditHistoryAssertions.AllAsync(owner, state, token);
    }

    internal static async Task ReopenAsync(NativeTextProtectedRebuildColdOwner owner)
    {
        NativeTextProviderAuditImages.RequireOriginalRoot(owner);
        var image = NativeTextProviderAuditImages.Capture(owner);
        owner.Reopen(removeProjection: false);
        await NativeTextProviderAuditImages.SameAsync(owner, image);
    }

    private static async Task RefuseAsync(NativeTextProtectedRebuildColdOwner owner,
        (KeyLoad.Storage.StoreIdentity Identity, long Position, string[] Rows) canonical, string[] native)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            _ = owner.Search;
        }) ?? throw new InvalidOperationException(ExpectedCorruptionRefusal);
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo(NativeTextProtocol.ProjectionCorrupt);
        await Assert.That(NativeTextProviderAuditImages.NativeFiles(owner.ProjectionRoot))
            .IsEquivalentTo(native, CollectionOrdering.Matching);
        await NativeTextProviderAuditImages.SameAsync(owner, canonical);
    }
}
