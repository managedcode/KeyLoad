using System.Text.Json;
using Grpc.Core;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class KurrentCleanupDiagnosticTests
{
    private const string Secret = "credential-marker";
    private const int NativeDeadlineStatus = 4;

    /// <summary>AC-ISO-005/006: acknowledged native cleanup is complete only with balanced final counters.</summary>
    [Test]
    public async Task CompleteCountsRequireEveryStreamAcknowledgedAndNoPendingOperation()
    {
        var complete = new KurrentCleanupCounts(55_378, 55_378, 55_378, 0, 0, 16);
        await Assert.That(complete.IsValid).IsTrue();
        await Assert.That(complete.IsComplete).IsTrue();
        await Assert.That((complete with { Acknowledged = 55_377, Pending = 1 }).IsComplete).IsFalse();
        await Assert.That((complete with { Acknowledged = 55_377, Faulted = 1 }).IsComplete).IsFalse();
        await Assert.That((complete with { Submitted = 55_377, Acknowledged = 55_377 }).IsComplete).IsFalse();
    }

    [Test]
    public async Task InvalidAndOverflowingCountersNeverBecomeSuccessfulCleanup()
    {
        KurrentCleanupCounts[] invalid =
        [
            new(-1, 0, 0, 0, 0, 0), new(2, 3, 3, 0, 0, 1), new(2, 2, 3, 0, 0, 1),
            new(2, 2, 1, 0, 0, 1), new(2, 2, 1, 0, 1, 17), new(2, 2, 1, 0, 1, 0),
            new(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, 1, 16),
        ];
        foreach (var counts in invalid)
        {
            await Assert.That(counts.IsValid).IsFalse();
            await Assert.That(counts.IsComplete).IsFalse();
        }
        await Assert.That(new KurrentCleanupCounts(2, 2, 1, 1, 0, 2).IsValid).IsTrue();
        await Assert.That(new KurrentCleanupCounts(0, 0, 0, 0, 0, 0).IsComplete).IsTrue();
    }

    [Test]
    public async Task ClosedProjectionRetainsActualNumericStatusAndNeverNativeText()
    {
        var error = new AggregateException(new RpcException(new Status(StatusCode.DeadlineExceeded, Secret)));
        var classified = KurrentCleanupDiagnostics.Classify(error);
        var diagnostic = Failure(classified.Reason, classified.GrpcStatus);
        var text = KurrentCleanupDiagnostics.Project(diagnostic);
        using var document = JsonDocument.Parse(text);
        await Assert.That(text.Contains(Secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Length <= KurrentCleanupDiagnostics.MaximumCharacters).IsTrue();
        await Assert.That(document.RootElement.GetProperty(nameof(KurrentCleanupDiagnostic.GrpcStatus)).GetInt32()).IsEqualTo(NativeDeadlineStatus);
        await Assert.That(classified.Reason).IsEqualTo(KurrentCleanupFailureReason.NativeRpc);
    }

    [Test]
    public async Task UnknownAndOutOfRangeStatusRemainNullObservations()
    {
        foreach (var error in new Exception[]
        {
            new InvalidOperationException(Secret), new RpcException(new Status((StatusCode)99, Secret)),
        })
        {
            var classified = KurrentCleanupDiagnostics.Classify(error);
            var text = KurrentCleanupDiagnostics.Project(Failure(classified.Reason, classified.GrpcStatus));
            using var document = JsonDocument.Parse(text);
            await Assert.That(document.RootElement.GetProperty(nameof(KurrentCleanupDiagnostic.GrpcStatus)).ValueKind).IsEqualTo(JsonValueKind.Null);
            await Assert.That(text.Contains(Secret, StringComparison.Ordinal)).IsFalse();
        }
    }

    [Test]
    public async Task CancellationAndUnknownAcknowledgementRemainFailureFacts()
    {
        var classified = KurrentCleanupDiagnostics.Classify(new OperationCanceledException(Secret));
        var diagnostic = Failure(classified.Reason, classified.GrpcStatus) with { CancellationRequested = true, DeadlineExpired = true };
        using var document = JsonDocument.Parse(KurrentCleanupDiagnostics.Project(diagnostic));
        await Assert.That(classified.Reason).IsEqualTo(KurrentCleanupFailureReason.Cancelled);
        await Assert.That(document.RootElement.GetProperty(nameof(KurrentCleanupDiagnostic.Outcome)).GetInt32()).IsEqualTo((int)KurrentCleanupOutcome.Failed);
        await Assert.That(diagnostic.Counts.IsComplete).IsFalse();
        await Assert.That(diagnostic.GrpcStatus).IsNull();
    }

    [Test]
    public async Task FirstNativeFailureStopsAdmissionAndSurvivesLaterDisposalFailure()
    {
        var state = new KurrentCleanupState(3, NativeExecutionPolicyFixture.Lifecycle());
        var first = new RpcException(new Status(StatusCode.DeadlineExceeded, Secret));
        await Assert.That(state.TrySubmit(CancellationToken.None, out var index)).IsTrue();
        await Assert.That(index).IsEqualTo(0);
        state.CompleteDelete(first);
        await Assert.That(state.TrySubmit(CancellationToken.None, out _)).IsFalse();
        state.RecordFailure(new IOException(Secret), KurrentCleanupStage.NativeDispose, disposal: true);
        state.FinishDeletion(cancellationRequested: true, expired: false);
        var diagnostic = state.Snapshot();
        await Assert.That(diagnostic.Counts).IsEqualTo(new KurrentCleanupCounts(3, 1, 0, 1, 0, 1));
        await Assert.That(diagnostic.Stage).IsEqualTo(KurrentCleanupStage.Delete);
        await Assert.That(diagnostic.GrpcStatus).IsEqualTo(NativeDeadlineStatus);
        await Assert.That(diagnostic.LaterDisposalFailures).IsEqualTo(1);
        Exception? thrown = null;
        try
        {
            state.ThrowIfFailed();
        }
        catch (RpcException error)
        {
            thrown = error;
        }
        await Assert.That(ReferenceEquals(thrown, first)).IsTrue();
    }

    [Test]
    public async Task ProjectionRejectsIncompleteSuccessAndDisposalFailureRelabelling()
    {
        var success = new KurrentCleanupDiagnostic(1, KurrentCleanupStage.Complete, KurrentCleanupOutcome.Succeeded,
            KurrentCleanupFailureReason.None, new KurrentCleanupCounts(1, 1, 1, 0, 0, 1), 1, false, false, null, 0);
        KurrentCleanupDiagnostic[] invalid =
        [
            success with { Counts = new KurrentCleanupCounts(1, 1, 0, 0, 1, 1) },
            success with { LaterDisposalFailures = 1 }, success with { DeadlineExpired = true },
            success with { Reason = KurrentCleanupFailureReason.Unknown }, success with { GrpcStatus = 4 },
        ];
        foreach (var diagnostic in invalid)
        {
            ArgumentException? failure = null;
            try
            {
                _ = KurrentCleanupDiagnostics.Project(diagnostic);
            }
            catch (ArgumentException error)
            {
                failure = error;
            }
            await Assert.That(failure).IsNotNull();
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(2)]
    public async Task UnknownSchemaVersionsNeverProduceCleanupEvidence(int version)
    {
        ArgumentException? failure = null;
        try
        {
            _ = KurrentCleanupDiagnostics.Project(Failure(KurrentCleanupFailureReason.Unknown, null) with { SchemaVersion = version });
        }
        catch (ArgumentException error)
        {
            failure = error;
        }
        await Assert.That(failure).IsNotNull();
    }

    private static KurrentCleanupDiagnostic Failure(KurrentCleanupFailureReason reason, int? status)
        => new(1, KurrentCleanupStage.Delete, KurrentCleanupOutcome.Failed, reason,
            new KurrentCleanupCounts(3, 2, 1, 1, 0, 2), 20_000, false, false, status, 0);
}
