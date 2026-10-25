namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class SampleChunkRetainedBudgetExpected
{
    internal static async Task RequireAsync(SampleChunkPartialRetentionFixture fixture,
        SampleChunkWindowResult actual, bool merged, bool fresh)
    {
        long generation = merged ? 2 : 1;
        long revision = merged ? 6 : 5;
        long sequence = 3;
        var late = new SampleRecord(SampleChunkCanonicalFixture.Series, SampleChunkCanonicalFixture.Late,
            SampleChunkPartialRetentionFixture.LateSequence, SampleChunkPartialRetentionFixture.LateTags);
        var rows = new List<SampleRecord> { late };
        if (fresh)
        {
            generation = 3;
            revision = 8;
            sequence = 4;
            rows.Add(new(SampleChunkCanonicalFixture.Series, SampleChunkPartialRetentionFixture.Fresh,
                SampleChunkPartialRetentionFixture.FreshSequence, SampleChunkCanonicalFixture.Tags));
        }
        var expected = new SampleChunkWindowResult(fixture.WindowId, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until, generation, revision, sequence,
            SampleChunkCanonicalFixture.Late.Timestamp.UtcTicks, [.. rows], fixture.Store.Position);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
        await Assert.That(actual.Records.Length).IsEqualTo(rows.Count);
        for (var index = 0; index < rows.Count; index++)
        {
            await Assert.That(BitConverter.DoubleToInt64Bits(actual.Records[index].Sample.Value))
                .IsEqualTo(BitConverter.DoubleToInt64Bits(rows[index].Sample.Value));
        }
    }
}
