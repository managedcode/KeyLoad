namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkTextValidator
{
    internal static void Validate(SampleChunkPayload payload, ReadExecutionBudget budget)
    {
        const long TextBytesInitialValue = 0L;
        const int EmptySeriesBytes = 0;
        const int IndexInitialValue = 0;

        var textBytes = TextBytesInitialValue;
        var series = new SampleChunkReader(payload.Series.Span);
        var seriesBytes = SampleChunkText.ValidateFramed(ref series, budget);
        SampleChunkWire.Require(seriesBytes > EmptySeriesBytes);
        textBytes += seriesBytes;
        series.RequireEnd();

        var events = new SampleChunkReader(payload.EventIds.Span);
        for (var index = IndexInitialValue; index < payload.RecordCount; index++)
        {
            budget.Check();
            var eventBytes = SampleChunkText.ValidateFramed(ref events, budget);
            SampleChunkWire.Require(eventBytes > EmptySeriesBytes);
            textBytes += eventBytes;
        }
        events.RequireEnd();

        var tags = new SampleChunkReader(payload.Tags.Span);
        var dictionaryCount = tags.ReadVarUInt();
        SampleChunkWire.Require(dictionaryCount is > EmptySeriesBytes and <= SampleChunkWire.MaximumRecords
            && dictionaryCount <= (ulong)payload.RecordCount);
        var count = (int)dictionaryCount;
        for (var index = IndexInitialValue; index < count; index++)
        {
            budget.Check();
            var tagBytes = SampleChunkText.ValidateFramed(ref tags, budget);
            SampleChunkWire.Require(tagBytes > EmptySeriesBytes);
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
        const int TagIndexWordCount = 4;
        const int NextFirstIndexInitialValue = 0;
        const int RecordInitialValue = 0;
        const int BitsPerTagWord = 64;
        const ulong FirstTagBit = 1UL;
        const int BitsPerTagWordForMask = 64;
        const int EmptyUsedWordMask = 0;

        Span<ulong> used = stackalloc ulong[TagIndexWordCount];
        used.Clear();
        var nextFirstIndex = NextFirstIndexInitialValue;
        for (var record = RecordInitialValue; record < recordCount; record++)
        {
            budget.Check();
            var indexValue = reader.ReadVarUInt();
            SampleChunkWire.Require(indexValue < (ulong)dictionaryCount);
            var index = (int)indexValue;
            var word = index / BitsPerTagWord;
            var mask = FirstTagBit << (index % BitsPerTagWordForMask);
            if ((used[word] & mask) == EmptyUsedWordMask)
            {
                SampleChunkWire.Require(index == nextFirstIndex++);
                used[word] |= mask;
            }
        }
        SampleChunkWire.Require(nextFirstIndex == dictionaryCount);
    }
}
