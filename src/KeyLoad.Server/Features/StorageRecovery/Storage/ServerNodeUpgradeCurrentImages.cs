using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using KeyLoad.Replication;
using KeyLoad.Storage;

namespace KeyLoad.Server;

internal static class ServerNodeUpgradeCurrentImages
{
    internal static void Verify(string directory, IAtomicStore canonical, ReplicaHardState state,
        IOptions<ReplicaConfiguration> replicaOptions, string privateCopies,
        IOptions<ServerNodeUpgradeExecutionOptions> executionOptions, IOptions<OfflineRecoveryExecutionOptions> recoveryOptions)
    {
        const int CountInitialValue = 0;
        const int BytesInitialValue = 0;

        replicaOptions.Value.Validate();
        recoveryOptions.Value.Validate();
        var maximumImageBytes = replicaOptions.Value.MaxSnapshotBytes;
        var recovery = recoveryOptions.Value;
        var snapshots = Path.Combine(directory, ServerNodeUpgradeProtocol.Snapshots);
        if (!Directory.Exists(snapshots))
        {
            if (state.Snapshot is not null)
            { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
            return;
        }
        ServerNodeUpgradeFiles.CreatePrivateDirectory(privateCopies);
        var inventory = ServerNodeUpgradeInventory.Capture(snapshots, executionOptions: executionOptions);
        var count = CountInitialValue;
        long bytes = BytesInitialValue;
        var foundPublished = state.Snapshot is null;
        foreach (var image in inventory.Entries)
        {
            ValidateImageEntry(image);
            count++;
            if (count > recovery.MaximumSnapshotImages || image.Length > maximumImageBytes
                || image.Length > recovery.MaximumSnapshotInventoryBytes - bytes)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, ServerNodeUpgradeProtocol.Limit); }
            bytes += image.Length;
            var copy = CopyVerified(snapshots, privateCopies, image, executionOptions: executionOptions);
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

    private static string CopyVerified(string original, string destination, ServerNodeUpgradeEntry image, IOptions<ServerNodeUpgradeExecutionOptions> executionOptions)
    {
        var copy = Path.Combine(destination, image.Path);
        ServerNodeUpgradeFiles.Copy(Path.Combine(original, image.Path), copy, executionOptions: executionOptions);
        using var input = ServerNodeUpgradeFiles.OpenRead(copy, executionOptions: executionOptions);
        if (input.Length != image.Length
            || Convert.ToHexStringLower(SHA256.HashData(input)) != image.Sha256 || input.Position != image.Length)
        { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
        return copy;
    }

    private static void ValidateImageEntry(ServerNodeUpgradeEntry image)
    {
        const char SlashCharacter = '/';
        const string SnapshotFileExtension = ".snapshot";
        const string CompactIdentityFormat = "N";

        if (image.Directory || image.Path.Contains(SlashCharacter, StringComparison.Ordinal)
            || !image.Path.EndsWith(ServerNodeUpgradeProtocol.SnapshotSuffix, StringComparison.Ordinal)
            || !Guid.TryParseExact(image.Path[..^SnapshotFileExtension.Length], CompactIdentityFormat, out _))
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
