using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativePlacementAdmissionFlow
{
    private const int FirstCompleted = 1;
    private const int TotalMoves = 2;
    private const int TwoFrames = 2;

    internal static async Task GrammarAsync(RequestCqrsClusterFixture fixture)
    {
        var id = Guid.NewGuid();
        var intent = Guid.NewGuid();
        var request = NativeProgressAdmissionSupport.Placement(fixture, intent);
        var first = NativeProgressAdmissionSupport.Observation(intent, Guid.NewGuid(), FirstCompleted,
            TotalMoves, NativeProgressAdmissionSupport.InitialRevision);
        var last = NativeProgressAdmissionSupport.Observation(intent, Guid.NewGuid(), TotalMoves,
            TotalMoves, NativeProgressAdmissionSupport.NextRevision);
        var frames = await NativePlacementProgressFrames.CreateAsync(id, first, last);
        var full = NativeProgressAdmissionSupport.Admission(fixture, id, OperationKind.ApplyPlacementIntent, request);
        full.Admit(RequestCqrsProtocolCases.StartedChunk(id), CancellationToken.None);
        full.Admit(frames[NativeProgressAdmissionSupport.Started], CancellationToken.None);
        await NativeProgressAdmissionSupport.RefusedAsync(() => full.Admit(
            NativeProgressAdmissionSupport.Terminal(NativeProgressAdmissionSupport.SecondSequence), CancellationToken.None), ErrorCode.OwnershipLost);
        full.Admit(frames[NativeProgressAdmissionSupport.First], CancellationToken.None);
        await NativeProgressAdmissionSupport.CompleteAsync(full,
            [NativeProgressAdmissionSupport.Terminal(NativeProgressAdmissionSupport.TerminalSequence)]);
        var resumed = NativeProgressAdmissionSupport.Admission(fixture, id, OperationKind.ApplyPlacementIntent, request);
        resumed.Admit(RequestCqrsProtocolCases.StartedChunk(id), CancellationToken.None);
        resumed.Admit((await NativePlacementProgressFrames.CreateAsync(id, last))[NativeProgressAdmissionSupport.Started], CancellationToken.None);
        await NativeProgressAdmissionSupport.CompleteAsync(resumed,
            [NativeProgressAdmissionSupport.Terminal(NativeProgressAdmissionSupport.SecondSequence)]);
        var replay = NativeProgressAdmissionSupport.Admission(fixture, id, OperationKind.ApplyPlacementIntent, request);
        await NativeProgressAdmissionSupport.CompleteAsync(replay,
            [RequestCqrsProtocolCases.StartedChunk(id), NativeProgressAdmissionSupport.Terminal(TwoFrames)]);
        var wrong = NativeProgressAdmissionSupport.Admission(fixture, id, OperationKind.Batch, request);
        wrong.Admit(RequestCqrsProtocolCases.StartedChunk(id), CancellationToken.None);
        await NativeProgressAdmissionSupport.RefusedAsync(() => wrong.Admit(frames[NativeProgressAdmissionSupport.Started],
            CancellationToken.None), ErrorCode.OwnershipLost);
    }

    internal static async Task BoundContinuationAsync(RequestCqrsClusterFixture fixture, bool aggregate)
    {
        var id = Guid.NewGuid();
        var intent = Guid.NewGuid();
        var request = NativeProgressAdmissionSupport.Placement(fixture, intent);
        var first = NativeProgressAdmissionSupport.Observation(intent, Guid.NewGuid(), FirstCompleted,
            TotalMoves, NativeProgressAdmissionSupport.InitialRevision);
        var larger = first with
        {
            Revision = long.MaxValue
        };
        var healthy = (await NativePlacementProgressFrames.CreateAsync(id, first))[NativeProgressAdmissionSupport.Started];
        var excessive = (await NativePlacementProgressFrames.CreateAsync(id, larger))[NativeProgressAdmissionSupport.Started];
        var started = RequestCqrsProtocolCases.StartedChunk(id);
        var healthyBytes = NativeProgressAdmissionSupport.Measure(fixture, healthy);
        var excessiveBytes = NativeProgressAdmissionSupport.Measure(fixture, excessive);
        var startedBytes = NativeProgressAdmissionSupport.Measure(fixture, started);
        await Assert.That(excessiveBytes).IsGreaterThan(healthyBytes);
        await Assert.That(healthyBytes).IsGreaterThanOrEqualTo(startedBytes);
        var options = new GrainRoutingOptions();
        if (aggregate)
        {
            options.MaximumAggregateBytes = checked((int)(startedBytes + healthyBytes));
        }
        else
        {
            options.MaximumStartedBytes = checked((int)healthyBytes);
        }
        var admission = NativeProgressAdmissionSupport.Admission(fixture, id, OperationKind.ApplyPlacementIntent,
            request, UnitRoutingOptions.Routing(options));
        admission.Admit(started, CancellationToken.None);
        await NativeProgressAdmissionSupport.RefusedAsync(() => admission.Admit(excessive, CancellationToken.None), ErrorCode.BudgetExceeded);
        admission.Admit(healthy, CancellationToken.None);
        await Assert.That(admission.CompletionFailure(CancellationToken.None)).IsTypeOf<KeyLoadException>();
        // The same original MoveId is admitted after refusal; no rejected revision/count/move was committed.
        var following = NativeProgressAdmissionSupport.Observation(intent, Guid.NewGuid(), TotalMoves,
            TotalMoves, NativeProgressAdmissionSupport.NextRevision);
        var normal = NativeProgressAdmissionSupport.Admission(fixture, id, OperationKind.ApplyPlacementIntent, request);
        normal.Admit(started, CancellationToken.None);
        await NativeProgressAdmissionSupport.CompleteAsync(normal,
            (await NativePlacementProgressFrames.CreateAsync(id, first, following))
                .Append(NativeProgressAdmissionSupport.Terminal(NativeProgressAdmissionSupport.TerminalSequence)));
    }
}
