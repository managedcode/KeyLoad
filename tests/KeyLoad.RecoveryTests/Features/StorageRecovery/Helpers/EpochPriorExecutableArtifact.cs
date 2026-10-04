using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.CrashHost;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal static class EpochPriorExecutableArtifact
{
    private const string ArtifactDirectoryEnvironment = "KeyLoadTests__PriorProbesDirectory";
    private const string Native5Directory = "native5-probe";
    private const string Native6Directory = "native6-probe";
    private const string ExecutableFile = "KeyLoad.CrashHost.dll";
    private const string SolutionFile = "KeyLoad.slnx";
    private const string InvalidArtifact = "Build and verify the immutable native5 and native6 probes before running upgrade recovery tests.";
    private const int MaximumReceiptBytes = 4194304;
    private const int MaximumFiles = 512;
    private const int Native5Epoch = 5;
    private const int Native6Epoch = 6;
    private const int Native5CheckpointVersion = 3;
    private const int Native6CheckpointVersion = 4;
    private const string Native5Tree = "b03bf1301a03b3fe00f419c3c7bf5285a34b63f9";
    private const string Native6Tree = "678ac682c90294306a0ae4092c4c80b382c4a18b";
    private const string ProbeProducer = "immutable source export plus isolated test driver";
    private static readonly string[] DriverSources =
    [
        "tests/KeyLoad.CrashHost/Features/StorageRecovery/Helpers/EpochPriorSourceProbe.cs",
        "tests/KeyLoad.CrashHost/Features/StorageRecovery/Fixtures/EpochUpgradeFixture.cs"
    ];

    internal static async Task<string> VerifyAsync(int expectedDataEpoch, CancellationToken cancellationToken)
    {
        var repository = FindRepository();
        var root = ReadProbeRoot();
        var profile = Profile(expectedDataEpoch);
        var probeDirectory = expectedDataEpoch == Native5Epoch ? Native5Directory : Native6Directory;
        var fileName = $"native{expectedDataEpoch}-probe.json";
        var path = Path.Combine(root, probeDirectory, fileName);
        var probeRoot = Path.Combine(root, probeDirectory);
        RequireRegular(path, probeRoot);
        if (new FileInfo(path).Length is <= 0 or > MaximumReceiptBytes)
        {
            throw new InvalidDataException(InvalidArtifact);
        }
        using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(path, cancellationToken));
        var receipt = document.RootElement.Deserialize<EpochPriorArtifactReceipt?>(EpochPriorSourceProbe.JsonOptions)
            ?? throw new InvalidDataException(InvalidArtifact);
        if (receipt.SchemaVersion != 1 || receipt.SourceRevision != EpochPriorSourceProbe.SourceRevisionForEpoch(expectedDataEpoch)
            || receipt.SourceTree != profile.Tree || receipt.DataEpoch != expectedDataEpoch
            || receipt.JournalVersion != 4 || receipt.CheckpointVersion != profile.CheckpointVersion
            || !IsDigest(receipt.OriginalArchiveSha256) || receipt.GitHubQualified
            || receipt.Producer != ProbeProducer
            || receipt.Files.Length is < 1 or > MaximumFiles
            || !receipt.DriverSources.Select(source => source.Path).Order(StringComparer.Ordinal)
                .SequenceEqual(DriverSources.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidDataException(InvalidArtifact);
        }
        await VerifyFilesAsync(probeRoot, receipt.Files, cancellationToken);
        await VerifyFilesAsync(repository, receipt.DriverSources, cancellationToken);
        var executable = Path.Combine(probeRoot, ExecutableFile);
        if (!receipt.Files.Any(file => file.Path == ExecutableFile))
        {
            throw new InvalidDataException(InvalidArtifact);
        }
        return executable;
    }

    private static string ReadProbeRoot()
    {
        var configured = Environment.GetEnvironmentVariable(ArtifactDirectoryEnvironment);
        if (string.IsNullOrWhiteSpace(configured) || !Path.IsPathFullyQualified(configured))
        { throw new InvalidDataException(InvalidArtifact); }
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured));
    }

    private static (string Tree, int CheckpointVersion) Profile(int epoch) => epoch switch
    {
        Native5Epoch => (Native5Tree, Native5CheckpointVersion),
        Native6Epoch => (Native6Tree, Native6CheckpointVersion),
        _ => throw new ArgumentOutOfRangeException(nameof(epoch))
    };

    private static bool IsDigest(string value)
        => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

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

internal readonly record struct EpochPriorArtifactReceipt(int SchemaVersion, string SourceRevision, int DataEpoch,
    int JournalVersion, int CheckpointVersion, string SourceTree, string OriginalArchiveSha256,
    EpochPriorArtifactFile[] DriverSources, EpochPriorArtifactFile[] Files, bool GitHubQualified, string Producer);

internal readonly record struct EpochPriorArtifactFile(string Path, long Bytes, string Sha256);
