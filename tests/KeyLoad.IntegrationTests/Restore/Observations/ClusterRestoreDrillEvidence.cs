using System.Collections.Immutable;
using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal sealed class ClusterRestoreDrillEvidence
{
    private const string Prefix = "Actual cluster restore interval/cut evidence: ";
    private readonly TimeProvider clock;
    private readonly long started;
    private long captureStarted;
    private long captureFinished;
    private long restoreStarted;
    private long publicReady;
    private long coldFinished;

    internal ClusterRestoreDrillEvidence(TimeProvider clock)
    {
        this.clock = clock;
        started = clock.GetTimestamp();
    }

    internal void BeginCapture() => captureStarted = clock.GetTimestamp();
    internal void EndCapture() => captureFinished = clock.GetTimestamp();
    internal void BeginRestore() => restoreStarted = clock.GetTimestamp();
    internal void PublicReady() => publicReady = clock.GetTimestamp();
    internal void ColdJoined() => coldFinished = clock.GetTimestamp();

    internal async Task WriteAsync(ImmutableArray<ClusterBackupOwnerReceipt> originals)
    {
        var wholeJoined = clock.GetTimestamp();
        var evidence = new
        {
            timestampFrequency = clock.TimestampFrequency,
            captureStart = captureStarted - started,
            captureEnd = captureFinished - started,
            restoreStart = restoreStarted - started,
            publicReady = publicReady - started,
            coldJoined = coldFinished - started,
            wholeOwnerJoined = wholeJoined - started,
            captureDurationTicks = clock.GetElapsedTime(captureStarted, captureFinished).Ticks,
            restoreToPublicContinuationTicks = clock.GetElapsedTime(restoreStarted, publicReady).Ticks,
            restoreToColdJoinedTicks = clock.GetElapsedTime(restoreStarted, coldFinished).Ticks,
            physicalCuts = originals.Select(original => new
            {
                original.Cut.CaptureId,
                original.Cut.SourceNodeId,
                original.Cut.Owner.PhysicalShardId,
                original.Cut.Owner.Incarnation,
                original.Cut.StorePosition,
                original.Cut.AppliedIndex
            }).ToArray()
        };
        await TestContext.Current!.OutputWriter.WriteLineAsync(Prefix + JsonSerializer.Serialize(evidence));
    }
}
