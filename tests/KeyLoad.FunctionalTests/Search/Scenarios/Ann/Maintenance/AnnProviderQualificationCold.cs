using System.Security.Cryptography;
using KeyLoad.Query.Search;
using Microsoft.Extensions.Options;
using static KeyLoad.FunctionalTests.Search.AnnProviderQualificationProtocol;

namespace KeyLoad.FunctionalTests.Search;

internal static class AnnProviderQualificationCold
{
    internal static async Task DamageAndRepairAsync(AnnProviderQualificationOwner owner, AnnProviderQualificationState state,
        AnnProviderQualificationDamage damage, PackedAnnIndex healthy, CancellationToken token)
    {
        var image = AnnProviderQualificationImages.Capture(owner);
        state.DamagedFile = AnnProviderQualificationDamageCodec.Create(owner, state, damage, token);
        state.DamagedDigest = SHA256.HashData(state.DamagedFile);
        await Assert.That(state.DamagedFile.AsSpan().SequenceEqual(state.OriginalFile)).IsFalse();
        await AnnProviderQualificationPersistence.WriteExactAsync(owner, state.DamagedFile, token);
        await RefuseAsync(owner, state, damage, token);
        await AnnProviderQualificationResults.CurrentAsync(owner, state, healthy, token);
        await AnnProviderQualificationHistoryAssertions.AllAsync(owner, state, token);
        await AnnProviderQualificationImages.SameAsync(owner, image);
        owner.Reopen();
        await AnnProviderQualificationImages.SameAsync(owner, image);
        await RefuseAsync(owner, state, damage, token);
        await AnnProviderQualificationResults.CurrentAsync(owner, state, healthy, token);
        await AnnProviderQualificationHistoryAssertions.AllAsync(owner, state, token);
        await AnnProviderQualificationPersistence.WriteExactAsync(owner, state.OriginalFile, token);
        var repaired = AnnProviderQualificationPersistence.Load(owner, state, token);
        await AnnProviderQualificationResults.CurrentAsync(owner, state, repaired, token);
        await AnnProviderQualificationHistoryAssertions.AllAsync(owner, state, token);
        await AnnProviderQualificationImages.SameAsync(owner, image);
        await FileAsync(owner.SnapshotPath, state.OriginalFile, state.OriginalDigest, token);
    }

    internal static async Task ReopenAsync(AnnProviderQualificationOwner owner, AnnProviderQualificationState state,
        CancellationToken token)
    {
        var image = AnnProviderQualificationImages.Capture(owner);
        owner.Reopen();
        await AnnProviderQualificationImages.SameAsync(owner, image);
        await FileAsync(owner.SnapshotPath, state.OriginalFile, state.OriginalDigest, token);
        if (state.Healthy)
        {
            await FileAsync(AnnProviderQualificationPersistence.Path(owner, state), state.HealthyFile, state.HealthyDigest, token);
        }
        await AnnProviderQualificationResults.CurrentAsync(owner, state,
            AnnProviderQualificationPersistence.Load(owner, state, token), token);
        await AnnProviderQualificationHistoryAssertions.AllAsync(owner, state, token);
    }

    private static async Task RefuseAsync(AnnProviderQualificationOwner owner, AnnProviderQualificationState state,
        AnnProviderQualificationDamage damage, CancellationToken token)
    {
        var image = AnnProviderQualificationImages.Capture(owner);
        using var source = new FileStream(owner.SnapshotPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => PackedAnnIndex.Load(source, state.DamagedDigest,
            UnitExecutionOptions.PackedAnn(state.Policy), Options.Create(new PackedAnnStorageOptions()),
            AnnProviderQualificationCorpus.Budget(owner, token))) ?? throw new InvalidOperationException(ExpectedRefusal);
        await Assert.That(failure.Code).IsEqualTo(damage == AnnProviderQualificationDamage.HeaderVersion
            ? ErrorCode.FormatUnsupported : ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(PackedAnnStorageFrames.InvalidSnapshot);
        await AnnProviderQualificationImages.SameAsync(owner, image);
        await FileAsync(owner.SnapshotPath, state.DamagedFile, state.DamagedDigest, token);
    }

    private static async Task FileAsync(string path, byte[] expected, byte[] digest,
        CancellationToken token)
    {
        var actual = await File.ReadAllBytesAsync(path, token);
        await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue();
        await Assert.That(SHA256.HashData(actual).AsSpan().SequenceEqual(digest)).IsTrue();
    }

    internal static void RequireOriginalRoot(string root)
    {
        if (!Path.IsPathFullyQualified(root) || !string.Equals(Path.GetFullPath(root), root, StringComparison.Ordinal)
            || !Directory.Exists(root) || !Directory.EnumerateFileSystemEntries(root).Any()
            || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != NoFileAttributes)
        {
            throw new InvalidOperationException(WrongRoot);
        }
    }
}
