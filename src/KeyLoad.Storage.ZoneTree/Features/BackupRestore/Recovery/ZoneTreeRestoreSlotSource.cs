using System.Security.Cryptography;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeRestoreSlotSource
{
    private const int First = 0;
    private const int NoBytes = 0;
    private const string Invalid = "The original restore slot archive no longer matches its admitted source.";

    internal static ZoneTreeBackupRestoreManifestFile[] Describe(string backup,
        ZoneTreeStorageExecutionOptions policy, CancellationToken ct)
    {
        string[] names = [IdentityFileName, JournalFileName, BackupManifestFileName, ZoneTreeCatalogBackupMetadataFile.FileName];
        return names.Select(name => DescribeFile(backup, name, policy, ct)).ToArray();
    }

    internal static StoreIdentity Require(string backup, ClusterRestoreSlotContext context,
        ZoneTreeStorageExecutionOptions policy, CancellationToken ct, out ZoneTreeBackupRestoreManifest manifest)
    {
        ct.ThrowIfCancellationRequested();
        manifest = ZoneTreeBackupRestoreFiles.ReadManifestDefinition(backup, policy);
        var entry = manifest.Files.Single(file => file.Name == IdentityFileName);
        var bytes = ZoneTreeBackupRestoreFiles.ReadAndVerifyIdentityFile(backup, entry,
            policy.MaximumIdentityFileBytes, policy.StreamBufferBytes);
        var identity = ZoneTreeIdentityFile.Read(bytes.Span);
        if (identity.NodeId != context.SourceNodeId || identity.Incarnation != context.SourceIncarnation
            || manifest.Position != context.SourcePosition)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        ZoneTreeCatalogBackupMetadataFile.ReadVerified(backup, identity, manifest.Position, policy);
        var envelope = DescribeFile(backup, ZoneTreeCatalogBackupMetadataFile.FileName, policy, ct);
        if (!string.Equals(envelope.Checksum, context.SourceEnvelopeDigest, StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        return identity;
    }

    internal static void RequireSame(ZoneTreeBackupRestoreManifestFile[] original,
        ZoneTreeBackupRestoreManifestFile[] current)
    {
        if (!original.SequenceEqual(current))
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
    }

    private static ZoneTreeBackupRestoreManifestFile DescribeFile(string backup, string name,
        ZoneTreeStorageExecutionOptions policy, CancellationToken ct)
    {
        var path = Path.Combine(backup, name);
        if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != NoBytes)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, policy.StreamBufferBytes);
        // Journal bytes use the existing verified manifest length and bounded streaming; no new snapshot-sized journal cap.
        var bound = name == IdentityFileName ? policy.MaximumIdentityFileBytes : policy.MaximumBackupManifestBytes;
        if (file.Length <= NoBytes || name != JournalFileName && file.Length > bound)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, Invalid); }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[policy.FileBufferBytes];
        int length;
        while ((length = file.Read(buffer)) > NoBytes)
        { ct.ThrowIfCancellationRequested(); hash.AppendData(buffer, First, length); }
        ct.ThrowIfCancellationRequested();
        return new(name, file.Length, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }
}
