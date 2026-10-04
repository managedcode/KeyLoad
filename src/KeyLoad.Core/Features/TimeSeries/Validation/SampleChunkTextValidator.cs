namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkTextValidator
{
    internal static void Validate(SampleChunkPayload payload, ReadExecutionBudget budget)
    {
        var textBytes = 0L;
        var series = new SampleChunkReader(payload.Series.Span);
        var seriesBytes = SampleChunkText.ValidateFramed(ref series, budget);
        SampleChunkWire.Require(seriesBytes > 0);
        textBytes += seriesBytes;
        series.RequireEnd();

        var events = new SampleChunkReader(payload.EventIds.Span);
        for (var index = 0; index < payload.RecordCount; index++)
        {
            budget.Check();
            var eventBytes = SampleChunkText.ValidateFramed(ref events, budget);
            SampleChunkWire.Require(eventBytes > 0);
            textBytes += eventBytes;
        }
        events.RequireEnd();

        var tags = new SampleChunkReader(payload.Tags.Span);
        var dictionaryCount = tags.ReadVarUInt();
        SampleChunkWire.Require(dictionaryCount is > 0 and <= SampleChunkWire.MaximumRecords
            && dictionaryCount <= (ulong)payload.RecordCount);
        var count = (int)dictionaryCount;
        for (var index = 0; index < count; index++)
        {
            budget.Check();
            var tagBytes = SampleChunkText.ValidateFramed(ref tags, budget);
            SampleChunkWire.Require(tagBytes > 0);
            textBytes += tagBytes;
        }
        ValidateTagIndexes(ref tags, payload.RecordCount, count, budget);
        tags.RequireEnd();
        SampleChunkWire.Require(textBytes <= SampleChunkWire.MaximumEncodedBytes);
        budget.Check();
    }

    private static void ValidateTagIndexes(ref SampleChunkReader reader, int recordCount, int dictionaryCount,
        ReadExecutionBudget budget)
    {
        Span<ulong> used = stackalloc ulong[4];
        used.Clear();
        var nextFirstIndex = 0;
        for (var record = 0; record < recordCount; record++)
        {
            budget.Check();
            var indexValue = reader.ReadVarUInt();
            SampleChunkWire.Require(indexValue < (ulong)dictionaryCount);
            var index = (int)indexValue;
            var word = index / 64;
            var mask = 1UL << (index % 64);
            if ((used[word] & mask) == 0)
            {
                SampleChunkWire.Require(index == nextFirstIndex++);
                used[word] |= mask;
            }
        }
        SampleChunkWire.Require(nextFirstIndex == dictionaryCount);
    }
}
