using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeCatalogBackupMetadataFile
{
    internal const string FileName = "catalog-backup.native";
    private const string InvalidMetadata = "The native catalog archive metadata is invalid.";
    private const int NoFileAttributes = 0;
    private const int NoMetadataBytes = 0;

    internal static NativeCatalogBackupCapture ReadArchive(string backup,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        cancellationToken.ThrowIfCancellationRequested();
        var verified = ZoneTreeBackupVerification.Verify(backup, executionOptions);
        cancellationToken.ThrowIfCancellationRequested();
        var metadata = ReadVerified(backup, verified.Identity, verified.Position, executionOptions.Value, out var completeDigest);
        cancellationToken.ThrowIfCancellationRequested();
        return new(verified.Position, metadata.Metadata, completeDigest);
    }

    internal static string Write(ZoneTreeStoreRuntime runtime, string directory, long position,
        ReadOnlyMemory<byte> metadata)
    {
        var manifestDigest = ManifestDigest(directory, runtime.Options.MaximumBackupManifestBytes,
            runtime.Options.StreamBufferBytes);
        var envelope = new ZoneTreeCatalogBackupMetadata(ZoneTreeCatalogBackupMetadata.CurrentVersion,
            position, runtime.Identity.NodeId, runtime.Identity.Incarnation, manifestDigest, metadata);
        var bytes = NativeSerialization.Serialize(envelope);
        if (bytes.Length > runtime.Options.MaximumBackupManifestBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, ZoneTreeCatalogBackupPublication.MetadataExceeded); }
        using var file = new FileStream(Path.Combine(directory, FileName), FileMode.CreateNew,
            FileAccess.Write, FileShare.None, runtime.Options.StreamBufferBytes);
        file.Write(bytes);
        file.Flush(true);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    internal static ZoneTreeCatalogBackupMetadata ReadVerified(string directory, StoreIdentity verifiedIdentity,
        long verifiedPosition, ZoneTreeStorageExecutionOptions policy)
        => ReadVerified(directory, verifiedIdentity, verifiedPosition, policy, out _);

    private static ZoneTreeCatalogBackupMetadata ReadVerified(string directory, StoreIdentity verifiedIdentity,
        long verifiedPosition, ZoneTreeStorageExecutionOptions policy, out string completeDigest)
    {
        var path = Path.Combine(directory, FileName);
        RequireRegular(path);
        var bytes = ZoneTreeMetadataFile.Read(path, policy.MaximumBackupManifestBytes,
            policy.StreamBufferBytes, InvalidMetadata);
        var metadata = NativeSerialization.Deserialize<ZoneTreeCatalogBackupMetadata>(bytes.Span);
        if (metadata is null || metadata.Version != ZoneTreeCatalogBackupMetadata.CurrentVersion
            || metadata.NodeId != verifiedIdentity.NodeId || metadata.Incarnation != verifiedIdentity.Incarnation
            || metadata.Position != verifiedPosition || metadata.Metadata.Length == NoMetadataBytes
            || !string.Equals(metadata.ManifestDigest,
                ManifestDigest(directory, policy.MaximumBackupManifestBytes, policy.StreamBufferBytes),
                StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidMetadata); }
        completeDigest = Convert.ToHexStringLower(SHA256.HashData(bytes.Span));
        return metadata;
    }

    private static string ManifestDigest(string directory, int maximumBytes, int streamBufferBytes)
    {
        var path = Path.Combine(directory, BackupManifestFileName);
        RequireRegular(path);
        var bytes = ZoneTreeMetadataFile.Read(path, maximumBytes, streamBufferBytes, InvalidMetadata);
        return Convert.ToHexStringLower(SHA256.HashData(bytes.Span));
    }

    private static void RequireRegular(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != NoFileAttributes)
            { throw Errors.Fail(ErrorCode.Corruption, InvalidMetadata); }
        }
        catch (FileNotFoundException) { throw Errors.Fail(ErrorCode.Corruption, InvalidMetadata); }
        catch (DirectoryNotFoundException) { throw Errors.Fail(ErrorCode.Corruption, InvalidMetadata); }
    }
}
