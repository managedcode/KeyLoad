using System.Globalization;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Preserves the earliest bounded failures and the unchanged last-failure view.</summary>
internal sealed class ClusterFailureReceipts
{
    private const int MaximumReceipts = 32;
    private const string FilePrefix = "rf3-failure-";
    private const string RestartFilePrefix = "rf3-restart-failure-";
    private const string RestartLatestFileName = "rf3-restart-failure.log";
    private const string FileSuffix = ".log";
    private const string SequenceFormat = "D4";
    private readonly Lock writeGate = new();
    private readonly string filePrefix;
    private readonly string latestFileName;
    private const string RunOwnerMismatch = "The native diagnostic receipt owner belongs to another results root.";
    private string? runRoot;
    private string? runOutput;
    private int savedReceipts;

    internal ClusterFailureReceipts(ClusterFailureReceiptKind kind = ClusterFailureReceiptKind.General)
    {
        (filePrefix, latestFileName) = kind switch
        {
            ClusterFailureReceiptKind.General => (FilePrefix, ClusterFixtureProtocol.DiagnosticsFileName),
            ClusterFailureReceiptKind.Restart => (RestartFilePrefix, RestartLatestFileName),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    /// <summary>Retains one native runner result namespace for this original receipt owner.</summary>
    internal string RequireRunOutput(string resultsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resultsDirectory);
        var root = Path.GetFullPath(resultsDirectory);
        lock (writeGate)
        {
            if (runRoot is not null && !string.Equals(runRoot, root, StringComparison.Ordinal))
            { throw new InvalidOperationException(RunOwnerMismatch); }
            runRoot ??= root;
            return runOutput ??= Path.Combine(root, ClusterFixtureProtocol.QualificationDirectory,
                Guid.NewGuid().ToString(ClusterFixtureProtocol.GuidFormat));
        }
    }

    /// <summary>Serializes whole-file writes without retaining additional diagnostic text in memory.</summary>
    /// <param name="output">The fixture-owned diagnostic directory.</param>
    /// <param name="bounded">Lines already clipped to the existing privacy, line and byte budgets.</param>
    /// <returns>The unchanged last-failure artifact path.</returns>
    internal static string SaveImmutable(string output, string[] bounded)
    {
        var path = Path.Combine(output, ClusterFixtureProtocol.DiagnosticsFileName);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, leaveOpen: true);
        foreach (var line in bounded)
        { writer.WriteLine(line); }
        return path;
    }

    internal string Save(string output, string[] bounded)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        ArgumentNullException.ThrowIfNull(bounded);
        var latestPath = Path.Combine(output, latestFileName);
        lock (writeGate)
        {
            File.WriteAllLines(latestPath, bounded);
            if (savedReceipts < MaximumReceipts)
            {
                var sequence = (savedReceipts + 1).ToString(SequenceFormat, CultureInfo.InvariantCulture);
                File.WriteAllLines(Path.Combine(output, filePrefix + sequence + FileSuffix), bounded);
                savedReceipts++;
            }
        }
        return latestPath;
    }
}
