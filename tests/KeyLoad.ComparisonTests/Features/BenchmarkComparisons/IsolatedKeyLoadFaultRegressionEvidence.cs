using System.Text.Json;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Owned atomic no-overwrite evidence exports only safe provider, native and public membership facts.</summary>
internal sealed class IsolatedKeyLoadFaultRegressionEvidence(string cell, IsolatedComparisonWorker worker)
{
    private const int MaximumBytes = 262_144;
    public int SchemaVersion { get; } = 1;
    public string Cell { get; } = cell;
    public IsolatedComparisonWorker Worker { get; } = worker;
    public DateTimeOffset StartedAt { get; } = TimeProvider.System.GetUtcNow();
    public DateTimeOffset? CompletedAt { get; private set; }
    public string Status { get; private set; } = "failed";
    public string? FailureCategory { get; private set; }
    public string Stage { get; internal set; } = "setup";
    public IsolatedKeyLoadFaultRegressionMembership[] Baseline { get; internal set; } = [];
    public List<IsolatedKeyLoadFaultRegressionPhaseReceipt> Phases { get; } = [];
    public List<IsolatedKeyLoadFaultRegressionNativeReceipt> Native { get; internal set; } = [];

    internal async Task WriteAsync(string directory, string? failureCategory)
    {
        FailureCategory = failureCategory;
        Status = failureCategory is null ? "passed" : "failed";
        CompletedAt = TimeProvider.System.GetUtcNow();
        IsolatedKeyLoadFaultRegressionProtocol.Require(Path.GetFullPath(directory)
            == Path.GetFullPath(IsolatedNativeReportAssertions.EvidenceDirectory()));
        Directory.CreateDirectory(directory);
        var pending = Path.Combine(directory, ".fault-" + Guid.NewGuid().ToString("N") + ".pending");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this, JsonDefaults.Options);
        IsolatedKeyLoadFaultRegressionProtocol.Require(bytes.Length <= MaximumBytes);
        var owned = false;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(IsolatedKeyLoadFaultRegressionProtocol.CliSeconds));
        try
        {
            await using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 4_096, FileOptions.Asynchronous))
            {
                owned = true;
                await stream.WriteAsync(bytes, deadline.Token);
                await stream.FlushAsync(deadline.Token);
            }
            deadline.Token.ThrowIfCancellationRequested();
            File.Move(pending, Path.Combine(directory, IsolatedKeyLoadFaultRegressionProtocol.EvidenceFile), overwrite: false);
        }
        finally
        {
            if (owned)
            {
                File.Delete(pending);
            }
        }
    }
}

internal sealed class IsolatedKeyLoadFaultRegressionPhaseReceipt(int phase, int killedNode, Guid commandId, string kind)
{
    public int Phase { get; } = phase;
    public int KilledNode { get; } = killedNode;
    public Guid CommandId { get; } = commandId;
    public string Kind { get; } = kind;
    public bool NativeMcpAuthentication503 { get; internal set; }
    public string? WriteRejection { get; internal set; }
    public long? ResolvedPosition { get; internal set; }
    public IsolatedKeyLoadFaultRegressionMembership[] Restored { get; internal set; } = [];
    public bool Complete { get; internal set; }
}
