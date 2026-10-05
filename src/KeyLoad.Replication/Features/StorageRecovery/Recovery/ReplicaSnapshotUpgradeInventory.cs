using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.Replication;

internal static class ReplicaSnapshotUpgradeInventory
{
    private const int NoInventoriedEntries = 0;
    private const int NoInventoriedBytes = 0;
    private const int EmptyImageInventory = 0;
    private const int NoFileAttributeFlags = 0;
    private const int EmptyImageLength = 0;
    private const int BeforeFirstStoragePosition = 0;
    private const int BeforeFirstAppliedPosition = 0;
    private const int EmptyRecordCount = 0;

    private const string FormatError = "The replica snapshot inventory is unknown, linked or ambiguous.";
    private const string RecoveryError = "Pending replica snapshot transfer files must be settled by the matching executable.";

    internal static (string Path, List<ReplicaSnapshotUpgradeImage> Images) Read(string path,
        ReplicaConfiguration configuration, ReplicaSnapshot? pointer,
        Func<string, StorageSnapshot> verifySourceImage, int maximumImages, long maximumTotalBytes, int fileBufferBytes)
    {
        ArgumentNullException.ThrowIfNull(verifySourceImage);
        var fullPath = Path.GetFullPath(path);
        RejectLinks(fullPath);
        if (!Directory.Exists(fullPath))
        {
            if (pointer is null && !File.Exists(fullPath))
            { return (fullPath, []); }
            throw Errors.Fail(ErrorCode.FormatUnsupported, FormatError);
        }
        RejectFixedPendingFiles(fullPath);
        var images = new List<ReplicaSnapshotUpgradeImage>();
        var names = Directory.EnumerateFileSystemEntries(fullPath);
        var entries = NoInventoriedEntries;
        long totalBytes = NoInventoriedBytes;
        foreach (var entry in names)
        {
            var name = Path.GetFileName(entry);
            RejectLinks(entry);
            if (IsPending(name))
            { throw Errors.Fail(ErrorCode.RecoveryRequired, RecoveryError); }
            if (++entries > maximumImages)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, FormatError); }
            var image = ReadImage(entry, name, configuration, totalBytes, verifySourceImage, maximumTotalBytes, fileBufferBytes);
            totalBytes += image.Length;
            images.Add(image);
            if (images.Count > maximumImages)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, FormatError); }
        }
        images.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.FileName, right.FileName));
        if (pointer is null ? images.Count != EmptyImageInventory : images.All(image => image.FileName != pointer.FileName))
        { throw Errors.Fail(ErrorCode.FormatUnsupported, FormatError); }
        if (pointer is not null)
        { ValidatePointer(pointer, images, configuration); }
        return (fullPath, images);
    }

    private static ReplicaSnapshotUpgradeImage ReadImage(string path, string name,
        ReplicaConfiguration configuration, long totalBytes, Func<string, StorageSnapshot> verifySourceImage,
        long maximumTotalBytes, int fileBufferBytes)
    {
        if (Directory.Exists(path) || !TryImageName(name))
        { throw Errors.Fail(ErrorCode.FormatUnsupported, FormatError); }
        KeyLoad.Storage.IO.OfflineRegularFile.RequireRegular(path);
        var file = new FileInfo(path);
        if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != NoFileAttributeFlags)
        { throw Errors.Fail(ErrorCode.FormatUnsupported, FormatError); }
        if (file.Length <= EmptyImageLength || file.Length > configuration.MaxSnapshotBytes
            || file.Length > maximumTotalBytes - totalBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, FormatError); }
        var checksum = Digest(path, configuration.MaxSnapshotBytes, fileBufferBytes);
        var cut = verifySourceImage(path);
        ValidateCut(cut, configuration);
        return new(name, file.Length, checksum, cut);
    }

    internal static string Digest(string path, long maximumBytes, int fileBufferBytes)
    {
        using var stream = KeyLoad.Storage.IO.OfflineRegularFile.Open(path, FileAccess.Read, FileShare.Read,
            fileBufferBytes);
        var length = stream.Length;
        if (length <= EmptyImageLength || length > maximumBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, FormatError); }
        var digest = Convert.ToHexStringLower(SHA256.HashData(stream));
        if (stream.Position != length || stream.Length != length)
        { throw Errors.Fail(ErrorCode.Corruption, FormatError); }
        return digest;
    }

    internal static void RejectLinks(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            try
            {
                if (new FileInfo(current).LinkTarget is not null || new DirectoryInfo(current).LinkTarget is not null
                    || (File.GetAttributes(current) & FileAttributes.ReparsePoint) != NoFileAttributeFlags)
                { throw Errors.Fail(ErrorCode.FormatUnsupported, FormatError); }
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = Path.GetDirectoryName(current);
        }
    }

    private static bool IsPending(string name)
    {
        if (name == ReplicaProtocol.IncomingManifest || name == ReplicaProtocol.IncomingImage
            || name == ReplicaProtocol.IncomingManifest + ReplicaProtocol.TemporarySuffix
            || name == ReplicaProtocol.IncomingImage + ReplicaProtocol.TemporarySuffix)
        { return true; }
        return name.EndsWith(ReplicaProtocol.SnapshotExtension + ReplicaProtocol.TemporarySuffix, StringComparison.Ordinal)
            && TryImageName(name[..^ReplicaProtocol.TemporarySuffix.Length]);
    }

    private static void RejectFixedPendingFiles(string directory)
    {
        var names = new[]
        {
            ReplicaProtocol.IncomingManifest,
            ReplicaProtocol.IncomingImage,
            ReplicaProtocol.IncomingManifest + ReplicaProtocol.TemporarySuffix,
            ReplicaProtocol.IncomingImage + ReplicaProtocol.TemporarySuffix
        };
        foreach (var name in names)
        {
            var path = Path.Combine(directory, name);
            RejectLinks(path);
            if (File.Exists(path) || Directory.Exists(path))
            { throw Errors.Fail(ErrorCode.RecoveryRequired, RecoveryError); }
        }
    }

    private static bool TryImageName(string name)
    {
        if (!name.EndsWith(ReplicaProtocol.SnapshotExtension, StringComparison.Ordinal))
        { return false; }
        var stem = name[..^ReplicaProtocol.SnapshotExtension.Length];
        return Guid.TryParseExact(stem, ReplicaPersistence.GuidFormat, out var id)
            && stem == id.ToString(ReplicaPersistence.GuidFormat);
    }

    private static void ValidatePointer(ReplicaSnapshot pointer, List<ReplicaSnapshotUpgradeImage> images,
        ReplicaConfiguration configuration)
    {
        ReplicaPersistence.ValidateSnapshot(pointer, configuration);
        var image = images.SingleOrDefault(candidate => candidate.FileName == pointer.FileName);
        if (image is null || image.Length != pointer.Length
            || !string.Equals(image.Sha256, pointer.Sha256, StringComparison.OrdinalIgnoreCase)
            || image.Snapshot.Incarnation != pointer.Incarnation
            || image.Snapshot.AppliedPosition != pointer.Index)
        { throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidSnapshot); }
    }

    private static void ValidateCut(StorageSnapshot cut, ReplicaConfiguration configuration)
    {
        if (cut.Incarnation != configuration.Incarnation || cut.Position < BeforeFirstStoragePosition || cut.AppliedPosition < BeforeFirstAppliedPosition
            || cut.RecordCount < EmptyRecordCount)
        { throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidSnapshot); }
    }
}
