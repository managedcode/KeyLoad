using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ArchiveCaptureFlow
{
    internal static async Task<D2ArchiveCapture> CaptureAsync(D2ArchiveSource source,
        string root, Guid captureId, CancellationToken token)
    {
        await D2ArchiveAssertions.SourceAsync(source, token);
        var before = ControlledPartitionMovementExpiryOwnerState.Capture(source.Owner);
        var directory = Path.Combine(root, "archive-" + source.Ordinal);
        var work = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(), source.Owner.Database.EvaluationClock, token);
        var capture = source.Owner.Store.CreateCatalogBackup(directory, (view, position, identity) =>
            NativeSerialization.Serialize(source.Owner.Database.CaptureClusterBackupOwner(view, D2ArchiveData.Root,
                captureId, position, identity, work)), token);
        var verified = ZoneTreeStore.ReadVerifiedCatalogBackup(directory, UnitExecutionOptions.StorageExecution(), token);
        await Assert.That(verified.Position).IsEqualTo(capture.Position);
        await Assert.That(verified.ManifestDigest).IsEqualTo(capture.ManifestDigest);
        await Assert.That(verified.Metadata.Span.SequenceEqual(capture.Metadata.Span)).IsTrue();
        var cut = ZoneTreeStore.ReadVerifiedCatalogBackup(directory, UnitExecutionOptions.StorageExecution(),
            (view, identity, position, metadata) => ClusterBackupNativeCutVerification.Require(view, identity,
                position, metadata, D2ArchiveData.Credential, UnitExecutionOptions.DatabaseLimits(),
                source.Owner.Database.EvaluationClock, work), token);
        await Assert.That(cut.CaptureId).IsEqualTo(captureId);
        await Assert.That(cut.StorePosition).IsEqualTo(capture.Position);
        await D2ResourceInventoryAssertions.EqualAsync(source.Registered.Owner, cut.Owner);
        await before.AssertUnchangedAsync(source.Owner);
        var files = Files(directory, token);
        await Assert.That(files.Length).IsEqualTo(D2ArchiveData.ArchiveFiles.Length);
        return new(source, directory, capture, cut, files, before);
    }

    internal static string[] Files(string directory, CancellationToken token)
    {
        var result = new List<string>();
        var actual = Directory.EnumerateFiles(directory).OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
        if (!actual.Select(Path.GetFileName).SequenceEqual(D2ArchiveData.ArchiveFiles, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The native catalog archive must retain exactly its four selected files.");
        }
        foreach (var path in actual)
        {
            token.ThrowIfCancellationRequested();
            using var input = File.OpenRead(path);
            result.Add(Path.GetFileName(path) + ":" + input.Length + ":" + Convert.ToHexStringLower(SHA256.HashData(input)));
        }
        return result.ToArray();
    }

    internal static async Task UnchangedAsync(D2ArchiveCapture archive, CancellationToken token)
    {
        await Assert.That(Files(archive.Directory, token).SequenceEqual(archive.Files, StringComparer.Ordinal)).IsTrue();
        await archive.SourceCut.AssertUnchangedAsync(archive.Source.Owner);
        await D2ArchiveAssertions.SourceAsync(archive.Source, token);
    }
}
