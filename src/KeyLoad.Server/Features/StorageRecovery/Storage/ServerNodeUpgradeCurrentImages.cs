using System.Security.Cryptography;
using KeyLoad.Replication;
using KeyLoad.Storage;

namespace KeyLoad.Server;

internal static class ServerNodeUpgradeCurrentImages
{
    private const int MaximumImages = 1_024;
    private const long MaximumBytes = 68_719_476_736;

    internal static void Verify(string directory, IAtomicStore canonical, ReplicaHardState state, long maximumImageBytes,
        string privateCopies)
    {
        var snapshots = Path.Combine(directory, ServerNodeUpgradeProtocol.Snapshots);
        if (!Directory.Exists(snapshots))
        {
            if (state.Snapshot is not null)
            { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
            return;
        }
        ServerNodeUpgradeFiles.CreatePrivateDirectory(privateCopies);
        var inventory = ServerNodeUpgradeInventory.Capture(snapshots);
        var count = 0;
        long bytes = 0;
        var foundPublished = state.Snapshot is null;
        foreach (var image in inventory.Entries)
        {
            ValidateImageEntry(image);
            count++;
            if (count > MaximumImages || image.Length > maximumImageBytes || image.Length > MaximumBytes - bytes)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, ServerNodeUpgradeProtocol.Limit); }
            bytes += image.Length;
            var copy = CopyVerified(snapshots, privateCopies, image);
            var cut = canonical.VerifySnapshot(copy);
            if (cut.Incarnation != canonical.Identity.Incarnation)
            { throw Errors.Fail(ErrorCode.TokenInvalidated, ServerNodeUpgradeProtocol.Corrupt); }
            if (state.Snapshot is { } pointer && pointer.FileName == image.Path)
            {
                VerifyPointer(pointer, image, cut);
                foundPublished = true;
            }
        }
        if (!foundPublished)
        { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
    }

    private static string CopyVerified(string original, string destination, ServerNodeUpgradeEntry image)
    {
        var copy = Path.Combine(destination, image.Path);
        ServerNodeUpgradeFiles.Copy(Path.Combine(original, image.Path), copy);
        using var input = ServerNodeUpgradeFiles.OpenRead(copy);
        if (input.Length != image.Length
            || Convert.ToHexStringLower(SHA256.HashData(input)) != image.Sha256 || input.Position != image.Length)
        { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
        return copy;
    }

    private static void ValidateImageEntry(ServerNodeUpgradeEntry image)
    {
        if (image.Directory || image.Path.Contains('/', StringComparison.Ordinal)
            || !image.Path.EndsWith(".snapshot", StringComparison.Ordinal)
            || !Guid.TryParseExact(image.Path[..^".snapshot".Length], "N", out _))
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
    }

    private static void VerifyPointer(ReplicaSnapshot pointer, ServerNodeUpgradeEntry image, StorageSnapshot cut)
    {
        if (pointer.Length != image.Length || !string.Equals(pointer.Sha256, image.Sha256, StringComparison.OrdinalIgnoreCase)
            || cut.AppliedPosition != pointer.Index || cut.Incarnation != pointer.Incarnation
            || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(pointer.Sha256), Convert.FromHexString(image.Sha256)))
        { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
    }
}
