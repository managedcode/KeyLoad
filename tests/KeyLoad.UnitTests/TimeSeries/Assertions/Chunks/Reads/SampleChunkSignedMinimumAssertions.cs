using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.OrleansExecution;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkSignedMinimumAssertions
{
    private const long NoCut = 0;
    private const string InvalidMac = "The signed token is invalid.";
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
            throw new InvalidOperationException("The execution signed chunk read has no owner cut.");
        }
        var expected = new SampleChunkWindowResult(request.WindowId, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until, SampleChunkCanonicalFixture.FirstGeneration,
            SampleChunkCanonicalFixture.SealedRevision, SampleChunkCanonicalFixture.InitialSequence, null,
            [.. SampleChunkCanonicalFixture.Expected()], actual.CutPosition);
        SampleChunkExplicitMinimumUnitReads.RequireBytes(expected, actual);
    }

    internal static async Task HealthyAsync(ConnectionScenario scenario, SampleChunkSignedMinimumState state,
        CancellationToken token)
    {
        var id = Guid.NewGuid();
        var signed = SampleChunkSignedMinimumEnvelope.Create(scenario.Fixture.Codec, state.Reader, id, state.Request);
        SampleChunkSignedMinimumEnvelope.Decode(scenario.Fixture.Codec, signed, id, state.Request);
        var reply = await scenario.InvokeAsync(state.Reader, id, Guid.Empty, signed, token);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        Page(state.Request, ConnectionAssertions.Value<SampleChunkWindowResult>(reply));
    }

    internal static async Task RefusedAsync(ConnectionScenario scenario, SampleChunkSignedMinimumState state,
        Guid id, string signed, bool mac, CancellationToken token)
    {
        if (mac)
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => scenario.Fixture.Codec.VerifyRead(signed, id));
            SampleChunkExplicitMinimumUnitReads.RequireBytes(Errors.Problem(ErrorCode.TokenInvalidated, InvalidMac), failure.ToProblem());
            GrainOperationReply? partial = null;
            var rejected = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial =
                await scenario.InvokeAsync(state.Reader, id, Guid.Empty, signed, token))
                ?? throw new InvalidOperationException("The invalid MAC did not refuse before routing.");
            SampleChunkExplicitMinimumUnitReads.RequireBytes(Errors.Problem(ErrorCode.TokenInvalidated, InvalidMac), rejected.ToProblem());
            await Assert.That(partial).IsNull();
            return;
        }
        var reply = await scenario.InvokeAsync(state.Reader, id, Guid.Empty, signed, token);
        await Assert.That(reply.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(reply.SafeDetail).IsEqualTo(ScopeDenied);
        await Assert.That(reply.Payload.IsEmpty).IsTrue();
    }
}
