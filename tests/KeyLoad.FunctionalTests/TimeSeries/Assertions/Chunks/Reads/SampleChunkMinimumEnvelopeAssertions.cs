using KeyLoad.Clustering;
using KeyLoad.FunctionalTests.OrleansExecution;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class SampleChunkMinimumEnvelopeAssertions
{
    private const long NoCut = 0;
    private const string ScopeDenied = "The principal cannot perform this operation in this scope.";

    internal static async Task ObserveAsync(ZoneTreeStore store, Func<Task> operation)
    {
        var cut = SampleRawIntegrityAssertions.Capture(store);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => SampleRawIntegrityAssertions.UnchangedAsync(store, cut), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static void Page(ReadSampleChunkWindowRequest request, SampleChunkWindowResult actual)
    {
        if (actual.CutPosition <= NoCut)
        {
            throw new InvalidOperationException("The execution chunk read has no owner cut.");
        }
        var expected = new SampleChunkWindowResult(request.WindowId, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until, SampleChunkCanonicalFixture.FirstGeneration,
            SampleChunkCanonicalFixture.SealedRevision, SampleChunkCanonicalFixture.InitialSequence, null,
            [.. SampleChunkCanonicalFixture.Expected()], actual.CutPosition);
        SampleChunkExplicitMinimumUnitReads.RequireBytes(expected, actual);
    }

    internal static async Task HealthyAsync(ConnectionScenario scenario, SampleChunkMinimumEnvelopeState state,
        CancellationToken token)
    {
        var id = Guid.NewGuid();
        var envelope = SampleChunkMinimumEnvelopeIssuance.Create(scenario.Fixture.Codec, state.Reader, id, state.Request);
        SampleChunkMinimumEnvelopeIssuance.Decode(scenario.Fixture.Codec, envelope, id, state.Request);
        var reply = await scenario.InvokeAsync(state.Reader, id, Guid.Empty, envelope, token);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        Page(state.Request, ConnectionAssertions.Value<SampleChunkWindowResult>(reply));
    }

    internal static async Task RefusedAsync(ConnectionScenario scenario, SampleChunkMinimumEnvelopeState state,
        Guid id, GrainRequestEnvelope envelope, bool outOfScope, CancellationToken token)
    {
        if (outOfScope)
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => scenario.Fixture.Codec.DecodeRead(envelope, id));
            SampleChunkExplicitMinimumUnitReads.RequireBytes(Errors.Problem(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest), failure.ToProblem());
            GrainOperationReply? partial = null;
            var rejected = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial =
                await scenario.InvokeAsync(state.Reader, id, Guid.Empty, envelope, token))
                ?? throw new InvalidOperationException("The out-of-scope envelope did not refuse before routing.");
            SampleChunkExplicitMinimumUnitReads.RequireBytes(Errors.Problem(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest), rejected.ToProblem());
            await Assert.That(partial).IsNull();
            return;
        }
        var reply = await scenario.InvokeAsync(state.Reader, id, Guid.Empty, envelope, token);
        await Assert.That(reply.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(reply.SafeDetail).IsEqualTo(ScopeDenied);
        await Assert.That(reply.Result).IsNull();
    }
}
