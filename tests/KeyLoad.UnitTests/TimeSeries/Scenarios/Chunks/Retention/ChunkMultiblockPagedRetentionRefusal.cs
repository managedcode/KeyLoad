namespace KeyLoad.UnitTests.TimeSeries;

internal static class ChunkMultiblockPagedRetentionRefusal
{
    private const int FirstIndex = 0;

    internal static async Task RunAsync(SampleChunkPartialRetentionFixture fixture)
    {
        fixture.Commit(new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            SampleChunkCanonicalFixture.Initial, SampleChunkCanonicalFixture.Tags));
        await ChunkMultiblockPagedRetentionRows.RequireAsync(fixture, expired: true, healthy: false);
        var changed = SampleChunkCanonicalFixture.Initial[FirstIndex] with
        {
            Value = SampleChunkPartialRetentionFixture.FreshValue
        };
        var result = fixture.Apply(fixture.Operation(new AppendSamples(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, [changed], SampleChunkCanonicalFixture.Tags)));
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.Conflict);

        await ChunkMultiblockPagedRetentionRows.RequireAsync(fixture, expired: true, healthy: false);
        var cut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var error = Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Read(cancellation.Token));
        await Assert.That(error.CancellationToken).IsEqualTo(cancellation.Token);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, cut);
        await SampleChunkPartialRetentionAssertions.Replay(fixture);
    }
}
