using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.CrashHost;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal static class EpochPriorExecutableArtifact
{
    private const string ArtifactDirectory = "artifacts/native5-probe";
    private const string ReceiptFile = "native5-probe.json";
    private const string ExecutableFile = "KeyLoad.CrashHost.dll";
    private const string SolutionFile = "KeyLoad.slnx";
    private const string InvalidArtifact = "Build and verify the immutable native5 probe before running upgrade recovery tests.";
    private const int MaximumReceiptBytes = 4194304;
    private const int MaximumFiles = 512;
    private static readonly string[] DriverSources =
    [
        "tests/KeyLoad.CrashHost/Features/StorageRecovery/EpochPriorSourceProbe.cs",
        "tests/KeyLoad.CrashHost/Features/StorageRecovery/EpochUpgradeFixture.cs"
    ];

    internal static async Task<string> VerifyAsync(CancellationToken cancellationToken)
    {
        var repository = FindRepository();
        var root = Path.GetFullPath(Path.Combine(repository, ArtifactDirectory));
        var path = Path.Combine(root, ReceiptFile);
        RequireRegular(path, root);
        if (new FileInfo(path).Length is <= 0 or > MaximumReceiptBytes)
        {
            throw new InvalidDataException(InvalidArtifact);
        }
        using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(path, cancellationToken));
        var receipt = document.RootElement.Deserialize<EpochPriorArtifactReceipt>(EpochPriorSourceProbe.JsonOptions)
            ?? throw new InvalidDataException(InvalidArtifact);
        if (receipt.SchemaVersion != 1 || receipt.SourceRevision != EpochPriorSourceProbe.SourceRevision
            || receipt.DataEpoch != 5 || receipt.JournalVersion != 4 || receipt.CheckpointVersion != 3
            || receipt.Files.Length is < 1 or > MaximumFiles
            || !receipt.DriverSources.Select(source => source.Path).Order(StringComparer.Ordinal)
                .SequenceEqual(DriverSources.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidDataException(InvalidArtifact);
        }
        await VerifyFilesAsync(root, receipt.Files, cancellationToken);
        await VerifyFilesAsync(repository, receipt.DriverSources, cancellationToken);
        var executable = Path.Combine(root, ExecutableFile);
        if (!receipt.Files.Any(file => file.Path == ExecutableFile))
        {
            throw new InvalidDataException(InvalidArtifact);
        }
        return executable;
    }

    private static async Task VerifyFilesAsync(string root, EpochPriorArtifactFile[] files,
        CancellationToken cancellationToken)
    {
        if (files.Select(file => file.Path).Distinct(StringComparer.Ordinal).Count() != files.Length)
        {
            throw new InvalidDataException(InvalidArtifact);
        }
        foreach (var file in files)
        {
            var path = Path.GetFullPath(Path.Combine(root, file.Path));
            RequireRegular(path, root);
            await using var stream = File.OpenRead(path);
            var digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
            if (stream.Length != file.Bytes || !string.Equals(digest, file.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(InvalidArtifact);
            }
        }
    }

    private static void RequireRegular(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar).Contains("..")
            || !File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(InvalidArtifact);
        }
        for (var directory = new FileInfo(path).Directory; directory is not null; directory = directory.Parent)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(InvalidArtifact);
            }
            if (directory.FullName == root)
            {
                return;
            }
        }
        throw new InvalidDataException(InvalidArtifact);
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
            {
                return directory.FullName;
            }
        }
        throw new InvalidDataException(InvalidArtifact);
    }
}

internal sealed record EpochPriorArtifactReceipt(int SchemaVersion, string SourceRevision, int DataEpoch,
    int JournalVersion, int CheckpointVersion, EpochPriorArtifactFile[] DriverSources, EpochPriorArtifactFile[] Files);

internal sealed record EpochPriorArtifactFile(string Path, long Bytes, string Sha256);
