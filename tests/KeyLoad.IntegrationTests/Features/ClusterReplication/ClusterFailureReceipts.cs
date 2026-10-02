using System.Globalization;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Preserves the earliest bounded failures and the unchanged last-failure view.</summary>
internal sealed class ClusterFailureReceipts
{
    private const int MaximumReceipts = 32;
    private const string FilePrefix = "rf3-failure-";
    private const string FileSuffix = ".log";
    private const string SequenceFormat = "D4";
    private readonly Lock writeGate = new();
    private int savedReceipts;

    /// <summary>Serializes whole-file writes without retaining additional diagnostic text in memory.</summary>
    /// <param name="output">The fixture-owned diagnostic directory.</param>
    /// <param name="bounded">Lines already clipped to the existing privacy, line and byte budgets.</param>
    /// <returns>The unchanged last-failure artifact path.</returns>
    internal string Save(string output, string[] bounded)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        ArgumentNullException.ThrowIfNull(bounded);
        var latestPath = Path.Combine(output, ClusterFixtureProtocol.DiagnosticsFileName);
        lock (writeGate)
        {
            File.WriteAllLines(latestPath, bounded);
            if (savedReceipts < MaximumReceipts)
            {
                var sequence = (savedReceipts + 1).ToString(SequenceFormat, CultureInfo.InvariantCulture);
                File.WriteAllLines(Path.Combine(output, FilePrefix + sequence + FileSuffix), bounded);
                savedReceipts++;
            }
        }
        return latestPath;
    }
}
