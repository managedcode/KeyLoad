using System.Runtime.ExceptionServices;
using System.Security.Cryptography;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeSnapshotUpgradeRunner
{
    private const int BufferBytes = ZoneTreePersistenceFormat.FileBufferBytes;

    internal static StorageSnapshot VerifySource(string sourcePath, Guid incarnation,
        ZoneTreeSnapshotUpgradeOptions? options)
    {
        var settings = ZoneTreeSnapshotUpgradeSafety.ValidateOptions(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ZoneTreeSnapshotUpgradeSafety.ValidateIncarnation(incarnation);
        var path = ZoneTreeSnapshotUpgradeSafety.Normalize(sourcePath);
        ZoneTreeSnapshotUpgradeSafety.VerifySourcePath(path);
        return ZoneTreeSnapshotUpgradeIO.WithSource(path, settings.MaxSnapshotBytes, input =>
        {
            var rawDigest = ZoneTreeSnapshotUpgradeIO.DigestFile(input, settings.MaxSnapshotBytes);
            var semantic = ReadSource(input, settings, incarnation);
            if (!ZoneTreeSnapshotUpgradeDigest.Equal(rawDigest,
                    ZoneTreeSnapshotUpgradeIO.DigestFile(input, settings.MaxSnapshotBytes)))
            { throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.BackupFileVerificationFailed); }

            return semantic.Snapshot;
        });
    }

    internal static StorageSnapshot Upgrade(string sourcePath, string destinationPath, Guid incarnation,
        ZoneTreeSnapshotUpgradeOptions? options)
    {
        var settings = ZoneTreeSnapshotUpgradeSafety.ValidateOptions(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ZoneTreeSnapshotUpgradeSafety.ValidateIncarnation(incarnation);
        var source = ZoneTreeSnapshotUpgradeSafety.Normalize(sourcePath);
        var destination = ZoneTreeSnapshotUpgradeSafety.Normalize(destinationPath);
        ZoneTreeSnapshotUpgradeSafety.ValidatePaths(source, destination);
        var created = false;
        try
        { return UpgradeCore(source, destination, incarnation, settings, () => created = true); }
        catch (Exception primary)
        {
            if (created)
            {
                try
                { File.Delete(destination); }
                catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            }

            ExceptionDispatchInfo.Capture(primary).Throw();
            throw;
        }
    }

    private static StorageSnapshot UpgradeCore(string source, string destination, Guid incarnation,
        ZoneTreeSnapshotUpgradeSettings settings, Action outputCreated)
    {
        ZoneTreeSnapshotUpgradeSafety.VerifySourcePath(source);
        ZoneTreeSnapshotUpgradeSafety.VerifyAbsentDestination(destination);
        return ZoneTreeSnapshotUpgradeIO.WithSource(source, settings.MaxSnapshotBytes,
            input => UpgradeWithSource(source, destination, incarnation, settings, outputCreated, input));
    }

    private static StorageSnapshot UpgradeWithSource(string source, string destination, Guid incarnation,
        ZoneTreeSnapshotUpgradeSettings settings, Action outputCreated, FileStream input)
    {
        var rawDigest = ZoneTreeSnapshotUpgradeIO.DigestFile(input, settings.MaxSnapshotBytes);
        var firstPass = ReadSource(input, settings, incarnation);
        using var secondSemantic = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        StorageSnapshot? secondSnapshot = null;
        var outputSnapshot = ZoneTreeSnapshotUpgradeOutput.Write(destination, settings, firstPass.Snapshot,
            outputCreated, visitor =>
            {
                input.Position = 0;
                secondSnapshot = ZoneTreeCheckpointReader.ReadNative3ForUpgrade(input,
                    ReaderOptions(source, settings), mutation =>
                    {
                        ZoneTreeSnapshotUpgradeDigest.Append(secondSemantic, mutation);
                        visitor(mutation);
                    }, incarnation);
                ZoneTreeSnapshotUpgradeIO.RequireEndOfFile(input);
            });
        var secondDigest = ZoneTreeSnapshotUpgradeDigest.Finish(secondSemantic);
        VerifySamePass(firstPass, secondSnapshot, secondDigest);
        var verifiedOutput = ReadOutput(destination, settings, firstPass, outputSnapshot);
        input.Position = 0;
        if (!ZoneTreeSnapshotUpgradeDigest.Equal(rawDigest,
                ZoneTreeSnapshotUpgradeIO.DigestFile(input, settings.MaxSnapshotBytes)))
        { throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.BackupFileVerificationFailed); }

        return verifiedOutput;
    }

    private static void VerifySamePass(ReadResult first, StorageSnapshot? secondSnapshot, byte[] secondDigest)
    {
        if (secondSnapshot != first.Snapshot || !ZoneTreeSnapshotUpgradeDigest.Equal(first.SemanticDigest, secondDigest))
        { throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointVerificationFailed); }
    }

    private static StorageSnapshot ReadOutput(string path, ZoneTreeSnapshotUpgradeSettings settings,
        ReadResult first, StorageSnapshot outputSnapshot)
    {
        using var semantic = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes,
            FileOptions.SequentialScan);
        ZoneTreeSnapshotUpgradeIO.RequireSnapshotSize(input, settings.MaxSnapshotBytes);
        var verified = ZoneTreeCheckpointReader.Read(input, ReaderOptions(path, settings),
            mutation => ZoneTreeSnapshotUpgradeDigest.Append(semantic, mutation));
        ZoneTreeSnapshotUpgradeIO.RequireEndOfFile(input);
        var digest = ZoneTreeSnapshotUpgradeDigest.Finish(semantic);
        if (verified != first.Snapshot || outputSnapshot != first.Snapshot
            || !ZoneTreeSnapshotUpgradeDigest.Equal(first.SemanticDigest, digest))
        { throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointVerificationFailed); }

        return verified;
    }

    private static ReadResult ReadSource(FileStream input, ZoneTreeSnapshotUpgradeSettings settings, Guid incarnation)
    {
        input.Position = 0;
        using var semantic = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var snapshot = ZoneTreeCheckpointReader.ReadNative3ForUpgrade(input, ReaderOptions(input.Name, settings),
            mutation => ZoneTreeSnapshotUpgradeDigest.Append(semantic, mutation), incarnation);
        ZoneTreeSnapshotUpgradeIO.RequireEndOfFile(input);
        return new(snapshot, ZoneTreeSnapshotUpgradeDigest.Finish(semantic));
    }

    private static ZoneTreeStoreOptions ReaderOptions(string directory, ZoneTreeSnapshotUpgradeSettings settings)
        => new(directory) { MaxFrameBytes = settings.MaxFrameBytes, MaxSnapshotBytes = settings.MaxSnapshotBytes };

    private static ZoneTreeStoreOptions WriterOptions(string directory, ZoneTreeSnapshotUpgradeSettings settings)
        => new(directory)
        {
            MaxFrameBytes = settings.MaxFrameBytes,
            MaxSnapshotBytes = settings.MaxSnapshotBytes,
            FaultObserver = settings.FaultObserver
        };

    internal static StorageSnapshot WriteOutput(FileStream output, string path,
        ZoneTreeSnapshotUpgradeSettings settings, StorageSnapshot snapshot,
        Action<Action<StorageMutation>> visit)
        => ZoneTreeCheckpointWriter.WriteMutations(output, WriterOptions(path, settings), snapshot, visit);

    private sealed record ReadResult(StorageSnapshot Snapshot, byte[] SemanticDigest);
}
