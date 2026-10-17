namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkCanonicalAssertions
{
    internal static async Task Literal(SampleChunkCanonicalFixture fixture, long revision, long generation,
        long sequence, bool late = false, long? floor = null)
    {
        var expected = new SampleChunkWindowResult(fixture.WindowId, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until, generation, revision, sequence, floor,
            [.. SampleChunkCanonicalFixture.Expected(late).Where(record => floor is null || record.Sample.Timestamp.UtcTicks >= floor)],
            fixture.Owner.Store.Position);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(fixture.Read())))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
        var raw = fixture.Owner.Database.ReadSamples(SampleChunkCanonicalFixture.Principal,
            fixture.Owner.Partition, SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            SampleChunkCanonicalFixture.Start, SampleChunkCanonicalFixture.Until, SampleChunkCanonicalFixture.OutputLimit);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(raw)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected.Records)));
    }

    internal static async Task Replay(SampleChunkCanonicalFixture fixture, Guid id, Mutation mutation,
        OperationResult original)
    {
        var image = fixture.Image();
        var position = fixture.Owner.Store.Position;
        var actual = fixture.Commit(id, mutation);
        await Assert.That(SampleRollupWholeFlow.Outcome(actual)).IsEqualTo(SampleRollupWholeFlow.Outcome(original));
        await Assert.That(fixture.Image()).IsEqualTo(image);
        await Assert.That(fixture.Owner.Store.Position).IsEqualTo(position);
    }

    internal static async Task ReadDenial(SampleChunkCanonicalFixture fixture, Action read, ErrorCode code)
    {
        var image = fixture.Image();
        var position = fixture.Owner.Store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(read);
        await Assert.That(failure.Code).IsEqualTo(code);
        await Assert.That(fixture.Image()).IsEqualTo(image);
        await Assert.That(fixture.Owner.Store.Position).IsEqualTo(position);
    }
}
