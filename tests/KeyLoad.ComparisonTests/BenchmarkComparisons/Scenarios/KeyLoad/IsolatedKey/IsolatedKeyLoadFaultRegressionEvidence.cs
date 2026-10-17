using System.Text.Json;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Owned atomic no-overwrite evidence exports only safe provider, native and public membership facts.</summary>
internal sealed class IsolatedKeyLoadFaultRegressionEvidence(string cell, BenchmarkSessionFaultWorker worker)
{
    public int SchemaVersion { get; } = 1;
    public string Cell { get; } = cell;
    public BenchmarkSessionFaultWorker Worker { get; } = worker;
    public DateTimeOffset StartedAt { get; } = TimeProvider.System.GetUtcNow();
    public DateTimeOffset? CompletedAt
    {
        get; private set;
    }
    public string Status { get; private set; } = "failed";
    public string? FailureCategory
    {
        get; private set;
    }
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
            == Path.GetFullPath(Path.GetFullPath(Environment.GetEnvironmentVariable("KEYLOAD_COMPARISON_EVIDENCE_DIRECTORY")
            ?? throw new InvalidOperationException("BenchmarkSessionEvidenceDirectoryMissing"))));
        Directory.CreateDirectory(directory);
        var pending = Path.Combine(directory, ".fault-" + Guid.NewGuid().ToString("N") + ".pending");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this, JsonDefaults.Options);
        IsolatedKeyLoadFaultRegressionProtocol.Require(bytes.Length <= NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultMaximumEvidenceBytes);
        var owned = false;
        using var deadline = new CancellationTokenSource(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultCliTimeout, TimeProvider.System);
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
